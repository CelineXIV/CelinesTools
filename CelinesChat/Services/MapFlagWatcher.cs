using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace CelinesChat.Services;

/// <summary>
/// Reads whether a map flag is currently placed, straight from AgentMap
/// (<see cref="AgentMap.FlagMarkerCount"/>) - used only for the manual "insert current flag"
/// compose menu entry, which covers the case where <see cref="ChatActivationWatcher"/>'s
/// automatic capture missed its moment (e.g. the compose box gained focus after the flag was
/// already placed). The automatic path doesn't need this at all - it reacts to the native
/// activation event directly, which already carries the "&lt;flag&gt;" placeholder text itself.
/// </summary>
internal static class MapFlagWatcher
{
    public static unsafe bool HasFlag()
    {
        var agentMap = AgentMap.Instance();
        return agentMap != null && agentMap->FlagMarkerCount > 0;
    }
}
