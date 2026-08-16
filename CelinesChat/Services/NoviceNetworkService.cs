using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CelinesChat.Services;

internal static unsafe class NoviceNetworkService
{
    private const string LogPrefix = "[CelinesChat][NoviceNetwork]";

    public static bool IsConnected { get; private set; }

    public static void ObserveSystemMessage(string text)
    {
        if (text.Contains("joined the Novice Network", StringComparison.OrdinalIgnoreCase))
        {
            IsConnected = true;
        }
        else if (text.Contains("left the Novice Network", StringComparison.OrdinalIgnoreCase)
            || (text.Contains("auto-join", StringComparison.OrdinalIgnoreCase) && text.Contains("off", StringComparison.OrdinalIgnoreCase)))
        {
            IsConnected = false;
        }
    }

    public static void Connect(IPluginLog log)
    {
        try
        {
            ChatSender.Send(MessageMarkerEncoder.Encode("/nnetwork on"));
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"{LogPrefix} Konnte den Auto-Beitritt nicht aktivieren.");
        }
    }

    public static void Disconnect(IPluginLog log)
    {
        try
        {
            ChatSender.Send(MessageMarkerEncoder.Encode("/nnetwork off"));
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"{LogPrefix} Konnte den Auto-Beitritt nicht deaktivieren.");
        }
    }

    public static void OpenNativeMemberList(IPluginLog log)
    {
        try
        {
            var agent = AgentChatLog.Instance();
            if (agent == null)
            {
                log.Warning($"{LogPrefix} AgentChatLog nicht verfuegbar.");
                return;
            }

            var value = new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int, Int = 3 };
            var returnValue = default(AtkValue);
            agent->ReceiveEvent(&returnValue, &value, 0, 0);
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"{LogPrefix} Konnte die native Mitgliederliste nicht oeffnen.");
        }
    }
}
