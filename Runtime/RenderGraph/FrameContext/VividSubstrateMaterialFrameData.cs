using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime
{
    /// <summary>
    /// Per-frame RenderGraph context carrying the Substrate Closure Buffer
    /// (RWTexture2DArray&lt;uint&gt;) between VisibilityBuffer Resolve and downstream
    /// Classification, Deferred Lighting, and Lumen passes without modifying .vrdg ports.
    /// </summary>
    internal sealed class VividSubstrateMaterialFrameData : ContextItem
    {
        internal static readonly CameraHistoryId ClosureBufferHistoryId =
            CameraHistoryId.Create("VividSubstrateClosureBuffer");

        private static readonly int OutSubstrateClosureBufferId = Shader.PropertyToID("_OutSubstrateClosureBuffer");
        private static readonly int ClassificationWidthId = Shader.PropertyToID("_ClassificationWidth");
        private static readonly int ClassificationHeightId = Shader.PropertyToID("_ClassificationHeight");
        private const int ClearHeaderGroupSize = 8;

        private static RenderTexture s_DummyClosureBuffer;
        private static RTHandle s_DummyClosureBufferRTHandle;

        internal CameraHistoryTexture ClosureBufferHistory;
        internal TextureHandle ClosureBufferHandle;
        internal int Width;
        internal int Height;
        internal int SliceCount;
        internal bool HasSubstrateContent;
        internal bool HeadersClearedThisFrame;
        internal int WriterCount;
        internal bool IsValid => ClosureBufferHistory != null && Width > 0 && Height > 0 && SliceCount > 0;

        public override void Reset()
        {
            ClosureBufferHistory = null;
            ClosureBufferHandle = default;
            Width = 0;
            Height = 0;
            SliceCount = 0;
            HasSubstrateContent = false;
            HeadersClearedThisFrame = false;
            WriterCount = 0;
        }

        internal static CameraHistoryTextureDescriptor CreateClosureBufferDescriptor(
            int width,
            int height,
            int sliceCount = VividSubstrateContract.DefaultSliceCount)
        {
            return new CameraHistoryTextureDescriptor(
                Mathf.Max(1, width),
                Mathf.Max(1, height),
                VividSubstrateContract.ClosureBufferFormat,
                VividSubstrateContract.ClampSliceCount(sliceCount),
                VividSubstrateContract.ClosureBufferDimension,
                DepthBits.None,
                MSAASamples.None,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                anisoLevel: 1,
                mipMapBias: 0f,
                enableRandomWrite: true,
                useMipMap: false,
                autoGenerateMips: false,
                isShadowMap: false,
                bindTextureMS: false,
                useDynamicScale: false,
                useDynamicScaleExplicit: false);
        }

        internal static RenderTexture GetOrCreateDummyClosureBuffer()
        {
            if (s_DummyClosureBuffer != null && s_DummyClosureBuffer.IsCreated())
                return s_DummyClosureBuffer;

            if (s_DummyClosureBuffer != null)
            {
                s_DummyClosureBuffer.Release();
                CoreUtils.Destroy(s_DummyClosureBuffer);
                s_DummyClosureBuffer = null;
            }

            var desc = new RenderTextureDescriptor(1, 1, VividSubstrateContract.ClosureBufferFormat, 0)
            {
                dimension = VividSubstrateContract.ClosureBufferDimension,
                volumeDepth = 1,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                msaaSamples = 1,
            };
            s_DummyClosureBuffer = new RenderTexture(desc)
            {
                name = "VividDummySubstrateClosureBuffer",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            s_DummyClosureBuffer.Create();
            return s_DummyClosureBuffer;
        }

        internal static RTHandle GetOrCreateDummyClosureBufferRTHandle()
        {
            RenderTexture dummy = GetOrCreateDummyClosureBuffer();
            if (s_DummyClosureBufferRTHandle != null && ReferenceEquals(s_DummyClosureBufferRTHandle.rt, dummy))
                return s_DummyClosureBufferRTHandle;

            s_DummyClosureBufferRTHandle?.Release();
            s_DummyClosureBufferRTHandle = RTHandles.Alloc(dummy);
            return s_DummyClosureBufferRTHandle;
        }

        /// <summary>
        /// Ensures the per-camera Substrate Closure Buffer is allocated and imported for write access
        /// by <paramref name="pass"/>. Supports multiple writers in the same frame (e.g.
        /// VisibilityBufferGBufferResolvePass followed by GBufferPass) without resetting earlier
        /// writers' pixels or allocating managed memory after warmup.
        /// </summary>
        internal static bool EnsureAllocatedForWrite(
            IRenderPass pass,
            ContextContainer frameData,
            VividCameraData cameraData,
            int width,
            int height,
            int requestedSliceCount,
            out VividSubstrateMaterialFrameData substrateData,
            out TextureHandle handle,
            out bool isFirstWriter)
        {
            substrateData = frameData?.GetOrCreate<VividSubstrateMaterialFrameData>();
            handle = default;
            isFirstWriter = false;

            if (pass == null || substrateData == null || cameraData?.camera == null || !PassRecorder.HasActiveRenderGraph)
                return false;

            int clampedSlices = VividSubstrateContract.ClampSliceCount(requestedSliceCount);
            if (substrateData.HasSubstrateContent
                && substrateData.IsValid
                && substrateData.Width == width
                && substrateData.Height == height
                && substrateData.SliceCount >= clampedSlices)
            {
                handle = CameraHistoryRenderGraphBridge.ImportForPass(
                    pass,
                    substrateData.ClosureBufferHistory,
                    0,
                    AccessFlags.ReadWrite);
                if (!handle.IsValid())
                    return false;

                substrateData.ClosureBufferHandle = handle;
                substrateData.WriterCount++;
                isFirstWriter = false;
                return true;
            }

            var cameraHistory = cameraData.camera.GetVividCameraHistory();
            if (!cameraHistory.IsFrameActive)
                return false;

            int effectiveSlices = substrateData.IsValid
                ? Mathf.Max(substrateData.SliceCount, clampedSlices)
                : clampedSlices;
            var descriptor = CreateClosureBufferDescriptor(width, height, effectiveSlices);
            CameraHistoryTexture history = cameraHistory.GetOrCreateTexture(
                ClosureBufferHistoryId,
                1,
                descriptor);
            handle = CameraHistoryRenderGraphBridge.ImportForPass(
                pass,
                history,
                0,
                AccessFlags.Write);
            if (!handle.IsValid())
                return false;

            substrateData.ClosureBufferHistory = history;
            substrateData.ClosureBufferHandle = handle;
            substrateData.Width = width;
            substrateData.Height = height;
            substrateData.SliceCount = effectiveSlices;
            substrateData.HasSubstrateContent = true;
            substrateData.HeadersClearedThisFrame = false;
            substrateData.WriterCount = 1;
            isFirstWriter = true;
            return true;
        }

        /// <summary>
        /// Clears only Slice 0 (32-bit Substrate Pixel Header = VIVID_SUBSTRATE_MODE_EMPTY)
        /// when no earlier pass in the current frame has cleared or fully populated it.
        /// </summary>
        internal static void ClearHeadersIfNeeded(
            CommandBuffer cmd,
            VividSubstrateMaterialFrameData substrateData,
            ComputeShader classificationCompute,
            int clearHeadersKernel,
            TextureHandle closureBufferHandle)
        {
            if (cmd == null
                || substrateData == null
                || substrateData.HeadersClearedThisFrame
                || !substrateData.IsValid
                || classificationCompute == null
                || clearHeadersKernel < 0
                || !closureBufferHandle.IsValid())
            {
                return;
            }

            int width = substrateData.Width;
            int height = substrateData.Height;
            cmd.SetComputeIntParam(classificationCompute, ClassificationWidthId, width);
            cmd.SetComputeIntParam(classificationCompute, ClassificationHeightId, height);
            cmd.SetComputeTextureParam(classificationCompute, clearHeadersKernel, OutSubstrateClosureBufferId, closureBufferHandle);
            int groupsX = Mathf.Max(1, (width + ClearHeaderGroupSize - 1) / ClearHeaderGroupSize);
            int groupsY = Mathf.Max(1, (height + ClearHeaderGroupSize - 1) / ClearHeaderGroupSize);
            cmd.DispatchCompute(classificationCompute, clearHeadersKernel, groupsX, groupsY, 1);
            substrateData.HeadersClearedThisFrame = true;
        }

        /// <summary>
        /// Imports the frame's Substrate Closure Buffer for read access when valid and matching
        /// (expectedWidth, expectedHeight), otherwise imports a 1x1x1 R32_UInt Tex2DArray dummy handle
        /// so compute passes always bind a dimension-matched TextureHandle.
        /// Returns true only when real Substrate content is bound.
        /// </summary>
        internal static bool ImportClosureBufferOrDummyForPass(
            IRenderPass pass,
            ContextContainer frameData,
            int expectedWidth,
            int expectedHeight,
            out TextureHandle handle)
        {
            handle = default;
            if (pass == null || !PassRecorder.HasActiveRenderGraph)
                return false;

            if (frameData != null && frameData.Contains<VividSubstrateMaterialFrameData>())
            {
                var substrate = frameData.Get<VividSubstrateMaterialFrameData>();
                if (substrate.HasSubstrateContent
                    && substrate.IsValid
                    && substrate.Width == expectedWidth
                    && substrate.Height == expectedHeight)
                {
                    handle = CameraHistoryRenderGraphBridge.ImportForPass(
                        pass,
                        substrate.ClosureBufferHistory,
                        0,
                        AccessFlags.Read);
                    if (handle.IsValid())
                        return true;
                }
            }

            handle = PassRecorder.ImportTextureForPass(
                pass,
                GetOrCreateDummyClosureBufferRTHandle(),
                AccessFlags.Read);
            return false;
        }
    }
}
