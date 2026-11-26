using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace CelinesChat.Services;

internal sealed class AvatarService : IDisposable
{
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(7);

    private static readonly Regex EntryRegex = new(
        "<a href=\"/lodestone/character/\\d+/\" class=\"entry__link\">.*?<img src=\"(?<img>[^\"]+)\".*?<p class=\"entry__name\">(?<name>[^<]+)</p>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly ITextureProvider textures;
    private readonly ITextureReadbackProvider readback;
    private readonly IPluginLog log;
    private readonly string customFolder;
    private readonly string lodestoneFolder;
    private readonly HttpClient http;
    private readonly SemaphoreSlim fetchGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Task> running = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource disposing = new();

    private const long MemoryCacheMs = 10_000;
    private readonly Dictionary<string, CachedAvatar> memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> forgetQueue = new();

    private const int MaxQueuedLookups = 25;

    private sealed class CachedAvatar
    {
        public string? Path;
        public ISharedImmediateTexture? Texture;
        public LookupState State;
        public long CheckedAt;
        public string? CustomFileName;
        public bool AllowLodestone;
    }

    public AvatarService(ITextureProvider textures, ITextureReadbackProvider readback, IPluginLog log, string configDirectory)
    {
        this.textures = textures;
        this.readback = readback;
        this.log = log;
        customFolder = Path.Combine(configDirectory, "Avatars", "Custom");
        lodestoneFolder = Path.Combine(configDirectory, "Avatars", "Lodestone");

        http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; CelinesChat)");
    }

    internal enum LookupState
    {
        None,
        Loading,
        NotFound,
    }

    internal ImTextureID? Get(string key, string? customFileName, bool allowLodestone, string name, string? world, out LookupState state)
    {
        while (forgetQueue.TryDequeue(out var finished))
        {
            memory.Remove(finished);
        }

        var now = Environment.TickCount64;
        if (memory.TryGetValue(key, out var cached)
            && now - cached.CheckedAt < MemoryCacheMs
            && cached.CustomFileName == customFileName
            && cached.AllowLodestone == allowLodestone)
        {
            state = cached.State;
            return cached.Texture?.GetWrapOrDefault()?.Handle;
        }

        var path = Resolve(key, customFileName, allowLodestone, name, world, out state);
        var texture = path != null ? TryShared(path) : null;
        memory[key] = new CachedAvatar
        {
            Path = path,
            Texture = texture,
            State = state,
            CheckedAt = now,
            CustomFileName = customFileName,
            AllowLodestone = allowLodestone,
        };

        if (memory.Count > 2000)
        {
            memory.Clear();
        }

        return texture?.GetWrapOrDefault()?.Handle;
    }

    private const int MaxGreyCopies = 400;
    private const int MaxGreyConversions = 3;
    private readonly Dictionary<string, IDalamudTextureWrap> grey = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> greyRunning = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(string Path, IDalamudTextureWrap? Wrap)> greyDone = new();

    internal ImTextureID? GetGrey(string key, string? customFileName, bool allowLodestone, string name, string? world)
    {
        while (greyDone.TryDequeue(out var done))
        {
            greyRunning.Remove(done.Path);
            if (done.Wrap != null)
            {
                if (grey.Count >= MaxGreyCopies)
                {
                    foreach (var old in grey.Values)
                    {
                        old.Dispose();
                    }

                    grey.Clear();
                }

                if (grey.Remove(done.Path, out var replaced))
                {
                    replaced.Dispose();
                }

                grey[done.Path] = done.Wrap;
            }
        }

        if (Get(key, customFileName, allowLodestone, name, world, out _) == null
            || !memory.TryGetValue(key, out var cached) || cached.Path is not { } path)
        {
            return null;
        }

        if (grey.TryGetValue(path, out var wrap))
        {
            return wrap.Handle;
        }

        if (greyRunning.Count < MaxGreyConversions && !greyRunning.Contains(path) && cached.Texture?.GetWrapOrDefault() is { } source)
        {
            greyRunning.Add(path);
            _ = MakeGreyAsync(path, source);
        }

        return null;
    }

    private async Task MakeGreyAsync(string path, IDalamudTextureWrap source)
    {
        IDalamudTextureWrap? result = null;
        try
        {
            var (spec, data) = await readback.GetRawImageAsync(source, new TextureModificationArgs { DxgiFormat = 87 }, true, disposing.Token);
            for (var y = 0; y < spec.Height; y++)
            {
                var rowStart = y * spec.Pitch;
                for (var x = 0; x < spec.Width; x++)
                {
                    var i = rowStart + (x * 4);
                    var lum = (byte)Math.Clamp((0.114 * data[i]) + (0.587 * data[i + 1]) + (0.299 * data[i + 2]), 0, 255);
                    data[i] = lum;
                    data[i + 1] = lum;
                    data[i + 2] = lum;
                }
            }

            result = await textures.CreateFromRawAsync(spec, data, "CelinesChat grey avatar", disposing.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Debug(ex, "[CelinesChat] Graues Avatarbild konnte nicht erstellt werden.");
        }

        greyDone.Enqueue((path, result));
    }

    private ISharedImmediateTexture? TryShared(string path)
    {
        try
        {
            return textures.GetFromFile(new FileInfo(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? Resolve(string key, string? customFileName, bool allowLodestone, string name, string? world, out LookupState state)
    {
        state = LookupState.None;

        if (!string.IsNullOrEmpty(customFileName))
        {
            var custom = Path.Combine(customFolder, customFileName);
            if (File.Exists(custom))
            {
                return custom;
            }
        }

        if (!allowLodestone || string.IsNullOrEmpty(world))
        {
            return null;
        }

        var file = Path.Combine(lodestoneFolder, SafeFileName(key) + ".jpg");
        var notFoundMarker = Path.Combine(lodestoneFolder, SafeFileName(key) + ".none");

        if (File.Exists(file))
        {
            if (DateTime.Now - File.GetLastWriteTime(file) > RefreshAfter)
            {
                StartLookup(key, name, world, file, notFoundMarker);
            }

            return file;
        }

        if (File.Exists(notFoundMarker) && DateTime.Now - File.GetLastWriteTime(notFoundMarker) < RefreshAfter)
        {
            state = LookupState.NotFound;
            return null;
        }

        StartLookup(key, name, world, file, notFoundMarker);
        state = LookupState.Loading;
        return null;
    }

    private void StartLookup(string key, string name, string world, string file, string notFoundMarker)
    {
        if (running.ContainsKey(key) || running.Count >= MaxQueuedLookups)
        {
            return;
        }

        running[key] = Task.Run(async () =>
        {
            try
            {
                await LookupAsync(name, world, file, notFoundMarker, disposing.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                log.Debug(ex, "[CelinesChat] Lodestone-Avatar konnte nicht geladen werden.");
            }
            finally
            {
                running.TryRemove(key, out _);
                Forget(key);
            }
        });
    }

    private async Task LookupAsync(string name, string world, string file, string notFoundMarker, CancellationToken token)
    {
        await fetchGate.WaitAsync(token);
        try
        {
            var url = "https://de.finalfantasyxiv.com/lodestone/character/?q=" + Uri.EscapeDataString(name)
                + "&worldname=" + Uri.EscapeDataString(world);
            var html = await http.GetStringAsync(url, token);

            string? imageUrl = null;
            foreach (Match match in EntryRegex.Matches(html))
            {
                if (string.Equals(WebUtility.HtmlDecode(match.Groups["name"].Value).Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    imageUrl = WebUtility.HtmlDecode(match.Groups["img"].Value);
                    break;
                }
            }

            Directory.CreateDirectory(lodestoneFolder);
            if (imageUrl == null || !imageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                await File.WriteAllTextAsync(notFoundMarker, string.Empty, token);
                return;
            }

            var bytes = await http.GetByteArrayAsync(imageUrl, token);
            var temp = file + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, token);
            File.Move(temp, file, true);
            File.Delete(notFoundMarker);
        }
        finally
        {
            try
            {
                await Task.Delay(1200, token);
            }
            finally
            {
                fetchGate.Release();
            }
        }
    }

    private void Forget(string key) => forgetQueue.Enqueue(key);

    internal string CustomPath(string fileName) => Path.Combine(customFolder, fileName);

    internal (Vector2 Uv0, Vector2 Uv1) UvFor(string key, string? customFile, Dictionary<string, Vector4> crops)
    {
        if (customFile != null && crops.TryGetValue(customFile, out var crop))
        {
            return (new Vector2(crop.X, crop.Y), new Vector2(crop.Z, crop.W));
        }

        if (memory.TryGetValue(key, out var cached) && cached.Texture?.GetWrapOrDefault() is { } wrap
            && wrap.Width > 0 && wrap.Height > 0 && wrap.Width != wrap.Height)
        {
            float w = wrap.Width;
            float h = wrap.Height;
            var side = MathF.Min(w, h);
            var x = (w - side) / 2f / w;
            var y = (h - side) / 2f / h;
            return (new Vector2(x, y), new Vector2(x + (side / w), y + (side / h)));
        }

        return (Vector2.Zero, Vector2.One);
    }

    internal string StoreCustom(string key, string sourcePath)
    {
        Directory.CreateDirectory(customFolder);

        var fileName = SafeFileName(key) + "-" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(sourcePath).ToLowerInvariant();
        File.Copy(sourcePath, Path.Combine(customFolder, fileName), true);
        return fileName;
    }

    internal void DeleteCustom(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(customFolder, fileName));
        }
        catch (Exception)
        {
        }
    }

    internal void ForgetLodestone(string key)
    {
        Forget(key);
        foreach (var ext in new[] { ".jpg", ".none" })
        {
            try
            {
                File.Delete(Path.Combine(lodestoneFolder, SafeFileName(key) + ext));
            }
            catch (Exception)
            {
            }
        }
    }

    private static string SafeFileName(string key)
    {
        var name = key;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Replace(' ', '_');
    }

    public void Dispose()
    {
        disposing.Cancel();
        foreach (var wrap in grey.Values)
        {
            wrap.Dispose();
        }

        grey.Clear();
        while (greyDone.TryDequeue(out var done))
        {
            done.Wrap?.Dispose();
        }

        http.Dispose();
    }
}
