using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace CelinesToolkit.Services;

public sealed class PreviewTextureCache : IDisposable
{
    private sealed class Entry
    {
        public DateTime LastWriteTimeUtc;
        public IDalamudTextureWrap? Wrap;
        public Task<IDalamudTextureWrap>? LoadingTask;
    }

    private readonly ITextureProvider textureProvider;
    private readonly Dictionary<string, Entry> entries = new();

    public PreviewTextureCache(ITextureProvider textureProvider)
    {
        this.textureProvider = textureProvider;
    }

    public IDalamudTextureWrap? GetOrLoad(string path)
    {
        DateTime lastWriteTimeUtc;
        try
        {
            lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return null;
        }

        if (!entries.TryGetValue(path, out var entry))
        {
            entry = new Entry();
            entries[path] = entry;
        }

        if (entry.LoadingTask == null && entry.LastWriteTimeUtc != lastWriteTimeUtc)
        {
            entry.LastWriteTimeUtc = lastWriteTimeUtc;
            entry.LoadingTask = LoadAsync(path);
        }

        if (entry.LoadingTask is { IsCompletedSuccessfully: true } completedTask)
        {
            entry.Wrap?.Dispose();
            entry.Wrap = completedTask.Result;
            entry.LoadingTask = null;
        }
        else if (entry.LoadingTask is { IsFaulted: true } or { IsCanceled: true })
        {
            entry.LoadingTask = null;
        }

        return entry.Wrap;
    }

    private async Task<IDalamudTextureWrap> LoadAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        return await textureProvider.CreateFromImageAsync(bytes);
    }

    public void Dispose()
    {
        foreach (var entry in entries.Values)
        {
            entry.Wrap?.Dispose();
        }

        entries.Clear();
    }
}
