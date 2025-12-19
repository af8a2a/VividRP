using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class RaytracingShadowPass : ScriptableRenderPass
    {
        #region Shadow Classify

        ComputeShader m_ShadowClassifyCS;
        int m_ClassifyByNormalKernel;
        int m_ClassifyByCascadeRangeKernel;
        int m_ClassifyByCascadesKernel;

        // Shader property IDs (classify)
        static readonly int s_TextureSizeID = Shader.PropertyToID("textureSize");
        static readonly int s_LightDirID = Shader.PropertyToID("lightDir");
        static readonly int s_SkyHeightID = Shader.PropertyToID("skyHeight");
        static readonly int s_PixelThicknessID = Shader.PropertyToID("pixelThickness");
        static readonly int s_SunSizeID = Shader.PropertyToID("sunSize");
        static readonly int s_NoisePhaseID = Shader.PropertyToID("noisePhase");
        static readonly int s_RejectLitPixelsID = Shader.PropertyToID("bRejectLitPixels");
        static readonly int s_CascadeCountID = Shader.PropertyToID("cascadeCount");
        static readonly int s_ActiveCascadesID = Shader.PropertyToID("activeCascades");
        static readonly int s_TileToleranceID = Shader.PropertyToID("tileTolerance");
        static readonly int s_BlockerOffsetID = Shader.PropertyToID("blockerOffset");
        static readonly int s_CascadePixelSizeID = Shader.PropertyToID("cascadePixelSize");
        static readonly int s_CascadeSizeID = Shader.PropertyToID("cascadeSize");
        static readonly int s_SunSizeLightSpaceID = Shader.PropertyToID("sunSizeLightSpace");
        static readonly int s_UseCascadesForRayTID = Shader.PropertyToID("bUseCascadesForRayT");
        static readonly int s_CascadeScaleID = Shader.PropertyToID("cascadeScale");
        static readonly int s_CascadeOffsetID = Shader.PropertyToID("cascadeOffset");
        static readonly int s_ViewToWorldID = Shader.PropertyToID("viewToWorld");
        static readonly int s_LightViewID = Shader.PropertyToID("lightView");
        static readonly int s_InverseLightViewID = Shader.PropertyToID("inverseLightView");

        // Texture/buffer IDs (classify)
        static readonly int s_DepthTextureID = Shader.PropertyToID("t2d_depth");
        static readonly int s_NormalTextureID = Shader.PropertyToID("t2d_normals");
        static readonly int s_ShadowMapID = Shader.PropertyToID("t2d_shadowMap");
        static readonly int s_TilesBufferID = Shader.PropertyToID("rwsb_tiles");
        static readonly int s_TileCountBufferID = Shader.PropertyToID("rwb_tileCount");
        static readonly int s_RayHitResultsID = Shader.PropertyToID("rwt2d_rayHitResults");

        
        
        // Constant buffer ID
        static readonly int s_ControlsBufferID = Shader.PropertyToID("cb_controls");

        unsafe struct ClassifyConstants
        {
            public float4 textureSize;

            public float3 lightDir;
            public float skyHeight;

            public float pixelThickness;
            public float sunSize;
            public float noisePhase;
            public int bRejectLitPixels;

            public uint cascadeCount;
            public uint activeCascades;
            public uint tileTolerance;
            public float blockerOffset;

            public float cascadePixelSize;
            public float cascadeSize;
            public float sunSizeLightSpace;
            public int bUseCascadesForRayT;

            [HLSLArray(4, typeof(Vector4))] public fixed float cascadeScale[4 * 4];

            [HLSLArray(4, typeof(Vector4))] public fixed float cascadeOffset[4 * 4];

            public float4x4 viewToWorld;
            public float4x4 lightView;
            public float4x4 inverseLightView;
        }

        #endregion

        private class PassData
        {
            // Texture

            internal TextureHandle shadowMaskTexture;


            internal Vector2Int screenSpaceShadowmapSize;
            internal TextureHandle cameraDepthTexture;
            internal TextureHandle blueNoiseTexture;
            internal TextureHandle rayHitResultsTexture;

            internal TextureHandle debugTexture;

            // Buffer

            // Ray Tracing
            internal bool requireRayTracing;
            internal RayTracingAccelerationStructure rtas;
            internal ComputeShader TraceShadowCS;

            internal uint dispatchRaySizeX;
            internal uint dispatchRaySizeY;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal RuntimeTextureSystem.DitheredTextureHandleSet ditheredTextureHandleSet;

            internal float radius;
            internal int sampleCount;
            internal int frameIndex;

            // Classify outputs
            internal BufferHandle tilesBuffer;
            internal BufferHandle tileCountBuffer;
            internal ComputeShader shadowClassifyCS;
            internal ShadowClassifyMode classifyMode;
            internal int classifyByNormalKernel;
            internal int classifyByCascadeRangeKernel;
            internal int classifyByCascadesKernel;
            internal ClassifyConstants constants;
            internal TextureHandle normalGBuffer;
            internal TextureHandle dirShadowmapTex;
        }

        static class ShaderConstants
        {
            public static readonly int _RayTracingShadowsTextureRW = Shader.PropertyToID("_RayTracingShadowsTextureRW");
            public static readonly int _ShadowMaskTexture = Shader.PropertyToID("_ShadowMaskTexture");
            public static readonly int _ScramblingTexture = Shader.PropertyToID("_ScramblingTexture");

            public static readonly int _RaytracingShadowTexture = Shader.PropertyToID("_RaytracingShadowTexture");
            public static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int _CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");
            public static readonly int _ShadowTileBuffer = Shader.PropertyToID("_ShadowTileBuffer");

            public static readonly int radius = Shader.PropertyToID("radius");
            public static readonly int sampleCount = Shader.PropertyToID("sampleCount");
            public static readonly int frameIndex = Shader.PropertyToID("frameIndex");
        }


        private void InitRayTracingPassData(
            RenderGraph renderGraph,
            PassData passData,
            RayTracingSystem rayTracingSystem,
            ContextContainer frameData)
        {
            var stack = VolumeManager.instance.stack;
            var volumeSettings = stack.GetComponent<Shadows>();
            if (!volumeSettings)
            {
                passData.requireRayTracing = false;
                return;
            }

            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var lightData = frameData.Get<UniversalLightData>();
            var historyRT = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);


            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<RaytracingShadowRuntimeShaders>();
            passData.TraceShadowCS = runtimeShaders.hybridShadowShader;
            passData.rtas = rayTracingSystem.RequestAccelerationStructure(cameraData);

            var width = cameraData.actualWidth;
            var height = cameraData.actualHeight;
            passData.dispatchRaySizeX = (uint)width;
            passData.dispatchRaySizeY = (uint)height;

            passData.shadowMaskTexture = renderGraph.CreateTexture(
                new TextureDesc(CoreUtils.DivRoundUp(cameraData.scaledWidth, 8), CoreUtils.DivRoundUp(cameraData.scaledHeight, 4))
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R32_UInt,
                    name = " ShadowMask Texture"
                });

            // RayTracing constant buffer

            var rayTracingSettings = stack.GetComponent<RayTracingSettings>();

            passData.rayTracingCB = rayTracingSystem.GetShaderVariablesRaytracingCB(cameraData);
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

            passData.blueNoiseTexture = renderGraph.ImportTexture(RuntimeTextureSystem.instance.scramblingTex);
            passData.cameraDepthTexture = resourceData.activeDepthTexture;
            passData.normalGBuffer = resourceData.gBuffer[2];
            passData.frameIndex = historyRT.historyFrameCount;
            passData.sampleCount = volumeSettings.sampleCount.value;
            passData.radius = volumeSettings.radius.value;


            if (m_ShadowClassifyCS == null)
            {
                m_ShadowClassifyCS = runtimeShaders.fidelityFXShadowClassify;
                if (m_ShadowClassifyCS != null)
                {
                    m_ClassifyByNormalKernel = m_ShadowClassifyCS.FindKernel("ClassifyByNormal");
                    m_ClassifyByCascadeRangeKernel = m_ShadowClassifyCS.FindKernel("ClassifyByCascadeRange");
                    m_ClassifyByCascadesKernel = m_ShadowClassifyCS.FindKernel("ClassifyByCascades");
                }
            }

            var shadowData = frameData.Get<UniversalShadowData>();
            var shadowSetting = VolumeManager.instance.stack.GetComponent<Shadows>();

            passData.shadowClassifyCS = m_ShadowClassifyCS;
            passData.classifyMode = shadowSetting.shadowClassifyMode.value;
            passData.classifyByNormalKernel = m_ClassifyByNormalKernel;
            passData.classifyByCascadeRangeKernel = m_ClassifyByCascadeRangeKernel;
            passData.classifyByCascadesKernel = m_ClassifyByCascadesKernel;

            passData.normalGBuffer = resourceData.gBuffer[2];
            passData.dirShadowmapTex = resourceData.directionalShadowsTexture;

            int tileWidth = CoreUtils.DivRoundUp(cameraData.actualWidth, 8);
            int tileHeight = CoreUtils.DivRoundUp(cameraData.actualHeight, 4);
            passData.dispatchRaySizeX = (uint)tileWidth;
            passData.dispatchRaySizeY = (uint)tileHeight;

            var constants = new ClassifyConstants
            {
                textureSize = new float4(cameraData.actualWidth, cameraData.actualHeight,
                    1.0f / cameraData.actualWidth, 1.0f / cameraData.actualHeight)
            };

            int mainLightIndex = lightData.mainLightIndex;
            if (mainLightIndex >= 0)
            {
                var mainLight = lightData.visibleLights[mainLightIndex];
                constants.lightDir = mainLight.GetForward();
            }
            else
            {
                constants.lightDir = new float3(0, -1, 0);
            }

            constants.skyHeight = 1000.0f;
            constants.pixelThickness = 0.01f;
            constants.sunSize = shadowSetting.radius.value;
            constants.noisePhase = Time.time;
            constants.bRejectLitPixels = 0;
            constants.cascadeCount = (uint)shadowData.mainLightShadowCascadesCount;
            constants.activeCascades = (uint)((1 << shadowData.mainLightShadowCascadesCount) - 1);
            constants.tileTolerance = 0;
            constants.blockerOffset = 0.001f;
            constants.cascadePixelSize = 1.0f / shadowData.mainLightShadowmapWidth;
            constants.cascadeSize = shadowData.mainLightShadowmapWidth;
            constants.sunSizeLightSpace = constants.sunSize;
            constants.bUseCascadesForRayT = 1;

            if (mainLightIndex >= 0 &&
                mainLightIndex < shadowData.visibleLightsShadowCullingInfos.Length)
            {
                var shadowLight = shadowData.visibleLightsShadowCullingInfos[mainLightIndex];
                for (int i = 0; i < shadowData.mainLightShadowCascadesCount && i < 4; i++)
                {
                    if (i < shadowLight.slices.Length)
                    {
                        var shadowSlice = shadowLight.slices[i];
                        var shadowTransform = shadowSlice.shadowTransform;
                        var scale = new float4(shadowTransform.m00, shadowTransform.m11, shadowTransform.m22, 1.0f);
                        var offset = new float4(shadowTransform.m03, shadowTransform.m13, shadowTransform.m23, 0.0f);
                        unsafe
                        {
                            for (int j = 0; j < 4; j++)
                            {
                                constants.cascadeScale[i * 4 + j] = scale[j];
                                constants.cascadeOffset[i * 4 + j] = offset[j];
                            }
                        }
                    }
                }
            }

            constants.viewToWorld = cameraData.GetViewMatrix().inverse;
            if (mainLightIndex >= 0)
            {
                var light = lightData.visibleLights[mainLightIndex];
                constants.lightView = float4x4.LookAt(light.GetPosition(), light.GetForward(), light.GetUp());
                constants.inverseLightView = math.inverse(constants.lightView);
            }
            else
            {
                constants.lightView = float4x4.identity;
                constants.inverseLightView = float4x4.identity;
            }

            passData.rayTracingCB = default;
            passData.constants = constants;

            int maxTiles = tileWidth * tileHeight;
            passData.rayHitResultsTexture = renderGraph.CreateTexture(new TextureDesc(tileWidth, tileHeight)
            {
                format = GraphicsFormat.R32_UInt,
                enableRandomWrite = true,
                name = "Shadow Classify Ray Hit Results"
            });



            passData.tilesBuffer = renderGraph.CreateBuffer(new BufferDesc(maxTiles * 16, 16)
            {
                name = "Shadow Classify Tiles"
            });

            var bufferSystem = GraphicsBufferSystem.instance;
            var dispatchIndirectBuffer = bufferSystem.GetGraphicsBuffer<uint>(
                GraphicsBufferSystemBufferID.ShadowTileCountBuffer,
                4,
                "ShadowClassifyTileCount",
                GraphicsBuffer.Target.IndirectArguments);
            dispatchIndirectBuffer.SetData(new int[] { 0 });
            passData.tileCountBuffer = renderGraph.ImportBuffer(dispatchIndirectBuffer);
        }

        private static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            if (data.requireRayTracing)
            {
                using (new ProfilingScope(cmd, new ProfilingSampler("Hybrid RayTracing Shadows Classify")))
                {
                    int kernelIndex = data.classifyMode switch
                    {
                        ShadowClassifyMode.ByNormal =>data.classifyByNormalKernel ,
                        ShadowClassifyMode.ByCascadeRange => data.classifyByCascadeRangeKernel,
                        ShadowClassifyMode.ByCascades => data.classifyByCascadesKernel,
                    };

                    ConstantBuffer.Push(cmd, data.rayTracingCB, data.shadowClassifyCS, RayTracingSystem._ShaderVariablesRaytracing);
                    ConstantBuffer.Push(cmd, data.constants, data.shadowClassifyCS, s_ControlsBufferID);

                    cmd.SetComputeTextureParam(data.shadowClassifyCS, kernelIndex, s_DepthTextureID, data.cameraDepthTexture);
                    cmd.SetComputeTextureParam(data.shadowClassifyCS, kernelIndex, s_NormalTextureID, data.normalGBuffer);
                    cmd.SetComputeTextureParam(data.shadowClassifyCS, kernelIndex, s_ShadowMapID, data.dirShadowmapTex);
                    cmd.SetComputeBufferParam(data.shadowClassifyCS, kernelIndex, s_TilesBufferID, data.tilesBuffer);
                    cmd.SetComputeBufferParam(data.shadowClassifyCS, kernelIndex, s_TileCountBufferID, data.tileCountBuffer);
                    cmd.SetComputeTextureParam(data.shadowClassifyCS, kernelIndex, s_RayHitResultsID, data.rayHitResultsTexture);

                    cmd.DispatchCompute(data.shadowClassifyCS, kernelIndex, (int)data.dispatchRaySizeX, (int)data.dispatchRaySizeY, 1);
                }


                
                using (new ProfilingScope(cmd, new ProfilingSampler("Hybrid RayTracing Shadows Trace")))
                {
                    // Set the acceleration structure for the pass
                    cmd.SetRayTracingAccelerationStructure(data.TraceShadowCS, 0, "_RaytracingAccelerationStructure", data.rtas);

                    // SetConstantBuffer
                    ConstantBuffer.Push(cmd, data.rayTracingCB, data.TraceShadowCS, RayTracingSystem._ShaderVariablesRaytracing);
                    ConstantBuffer.Push(cmd, data.constants, data.TraceShadowCS, s_ControlsBufferID);

                    // SetTextures
                    cmd.SetComputeTextureParam(data.TraceShadowCS, 0, ShaderConstants._ScramblingTexture, data.blueNoiseTexture);

                    cmd.SetComputeTextureParam(data.TraceShadowCS, 0, ShaderConstants._CameraDepthTexture, data.cameraDepthTexture);
                    cmd.SetComputeTextureParam(data.TraceShadowCS, 0, ShaderConstants._CameraNormalsTexture, data.normalGBuffer);
                    cmd.SetComputeTextureParam(data.TraceShadowCS, 0, ShaderConstants._ShadowMaskTexture, data.shadowMaskTexture);

                    cmd.SetComputeTextureParam(data.TraceShadowCS, 0,"debugTexture", data.debugTexture);

                    cmd.SetComputeBufferParam(data.TraceShadowCS, 0, ShaderConstants._ShadowTileBuffer, data.tilesBuffer);


                    cmd.SetComputeFloatParam(data.TraceShadowCS, ShaderConstants.sampleCount, data.sampleCount);
                    cmd.SetComputeFloatParam(data.TraceShadowCS, ShaderConstants.radius, data.radius);
                    cmd.SetComputeIntParam(data.TraceShadowCS, ShaderConstants.frameIndex, data.frameIndex);
                    cmd.DispatchCompute(data.TraceShadowCS, 0, data.tileCountBuffer, 0);


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

            TextureHandle shadowmask;
            using (var builder = renderGraph.AddComputePass<PassData>("Hybrid RayTracing Shadows", out var passData))
            {
                var rayTracingSystem = RayTracingSystem.instance;

                passData.requireRayTracing = rayTracingSystem.GetRayTracingState();

                InitRayTracingPassData(renderGraph, passData, rayTracingSystem, frameData);
                int tileWidth = CoreUtils.DivRoundUp(cameraData.actualWidth, 8);
                int tileHeight = CoreUtils.DivRoundUp(cameraData.actualHeight, 4);

                passData.debugTexture = builder.CreateTransientTexture(new TextureDesc(tileWidth, tileHeight)
                {
                    format = GraphicsFormat.R8_UNorm,
                    enableRandomWrite = true
                });

                if (!passData.requireRayTracing)
                {
                    return;
                }


                builder.UseBuffer(passData.tilesBuffer, AccessFlags.Write);
                builder.UseBuffer(passData.tileCountBuffer, AccessFlags.Write);
                builder.UseTexture(passData.dirShadowmapTex, AccessFlags.Write);
                builder.UseTexture(passData.shadowMaskTexture, AccessFlags.Write);
                builder.UseTexture(passData.rayHitResultsTexture, AccessFlags.Write);

                builder.UseTexture(passData.cameraDepthTexture);
                builder.UseTexture(passData.normalGBuffer);
                builder.UseTexture(passData.blueNoiseTexture);
                
                builder.SetRenderFunc((PassData data, ComputeGraphContext context) => { ExecutePass(data, context); });
                shadowmask = passData.shadowMaskTexture;
                resourceData.tilesBuffer = passData.tilesBuffer;
                resourceData.tileCountBuffer = passData.tileCountBuffer;
            }

            resourceData.shadowMaskTexture = shadowmask;
            var hybridShadowDenoiser = cameraData.denoiseSystem.fidelityFXShadowDenoiser;
            var denoised = hybridShadowDenoiser.Denoise(renderGraph, frameData, resourceData.cameraDepthTexture,
                resourceData.motionVectorColor,
                resourceData.gBuffer[2], shadowmask);
            resourceData.raytracingShadowTexture = denoised;
        }
    }


    /// <summary>
    /// Context item to store shadow classify buffers for debug access
    /// </summary>
    partial class UniversalResourceData
    {
        public BufferHandle tilesBuffer;
        public BufferHandle tileCountBuffer;
        public TextureHandle shadowMaskTexture;
    }
}