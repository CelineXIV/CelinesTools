using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace CelinesChat.Services;

/// <summary>
/// Reads which linkshells/cross-world linkshells the current character actually belongs to,
/// with their real names, straight from the game's own info proxies - the same data source the
/// native chat-channel picker itself uses. Never throws: a native lookup failing just means the
/// channel picker shows nothing for that section instead of taking the plugin down with it.
/// </summary>
internal static class NativeChannels
{
    public static unsafe List<(int Number, string Name)> GetExistingLinkshells()
    {
        var result = new List<(int, string)>();
        try
        {
            var proxy = InfoProxyLinkshell.Instance();
            if (proxy == null)
            {
                return result;
            }

            for (var i = 0; i < proxy->LinkShells.Length; i++)
            {
                var id = proxy->LinkShells[i].Id;
                if (id == 0)
                {
                    continue;
                }

                var name = proxy->GetLinkshellName(id).ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    result.Add((i + 1, name));
                }
            }
        }
        catch
        {
            // Best-effort only - see class summary.
        }

        return result;
    }

    public static unsafe List<(int Number, string Name)> GetExistingCrossWorldLinkshells()
    {
        var result = new List<(int, string)>();
        try
        {
            var proxy = InfoProxyCrossWorldLinkshell.Instance();
            if (proxy == null)
            {
                return result;
            }

            var shells = proxy->CrossWorldLinkshells;
            for (var i = 0; i < shells.Length; i++)
            {
                var name = shells[i].Name.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    result.Add((i + 1, name));
                }
            }
        }
        catch
        {
            // Best-effort only - see class summary.
        }

        return result;
    }

    /// <summary>
    /// All 8 numbered slots, with a real name filled in wherever <paramref name="existing" /> has
    /// one - unlike the send-channel picker (which only lists ones you can actually send to
    /// right now), settings like per-linkshell colors and per-tab visibility are legitimately
    /// useful to configure in advance even for a linkshell you're not currently in, so those
    /// pages show every slot regardless.
    /// </summary>
    public static List<(int Number, string Name)> AllSlots(List<(int Number, string Name)> existing)
    {
        var namesByNumber = new Dictionary<int, string>();
        foreach (var (number, name) in existing)
        {
            namesByNumber[number] = name;
        }

        var result = new List<(int, string)>(8);
        for (var i = 1; i <= 8; i++)
        {
            result.Add((i, namesByNumber.TryGetValue(i, out var name) ? name : string.Empty));
        }

        return result;
    }
}
