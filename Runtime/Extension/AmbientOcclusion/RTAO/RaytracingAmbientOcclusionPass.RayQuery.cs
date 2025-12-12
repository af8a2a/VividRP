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

                var tx = RenderingUtilsExt.DivRoundUp((int)data.dispatchRaySizeX, 8);
                var ty = RenderingUtilsExt.DivRoundUp((int)data.dispatchRaySizeY, 8);

                cmd.DispatchCompute(data.rtaoRayQueryShader, data.RTAOKernelID, tx, ty, 1);
            }

            cmd.SetGlobalVector("_AmbientOcclusionParam",
                new Vector4(1, 0, 0, 1));
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
                format = GraphicsFormat.R8_UNorm,
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

                builder.UseTexture(passData.DepthTexture);
                builder.UseTexture(passData.NormalTexture);
                builder.UseTexture(passData.AOTexture, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc<PassData>(ExecuteRayQuery);
                builder.UseTexture(output);

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
                var histroyRT = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);
                var prevFrameRT = ReAllocatedHistoryAOBufferIfNeeded(histroyRT);
                var aoHistory = renderGraph.ImportTexture(prevFrameRT);


                var spatialDenoiser = cameraData.denoiseSystem.spatialDenoiser;

                var temporalDenoiser = cameraData.denoiseSystem.temporalDenoiser;


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
                    renderGraph.defaultResources.blackTexture,
                    aoHistory,
                    resourceData.cameraDepthTexture,
                    resourceData.gBuffer[2],
                    resourceData.motionVectorColor,
                    cameraData.denoiseSystem.historyValidity);


                MipGenerator.instance.CopyColor(renderGraph, denoisedRtao, aoHistory);


                SpatialDenoiser.DiffuseDenoiserParameters ddParams;
                ddParams.singleChannel = true;
                ddParams.kernelSize = aoSetting.denoiseRadius.value;
                ddParams.halfResolutionFilter = false;
                ddParams.jitterFilter = false;
                ddParams.resolutionMultiplier = 1.0f;
                denoisedRtao = spatialDenoiser.Denoise(renderGraph, cameraData, ddParams, denoisedRtao, resourceData.cameraDepthTexture,
                    resourceData.gBuffer[2], aoTexture);
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