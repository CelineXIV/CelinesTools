using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace CelinesToolkit.Services;

internal sealed class MacroRunner : IDisposable
{
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Queue<string> queue = new();
    private DateTime nextSendTime = DateTime.MinValue;
    private int delayMs = 600;

    public MacroRunner(IFramework framework, IPluginLog log)
    {
        this.framework = framework;
        this.log = log;
        this.framework.Update += OnUpdate;
    }

    public void Run(MacroSequence macro, int delayBetweenCommandsMs)
    {
        if (macro.Commands.Count == 0)
        {
            return;
        }

        delayMs = Math.Max(0, delayBetweenCommandsMs);
        queue.Clear();
        EnqueueCommands(macro);
        nextSendTime = DateTime.UtcNow;
    }

    public void RunMany(IEnumerable<MacroSequence> macros, int delayBetweenCommandsMs, int initialDelayMs)
    {
        delayMs = Math.Max(0, delayBetweenCommandsMs);
        queue.Clear();

        foreach (var macro in macros)
        {
            EnqueueCommands(macro);
        }

        if (queue.Count == 0)
        {
            return;
        }

        nextSendTime = DateTime.UtcNow.AddMilliseconds(Math.Max(0, initialDelayMs));
    }

    private void EnqueueCommands(MacroSequence macro)
    {
        foreach (var command in macro.Commands)
        {
            if (!string.IsNullOrWhiteSpace(command.Text))
            {
                queue.Enqueue(command.Text);
            }
        }
    }

    public void Stop()
    {
        queue.Clear();
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

        nextSendTime = DateTime.UtcNow.AddMilliseconds(delayMs);
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        queue.Clear();
    }
}
