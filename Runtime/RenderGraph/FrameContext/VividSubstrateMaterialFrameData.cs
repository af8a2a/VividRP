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

        private static RenderTexture s_DummyClosureBuffer;
        private static RTHandle s_DummyClosureBufferRTHandle;

        internal CameraHistoryTexture ClosureBufferHistory;
        internal TextureHandle ClosureBufferHandle;
        internal int Width;
        internal int Height;
        internal int SliceCount;
        internal bool HasSubstrateContent;
        internal bool IsValid => ClosureBufferHistory != null && Width > 0 && Height > 0 && SliceCount > 0;

        public override void Reset()
        {
            ClosureBufferHistory = null;
            ClosureBufferHandle = default;
            Width = 0;
            Height = 0;
            SliceCount = 0;
            HasSubstrateContent = false;
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
