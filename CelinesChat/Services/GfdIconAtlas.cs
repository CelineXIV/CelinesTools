using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;

namespace CelinesChat.Services;

internal static class GfdIconAtlas
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GfdHeader
    {
        public unsafe fixed byte Signature[8];
        public int Count;
        public unsafe fixed byte Padding[4];
    }

    [StructLayout(LayoutKind.Sequential, Size = 0x10)]
    public struct GfdEntry
    {
        public ushort Id;
        public ushort Left;
        public ushort Top;
        public ushort Width;
        public ushort Height;
        public ushort Unk0A;
        public ushort Redirect;
        public ushort Unk0E;

        public readonly bool IsEmpty => Width == 0 || Height == 0;
    }

    private static byte[]? gfdFileBytes;

    public static unsafe bool TryGetEntry(IDataManager dataManager, uint iconId, out GfdEntry entry)
    {
        entry = default;
        if (iconId == 0)
        {
            return false;
        }

        gfdFileBytes ??= dataManager.GetFile("common/font/gfdata.gfd")?.Data;
        if (gfdFileBytes is not { Length: > 0 } bytes)
        {
            return false;
        }

        fixed (byte* ptr = bytes)
        {
            if (bytes.Length < sizeof(GfdHeader))
            {
                return false;
            }

            var header = *(GfdHeader*)ptr;
            var entries = new ReadOnlySpan<GfdEntry>(ptr + sizeof(GfdHeader), header.Count);
            return TryFind(entries, iconId, out entry);
        }
    }

    private static bool TryFind(ReadOnlySpan<GfdEntry> entries, uint iconId, out GfdEntry entry, bool followRedirect = true)
    {
        if (iconId <= entries.Length && entries[(int)(iconId - 1)].Id == iconId)
        {
            entry = entries[(int)(iconId - 1)];
        }
        else if (!BinarySearch(entries, iconId, out entry))
        {
            return false;
        }

        if (followRedirect && entry.Redirect != 0)
        {
            return TryFind(entries, entry.Redirect, out entry, followRedirect: false);
        }

        return !entry.IsEmpty;
    }

    private static bool BinarySearch(ReadOnlySpan<GfdEntry> entries, uint iconId, out GfdEntry entry)
    {
        var lo = 0;
        var hi = entries.Length - 1;
        while (lo <= hi)
        {
            var i = lo + ((hi - lo) >> 1);
            if (entries[i].Id == iconId)
            {
                entry = entries[i];
                return true;
            }

            if (entries[i].Id < iconId)
            {
                lo = i + 1;
            }
            else
            {
                hi = i - 1;
            }
        }

        entry = default;
        return false;
    }
}
