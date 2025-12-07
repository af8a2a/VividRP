using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class RaytracingAmbientOcclusionPass
    {
        private static void ExecuteRayQuery(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            using (new ProfilingScope(cmd, new ProfilingSampler("RayTracing AmbientOcclusion")))
            {
                // Define the shader pass to use for the reflection pass

                // Set the acceleration structure for the pass
                cmd.SetRayTracingAccelerationStructure(data.rtaoRayQueryShader, data.RTAOKernelID, ShaderConstants._AccelerationStructure, data.rtas);

                // SetConstantBuffer
                ConstantBuffer.PushGlobal(cmd, data.rayTracingCB, RayTracingSystem._ShaderVariablesRaytracing);

                RuntimeTextureSystem.BindDitheredTextureSet(cmd, data.ditheredTextureHandleSet);
                // SetTextures
                cmd.SetComputeTextureParam(data.rtaoRayQueryShader, data.RTAOKernelID, ShaderConstants.SceneDepth, data.DepthTexture);
                cmd.SetComputeTextureParam(data.rtaoRayQueryShader, data.RTAOKernelID, ShaderConstants.SceneNormal, data.NormalTexture);
                cmd.SetComputeTextureParam(data.rtaoRayQueryShader, data.RTAOKernelID, ShaderConstants.AmbientOcclusionTexture, data.AOTexture);

                cmd.SetComputeIntParam(data.rtaoRayQueryShader, ShaderConstants.sampleCount, data.sampleCount);
                cmd.SetComputeIntParam(data.rtaoRayQueryShader, ShaderConstants.frameIndex, data.frameIndex);

                cmd.SetComputeFloatParam(data.rtaoRayQueryShader, ShaderConstants.radius, data.radius);
                cmd.SetComputeFloatParam(data.rtaoRayQueryShader, ShaderConstants.intensity, data.intensity);
                CoreUtils.SetKeyword(cmd,"_GBUFFER_NORMALS_OCT", true);

                var tx = RenderingUtilsExt.DivRoundUp((int)data.dispatchRaySizeX, 8);
                var ty = RenderingUtilsExt.DivRoundUp((int)data.dispatchRaySizeY, 8);

                cmd.DispatchCompute(data.rtaoRayQueryShader, data.RTAOKernelID, tx, ty, 1);
            }

            cmd.SetGlobalVector("_AmbientOcclusionParam",
                new Vector4(1f, 0f, 0f, data.directLightingStrength));
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, true);
        }

        TextureHandle RTAORayQuery(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            var camHistoryRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);

            var output = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R16_SFloat,
            });
            var histroyRT = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);
            var prevFrameRT = ReAllocatedHistoryAOBufferIfNeeded(histroyRT);
            var AOHistory = renderGraph.ImportTexture(prevFrameRT);
            TextureHandle aoTexture;
            var urpRenderer = (UniversalRenderer)UniversalRenderer.current;

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

                builder.UseTexture(passData.DepthTexture);
                builder.UseTexture(passData.NormalTexture);
                builder.UseTexture(passData.AOTexture, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc<PassData>(ExecuteRayQuery);
                builder.UseTexture(output);

                aoTexture = passData.AOTexture;
                
                builder.SetGlobalTextureAfterPass(output, Shader.PropertyToID("_ScreenSpaceOcclusionTexture"));

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
                renderGraph.defaultResources.blackTexture,
                AOHistory,
                resourceData.cameraDepthTexture,
                urpRenderer.usesDeferredLighting ? resourceData.cameraNormalsTexture : resourceData.gBuffer[2],
                resourceData.motionVectorColor,
                cameraData.denoiseSystem.historyValidity);

            

            MipGenerator.instance.CopyColor(renderGraph,  denoisedRTAO, AOHistory);
            
            
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
    }
}