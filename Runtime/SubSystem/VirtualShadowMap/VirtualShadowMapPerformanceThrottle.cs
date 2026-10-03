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
        internal static GraphicsBuffer Feedback { get; private set; }
        internal const int ClusterCountEntries = Entries * (int)VividRendererListID.Count * 2;
        internal static GraphicsBuffer ClusterCounts { get; private set; }
        internal static int CullKernel { get; private set; }
        internal static int PostCullKernel { get; private set; }
        internal static GraphicsBuffer Current => s_Throttle[s_Current];
        internal static GraphicsBuffer Previous => s_Throttle[1 - s_Current];
        internal static bool Enabled { get; private set; }
        private static ComputeShader s_Shader;
        private static int s_Process, s_Update, s_PrepareFeedback, s_CaptureFeedback;
        private static int s_Current, s_PreviousFrame = -1, s_Frame;
        private static ulong s_PreviousGeneration, s_Generation;
        private static bool s_Completed;
        private static Vector4 s_Parameters;
        private static readonly ProfilingSampler s_UpdateSampler = new("VSM.PerformanceThrottle");
        private static readonly ProfilingSampler s_FeedbackSampler = new("VSM.RasterFeedback");
        private static readonly int s_PrevId = Shader.PropertyToID("_VSMPrevThrottle");
        private static readonly int s_OutId = Shader.PropertyToID("_VSMThrottleRW");
        private static readonly int s_FeedbackId = Shader.PropertyToID("_VSMRasterFeedbackRW");
        private static readonly int s_ClusterCountsId = Shader.PropertyToID("_VSMRasterClusterCountsRW");
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
            s_Completed = false;
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
            Feedback?.Dispose(); Feedback = null;
            ClusterCounts?.Dispose(); ClusterCounts = null;
            s_Shader = null;
        }
    }
}
