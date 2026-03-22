using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core.Sigma
{
    public sealed class SIGMAShadowDenoisePass : UnsafePass
    {
        private const string HistoryShadowKey = "HistoryShadow";
        private const string HistoryLengthKey = "HistoryLength";

        private static readonly int gIn_ViewZ = Shader.PropertyToID("gIn_ViewZ");
        private static readonly int gIn_Penumbra = Shader.PropertyToID("gIn_Penumbra");
        private static readonly int gIn_Tiles = Shader.PropertyToID("gIn_Tiles");
        private static readonly int gOut_Tiles = Shader.PropertyToID("gOut_Tiles");
        private static readonly int gIn_Normal_Roughness = Shader.PropertyToID("gIn_Normal_Roughness");
        private static readonly int gOut_Penumbra = Shader.PropertyToID("gOut_Penumbra");
        private static readonly int gIn_Shadow_Translucency = Shader.PropertyToID("gIn_Shadow_Translucency");
        private static readonly int gOut_Shadow_Translucency = Shader.PropertyToID("gOut_Shadow_Translucency");
        private static readonly int gIn_Mv = Shader.PropertyToID("gIn_Mv");
        private static readonly int gIn_History = Shader.PropertyToID("gIn_History");
        private static readonly int gIn_HistoryLength = Shader.PropertyToID("gIn_HistoryLength");
        private static readonly int gOut_History = Shader.PropertyToID("gOut_History");
        private static readonly int gOut_HistoryLength = Shader.PropertyToID("gOut_HistoryLength");

        private static readonly int SIGMA_ClassifyTilesConstantsId = Shader.PropertyToID("SIGMA_ClassifyTilesConstants");
        private static readonly int SIGMA_SmoothTilesConstantsId = Shader.PropertyToID("SIGMA_SmoothTilesConstants");
        private static readonly int SIGMA_BlurConstantsId = Shader.PropertyToID("SIGMA_BlurConstants");
        private static readonly int SIGMA_TemporalStabilizationConstantsId = Shader.PropertyToID("SIGMA_TemporalStabilizationConstants");
        private static readonly int SIGMA_CopyConstantsId = Shader.PropertyToID("SIGMA_CopyConstants");

        [RenderGraphResource(Name = "RawShadow", Access = AccessFlags.Read)]
        private RenderGraphTexture m_RawShadowTexture;

        [RenderGraphResource(Name = "LinearDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "MotionVectors", Access = AccessFlags.Read)]
        private RenderGraphTexture m_MotionVectorTexture;

        [RenderGraphResource(
            Name = "HistoryShadow",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_HistoryShadowTexture;

        [RenderGraphResource(
            Name = "HistoryLength",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_HistoryLengthTexture;

        [RenderGraphResource(Name = "DenoisedShadow", Access = AccessFlags.Write)]
        private RenderGraphTexture m_DenoisedShadowTexture;

        [RenderGraphResource(
            Name = "HistoryShadowOut",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_HistoryShadowOut;

        [RenderGraphResource(
            Name = "HistoryLengthOut",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_HistoryLengthOut;

        private ComputeShader m_ClassifyTiles;
        private ComputeShader m_SmoothTiles;
        private ComputeShader m_ShadowCopy;
        private ComputeShader m_ShadowBlur;
        private ComputeShader m_ShadowPostBlur;
        private ComputeShader m_ShadowTemporalStabilization;

        // Internal transient RTHandles
        private RTHandle m_TileTexture;
        private RTHandle m_SmoothTileTexture;
        private RTHandle m_TransientPenumbra;
        private RTHandle m_TransientShadow;
        private RTHandle m_TransientPenumbra2;
        private RTHandle m_TransientShadow2;
        private RTHandle m_TransientHistory;
        private RTHandle m_TransientHistoryLength;

        private SigmaSharedConstants m_Constants;
        private int m_Width;
        private int m_Height;
        private int m_PrevWidth;
        private int m_PrevHeight;
        private Vector3 m_PrevCameraPosition;
        private Matrix4x4 m_PrevWorldToView = Matrix4x4.identity;
        private Matrix4x4 m_PrevViewToClip = Matrix4x4.identity;
        private bool m_HasValidHistory;

        private const float DefaultPlaneDistSensitivity = 0.02f;
        private const uint DefaultMaxStabilizedFrameNum = 5;

        public SIGMAShadowDenoisePass()
        {
            profilingSampler = new ProfilingSampler(nameof(SIGMAShadowDenoisePass));
            m_RawShadowTexture = CreateInputTexture("RawShadow", GraphicsFormat.R16_SFloat);
            m_DepthTexture = CreateInputTexture("LinearDepth", GraphicsFormat.R32_SFloat);
            m_GBuffer1 = CreateInputTexture("GBuffer1", GraphicsFormat.R16G16_SFloat);
            m_MotionVectorTexture = CreateInputTexture("MotionVectors", GraphicsFormat.R16G16_SFloat);
            m_HistoryShadowTexture = CreateInputTexture("HistoryShadow", GraphicsFormat.R8_UNorm);
            m_HistoryLengthTexture = CreateInputTexture("HistoryLength", GraphicsFormat.R32_UInt);
            m_DenoisedShadowTexture = CreateOutputTexture("DenoisedShadow", GraphicsFormat.R8_UNorm);
            m_HistoryShadowOut = CreateOutputTexture("HistoryShadowOut", GraphicsFormat.R8_UNorm);
            m_HistoryLengthOut = CreateOutputTexture("HistoryLengthOut", GraphicsFormat.R32_UInt);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (resources == null) return;

            m_ClassifyTiles = resources.SIGMAClassifyTilesCompute;
            m_SmoothTiles = resources.SIGMASmoothTilesCompute;
            m_ShadowCopy = resources.SIGMAShadowCopyCompute;
            m_ShadowBlur = resources.SIGMAShadowPreBlurCompute;
            m_ShadowPostBlur = resources.SIGMAShadowPostBlurCompute;
            m_ShadowTemporalStabilization = resources.SIGMATemporalStabilizationCompute;
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_Width = cameraData.actualWidth;
            m_Height = cameraData.actualHeight;

            EnsureTransientTextures(m_Width, m_Height);
            ConfigureOutputTextures(m_Width, m_Height);

            var camera = cameraData.camera;
            var worldToView = camera.worldToCameraMatrix;
            var viewToClip = cameraData.GetGPUProjectionMatrix(renderIntoTexture: true);
            var cameraPos = camera.transform.position;

            // Light direction
            var lightData = frameData.GetOrCreate<VividLightData>();
            var lightDir = Vector3.up;
            if (lightData != null && lightData.hasMainDirectionalLight)
                lightDir = lightData.mainDirectionalLight.directionWS.normalized;

            m_Constants = SigmaSharedConstants.Compute(
                worldToView, viewToClip,
                m_PrevWorldToView, m_PrevViewToClip,
                cameraPos, m_PrevCameraPosition,
                lightDir,
                m_Width, m_Height,
                m_PrevWidth > 0 ? m_PrevWidth : m_Width,
                m_PrevHeight > 0 ? m_PrevHeight : m_Height,
                (uint)Time.frameCount,
                camera.farClipPlane,
                DefaultPlaneDistSensitivity,
                DefaultMaxStabilizedFrameNum / 7f,
                camera.orthographic);

            var hasValidShadowHistory = AllocHistoryTexture(
                HistoryShadowKey,
                m_HistoryShadowTexture,
                null,
                m_HistoryShadowOut?.desc);
            AllocHistoryTexture(
                HistoryShadowKey,
                m_HistoryShadowTexture,
                hasValidShadowHistory ? m_HistoryShadowOut : m_DenoisedShadowTexture,
                hasValidShadowHistory ? m_HistoryShadowOut?.desc : m_DenoisedShadowTexture?.desc);
            var hasValidHistoryLength = AllocHistoryTexture(
                HistoryLengthKey,
                m_HistoryLengthTexture,
                m_HistoryLengthOut,
                m_HistoryLengthOut?.desc);
            m_HasValidHistory = hasValidShadowHistory && hasValidHistoryLength;

            // Store for next frame
            m_PrevWorldToView = worldToView;
            m_PrevViewToClip = viewToClip;
            m_PrevCameraPosition = cameraPos;
            m_PrevWidth = m_Width;
            m_PrevHeight = m_Height;
        }

        public override void Record(UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (!CanExecute())
                    return;

                int kernel = 0;
                int tileW = CoreUtils.DivRoundUp(m_Width, 16);
                int tileH = CoreUtils.DivRoundUp(m_Height, 16);

                // Stage 1: ClassifyTiles
                ConstantBuffer.Push(cmd, m_Constants, m_ClassifyTiles, SIGMA_ClassifyTilesConstantsId);
                cmd.SetComputeTextureParam(m_ClassifyTiles, kernel, gIn_ViewZ, m_DepthTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ClassifyTiles, kernel, gIn_Penumbra, m_RawShadowTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ClassifyTiles, kernel, gOut_Tiles, m_TileTexture);
                cmd.DispatchCompute(m_ClassifyTiles, kernel, tileW, tileH, 1);

                // Stage 2: SmoothTiles
                int smoothTileW = CoreUtils.DivRoundUp(tileW, 16);
                int smoothTileH = CoreUtils.DivRoundUp(tileH, 16);
                ConstantBuffer.Push(cmd, m_Constants, m_SmoothTiles, SIGMA_SmoothTilesConstantsId);
                cmd.SetComputeTextureParam(m_SmoothTiles, kernel, gIn_Tiles, m_TileTexture);
                cmd.SetComputeTextureParam(m_SmoothTiles, kernel, gOut_Tiles, m_SmoothTileTexture);
                cmd.DispatchCompute(m_SmoothTiles, kernel, smoothTileW, smoothTileH, 1);

                int dispatchX = CoreUtils.DivRoundUp(m_Width, 8);
                int dispatchY = CoreUtils.DivRoundUp(m_Height, 16);

                // Stage 3: ShadowCopy (history → transient)
                if (m_HasValidHistory)
                {
                    ConstantBuffer.Push(cmd, m_Constants, m_ShadowCopy, SIGMA_CopyConstantsId);
                    cmd.SetComputeTextureParam(m_ShadowCopy, kernel, gIn_Tiles, m_SmoothTileTexture);
                    cmd.SetComputeTextureParam(m_ShadowCopy, kernel, gIn_History, m_HistoryShadowTexture.innerHandle);
                    cmd.SetComputeTextureParam(m_ShadowCopy, kernel, gIn_HistoryLength, m_HistoryLengthTexture.innerHandle);
                    cmd.SetComputeTextureParam(m_ShadowCopy, kernel, gOut_History, m_TransientHistory);
                    cmd.SetComputeTextureParam(m_ShadowCopy, kernel, gOut_HistoryLength, m_TransientHistoryLength);
                    cmd.DispatchCompute(m_ShadowCopy, kernel, dispatchX, dispatchY, 1);
                }

                // Stage 4: PreBlur
                ConstantBuffer.Push(cmd, m_Constants, m_ShadowBlur, SIGMA_BlurConstantsId);
                cmd.SetComputeTextureParam(m_ShadowBlur, kernel, gIn_ViewZ, m_DepthTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ShadowBlur, kernel, gIn_Normal_Roughness, m_GBuffer1.innerHandle);
                cmd.SetComputeTextureParam(m_ShadowBlur, kernel, gIn_Tiles, m_SmoothTileTexture);
                cmd.SetComputeTextureParam(m_ShadowBlur, kernel, gIn_Penumbra, m_RawShadowTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ShadowBlur, kernel, gOut_Penumbra, m_TransientPenumbra);
                cmd.SetComputeTextureParam(m_ShadowBlur, kernel, gOut_Shadow_Translucency, m_TransientShadow);
                cmd.DispatchCompute(m_ShadowBlur, kernel, dispatchX, dispatchY, 1);

                // Stage 5: PostBlur
                bool useTemporalStabilization = m_HasValidHistory && DefaultMaxStabilizedFrameNum > 0;
                ConstantBuffer.Push(cmd, m_Constants, m_ShadowPostBlur, SIGMA_BlurConstantsId);
                cmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gIn_ViewZ, m_DepthTexture.innerHandle);
                cmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gIn_Normal_Roughness, m_GBuffer1.innerHandle);
                cmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gIn_Tiles, m_SmoothTileTexture);
                cmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gIn_Penumbra, m_TransientPenumbra);
                cmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gIn_Shadow_Translucency, m_TransientShadow);
                cmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gOut_Penumbra, m_TransientPenumbra2);
                cmd.SetComputeTextureParam(m_ShadowPostBlur, kernel, gOut_Shadow_Translucency,
                    useTemporalStabilization ? m_TransientShadow2 : m_DenoisedShadowTexture.innerHandle);
                cmd.DispatchCompute(m_ShadowPostBlur, kernel, dispatchX, dispatchY, 1);

                // Stage 6: TemporalStabilization
                if (useTemporalStabilization)
                {
                    ConstantBuffer.Push(cmd, m_Constants, m_ShadowTemporalStabilization, SIGMA_TemporalStabilizationConstantsId);
                    cmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_ViewZ, m_DepthTexture.innerHandle);
                    cmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_Mv, m_MotionVectorTexture.innerHandle);
                    cmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_Penumbra, m_TransientPenumbra2);
                    cmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_Shadow_Translucency, m_TransientShadow2);
                    cmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_History, m_TransientHistory);
                    cmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_HistoryLength, m_TransientHistoryLength);
                    cmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gIn_Tiles, m_SmoothTileTexture);
                    cmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gOut_Shadow_Translucency, m_DenoisedShadowTexture.innerHandle);
                    cmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gOut_HistoryLength, m_HistoryLengthOut.innerHandle);
                    cmd.SetComputeTextureParam(m_ShadowTemporalStabilization, kernel, gOut_History, m_HistoryShadowOut.innerHandle);
                    cmd.DispatchCompute(m_ShadowTemporalStabilization, kernel, dispatchX, dispatchY, 1);
                }
            }
        }

        public override void Dispose()
        {
            m_TileTexture?.Release();
            m_SmoothTileTexture?.Release();
            m_TransientPenumbra?.Release();
            m_TransientShadow?.Release();
            m_TransientPenumbra2?.Release();
            m_TransientShadow2?.Release();
            m_TransientHistory?.Release();
            m_TransientHistoryLength?.Release();

            m_TileTexture = null;
            m_SmoothTileTexture = null;
            m_TransientPenumbra = null;
            m_TransientShadow = null;
            m_TransientPenumbra2 = null;
            m_TransientShadow2 = null;
            m_TransientHistory = null;
            m_TransientHistoryLength = null;
        }

        private bool CanExecute()
        {
            return m_ClassifyTiles != null
                && m_SmoothTiles != null
                && m_ShadowCopy != null
                && m_ShadowBlur != null
                && m_ShadowPostBlur != null
                && m_ShadowTemporalStabilization != null
                && m_RawShadowTexture != null && m_RawShadowTexture.innerHandle.IsValid()
                && m_DepthTexture != null && m_DepthTexture.innerHandle.IsValid()
                && m_GBuffer1 != null && m_GBuffer1.innerHandle.IsValid()
                && m_DenoisedShadowTexture != null && m_DenoisedShadowTexture.innerHandle.IsValid();
        }

        private void EnsureTransientTextures(int width, int height)
        {
            int tileW = CoreUtils.DivRoundUp(width, 16);
            int tileH = CoreUtils.DivRoundUp(height, 16);

            EnsureRTHandle(ref m_TileTexture, tileW, tileH, GraphicsFormat.R16G16B16A16_SFloat, "SIGMA_TileTexture");
            EnsureRTHandle(ref m_SmoothTileTexture, tileW, tileH, GraphicsFormat.R16G16_SFloat, "SIGMA_SmoothTileTexture");
            EnsureRTHandle(ref m_TransientPenumbra, width, height, GraphicsFormat.R16_SFloat, "SIGMA_TransientPenumbra");
            EnsureRTHandle(ref m_TransientShadow, width, height, GraphicsFormat.R8_UNorm, "SIGMA_TransientShadow");
            EnsureRTHandle(ref m_TransientPenumbra2, width, height, GraphicsFormat.R16_SFloat, "SIGMA_TransientPenumbra2");
            EnsureRTHandle(ref m_TransientShadow2, width, height, GraphicsFormat.R8_UNorm, "SIGMA_TransientShadow2");
            EnsureRTHandle(ref m_TransientHistory, width, height, GraphicsFormat.R8_UNorm, "SIGMA_TransientHistory");
            EnsureRTHandle(ref m_TransientHistoryLength, width, height, GraphicsFormat.R32_UInt, "SIGMA_TransientHistoryLength");
        }

        private static void EnsureRTHandle(ref RTHandle handle, int width, int height, GraphicsFormat format, string name)
        {
            if (handle != null && handle.rt != null && handle.rt.width == width && handle.rt.height == height)
                return;

            handle?.Release();
            handle = RTHandles.Alloc(width, height, colorFormat: format, enableRandomWrite: true, name: name);
        }

        private void ConfigureOutputTextures(int width, int height)
        {
            ConfigureTexture(m_DenoisedShadowTexture, width, height, clearBuffer: false);
            ConfigureTexture(m_HistoryShadowOut, width, height, clearBuffer: true);
            ConfigureTexture(m_HistoryLengthOut, width, height, clearBuffer: true);
        }

        private static void ConfigureTexture(RenderGraphTexture tex, int width, int height, bool clearBuffer)
        {
            if (tex?.desc == null) return;
            tex.desc.Width = width;
            tex.desc.Height = height;
            tex.desc.EnableRandomWrite = true;
            tex.desc.ClearBuffer = clearBuffer;
            tex.desc.ClearColor = Color.clear;
        }

        private static RenderGraphTexture CreateInputTexture(string name, GraphicsFormat format, DepthBits depthBits = DepthBits.None)
        {
            var texture = new RenderGraphTexture
            {
                desc = format == GraphicsFormat.None
                    ? RenderGraphTextureDesc.CreateDepthTarget(1, 1, depthBits)
                    : RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = false;
            return texture;
        }

        private static RenderGraphTexture CreateOutputTexture(string name, GraphicsFormat format)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = true;
            return texture;
        }
    }
}
