using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class MotionBlurPass
    {
        Material material;

        private class MotionBlurPassData
        {
            internal TextureHandle destinationTexture;
            internal TextureHandle sourceTexture;
            internal TextureHandle motionVectors;
            internal Material material;
            internal int passIndex;
            internal Camera camera;
            internal XRPass xr;
            internal float intensity;
            internal float clamp;
            internal bool enableAlphaOutput;
        }

        internal static readonly int k_ShaderPropertyId_ViewProjM = Shader.PropertyToID("_ViewProjM");
        internal static readonly int k_ShaderPropertyId_PrevViewProjM = Shader.PropertyToID("_PrevViewProjM");
        internal static readonly int k_ShaderPropertyId_ViewProjMStereo = Shader.PropertyToID("_ViewProjMStereo");
        internal static readonly int k_ShaderPropertyId_PrevViewProjMStereo = Shader.PropertyToID("_PrevViewProjMStereo");

        internal static void UpdateMotionBlurMatrices(ref Material material, Camera camera, XRPass xr)
        {
            MotionVectorsPersistentData motionData = null;

            if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var additionalCameraData))
                motionData = additionalCameraData.motionVectorsPersistentData;

            if (motionData == null)
                return;

#if ENABLE_VR && ENABLE_XR_MODULE
            if (xr.enabled && xr.singlePassEnabled)
            {
                material.SetMatrixArray(k_ShaderPropertyId_PrevViewProjMStereo, motionData.previousViewProjectionStereo);
                material.SetMatrixArray(k_ShaderPropertyId_ViewProjMStereo, motionData.viewProjectionStereo);
            }
            else
#endif
            {
                int viewProjMIdx = 0;
#if ENABLE_VR && ENABLE_XR_MODULE
                if (xr.enabled)
                    viewProjMIdx = xr.multipassId;
#endif

                // TODO: These should be part of URP main matrix set. For now, we set them here for motion vector rendering.
                material.SetMatrix(k_ShaderPropertyId_PrevViewProjM, motionData.previousViewProjectionStereo[viewProjMIdx]);
                material.SetMatrix(k_ShaderPropertyId_ViewProjM, motionData.viewProjectionStereo[viewProjMIdx]);
            }
        }


        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            if (!material)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<PostProcessingRuntimeShader>();
                material = CoreUtils.CreateEngineMaterial(runtimeShader.cameraMotionBlur);
            }

            MotionBlur motionBlur = VolumeManager.instance.stack.GetComponent<MotionBlur>();

            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            
            
            
            bool useMotionBlur = motionBlur.IsActive() ;//&& !isSceneViewCamera;

            // Disable MotionBlur in EditMode, so that editing remains clear and readable.
            // NOTE: HDRP does the same via CoreUtils::AreAnimatedMaterialsEnabled().
            // Disable MotionBlurMode.CameraAndObjects on renderers that do not support motion vectors
            useMotionBlur = useMotionBlur && Application.isPlaying;
            if (useMotionBlur && motionBlur.mode.value == MotionBlurMode.CameraAndObjects)
            {
                useMotionBlur &= UniversalRenderer.current.SupportsMotionVectors();
                if (!useMotionBlur)
                {
                    var warning = "Disabling Motion Blur for Camera And Objects because the renderer does not implement motion vectors.";
                    const int warningThrottleFrames = 60 * 1; // 60 FPS * 1 sec
                    if (Time.frameCount % warningThrottleFrames == 0)
                        Debug.LogWarning(warning);
                    return source;
                }
            }

            
            var destination = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                enableRandomWrite = true,
                name = "_MotionBlurTarget",
                filterMode = FilterMode.Bilinear,
                clearBuffer = true,
                format = renderGraph.GetTextureDesc(source).format
            });


            TextureHandle motionVectorColor = resourceData.motionVectorColor;
            TextureHandle cameraDepthTexture = resourceData.cameraDepthTexture;

            var mode = motionBlur.mode.value;
            int passIndex = (int)motionBlur.quality.value;
            passIndex += (mode == MotionBlurMode.CameraAndObjects) ? 3 : 0;

            using (var builder = renderGraph.AddRasterRenderPass<MotionBlurPassData>("Motion Blur", out var passData,
                       ProfilingSampler.Get(URPProfileId.RG_MotionBlur)))
            {
                builder.AllowGlobalStateModification(true);
                passData.destinationTexture = destination;
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                passData.sourceTexture = source;
                builder.UseTexture(source, AccessFlags.Read);

                if (mode == MotionBlurMode.CameraAndObjects)
                {
                    Debug.Assert(ScriptableRenderer.current.SupportsMotionVectors(), "Current renderer does not support motion vectors.");
                    Debug.Assert(motionVectorColor.IsValid(), "Motion vectors are invalid. Per-object motion blur requires a motion vector texture.");

                    passData.motionVectors = motionVectorColor;
                    builder.UseTexture(motionVectorColor, AccessFlags.Read);
                }
                else
                {
                    passData.motionVectors = TextureHandle.nullHandle;
                }

                Debug.Assert(cameraDepthTexture.IsValid(), "Camera depth texture is invalid. Per-camera motion blur requires a depth texture.");
                builder.UseTexture(cameraDepthTexture, AccessFlags.Read);
                passData.material = material;
                passData.passIndex = passIndex;
                passData.camera = cameraData.camera;
                passData.xr = cameraData.xr;
                passData.enableAlphaOutput = cameraData.isAlphaOutputEnabled;
                passData.intensity = motionBlur.intensity.value;
                passData.clamp = motionBlur.clamp.value;
                builder.SetRenderFunc(static (MotionBlurPassData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    RTHandle sourceTextureHdl = data.sourceTexture;

                    UpdateMotionBlurMatrices(ref data.material, data.camera, data.xr);

                    data.material.SetFloat("_Intensity", data.intensity);
                    data.material.SetFloat("_Clamp", data.clamp);
                    CoreUtils.SetKeyword(data.material, ShaderKeywordStrings._ENABLE_ALPHA_OUTPUT, data.enableAlphaOutput);

                    PostProcessUtils.SetSourceSize(cmd, data.sourceTexture);
                    Vector2 viewportScale = sourceTextureHdl.useScaling
                        ? new Vector2(sourceTextureHdl.rtHandleProperties.rtHandleScale.x, sourceTextureHdl.rtHandleProperties.rtHandleScale.y)
                        : Vector2.one;
                    Blitter.BlitTexture(cmd, sourceTextureHdl, viewportScale, data.material, data.passIndex);
                });


                return destination;
            }
        }
    }
}