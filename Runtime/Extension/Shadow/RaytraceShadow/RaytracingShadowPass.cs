using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class RaytracingShadowPass : ScriptableRenderPass
    {
        private class PassData
        {
            // Compute shader
            internal ComputeShader cs;
            internal int classifyTilesKernel;
            internal int shadowmapKernel;
            internal int bilateralHKernel;
            internal int bilateralVKernel;

            internal int numTilesX;
            internal int numTilesY;

            // Compute Buffers
            internal BufferHandle dispatchIndirectBuffer;
            internal BufferHandle tileListBuffer;

            // Texture
            internal TextureHandle dirShadowmapTex;
            internal TextureHandle screenSpaceShadowmapTex;
            internal Vector2Int screenSpaceShadowmapSize;
            internal TextureHandle normalGBuffer;

            internal int camHistoryFrameCount;

            // Ray Tracing
            internal bool requireRayTracing;
            internal RayTracingShader rtrtShader;
            internal RayTracingAccelerationStructure rtas;
            internal uint dispatchRaySizeX;
            internal uint dispatchRaySizeY;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal TextureHandle stencilHandle;
        }

        static class ShaderConstants
        {
            public static readonly int g_DispatchIndirectBuffer = Shader.PropertyToID("g_DispatchIndirectBuffer");
            public static readonly int g_TileList = Shader.PropertyToID("g_TileList");

            public static readonly int _DirShadowmapTexture = Shader.PropertyToID("_DirShadowmapTexture");
            public static readonly int _SSDirShadowmapTexture = Shader.PropertyToID("_SSDirShadowmapTexture");
            public static readonly int _ScreenSpaceShadowmapTexture = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");
            public static readonly int _PCSSTexture = Shader.PropertyToID("_PCSSTexture");
            public static readonly int _BilateralTexture = Shader.PropertyToID("_BilateralTexture");
            public static readonly int _CamHistoryFrameCount = Shader.PropertyToID("_CamHistoryFrameCount");

            public static readonly int _RayTracingShadowsTextureRW = Shader.PropertyToID("_RayTracingShadowsTextureRW");
            public static readonly int _StencilTexture = Shader.PropertyToID("_StencilTexture");
            
            public static readonly int _RaytracingShadow = Shader.PropertyToID("_RaytracingShadow");

        }


        private void InitRayTracingPassData(
            RenderGraph renderGraph,
            PassData passData,
            RaytracingData raytracingData,
            UniversalCameraData cameraData, UniversalResourceData resourceData)
        {
            var stack = VolumeManager.instance.stack;
            var volumeSettings = stack.GetComponent<Shadows>();
            if (volumeSettings == null)
            {
                passData.requireRayTracing = false;
                return;
            }


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

                passData.stencilHandle = resourceData.activeDepthTexture;
                passData.screenSpaceShadowmapTex = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth, cameraData.scaledHeight)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R16_SFloat,
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

                    // SetTextures
                    cmd.SetRayTracingTextureParam(data.rtrtShader, ShaderConstants._RayTracingShadowsTextureRW, data.screenSpaceShadowmapTex);

                    cmd.DispatchRays(data.rtrtShader, "SingleRayGen", data.dispatchRaySizeX, data.dispatchRaySizeY, 1, null);
                }
            }
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddComputePass<PassData>(" Raytracing Shadow", out var passData))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                RaytracingData raytracingData = frameData.Get<RaytracingData>();

                passData.requireRayTracing = raytracingData.rayTracingSystem.GetRayTracingState();
                InitRayTracingPassData(renderGraph,passData,raytracingData, cameraData, resourceData);
                builder.UseTexture(passData.screenSpaceShadowmapTex);
                
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(passData.requireRayTracing);
                
                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    ExecutePass(data, context);
                });
                raytracingData.rayTracingShadowTexture = passData.screenSpaceShadowmapTex;
                builder.SetGlobalTextureAfterPass(raytracingData.rayTracingShadowTexture, ShaderConstants._RaytracingShadow);

            }
        }
    }
}