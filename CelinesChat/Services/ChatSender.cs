using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace CelinesChat.Services;

internal static unsafe class ChatSender
{
    /// <summary>
    /// Sends raw message bytes exactly as given - no UTF8 re-encoding - so a buffer built by
    /// <see cref="MessageMarkerEncoder" /> (plain text mixed with raw SeString payload bytes,
    /// like an embedded auto-translate entry or map link) reaches the game's chat box parser
    /// intact instead of being mangled by a string round-trip.
    ///
    /// Matches Chat2's own SendMessageUnsafe exactly (verified against its source): explicitly
    /// append a trailing 0x00 and use the static Utf8String.FromSequence(ReadOnlySpan&lt;byte&gt;)
    /// helper. An earlier version used the instance Ctor_FromSequence(byte*, length) native call
    /// instead to avoid relying on null-termination at all - that turned out to be the wrong
    /// native function for this (embedded auto-translate payloads sent as empty/garbage), even
    /// though its signature looked like the more "correct", explicit-length option.
    /// </summary>
    public static void Send(byte[] messageBytes)
    {
        if (messageBytes.Length == 0)
        {
            return;
        }

        var uiModule = UIModule.Instance();
        if (uiModule == null)
        {
            return;
        }

        var terminated = new byte[messageBytes.Length + 1];
        messageBytes.CopyTo(terminated, 0);
        terminated[^1] = 0;

        var utf8Message = Utf8String.FromSequence(terminated);
        try
        {
            uiModule->ProcessChatBoxEntry(utf8Message);
        }
        finally
        {
            utf8Message->Dtor(true);
        }
    }
}
