using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Channels;
using Dalamud.Game.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using C4TChat.Services.Web.Http;
using C4TChat.Windows;
using HttpMethod = C4TChat.Services.Web.Http.HttpMethod;

namespace C4TChat.Services.Web;

internal sealed class WebRoutes : IDisposable
{
    private const string CookieName = "CelinesChat-token";

    private const int MaxSendTextLength = 4000;

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

    private readonly byte[]? logoBlue;
    private readonly byte[]? logoPurple;
    private readonly byte[]? rajdhaniFont;

    private const int MaxWebSessions = 20;

    private static readonly TimeSpan SessionTouchInterval = TimeSpan.FromHours(1);

    public WebRoutes(Plugin plugin, WebSseHub hub, MiniHttpServer host, string webRootDir)
    {
        this.plugin = plugin;
        this.hub = hub;

        indexHtml = File.ReadAllText(Path.Combine(webRootDir, "index.html"));
        appJs = File.ReadAllText(Path.Combine(webRootDir, "app.js"));
        stylesCss = File.ReadAllText(Path.Combine(webRootDir, "styles.css"));

        var pluginDir = Path.GetDirectoryName(webRootDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? webRootDir;
        logoBlue = TryReadBytes(Path.Combine(pluginDir, "Assets", "c4t-logo-blau.png"));
        logoPurple = TryReadBytes(Path.Combine(pluginDir, "Assets", "c4t-logo-lila.png"));
        rajdhaniFont = TryReadBytes(Path.Combine(pluginDir, "Fonts", "Rajdhani-SemiBold.ttf"));

        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/", ServeIndex, OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/app.js", ServeAppJs, OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/styles.css", ServeStyles, OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/c4t-logo-blau.png", ctx => SendBinary(ctx, logoBlue, "image/png"), OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/c4t-logo-lila.png", ctx => SendBinary(ctx, logoPurple, "image/png"), OnException);
        host.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/fonts/rajdhani.ttf", ctx => SendBinary(ctx, rajdhaniFont, "font/ttf"), OnException);
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
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/bookmark", HandleBookmarkToggle, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/api/bookmarks", HandleBookmarkList, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/bookmark/remove", HandleBookmarkRemove, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/reply", HandleReply, OnException);
        host.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/api/history/older", HandleOlderHistory, OnException);

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

    private static byte[]? TryReadBytes(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task SendBinary(HttpContext ctx, byte[]? bytes, string contentType)
    {
        if (bytes == null)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.Send("Not found");
            return;
        }

        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = contentType;
        ctx.Response.Headers.Add("Cache-Control", "public, max-age=86400");
        await ctx.Response.SendBytes(bytes);
    }

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
            var sessions = plugin.Configuration.WebClientSessions;
            PruneSessions(sessions);
            while (sessions.Count >= MaxWebSessions)
            {
                var oldest = string.Empty;
                var oldestTime = DateTime.MaxValue;
                foreach (var (key, lastUsed) in sessions)
                {
                    if (lastUsed < oldestTime)
                    {
                        oldest = key;
                        oldestTime = lastUsed;
                    }
                }

                sessions.Remove(oldest);
            }

            sessions[token] = DateTime.UtcNow;
            plugin.SaveConfiguration();
        });

        var maxAgeSeconds = (long)TimeSpan.FromDays(Math.Max(1, plugin.Configuration.WebClientSessionDays)).TotalSeconds;
        ctx.Response.Headers.Add("Set-Cookie", $"{CookieName}={token}; Path=/; HttpOnly; SameSite=Strict; Max-Age={maxAgeSeconds}");
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
                plugin.Configuration.WebClientSessions.Remove(token);
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

    private async Task<bool> IsAuthenticatedAsync(HttpContext ctx)
    {
        var token = GetCookieToken(ctx);
        if (token == null)
        {
            return false;
        }

        return await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var sessions = plugin.Configuration.WebClientSessions;
            if (!sessions.TryGetValue(token, out var lastUsed))
            {
                return false;
            }

            var now = DateTime.UtcNow;
            if (now - lastUsed > SessionLifetime)
            {
                sessions.Remove(token);
                plugin.SaveConfiguration();
                return false;
            }

            if (now - lastUsed > SessionTouchInterval)
            {
                sessions[token] = now;
                plugin.SaveConfiguration();
            }

            return true;
        });
    }

    private TimeSpan SessionLifetime => TimeSpan.FromDays(Math.Max(1, plugin.Configuration.WebClientSessionDays));

    private void PruneSessions(Dictionary<string, DateTime> sessions)
    {
        var now = DateTime.UtcNow;
        var expired = new List<string>();
        foreach (var (key, lastUsed) in sessions)
        {
            if (now - lastUsed > SessionLifetime)
            {
                expired.Add(key);
            }
        }

        foreach (var key in expired)
        {
            sessions.Remove(key);
        }
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

    private async Task HandleBookmarkToggle(HttpContext ctx)
    {
        var body = TryDeserialize<WebBookmarkRequest>(ctx.Request.DataAsString);
        if (body == null)
        {
            await SendJson(ctx, 400, new WebErrorResponse("Invalid request."));
            return;
        }

        var result = await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var entry = plugin.ChatLog.FindBySequence(body.Sequence);
            if (entry == null)
            {
                return (bool?)null;
            }

            plugin.ToggleBookmark(entry);
            return plugin.IsBookmarked(entry);
        });

        if (result == null)
        {
            await SendJson(ctx, 404, new WebErrorResponse("This message is no longer in memory."));
            return;
        }

        await SendJson(ctx, 200, new { bookmarked = result.Value });
    }

    private async Task HandleBookmarkList(HttpContext ctx)
    {
        var list = await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var config = plugin.Configuration;
            var result = new List<WebBookmarkDto>(config.Bookmarks.Count);
            for (var i = config.Bookmarks.Count - 1; i >= 0; i--)
            {
                var b = config.Bookmarks[i];
                result.Add(new WebBookmarkDto
                {
                    Key = b.Key,
                    Time = b.Timestamp.ToString("dd.MM.yyyy HH:mm"),
                    ChannelTag = ChannelDisplay.DisplayTag(b.ChatType),
                    ChannelColorCss = ToCssColor(ChannelDisplay.Color(b.ChatType, config)),
                    Sender = b.SenderWorld != null ? $"{b.Sender}@{b.SenderWorld}" : b.Sender,
                    Text = b.Text,
                });
            }

            return result;
        });

        await SendJson(ctx, 200, list);
    }

    private async Task HandleBookmarkRemove(HttpContext ctx)
    {
        var body = TryDeserialize<WebBookmarkRemoveRequest>(ctx.Request.DataAsString);
        if (body == null || string.IsNullOrEmpty(body.Key))
        {
            await SendJson(ctx, 400, new WebErrorResponse("Invalid request."));
            return;
        }

        await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var bookmark = plugin.Configuration.Bookmarks.Find(b => b.Key == body.Key);
            if (bookmark != null)
            {
                plugin.RemoveBookmark(bookmark);
            }
        });

        await SendJson(ctx, 200, new { ok = true });
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

            var resuming = long.TryParse(lastEventId, out var sinceSeq);
            var entries = resuming
                ? await plugin.Framework.RunOnFrameworkThread(() => FilterToCurrentView(plugin.ChatLog.SnapshotSince(sinceSeq)))
                : await BuildHistoryForCurrentViewAsync();

            var dtos = await plugin.Framework.RunOnFrameworkThread(() =>
            {
                var config = plugin.Configuration;
                var mentionTerm = plugin.MentionTerms;
                return entries.Select(entry => ToDto(entry, config, mentionTerm)).ToList();
            });

            if (resuming)
            {
                foreach (var dto in dtos)
                {
                    var frame = BuildFrame("message", dto.Sequence, dto);
                    if (!await ctx.Response.SendChunk(Encoding.UTF8.GetBytes(frame), false, ctx.Token))
                    {
                        return;
                    }
                }
            }
            else
            {
                var lastLive = dtos.Count == 0 ? (long?)null : dtos.Max(d => d.Sequence);
                var frame = BuildFrame("history", lastLive is > 0 ? lastLive : null, dtos);
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
        if (hub.ConnectedCount == 0)
        {
            return;
        }

        var inView = MatchesCurrentView(entry);

        if (entry.NotifyKind != 0)
        {
            hub.Broadcast(BuildFrame("notify", null, new { kind = entry.NotifyKind == 1 ? "whisper" : "mention", sender = entry.Sender, inView }));
        }

        if (!inView)
        {
            return;
        }

        var dto = ToDto(entry, plugin.Configuration, plugin.MentionTerms);
        hub.Broadcast(BuildFrame("message", entry.Sequence, dto));
    }

    private async void OnViewChanged()
    {
        if (hub.ConnectedCount == 0)
        {
            return;
        }

        try
        {
            hub.Broadcast(BuildFrame("view", null, BuildViewDto()));

            var historyEntries = await BuildHistoryForCurrentViewAsync();
            var history = await plugin.Framework.RunOnFrameworkThread(() =>
            {
                var config = plugin.Configuration;
                var mentionTerm = plugin.MentionTerms;
                return historyEntries.Select(entry => ToDto(entry, config, mentionTerm)).ToList();
            });
            hub.Broadcast(BuildFrame("history", null, history));
        }
        catch (Exception ex)
        {
            plugin.Log.Warning(ex, "[C4TChat] Web-Client: Ansicht konnte nicht aktualisiert werden.");
        }
    }

    internal const int HistoryPageSize = 80;

    private async Task<List<ChatLogEntry>> BuildHistoryForCurrentViewAsync()
    {
        var all = await BuildFullHistoryForCurrentViewAsync(HistoryPageSize);
        return Tail(all, HistoryPageSize);
    }

    private async Task<List<ChatLogEntry>> BuildFullHistoryForCurrentViewAsync(int liveLimit)
    {
        var (liveTail, whisperTarget) = await plugin.Framework.RunOnFrameworkThread(() =>
            (Tail(FilterToCurrentView(plugin.ChatLog.Snapshot()), liveLimit), plugin.ChatWindowInstance.ActiveWhisperTabTarget));

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

        return MergeByTimestamp(deduped, liveTail).ToList();
    }

    private async Task HandleOlderHistory(HttpContext ctx)
    {
        var body = TryDeserialize<WebOlderHistoryRequest>(ctx.Request.DataAsString);
        if (body == null || body.Skip < 0)
        {
            await SendJson(ctx, 400, new WebErrorResponse("Invalid request."));
            return;
        }

        var count = Math.Clamp(body.Count, 1, 200);
        var all = await BuildFullHistoryForCurrentViewAsync(int.MaxValue);
        var end = Math.Max(0, all.Count - body.Skip);
        var start = Math.Max(0, end - count);
        var page = all.GetRange(start, end - start);

        var dtos = await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var config = plugin.Configuration;
            var mentionTerm = plugin.MentionTerms;
            return page.Select(entry => ToDto(entry, config, mentionTerm)).ToList();
        });

        await SendJson(ctx, 200, new { messages = dtos, hasMore = start > 0 });
    }

    private async Task HandleReply(HttpContext ctx)
    {
        var body = TryDeserialize<WebReplyRequest>(ctx.Request.DataAsString);
        if (body == null)
        {
            await SendJson(ctx, 400, new WebErrorResponse("Invalid request."));
            return;
        }

        var result = await plugin.Framework.RunOnFrameworkThread(() =>
        {
            var entry = plugin.ChatLog.FindBySequence(body.Sequence);
            if (entry == null)
            {
                return "This message is no longer in memory.";
            }

            return plugin.ReplyTo(entry) ? null : "There is no channel to reply in for this message.";
        });

        if (result != null)
        {
            await SendJson(ctx, 400, new WebErrorResponse(result));
            return;
        }

        await SendJson(ctx, 202, new { ok = true });
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
        if (plugin.IsFilteredOut(entry))
        {
            return false;
        }

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

    private WebMessageDto ToDto(ChatLogEntry entry, Configuration config, string mentionTerm)
    {
        var isOutgoingTell = entry.ChatType == XivChatType.TellOutgoing;
        var displayName = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
        var channelColor = ChannelDisplay.Color(entry.ChatType, config);
        var relation = plugin.GetRelation(entry);

        var senderColor = isOutgoingTell
            ? config.SendAccentColor
            : ChatWindow.RelationNameColor(relation, entry, displayName, config)
                ?? (entry.ChatType == XivChatType.TellIncoming
                    ? ChannelDisplay.WhisperColor(displayName, config)
                    : channelColor);

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
            Note = plugin.GetPlayerNote(entry),
            Bookmarked = plugin.IsBookmarked(entry),
            CanReply = entry.Sequence > 0 && Plugin.CanReplyIn(entry.ChatType),
            IsWhisper = entry.ChatType is XivChatType.TellIncoming or XivChatType.TellOutgoing,
            WhisperTarget = !isOutgoingTell && entry.IsRealPlayer && entry.Sender.Length > 0
                && !string.Equals(entry.Sender, plugin.OwnCharacterName, StringComparison.OrdinalIgnoreCase)
                ? displayName
                : null,
            RelationIcon = relation == 0 || !config.ShowRelationIcons
                ? null
                : relation switch
                {
                    Plugin.RelationFavorite => "favorite",
                    Plugin.RelationFriend => "friend",
                    _ => "fc",
                },
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
