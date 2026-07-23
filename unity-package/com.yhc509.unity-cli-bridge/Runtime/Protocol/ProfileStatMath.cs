#nullable enable
using System;

namespace UnityCli.Protocol
{
    /// <summary>Pure statistics helpers for profile summaries. Editor- and .NET-safe.</summary>
    public static class ProfileStatMath
    {
        public static double Median(double[] values)
        {
            if (values == null || values.Length == 0)
            {
                return 0.0;
            }

            double[] sorted = (double[])values.Clone();
            Array.Sort(sorted);
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        /// <summary>Nearest-rank percentile. Input must be sorted ascending.</summary>
        public static double Percentile(double[] sortedAscending, double percentile)
        {
            if (sortedAscending == null || sortedAscending.Length == 0)
            {
                return 0.0;
            }

            int rank = (int)Math.Ceiling(percentile / 100.0 * sortedAscending.Length);
            int index = Math.Min(Math.Max(rank - 1, 0), sortedAscending.Length - 1);
            return sortedAscending[index];
        }

        public static ProfileFrameTimeStats BuildFrameTimeStats(double[] frameMs, float budgetMs)
        {
            var stats = new ProfileFrameTimeStats();
            if (frameMs == null || frameMs.Length == 0)
            {
                return stats;
            }

            double worst = double.MinValue;
            int worstFrame = -1;
            int overBudget = 0;
            for (int index = 0; index < frameMs.Length; index++)
            {
                if (frameMs[index] > worst)
                {
                    worst = frameMs[index];
                    worstFrame = index;
                }

                if (frameMs[index] > budgetMs)
                {
                    overBudget++;
                }
            }

            double[] sorted = (double[])frameMs.Clone();
            Array.Sort(sorted);

            stats.medianMs = Median(frameMs);
            stats.p95Ms = Percentile(sorted, 95);
            stats.worstMs = worst;
            stats.worstFrame = worstFrame;
            stats.overBudgetCount = overBudget;
            return stats;
        }

        public static ProfileVerdict ComputeVerdict(
            double gpuMedianMs,
            double cpuMedianMs,
            bool sawGpuWaitMarker,
            int overBudgetCount)
        {
            var verdict = new ProfileVerdict
            {
                gpuMedianMs = gpuMedianMs > 0 ? gpuMedianMs : -1,
            };

            if (gpuMedianMs > 0)
            {
                verdict.basis = "gpuTime";
            }
            else if (sawGpuWaitMarker)
            {
                verdict.basis = "waitMarkers";
            }
            else
            {
                verdict.basis = "none";
            }

            if (overBudgetCount == 0)
            {
                verdict.bound = "withinBudget";
                return verdict;
            }

            if (gpuMedianMs > 0)
            {
                verdict.bound = gpuMedianMs >= cpuMedianMs ? "gpu" : "cpu";
                return verdict;
            }

            verdict.bound = sawGpuWaitMarker ? "gpu" : "cpu";
            return verdict;
        }
    }
}
