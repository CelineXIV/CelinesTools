using System;
using System.Diagnostics;

namespace C4TChat.Services;

internal sealed class PerfStats
{
    private const int Samples = 120;

    internal sealed class Series
    {
        private readonly double[] values = new double[Samples];
        private int next;
        private int count;

        public void Add(double ms)
        {
            values[next] = ms;
            next = (next + 1) % Samples;
            count = Math.Min(count + 1, Samples);
        }

        public double Average
        {
            get
            {
                if (count == 0)
                {
                    return 0;
                }

                double sum = 0;
                for (var i = 0; i < count; i++)
                {
                    sum += values[i];
                }

                return sum / count;
            }
        }

        public double Max
        {
            get
            {
                double max = 0;
                for (var i = 0; i < count; i++)
                {
                    max = Math.Max(max, values[i]);
                }

                return max;
            }
        }

        public double Last => count == 0 ? 0 : values[(next - 1 + Samples) % Samples];

        public float[] Snapshot()
        {
            var result = new float[count];
            var start = count < Samples ? 0 : next;
            for (var i = 0; i < count; i++)
            {
                result[i] = (float)values[(start + i) % Samples];
            }

            return result;
        }
    }

    public Series ChatWindow { get; } = new();

    public Series SecondaryWindow { get; } = new();

    public Series FrameworkUpdate { get; } = new();

    public int VisibleEntries { get; set; }

    public int CachedHeights { get; set; }

    public static long Start() => Stopwatch.GetTimestamp();

    public static double ElapsedMs(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
}
