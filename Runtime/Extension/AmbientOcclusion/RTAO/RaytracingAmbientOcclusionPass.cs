using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class RaytracingAmbientOcclusionPass : ScriptableRenderPass
    {
        public RaytracingAmbientOcclusionPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
        }


        class PassData
        {
            internal TextureHandle NormalTexture;
            internal TextureHandle DepthTexture;

            internal TextureHandle AOTexture;


            // Ray Tracing
            internal ComputeShader rtaoShader;
            internal int RTAOKernelID;

            internal RayTracingAccelerationStructure rtas;
            internal uint dispatchRaySizeX;
            internal uint dispatchRaySizeY;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal BlueNoiseSystem.DitheredTextureHandleSet ditheredTextureHandleSet;

            internal float intensity;
            internal float radius;
            internal float directLightingStrength;
            internal int sampleCount;
            internal int frameIndex;
        }

        private void InitRayTracingPassData(
            RenderGraph renderGraph,
            PassData passData,
            RaytracingData raytracingData,
            UniversalCameraData cameraData,
            UniversalResourceData resourceData)
        {
            var stack = VolumeManager.instance.stack;
            var volumeSettings = stack.GetComponent<RaytracingAmbientOcclusion>();
            if (!volumeSettings)
            {
                return;
            }

            var historyRT = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);


            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<RaytracingAmbientOcclusionRuntimeShader>();

            passData.NormalTexture = resourceData.cameraNormalsTexture;
            passData.DepthTexture = resourceData.cameraDepthTexture;
            passData.rtaoShader = runtimeShaders.raytracingAmbientOcclusionShader;
            passData.RTAOKernelID = passData.rtaoShader.FindKernel("RTAO");

            passData.rtas = raytracingData.rayTracingSystem.RequestAccelerationStructure();

            var width = cameraData.cameraTargetDescriptor.width;
            var height = cameraData.cameraTargetDescriptor.height;
            passData.dispatchRaySizeX = (uint)width;
            passData.dispatchRaySizeY = (uint)height;

            passData.AOTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R16_SFloat,
                name = "Raytracing AO Texture"
            });
            // RayTracing constant buffer
            {
                var rayTracingSettings = stack.GetComponent<RayTracingSettings>();

                passData.rayTracingCB = raytracingData.rayTracingSystem.GetShaderVariablesRaytracingCB(new Vector2Int(width, height), rayTracingSettings);
                passData.rayTracingCB._RaytracingRayMaxLength = rayTracingSettings.directionalShadowRayLength.value;
                passData.rayTracingCB._RayTracingClampingFlag = 1;
                passData.rayTracingCB._RaytracingIntensityClamp = 1.0f;
                passData.rayTracingCB._RaytracingPreExposition = 0;
                passData.rayTracingCB._RayTracingDiffuseLightingOnly = 0;
                passData.rayTracingCB._RayTracingAPVRayMiss = 0;
                passData.rayTracingCB._RayTracingRayMissFallbackHierarchy = 0;
                passData.rayTracingCB._RayTracingRayMissUseAmbientProbeAsSky = 0;
                passData.rayTracingCB._RayTracingLastBounceFallbackHierarchy = 0;
                passData.rayTracingCB._RayTracingAmbientProbeDimmer = 1.0f;
            }
            passData.ditheredTextureHandleSet = BlueNoiseSystem.instance.DitheredTextureSet8SPP().RenderGraphImport(renderGraph);


            passData.frameIndex = historyRT.historyFrameCount;
            passData.sampleCount = volumeSettings.samplesPerPixel.value;
            passData.radius = volumeSettings.radius.value;
            passData.intensity = volumeSettings.intensity.value;
            passData.directLightingStrength= volumeSettings.directLightingStrength.value;
        }


        static class ShaderConstants
        {


            public static readonly int AmbientOcclusionTexture = Shader.PropertyToID("AmbientOcclusionTexture");
            public static readonly int SceneDepth = Shader.PropertyToID("SceneDepth");
            public static readonly int SceneNormal = Shader.PropertyToID("SceneNormal");

            public static readonly int radius = Shader.PropertyToID("radius");
            public static readonly int sampleCount = Shader.PropertyToID("sampleCount");
            public static readonly int intensity = Shader.PropertyToID("intensity");
            public static readonly int frameIndex = Shader.PropertyToID("frameIndex");
            public static readonly int _AccelerationStructure = Shader.PropertyToID("_AccelerationStructure");

        }

        public void Setup()
        {
            ConfigureInput(ScriptableRenderPassInput.Normal);
        }


        private static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            using (new ProfilingScope(cmd, new ProfilingSampler("RayTracingShadows")))
            {
                // Define the shader pass to use for the reflection pass

                // Set the acceleration structure for the pass
                cmd.SetRayTracingAccelerationStructure(data.rtaoShader, data.RTAOKernelID,ShaderConstants._AccelerationStructure, data.rtas);

                // SetConstantBuffer
                ConstantBuffer.PushGlobal(cmd, data.rayTracingCB, RayTracingSystem._ShaderVariablesRaytracing);

                BlueNoiseSystem.BindDitheredTextureSet(cmd, data.ditheredTextureHandleSet);
                // SetTextures
                cmd.SetComputeTextureParam(data.rtaoShader,data.RTAOKernelID, ShaderConstants.SceneDepth, data.DepthTexture);
                cmd.SetComputeTextureParam(data.rtaoShader,data.RTAOKernelID, ShaderConstants.SceneNormal, data.NormalTexture);
                cmd.SetComputeTextureParam(data.rtaoShader,data.RTAOKernelID, ShaderConstants.AmbientOcclusionTexture, data.AOTexture);

                cmd.SetComputeIntParam(data.rtaoShader, ShaderConstants.sampleCount, data.sampleCount);
                cmd.SetComputeIntParam(data.rtaoShader, ShaderConstants.frameIndex, data.frameIndex);
                
                cmd.SetComputeFloatParam(data.rtaoShader, ShaderConstants.radius, data.radius);
                cmd.SetComputeFloatParam(data.rtaoShader, ShaderConstants.intensity, data.intensity);

                var tx = RenderingUtilsExt.DivRoundUp((int)data.dispatchRaySizeX,8);
                var ty = RenderingUtilsExt.DivRoundUp((int)data.dispatchRaySizeY,8);

                cmd.DispatchCompute(data.rtaoShader, data.RTAOKernelID, tx, ty, 1);
            }
            
            cmd.SetGlobalVector("_AmbientOcclusionParam",
                new Vector4(1f, 0f, 0f, data.directLightingStrength));
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, true);

        }
        
        

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddComputePass<PassData>("Raytracing AmbientOcclusion", out var passData))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                if (!frameData.Contains<RaytracingData>())
                {
                    return;
                }
                RaytracingData raytracingData = frameData.Get<RaytracingData>();

                var requireRayTracingVaild = raytracingData.rayTracingSystem.GetRayTracingState();
                if (!requireRayTracingVaild || !RayTracingSystem.SupportedCamera(cameraData.camera))
                {
                    return;
                }

                InitRayTracingPassData(renderGraph, passData, raytracingData, cameraData, resourceData);
                passData.ditheredTextureHandleSet.Use(builder);
                
                builder.UseTexture(passData.DepthTexture);
                builder.UseTexture(passData.NormalTexture);
                builder.UseTexture(passData.AOTexture,AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc<PassData>(ExecutePass);
                
                
                builder.SetGlobalTextureAfterPass(passData.AOTexture, Shader.PropertyToID("_ScreenSpaceOcclusionTexture"));
                resourceData.ssaoTexture = passData.AOTexture;

            }
        }
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, false);
        }

    }
}