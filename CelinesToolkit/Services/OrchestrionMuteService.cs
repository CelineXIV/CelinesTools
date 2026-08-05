using System;
using Dalamud.Plugin.Services;

namespace CelinesToolkit.Services;

internal sealed class OrchestrionMuteService : IDisposable
{
    private const int TriggerDelayMs = 3000;
    private const string MuteCommand = "/porch play 1";
    private const string UnmuteCommand = "/porch play 0";

    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly Configuration configuration;

    private string? pendingCommand;
    private DateTime pendingSendTime;

    public OrchestrionMuteService(IClientState clientState, IFramework framework, Configuration configuration)
    {
        this.clientState = clientState;
        this.framework = framework;
        this.configuration = configuration;

        this.framework.Update += OnFrameworkUpdate;
        this.clientState.Login += OnLogin;
        this.clientState.TerritoryChanged += OnTerritoryChanged;

        if (this.configuration.MuteOrchestrion && this.clientState.IsLoggedIn)
        {
            Queue(MuteCommand, TriggerDelayMs);
        }
    }

    public void ApplyNow(bool muted)
    {
        Queue(muted ? MuteCommand : UnmuteCommand, 0);
    }

    private void OnLogin()
    {
        if (configuration.MuteOrchestrion)
        {
            Queue(MuteCommand, TriggerDelayMs);
        }
    }

    private void OnTerritoryChanged(uint territoryType)
    {
        if (configuration.MuteOrchestrion)
        {
            Queue(MuteCommand, TriggerDelayMs);
        }
    }

    private void Queue(string command, int delayMs)
    {
        pendingCommand = command;
        pendingSendTime = DateTime.UtcNow.AddMilliseconds(delayMs);
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        if (pendingCommand == null || DateTime.UtcNow < pendingSendTime)
        {
            return;
        }

        ChatSender.Send(pendingCommand);
        pendingCommand = null;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        clientState.Login -= OnLogin;
        clientState.TerritoryChanged -= OnTerritoryChanged;
    }
}
