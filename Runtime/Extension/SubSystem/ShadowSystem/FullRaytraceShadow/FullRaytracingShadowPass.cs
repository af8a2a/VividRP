using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class FullRaytracingShadowPass : ScriptableRenderPass
    {
        private class PassData
        {
            // Texture
            internal TextureHandle dirShadowmapTex;
            internal TextureHandle raytracingShadowmapTex;
            internal Vector2Int screenSpaceShadowmapSize;
            internal TextureHandle normalGBuffer;
            internal TextureHandle cameraDepthTexture;


            // Ray Tracing
            internal bool requireRayTracing;
            internal ComputeShader fullRayTracingShadowShader;
            internal RayTracingAccelerationStructure rtas;
            internal int dispatchRaySizeX;
            internal int dispatchRaySizeY;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal BlueNoiseSystem.DitheredTextureHandleSet ditheredTextureHandleSet;
            internal int frameIndex;

            internal float TanSunAngularRadius;
        }

        static class ShaderConstants
        {
            public static readonly int _UnfilterShadowTexture = Shader.PropertyToID("_UnfilterShadowTexture");

            public static readonly int _AccelerationStructure = Shader.PropertyToID("_AccelerationStructure");
            public static readonly int _SceneDepth = Shader.PropertyToID("_SceneDepth");
            public static readonly int _SceneNormal = Shader.PropertyToID("_SceneNormal");

            public static readonly int _TanSunAngularRadius = Shader.PropertyToID("_TanSunAngularRadius");
            public static readonly int frameIndex = Shader.PropertyToID("frameIndex");
        }


        private void InitRayTracingPassData(
            RenderGraph renderGraph,
            PassData passData,
            RaytracingData raytracingData,
            UniversalCameraData cameraData, UniversalResourceData resourceData)
        {
            var stack = VolumeManager.instance.stack;
            var volumeSettings = stack.GetComponent<Shadows>();
            if (!volumeSettings)
            {
                passData.requireRayTracing = false;
                return;
            }

            var historyRT = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);


            passData.requireRayTracing &= volumeSettings.rayTracing.value;

            if (passData.requireRayTracing)
            {
                var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<RaytracingShadowRuntimeShaders>();
                passData.fullRayTracingShadowShader = runtimeShaders.fullRayTracingShadowShader;
                passData.rtas = raytracingData.rayTracingSystem.RequestAccelerationStructure();

                var width = cameraData.cameraTargetDescriptor.width;
                var height = cameraData.cameraTargetDescriptor.height;
                passData.dispatchRaySizeX = CoreUtils.DivRoundUp(width, 8);
                passData.dispatchRaySizeY = CoreUtils.DivRoundUp(height, 8);

                passData.raytracingShadowmapTex = renderGraph.CreateTexture(new TextureDesc(width, height)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R16_SFloat,
                    name = "Full Raytracing ShadowTexture"
                });
                // RayTracing constant buffer
                {
                    var rayTracingSettings = stack.GetComponent<RayTracingSettings>();

                    passData.rayTracingCB = raytracingData.rayTracingSystem.GetShaderVariablesRaytracingCB(new Vector2Int(width, height), rayTracingSettings);
                    passData.rayTracingCB._RaytracingRayMaxLength =
                        Mathf.Min(volumeSettings.dirShadowsRayLength.value, rayTracingSettings.directionalShadowRayLength.value);
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
                passData.cameraDepthTexture = resourceData.activeDepthTexture;
                passData.normalGBuffer = resourceData.gBuffer[2];
                passData.frameIndex = historyRT.historyFrameCount;
                passData.TanSunAngularRadius = Mathf.Tan(Mathf.Deg2Rad * volumeSettings.sunAngularDiameter.value * 0.5f);
            }
        }

        private static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            if (data.requireRayTracing)
            {
                using (new ProfilingScope(cmd, new ProfilingSampler("FullRayTracingShadows")))
                {
                    var cs = data.fullRayTracingShadowShader;
                    var kernel = 0;

                    // Set the acceleration structure for the pass
                    cmd.SetRayTracingAccelerationStructure(cs, kernel, ShaderConstants._AccelerationStructure, data.rtas);

                    // SetConstantBuffer
                    ConstantBuffer.PushGlobal(cmd, data.rayTracingCB, RayTracingSystem._ShaderVariablesRaytracing);

                    BlueNoiseSystem.BindDitheredTextureSet(cmd, cs, kernel, data.ditheredTextureHandleSet);
                    // SetTextures
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants._UnfilterShadowTexture, data.raytracingShadowmapTex);
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants._SceneDepth, data.cameraDepthTexture);
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants._SceneNormal, data.normalGBuffer);


                    cmd.SetComputeFloatParam(cs, ShaderConstants._TanSunAngularRadius, data.TanSunAngularRadius);

                    cmd.SetComputeIntParam(cs, ShaderConstants.frameIndex, data.frameIndex);
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants._SceneNormal, data.normalGBuffer);


                    cmd.DispatchCompute(cs, kernel, data.dispatchRaySizeX, data.dispatchRaySizeY, 1);
                    CoreUtils.SetKeyword(cmd, "_RAYTRACING_SHADOW", true);
                }
            }
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            CoreUtils.SetKeyword(cmd, "_RAYTRACING_SHADOW", false);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            var stack = VolumeManager.instance.stack;
            var volumeSettings = stack.GetComponent<Shadows>();


            using (var builder = renderGraph.AddComputePass<PassData>(" Raytracing Shadow", out var passData))
            {
                if (!frameData.Contains<RaytracingData>())
                {
                    return;
                }

                RaytracingData raytracingData = frameData.Get<RaytracingData>();

                passData.requireRayTracing = raytracingData.rayTracingSystem.GetRayTracingState();

                InitRayTracingPassData(renderGraph, passData, raytracingData, cameraData, resourceData);

                if (!passData.requireRayTracing)
                {
                    return;
                }

                passData.ditheredTextureHandleSet.Use(builder);
                builder.UseTexture(passData.raytracingShadowmapTex, AccessFlags.Write);
                builder.UseTexture(passData.cameraDepthTexture);
                builder.UseTexture(passData.normalGBuffer);


                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(passData.requireRayTracing);

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) => { ExecutePass(data, context); });
                resourceData.raytracingShadowTexture = passData.raytracingShadowmapTex;
                if (volumeSettings.useFullRTShadow.value)
                {
                    resourceData.mainShadowsTexture = passData.raytracingShadowmapTex;
                    resourceData.screenSpaceShadowsTexture = passData.raytracingShadowmapTex;
                }
            }



            // SigmaTileClassifier.instance.ClassifyShadowPenumbra(renderGraph, cameraData, volumeSettings, resourceData.linearDepthTexture,
            //     resourceData.raytracingShadowTexture);
           var denoised= cameraData.denoiseSystem.nrdSIGMADenoiser.Denoise(renderGraph, frameData,
                resourceData.motionVectorDepth, resourceData.gBuffer[2], resourceData.linearDepthTexture, resourceData.raytracingShadowTexture,
                TextureHandle.nullHandle);

           resourceData.mainShadowsTexture = denoised;
           resourceData.screenSpaceShadowsTexture = denoised;

        }
    }
}