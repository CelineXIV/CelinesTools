using System.Numerics;
using Dalamud.Game.Text;

namespace CelinesRPChat.Services;

internal static class ChannelDisplay
{
    public static string Tag(XivChatType type) => type switch
    {
        XivChatType.Say => "[S]",
        XivChatType.Party => "[P]",
        XivChatType.TellIncoming => "[T]",
        XivChatType.TellOutgoing => "[T]",
        XivChatType.Yell => "[Y]",
        XivChatType.Shout => "[Sh]",
        XivChatType.FreeCompany => "[FC]",
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => "[LS]",
        >= XivChatType.CrossLinkShell1 and <= XivChatType.CrossLinkShell8 => "[CWLS]",
        _ => "[?]",
    };

    public static XivChatType ParseTag(string tag) => tag switch
    {
        "[S]" => XivChatType.Say,
        "[P]" => XivChatType.Party,
        "[T]" => XivChatType.TellIncoming,
        "[Y]" => XivChatType.Yell,
        "[Sh]" => XivChatType.Shout,
        "[FC]" => XivChatType.FreeCompany,
        "[LS]" => XivChatType.Ls1,
        "[CWLS]" => XivChatType.CrossLinkShell1,
        _ => XivChatType.Say,
    };

    public static Vector4 Color(XivChatType type) => type switch
    {
        XivChatType.Say => new Vector4(0.9f, 0.9f, 0.9f, 1f),
        XivChatType.Party => new Vector4(0.4f, 0.8f, 1f, 1f),
        XivChatType.TellIncoming => new Vector4(1f, 0.55f, 0.85f, 1f),
        XivChatType.TellOutgoing => new Vector4(1f, 0.55f, 0.85f, 1f),
        XivChatType.Yell => new Vector4(1f, 0.65f, 0.2f, 1f),
        XivChatType.Shout => new Vector4(1f, 0.4f, 0.2f, 1f),
        XivChatType.FreeCompany => new Vector4(0.5f, 0.9f, 0.5f, 1f),
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => new Vector4(0.4f, 0.9f, 0.85f, 1f),
        >= XivChatType.CrossLinkShell1 and <= XivChatType.CrossLinkShell8 => new Vector4(0.4f, 0.9f, 0.85f, 1f),
        _ => new Vector4(0.8f, 0.8f, 0.8f, 1f),
    };

    public static bool IsVisible(XivChatType type, Configuration config) => type switch
    {
        XivChatType.Say => config.ChatLogShowSay,
        XivChatType.Party => config.ChatLogShowParty,
        XivChatType.TellIncoming => config.ChatLogShowTell,
        XivChatType.TellOutgoing => config.ChatLogShowTell,
        XivChatType.Yell => config.ChatLogShowYell,
        XivChatType.Shout => config.ChatLogShowShout,
        XivChatType.FreeCompany => config.ChatLogShowFreeCompany,
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => config.ChatLogShowLinkshell,
        >= XivChatType.CrossLinkShell1 and <= XivChatType.CrossLinkShell8 => config.ChatLogShowLinkshell,
        _ => false,
    };
}
