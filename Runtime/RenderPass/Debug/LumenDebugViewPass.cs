using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum LumenDebugViewMode
    {
        None = 0,
        Overview = 1,
        PerformanceOverview = 2,
        LumenScene = 3,
        ReflectionView = 4,
        SurfaceCache = 5,
        GeometryNormals = 6,
        DedicatedReflectionRays = 7,
        Albedo = 8,
        CardNormals = 9,
        Emissive = 10,
        Opacity = 11,
        CardWeights = 12,
        DirectLighting = 13,
        IndirectLighting = 14,
        LocalPosition = 15,
        Velocity = 16,
        DirectLightingUpdates = 17,
        IndirectLightingUpdates = 18,
        LastUsedPages = 19,
        LastUsedHighResPages = 20,
        TileShadowDownsampleFactor = 21,
        CardSharingId = 22,
        FastUpdateAmount = 23,
        FramesAccumulated = 24,
        ScreenProbeTraceSources = 25,
        ScreenProbeRadiance = 26,
        DiffuseGI = 27,
        ShortRangeAO = 28,
        BentNormal = 29,
        RadiosityAtlas = 30,
        WorldRadianceCache = 31,
        WorldProbeAge = 32,
        GlobalSdf = 33,
        ScreenProbeRayState = 34,
    }

    internal sealed class VividLumenDebugFrameData : ContextItem
    {
        internal LumenDebugViewMode Mode;
        internal TextureHandle Output;
        internal bool Produced;

        public override void Reset()
        {
            Mode = LumenDebugViewMode.None;
            Output = default;
            Produced = false;
        }
    }

    // Diagnostic kernels execute alongside the gather so they can inspect its transient resources.
    // FinalBlit consumes only the composited diagnostic image, after post-processing.
    internal sealed class LumenDebugViewPass
    {
        internal static readonly ProfilingSampler Sampler = new(nameof(LumenDebugViewPass));
        internal static readonly int ModeId = Shader.PropertyToID("_LumenDebugMode");
        internal static readonly int OutputId = Shader.PropertyToID("_LumenDebugOutput");
        internal static readonly int FeedId = Shader.PropertyToID("_LumenDebugFeed");
        internal static readonly int PackedFeedId = Shader.PropertyToID("_LumenDebugPackedFeed");
        internal static readonly int DirectId = Shader.PropertyToID("_LumenDebugDirect");
        internal static readonly int ParametersId = Shader.PropertyToID("_LumenDebugParameters");
        internal static readonly int CardFramesId = Shader.PropertyToID("_LumenDebugCardFrames");
        internal static readonly int CardFramesOutputId = Shader.PropertyToID("_LumenDebugCardFramesOutput");
        internal static readonly int CardUpdatesId = Shader.PropertyToID("_LumenDebugCardUpdates");
        internal static readonly int FrameParametersId = Shader.PropertyToID("_LumenDebugFrameParameters");
        internal static readonly int VisualizeFeedbackId = Shader.PropertyToID("_LumenDebugVisualizeFeedback");
        private static readonly int LightingOrderId = Shader.PropertyToID("_SdfFinalLightingOrder");
        private static readonly int FeedbackBufferId = Shader.PropertyToID("_SdfSurfaceCacheLastUsed");
        private static readonly int FeedbackFrameId = Shader.PropertyToID("_SdfSurfaceCacheFeedbackFrame");

        private static readonly CameraHistoryId CardFramesHistoryId = CameraHistoryId.Create("LumenDebugCardFrames");
        private static readonly CameraHistoryId FeedbackHistoryId = CameraHistoryId.Create("LumenSurfaceCacheLastUsed");
        private readonly RenderGraphTexture m_OutputDesc = CreateTarget("Lumen Debug View", GraphicsFormat.R8G8B8A8_UNorm);
        private readonly RenderGraphTexture m_CardFramesDesc = CreateTarget("Lumen Debug Card Frames", GraphicsFormat.R32G32_SFloat);
        private CameraHistoryTexture m_CardFramesHistory;
        private CameraHistoryBuffer m_FeedbackHistory;
        private BufferHandle m_Feedback;
        private bool m_ClearFeedback;

        internal int Width => m_OutputDesc.desc.Width;
        internal int Height => m_OutputDesc.desc.Height;
        internal TextureHandle CardFrames { get; private set; }
        internal bool ClearCardFrames { get; private set; }
        internal bool HasSurfaceCacheFeedback => m_Feedback.IsValid();
        internal BufferHandle FeedbackBuffer => m_Feedback;
        internal LumenDebugViewMode Mode { get; private set; }

        internal static bool IsOverview(LumenDebugViewMode mode) =>
            mode is LumenDebugViewMode.Overview or LumenDebugViewMode.PerformanceOverview;

        internal static bool IsSurfaceView(LumenDebugViewMode mode) =>
            mode is LumenDebugViewMode.Overview or LumenDebugViewMode.LumenScene or LumenDebugViewMode.SurfaceCache
                or LumenDebugViewMode.GeometryNormals or >= LumenDebugViewMode.Albedo and <= LumenDebugViewMode.LastUsedHighResPages
                or LumenDebugViewMode.CardSharingId;

        internal static bool IsFeedbackView(LumenDebugViewMode mode) =>
            mode is LumenDebugViewMode.LastUsedPages or LumenDebugViewMode.LastUsedHighResPages;

        internal static string ModeDescription(LumenDebugViewMode mode) => mode switch
        {
            LumenDebugViewMode.Overview => "Left to right: geometry normals, reflection view, surface cache coverage.",
            LumenDebugViewMode.PerformanceOverview => "Unavailable: VividRP has no Lumen performance overview mode.",
            LumenDebugViewMode.ReflectionView => "Dedicated Lumen specular reflections (pre-exposure normalized and tone-mapped).",
            LumenDebugViewMode.DedicatedReflectionRays => "Red: dedicated reflection rays (brighter = smoother). Green: foliage/subsurface reflection rays.",
            LumenDebugViewMode.LastUsedPages =>
                "Screen probe card accesses. White this frame, red to blue over 8 frames, black if never recorded.",
            LumenDebugViewMode.LastUsedHighResPages =>
                "Enable Capture Visualization Feedback and view Lumen Scene or Surface Cache before returning here. Shows the resident pages actually sampled by that path.",
            LumenDebugViewMode.TileShadowDownsampleFactor =>
                "Unavailable: VividRP has no adaptive Lumen card tile shadow downsampling.",
            LumenDebugViewMode.SurfaceCache => "Pink: missing card coverage. Yellow: raster geometry missing from Mesh SDF tracing.",
            LumenDebugViewMode.DirectLightingUpdates or LumenDebugViewMode.IndirectLightingUpdates =>
                "Brighter cards were updated more recently. The view records update ages while either update mode is selected.",
            LumenDebugViewMode.Opacity => "Shows binary coverage from captured VividRP card texels.",
            LumenDebugViewMode.CardWeights => "Shows the selected VividRP card with the card texel grid.",
            LumenDebugViewMode.CardSharingId => "Colors identify the baked material slice shared by matching instances.",
            LumenDebugViewMode.WorldProbeAge => "Shows the 64×64 physical probe slots. Brighter probes were traced more recently.",
            LumenDebugViewMode.ScreenProbeRayState => "Shows the flattened probe ray atlas with distinct trace-state colors.",
            _ => "Diagonal stripes indicate unavailable data. These views require an active realtime GI gather.",
        };

        internal void Prepare(IRenderPass owner, ContextContainer frameData, Camera camera, int width, int height,
            LumenDebugViewMode mode, int cards, bool invalidateCardFrames)
        {
            Mode = LumenDebugViewMode.None;
            CardFrames = default;
            m_CardFramesHistory = null;
            if (mode == LumenDebugViewMode.None || !PassRecorder.HasActiveRenderGraph)
                return;

            bool overview = IsOverview(mode);
            int divisor = IsSurfaceView(mode) || mode == LumenDebugViewMode.GlobalSdf ? 2 : 1;
            int outputWidth = overview ? CoreUtils.DivRoundUp(width, 3) * 3 : CoreUtils.DivRoundUp(width, divisor);
            m_OutputDesc.Resize(outputWidth, CoreUtils.DivRoundUp(height, overview ? 3 : divisor));
            TextureDesc outputTextureDesc = m_OutputDesc.desc;
            var handle = FrameResourceTable.GetOrCreate(frameData).CreateAndPublishTextureForPass(
                owner,
                FrameResourceSlots.LumenDebugOutput,
                in outputTextureDesc,
                AccessFlags.Write);
            if (!handle.IsValid())
                return;

            var data = frameData.GetOrCreate<VividLumenDebugFrameData>();
            data.Mode = Mode = mode;
            data.Output = handle;

            if (cards <= 0 || mode is not (LumenDebugViewMode.DirectLightingUpdates or LumenDebugViewMode.IndirectLightingUpdates)
                || !SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample))
                return;

            var history = camera.GetVividCameraHistory();
            if (!history.IsFrameActive)
                return;

            m_CardFramesDesc.Resize(64, CoreUtils.DivRoundUp(cards, 64));
            m_CardFramesHistory = history.GetOrCreateTexture(CardFramesHistoryId, 1,
                CameraHistoryRenderGraphBridge.CreateDescriptor(m_CardFramesDesc.desc));
            ClearCardFrames = invalidateCardFrames || !m_CardFramesHistory.IsValid(0);
            CardFrames = CameraHistoryRenderGraphBridge.ImportForPass(owner, m_CardFramesHistory, 0, AccessFlags.ReadWrite);
        }

        internal void PrepareFeedback(IRenderPass owner, Camera camera, int cards, bool invalidate)
        {
            m_FeedbackHistory = null;
            m_Feedback = default;
            if (cards == 0)
                return;

            var history = camera.GetVividCameraHistory();
            if (!history.IsFrameActive)
                return;

            m_FeedbackHistory = history.GetOrCreateBuffer(FeedbackHistoryId, 1,
                new CameraHistoryBufferDescriptor(cards, 2 * sizeof(uint)));
            m_ClearFeedback = invalidate || !m_FeedbackHistory.IsValid(0);
            m_Feedback = CameraHistoryRenderGraphBridge.ImportForPass(owner, m_FeedbackHistory, 0, AccessFlags.ReadWrite);
        }

        internal void BeginFeedback(ComputeCommandBuffer cmd, ComputeShader shader, int clearKernel, uint frame)
        {
            if (m_ClearFeedback)
            {
                BindFeedback(cmd, shader, clearKernel);
                cmd.DispatchCompute(shader, clearKernel, CoreUtils.DivRoundUp(m_FeedbackHistory.Descriptor.Count, 64), 1, 1);
            }
            cmd.SetComputeIntParam(shader, FeedbackFrameId, unchecked((int)frame));
        }

        internal void BindFeedback(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            if (HasSurfaceCacheFeedback)
                cmd.SetComputeBufferParam(shader, kernel, FeedbackBufferId, m_Feedback);
        }

        internal void MarkFeedbackWritten()
        {
            if (HasSurfaceCacheFeedback)
                m_FeedbackHistory.MarkWritten();
        }

        internal void RecordCardFrames(ComputeCommandBuffer cmd, ComputeShader shader, int kernel, GraphicsBuffer order,
            int cards, int frame, Vector4 updates, bool priorityIndirect)
        {
            if (!CardFrames.IsValid() || kernel < 0)
                return;
            cmd.SetComputeBufferParam(shader, kernel, LightingOrderId, order);
            cmd.SetComputeTextureParam(shader, kernel, CardFramesOutputId, CardFrames);
            cmd.SetComputeVectorParam(shader, CardUpdatesId, updates);
            cmd.SetComputeVectorParam(shader, FrameParametersId,
                new Vector4(frame, cards, ClearCardFrames ? 1f : 0f, priorityIndirect ? 1f : 0f));
            cmd.DispatchCompute(shader, kernel, CoreUtils.DivRoundUp(cards, 64), 1, 1);
            m_CardFramesHistory.MarkWritten();
        }

        internal static Rect GetOverviewViewport(Rect viewport) =>
            new(viewport.x, viewport.y, viewport.width, viewport.height / 3f);

        private static RenderGraphTexture CreateTarget(string name, GraphicsFormat format)
        {
            var result = RenderGraphTexture.CreateOutput(name, format);
            result.desc.EnableRandomWrite = true;
            result.desc.FilterMode = FilterMode.Point;
            return result;
        }
    }
}
