using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;

namespace C4TChat.Services;

internal sealed class PlayerExtrasCache
{
    internal sealed class CachedMoodle
    {
        public uint Icon { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int Type { get; set; }

        public int Stacks { get; set; }
    }

    internal sealed class Entry
    {
        public string? Title { get; set; }

        public bool IsPrefix { get; set; }

        public Vector4? Color { get; set; }

        public List<CachedMoodle> Moodles { get; set; } = new();

        public DateTime SeenAt { get; set; }
    }

    private const int MaxPlayers = 500;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private const long SaveDelayMs = 30_000;

    private readonly string path;
    private readonly IPluginLog log;
    private Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> lastQueried = new(StringComparer.OrdinalIgnoreCase);
    private bool dirty;
    private long saveDueAt;

    public PlayerExtrasCache(string configDirectory, IPluginLog log)
    {
        path = Path.Combine(configDirectory, "PlayerExtras.json");
        this.log = log;
        Load();
    }

    internal Entry? Get(string key) => entries.TryGetValue(key, out var entry) ? entry : null;

    internal bool ShouldQuery(string key, long now, long intervalMs)
    {
        if (lastQueried.TryGetValue(key, out var last) && now - last < intervalMs)
        {
            return false;
        }

        lastQueried[key] = now;
        if (lastQueried.Count > 2000)
        {
            lastQueried.Clear();
        }

        return true;
    }

    internal void Store(string key, PluginIntegrations.HonorificTitle? title, List<PluginIntegrations.MoodleStatus> moodles)
    {
        entries[key] = new Entry
        {
            Title = title?.Text,
            IsPrefix = title?.IsPrefix ?? false,
            Color = title?.Color,
            Moodles = moodles.Select(m => new CachedMoodle { Icon = m.Icon, Title = m.Title, Description = m.Description, Type = m.Type, Stacks = m.Stacks }).ToList(),
            SeenAt = DateTime.Now,
        };

        if (!dirty)
        {
            dirty = true;
            saveDueAt = Environment.TickCount64 + SaveDelayMs;
        }
    }

    internal void Clear()
    {
        entries.Clear();
        dirty = true;
        saveDueAt = 0;
        SaveIfDue();
    }

    internal void SaveIfDue(bool force = false)
    {
        if (!dirty || (!force && Environment.TickCount64 < saveDueAt))
        {
            return;
        }

        dirty = false;
        try
        {
            Prune();
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(entries));
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[C4TChat] Gespeicherte Titel/Moodles konnten nicht geschrieben werden.");
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var loaded = JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(path));
            if (loaded != null)
            {
                entries = new Dictionary<string, Entry>(loaded, StringComparer.OrdinalIgnoreCase);
                Prune();
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[C4TChat] Gespeicherte Titel/Moodles konnten nicht gelesen werden.");
        }
    }

    private void Prune()
    {
        var cutoff = DateTime.Now - MaxAge;
        foreach (var key in entries.Where(e => e.Value.SeenAt < cutoff).Select(e => e.Key).ToList())
        {
            entries.Remove(key);
        }

        if (entries.Count > MaxPlayers)
        {
            foreach (var key in entries.OrderBy(e => e.Value.SeenAt).Take(entries.Count - MaxPlayers).Select(e => e.Key).ToList())
            {
                entries.Remove(key);
            }
        }
    }
}
