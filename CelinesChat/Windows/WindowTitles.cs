namespace CelinesChat.Windows;

internal static class WindowTitles
{
    private const string Brand = "CelinesChat";

    public static string Chat => $"{Brand}##ChatWindow";

    public static string Settings => $"{Brand} - {Loc.T("Window.Settings")}##SettingsWindow";

    public static string Preview => $"{Brand} - {Loc.T("Window.Preview")}##PreviewWindow";
}
