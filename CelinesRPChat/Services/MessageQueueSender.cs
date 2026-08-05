using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace CelinesRPChat.Services;

internal sealed class MessageQueueSender : IDisposable
{
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Queue<string> queue = new();
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

    public void Enqueue(IEnumerable<string> commands, int delayBetweenMessagesMs)
    {
        delayMs = Math.Max(0, delayBetweenMessagesMs);
        queue.Clear();

        foreach (var command in commands)
        {
            if (!string.IsNullOrWhiteSpace(command))
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
            log.Error(ex, Loc.T("Log.SendError", command));
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
