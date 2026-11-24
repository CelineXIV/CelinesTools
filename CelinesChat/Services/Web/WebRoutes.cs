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

internal sealed class WebRoutes : IDisposable
{
    private const string CookieName = "CelinesChat-token";

    // Same limit as the in-game compose box - the text is split into chat-sized messages anyway,
    // this only keeps an absurdly large request from being processed (and split) on the game thread.
    private const int MaxSendTextLength = 4000;

    // Comfortably above any real "Firstname Lastname@World" - anything longer isn't a character name
    // and would only end up persisted in the whisper tab list.
    private const int MaxWhisperTargetLength = 64;

    private static readonly Vector4 LinkColor = new(0.4f, 0.7f, 1f, 1f);

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

        indexHtml = File.ReadAllText(Path.Combine(webRootDir, "index.html"));
        appJs = File.ReadAllText(Path.Combine(webRootDir, "app.js"));
        stylesCss = File.ReadAllText(Path.Combine(webRootDir, "styles.css"));

        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/", ServeIndex, OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/app.js", ServeAppJs, OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/styles.css", ServeStyles, OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.POST, "/api/auth", HandleAuth, OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/api/session", HandleSession, OnException);

        host.Routes.AuthenticateRequest = CheckAuth;

        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/logout", HandleLogout, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/api/events", HandleEvents, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/send", HandleSend, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/view/tab", HandleViewTab, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/view/whisper", HandleViewWhisper, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/view/whisper/close", HandleCloseWhisper, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/channel", HandleChannel, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/api/channels", HandleChannels, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/links/click", HandleLinkClick, OnException);

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
        if (body == null || string.IsNullOrEmpty(body.Code) || !WebAuth.CodeMatches(body.Code, plugin.Configuration.WebClientAuthCode))
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

        ctx.Response.Headers.Add("Set-Cookie", $"{CookieName}={token}; Path=/; HttpOnly; SameSite=Strict; Max-Age=2592000");
        await SendJson(ctx, 200, new { ok = true });
    }

    private async Task HandleSession(HttpContext ctx)
    {
        var authenticated = await IsAuthenticatedAsync(ctx);
        await SendJson(ctx, authenticated ? 200 : 401, new { authenticated });
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

    private async Task CheckAuth(HttpContext ctx)
    {
        if (await IsAuthenticatedAsync(ctx))
        {
            return;
        }

        await SendJson(ctx, 401, new WebErrorResponse("Not authenticated."));
    }

    // Checked on the framework thread - that's the only thread that ever changes
    // WebClientAuthStore (login, logout, code change), and a plain HashSet read racing one of those
    // writes from this HTTP thread can throw or give a wrong answer.
    private async Task<bool> IsAuthenticatedAsync(HttpContext ctx)
    {
        var token = GetCookieToken(ctx);
        if (token == null)
        {
            return false;
        }

        return await plugin.Framework.RunOnFrameworkThread(() => plugin.Configuration.WebClientAuthStore.Contains(token));
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

        if (text.Length > MaxSendTextLength)
        {
            await SendJson(ctx, 400, new WebErrorResponse("Message too long."));
            return;
        }

        var error = await plugin.Framework.RunOnFrameworkThread(() =>
        {
            if (!plugin.IsLoggedIn)
            {
                return "Not logged in.";
            }

            if (text.StartsWith('/'))
            {
                return plugin.SendRawCommand(text) ? null : "Command too long.";
            }

            var state = plugin.GetCharacterState();
            if (state.LastChannel == ChatChannel.Whisper && string.IsNullOrWhiteSpace(state.LastWhisperTarget))
            {
                return "No whisper target selected.";
            }

            var chunks = MessageSplitter.BuildMessages(text, plugin.Configuration.MaxMessageLength, plugin.GetChannelPrefixBytes(state));
            if (chunks.Count > 0)
            {
                plugin.SendChunks(chunks, text);
            }

            return (string?)null;
        });

        if (error != null)
        {
            await SendJson(ctx, 400, new WebErrorResponse(error));
            return;
        }

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
        if (body == null || !IsPlausibleWhisperTarget(body.Target))
        {
            await SendJson(ctx, 400, new WebErrorResponse("Invalid whisper target."));
            return;
        }

        var target = body.Target!.Trim();
        await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var config = plugin.Configuration;
            plugin.ChatWindowInstance.EnterWhisperView(config, plugin.GetCharacterState(), target);
        });

        await SendJson(ctx, 202, new { ok = true });
    }

    private async Task HandleCloseWhisper(HttpContext ctx)
    {
        var body = TryDeserialize<WebWhisperSwitchRequest>(ctx.Request.DataAsString);
        if (body == null || !IsPlausibleWhisperTarget(body.Target))
        {
            await SendJson(ctx, 400, new WebErrorResponse("Invalid whisper target."));
            return;
        }

        var target = body.Target!.Trim();
        await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var config = plugin.Configuration;
            plugin.ChatWindowInstance.CloseWhisperTab(config, plugin.GetCharacterState(), target);
        });

        await SendJson(ctx, 202, new { ok = true });
    }

    private async Task HandleChannel(HttpContext ctx)
    {
        var body = TryDeserialize<WebChannelSwitchRequest>(ctx.Request.DataAsString);

        // IsDefined as well: Enum.TryParse also happily accepts any number ("42") as a channel.
        if (body == null || !Enum.TryParse<ChatChannel>(body.Channel, out var channel) || !Enum.IsDefined(channel)
            || body.Number is < 1 or > 8)
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

    // Fetched by the client on-demand each time the channel-picker sheet opens, not pushed via SSE -
    // linkshell/party membership doesn't change often enough to justify that, and this mirrors how
    // the in-game picker itself just re-queries NativeChannels/GetPartyMemberNames live every single
    // frame it's open (ChatWindow.DrawChannelPickerButton) rather than caching a snapshot.
    private async Task HandleChannels(HttpContext ctx)
    {
        var options = await plugin.Framework.RunOnFrameworkThread(() => new WebChannelOptionsDto
        {
            Linkshells = NativeChannels.GetExistingLinkshells()
                .Select(ls => new WebNumberedChannelDto { Number = ls.Number, Name = ls.Name })
                .ToList(),
            CrossWorldLinkshells = NativeChannels.GetExistingCrossWorldLinkshells()
                .Select(ls => new WebNumberedChannelDto { Number = ls.Number, Name = ls.Name })
                .ToList(),
            PartyMembers = plugin.GetPartyMemberNames(),
        });

        await SendJson(ctx, 200, options);
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
        ctx.Response.ServerSentEvents = true;

        var client = hub.Register();
        try
        {
            var lastEventId = ctx.Request.RetrieveHeaderValue("Last-Event-ID");

            // A resumed connection (Last-Event-ID present) is only catching up on genuinely new
            // traffic since a specific sequence - file-based whisper history has no sequence of its
            // own to resume from, so it's only merged in for a *fresh* connect, same as
            // OnViewChanged below only does it for an actual view switch. BuildHistoryForCurrentViewAsync
            // is called directly here (not from inside RunOnFrameworkThread) so its own file read
            // stays off the framework thread - see its remarks.
            var entries = long.TryParse(lastEventId, out var sinceSeq)
                ? await plugin.Framework.RunOnFrameworkThread(() => FilterToCurrentView(plugin.ChatLog.SnapshotSince(sinceSeq)))
                : await BuildHistoryForCurrentViewAsync();

            var (config, mentionTerm) = await plugin.Framework.RunOnFrameworkThread(() => (plugin.Configuration, plugin.MentionFirstName));

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
        }
        finally
        {
            hub.Unregister(client.Id);

            try
            {
                await ctx.Response.SendChunk(Array.Empty<byte>(), true, CancellationToken.None);
            }
            catch (Exception)
            {
            }
        }
    }

    // FFXIV character names are letters plus ' and -, world names letters - anything else (or a huge
    // string) can't be a real /tell target and would only end up persisted as a whisper tab.
    private static bool IsPlausibleWhisperTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var trimmed = target.Trim();
        if (trimmed.Length > MaxWhisperTargetLength || trimmed.Count(c => c == '@') > 1)
        {
            return false;
        }

        return trimmed.All(c => char.IsLetter(c) || c is ' ' or '\'' or '-' or '@');
    }

    private void OnEntryAdded(ChatLogEntry entry)
    {
        // Runs on the game thread for every incoming chat message while the web client is enabled -
        // building and serializing the message is wasted work when no browser is connected at all.
        if (hub.ConnectedCount == 0 || !MatchesCurrentView(entry))
        {
            return;
        }

        var dto = ToDto(entry, plugin.Configuration, plugin.MentionFirstName);
        hub.Broadcast(BuildFrame("message", entry.Sequence, dto));
    }

    // async void is deliberate here, not sloppy - ViewChanged is a plain Action event, so this
    // handler can't return a Task, and the only way to actually keep BuildHistoryForCurrentViewAsync's
    // file read off the framework thread (see its own remarks) is to genuinely await it rather than
    // blocking synchronously on .Result/.Wait() from inside a handler that itself already runs on
    // that same thread (SwitchToFixedTab/EnterWhisperView/SelectChannel all raise ViewChanged
    // directly, whether triggered by an in-game click or by a web request already marshalled there).
    private async void OnViewChanged()
    {
        // Every in-game tab switch lands here - with nobody connected there's no one to send the
        // view or its history (which for a whisper tab means reading its whole log file) to.
        if (hub.ConnectedCount == 0)
        {
            return;
        }

        // An exception escaping an async void method terminates the whole process (the game) - e.g.
        // the framework going away mid-await while the plugin unloads.
        try
        {
            hub.Broadcast(BuildFrame("view", null, BuildViewDto()));

            var config = plugin.Configuration;
            var mentionTerm = plugin.MentionFirstName;
            var history = (await BuildHistoryForCurrentViewAsync())
                .Select(entry => ToDto(entry, config, mentionTerm))
                .ToList();
            hub.Broadcast(BuildFrame("history", null, history));
        }
        catch (Exception ex)
        {
            plugin.Log.Warning(ex, "[CelinesChat] Web-Client: Ansicht konnte nicht aktualisiert werden.");
        }
    }

    // The live in-memory buffer alone (what FilterToCurrentView/Tail draw from) only covers this
    // session's traffic - a whisper tab's *older* history lives in its own per-target log file and
    // is normally merged in by ChatWindow.PreloadWhisperHistoryForFirstView, which the web client
    // never went through at all, so whisper history simply never showed up here. Mirrors that same
    // load-dedupe-merge shape (LoadWhisperHistory, a timestamp-truncated identity check against
    // what's already live, then a merge-sort of two individually-sorted lists) rather than reusing
    // ChatWindow's own private version, since triggering it here would also need to reproduce its
    // "only once per session per target" bookkeeping and its own negative synthetic Sequence
    // assignment (irrelevant for a one-shot HTTP/SSE response, unlike the always-open ImGui window).
    //
    // LoadWhisperHistory reads and parses an entire per-target log file synchronously - for a long-
    // running conversation that file can grow large enough to produce a real, reported 400-500ms
    // "[HITCH] Long UiBuilder(CelinesChat)" if done inline on the framework thread (see ChatWindow.
    // pendingWhisperHistoryLoads' matching remarks for the equivalent in-game-side fix). Everything
    // that touches game/plugin state is still wrapped in RunOnFrameworkThread as usual; only the
    // file read itself runs on a plain background Task instead.
    private async Task<List<ChatLogEntry>> BuildHistoryForCurrentViewAsync()
    {
        var (liveTail, whisperTarget) = await plugin.Framework.RunOnFrameworkThread(() =>
            (FilterToCurrentView(Tail(plugin.ChatLog.Snapshot(), 200)), plugin.ChatWindowInstance.ActiveWhisperTabTarget));

        if (string.IsNullOrEmpty(whisperTarget))
        {
            return liveTail;
        }

        var fileEntries = await Task.Run(() => plugin.ChatLog.LoadWhisperHistory(whisperTarget));
        if (fileEntries.Count == 0)
        {
            return liveTail;
        }

        var alreadyLive = await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var set = new HashSet<(XivChatType, string, string?, string, DateTime)>();
            foreach (var live in plugin.ChatLog.Snapshot())
            {
                set.Add((live.ChatType, live.Sender, live.SenderWorld, live.Text, TruncateToSeconds(live.Timestamp)));
            }

            return set;
        });

        var deduped = fileEntries
            .Where(f => !alreadyLive.Contains((f.ChatType, f.Sender, f.SenderWorld, f.Text, TruncateToSeconds(f.Timestamp))))
            .ToList();

        var merged = MergeByTimestamp(deduped, liveTail).ToList();
        return merged.Count > 200 ? merged.GetRange(merged.Count - 200, 200) : merged;
    }

    private static DateTime TruncateToSeconds(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Kind);

    private static IEnumerable<ChatLogEntry> MergeByTimestamp(IReadOnlyList<ChatLogEntry> a, IReadOnlyList<ChatLogEntry> b)
    {
        var i = 0;
        var j = 0;
        while (i < a.Count && j < b.Count)
        {
            yield return a[i].Timestamp <= b[j].Timestamp ? a[i++] : b[j++];
        }

        while (i < a.Count)
        {
            yield return a[i++];
        }

        while (j < b.Count)
        {
            yield return b[j++];
        }
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
            LinkshellNumber = state.LinkshellNumber,
            CrossWorldLinkshellNumber = state.CrossWorldLinkshellNumber,
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

        var allowRpFormatting = config.AllowsRpFormatting(entry.ChatType);
        var segments = entry.Payloads != null
            ? ColoredTextSegmenter.BuildSegments(entry.Payloads, mentionTerm, channelColor, config.EmoteTextColor, config.OocTextColor, config.MentionColor, LinkColor, allowRpFormatting)
            : ColoredTextSegmenter.BuildSegments(entry.Text, mentionTerm, channelColor, config.EmoteTextColor, config.OocTextColor, config.MentionColor, allowRpFormatting);

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
        _ = ex;
    }
}
