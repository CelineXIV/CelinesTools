using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CelinesChat.Services;

/// <summary>
/// Intercepts the native ChatLog addon's own "please activate/prefill the chat box with this
/// text" dispatch - the same mechanism vanilla FFXIV uses for map-flag placement (Ctrl+Right-click
/// on the map inserts the literal placeholder "&lt;flag&gt;") and item links (inventory right-click
/// -&gt; Link). There is no Dalamud-level event for this and no FFXIVClientStructs wrapper for it
/// either - this exists only because Chat2 hooks the exact same native function for the exact same
/// reason (GameFunctions/Chat.cs, ChatLogRefreshHook/ChatLogRefreshDetour - verified against their
/// real source, not guessed): the native ChatLog addon's OnRefresh handler, found via a raw byte
/// signature since it isn't otherwise named/exposed.
///
/// Without this, these native "insert into whichever chat box is active" actions have no chat box
/// to find, since our own compose box is a custom ImGui widget the game has no way to know about -
/// they'd either silently do nothing or target the (hidden) native chat log, never our own.
///
/// Returning 1 (instead of calling through to the original) stops the native ChatLog addon from
/// also trying to handle the refresh - it's already hidden (see Plugin.SetNativeChatVisible), but
/// letting it process this anyway risks it re-showing itself to display the prefilled text, which
/// would undo that.
/// </summary>
internal sealed unsafe class ChatActivationWatcher : IDisposable
{
    private delegate byte ChatLogRefreshDelegate(nint log, ushort eventId, AtkValue* value);

    // Signature copied verbatim from Chat2's own ChatLogRefreshHook - this resolves "Client::UI::
    // AddonChatLog.OnRefresh" by byte pattern (there's no named FFXIVClientStructs member for it),
    // so matching their already-verified pattern exactly is safer than attempting a fresh scan.
    [Signature("40 53 57 41 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 4D 8B F8", DetourName = nameof(ChatLogRefreshDetour))]
    private Hook<ChatLogRefreshDelegate>? chatLogRefreshHook = null!;

    /// <summary>
    /// Fires with the literal text the game wanted to pre-fill into a chat box (e.g. "&lt;flag&gt;",
    /// or an item link's text form) - consumed the same way as Plugin.PendingWhisperTarget.
    /// </summary>
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
        // eventId 0x31 with value[0].UInt of 0x05/0x0C is specifically the "activate chat with
        // this prefill text" refresh call - every other refresh (tab switches, normal message
        // display, ...) falls through to the original untouched.
        if (eventId != 0x31 || value == null || value->UInt is not (0x05 or 0x0C))
        {
            return chatLogRefreshHook!.Original(log, eventId, value);
        }

        // The prefill text (if any) is the third AtkValue in this call's argument list.
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
