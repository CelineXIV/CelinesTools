using System;
using System.Collections.Generic;

namespace C4TChat.Services;

internal static class GifDecoder
{
    internal sealed class Animation
    {
        public int Width { get; init; }

        public int Height { get; init; }

        public List<byte[]> Frames { get; } = new();

        public List<int> DelaysMs { get; } = new();
    }

    internal static bool IsGif(ReadOnlySpan<byte> data) =>
        data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8';

    internal static Animation? Decode(byte[] data, int maxFrames)
    {
        if (data.Length < 13 || !IsGif(data))
        {
            return null;
        }

        var width = data[6] | (data[7] << 8);
        var height = data[8] | (data[9] << 8);
        var packed = data[10];
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
        {
            return null;
        }

        var result = new Animation { Width = width, Height = height };
        var pos = 13;
        byte[]? globalTable = null;
        if ((packed & 0x80) != 0)
        {
            var size = 3 * (1 << ((packed & 7) + 1));
            globalTable = data.AsSpan(pos, Math.Min(size, data.Length - pos)).ToArray();
            pos += size;
        }

        var canvas = new byte[width * height * 4];
        var disposal = 0;
        var delay = 0;
        var transparent = -1;
        try
        {
            while (pos < data.Length && result.Frames.Count < maxFrames)
            {
                var block = data[pos++];
                if (block == 0x3B)
                {
                    break;
                }

                if (block == 0x21)
                {
                    var label = data[pos++];
                    if (label == 0xF9 && data[pos] >= 4)
                    {
                        var flags = data[pos + 1];
                        disposal = (flags >> 2) & 7;
                        delay = (data[pos + 2] | (data[pos + 3] << 8)) * 10;
                        transparent = (flags & 1) != 0 ? data[pos + 4] : -1;
                        pos += data[pos] + 1;
                    }

                    pos = SkipSubBlocks(data, pos);
                    continue;
                }

                if (block != 0x2C)
                {
                    break;
                }

                var fx = data[pos] | (data[pos + 1] << 8);
                var fy = data[pos + 2] | (data[pos + 3] << 8);
                var fw = data[pos + 4] | (data[pos + 5] << 8);
                var fh = data[pos + 6] | (data[pos + 7] << 8);
                var imagePacked = data[pos + 8];
                pos += 9;
                var table = globalTable;
                if ((imagePacked & 0x80) != 0)
                {
                    var size = 3 * (1 << ((imagePacked & 7) + 1));
                    table = data.AsSpan(pos, size).ToArray();
                    pos += size;
                }

                var interlaced = (imagePacked & 0x40) != 0;
                var minCodeSize = data[pos++];
                var compressed = ReadSubBlocks(data, ref pos);
                if (table == null || fw <= 0 || fh <= 0)
                {
                    disposal = 0;
                    delay = 0;
                    transparent = -1;
                    continue;
                }

                var indices = Lzw(compressed, minCodeSize, fw * fh);
                var previous = disposal == 3 ? (byte[])canvas.Clone() : null;
                var rows = interlaced ? InterlacedRows(fh) : null;
                for (var r = 0; r < fh; r++)
                {
                    var y = fy + (rows?[r] ?? r);
                    if (y >= height)
                    {
                        continue;
                    }

                    for (var c = 0; c < fw; c++)
                    {
                        var x = fx + c;
                        if (x >= width)
                        {
                            break;
                        }

                        var index = indices[(r * fw) + c];
                        if (index == transparent || (index * 3) + 2 >= table.Length)
                        {
                            continue;
                        }

                        var o = ((y * width) + x) * 4;
                        canvas[o] = table[index * 3];
                        canvas[o + 1] = table[(index * 3) + 1];
                        canvas[o + 2] = table[(index * 3) + 2];
                        canvas[o + 3] = 255;
                    }
                }

                result.Frames.Add((byte[])canvas.Clone());
                result.DelaysMs.Add(delay < 20 ? 100 : delay);

                if (disposal == 2)
                {
                    for (var y = fy; y < Math.Min(height, fy + fh); y++)
                    {
                        var start = ((y * width) + fx) * 4;
                        var length = Math.Max(0, Math.Min(fw, width - fx)) * 4;
                        Array.Clear(canvas, start, length);
                    }
                }
                else if (disposal == 3 && previous != null)
                {
                    canvas = previous;
                }

                disposal = 0;
                delay = 0;
                transparent = -1;
            }
        }
        catch (IndexOutOfRangeException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return result.Frames.Count > 0 ? result : null;
    }

    internal static byte[] Scale(byte[] source, int width, int height, int newWidth, int newHeight)
    {
        var target = new byte[newWidth * newHeight * 4];
        for (var y = 0; y < newHeight; y++)
        {
            var sy = Math.Min(height - 1, (int)((y + 0.5f) * height / newHeight));
            for (var x = 0; x < newWidth; x++)
            {
                var sx = Math.Min(width - 1, (int)((x + 0.5f) * width / newWidth));
                Buffer.BlockCopy(source, ((sy * width) + sx) * 4, target, ((y * newWidth) + x) * 4, 4);
            }
        }

        return target;
    }

    private static int SkipSubBlocks(byte[] data, int pos)
    {
        while (pos < data.Length)
        {
            var size = data[pos++];
            if (size == 0)
            {
                break;
            }

            pos += size;
        }

        return pos;
    }

    private static byte[] ReadSubBlocks(byte[] data, ref int pos)
    {
        var start = pos;
        var total = 0;
        var scan = pos;
        while (scan < data.Length)
        {
            var size = data[scan++];
            if (size == 0)
            {
                break;
            }

            total += size;
            scan += size;
        }

        var buffer = new byte[total];
        var offset = 0;
        pos = start;
        while (pos < data.Length)
        {
            var size = data[pos++];
            if (size == 0)
            {
                break;
            }

            var copy = Math.Min(size, data.Length - pos);
            Buffer.BlockCopy(data, pos, buffer, offset, copy);
            offset += copy;
            pos += size;
        }

        return buffer;
    }

    private static int[] InterlacedRows(int height)
    {
        var rows = new int[height];
        var i = 0;
        foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
        {
            for (var y = start; y < height && i < height; y += step)
            {
                rows[i++] = y;
            }
        }

        return rows;
    }

    private static byte[] Lzw(byte[] input, int minCodeSize, int pixelCount)
    {
        var output = new byte[pixelCount];
        if (minCodeSize < 1 || minCodeSize > 11)
        {
            return output;
        }

        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var codeSize = minCodeSize + 1;
        var codeMask = (1 << codeSize) - 1;
        var next = clear + 2;
        var prefix = new short[4096];
        var suffix = new byte[4096];
        var stack = new byte[4097];
        for (var i = 0; i < clear; i++)
        {
            suffix[i] = (byte)i;
        }

        var old = -1;
        byte first = 0;
        var outPos = 0;
        var bits = 0;
        var datum = 0;
        var inPos = 0;
        while (outPos < pixelCount)
        {
            while (bits < codeSize)
            {
                if (inPos >= input.Length)
                {
                    return output;
                }

                datum |= input[inPos++] << bits;
                bits += 8;
            }

            var code = datum & codeMask;
            datum >>= codeSize;
            bits -= codeSize;

            if (code == clear)
            {
                codeSize = minCodeSize + 1;
                codeMask = (1 << codeSize) - 1;
                next = clear + 2;
                old = -1;
                continue;
            }

            if (code == end)
            {
                break;
            }

            if (old == -1)
            {
                if (code >= clear)
                {
                    return output;
                }

                output[outPos++] = suffix[code];
                old = code;
                first = (byte)code;
                continue;
            }

            if (code > next)
            {
                return output;
            }

            var current = code;
            var sp = 0;
            if (code == next)
            {
                stack[sp++] = first;
                code = old;
            }

            while (code >= clear)
            {
                if (sp >= 4096)
                {
                    return output;
                }

                stack[sp++] = suffix[code];
                code = prefix[code];
            }

            first = suffix[code];
            stack[sp++] = first;
            while (sp > 0 && outPos < pixelCount)
            {
                output[outPos++] = stack[--sp];
            }

            if (next < 4096)
            {
                prefix[next] = (short)old;
                suffix[next] = first;
                next++;
                if (next > codeMask && codeSize < 12)
                {
                    codeSize++;
                    codeMask = (1 << codeSize) - 1;
                }
            }

            old = current;
        }

        return output;
    }
}
