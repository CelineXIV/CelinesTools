using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Dalamud.Plugin.Services;

namespace CelinesChat.Services;

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

    public void Flush()
    {
        lock (flushLock)
        {
            if (pending.IsEmpty)
            {
                return;
            }

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
