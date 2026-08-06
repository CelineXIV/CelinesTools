using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin.Services;

namespace CelinesChat.Services;

internal sealed class MessageQueueSender : IDisposable
{
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Queue<byte[]> queue = new();
    private DateTime nextSendTime = DateTime.MinValue;
    private int delayMs = 600;

    public bool IsSending => queue.Count > 0;

    public int Remaining => queue.Count;

    public int Total { get; private set; }

    public MessageQueueSender(IFramework framework, IPluginLog log)
    {
        this.framework = framework;
        this.log = log;
        this.framework.Update += OnUpdate;
    }

    public void Enqueue(IEnumerable<byte[]> commands, int delayBetweenMessagesMs)
    {
        delayMs = Math.Max(0, delayBetweenMessagesMs);
        queue.Clear();

        foreach (var command in commands)
        {
            if (command.Length > 0)
            {
                queue.Enqueue(command);
            }
        }

        Total = queue.Count;
        nextSendTime = DateTime.UtcNow;
    }

    public void Cancel()
    {
        queue.Clear();
        Total = 0;
    }

    private void OnUpdate(IFramework fw)
    {
        if (queue.Count == 0 || DateTime.UtcNow < nextSendTime)
        {
            return;
        }

        var command = queue.Dequeue();
        try
        {
            ChatSender.Send(command);
        }
        catch (Exception ex)
        {
            // Best-effort UTF8 decode purely for the log message - won't render an embedded
            // payload's raw bytes as anything meaningful, but that's fine for diagnostics.
            log.Error(ex, Loc.T("Log.SendError", Encoding.UTF8.GetString(command)));
        }

        if (queue.Count == 0)
        {
            Total = 0;
        }

        nextSendTime = DateTime.UtcNow.AddMilliseconds(delayMs);
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        queue.Clear();
    }
}
