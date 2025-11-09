using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class RaytracingShadowPass : ScriptableRenderPass
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
            internal RayTracingShader rtrtShader;
            internal RayTracingAccelerationStructure rtas;
            internal uint dispatchRaySizeX;
            internal uint dispatchRaySizeY;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal RuntimeTextureSystem.DitheredTextureHandleSet ditheredTextureHandleSet;

            internal float radius;
            internal int sampleCount;
            internal int frameIndex;
        }

        static class ShaderConstants
        {
            public static readonly int _RayTracingShadowsTextureRW = Shader.PropertyToID("_RayTracingShadowsTextureRW");


            public static readonly int _RaytracingShadowTexture = Shader.PropertyToID("_RaytracingShadowTexture");
            public static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int _CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");

            public static readonly int radius = Shader.PropertyToID("radius");
            public static readonly int sampleCount = Shader.PropertyToID("sampleCount");
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
                passData.rtrtShader = runtimeShaders.rayTracingShadowShader;
                passData.rtas = raytracingData.rayTracingSystem.RequestAccelerationStructure();

                var width = cameraData.cameraTargetDescriptor.width;
                var height = cameraData.cameraTargetDescriptor.height;
                passData.dispatchRaySizeX = (uint)width;
                passData.dispatchRaySizeY = (uint)height;

                passData.raytracingShadowmapTex = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R16_SFloat,
                    name = "Raytracing ShadowTexture"
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
                passData.ditheredTextureHandleSet = RuntimeTextureSystem.instance.DitheredTextureSet8SPP().RenderGraphImport(renderGraph);
                passData.cameraDepthTexture = resourceData.activeDepthTexture;
                passData.normalGBuffer = resourceData.gBuffer[2];
                passData.frameIndex = historyRT.historyFrameCount;
                passData.sampleCount = volumeSettings.sampleCount.value;
                passData.radius = volumeSettings.radius.value;
            }
        }

        private static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            if (data.requireRayTracing)
            {
                using (new ProfilingScope(cmd, new ProfilingSampler("RayTracingShadows")))
                {
                    // Define the shader pass to use for the reflection pass
                    cmd.SetRayTracingShaderPass(data.rtrtShader, "VisibilityDXR");

                    // Set the acceleration structure for the pass
                    cmd.SetRayTracingAccelerationStructure(data.rtrtShader, "_RaytracingAccelerationStructure", data.rtas);

                    // SetConstantBuffer
                    ConstantBuffer.PushGlobal(cmd, data.rayTracingCB, RayTracingSystem._ShaderVariablesRaytracing);

                    RuntimeTextureSystem.BindRaytraceDitheredTextureSet(cmd,data.rtrtShader, data.ditheredTextureHandleSet);
                    // SetTextures
                    cmd.SetRayTracingTextureParam(data.rtrtShader, ShaderConstants._RayTracingShadowsTextureRW, data.raytracingShadowmapTex);
                    cmd.SetRayTracingTextureParam(data.rtrtShader, ShaderConstants._CameraDepthTexture, data.cameraDepthTexture);
                    cmd.SetRayTracingTextureParam(data.rtrtShader, ShaderConstants._CameraNormalsTexture, data.normalGBuffer);


                    cmd.SetRayTracingFloatParam(data.rtrtShader, ShaderConstants.sampleCount, data.sampleCount);
                    cmd.SetRayTracingFloatParam(data.rtrtShader, ShaderConstants.radius, data.radius);
                    cmd.SetRayTracingIntParam(data.rtrtShader, ShaderConstants.frameIndex, data.frameIndex);
                    cmd.DispatchRays(data.rtrtShader, "SingleRayGen", data.dispatchRaySizeX, data.dispatchRaySizeY, 1, null);
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
            using (var builder = renderGraph.AddComputePass<PassData>(" Raytracing Shadow", out var passData))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();


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
            }
        }
    }
}