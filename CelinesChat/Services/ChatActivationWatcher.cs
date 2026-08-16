using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CelinesChat.Services;

internal sealed unsafe class ChatActivationWatcher : IDisposable
{
    private delegate byte ChatLogRefreshDelegate(nint log, ushort eventId, AtkValue* value);

    [Signature("40 53 57 41 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 4D 8B F8", DetourName = nameof(ChatLogRefreshDetour))]
    private Hook<ChatLogRefreshDelegate>? chatLogRefreshHook = null!;

    public event Action<string>? TextToInsert;

    public ChatActivationWatcher(IGameInteropProvider gameInteropProvider, IPluginLog log)
    {
        try
        {
            gameInteropProvider.InitializeFromAttributes(this);
            chatLogRefreshHook?.Enable();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[CelinesChat] Konnte den ChatLog-OnRefresh-Hook nicht einrichten - Kartenmarkierungen und Item-Links landen dann nicht automatisch in der Texteingabe.");
        }
    }

    private byte ChatLogRefreshDetour(nint log, ushort eventId, AtkValue* value)
    {
        if (eventId != 0x31 || value == null || value->UInt is not (0x05 or 0x0C))
        {
            return chatLogRefreshHook!.Original(log, eventId, value);
        }

        var str = value + 2;
        if (str != null && ((int)str->Type & 0xF) == (int)AtkValueType.String && str->String.HasValue)
        {
            var text = str->String.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                TextToInsert?.Invoke(text);
            }
        }

        return 1;
    }

    public void Dispose() => chatLogRefreshHook?.Dispose();
}
