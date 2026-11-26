using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace C4TChat.Services;

internal sealed class ImagePreviewService : IDisposable
{
    internal enum PreviewState
    {
        Loading,
        Ready,
        Failed,
    }

    private const long MaxBytes = 8 * 1024 * 1024;
    private const int MaxParallel = 2;
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

    private static readonly Regex UrlRegex = new(@"https?://[^\s<>""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] ImageHosts = { "i.imgur.com", "cdn.discordapp.com", "media.discordapp.net", "i.gyazo.com", "pbs.twimg.com" };
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    private static readonly System.Text.RegularExpressions.Regex OgImage = new(
        @"<meta[^>]+property=""og:image""[^>]+content=""([^""]+)""|<meta[^>]+content=""([^""]+)""[^>]+property=""og:image""",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex TenorView = new(
        @"^/(?:[a-z]{2}(?:-[a-z]{2,4})?/)?view/[^/]+",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsGifPage(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath;
        return ((host is "tenor.com" or "www.tenor.com") && TenorView.IsMatch(path))
            || ((host is "giphy.com" or "www.giphy.com") && path.StartsWith("/gifs/", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> ResolveGifPage(string url)
    {
        var uri = new Uri(url);
        if (uri.Host.EndsWith("giphy.com", StringComparison.OrdinalIgnoreCase))
        {
            var slug = uri.AbsolutePath.TrimEnd('/').Split('/')[^1];
            var id = slug[(slug.LastIndexOf('-') + 1)..];
            return id.Length > 0 && id.All(char.IsLetterOrDigit) ? "https://i.giphy.com/" + id + ".gif" : null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, disposing.Token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(disposing.Token);
        var buffer = new byte[512 * 1024];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), disposing.Token)) > 0)
        {
            total += read;
        }

        var match = OgImage.Match(Encoding.UTF8.GetString(buffer, 0, total));
        if (!match.Success)
        {
            return null;
        }

        var image = System.Net.WebUtility.HtmlDecode(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
        return Uri.TryCreate(image, UriKind.Absolute, out var resolved) && resolved.Scheme == Uri.UriSchemeHttps ? image : null;
    }

    private static string DownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !uri.Host.Equals("media.discordapp.net", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length < 2)
        {
            return url;
        }

        var kept = new List<string>();
        foreach (var part in uri.Query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Split('=')[0];
            if (!name.Equals("format", StringComparison.OrdinalIgnoreCase) && !name.Equals("quality", StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(part);
            }
        }

        return uri.GetLeftPart(UriPartial.Path) + (kept.Count > 0 ? "?" + string.Join("&", kept) : string.Empty);
    }

    private readonly ITextureProvider textures;
    private readonly IPluginLog log;
    private readonly string folder;
    private readonly HttpClient http;
    private readonly CancellationTokenSource disposing = new();
    private readonly Dictionary<string, PreviewState> states = new();
    private readonly ConcurrentQueue<(string Url, PreviewState State)> finished = new();
    private readonly Dictionary<long, string[]> urlsBySequence = new();
    private int running;

    private const int MaxGifFrames = 300;
    private const long GifBudgetBytes = 48L * 1024 * 1024;
    private const int MaxGifSide = 480;

    private sealed class GifAnimation
    {
        public volatile IDalamudTextureWrap[]? Frames;
        public int[] Ends = Array.Empty<int>();
        public bool Static;
        public bool Disposed;
        public long LastUsed;
    }

    private readonly Dictionary<string, GifAnimation> gifs = new();
    private long nextGifSweep;

    public ImagePreviewService(ITextureProvider textures, IPluginLog log, string configDirectory)
    {
        this.textures = textures;
        this.log = log;
        folder = Path.Combine(configDirectory, "ImageCache");
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; C4TChat)");
        Task.Run(CleanUp);
    }

    internal string[] ImageUrls(ChatLogEntry entry)
    {
        if (urlsBySequence.TryGetValue(entry.Sequence, out var cached))
        {
            return cached;
        }

        var found = new List<string>();
        foreach (Match match in UrlRegex.Matches(entry.Text))
        {
            var url = match.Value.TrimEnd('.', ',', ')', '!', '?');
            if (IsImageUrl(url) && !found.Contains(url))
            {
                found.Add(url);
            }

            if (found.Count >= 3)
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

    private static bool IsImageUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath.ToLowerInvariant();
        if (IsGifPage(uri))
        {
            return true;
        }

        foreach (var ext in ImageExtensions)
        {
            if (path.EndsWith(ext, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var host in ImageHosts)
        {
            if (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && path.Contains('.'))
            {
                return true;
            }
        }

        return false;
    }

    internal IDalamudTextureWrap? Get(string url, out PreviewState state)
    {
        while (finished.TryDequeue(out var done))
        {
            states[done.Url] = done.State;
        }

        var file = FileFor(url);
        if (!states.TryGetValue(url, out state))
        {
            if (File.Exists(file))
            {
                state = PreviewState.Ready;
                states[url] = state;
            }
            else if (running < MaxParallel)
            {
                state = PreviewState.Loading;
                states[url] = state;
                Interlocked.Increment(ref running);
                _ = Download(url, file);
            }
            else
            {
                state = PreviewState.Loading;
                return null;
            }
        }

        if (state != PreviewState.Ready)
        {
            return null;
        }

        if (Animated(url, file) is { } frame)
        {
            return frame;
        }

        try
        {
            return textures.GetFromFile(new FileInfo(file)).GetWrapOrDefault();
        }
        catch (Exception)
        {
            states[url] = PreviewState.Failed;
            state = PreviewState.Failed;
            return null;
        }
    }

    private IDalamudTextureWrap? Animated(string url, string file)
    {
        var now = Environment.TickCount64;
        SweepGifs(now);
        if (!gifs.TryGetValue(url, out var gif))
        {
            gif = new GifAnimation();
            gifs[url] = gif;
            if (IsGifFile(file))
            {
                _ = Task.Run(() => DecodeGif(gif, file));
            }
            else
            {
                gif.Static = true;
            }
        }

        gif.LastUsed = now;
        var frames = gif.Frames;
        if (gif.Static || frames == null || frames.Length < 2)
        {
            return frames is { Length: 1 } single ? single[0] : null;
        }

        var ends = gif.Ends;
        var t = (int)(now % ends[^1]);
        for (var i = 0; i < ends.Length; i++)
        {
            if (t < ends[i])
            {
                return frames[i];
            }
        }

        return frames[^1];
    }

    private static bool IsGifFile(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            Span<byte> head = stackalloc byte[6];
            return stream.Read(head) == 6 && GifDecoder.IsGif(head);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void DecodeGif(GifAnimation gif, string file)
    {
        try
        {
            var animation = GifDecoder.Decode(File.ReadAllBytes(file), MaxGifFrames);
            if (animation == null || animation.Frames.Count < 2)
            {
                gif.Static = true;
                return;
            }

            var width = animation.Width;
            var height = animation.Height;
            var factor = MathF.Min(1f, MaxGifSide / (float)Math.Max(width, height));
            var bytes = (double)animation.Frames.Count * width * height * 4 * factor * factor;
            if (bytes > GifBudgetBytes)
            {
                factor *= (float)Math.Sqrt(GifBudgetBytes / bytes);
            }

            var newWidth = Math.Max(1, (int)(width * factor));
            var newHeight = Math.Max(1, (int)(height * factor));
            var spec = RawImageSpecification.Rgba32(newWidth, newHeight);
            var wraps = new IDalamudTextureWrap[animation.Frames.Count];
            var ends = new int[animation.Frames.Count];
            var total = 0;
            for (var i = 0; i < wraps.Length; i++)
            {
                if (gif.Disposed || disposing.IsCancellationRequested)
                {
                    for (var j = 0; j < i; j++)
                    {
                        wraps[j].Dispose();
                    }

                    return;
                }

                var pixels = newWidth == width && newHeight == height
                    ? animation.Frames[i]
                    : GifDecoder.Scale(animation.Frames[i], width, height, newWidth, newHeight);
                wraps[i] = textures.CreateFromRaw(spec, pixels, "C4TChat GIF");
                total += animation.DelaysMs[i];
                ends[i] = total;
            }

            lock (gif)
            {
                if (gif.Disposed)
                {
                    foreach (var wrap in wraps)
                    {
                        wrap.Dispose();
                    }

                    return;
                }

                gif.Ends = ends;
                gif.Frames = wraps;
            }
        }
        catch (Exception ex)
        {
            gif.Static = true;
            log.Debug(ex, "[C4TChat] GIF konnte nicht animiert werden.");
        }
    }

    private void SweepGifs(long now)
    {
        if (now < nextGifSweep)
        {
            return;
        }

        nextGifSweep = now + 5000;
        List<string>? stale = null;
        foreach (var (url, gif) in gifs)
        {
            if (now - gif.LastUsed > 20_000)
            {
                (stale ??= new List<string>()).Add(url);
            }
        }

        if (stale == null)
        {
            return;
        }

        foreach (var url in stale)
        {
            ReleaseGif(gifs[url]);
            gifs.Remove(url);
        }
    }

    private static void ReleaseGif(GifAnimation gif)
    {
        lock (gif)
        {
            gif.Disposed = true;
            if (gif.Frames is { } frames)
            {
                gif.Frames = null;
                foreach (var wrap in frames)
                {
                    wrap.Dispose();
                }
            }
        }
    }

    private async Task Download(string url, string file)
    {
        var result = PreviewState.Failed;
        try
        {
            var target = DownloadUrl(url);
            if (Uri.TryCreate(url, UriKind.Absolute, out var pageUri) && IsGifPage(pageUri))
            {
                target = await ResolveGifPage(url) ?? throw new InvalidDataException("no gif on page");
            }

            using var response = await http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, disposing.Token);
            if (response.IsSuccessStatusCode
                && response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
                && (response.Content.Headers.ContentLength ?? 0) <= MaxBytes)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(disposing.Token);
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, disposing.Token)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > MaxBytes)
                    {
                        throw new InvalidDataException("too large");
                    }
                }

                Directory.CreateDirectory(folder);
                var temp = file + ".tmp";
                await File.WriteAllBytesAsync(temp, buffer.ToArray(), disposing.Token);
                File.Move(temp, file, true);
                result = PreviewState.Ready;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Debug(ex, "[C4TChat] Bildvorschau konnte nicht geladen werden.");
        }
        finally
        {
            Interlocked.Decrement(ref running);
            finished.Enqueue((url, result));
        }
    }

    private string FileFor(string url)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(folder, hash + ".img");
    }

    private void CleanUp()
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(folder))
            {
                if (DateTime.Now - File.GetLastWriteTime(file) > KeepFor)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        disposing.Cancel();
        foreach (var gif in gifs.Values)
        {
            ReleaseGif(gif);
        }

        gifs.Clear();
        http.Dispose();
    }
}
