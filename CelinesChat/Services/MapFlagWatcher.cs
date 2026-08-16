using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace CelinesChat.Services;

internal static class MapFlagWatcher
{
    public static unsafe bool HasFlag()
    {
        var agentMap = AgentMap.Instance();
        return agentMap != null && agentMap->FlagMarkerCount > 0;
    }
}
