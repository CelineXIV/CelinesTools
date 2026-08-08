using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Channels;
using Dalamud.Game.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using CelinesChat.Services.Web.Http;
using CelinesChat.Windows;
using HttpMethod = CelinesChat.Services.Web.Http.HttpMethod;

namespace CelinesChat.Services.Web;

/// <summary>
/// The actual HTTP route handlers - registered against a MiniHttpServer by WebServerService, which
/// owns the server's start/stop lifecycle. Every handler that reads/writes Configuration or calls
/// into ChatWindow/game APIs marshals onto the framework thread first (see Plugin.Framework) -
/// this class's own methods otherwise run on arbitrary MiniHttpServer connection threads.
/// </summary>
internal sealed class WebRoutes : IDisposable
{
    private const string CookieName = "CelinesChat-token";

    // Matches ChatWindow's own hardcoded LinkColor field (kept private there, this is a small
    // enough constant that duplicating it here beats exposing a whole UI-only field just for it -
    // if that ever changes, update this too).
    private static readonly Vector4 LinkColor = new(0.4f, 0.7f, 1f, 1f);

    // Newtonsoft.Json's default output preserves C# PascalCase property names as-is (e.g.
    // "ChannelColorCss"), but app.js reads every field in idiomatic camelCase ("channelColorCss")
    // - confirmed as a real, live mismatch by curl-testing the running server directly. Every
    // outgoing JSON payload below goes through this so the two sides actually agree, without
    // having to rename every C# DTO property to break normal .NET convention instead.
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
    };

    private readonly Plugin plugin;
    private readonly WebSseHub hub;
    private readonly string indexHtml;
    private readonly string appJs;
    private readonly string stylesCss;

    public WebRoutes(Plugin plugin, WebSseHub hub, MiniHttpServer host, string webRootDir)
    {
        this.plugin = plugin;
        this.hub = hub;

        // Read once at construction (i.e. once per server start) rather than per-request - a
        // plugin reload/rebuild already picks up new file contents naturally, and re-reading
        // three small files on every single page load would be pure waste.
        indexHtml = File.ReadAllText(Path.Combine(webRootDir, "index.html"));
        appJs = File.ReadAllText(Path.Combine(webRootDir, "app.js"));
        stylesCss = File.ReadAllText(Path.Combine(webRootDir, "styles.css"));

        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/", ServeIndex, OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/app.js", ServeAppJs, OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/styles.css", ServeStyles, OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.POST, "/api/auth", HandleAuth, OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/api/session", HandleSession, OnException);

        // Everything below only runs once CheckAuth lets it through - see its own remarks.
        host.Routes.AuthenticateRequest = CheckAuth;

        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/logout", HandleLogout, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/api/events", HandleEvents, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/send", HandleSend, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/view/tab", HandleViewTab, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/view/whisper", HandleViewWhisper, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/channel", HandleChannel, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/links/click", HandleLinkClick, OnException);

        // Live push for new messages matching whatever the shared view currently is, and for
        // view changes themselves (a desktop tab click has to reach connected browsers exactly
        // the same way a web-initiated one does - see ChatWindow.ViewChanged's remarks).
        plugin.ChatLog.EntryAdded += OnEntryAdded;
        plugin.ChatWindowInstance.ViewChanged += OnViewChanged;
    }

    public void Dispose()
    {
        plugin.ChatLog.EntryAdded -= OnEntryAdded;
        plugin.ChatWindowInstance.ViewChanged -= OnViewChanged;
    }

    #region Static files

    private async Task ServeIndex(HttpContext ctx) => await SendText(ctx, indexHtml, "text/html; charset=utf-8");

    private async Task ServeAppJs(HttpContext ctx) => await SendText(ctx, appJs, "text/javascript; charset=utf-8");

    private async Task ServeStyles(HttpContext ctx) => await SendText(ctx, stylesCss, "text/css; charset=utf-8");

    private static async Task SendText(HttpContext ctx, string body, string contentType)
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = contentType;
        // Never cached - an updated plugin version shipping a new page would otherwise risk a
        // browser (especially a phone's, which tend to cache far more aggressively) silently
        // keeping stale HTML/JS/CSS around indefinitely.
        ctx.Response.Headers.Add("Cache-Control", "no-store");
        await ctx.Response.Send(body);
    }

    #endregion

    #region Auth

    private async Task HandleAuth(HttpContext ctx)
    {
        var ip = ctx.Request.Source.IpAddress;
        if (WebAuth.IsRateLimited(ip))
        {
            await SendJson(ctx, 429, new WebErrorResponse("Rate limit active - please wait a few seconds."));
            return;
        }

        WebAuth.MarkAttempt(ip);

        var body = TryDeserialize<WebAuthRequest>(ctx.Request.DataAsString);
        if (body == null || body.Code != plugin.Configuration.WebClientAuthCode || string.IsNullOrEmpty(body.Code))
        {
            await SendJson(ctx, 401, new WebErrorResponse("Invalid code."));
            return;
        }

        var token = WebAuth.GenerateSessionToken();
        await plugin.Framework.RunOnFrameworkThread(() =>
        {
            plugin.Configuration.WebClientAuthStore.Add(token);
            plugin.SaveConfiguration();
        });

        // HttpOnly (JS never needs to read it) + SameSite=Strict (single-origin LAN tool, no
        // cross-site requests are ever legitimate here, so this alone is sufficient CSRF
        // protection without needing separate CSRF tokens) - no Secure flag, since this is
        // deliberately plain HTTP (LAN-only by design, see WebClientPage's warning text).
        ctx.Response.Headers.Add("Set-Cookie", $"{CookieName}={token}; Path=/; HttpOnly; SameSite=Strict; Max-Age=2592000");
        await SendJson(ctx, 200, new { ok = true });
    }

    private async Task HandleSession(HttpContext ctx)
    {
        await SendJson(ctx, IsAuthenticated(ctx) ? 200 : 401, new { authenticated = IsAuthenticated(ctx) });
    }

    private async Task HandleLogout(HttpContext ctx)
    {
        var token = GetCookieToken(ctx);
        if (token != null)
        {
            await plugin.Framework.RunOnFrameworkThread(() =>
            {
                plugin.Configuration.WebClientAuthStore.Remove(token);
                plugin.SaveConfiguration();
            });
        }

        ctx.Response.Headers.Add("Set-Cookie", $"{CookieName}=; Path=/; Max-Age=0");
        await SendJson(ctx, 200, new { ok = true });
    }

    /// <summary>
    /// MiniHttpServer's global pre-route gate (see WebServerService) - sends a 401 and returns without
    /// calling through to the actual route whenever the session cookie is missing/invalid,
    /// mirroring Chat2's own confirmed-working AuthenticateRequest pattern (they redirect an HTML
    /// page instead of returning JSON, since their post-auth routes are pages; every one of ours
    /// is a JSON API, so a plain 401 is the right shape here instead).
    /// </summary>
    private async Task CheckAuth(HttpContext ctx)
    {
        if (IsAuthenticated(ctx))
        {
            return;
        }

        await SendJson(ctx, 401, new WebErrorResponse("Not authenticated."));
    }

    private bool IsAuthenticated(HttpContext ctx)
    {
        var token = GetCookieToken(ctx);
        return token != null && plugin.Configuration.WebClientAuthStore.Contains(token);
    }

    private static string? GetCookieToken(HttpContext ctx)
    {
        var header = ctx.Request.RetrieveHeaderValue("Cookie");
        if (string.IsNullOrEmpty(header))
        {
            return null;
        }

        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq] == CookieName)
            {
                return part[(eq + 1)..];
            }
        }

        return null;
    }

    #endregion

    #region Actions

    private async Task HandleSend(HttpContext ctx)
    {
        var body = TryDeserialize<WebSendRequest>(ctx.Request.DataAsString);
        var text = body?.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            await SendJson(ctx, 400, new WebErrorResponse("Empty message."));
            return;
        }

        // Fire-and-forget onto the framework thread, same as the desktop compose box's own send
        // path - the HTTP response doesn't wait for the actual game-thread send to complete.
        _ = plugin.Framework.RunOnFrameworkThread(() =>
        {
            // Mirrors ChatWindow.TrySend: a message starting with "/" is a raw game command
            // (typed verbatim, not wrapped in a channel prefix or run through the splitter).
            if (text.StartsWith('/'))
            {
                plugin.SendRawCommand(text);
                return;
            }

            var state = plugin.GetCharacterState();
            if (state.LastChannel == ChatChannel.Whisper && string.IsNullOrWhiteSpace(state.LastWhisperTarget))
            {
                return;
            }

            var chunks = MessageSplitter.BuildMessages(text, plugin.Configuration.MaxMessageLength);
            if (chunks.Count > 0)
            {
                plugin.SendChunks(chunks, text);
            }
        });

        await SendJson(ctx, 202, new { ok = true });
    }

    private async Task HandleViewTab(HttpContext ctx)
    {
        var body = TryDeserialize<WebTabSwitchRequest>(ctx.Request.DataAsString);
        if (body == null || !Guid.TryParse(body.TabId, out var tabId))
        {
            await SendJson(ctx, 400, new WebErrorResponse("Invalid tab id."));
            return;
        }

        await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var config = plugin.Configuration;
            var tab = config.ChatTabs.Find(t => t.Id == tabId);
            if (tab != null)
            {
                plugin.ChatWindowInstance.SwitchToFixedTab(config, plugin.GetCharacterState(), tab);
            }
        });

        await SendJson(ctx, 202, new { ok = true });
    }

    private async Task HandleViewWhisper(HttpContext ctx)
    {
        var body = TryDeserialize<WebWhisperSwitchRequest>(ctx.Request.DataAsString);
        if (body == null || string.IsNullOrWhiteSpace(body.Target))
        {
            await SendJson(ctx, 400, new WebErrorResponse("Invalid whisper target."));
            return;
        }

        await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var config = plugin.Configuration;
            plugin.ChatWindowInstance.EnterWhisperView(config, plugin.GetCharacterState(), body.Target);
        });

        await SendJson(ctx, 202, new { ok = true });
    }

    private async Task HandleChannel(HttpContext ctx)
    {
        var body = TryDeserialize<WebChannelSwitchRequest>(ctx.Request.DataAsString);
        if (body == null || !Enum.TryParse<ChatChannel>(body.Channel, out var channel))
        {
            await SendJson(ctx, 400, new WebErrorResponse("Invalid channel."));
            return;
        }

        await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var config = plugin.Configuration;
            var state = plugin.GetCharacterState();
            if (body.Number is { } number)
            {
                if (channel == ChatChannel.Linkshell)
                {
                    state.LinkshellNumber = number;
                }
                else if (channel == ChatChannel.CrossWorldLinkshell)
                {
                    state.CrossWorldLinkshellNumber = number;
                }
            }

            plugin.ChatWindowInstance.SelectChannel(config, state, channel);
        });

        await SendJson(ctx, 202, new { ok = true });
    }

    private async Task HandleLinkClick(HttpContext ctx)
    {
        var body = TryDeserialize<WebLinkClickRequest>(ctx.Request.DataAsString);
        if (body == null)
        {
            await SendJson(ctx, 400, new WebErrorResponse("Invalid request."));
            return;
        }

        await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var entry = plugin.ChatLog.FindBySequence(body.Sequence);
            if (entry?.Payloads == null || body.LinkIndex < 0 || body.LinkIndex >= entry.Payloads.Count)
            {
                return;
            }

            plugin.HandleChatLinkClicked(entry.Payloads[body.LinkIndex], entry.Payloads);
        });

        await SendJson(ctx, 202, new { ok = true });
    }

    #endregion

    #region SSE

    private async Task HandleEvents(HttpContext ctx)
    {
        ctx.Response.StatusCode = 200;
        // Frames are built by hand (see BuildFrame) rather than through some higher-level "send an
        // SSE event" helper, specifically so an "id:" field can be included on every frame - the
        // browser's EventSource needs that for Last-Event-ID resumption to work at all.
        ctx.Response.ServerSentEvents = true;

        var client = hub.Register();
        try
        {
            var lastEventId = ctx.Request.RetrieveHeaderValue("Last-Event-ID");
            var (entries, config, mentionTerm) = await plugin.Framework.RunOnFrameworkThread(() =>
            {
                var snapshot = long.TryParse(lastEventId, out var sinceSeq)
                    ? plugin.ChatLog.SnapshotSince(sinceSeq)
                    : Tail(plugin.ChatLog.Snapshot(), 200);
                return (FilterToCurrentView(snapshot), plugin.Configuration, plugin.MentionFirstName);
            });

            foreach (var entry in entries)
            {
                var frame = BuildFrame("message", entry.Sequence, ToDto(entry, config, mentionTerm));
                if (!await ctx.Response.SendChunk(Encoding.UTF8.GetBytes(frame), false, ctx.Token))
                {
                    return;
                }
            }

            var initialView = await plugin.Framework.RunOnFrameworkThread(BuildViewDto);
            if (!await ctx.Response.SendChunk(Encoding.UTF8.GetBytes(BuildFrame("view", null, initialView)), false, ctx.Token))
            {
                return;
            }

            await foreach (var queued in client.Outbound.Reader.ReadAllAsync(ctx.Token))
            {
                if (!await ctx.Response.SendChunk(Encoding.UTF8.GetBytes(queued), false, ctx.Token))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected (or the plugin/server is shutting down) - not an error.
        }
        finally
        {
            hub.Unregister(client.Id);

            // MiniHttpServer's per-connection loop sends its own 500 fallback if a route returns
            // without ever marking the response sent - and SendChunk only sets that flag when
            // isFinal is true, which none of the streaming sends above ever pass (they can't -
            // there's no "final" message until the connection is actually ending). This closes
            // the chunked response out properly right as the handler is about to return, however
            // it got here (client disconnect, a failed send, server shutdown), so that fallback
            // never fires on top of an already-streaming connection. ctx.Token is already
            // cancelled in the disconnect case, so CancellationToken.None is used here deliberately
            // - if the socket's still there, this gives it one last real chance to send instead of
            // failing immediately on an already-cancelled token.
            try
            {
                await ctx.Response.SendChunk(Array.Empty<byte>(), true, CancellationToken.None);
            }
            catch (Exception)
            {
                // The connection is already gone in most disconnect cases - nothing more to do.
            }
        }
    }

    /// <summary>Called on the framework thread (ChatLogService.EntryAdded always fires there) - safe to read Configuration/ChatWindow state directly.</summary>
    private void OnEntryAdded(ChatLogEntry entry)
    {
        if (!MatchesCurrentView(entry))
        {
            return;
        }

        var dto = ToDto(entry, plugin.Configuration, plugin.MentionFirstName);
        hub.Broadcast(BuildFrame("message", entry.Sequence, dto));
    }

    /// <summary>
    /// ChatWindow.ViewChanged fires synchronously from wherever SelectChannel/SwitchToFixedTab/
    /// EnterWhisperView was called - for a web-initiated switch that's already the framework
    /// thread (see the Handle* methods above); for a desktop tab click it's ChatWindow's own
    /// Draw(), also the framework thread. Either way, safe to read state directly here.
    /// </summary>
    private void OnViewChanged()
    {
        hub.Broadcast(BuildFrame("view", null, BuildViewDto()));
    }

    private bool MatchesCurrentView(ChatLogEntry entry)
    {
        var chatWindow = plugin.ChatWindowInstance;
        if (!string.IsNullOrEmpty(chatWindow.ActiveWhisperTabTarget))
        {
            return chatWindow.MatchesWhisperTarget(entry, chatWindow.ActiveWhisperTabTarget);
        }

        var tab = chatWindow.GetActiveFixedTab(plugin.Configuration);
        return tab != null && tab.Matches(entry.ChatType);
    }

    private List<ChatLogEntry> FilterToCurrentView(List<ChatLogEntry> entries)
    {
        return entries.Where(MatchesCurrentView).ToList();
    }

    private WebViewDto BuildViewDto()
    {
        var config = plugin.Configuration;
        var chatWindow = plugin.ChatWindowInstance;
        var state = plugin.GetCharacterState();

        return new WebViewDto
        {
            Tabs = config.ChatTabs.Select(t => new WebTabDto { Id = t.Id.ToString(), Name = t.Name }).ToList(),
            Whispers = state.RecentWhisperTargets.Select(t => new WebWhisperDto { Target = t, DisplayName = ChatWindow.FirstName(t) }).ToList(),
            ActiveTabId = string.IsNullOrEmpty(chatWindow.ActiveWhisperTabTarget) ? chatWindow.ActiveTabId.ToString() : null,
            ActiveWhisperTarget = chatWindow.ActiveWhisperTabTarget,
            Channel = state.LastChannel.ToString(),
        };
    }

    private static List<ChatLogEntry> Tail(List<ChatLogEntry> entries, int count)
    {
        return entries.Count > count ? entries.GetRange(entries.Count - count, count) : entries;
    }

    private static string BuildFrame(string eventType, long? id, object payload)
    {
        var sb = new StringBuilder();
        if (id.HasValue)
        {
            sb.Append("id: ").Append(id.Value).Append('\n');
        }

        sb.Append("event: ").Append(eventType).Append('\n');
        sb.Append("data: ").Append(JsonConvert.SerializeObject(payload, JsonSettings)).Append("\n\n");
        return sb.ToString();
    }

    #endregion

    #region Mapping

    private static WebMessageDto ToDto(ChatLogEntry entry, Configuration config, string mentionTerm)
    {
        var isOutgoingTell = entry.ChatType == XivChatType.TellOutgoing;
        var displayName = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
        var channelColor = ChannelDisplay.Color(entry.ChatType, config);

        var senderColor = isOutgoingTell
            ? config.SendAccentColor
            : entry.ChatType == XivChatType.TellIncoming
                ? ChannelDisplay.WhisperColor(displayName, config)
                : channelColor;

        var segments = entry.Payloads != null
            ? ColoredTextSegmenter.BuildSegments(entry.Payloads, mentionTerm, channelColor, config.EmoteTextColor, config.OocTextColor, config.MentionColor, LinkColor)
            : ColoredTextSegmenter.BuildSegments(entry.Text, mentionTerm, channelColor, config.EmoteTextColor, config.OocTextColor, config.MentionColor);

        return new WebMessageDto
        {
            Sequence = entry.Sequence,
            Sender = displayName,
            SenderColorCss = ToCssColor(senderColor),
            ChannelTag = ChannelDisplay.DisplayTag(entry.ChatType),
            ChannelColorCss = ToCssColor(channelColor),
            Segments = segments,
            Time = entry.Timestamp.ToString("HH:mm"),
        };
    }

    private static string ToCssColor(Vector4 color)
    {
        var r = (int)(Math.Clamp(color.X, 0f, 1f) * 255f);
        var g = (int)(Math.Clamp(color.Y, 0f, 1f) * 255f);
        var b = (int)(Math.Clamp(color.Z, 0f, 1f) * 255f);
        var a = Math.Clamp(color.W, 0f, 1f);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "rgba({0},{1},{2},{3:0.00})", r, g, b, a);
    }

    #endregion

    private static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task SendJson(HttpContext ctx, int statusCode, object payload)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.Send(JsonConvert.SerializeObject(payload, JsonSettings));
    }

    private static async Task OnException(HttpContext ctx, Exception ex)
    {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.Send(JsonConvert.SerializeObject(new WebErrorResponse("Internal server error."), JsonSettings));
        // Swallow ex here deliberately - Plugin.Log isn't reachable statically, and
        // WebServerService's own Events.ExceptionEncountered hook (registered separately) already
        // logs every server-side exception through Dalamud's logger.
        _ = ex;
    }
}
