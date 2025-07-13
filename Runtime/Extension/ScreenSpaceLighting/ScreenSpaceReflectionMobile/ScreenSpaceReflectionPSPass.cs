using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class ScreenSpaceReflectionMobilePass : ScriptableRenderPass
    {
        const string m_ProfilerTag = "Screen Space Reflection";

        private static readonly int minSmoothness = Shader.PropertyToID("_MinSmoothness");
        private static readonly int fadeSmoothness = Shader.PropertyToID("_FadeSmoothness");
        private static readonly int edgeFade = Shader.PropertyToID("_EdgeFade");
        private static readonly int thickness = Shader.PropertyToID("_Thickness");
        private static readonly int stepSize = Shader.PropertyToID("_StepSize");
        private static readonly int stepSizeMultiplier = Shader.PropertyToID("_StepSizeMultiplier");
        private static readonly int maxStep = Shader.PropertyToID("_MaxStep");
        private static readonly int downSample = Shader.PropertyToID("_DownSample");
        private static readonly int accumFactor = Shader.PropertyToID("_AccumulationFactor");

        private Material material;
        private TextureHandle sourceHandle;
        private TextureHandle historyHandle;
        private TextureHandle reflectHandle;

        private Material SSRMaterial => material ??= new Material(Shader.Find("ScreenSpaceReflection"));

        public ScreenSpaceReflectionMobilePass()
        {

            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;


        }

        class PassData
        {
            internal Material material;
            internal bool isPBRAccumulation;
            internal float accumFactor;
            internal TextureHandle sourceHandle;
            internal TextureHandle reflectHandle;
            internal TextureHandle historyHandle;
            internal TextureHandle cameraColorHandle;
        }


        void Setup(ScreenSpaceReflectionMobile setting)
        {
            if (setting.quality.value == ScreenSpaceReflectionMobile.Quality.Low)
            {
                SSRMaterial.SetFloat(stepSize, 0.4f);
                SSRMaterial.SetFloat(stepSizeMultiplier, 1.33f);
                SSRMaterial.SetFloat(maxStep, 16);
            }
            else if (setting.quality.value == ScreenSpaceReflectionMobile.Quality.Medium)
            {
                SSRMaterial.SetFloat(stepSize, 0.3f);
                SSRMaterial.SetFloat(stepSizeMultiplier, 1.33f);
                SSRMaterial.SetFloat(maxStep, 32);
            }
            else if (setting.quality.value == ScreenSpaceReflectionMobile.Quality.High)
            {
                SSRMaterial.SetFloat(stepSize, 0.2f);
                SSRMaterial.SetFloat(stepSizeMultiplier, 1.33f);
                SSRMaterial.SetFloat(maxStep, 64);
            }
            else
            {
                SSRMaterial.SetFloat(stepSize, 0.2f);
                SSRMaterial.SetFloat(stepSizeMultiplier, 1.1f);
                SSRMaterial.SetFloat(maxStep, setting.maxStep.value);
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var setting = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflectionMobile>();
            if (setting == null || !setting.IsActive())
            {
                return;
            }

            Setup(setting);
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.useMipMap = false;


            if (setting.algorithm == ScreenSpaceReflectionMobile.Algorithm.PBRAccumulation)
            {
                RenderTextureDescriptor descHit = desc;
                descHit.width = (int)setting.resolution.value * (int)(desc.width * 0.25f);
                descHit.height = (int)setting.resolution.value * (int)(desc.height * 0.25f);
                descHit.colorFormat = RenderTextureFormat.ARGBHalf; // Store "hitUV.xy" + "fresnel.z"
                reflectHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc,
                    name: "_ScreenSpaceReflectionHitTexture", false);
                historyHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc,
                    name: "_ScreenSpaceReflectionHistoryTexture", false);
                sourceHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc,
                    name: "_ScreenSpaceReflectionSourceTexture", false);
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);
            }
            else
            {
                RenderTextureDescriptor descHit = desc;
                descHit.width = (int)setting.resolution.value * (int)(desc.width * 0.25f);
                descHit.height = (int)setting.resolution.value * (int)(desc.height * 0.25f);
                descHit.colorFormat = RenderTextureFormat.ARGBHalf; // Store "hitUV.xy" + "fresnel.z"
                sourceHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc,
                    name: "_ScreenSpaceReflectionSourceTexture", false);

                desc.width = (int)setting.resolution.value * (int)(desc.width * 0.25f);
                desc.height = (int)setting.resolution.value * (int)(desc.height * 0.25f);
                desc.useMipMap = (setting.mipmapsMode == ScreenSpaceReflectionMobile.MipmapsMode.Trilinear);
                FilterMode filterMode = (setting.mipmapsMode == ScreenSpaceReflectionMobile.MipmapsMode.Trilinear)
                    ? FilterMode.Trilinear
                    : FilterMode.Point;
                reflectHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc,
                    name: "_ScreenSpaceReflectionColorTexture", false, filterMode: filterMode);


                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            bool isPbrAccumulation = setting.algorithm == ScreenSpaceReflectionMobile.Algorithm.PBRAccumulation;
            if (isPbrAccumulation)
            {
                SSRMaterial.SetFloat(accumFactor, setting.accumFactor.value);
            }

            if (setting.mipmapsMode == ScreenSpaceReflectionMobile.MipmapsMode.Trilinear)
                SSRMaterial.EnableKeyword("_SSR_APPROX_COLOR_MIPMAPS");
            else
                SSRMaterial.DisableKeyword("_SSR_APPROX_COLOR_MIPMAPS");

            SSRMaterial.SetFloat(minSmoothness, setting.minSmoothness.value);
            SSRMaterial.SetFloat(fadeSmoothness,
                setting.fadeSmoothness.value <= setting.minSmoothness.value
                    ? setting.minSmoothness.value + 0.01f
                    : setting.fadeSmoothness.value);
            SSRMaterial.SetFloat(edgeFade, setting.edgeFade.value);
            SSRMaterial.SetFloat(thickness, setting.thickness.value);
            SSRMaterial.SetFloat(downSample, (float)setting.resolution.value * 0.25f);

            using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData))
            {
                passData.material = SSRMaterial;
                passData.isPBRAccumulation = isPbrAccumulation;
                passData.accumFactor = setting.accumFactor.value;
                passData.sourceHandle = sourceHandle;
                if (isPbrAccumulation)
                {
                    passData.historyHandle = historyHandle;
                    builder.UseTexture(passData.historyHandle, AccessFlags.Write);
                }

                passData.reflectHandle = reflectHandle;
                passData.cameraColorHandle = resourceData.activeColorTexture;


                builder.UseTexture(passData.sourceHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.reflectHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.cameraColorHandle, AccessFlags.ReadWrite);


                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }

        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            if (data.isPBRAccumulation)
            {
                data.material.SetFloat(accumFactor, data.accumFactor);
                Blitter.BlitCameraTexture(cmd, data.cameraColorHandle, data.sourceHandle, data.material, pass: 2);
                data.material.SetTexture("_ScreenSpaceReflectionHitTexture", data.sourceHandle);
                data.material.SetTexture("_ScreenSpaceReflectionHistoryTexture", data.historyHandle);

                // Resolve Color
                Blitter.BlitCameraTexture(cmd, data.cameraColorHandle, data.reflectHandle, data.material, pass: 3);
                // Blit to Screen (required by denoiser)
                Blitter.BlitCameraTexture(cmd, data.reflectHandle, data.cameraColorHandle);
                if (data.accumFactor > 0f)
                {
                    Blitter.BlitCameraTexture(cmd, data.reflectHandle, data.cameraColorHandle, data.material, pass: 4);

                    // We need to Load & Store the history texture, or it will not be stored on some platforms.
                    cmd.SetRenderTarget(
                        data.historyHandle,
                        RenderBufferLoadAction.Load,
                        RenderBufferStoreAction.Store,
                        data.historyHandle,
                        RenderBufferLoadAction.DontCare,
                        RenderBufferStoreAction.DontCare);
                    // Update History
                    Blitter.BlitCameraTexture(cmd, data.cameraColorHandle, data.historyHandle);
                }
            }
            else
            {
                // Copy Scene Color
                Blitter.BlitCameraTexture(cmd, data.cameraColorHandle, data.sourceHandle);
                // Screen Space Reflection
                Blitter.BlitCameraTexture(cmd, data.sourceHandle, data.reflectHandle, data.material, pass: 0);
                // Combine Color
                Blitter.BlitCameraTexture(cmd, data.reflectHandle, data.cameraColorHandle, data.material, pass: 1);
            }
        }
    }
}