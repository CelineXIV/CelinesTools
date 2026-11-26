using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace C4TChat.Services;

internal sealed class LinkCardService : IDisposable
{
    internal enum Provider
    {
        YouTube,
        Twitch,
        Spotify,
    }

    internal sealed class LinkCard
    {
        public Provider Provider { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Author { get; set; }

        public string? ThumbnailUrl { get; set; }

        public string? AppUri { get; set; }

        public DateTime FetchedAt { get; set; }
    }

    private const int MaxHtmlBytes = 1024 * 1024;
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);
    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromMinutes(10);
    private static readonly Regex UrlRegex = new(@"https?://[^\s<>""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex YouTubeId = new(@"^[A-Za-z0-9_-]{6,20}$", RegexOptions.Compiled);
    private static readonly HashSet<string> SpotifyTypes = new() { "track", "album", "playlist", "artist", "episode", "show" };
    private static readonly Regex SpotifyId = new(@"^[A-Za-z0-9]{10,40}$", RegexOptions.Compiled);
    private static readonly Regex SpotifyListenTitle = new(@"^Listen to (.+?) - [^|]+? by (.+?) \| Spotify$", RegexOptions.Compiled);
    private static readonly Regex MetaTag = new(@"<meta\s[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MetaAttr = new(@"(property|name|content)\s*=\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IPluginLog log;
    private readonly string file;
    private readonly HttpClient http;
    private readonly CancellationTokenSource disposing = new();
    private readonly Dictionary<string, LinkCard> cards = new();
    private readonly Dictionary<string, DateTime> failed = new();
    private readonly HashSet<string> loading = new();
    private readonly ConcurrentQueue<(string Url, LinkCard? Card)> finished = new();
    private readonly Dictionary<long, string[]> urlsBySequence = new();
    private bool dirty;
    private long nextSave;

    public LinkCardService(IPluginLog log, string configDirectory)
    {
        this.log = log;
        file = Path.Combine(configDirectory, "LinkCards.json");
        http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        Load();
    }

    internal string[] CardUrls(ChatLogEntry entry)
    {
        if (urlsBySequence.TryGetValue(entry.Sequence, out var cached))
        {
            return cached;
        }

        var found = new List<string>();
        foreach (Match match in UrlRegex.Matches(entry.Text))
        {
            var url = match.Value.TrimEnd('.', ',', ')', '!', '?');
            if (Classify(url, out _, out _) && !found.Contains(url))
            {
                found.Add(url);
            }

            if (found.Count >= 2)
            {
                break;
            }
        }

        var result = found.ToArray();
        if (urlsBySequence.Count > 5000)
        {
            urlsBySequence.Clear();
        }

        urlsBySequence[entry.Sequence] = result;
        return result;
    }

    internal LinkCard? Get(string url, out bool isLoading)
    {
        while (finished.TryDequeue(out var done))
        {
            loading.Remove(done.Url);
            if (done.Card != null)
            {
                cards[done.Url] = done.Card;
                dirty = true;
            }
            else
            {
                failed[done.Url] = DateTime.UtcNow;
            }
        }

        SaveIfDue();
        isLoading = false;
        if (cards.TryGetValue(url, out var card))
        {
            return card;
        }

        if (loading.Contains(url))
        {
            isLoading = true;
            return null;
        }

        if (failed.TryGetValue(url, out var at) && DateTime.UtcNow - at < RetryFailedAfter)
        {
            return null;
        }

        if (!Classify(url, out var provider, out var normalized) || loading.Count >= 3)
        {
            isLoading = loading.Count >= 3;
            return null;
        }

        loading.Add(url);
        isLoading = true;
        _ = Fetch(url, provider, normalized);
        return null;
    }

    internal static bool Classify(string url, out Provider provider, out string normalized)
    {
        provider = Provider.YouTube;
        normalized = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (host is "youtu.be")
        {
            return segments.Length >= 1 && YouTubeVideo(segments[0], out normalized);
        }

        if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com")
        {
            if (segments.Length >= 1 && segments[0] == "watch")
            {
                foreach (var part in uri.Query.TrimStart('?').Split('&'))
                {
                    if (part.StartsWith("v=", StringComparison.Ordinal))
                    {
                        return YouTubeVideo(Uri.UnescapeDataString(part[2..]), out normalized);
                    }
                }

                return false;
            }

            if (segments.Length >= 2 && segments[0] is "shorts" or "live" or "embed")
            {
                return YouTubeVideo(segments[1], out normalized);
            }

            return false;
        }

        if (host is "open.spotify.com" or "play.spotify.com")
        {
            var offset = segments.Length > 0 && segments[0].StartsWith("intl-", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if (segments.Length >= offset + 2
                && SpotifyTypes.Contains(segments[offset].ToLowerInvariant())
                && SpotifyId.IsMatch(segments[offset + 1]))
            {
                provider = Provider.Spotify;
                normalized = "https://open.spotify.com/" + segments[offset].ToLowerInvariant() + "/" + segments[offset + 1];
                return true;
            }

            return false;
        }

        if (host is "clips.twitch.tv" && segments.Length >= 1)
        {
            provider = Provider.Twitch;
            normalized = "https://clips.twitch.tv/" + segments[0];
            return true;
        }

        if (host is "twitch.tv" or "www.twitch.tv" or "m.twitch.tv" && segments.Length >= 1)
        {
            var first = segments[0].ToLowerInvariant();
            if (first is "directory" or "settings" or "downloads" or "jobs" or "p" or "search" or "subscriptions" or "inventory" or "wallet" or "drops")
            {
                return false;
            }

            provider = Provider.Twitch;
            normalized = "https://www.twitch.tv/" + string.Join('/', segments);
            return true;
        }

        return false;
    }

    private static bool YouTubeVideo(string id, out string normalized)
    {
        normalized = YouTubeId.IsMatch(id) ? "https://www.youtube.com/watch?v=" + id : string.Empty;
        return normalized.Length > 0;
    }

    private async Task Fetch(string url, Provider provider, string normalized)
    {
        LinkCard? card = null;
        try
        {
            card = provider switch
            {
                Provider.YouTube => await FetchYouTube(normalized),
                Provider.Spotify => await FetchSpotify(normalized),
                _ => await FetchTwitch(normalized),
            };
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Debug(ex, "[C4TChat] Linkvorschau konnte nicht geladen werden.");
        }

        finished.Enqueue((url, card));
    }

    private async Task<LinkCard?> FetchYouTube(string normalized)
    {
        var endpoint = "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(normalized);
        using var response = await http.GetAsync(endpoint, disposing.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = JObject.Parse(await response.Content.ReadAsStringAsync(disposing.Token));
        var title = json.Value<string>("title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new LinkCard
        {
            Provider = Provider.YouTube,
            Title = title.Trim(),
            Author = json.Value<string>("author_name"),
            ThumbnailUrl = json.Value<string>("thumbnail_url"),
            FetchedAt = DateTime.UtcNow,
        };
    }

    private async Task<LinkCard?> FetchSpotify(string normalized)
    {
        var meta = await FetchMeta(normalized, "Mozilla/5.0 (compatible; C4TChat)");
        if (meta == null || !meta.TryGetValue("og:title", out var title) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        title = title.Trim();
        string? byLine = null;
        var listen = SpotifyListenTitle.Match(title);
        if (listen.Success)
        {
            title = listen.Groups[1].Value;
            byLine = listen.Groups[2].Value;
        }
        else if (title.EndsWith(" | Spotify", StringComparison.Ordinal))
        {
            title = title[..^10];
        }

        meta.TryGetValue("og:description", out var description);
        var parts = new List<string>();
        foreach (var part in (description ?? string.Empty).Split(" \u00b7 ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var lower = part.ToLowerInvariant();
            if (lower is "song" or "album" or "single" or "ep" or "playlist" or "artist" or "podcast" or "episode" or "compilation"
                || (part.Length == 4 && int.TryParse(part, out _))
                || lower.EndsWith(" songs", StringComparison.Ordinal))
            {
                continue;
            }

            parts.Add(part);
        }

        if (byLine != null && !parts.Contains(byLine))
        {
            parts.Insert(0, byLine);
        }

        meta.TryGetValue("og:image", out var image);
        var segments = new Uri(normalized).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return new LinkCard
        {
            Provider = Provider.Spotify,
            Title = title,
            Author = ShortDescription(string.Join(" \u00b7 ", parts)),
            ThumbnailUrl = image,
            AppUri = "spotify:" + segments[0] + ":" + segments[1],
            FetchedAt = DateTime.UtcNow,
        };
    }

    private async Task<Dictionary<string, string>?> FetchMeta(string url, string? userAgent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (userAgent != null)
        {
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(userAgent);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, disposing.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(disposing.Token);
        var buffer = new byte[MaxHtmlBytes];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), disposing.Token)) > 0)
        {
            total += read;
        }

        var html = Encoding.UTF8.GetString(buffer, 0, total);
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTag.Matches(html))
        {
            string? key = null;
            string? content = null;
            foreach (Match attr in MetaAttr.Matches(tag.Value))
            {
                if (attr.Groups[1].Value.Equals("content", StringComparison.OrdinalIgnoreCase))
                {
                    content = attr.Groups[2].Value;
                }
                else
                {
                    key = attr.Groups[2].Value;
                }
            }

            if (key != null && content != null)
            {
                meta.TryAdd(key, WebUtility.HtmlDecode(content));
            }
        }

        return meta;
    }

    private async Task<LinkCard?> FetchTwitch(string normalized)
    {
        var meta = await FetchMeta(normalized);
        if (meta == null || !meta.TryGetValue("og:title", out var title) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        title = title.Trim();
        if (title.EndsWith(" - Twitch", StringComparison.Ordinal))
        {
            title = title[..^9];
        }

        meta.TryGetValue("og:description", out var description);
        meta.TryGetValue("og:image", out var image);
        return new LinkCard
        {
            Provider = Provider.Twitch,
            Title = title,
            Author = ShortDescription(description),
            ThumbnailUrl = image,
            FetchedAt = DateTime.UtcNow,
        };
    }

    private static string? ShortDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        var cut = text.IndexOf(" — ", StringComparison.Ordinal);
        if (cut > 0 && text.Contains("streams live on Twitch", StringComparison.Ordinal))
        {
            text = text[..cut];
        }

        return text.Length > 90 ? text[..87] + "..." : text;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(file))
            {
                return;
            }

            var loaded = JsonConvert.DeserializeObject<Dictionary<string, LinkCard>>(File.ReadAllText(file));
            if (loaded == null)
            {
                return;
            }

            foreach (var (url, card) in loaded)
            {
                if (DateTime.UtcNow - card.FetchedAt < KeepFor)
                {
                    cards[url] = card;
                }
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, "[C4TChat] LinkCards.json konnte nicht gelesen werden.");
        }
    }

    private void SaveIfDue()
    {
        var now = Environment.TickCount64;
        if (!dirty || now < nextSave)
        {
            return;
        }

        dirty = false;
        nextSave = now + 30_000;
        Save();
    }

    private void Save()
    {
        try
        {
            var keep = new Dictionary<string, LinkCard>();
            foreach (var (url, card) in cards)
            {
                if (DateTime.UtcNow - card.FetchedAt < KeepFor)
                {
                    keep[url] = card;
                }
            }

            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(keep));
            File.Move(temp, file, true);
        }
        catch (Exception ex)
        {
            log.Debug(ex, "[C4TChat] LinkCards.json konnte nicht gespeichert werden.");
        }
    }

    public void Dispose()
    {
        disposing.Cancel();
        if (dirty)
        {
            Save();
        }

        http.Dispose();
    }
}
