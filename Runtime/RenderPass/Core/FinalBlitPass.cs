using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class FinalBlitPass : UnsafePass
    {
        private static readonly int ColorGradingLutId = Shader.PropertyToID("_VividColorGradingLut");
        private static readonly int ColorGradingParamsId = Shader.PropertyToID("_VividColorGradingParams");
        private static readonly int FilmGrainTextureId = Shader.PropertyToID("_VividFilmGrainTexture");
        private static readonly int FilmGrainParamsId = Shader.PropertyToID("_VividFilmGrainParams");
        private static readonly int FilmGrainTexParamsId = Shader.PropertyToID("_VividFilmGrainTexParams");

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        [RenderGraphResource(Name = "ColorGradingTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture colorGradingLut = new();

        private Material m_Material;
        private ColorGradingSettingsData m_ColorGradingSettings;
        private FilmGrainSettingsData m_FilmGrainSettings;
        private RenderTargetIdentifier m_CameraBackBufferTarget;
        private TextureUVOrigin m_CameraBackBufferTextureUVOrigin;
        private bool m_ShouldSetViewport;
        private bool m_PostProcessingAllowed;
        private Rect m_Viewport;
        private int m_FrameCount;

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData.camera;
            var hasTargetTexture = camera != null && camera.targetTexture != null;
            var cameraType = camera != null ? camera.cameraType : CameraType.Game;

            m_CameraBackBufferTarget = hasTargetTexture
                ? new RenderTargetIdentifier(camera.targetTexture)
                : BuiltinRenderTextureType.CameraTarget;
            m_CameraBackBufferTextureUVOrigin = GetCameraBackBufferTextureUVOrigin(cameraType, hasTargetTexture);
            m_ShouldSetViewport = ShouldSetViewport(cameraType);

            m_Viewport = GetViewport(cameraData);
            m_PostProcessingAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);
            m_ColorGradingSettings = m_PostProcessingAllowed
                ? ColorGradingSettingsResolver.Resolve()
                : ColorGradingSettingsData.CreateDefault();
            m_FilmGrainSettings = m_PostProcessingAllowed
                ? FilmGrainSettingsResolver.Resolve()
                : FilmGrainSettingsData.CreateDefault();

            m_FrameCount = Time.frameCount;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();

            m_Material = CoreUtils.CreateEngineMaterial(resources.FinalBlitShader);
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (m_Material == null)
                return;

            var cmd = context.cmd;
            var unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);
            RTHandle sourceHandle = source.innerHandle;
            if (sourceHandle == null)
                return;

            var scale = Vector2.one;

            if (sourceHandle != null && sourceHandle.useScaling)
            {
                scale.x = sourceHandle.rtHandleProperties.rtHandleScale.x;
                scale.y = sourceHandle.rtHandleProperties.rtHandleScale.y;
            }

            var useColorGradingLut = m_PostProcessingAllowed
                && m_ColorGradingSettings.RequiresLut
                && colorGradingLut != null
                && colorGradingLut.innerHandle.IsValid();

            m_Material.SetVector(
                ColorGradingParamsId,
                new Vector4(
                    1f / ColorGradingLutBuilder.LutSize,
                    ColorGradingLutBuilder.LutSize - 1f,
                    useColorGradingLut ? 1f : 0f,
                    m_ColorGradingSettings.postExposureLinear));

            if (useColorGradingLut)
                cmd.SetGlobalTexture(ColorGradingLutId, colorGradingLut.innerHandle);

            // Film Grain
            if (m_FilmGrainSettings.enabled && m_FilmGrainSettings.texture != null)
            {
                m_Material.SetTexture(FilmGrainTextureId, m_FilmGrainSettings.texture);
                m_Material.SetVector(FilmGrainParamsId, new Vector4(
                    m_FilmGrainSettings.intensity,
                    m_FilmGrainSettings.response,
                    0f, 0f));

                var texWidth = (float)m_FilmGrainSettings.texture.width;
                var texHeight = (float)m_FilmGrainSettings.texture.height;
                var screenWidth = m_Viewport.width > 0f ? m_Viewport.width : Screen.width;
                var screenHeight = m_Viewport.height > 0f ? m_Viewport.height : Screen.height;

                // Per-frame random offset to avoid static tiling
                var offsetX = (HashFrame(m_FrameCount, 0) % 1024) / 1024f;
                var offsetY = (HashFrame(m_FrameCount, 1) % 1024) / 1024f;

                m_Material.SetVector(FilmGrainTexParamsId, new Vector4(
                    screenWidth / texWidth,
                    screenHeight / texHeight,
                    offsetX,
                    offsetY));

                CoreUtils.SetKeyword(m_Material, "_FILM_GRAIN", true);
            }
            else
            {
                CoreUtils.SetKeyword(m_Material, "_FILM_GRAIN", false);
            }

            var sourceTextureUVOrigin = context.GetTextureUVOrigin(source.innerHandle);
            var scaleBias = GetFinalBlitScaleBias(scale, sourceTextureUVOrigin, m_CameraBackBufferTextureUVOrigin);

            cmd.SetRenderTarget(m_CameraBackBufferTarget);
            if (m_ShouldSetViewport)
                cmd.SetViewport(m_Viewport);

            Blitter.BlitTexture(unsafeCmd, sourceHandle, scaleBias, m_Material, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
        }

        private static long HashFrame(int frame, int state)
        {
            long hash = frame * 747796405 + 2891336453 + state * 197;
            hash = ((hash >> 16) ^ hash) * 45679;
            hash = ((hash >> 16) ^ hash) * 45679;
            hash = (hash >> 16) ^ hash;
            return hash & 0x7FFFFFFF;
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

        private static Vector4 GetFinalBlitScaleBias(
            Vector2 scale,
            TextureUVOrigin sourceTextureUVOrigin,
            TextureUVOrigin destinationTextureUVOrigin)
        {
            var yFlip = sourceTextureUVOrigin != destinationTextureUVOrigin;
            return yFlip
                ? new Vector4(scale.x, -scale.y, 0f, scale.y)
                : new Vector4(scale.x, scale.y, 0f, 0f);
        }
    }
}
