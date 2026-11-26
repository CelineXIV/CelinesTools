using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace C4TChat.Services;

internal static class MapFlagWatcher
{
    public static unsafe bool HasFlag()
    {
        var agentMap = AgentMap.Instance();
        return agentMap != null && agentMap->FlagMarkerCount > 0;
    }
}
