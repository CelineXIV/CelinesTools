using System;
using Dalamud.Plugin.Services;

namespace CelinesToolkit.Services;

public readonly struct SlidecastState
{
    public required float RemainingSeconds { get; init; }

    public required float TotalSeconds { get; init; }

    public required float Progress01 { get; init; }

    public required string ActionName { get; init; }

    public required bool IsInSlidecastWindow { get; init; }
}

/// <summary>
/// Reads the local player's own cast state directly off IBattleChara (via IObjectTable.LocalPlayer,
/// which implements it) - no hooks or client structs needed, Dalamud already exposes IsCasting/
/// CurrentCastTime/TotalCastTime/CastActionId. Shared by both slidecast overlay windows so the
/// remaining-time math and the action-name lookup aren't duplicated between them.
/// </summary>
public sealed class SlidecastService
{
    private readonly IObjectTable objectTable;
    private readonly IDataManager dataManager;

    public SlidecastService(IObjectTable objectTable, IDataManager dataManager)
    {
        this.objectTable = objectTable;
        this.dataManager = dataManager;
    }

    public bool TryGetState(float thresholdSeconds, out SlidecastState state)
    {
        var player = objectTable.LocalPlayer;
        if (player == null || !player.IsCasting || player.TotalCastTime <= 0f)
        {
            state = default;
            return false;
        }

        // Assumes CurrentCastTime counts up from 0 to TotalCastTime - if a real cast shows the bar
        // filling backwards, swap this to CurrentCastTime directly (it would mean the field is
        // actually already the remaining time), see the slidecast plan's verification notes.
        var remaining = player.TotalCastTime - player.CurrentCastTime;
        var progress = player.CurrentCastTime / player.TotalCastTime;

        state = new SlidecastState
        {
            RemainingSeconds = remaining,
            TotalSeconds = player.TotalCastTime,
            Progress01 = System.Math.Clamp(progress, 0f, 1f),
            ActionName = ResolveActionName(player.CastActionId),
            IsInSlidecastWindow = remaining <= thresholdSeconds,
        };
        return true;
    }

    /// <summary>Fakes a looping 2.5s cast so the overlays can be seen and positioned without needing to actually cast something in-game - used only while the settings page's non-persisted "Preview" toggle is on.</summary>
    public static SlidecastState GetPreviewState(float thresholdSeconds)
    {
        const float loopSeconds = 2.5f;
        var elapsed = Environment.TickCount64 % (long)(loopSeconds * 1000) / 1000f;
        var remaining = loopSeconds - elapsed;

        return new SlidecastState
        {
            RemainingSeconds = remaining,
            TotalSeconds = loopSeconds,
            Progress01 = elapsed / loopSeconds,
            ActionName = "Preview",
            IsInSlidecastWindow = remaining <= thresholdSeconds,
        };
    }

    private string ResolveActionName(uint actionId)
    {
        try
        {
            var sheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            var row = sheet.GetRowOrDefault(actionId);
            return row?.Name.ExtractText() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
