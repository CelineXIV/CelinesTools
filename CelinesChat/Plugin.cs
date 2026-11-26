using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CelinesChat;

public sealed class Plugin : IDalamudPlugin
{
    private const string NewInternalName = "C4TChat";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly WindowSystem windowSystem = new("CelinesChat");
    private readonly RenameWindow window;
    private bool notified;

    public Plugin(IDalamudPluginInterface pluginInterface, IChatGui chatGui, IClientState clientState)
    {
        this.pluginInterface = pluginInterface;
        this.chatGui = chatGui;
        this.clientState = clientState;

        window = new RenameWindow(this) { IsOpen = true };
        windowSystem.AddWindow(window);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += OpenWindow;
        pluginInterface.UiBuilder.OpenConfigUi += OpenWindow;
        clientState.Login += OnLogin;

        if (clientState.IsLoggedIn)
        {
            Notify();
        }
    }

    internal bool German => pluginInterface.UiLanguage.StartsWith("de", StringComparison.OrdinalIgnoreCase);

    internal string T(string de, string en) => German ? de : en;

    internal bool NewPluginLoaded
    {
        get
        {
            foreach (var plugin in pluginInterface.InstalledPlugins)
            {
                if (string.Equals(plugin.InternalName, NewInternalName, StringComparison.OrdinalIgnoreCase) && plugin.IsLoaded)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal void OpenInstallerForNew() => pluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, NewInternalName);

    internal void OpenInstallerForOld() => pluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.InstalledPlugins, "CelinesChat");

    private void OpenWindow() => window.IsOpen = true;

    private void OnLogin() => Notify();

    private void Notify()
    {
        if (notified)
        {
            return;
        }

        notified = true;
        var text = NewPluginLoaded
            ? T("CelinesChat heißt jetzt C4TChat und läuft bereits - du kannst CelinesChat deinstallieren.", "CelinesChat is now C4TChat and it is already running - you can uninstall CelinesChat.")
            : T("CelinesChat heißt jetzt C4TChat! Bitte installiere C4TChat aus dem Plugin-Installer - deine Einstellungen, Chatlogs und Bilder werden automatisch übernommen.", "CelinesChat is now C4TChat! Please install C4TChat from the plugin installer - your settings, chat logs and pictures are carried over automatically.");
        chatGui.Print(new XivChatEntry { Type = XivChatType.Echo, Message = "[CelinesChat] " + text });
    }

    public void Dispose()
    {
        clientState.Login -= OnLogin;
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= OpenWindow;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenWindow;
        windowSystem.RemoveAllWindows();
    }

    private sealed class RenameWindow : Window
    {
        private readonly Plugin plugin;

        public RenameWindow(Plugin plugin)
            : base("CelinesChat → C4TChat###celineschatRename", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
        {
            this.plugin = plugin;
            PositionCondition = ImGuiCond.FirstUseEver;
        }

        public override void Draw()
        {
            var accent = new Vector4(1f, 0.24f, 0.78f, 1f);
            ImGui.TextColored(accent, plugin.T("CelinesChat heißt jetzt C4TChat", "CelinesChat is now C4TChat"));
            ImGui.Spacing();

            using (Dalamud.Interface.Utility.Raii.ImRaii.TextWrapPos(420f))
            {
                if (plugin.NewPluginLoaded)
                {
                    ImGui.TextUnformatted(plugin.T(
                        "C4TChat ist installiert und hat deine Einstellungen, Chatlogs und Bilder übernommen. Du kannst CelinesChat jetzt deinstallieren.",
                        "C4TChat is installed and has carried over your settings, chat logs and pictures. You can uninstall CelinesChat now."));
                }
                else
                {
                    ImGui.TextUnformatted(plugin.T(
                        "Der Chat hat einen neuen Namen und läuft ab sofort als eigenes Plugin. Installiere C4TChat aus dem Plugin-Installer - beim ersten Start übernimmt es automatisch alle Einstellungen, Chatlogs, Notizen und Bilder aus CelinesChat.",
                        "The chat has a new name and now runs as its own plugin. Install C4TChat from the plugin installer - on its first start it carries over all settings, chat logs, notes and pictures from CelinesChat automatically."));
                    ImGui.Spacing();
                    ImGui.TextDisabled(plugin.T(
                        "Danach kannst du CelinesChat deinstallieren.",
                        "Afterwards you can uninstall CelinesChat."));
                }
            }

            ImGui.Spacing();
            if (plugin.NewPluginLoaded)
            {
                if (ImGui.Button(plugin.T("CelinesChat deinstallieren", "Uninstall CelinesChat")))
                {
                    plugin.OpenInstallerForOld();
                }
            }
            else
            {
                if (ImGui.Button(plugin.T("C4TChat installieren", "Install C4TChat")))
                {
                    plugin.OpenInstallerForNew();
                }
            }

            ImGui.SameLine();
            if (ImGui.Button(plugin.T("Schließen", "Close")))
            {
                IsOpen = false;
            }
        }
    }
}
