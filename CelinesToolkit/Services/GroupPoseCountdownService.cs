using System;
using System.Collections.Generic;
using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.Shell;

namespace CelinesToolkit.Services;

/// <summary>
/// Watches every chat message (any channel, any sender - including your own outgoing messages, so
/// the person who calls the codeword also poses) for configured codewords and runs the matching
/// command when one appears. Built for synchronized group RP poses: everyone who has the same
/// codeword configured plays their own chosen emote at the same moment, whoever says it.
///
/// Commands are executed via ShellCommandModule.ExecuteCommandInner - the game's own chatbox command
/// dispatcher (the same one invoked when you type something and press Enter) - rather than Dalamud's
/// ICommandManager.ProcessCommand, which only reaches commands OTHER PLUGINS registered with Dalamud
/// and never sees native game commands like "/emote wave" or "/dance". The first build used
/// ProcessCommand and silently did nothing for exactly that reason.
/// </summary>
internal sealed unsafe class GroupPoseCountdownService : IDisposable
{
    private const string LogPrefix = "[CelinesToolkit][GroupPoseCountdown]";

    /// <summary>Guards against a single chat line re-triggering the same codeword repeatedly (e.g. it appearing again moments later in a fast-scrolling channel).</summary>
    private const long CooldownMs = 3000;

    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly Dictionary<string, long> lastTriggeredTicks = new(StringComparer.OrdinalIgnoreCase);

    private bool listenerRegistered;

    public GroupPoseCountdownService(IChatGui chatGui, IPluginLog log, Configuration configuration)
    {
        this.chatGui = chatGui;
        this.log = log;
        this.configuration = configuration;
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled == listenerRegistered)
        {
            return;
        }

        if (enabled)
        {
            chatGui.ChatMessage += OnChatMessage;
            listenerRegistered = true;
            log.Info($"{LogPrefix} Enabled.");
        }
        else
        {
            chatGui.ChatMessage -= OnChatMessage;
            lastTriggeredTicks.Clear();
            listenerRegistered = false;
            log.Info($"{LogPrefix} Disabled.");
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!configuration.GroupPoseCountdownEnabled)
        {
            return;
        }

        try
        {
            var text = message.Message.TextValue;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            foreach (var trigger in configuration.GroupPoseCountdownTriggers)
            {
                if (string.IsNullOrWhiteSpace(trigger.Codeword) || string.IsNullOrWhiteSpace(trigger.Command))
                {
                    continue;
                }

                if (!text.Contains(trigger.Codeword, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (lastTriggeredTicks.TryGetValue(trigger.Codeword, out var lastTicks) && Environment.TickCount64 - lastTicks < CooldownMs)
                {
                    continue;
                }

                lastTriggeredTicks[trigger.Codeword] = Environment.TickCount64;
                log.Info($"{LogPrefix} Codeword \"{trigger.Codeword}\" detected - running \"{trigger.Command}\".");
                ExecuteChatCommand(trigger.Command);
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, $"{LogPrefix} Failed while checking a chat message for codewords.");
        }
    }

    private void ExecuteChatCommand(string command)
    {
        var shellModule = RaptureShellModule.Instance();
        if (shellModule == null)
        {
            log.Warning($"{LogPrefix} RaptureShellModule not available - could not run \"{command}\".");
            return;
        }

        var chatCommand = new Utf8String();
        chatCommand.Ctor();
        try
        {
            chatCommand.SetString(command);
            ((ShellCommandModule*)shellModule)->ExecuteCommandInner(&chatCommand, shellModule->UIModule);
        }
        finally
        {
            chatCommand.Dtor();
        }
    }

    public void Dispose() => SetEnabled(false);
}
