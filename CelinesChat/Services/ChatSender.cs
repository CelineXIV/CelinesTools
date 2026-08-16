using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace CelinesChat.Services;

internal static unsafe class ChatSender
{
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
