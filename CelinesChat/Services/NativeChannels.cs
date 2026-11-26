using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace CelinesChat.Services;

internal static class NativeChannels
{
    private const long CacheLifetimeMs = 1000;

    private static List<(int Number, string Name)>? cachedLinkshells;
    private static long cachedLinkshellsTick;
    private static List<(int Number, string Name)>? cachedCrossWorldLinkshells;
    private static long cachedCrossWorldLinkshellsTick;

    public static List<(int Number, string Name)> GetExistingLinkshells()
    {
        var now = Environment.TickCount64;
        if (cachedLinkshells == null || now - cachedLinkshellsTick >= CacheLifetimeMs)
        {
            cachedLinkshells = ReadExistingLinkshells();
            cachedLinkshellsTick = now;
        }

        return cachedLinkshells;
    }

    public static List<(int Number, string Name)> GetExistingCrossWorldLinkshells()
    {
        var now = Environment.TickCount64;
        if (cachedCrossWorldLinkshells == null || now - cachedCrossWorldLinkshellsTick >= CacheLifetimeMs)
        {
            cachedCrossWorldLinkshells = ReadExistingCrossWorldLinkshells();
            cachedCrossWorldLinkshellsTick = now;
        }

        return cachedCrossWorldLinkshells;
    }

    private static unsafe List<(int Number, string Name)> ReadExistingLinkshells()
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
        }

        return result;
    }

    private static unsafe List<(int Number, string Name)> ReadExistingCrossWorldLinkshells()
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
        }

        return result;
    }

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
