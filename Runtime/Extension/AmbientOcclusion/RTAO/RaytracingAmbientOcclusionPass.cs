using System.Linq;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class RaytracingAmbientOcclusionPass : ScriptableRenderPass
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


            // RayQuery
            internal ComputeShader rtaoRayQueryShader;
            internal int RTAOKernelID;

            //Raytracing DXR
            internal RayTracingShader rtaoShader;
            internal TextureHandle velocityTexture;
            internal bool AccurateGbufferNormals;

            #region SER

            internal bool SER;

            #endregion
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


        class ResolvePassData
        {
            internal TextureHandle AOTexture;
            internal ComputeShader rtaoResolveShader;
            internal int RTAOResolveKernelID;
            internal float intensity;
            internal uint dispatchRaySizeX;
            internal uint dispatchRaySizeY;
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
            passData.rtaoRayQueryShader = runtimeShaders.inlineRaytracingAmbientOcclusionShader;
            passData.RTAOKernelID = passData.rtaoRayQueryShader.FindKernel("RTAO");

            passData.rtaoShader = runtimeShaders.raytracingAmbientOcclusionRTShader;

            passData.rtas = raytracingData.rayTracingSystem.RequestAccelerationStructure();
            passData.SER = volumeSettings.shaderExecutionReordering.value && ExtensionSystem.SupportedExtension.Contains(HardwareExtension.ShaderExecutionReordering);

            
            
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
            passData.radius = volumeSettings.rayLength.value;
            passData.intensity = volumeSettings.intensity.value;
            passData.directLightingStrength = volumeSettings.directLightingStrength.value;
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
            public static readonly int _RaytracingAccelerationStructure = Shader.PropertyToID("_RaytracingAccelerationStructure");
            public static readonly int _VelocityBuffer = Shader.PropertyToID("_VelocityBuffer");
            public static readonly int _AmbientOcclusionTextureRW = Shader.PropertyToID("_AmbientOcclusionTextureRW");
            public static readonly int _UseNVSER = Shader.PropertyToID("_UseNVSER");
        }


        public void Setup()
        {
            ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Motion);
        }


        static RTHandle HistoryAOBufferAllocatorFunction(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one, colorFormat: graphicsFormat,
                enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_CameraHistoryAOBuffer{1}", viewName, frameIndex));
        }

        internal RTHandle ReAllocatedHistoryAOBufferIfNeeded(HistoryFrameRTSystem historyRTSystem)
        {
            return historyRTSystem.GetCurrentFrameRT(HistoryFrameType.RaytracingAmbientOcclusionHistory)
                   ?? historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.RaytracingAmbientOcclusionHistory,
                       HistoryAOBufferAllocatorFunction, GraphicsFormat.R16G16B16A16_SFloat, 1);
        }


        private static void ExecuteTrace(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            using (new ProfilingScope(cmd, new ProfilingSampler("RayTracing AmbientOcclusion")))
            {
                // Define the shader pass to use for the reflection pass

                // Set the acceleration structure for the pass

                // SetConstantBuffer
                ConstantBuffer.PushGlobal(cmd, data.rayTracingCB, RayTracingSystem._ShaderVariablesRaytracing);

                BlueNoiseSystem.BindDitheredTextureSet(cmd, data.ditheredTextureHandleSet);

                cmd.SetRayTracingShaderPass(data.rtaoShader, "VisibilityDXR");
                cmd.SetRayTracingAccelerationStructure(data.rtaoShader, ShaderConstants._RaytracingAccelerationStructure, data.rtas);

                // SetTextures
                cmd.SetRayTracingTextureParam(data.rtaoShader, ShaderConstants.SceneDepth, data.DepthTexture);
                cmd.SetRayTracingTextureParam(data.rtaoShader, ShaderConstants.SceneNormal, data.NormalTexture);
                cmd.SetRayTracingTextureParam(data.rtaoShader, ShaderConstants.AmbientOcclusionTexture, data.AOTexture);
                cmd.SetRayTracingTextureParam(data.rtaoShader, ShaderConstants._VelocityBuffer, data.velocityTexture);

                cmd.SetRayTracingIntParam(data.rtaoShader, ShaderConstants.sampleCount, data.sampleCount);
                cmd.SetRayTracingIntParam(data.rtaoShader, ShaderConstants.frameIndex, data.frameIndex);
                cmd.SetRayTracingFloatParam(data.rtaoShader, ShaderConstants.radius, data.radius);
                cmd.SetRayTracingFloatParam(data.rtaoShader, ShaderConstants.intensity, data.intensity);
                cmd.SetRayTracingFloatParam(data.rtaoShader, ShaderConstants._UseNVSER, data.SER ? 1 : 0);
                cmd.DispatchRays(data.rtaoShader, "RayGenAmbientOcclusion", data.dispatchRaySizeX, data.dispatchRaySizeY, 1, null);
            }

            cmd.SetKeyword(ShaderGlobalKeywords.ScreenSpaceOcclusion, true);

            cmd.SetGlobalVector("_AmbientOcclusionParam", new Vector4(1f, 0f, 0f, data.directLightingStrength));
        }


        TextureHandle RTAORayPipeline(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            var camHistoryRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);

            var output = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R16_SFloat,
            });

            var velocity = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R16_SFloat,
            });

            var prevFrameRT = ReAllocatedHistoryAOBufferIfNeeded(camHistoryRTSystem);
            var AOHistory = renderGraph.ImportTexture(prevFrameRT);
            TextureHandle aoTexture;
            using (var builder = renderGraph.AddComputePass<PassData>("Raytracing AmbientOcclusion", out var passData))
            {
                if (!frameData.Contains<RaytracingData>())
                {
                    return TextureHandle.nullHandle;
                }

                RaytracingData raytracingData = frameData.Get<RaytracingData>();

                var requireRayTracingVaild = raytracingData.rayTracingSystem.GetRayTracingState();
                if (!requireRayTracingVaild || !RayTracingSystem.SupportedCamera(cameraData.camera))
                {
                    return TextureHandle.nullHandle;
                }

                InitRayTracingPassData(renderGraph, passData, raytracingData, cameraData, resourceData);
                passData.ditheredTextureHandleSet.Use(builder);
                passData.velocityTexture = velocity;

                builder.UseTexture(passData.velocityTexture);
                builder.UseTexture(passData.DepthTexture);
                builder.UseTexture(passData.NormalTexture);
                builder.UseTexture(passData.AOTexture, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.UseTexture(output);
                builder.SetRenderFunc<PassData>(ExecuteTrace);
                aoTexture = passData.AOTexture;
            }

            var volumeSettings = VolumeManager.instance.stack.GetComponent<RaytracingAmbientOcclusion>();
            
            var spatialDenoiser = cameraData.denoiseSystem.spatialDenoiser;

            var temporalDenoiser = cameraData.denoiseSystem.temporalDenoiser;






            TemporalFilter.TemporalFilterParameters filterParams;
            filterParams.singleChannel = false;
            filterParams.historyValidity = 1;
            filterParams.occluderMotionRejection = volumeSettings.occluderMotionRejection.value;
            filterParams.receiverMotionRejection = volumeSettings.receiverMotionRejection.value;
            filterParams.exposureControl = false;
            filterParams.resolutionMultiplier = 1.0f;
            filterParams.historyResolutionMultiplier = 1.0f;

            TextureHandle denoisedRTAO =  temporalDenoiser.Denoise(renderGraph, cameraData, filterParams,
                aoTexture,
                velocity,
                AOHistory,
                resourceData.cameraDepthTexture,
                resourceData.cameraNormalsTexture,
                resourceData.motionVectorColor,
                cameraData.denoiseSystem.historyValidity);
            
            
            SpatialDenoiser.DiffuseDenoiserParameters ddParams;
            ddParams.singleChannel = true;
            ddParams.kernelSize = volumeSettings.denoiseRadius.value;
            ddParams.halfResolutionFilter = false;
            ddParams.jitterFilter = false;
            ddParams.resolutionMultiplier = 1.0f;
            denoisedRTAO = spatialDenoiser.Denoise(renderGraph, cameraData, ddParams, denoisedRTAO, resourceData.cameraDepthTexture,
                resourceData.cameraNormalsTexture, aoTexture);


            using (var builder = renderGraph.AddComputePass<ResolvePassData>("Raytracing AmbientOcclusion Resolve", out var passData))
            {
                var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<RaytracingAmbientOcclusionRuntimeShader>();

                passData.rtaoResolveShader = runtimeShaders.raytracingAmbientOcclusionResolveShader;
                passData.RTAOResolveKernelID = passData.rtaoResolveShader.FindKernel("RTAOApplyIntensity");
                var width = cameraData.cameraTargetDescriptor.width;
                var height = cameraData.cameraTargetDescriptor.height;
                passData.dispatchRaySizeX = (uint)width;
                passData.dispatchRaySizeY = (uint)height;

                passData.intensity = volumeSettings.intensity.value;

                passData.AOTexture = denoisedRTAO;
                builder.UseTexture(passData.AOTexture, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetGlobalTextureAfterPass(passData.AOTexture, Shader.PropertyToID("_ScreenSpaceOcclusionTexture"));


                builder.SetRenderFunc<ResolvePassData>((data, ctx) =>
                {
                    var cmd = ctx.cmd;
                    cmd.SetComputeFloatParam(data.rtaoResolveShader, ShaderConstants.intensity, data.intensity);

                    cmd.SetComputeTextureParam(data.rtaoResolveShader, data.RTAOResolveKernelID, ShaderConstants._AmbientOcclusionTextureRW, data.AOTexture);
                    var tx = RenderingUtilsExt.DivRoundUp((int)data.dispatchRaySizeX, 8);
                    var ty = RenderingUtilsExt.DivRoundUp((int)data.dispatchRaySizeY, 8);

                    cmd.DispatchCompute(data.rtaoResolveShader, data.RTAOResolveKernelID, tx, ty, 1);
                });
                return passData.AOTexture;
            }
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            var volumeSettings = VolumeManager.instance.stack.GetComponent<RaytracingAmbientOcclusion>();
            if (!volumeSettings.enabled.value || volumeSettings.intensity.value == 0)
            {
                return;
            }

            if (volumeSettings.rayQuery.value)
            {
                resourceData.ssaoTexture = RTAORayQuery(renderGraph, frameData);
            }
            else
            {
                resourceData.ssaoTexture = RTAORayPipeline(renderGraph, frameData);
            }
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, false);
        }
    }
}