using VividRP.Runtime.GPUDriven;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.VirtualShadowMap
{
    // Directional single-light adaptation of UE's throttle + Nanite feedback buffers.
    // GPU-only load control; no readback stalls, vertex quotas or dirty-page suppression.
    internal static class VirtualShadowMapPerformanceThrottle
    {
        private const int Entries = VirtualShadowMapClipmapLayout.MaxLevels;
        private static readonly Unity.Mathematics.uint4[] s_ZeroThrottle = new Unity.Mathematics.uint4[Entries + 2];
        private static readonly Unity.Mathematics.uint2[] s_ZeroFeedback = new Unity.Mathematics.uint2[Entries + 1];
        private static readonly GraphicsBuffer[] s_Throttle = new GraphicsBuffer[2];
        private static readonly GraphicsBuffer[] s_LocalThrottle = new GraphicsBuffer[2];
        private static GraphicsBuffer s_LocalClusterCounts;
        private static GraphicsBuffer s_DummyLocalClusterCounts;
        private static int s_LocalCapacity;
        private static int s_LocalCurrent, s_LocalPreviousFrame = -1, s_LocalFrame;
        private static int s_LocalPreviousCount;
        private static bool s_LocalCompleted;
        internal static GraphicsBuffer Feedback { get; private set; }
        internal const int ClusterCountEntries = Entries * (int)VividRendererListID.Count * 2;
        internal static GraphicsBuffer ClusterCounts { get; private set; }
        internal static int CullKernel { get; private set; }
        internal static int PostCullKernel { get; private set; }
        internal static GraphicsBuffer Current => s_Throttle[s_Current];
        internal static GraphicsBuffer Previous => s_Throttle[1 - s_Current];
        internal static GraphicsBuffer LocalCurrent => s_LocalThrottle[s_LocalCurrent];
        internal static GraphicsBuffer LocalPrevious => s_LocalThrottle[1 - s_LocalCurrent];
        internal static GraphicsBuffer LocalClusterCounts => s_LocalClusterCounts;
        internal static int LocalCapacity => s_LocalCapacity;
        internal static bool Enabled { get; private set; }
        internal static bool LocalEnabled { get; private set; }
        private static ComputeShader s_Shader;
        private static int s_Process, s_Update, s_PrepareFeedback, s_CaptureFeedback;
        private static int s_ProcessLocal = -1, s_UpdateLocal = -1;
        private static int s_Current, s_PreviousFrame = -1, s_Frame;
        private static ulong s_PreviousGeneration, s_Generation;
        private static bool s_Completed;
        private static Vector4 s_Parameters;
        private static readonly ProfilingSampler s_UpdateSampler = new("VSM.PerformanceThrottle");
        private static readonly ProfilingSampler s_LocalUpdateSampler = new("VSM.LocalPerformanceThrottle");
        private static readonly ProfilingSampler s_FeedbackSampler = new("VSM.RasterFeedback");
        private static readonly int s_PrevId = Shader.PropertyToID("_VSMPrevThrottle");
        private static readonly int s_OutId = Shader.PropertyToID("_VSMThrottleRW");
        private static readonly int s_FeedbackId = Shader.PropertyToID("_VSMRasterFeedbackRW");
        private static readonly int s_ClusterCountsId = Shader.PropertyToID("_VSMRasterClusterCountsRW");
        private static readonly int s_LocalClusterCountsId = Shader.PropertyToID("_VSMLocalRasterClusterCountsRW");
        private static readonly int s_LocalThrottleEnabledId = Shader.PropertyToID("_VSMLocalThrottleEnabled");
        private static readonly int s_LocalMaxThrottleEntriesId = Shader.PropertyToID("_VSMLocalMaxThrottleEntries");
        private static readonly int s_LocalStartVsmId = Shader.PropertyToID("_VSMLocalThrottleStartVSMId");
        private static readonly int s_LocalCountId = Shader.PropertyToID("_VSMLocalThrottleCount");
        private static readonly int s_LocalMemPressureEnabledId = Shader.PropertyToID("_VSMLocalThrottleMemoryPressureEnabled");
        private static readonly int s_ProjectionId = Shader.PropertyToID("_VSMThrottleProjectionsRW");
        private static readonly int s_PreviousValidId = Shader.PropertyToID("_VSMThrottlePreviousValid");
        private static readonly int s_EntriesValidId = Shader.PropertyToID("_VSMThrottleEntriesValid");
        private static readonly int s_ParametersId = Shader.PropertyToID("_VSMThrottleParameters");
        private static readonly int s_DrawMaskId = Shader.PropertyToID("_VSMRasterFeedbackDrawMask");

        internal static bool Prepare(CascadedShadowSettingsVolume settings, ComputeShader shader)
        {
            float load = settings.virtualShadowMapThrottleLoadBudget.value;
            bool enabled = load > 0;
            if (enabled != Enabled) s_Completed = false;
            Enabled = enabled;
            s_Parameters = new Vector4(load > 0 ? load : float.MaxValue,
                settings.virtualShadowMapThrottleHistoryWeight.value, settings.virtualShadowMapThrottleMaxBias.value,
                -1); // UE TimeBudget override awaits a qualified native GPU timestamp provider.
            if (!Enabled) return true;
            if (s_Shader != shader || shader == null)
            {
                if (shader == null || !shader.HasKernel("VSMProcessPreviousPerformance")
                    || !shader.HasKernel("VSMUpdatePerformanceThrottle")
                    || !shader.HasKernel("VSMPrepareRasterFeedback")
                    || !shader.HasKernel("VSMCaptureRasterFeedback")
                    || !shader.HasKernel("VSMCullMeshletsToPagesThrottle")
                    || !shader.HasKernel("VSMPostCullMeshletsToPagesThrottle"))
                {
                    Disable();
                    return false;
                }
                s_Shader = shader;
                s_Process = shader.FindKernel("VSMProcessPreviousPerformance");
                s_Update = shader.FindKernel("VSMUpdatePerformanceThrottle");
                s_PrepareFeedback = shader.FindKernel("VSMPrepareRasterFeedback");
                s_CaptureFeedback = shader.FindKernel("VSMCaptureRasterFeedback");
                CullKernel = shader.FindKernel("VSMCullMeshletsToPagesThrottle");
                PostCullKernel = shader.FindKernel("VSMPostCullMeshletsToPagesThrottle");
                s_ProcessLocal = shader.HasKernel("VSMProcessLocalPerformance") ? shader.FindKernel("VSMProcessLocalPerformance") : -1;
                s_UpdateLocal = shader.HasKernel("VSMUpdateLocalPerformanceThrottle") ? shader.FindKernel("VSMUpdateLocalPerformanceThrottle") : -1;
            }
            if (Feedback != null && Feedback.IsValid()) return true;
            for (int i = 0; i < 2; i++)
            {
                s_Throttle[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Entries + 2, 16)
                { name = i == 0 ? "VSMThrottle0" : "VSMThrottle1" };
                s_Throttle[i].SetData(s_ZeroThrottle);
            }
            Feedback = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Entries + 1, 8) { name = "VSMRasterFeedback" };
            Feedback.SetData(s_ZeroFeedback);
            ClusterCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ClusterCountEntries, sizeof(uint))
            { name = "VSMRasterClusterCounts" };
            s_Completed = false;
            return true;
        }
        internal static void Disable()
        {
            Enabled = false;
            LocalEnabled = false;
            s_Completed = false;
            s_LocalCompleted = false;
        }

        internal static bool PrepareLocal(int projectionCount)
        {
            if (!Enabled || projectionCount <= 0 || s_Shader == null || s_ProcessLocal < 0 || s_UpdateLocal < 0)
            {
                LocalEnabled = false;
                s_LocalCompleted = false;
                return false;
            }

            int required = Mathf.NextPowerOfTwo(Mathf.Max(projectionCount, 64));
            if (s_LocalClusterCounts == null || !s_LocalClusterCounts.IsValid() || s_LocalCapacity < required)
            {
                for (int i = 0; i < 2; i++)
                {
                    s_LocalThrottle[i]?.Dispose();
                    s_LocalThrottle[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, required + 2, 16)
                    { name = i == 0 ? "VSMLocalThrottle0" : "VSMLocalThrottle1" };
                    s_LocalThrottle[i].SetData(new Unity.Mathematics.uint4[required + 2]);
                }
                s_LocalClusterCounts?.Dispose();
                s_LocalClusterCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, required, sizeof(uint))
                { name = "VSMLocalRasterClusterCounts" };
                s_LocalClusterCounts.SetData(new uint[required]);
                s_LocalCapacity = required;
                s_LocalCompleted = false;
            }
            LocalEnabled = true;
            return true;
        }

        internal static void BeginLocalFrame(
            CommandBuffer cmd,
            int frame,
            int startVsmId,
            int projectionCount,
            bool memoryPressureEnabled,
            GraphicsBuffer unifiedProjections,
            GraphicsBuffer localPagePressure)
        {
            if (!LocalEnabled || s_Shader == null || unifiedProjections == null || localPagePressure == null)
                return;
            using var scope = new ProfilingScope(cmd, s_LocalUpdateSampler);
            s_LocalCurrent = 1 - s_LocalCurrent;
            s_LocalFrame = frame;
            bool previous = s_LocalCompleted && frame >= s_LocalPreviousFrame && frame - s_LocalPreviousFrame <= 1;
            bool entriesValid = previous && projectionCount == s_LocalPreviousCount;

            cmd.SetComputeIntParam(s_Shader, s_PreviousValidId, previous ? 1 : 0);
            cmd.SetComputeIntParam(s_Shader, s_EntriesValidId, entriesValid ? 1 : 0);
            cmd.SetComputeIntParam(s_Shader, s_LocalStartVsmId, startVsmId);
            cmd.SetComputeIntParam(s_Shader, s_LocalCountId, projectionCount);
            cmd.SetComputeIntParam(s_Shader, s_LocalMemPressureEnabledId, memoryPressureEnabled ? 1 : 0);
            cmd.SetComputeVectorParam(s_Shader, s_ParametersId, s_Parameters);

            cmd.SetComputeBufferParam(s_Shader, s_ProcessLocal, s_PrevId, LocalPrevious);
            cmd.SetComputeBufferParam(s_Shader, s_ProcessLocal, s_OutId, LocalCurrent);
            cmd.SetComputeBufferParam(s_Shader, s_ProcessLocal, s_LocalClusterCountsId, s_LocalClusterCounts);
            cmd.DispatchCompute(s_Shader, s_ProcessLocal, 1, 1, 1);

            cmd.SetComputeBufferParam(s_Shader, s_UpdateLocal, s_PrevId, LocalPrevious);
            cmd.SetComputeBufferParam(s_Shader, s_UpdateLocal, s_OutId, LocalCurrent);
            cmd.SetComputeBufferParam(s_Shader, s_UpdateLocal, s_ProjectionId, unifiedProjections);
            cmd.SetComputeBufferParam(s_Shader, s_UpdateLocal, VirtualShadowMapReceiverQuality.PressureId, localPagePressure);
            cmd.DispatchCompute(s_Shader, s_UpdateLocal, CoreUtils.DivRoundUp(Mathf.Max(projectionCount, 1), 64), 1, 1);

            s_LocalPreviousCount = projectionCount;
            s_LocalCompleted = false;
        }

        internal static void BindLocalRasterFeedback(CommandBuffer cmd, ComputeShader shader, int buildMeshletPagesKernel)
        {
            if (LocalEnabled && s_LocalClusterCounts != null && s_LocalClusterCounts.IsValid())
            {
                cmd.SetComputeIntParam(shader, s_LocalThrottleEnabledId, 1);
                cmd.SetComputeIntParam(shader, s_LocalMaxThrottleEntriesId, s_LocalCapacity);
                cmd.SetComputeBufferParam(shader, buildMeshletPagesKernel, s_LocalClusterCountsId, s_LocalClusterCounts);
                return;
            }
            if (s_DummyLocalClusterCounts == null || !s_DummyLocalClusterCounts.IsValid())
            {
                s_DummyLocalClusterCounts?.Dispose();
                s_DummyLocalClusterCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint))
                { name = "VSMLocalRasterClusterCountsDummy" };
            }
            cmd.SetComputeIntParam(shader, s_LocalThrottleEnabledId, 0);
            cmd.SetComputeIntParam(shader, s_LocalMaxThrottleEntriesId, 0);
            cmd.SetComputeBufferParam(shader, buildMeshletPagesKernel, s_LocalClusterCountsId, s_DummyLocalClusterCounts);
        }

        internal static void CompleteLocalFrame(bool success)
        {
            if (!LocalEnabled) return;
            s_LocalCompleted = success;
            s_LocalPreviousFrame = s_LocalFrame;
        }
        internal static void BeginFrame(CommandBuffer cmd, int frame, ulong generation, int count, Vector4 quality)
        {
            if (!Enabled) return;
            using var scope = new ProfilingScope(cmd, s_UpdateSampler);
            s_Current = 1 - s_Current;
            s_Frame = frame;
            s_Generation = generation;
            // Layout slides retain entry identities. Camera/light/basis changes reset
            // per-entry history; total prior work still contributes to the global budget.
            bool previous = s_Completed && frame >= s_PreviousFrame && frame - s_PreviousFrame <= 1;
            cmd.SetComputeIntParam(s_Shader, s_PreviousValidId, previous ? 1 : 0);
            cmd.SetComputeIntParam(s_Shader, s_EntriesValidId, generation == s_PreviousGeneration ? 1 : 0);
            cmd.SetComputeIntParam(s_Shader, VirtualShadowMapProjectionSet.CountId, count);
            cmd.SetComputeBufferParam(s_Shader, s_Process, s_PrevId, Previous);
            cmd.SetComputeBufferParam(s_Shader, s_Process, s_OutId, Current);
            cmd.SetComputeBufferParam(s_Shader, s_Process, s_FeedbackId, Feedback);
            cmd.DispatchCompute(s_Shader, s_Process, 1, 1, 1);
            var parameters = s_Parameters;
            cmd.SetComputeVectorParam(s_Shader, s_ParametersId, parameters);
            cmd.SetComputeVectorParam(s_Shader, VirtualShadowMapReceiverQuality.ParametersId, quality);
            cmd.SetComputeBufferParam(s_Shader, s_Update, s_PrevId, Previous);
            cmd.SetComputeBufferParam(s_Shader, s_Update, s_OutId, Current);
            cmd.SetComputeBufferParam(s_Shader, s_Update, s_ProjectionId, VirtualShadowMapPrototypeRuntime.Projections.Buffer);
            cmd.SetComputeBufferParam(s_Shader, s_Update, VirtualShadowMapReceiverQuality.PressureId, VirtualShadowMapPrototypeRuntime.PagePressure);
            cmd.DispatchCompute(s_Shader, s_Update, 1, 1, 1);
            s_Completed = false;
        }
        internal static void BeginCull(CommandBuffer cmd, int kernel)
        {
            if (!Enabled) return;
            cmd.SetComputeBufferParam(s_Shader, s_PrepareFeedback, s_ClusterCountsId, ClusterCounts);
            cmd.DispatchCompute(s_Shader, s_PrepareFeedback, (ClusterCountEntries + 63) / 64, 1, 1);
            cmd.SetComputeBufferParam(s_Shader, kernel, s_ClusterCountsId, ClusterCounts);
        }
        internal static void Capture(CommandBuffer cmd, uint submittedMask)
        {
            if (!Enabled || submittedMask == 0) return;
            using var scope = new ProfilingScope(cmd, s_FeedbackSampler);
            cmd.SetComputeIntParam(s_Shader, s_DrawMaskId, unchecked((int)submittedMask));
            cmd.SetComputeBufferParam(s_Shader, s_CaptureFeedback, s_ClusterCountsId, ClusterCounts);
            cmd.SetComputeBufferParam(s_Shader, s_CaptureFeedback, s_FeedbackId, Feedback);
            cmd.DispatchCompute(s_Shader, s_CaptureFeedback, 1, 1, 1);
        }
        internal static void CompleteFrame(bool success)
        {
            if (!Enabled) return;
            s_Completed = success;
            s_PreviousFrame = s_Frame;
            s_PreviousGeneration = s_Generation;
        }
        internal static void Dispose()
        {
            Disable();
            for (int i = 0; i < 2; i++) { s_Throttle[i]?.Dispose(); s_Throttle[i] = null; }
            for (int i = 0; i < 2; i++) { s_LocalThrottle[i]?.Dispose(); s_LocalThrottle[i] = null; }
            s_LocalClusterCounts?.Dispose(); s_LocalClusterCounts = null;
            s_DummyLocalClusterCounts?.Dispose(); s_DummyLocalClusterCounts = null;
            s_LocalCapacity = 0;
            Feedback?.Dispose(); Feedback = null;
            ClusterCounts?.Dispose(); ClusterCounts = null;
            s_Shader = null;
            s_ProcessLocal = -1;
            s_UpdateLocal = -1;
        }
    }
}
