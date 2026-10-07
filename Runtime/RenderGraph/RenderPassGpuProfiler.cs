using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine.Profiling;

namespace VividRP.Runtime
{
    // Counts only the outer Record scope of each pass. Nested scopes are deliberately excluded.
    internal static class RenderPassGpuProfiler
    {
        internal const string CategoryName = "VividRP GPU";
        internal const string PassPrefix = "VividRP GPU Pass/";
        internal const string SamplesPrefix = "VividRP GPU Samples/";
        internal const string ValidSamplesName = "VividRP GPU Valid Samples";
        internal static readonly ProfilerCategory Category = new(CategoryName);
        internal static readonly string[] GroupNames =
        {
            "Geometry", "Shadows", "Direct Lighting", "Indirect Lighting", "Sky and Volumetrics",
            "Virtual Texture", "Ray Tracing", "Post Processing", "Resources and Output", "Debug and Unclassified"
        };

        private const ProfilerCounterOptions CounterOptions =
            ProfilerCounterOptions.FlushOnEndOfFrame | ProfilerCounterOptions.ResetToZeroOnFlush;
        private static readonly ProfilerCounterValue<long>[] s_Groups = CreateGroupCounters();
        private static ProfilerCounterValue<long> s_ValidSamples =
            new(Category, ValidSamplesName, ProfilerMarkerDataUnit.Count, CounterOptions);
        private static readonly Dictionary<string, Entry> s_ByMarker = new(StringComparer.Ordinal);
        private static readonly List<Entry> s_Entries = new();
        private static bool s_Recording;

        private sealed class Entry
        {
            internal readonly string MarkerName;
            internal readonly int Group;
            internal ProfilerRecorder Recorder;
            internal ProfilerCounterValue<long> Time;
            internal ProfilerCounterValue<long> Samples;

            internal Entry(string markerName, string displayName, int group)
            {
                MarkerName = markerName;
                Group = group;
                string suffix = GroupNames[group] + "/" + displayName;
                Time = new ProfilerCounterValue<long>(Category, PassPrefix + suffix,
                    ProfilerMarkerDataUnit.TimeNanoseconds, CounterOptions);
                Samples = new ProfilerCounterValue<long>(Category, SamplesPrefix + suffix,
                    ProfilerMarkerDataUnit.Count, CounterOptions);
            }

            internal void Start()
            {
                // Own a GPU-only recorder; do not alter the sampler's shared CPU/GPU recorder state.
                Recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, MarkerName, 1,
                    ProfilerRecorderOptions.StartImmediately |
                    ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                    ProfilerRecorderOptions.SumAllSamplesInFrame |
                    ProfilerRecorderOptions.GpuRecorder);
            }

            internal void Stop()
            {
                if (Recorder.Valid)
                    Recorder.Dispose();
                Recorder = default;
                Time.Value = 0;
                Samples.Value = 0;
            }
        }

        internal static void Register(IRenderPass pass, RenderPassProfilerMarkers markers)
        {
#if ENABLE_PROFILER
            // Unity aggregates identical marker names across cameras and sampler instances.
            // Register each name once, otherwise that aggregate would be added multiple times.
            if (s_ByMarker.ContainsKey(markers.Record.name))
                return;
            var entry = new Entry(markers.Record.name, markers.DisplayName, Classify(pass?.GetType()));
            s_ByMarker.Add(entry.MarkerName, entry);
            s_Entries.Add(entry);
            if (s_Recording)
                entry.Start();
#endif
        }

        internal static void Collect()
        {
#if ENABLE_PROFILER
            bool enabled = Profiler.enabled && Profiler.IsCategoryEnabled(Category);
            if (enabled != s_Recording)
            {
                s_Recording = enabled;
                foreach (var entry in s_Entries)
                {
                    if (enabled) entry.Start();
                    else entry.Stop();
                }
            }
            if (!enabled)
                return;

            // Results describe completed GPU samples available at collection time, not this CPU frame.
            // Consume atomically without stopping the GPU recorder or replaying old values.
            foreach (var entry in s_Entries)
            {
                if (!TryConsumeLatest(ref entry.Recorder, out var sample))
                    continue;
                long groupTotal = s_Groups[entry.Group].Value;
                if (!TryAccumulate(sample.Value, sample.Count, ref groupTotal))
                    continue;
                s_Groups[entry.Group].Value = groupTotal;
                entry.Time.Value += sample.Value;
                entry.Samples.Value += sample.Count;
                s_ValidSamples.Value += sample.Count;
            }
#endif
        }

        internal static unsafe bool TryConsumeLatest(ref ProfilerRecorder recorder, out ProfilerRecorderSample sample)
        {
            sample = default;
            if (!recorder.Valid) return false;
            // CopyTo(reset: true) drains storage while staying attached. Reset() stops the recorder
            // and restarting it interrupts delayed GPU samples. Capacity is exactly one.
            ProfilerRecorderSample value = default;
            int copied = recorder.CopyTo(&value, 1, true);
            sample = value;
            return copied > 0;
        }
        internal static bool TryAccumulate(long nanoseconds, long count, ref long total)
        {
            if (count <= 0 || nanoseconds < 0)
                return false;
            total += nanoseconds;
            return true;
        }

        internal static void Clear()
        {
            foreach (var entry in s_Entries)
                entry.Stop();
            s_Entries.Clear();
            s_ByMarker.Clear();
            for (int i = 0; i < s_Groups.Length; i++)
                s_Groups[i].Value = 0;
            s_ValidSamples.Value = 0;
            s_Recording = false;
        }

        internal static int RegisteredCount => s_Entries.Count;

        private static ProfilerCounterValue<long>[] CreateGroupCounters()
        {
            var counters = new ProfilerCounterValue<long>[GroupNames.Length];
            for (int i = 0; i < counters.Length; i++)
                counters[i] = new ProfilerCounterValue<long>(Category, GroupNames[i],
                    ProfilerMarkerDataUnit.TimeNanoseconds, CounterOptions);
            return counters;
        }

        internal static int Classify(Type type)
        {
            // Actual namespaces do not consistently follow directories; use explicit pass names.
            // New passes remain visibly unclassified until reviewed.
            switch (type?.Name)
            {
                case "PreDepthPass": case "HZBGeneratePass": case "GBufferPass":
                case "DrawObjectPass": case "MotionVectorPass": case "MaterialClassificationPass":
                case "VisibilityBufferPass": case "VisibilityBufferResolvePass":
                case "VisibilityBufferGBufferResolvePass": case "ExperimentalVisibilityBufferPass":
                case "ExperimentalClosureBufferPass": case "ExperimentalClosureClassificationPass": return 0;
                case "CSMShadowPass": case "CSMShadowResolvePass": case "ShadowCasterPass":
                case "ShadowClassifyPass": case "VSMShadowPass": case "DirectionalRayTracedShadowPass":
                case "SIGMAShadowDenoisePass": return 1;
                case "LightGridPass": case "LightGridGlobalPass": case "ReGIRGridBuildPass":
                case "DeferredLightingPass": case "DeferredDirectionalLightingPass":
                case "ExperimentalClosureDeferredLightingPass": return 2;
                case "GTAOPass": case "ScreenSpaceReflectionPass": return 3;
                case "SkyInjectionPass": case "AtmosphericScatteringPass": case "VolumetricDensityPass":
                case "VolumetricLightingPass": case "VolumetricMaxZPass": return 4;
                case "VirtualTextureFeedbackPass": return 5;
                case "RTASBuildPass": case "RaytracingGBufferPass": case "ReferencedPathTracingPass":
                case "ReferencedPathTracingAccumulationPass": case "ReferencedPathTracingCapturePass":
                case "ReferencedPathTracingDenoisingPass": case "ReferencedPathTracingDLSSRayReconstructionPass":
                case "ReferencedPathTracingEnvironmentSamplingPass": case "ReferencedPathTracingLightListPass":
                case "ReferencedPathTracingReblurPass": return 6;
                case "AntialiasingPass": case "CMAA2Pass": case "TemporalAAPass": case "StopNaNPass":
                case "UberPostPass": case "AutoExposurePass": case "BloomPass": case "ColorGradingPass":
                case "ColorPyramidPass": case "DepthOfFieldPass": case "DiffusionPass":
                case "DLSSNeuralRenderingPass": case "DLSSPass": case "FSR3UpscalerPass":
                case "DataDrivenLensFlarePass": case "ScreenSpaceLensFlarePass": case "LocalExposurePass":
                case "TSRUpscalerPass": return 7;
                case "FinalBlitPass": case "CopyDepthPass": case "GenerateViewZPass": return 8;
                default: return 9;
            }
        }
    }
}