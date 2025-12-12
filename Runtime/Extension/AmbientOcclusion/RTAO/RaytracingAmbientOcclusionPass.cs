using System.Linq;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class RaytracingAmbientOcclusionPass : ScriptableRenderPass
    {
        public RaytracingAmbientOcclusionPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
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

            #region SER

            internal bool SER;

            #endregion

            internal RayTracingAccelerationStructure rtas;
            internal uint dispatchRaySizeX;
            internal uint dispatchRaySizeY;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal RuntimeTextureSystem.DitheredTextureHandleSet ditheredTextureHandleSet;
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
            RayTracingSystem rayTracingSystem,
            UniversalCameraData cameraData,
            UniversalResourceData resourceData)
        {
            var stack = VolumeManager.instance.stack;
            var volumeSettings = stack.GetComponent<AmbientOcclusion>();
            if (!volumeSettings)
            {
                return;
            }

            var historyRT = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);


            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<RaytracingAmbientOcclusionRuntimeShader>();

            passData.NormalTexture = resourceData.gBuffer[2];
            passData.DepthTexture = resourceData.cameraDepthTexture;
            passData.rtaoRayQueryShader = runtimeShaders.inlineRaytracingAmbientOcclusionShader;
            passData.RTAOKernelID = passData.rtaoRayQueryShader.FindKernel("RTAO");

            passData.rtaoShader = runtimeShaders.raytracingAmbientOcclusionRTShader;

            passData.rtas = rayTracingSystem.RequestAccelerationStructure(cameraData);
            passData.SER = volumeSettings.shaderExecutionReordering.value &&
                           ExtensionSystem.SupportedExtension.Contains(HardwareExtension.ShaderExecutionReordering);


            var width = cameraData.cameraTargetDescriptor.width;
            var height = cameraData.cameraTargetDescriptor.height;
            passData.dispatchRaySizeX = (uint)width;
            passData.dispatchRaySizeY = (uint)height;

            passData.AOTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R8_UNorm,
                name = "Raytracing AO Texture"
            });
            // RayTracing constant buffer
            {
                var rayTracingSettings = stack.GetComponent<RayTracingSettings>();

                passData.rayTracingCB = rayTracingSystem.GetShaderVariablesRaytracingCB(cameraData);
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
            passData.ditheredTextureHandleSet = RuntimeTextureSystem.instance.DitheredTextureSet8SPP().RenderGraphImport(renderGraph);


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
            public static readonly int _OwenScrambledTexture = Shader.PropertyToID("_OwenScrambledTexture");
            public static readonly int _ScramblingTileXSPP = Shader.PropertyToID("_ScramblingTileXSPP");
            public static readonly int _RankingTileXSPP = Shader.PropertyToID("_RankingTileXSPP");
            public static readonly int _ScramblingTexture = Shader.PropertyToID("_ScramblingTexture");
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

                RuntimeTextureSystem.BindDitheredTextureSet(cmd, data.ditheredTextureHandleSet);

                cmd.SetRayTracingShaderPass(data.rtaoShader, "VisibilityDXR");
                cmd.SetRayTracingAccelerationStructure(data.rtaoShader, ShaderConstants._RaytracingAccelerationStructure, data.rtas);


                cmd.SetRayTracingTextureParam(data.rtaoShader, ShaderConstants._OwenScrambledTexture, data.ditheredTextureHandleSet.owenScrambled256Tex);
                cmd.SetRayTracingTextureParam(data.rtaoShader, ShaderConstants._ScramblingTileXSPP, data.ditheredTextureHandleSet.scramblingTile);
                cmd.SetRayTracingTextureParam(data.rtaoShader, ShaderConstants._RankingTileXSPP, data.ditheredTextureHandleSet.rankingTile);
                cmd.SetRayTracingTextureParam(data.rtaoShader, ShaderConstants._ScramblingTexture, data.ditheredTextureHandleSet.scramblingTex);

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

            TextureHandle aoTexture;
            using (var builder = renderGraph.AddComputePass<PassData>("Raytracing AmbientOcclusion", out var passData))
            {
                var rayTracingSystem = RayTracingSystem.instance;

                var requireRayTracingVaild = rayTracingSystem.GetRayTracingState();
                if (!requireRayTracingVaild || !RayTracingSystem.SupportedCamera(cameraData.camera))
                {
                    return TextureHandle.nullHandle;
                }

                InitRayTracingPassData(renderGraph, passData, rayTracingSystem, cameraData, resourceData);
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

            var aoSetting = VolumeManager.instance.stack.GetComponent<AmbientOcclusion>();


            TextureHandle denoisedRtao;
            if (aoSetting.useNRD.value)
            {
                var denoiser = cameraData.denoiseSystem.ambientOcclusionDenoiser;
                denoisedRtao = denoiser.Denoise(renderGraph, frameData, resourceData.motionVectorColor, resourceData.gBuffer[2],
                    resourceData.linearDepthTexture,
                    aoTexture);
            }
            else
            {
                var spatialDenoiser = cameraData.denoiseSystem.spatialDenoiser;

                var temporalDenoiser = cameraData.denoiseSystem.temporalDenoiser;

                var prevFrameRT = ReAllocatedHistoryAOBufferIfNeeded(camHistoryRTSystem);
                var aoHistory = renderGraph.ImportTexture(prevFrameRT);

                TemporalFilter.TemporalFilterParameters filterParams;
                filterParams.singleChannel = false;
                filterParams.historyValidity = 1;
                filterParams.occluderMotionRejection = aoSetting.occluderMotionRejection.value;
                filterParams.receiverMotionRejection = aoSetting.receiverMotionRejection.value;
                filterParams.exposureControl = false;
                filterParams.resolutionMultiplier = 1.0f;
                filterParams.historyResolutionMultiplier = 1.0f;

                 denoisedRtao = temporalDenoiser.Denoise(renderGraph, cameraData, filterParams,
                    aoTexture,
                    velocity,
                    aoHistory,
                    resourceData.cameraDepthTexture,
                    resourceData.gBuffer[2],
                    resourceData.motionVectorColor,
                    cameraData.denoiseSystem.historyValidity);


                SpatialDenoiser.DiffuseDenoiserParameters ddParams;
                ddParams.singleChannel = true;
                ddParams.kernelSize = aoSetting.denoiseRadius.value;
                ddParams.halfResolutionFilter = false;
                ddParams.jitterFilter = false;
                ddParams.resolutionMultiplier = 1.0f;
                denoisedRtao = spatialDenoiser.Denoise(renderGraph, cameraData, ddParams, denoisedRtao, resourceData.cameraDepthTexture,
                    resourceData.cameraNormalsTexture, aoTexture);
            }



            using (var builder = renderGraph.AddComputePass<ResolvePassData>("Raytracing AmbientOcclusion Resolve", out var passData))
            {
                var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<RaytracingAmbientOcclusionRuntimeShader>();

                passData.rtaoResolveShader = runtimeShaders.raytracingAmbientOcclusionResolveShader;
                passData.RTAOResolveKernelID = passData.rtaoResolveShader.FindKernel("RTAOApplyIntensity");
                var width = cameraData.cameraTargetDescriptor.width;
                var height = cameraData.cameraTargetDescriptor.height;
                passData.dispatchRaySizeX = (uint)width;
                passData.dispatchRaySizeY = (uint)height;

                passData.intensity = aoSetting.intensity.value;

                passData.AOTexture = denoisedRtao;
                builder.UseTexture(passData.AOTexture, AccessFlags.ReadWrite);

                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc<ResolvePassData>((data, ctx) =>
                {
                    var cmd = ctx.cmd;
                    cmd.SetComputeFloatParam(data.rtaoResolveShader, ShaderConstants.intensity, data.intensity);

                    cmd.SetComputeTextureParam(data.rtaoResolveShader, data.RTAOResolveKernelID, ShaderConstants._AmbientOcclusionTextureRW, denoisedRtao);
                    var tx = RenderingUtilsExt.DivRoundUp((int)data.dispatchRaySizeX, 8);
                    var ty = RenderingUtilsExt.DivRoundUp((int)data.dispatchRaySizeY, 8);

                    cmd.DispatchCompute(data.rtaoResolveShader, data.RTAOResolveKernelID, tx, ty, 1);
                    cmd.SetGlobalVector("_AmbientOcclusionParam",
                        new Vector4(1f, 0f, 0f, 1));
                });
                return denoisedRtao;
            }
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            var aoSetting = VolumeManager.instance.stack.GetComponent<AmbientOcclusion>();
            if (!aoSetting.IsActive() || aoSetting.ambientOcclusionModeParameter.value is not AmbientOcclusionMode.RaytracingAmbientOcclusion)
            {
                return;
            }

            if (aoSetting.rayQuery.value)
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