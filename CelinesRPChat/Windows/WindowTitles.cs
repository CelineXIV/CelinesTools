namespace CelinesRPChat.Windows;

internal static class WindowTitles
{
    private const string Brand = "CelinesRPChat";

    public static string Compose => $"{Brand}##ComposeWindow";

    public static string Settings => $"{Brand} - {Loc.T("Window.Settings")}##SettingsWindow";

    public static string Read => $"{Brand} - {Loc.T("Window.Read")}##ChatLogWindow";
}
