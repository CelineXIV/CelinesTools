using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Dalamud.Plugin.Services;

namespace CelinesChat.Services;

/// <summary>
/// Batches log lines and appends them to disk from a background timer. Chat messages arrive on
/// the game's main thread; opening, writing and closing the log file for every single one of
/// them right there (busy /say or /shout areas easily do several per second) is a steady source
/// of frame-time spikes.
/// </summary>
internal sealed class ChatLogFileWriter : IDisposable
{
    private const int FlushIntervalMs = 1000;

    private readonly IPluginLog log;
    private readonly ConcurrentQueue<(string Path, string Line)> pending = new();
    private readonly object flushLock = new();
    private readonly Timer timer;

    public ChatLogFileWriter(IPluginLog log)
    {
        this.log = log;
        timer = new Timer(_ => FlushFromTimer(), null, FlushIntervalMs, FlushIntervalMs);
    }

    // An exception escaping a timer callback would terminate the whole game process.
    private void FlushFromTimer()
    {
        try
        {
            Flush();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Konnte Chatnachrichten nicht in Logdatei schreiben.");
        }
    }

    public void Enqueue(string path, string line) => pending.Enqueue((path, line));

    /// <summary>Writes everything queued so far. Safe to call from any thread.</summary>
    public void Flush()
    {
        lock (flushLock)
        {
            if (pending.IsEmpty)
            {
                return;
            }

            // Grouped per file so a flush is one append per file, not one per line - while
            // keeping each file's lines in arrival order.
            var byFile = new Dictionary<string, StringBuilder>();
            var order = new List<string>();
            while (pending.TryDequeue(out var item))
            {
                if (!byFile.TryGetValue(item.Path, out var builder))
                {
                    builder = new StringBuilder();
                    byFile[item.Path] = builder;
                    order.Add(item.Path);
                }

                builder.Append(item.Line).Append(Environment.NewLine);
            }

            foreach (var path in order)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.AppendAllText(path, byFile[path].ToString());
                }
                catch (Exception ex)
                {
                    log.Warning(ex, "Konnte Chatnachrichten nicht in Logdatei schreiben.");
                }
            }
        }
    }

    public void Dispose()
    {
        // Wait for a timer callback that might already be running, so nothing touches the files
        // after the plugin is unloaded - then write whatever is still queued.
        using (var done = new ManualResetEvent(false))
        {
            if (timer.Dispose(done))
            {
                done.WaitOne(2000);
            }
        }

        Flush();
    }
}
