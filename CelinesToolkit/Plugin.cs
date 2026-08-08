using System;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using CelinesToolkit.Services;
using CelinesToolkit.Services.HousingTracker;
using CelinesToolkit.Services.ShoppingList;
using CelinesToolkit.Windows;

namespace CelinesToolkit;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/celinestoolkit";
    private const string RunCommandName = "/ctrun";
    private const string GlamourerPreviewCommandName = "/ctglamourer";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly MacroRunner runner;
    private readonly OrchestrionMuteService orchestrionMuteService;
    private readonly WindowSystem windowSystem = new("CelinesToolkit");
    private readonly MainWindow mainWindow;
    private readonly QuickBarWindow quickBarWindow;
    private readonly CommandInfo openCommandInfo;
    private readonly CommandInfo runCommandInfo;
    private readonly CommandInfo glamourerPreviewCommandInfo;
    private readonly PenumbraPanelIntegration penumbraPanelIntegration;
    private readonly UniversalisClient universalisClient;
    private readonly PaissaClient paissaClient;
    private readonly GlamourerIpcService glamourerIpcService;
    private readonly GlamourerPreviewWindow glamourerPreviewWindow;
    private readonly SlidecastCastBarWindow slidecastCastBarWindow;
    private readonly SlidecastCursorOverlayWindow slidecastCursorOverlayWindow;

    private const int LoginInitialDelayMs = 3000;

    public Configuration Configuration { get; }

    public ModPreviewScanner ModPreviewScanner { get; }

    public PreviewImageService PreviewImageService { get; }

    public PreviewTextureCache PreviewTextureCache { get; }

    public ItemLookupService ItemLookupService { get; }

    public ShoppingListPricingService ShoppingListPricingService { get; }

    public HousingTrackerService HousingTrackerService { get; }

    public FileDialogManager FileDialogManager { get; } = new();

    /// <summary>Non-persisted, runtime-only: fakes a looping cast so the slidecast overlays can be positioned without needing to actually cast in-game. Toggled from SlidecastPage.</summary>
    public bool SlidecastPreviewMode { get; set; }

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog log,
        IClientState clientState,
        IFramework framework,
        ITextureProvider textureProvider,
        ICondition condition,
        IDataManager dataManager,
        IObjectTable objectTable,
        ITargetManager targetManager)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.log = log;
        this.clientState = clientState;

        Loc.SetLanguage(this.pluginInterface.UiLanguage);

        Configuration = this.pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        runner = new MacroRunner(framework, log);
        orchestrionMuteService = new OrchestrionMuteService(clientState, framework, Configuration);
        var penumbraIpc = new PenumbraIpcService(this.pluginInterface);
        ModPreviewScanner = new ModPreviewScanner(penumbraIpc);
        PreviewImageService = new PreviewImageService();
        PreviewTextureCache = new PreviewTextureCache(textureProvider);
        penumbraPanelIntegration = new PenumbraPanelIntegration(this.pluginInterface, penumbraIpc, PreviewTextureCache, Configuration);
        ItemLookupService = new ItemLookupService(dataManager, log);
        universalisClient = new UniversalisClient(log);
        ShoppingListPricingService = new ShoppingListPricingService(ItemLookupService, universalisClient, objectTable);
        paissaClient = new PaissaClient(log);
        HousingTrackerService = new HousingTrackerService(paissaClient, objectTable);
        glamourerIpcService = new GlamourerIpcService(this.pluginInterface);

        mainWindow = new MainWindow(this, clientState);
        windowSystem.AddWindow(mainWindow);

        quickBarWindow = new QuickBarWindow(this, this.pluginInterface, clientState, condition);
        windowSystem.AddWindow(quickBarWindow);
        quickBarWindow.IsOpen = Configuration.QuickBarEnabled;

        glamourerPreviewWindow = new GlamourerPreviewWindow(this.pluginInterface, glamourerIpcService, objectTable, targetManager, PreviewTextureCache, PreviewImageService, FileDialogManager);
        windowSystem.AddWindow(glamourerPreviewWindow);

        var slidecastService = new SlidecastService(objectTable, dataManager);
        slidecastCastBarWindow = new SlidecastCastBarWindow(this, slidecastService);
        windowSystem.AddWindow(slidecastCastBarWindow);
        slidecastCastBarWindow.IsOpen = true;

        slidecastCursorOverlayWindow = new SlidecastCursorOverlayWindow(this, slidecastService);
        windowSystem.AddWindow(slidecastCursorOverlayWindow);
        slidecastCursorOverlayWindow.IsOpen = true;

        openCommandInfo = new CommandInfo(OnOpenCommand) { HelpMessage = Loc.T("Command.Help.Open") };
        runCommandInfo = new CommandInfo(OnRunCommand) { HelpMessage = Loc.T("Command.Help.Run") };
        glamourerPreviewCommandInfo = new CommandInfo(OnGlamourerPreviewCommand) { HelpMessage = Loc.T("Command.Help.GlamourerPreview") };
        this.commandManager.AddHandler(CommandName, openCommandInfo);
        this.commandManager.AddHandler(RunCommandName, runCommandInfo);
        this.commandManager.AddHandler(GlamourerPreviewCommandName, glamourerPreviewCommandInfo);

        // Dalamud auto-hides a plugin's entire UI during GPose by default - opted out of here so
        // GlamourerPreviewWindow can stay usable there (browsing/applying designs while posing is
        // exactly when you'd want it). The other windows didn't ask for that, so they each
        // explicitly re-check IClientState.IsGPosing in their own DrawConditions to keep their
        // prior (hidden-during-gpose) behavior instead of all inheriting this opt-out for free.
        this.pluginInterface.UiBuilder.DisableGposeUiHide = true;

        this.pluginInterface.UiBuilder.Draw += DrawUi;
        this.pluginInterface.UiBuilder.Draw += FileDialogManager.Draw;
        this.pluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        this.pluginInterface.UiBuilder.OpenConfigUi += ToggleMainWindow;
        this.clientState.Login += OnLogin;
        this.pluginInterface.LanguageChanged += OnLanguageChanged;
    }

    public void RunMacro(MacroSequence macro) => runner.Run(macro, Configuration.DelayMs);

    public void SetOrchestrionMute(bool muted) => orchestrionMuteService.ApplyNow(muted);

    public void SetQuickBarEnabled(bool enabled)
    {
        Configuration.QuickBarEnabled = enabled;
        SaveConfiguration();
        quickBarWindow.IsOpen = enabled;
    }

    public void SetGlamourerPreviewEnabled(bool enabled)
    {
        Configuration.GlamourerPreviewEnabled = enabled;
        SaveConfiguration();
        glamourerPreviewWindow.IsOpen = enabled;
    }

    public void SaveConfiguration() => pluginInterface.SavePluginConfig(Configuration);

    private void OnOpenCommand(string command, string args) => mainWindow.Toggle();

    private void OnGlamourerPreviewCommand(string command, string args) => glamourerPreviewWindow.Toggle();

    private void OnRunCommand(string command, string args)
    {
        var name = args.Trim();
        if (string.IsNullOrEmpty(name))
        {
            log.Warning(Loc.T("Log.NoNameGiven"));
            return;
        }

        var macro = Configuration.Macros.Find(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        if (macro == null)
        {
            log.Warning(Loc.T("Log.MacroNotFound", name));
            return;
        }

        RunMacro(macro);
    }

    private void OnLogin()
    {
        var loginMacros = Configuration.Macros.FindAll(m => m.RunOnLogin);
        if (loginMacros.Count > 0)
        {
            runner.RunMany(loginMacros, Configuration.DelayMs, LoginInitialDelayMs);
        }

        if (Configuration.QuickBarEnabled)
        {
            quickBarWindow.IsOpen = true;
        }
    }

    private void OnLanguageChanged(string langCode)
    {
        Loc.SetLanguage(langCode);
        openCommandInfo.HelpMessage = Loc.T("Command.Help.Open");
        runCommandInfo.HelpMessage = Loc.T("Command.Help.Run");
        glamourerPreviewCommandInfo.HelpMessage = Loc.T("Command.Help.GlamourerPreview");
    }

    private void DrawUi() => windowSystem.Draw();

    private void ToggleMainWindow() => mainWindow.Toggle();

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= DrawUi;
        pluginInterface.UiBuilder.Draw -= FileDialogManager.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleMainWindow;
        clientState.Login -= OnLogin;
        pluginInterface.LanguageChanged -= OnLanguageChanged;

        windowSystem.RemoveAllWindows();

        commandManager.RemoveHandler(CommandName);
        commandManager.RemoveHandler(RunCommandName);
        commandManager.RemoveHandler(GlamourerPreviewCommandName);

        runner.Dispose();
        orchestrionMuteService.Dispose();
        PreviewImageService.Dispose();
        PreviewTextureCache.Dispose();
        penumbraPanelIntegration.Dispose();
        universalisClient.Dispose();
        paissaClient.Dispose();
    }
}
