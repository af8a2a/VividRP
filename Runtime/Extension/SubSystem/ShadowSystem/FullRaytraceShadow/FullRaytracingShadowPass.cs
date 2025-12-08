using Unity.Mathematics;
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

            internal TextureHandle ScramblingRanking;
            internal TextureHandle Sobol;


            // Ray Tracing
            internal bool requireRayTracing;
            internal ComputeShader fullRayTracingShadowShader;
            internal RayTracingAccelerationStructure rtas;

            internal Vector3 gSunBasisX;
            internal Vector3 gSunBasisY;
            internal Vector3 gSunDirection;
            internal Vector2 gJitter;

            internal int dispatchRaySizeX;
            internal int dispatchRaySizeY;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal RuntimeTextureSystem.DitheredTextureHandleSet ditheredTextureHandleSet;
            internal int frameIndex;

            internal float TanSunAngularRadius;

            internal float gAngularDiameter;
        }

        static class ShaderConstants
        {
            public static readonly int _UnfilterShadowTexture = Shader.PropertyToID("_UnfilterShadowTexture");

            public static readonly int _AccelerationStructure = Shader.PropertyToID("_AccelerationStructure");
            public static readonly int _SceneDepth = Shader.PropertyToID("_SceneDepth");
            public static readonly int _SceneNormal = Shader.PropertyToID("_SceneNormal");

            public static readonly int _TanSunAngularRadius = Shader.PropertyToID("_TanSunAngularRadius");

            public static readonly int gSunBasisX = Shader.PropertyToID("gSunBasisX");
            public static readonly int gSunBasisY = Shader.PropertyToID("gSunBasisY");
            public static readonly int gSunDirection = Shader.PropertyToID("gSunDirection");
            public static readonly int gJitter = Shader.PropertyToID("gJitter");
            public static readonly int gTanSunAngularRadius = Shader.PropertyToID("gTanSunAngularRadius");
            public static readonly int gTanPixelAngularRadius = Shader.PropertyToID("gTanPixelAngularRadius");
            public static readonly int gIn_ScramblingRanking = Shader.PropertyToID("gIn_ScramblingRanking");
            public static readonly int gIn_Sobol = Shader.PropertyToID("gIn_Sobol");
            public static readonly int gAngularDiameter = Shader.PropertyToID("gAngularDiameter");
            public static readonly int gFrameIndex = Shader.PropertyToID("gFrameIndex");
        }


        void GetBasis(Vector3 N, ref Vector3 T, ref Vector3 B)
        {
            float sz = math.sign(N.z);
            float a = 1.0f / (sz + N.z);
            float ya = N.y * a;
            float b = N.x * ya;
            float c = N.x * sz;

            T = new Vector3(c * N.x * a - 1.0f, sz * b, c);
            B = new Vector3(b, N.y * ya - sz, N.y);
        }


        private void InitRayTracingPassData(
            RenderGraph renderGraph,
            PassData passData,
            RayTracingSystem raytracing,
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
                passData.rtas = raytracing.RequestAccelerationStructure(cameraData);

                var width = cameraData.cameraTargetDescriptor.width;
                var height = cameraData.cameraTargetDescriptor.height;
                passData.dispatchRaySizeX = CoreUtils.DivRoundUp(width, 16);
                passData.dispatchRaySizeY = CoreUtils.DivRoundUp(height, 16);

                passData.raytracingShadowmapTex = renderGraph.CreateTexture(new TextureDesc(width, height)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R16_SFloat,
                    name = "Full Raytracing ShadowTexture"
                });
                // RayTracing constant buffer
                {
                    var rayTracingSettings = stack.GetComponent<RayTracingSettings>();

                    passData.rayTracingCB = raytracing.GetShaderVariablesRaytracingCB(cameraData);
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
                passData.ditheredTextureHandleSet = RuntimeTextureSystem.instance.DitheredTextureSet256SPP().RenderGraphImport(renderGraph);
                passData.cameraDepthTexture = resourceData.activeDepthTexture;
                passData.normalGBuffer = resourceData.gBuffer[2];
                passData.frameIndex = historyRT.historyFrameCount;
                passData.TanSunAngularRadius = Mathf.Tan(Mathf.Deg2Rad * volumeSettings.sunAngularDiameter.value * 0.5f);
                passData.gAngularDiameter = volumeSettings.sunAngularDiameter.value;
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

                    RuntimeTextureSystem.BindDitheredTextureSet(cmd, cs, kernel, data.ditheredTextureHandleSet);
                    // SetTextures
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants._UnfilterShadowTexture, data.raytracingShadowmapTex);
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants._SceneDepth, data.cameraDepthTexture);
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants._SceneNormal, data.normalGBuffer);
                    

                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants.gIn_ScramblingRanking, data.ScramblingRanking);
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants.gIn_Sobol, data.Sobol);



                    cmd.SetComputeIntParam(cs, ShaderConstants.gFrameIndex, data.frameIndex);
                    cmd.SetComputeTextureParam(cs, kernel, ShaderConstants._SceneNormal, data.normalGBuffer);


                    cmd.SetComputeFloatParam(cs, ShaderConstants.gTanSunAngularRadius, data.TanSunAngularRadius);
                    cmd.SetComputeVectorParam(cs, ShaderConstants.gSunBasisX, data.gSunBasisX);
                    cmd.SetComputeVectorParam(cs, ShaderConstants.gSunBasisY, data.gSunBasisY);
                    cmd.SetComputeVectorParam(cs, ShaderConstants.gSunDirection, data.gSunDirection);
                    cmd.SetComputeVectorParam(cs, ShaderConstants.gJitter, data.gJitter);
                    cmd.SetComputeFloatParam(cs, ShaderConstants.gAngularDiameter, data.gAngularDiameter);

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
            var lightData = frameData.Get<UniversalLightData>();
            var stack = VolumeManager.instance.stack;
            var volumeSettings = stack.GetComponent<Shadows>();


            using (var builder = renderGraph.AddComputePass<PassData>(" Raytracing Shadow", out var passData))
            {

                var rayTracingSystem = RayTracingSystem.instance;

                passData.requireRayTracing = rayTracingSystem.GetRayTracingState();

                InitRayTracingPassData(renderGraph, passData, rayTracingSystem, cameraData, resourceData);

                passData.gSunDirection = -lightData.visibleLights[lightData.mainLightIndex].GetForward();
                GetBasis(passData.gSunDirection, ref passData.gSunBasisX, ref passData.gSunBasisY);
                passData.gJitter = (Sequence.Halton2D((uint)Time.frameCount) - 0.5f) / new float2(cameraData.actualWidth, cameraData.actualHeight);

                if (!passData.requireRayTracing)
                {
                    return;
                }

                var noise = RuntimeTextureSystem.instance;
                passData.ScramblingRanking = renderGraph.ImportTexture(noise.scramblingRanking4SPP);
                passData.Sobol = renderGraph.ImportTexture(noise.sobel);
                passData.ditheredTextureHandleSet.Use(builder);

                builder.UseTexture(passData.raytracingShadowmapTex, AccessFlags.Write);
                builder.UseTexture(passData.cameraDepthTexture);
                builder.UseTexture(passData.normalGBuffer);
                builder.UseTexture(passData.ScramblingRanking);
                builder.UseTexture(passData.Sobol);


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
            var denoised = cameraData.denoiseSystem.nrdSIGMADenoiser.Denoise(renderGraph, frameData,
                resourceData.motionVectorColor, resourceData.gBuffer[2], resourceData.linearDepthTexture, resourceData.raytracingShadowTexture,
                TextureHandle.nullHandle);

            resourceData.mainShadowsTexture = denoised;
            resourceData.screenSpaceShadowsTexture = denoised;
        }
    }
}