using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    [FrameResourceSlot(FrameResourceSlots.LumenDebugOutputName, AccessFlags.Read, Optional = true)]
    public class FinalBlitPass : UnsafePass, IPostProcessSourceOverridePass, IStablePassResourceLayout, IRenderGraphPreparePass, IRenderGraphSideEffectPass
    {
        private static readonly int HDROutputParamsId = Shader.PropertyToID("_HDROutputParams");

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        private Material m_Material;
        private ColorGradingSettingsData m_ColorGradingSettings;
        private RenderTargetIdentifier m_CameraBackBufferTarget;
        private TextureUVOrigin m_CameraBackBufferTextureUVOrigin;
        private bool m_ShouldSetViewport;
        private Rect m_Viewport;
        private bool m_IsPassResourceLayoutDirty;
        private RenderGraphTexture m_OriginalSource;
        private bool m_HasSourceTextureOverride;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        internal RenderGraphTexture GetSourceTexture()
        {
            return source;
        }

        internal void SetSourceTexture(RenderGraphTexture sourceTexture)
        {
            if (sourceTexture == null)
                throw new ArgumentNullException(nameof(sourceTexture));

            if (ReferenceEquals(source, sourceTexture))
                return;

            if (!m_HasSourceTextureOverride)
            {
                m_OriginalSource = source;
            }

            source = sourceTexture;
            m_HasSourceTextureOverride = true;
            m_IsPassResourceLayoutDirty = true;
        }

        internal void RestoreSourceTexture()
        {
            if (!m_HasSourceTextureOverride)
                return;

            if (!ReferenceEquals(source, m_OriginalSource) && m_OriginalSource != null)
            {
                source = m_OriginalSource;
                m_IsPassResourceLayoutDirty = true;
            }

            m_OriginalSource = null;
            m_HasSourceTextureOverride = false;
        }

        RenderGraphTexture IPostProcessSourceOverridePass.GetSourceTexture() => GetSourceTexture();

        void IPostProcessSourceOverridePass.SetSourceTexture(RenderGraphTexture sourceTexture) => SetSourceTexture(sourceTexture);

        void IPostProcessSourceOverridePass.RestoreSourceTexture() => RestoreSourceTexture();

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData?.camera;
            var hasTargetTexture = camera != null && camera.targetTexture != null;
            var cameraType = camera != null ? camera.cameraType : CameraType.Game;
            m_CameraBackBufferTarget = hasTargetTexture
                ? new RenderTargetIdentifier(camera.targetTexture)
                : BuiltinRenderTextureType.CameraTarget;
            m_CameraBackBufferTextureUVOrigin = GetCameraBackBufferTextureUVOrigin(cameraType, hasTargetTexture);
            m_ShouldSetViewport = ShouldSetViewport(cameraType);
            m_Viewport = cameraData != null
                ? GetViewport(cameraData)
                : new Rect(0f, 0f, Screen.width, Screen.height);
            var postProcessingAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);
            m_ColorGradingSettings = postProcessingAllowed
                && ColorGradingSettingsResolver.TryGetResolved(frameData, out var settings, out _)
                ? settings
                : ColorGradingSettingsResolver.ResolveHDROutput(frameData);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Material = CoreUtils.CreateEngineMaterial(resources.FinalBlitShader);
        }

        public void PrepareRenderGraph(ContextContainer frameData)
        {
            if (FrameResourceTable.TryGet(frameData, out var frameResources)
                && frameResources.UseTextureSlotForPass(this, FrameResourceSlots.LumenDebugOutput, AccessFlags.Read, out _))
            {
                return;
            }

            if (frameData.Contains<VividLumenDebugFrameData>())
                PassRecorder.RegisterImportedTextureForPass(this, frameData.Get<VividLumenDebugFrameData>().Output);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_Material == null)
                return;

            RTHandle sourceHandle = source.innerHandle;
            if (sourceHandle == null)
                return;

            ConfigureMaterialHDROutput(
                m_Material,
                m_ColorGradingSettings.hdrOutputActive,
                m_ColorGradingSettings.hdrDisplayColorGamut);
            m_Material.SetVector(HDROutputParamsId, m_ColorGradingSettings.hdrOutputParameters);

            var cmd = context.cmd;
            var unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);
            var sourceTextureUVOrigin = context.GetTextureUVOrigin(source.innerHandle);
            var scaleBias = sourceHandle.GetScaleBias(sourceTextureUVOrigin, m_CameraBackBufferTextureUVOrigin);
            var debug = context.TryGet<VividLumenDebugFrameData>(out var debugFrame) && debugFrame.Produced
                ? debugFrame : null;
            cmd.SetRenderTarget(m_CameraBackBufferTarget);
            if (m_ShouldSetViewport)
                cmd.SetViewport(m_Viewport);
            BlitDisplay(context, unsafeCmd, sourceHandle, scaleBias, m_Viewport, m_CameraBackBufferTextureUVOrigin, debug);

#if UNITY_EDITOR
            var camera = context.Get<VividCameraData>()?.camera;
            if (VividAdditionalCameraData.TryGetFinalFrameScreenshotCaptureTarget(camera, out var screenshotTarget))
            {
                var screenshotScaleBias = sourceHandle.GetScaleBias(sourceTextureUVOrigin, TextureUVOrigin.BottomLeft);
                cmd.SetRenderTarget(screenshotTarget);
                var screenshotViewport = new Rect(0f, 0f, screenshotTarget.width, screenshotTarget.height);
                cmd.SetViewport(screenshotViewport);
                BlitDisplay(context, unsafeCmd, sourceHandle, screenshotScaleBias, screenshotViewport, TextureUVOrigin.BottomLeft, debug);
                VividAdditionalCameraData.MarkFinalFrameScreenshotCaptureTargetWritten(camera);

                cmd.SetRenderTarget(m_CameraBackBufferTarget);
                if (m_ShouldSetViewport)
                    cmd.SetViewport(m_Viewport);
            }
#endif
        }

        private void BlitDisplay(UnsafePassContext context, CommandBuffer cmd, RTHandle sourceHandle,
            Vector4 scaleBias, Rect viewport, TextureUVOrigin targetOrigin, VividLumenDebugFrameData debug)
        {
            RTHandle debugHandle = debug?.Output ?? default;
            bool overview = debugHandle != null && LumenDebugViewPass.IsOverview(debug.Mode);
            if (debugHandle == null || overview)
                Blitter.BlitTexture(cmd, sourceHandle, scaleBias, m_Material, 0);
            if (debugHandle == null)
                return;
            if (overview)
                cmd.SetViewport(LumenDebugViewPass.GetOverviewViewport(viewport));
            var debugScaleBias = debugHandle.GetScaleBias(context.GetTextureUVOrigin(debug.Output), targetOrigin);
            Blitter.BlitTexture(cmd, debugHandle, debugScaleBias, m_Material, 0);
            if (overview)
                cmd.SetViewport(viewport);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
        }

        private static TextureUVOrigin GetCameraBackBufferTextureUVOrigin(CameraType cameraType, bool hasTargetTexture)
        {
            var useActualBackbufferOrientation = cameraType != CameraType.SceneView
                && cameraType != CameraType.Preview
                && !hasTargetTexture;

            if (!useActualBackbufferOrientation)
                return TextureUVOrigin.BottomLeft;

            return SystemInfo.graphicsUVStartsAtTop ? TextureUVOrigin.TopLeft : TextureUVOrigin.BottomLeft;
        }

        private static bool ShouldSetViewport(CameraType cameraType)
        {
            return cameraType != CameraType.SceneView;
        }

        private static void ConfigureMaterialHDROutput(Material material, bool hdrOutputActive, ColorGamut gamut)
        {
            if (material == null)
                return;

            if (hdrOutputActive)
            {
                HDROutputUtils.ConfigureHDROutput(
                    material,
                    gamut,
                    HDROutputUtils.Operation.ColorEncoding);
                return;
            }

            CoreUtils.SetKeyword(material, HDROutputUtils.ShaderKeywords.HDR_ENCODING, false);
            CoreUtils.SetKeyword(material, HDROutputUtils.ShaderKeywords.HDR_COLORSPACE_CONVERSION, false);
            CoreUtils.SetKeyword(material, HDROutputUtils.ShaderKeywords.HDR_COLORSPACE_CONVERSION_AND_ENCODING, false);
            CoreUtils.SetKeyword(material, HDROutputUtils.ShaderKeywords.HDR_INPUT, false);
        }

        private static Rect GetViewport(VividCameraData cameraData)
        {
            if (cameraData.pixelRect.width > 0f && cameraData.pixelRect.height > 0f)
                return cameraData.pixelRect;

            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0 || height <= 0)
                return new Rect(0f, 0f, Screen.width, Screen.height);

            return new Rect(0f, 0f, width, height);
        }

    }
}
