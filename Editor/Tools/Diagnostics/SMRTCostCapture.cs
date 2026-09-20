using System;
using System.Collections.Generic;
using Unity.Collections;

namespace VividRP.Editor
{
    // CPU reduction of an explicit one-frame replay, never a frame-loop service.
    // Keep this ABI in sync with VSM_COST_ADD and VSMReceiverCost.
    internal static class SMRTCostCapture
    {
        internal const int CounterCount = 32;
        internal static readonly string[] Names =
        {
            "receivers", "smrtProjectionAttempts", "smrtProjectionRetries", "transitionAttempts",
            "footprintCalls", "footprintLevels", "footprintPageChecks", "footprintFailures",
            "rays", "clipmapSegments", "ddaCells", "ddaPageResolves",
            "ddaPageCacheHits", "unavailableRays", "unoccludedTails", "blockedRays",
            "staticFrontLoads", "dynamicFrontLoads", "staticHiddenLoads", "dynamicHiddenLoads",
            "staticPoolSkips", "dynamicPoolSkips", "pcfFallbacks", "pcfTapAttempts",
            "terminalUnavailable", "transitionSuccesses", "rayBudgetFailures", "gapHits",
            "rayPageFailures", "pcfProjectionAttempts", "exactShadowDifferences", "shadowDifferencesOverTolerance"
        };

        [Serializable]
        internal sealed class Metric
        {
            public string name;
            public long total;
            public int activePixels;
            public double meanPerReceiver;
            public uint p50, p95, p99, max, activeP95;
        }

        [Serializable]
        internal sealed class Report
        {
            public int schemaVersion = 1, width, height, receivers;
            public long bufferBytes, exactShadowDifferences, shadowDifferencesOverTolerance;
            public string scope = "Exact per-pixel operation counts from one raw pre-denoise replay; not GPU time or cache misses. Percentiles include zero-work receivers, exclude sky; activeP95 excludes zero work. Loads count scalar pool.Load calls, including PCF fallback, not memory transactions. Footprint checks may terminate early. This replay changes neither quality parameters nor production history.";
            public Metric[] metrics;
        }

        internal static Report Summarize(NativeArray<uint> data, int width, int height)
        {
            int pixels = checked(width * height);
            if (data.Length != checked(pixels * CounterCount)) throw new ArgumentException("SMRT counter buffer size mismatch.");
            var report = new Report { width = width, height = height, bufferBytes = (long)data.Length * sizeof(uint), metrics = new Metric[CounterCount] };
            for (int pixel = 0; pixel < pixels; pixel++)
            {
                int offset = pixel * CounterCount;
                if (data[offset] != 0) report.receivers++;
                report.exactShadowDifferences += data[offset + 30];
                report.shadowDifferencesOverTolerance += data[offset + 31];
            }
            var histogram = new Dictionary<uint, int>();
            var values = new List<uint>();
            for (int counter = 0; counter < CounterCount; counter++)
            {
                histogram.Clear(); values.Clear();
                var metric = new Metric { name = Names[counter] };
                for (int pixel = 0; pixel < pixels; pixel++)
                {
                    int offset = pixel * CounterCount;
                    if (data[offset] == 0) continue;
                    uint value = data[offset + counter];
                    metric.total += value;
                    if (value != 0) metric.activePixels++;
                    metric.max = Math.Max(metric.max, value);
                    histogram.TryGetValue(value, out int count); histogram[value] = count + 1;
                }
                values.AddRange(histogram.Keys); values.Sort();
                metric.meanPerReceiver = report.receivers == 0 ? 0 : (double)metric.total / report.receivers;
                metric.p50 = Percentile(histogram, values, report.receivers, 0.50, false);
                metric.p95 = Percentile(histogram, values, report.receivers, 0.95, false);
                metric.p99 = Percentile(histogram, values, report.receivers, 0.99, false);
                metric.activeP95 = Percentile(histogram, values, metric.activePixels, 0.95, true);
                report.metrics[counter] = metric;
            }
            return report;
        }

        private static uint Percentile(Dictionary<uint, int> histogram, List<uint> values, int count, double fraction, bool skipZero)
        {
            if (count == 0) return 0;
            int target = (int)Math.Ceiling(count * fraction), cumulative = 0;
            for (int i = 0; i < values.Count; i++)
            {
                uint value = values[i];
                if (skipZero && value == 0) continue;
                cumulative += histogram[value];
                if (cumulative >= target) return value;
            }
            return 0;
        }
    }
}
