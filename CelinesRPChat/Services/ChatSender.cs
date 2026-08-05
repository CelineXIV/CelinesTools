using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace CelinesRPChat.Services;

internal static unsafe class ChatSender
{
    public static void Send(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var uiModule = UIModule.Instance();
        if (uiModule == null)
        {
            return;
        }

        var utf8Message = Utf8String.FromString(message);
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
