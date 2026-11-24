using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace CelinesChat.Services;

internal sealed class MessageQueueSender : IDisposable
{
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly Queue<(byte[] Command, int DelayAfterMs)> queue = new();
    private DateTime nextSendTime = DateTime.MinValue;

    public bool IsSending => queue.Count > 0;

    public int Remaining => queue.Count;

    public int Total { get; private set; }

    public MessageQueueSender(IFramework framework, IClientState clientState, ICondition condition, IPluginLog log)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.condition = condition;
        this.log = log;
        this.framework.Update += OnUpdate;
    }

    /// <summary>
    /// Appends to whatever is still being sent instead of replacing it - anything enqueued while a
    /// multi-part post is still going out (a slash command, a message from the web client, ...) used
    /// to silently drop the remaining parts of that post. The pacing also carries over, so the first
    /// new message still waits out the delay after the last one that went out.
    /// </summary>
    public void Enqueue(IEnumerable<byte[]> commands, int delayBetweenMessagesMs)
    {
        if (queue.Count == 0)
        {
            Total = 0;
        }

        var delay = Math.Max(0, delayBetweenMessagesMs);
        foreach (var command in commands)
        {
            if (command.Length > 0)
            {
                queue.Enqueue((command, delay));
                Total++;
            }
        }
    }

    public void Cancel()
    {
        queue.Clear();
        Total = 0;
    }

    private void OnUpdate(IFramework fw)
    {
        if (queue.Count == 0)
        {
            return;
        }

        // Logged out mid-post: drop the rest rather than posting it from whichever character logs
        // in next.
        if (!clientState.IsLoggedIn)
        {
            Cancel();
            return;
        }

        // Chat input sent during a loading screen can get lost - wait until the zone is loaded.
        if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
        {
            return;
        }

        if (DateTime.UtcNow < nextSendTime)
        {
            return;
        }

        var (command, delayAfterMs) = queue.Dequeue();
        try
        {
            // Last line of defence - everything that builds commands already keeps to this limit,
            // but a line over it must never reach the game's chat box.
            if (command.Length > MessageSplitter.MaxChatLineBytes)
            {
                log.Error($"[CelinesChat] Nachricht mit {command.Length} Bytes verworfen - das Spiel erlaubt hoechstens {MessageSplitter.MaxChatLineBytes}.");
            }
            else
            {
                ChatSender.Send(command);
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, Loc.T("Log.SendError", Encoding.UTF8.GetString(command)));
        }

        if (queue.Count == 0)
        {
            Total = 0;
        }

        nextSendTime = DateTime.UtcNow.AddMilliseconds(delayAfterMs);
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        queue.Clear();
    }
}
