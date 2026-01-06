using System.Linq;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Referenced Path Tracing render pass for multi-bounce global illumination
    /// Implements physically-based path tracing using WorldLightCluster for light queries
    /// </summary>
    public class ReferencedPathTracingPass : ScriptableRenderPass
    {
        private const string kPassName = "Referenced Path Tracing";
        private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler(kPassName);

        // Path tracing settings
        private int m_MaxBounces = 4;
        private int m_SamplesPerPixel = 1;
        private float m_FireflyClamp = 10.0f;
        private bool m_UseNVSER = true;
        private bool m_AccumulateFrames = true;

        // History management
        private int m_FrameIndex = 0;

        public ReferencedPathTracingPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
        }

        /// <summary>
        /// Configure path tracing settings (optional - will use volume settings if not called)
        /// </summary>
        public void Setup(int maxBounces = 4, int samplesPerPixel = 1, float fireflyClamp = 10.0f, bool useNVSER = true, bool accumulate = true)
        {
            m_MaxBounces = maxBounces;
            m_SamplesPerPixel = samplesPerPixel;
            m_FireflyClamp = fireflyClamp;
            m_UseNVSER = useNVSER && ExtensionSystem.SupportedExtension.Contains(HardwareExtension.ShaderExecutionReordering);
            m_AccumulateFrames = accumulate;
        }

        /// <summary>
        /// Apply settings from volume component
        /// </summary>
        private void ApplyVolumeSettings(GlobalIllumination settings)
        {
            if (settings == null || !settings.IsPathTracingActive())
                return;

            m_MaxBounces = settings.GetMaxBounces();
            m_SamplesPerPixel = settings.GetSamplesPerPixel();
            m_FireflyClamp = settings.fireflyClamp.value;
            m_UseNVSER = settings.useNVSER.value && ExtensionSystem.SupportedExtension.Contains(HardwareExtension.ShaderExecutionReordering);
            m_AccumulateFrames = settings.temporalAccumulation.value;
        }

        class PassData
        {
            // Textures
            internal TextureHandle gBuffer0; // Albedo + AO
            internal TextureHandle gBuffer1; // Specular/Metallic + Roughness
            internal TextureHandle gBuffer2; // World space normal
            internal TextureHandle depthTexture;
            internal TextureHandle outputTexture;
            internal TextureHandle historyTexture;
            internal TextureHandle skyTexture;

            // NRD separate diffuse/specular outputs
            internal TextureHandle diffuseOutputTexture;
            internal TextureHandle specularOutputTexture;
            internal TextureHandle diffuseHistoryTexture;
            internal TextureHandle specularHistoryTexture;

            // Ray tracing resources
            internal RayTracingShader pathTracingShader;
            internal RayTracingAccelerationStructure rtas;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal RuntimeTextureSystem.DitheredTextureHandleSet ditheredTextureHandleSet;

            // Dispatch parameters
            internal uint dispatchWidth;
            internal uint dispatchHeight;

            // Path tracing specific parameters (not in ShaderVariablesRaytracing CB)
            internal bool accumulate;
            internal float intensity;
            internal float environmentIntensity;
            internal bool includeEmissive;
            internal bool includeDirectLighting;
            internal int debugVisualizeBounce;
            internal int debugMode;

            // NRD parameters
            internal float nrdHitDistanceParams;

            // SHARC parameters
            internal bool enableSharc;
            internal bool sharcUpdate;
            internal bool sharcQuery;
            internal Vector3 sharcCameraPosition;
            internal float sharcSceneScale;
            internal float sharcRoughnessThreshold;
            internal float sharcRadianceScale;
            internal int sharcGridLevelBias;
            internal int sharcSampleThreshold;
            internal int sharcEntriesNum;
            internal bool sharcAntiFirefly;
            internal bool sharcDebug;

            // SHARC buffers (RenderGraph managed)
            internal BufferHandle sharcHashEntriesBuffer;
            internal BufferHandle sharcLockBuffer;
            internal BufferHandle sharcAccumulationBuffer;
            internal BufferHandle sharcResolvedBuffer;
        }

        /// <summary>
        /// Pass data for copying output to history buffer
        /// </summary>
        class CopyToHistoryPassData
        {
            internal TextureHandle source;
            internal TextureHandle destination;
        }

        /// <summary>
        /// Pass data for debug visualization (blit to screen)
        /// </summary>
        class DebugVisualizationPassData
        {
            internal TextureHandle source;
            internal TextureHandle destination;
        }

        /// <summary>
        /// Pass data for SHARC resolve pass
        /// </summary>
        class SharcResolvePassData
        {
            internal ComputeShader resolveShader;
            internal int resolveKernel;
            internal Vector3 cameraPosition;
            internal Vector3 cameraPositionPrev;
            internal float sceneScale;
            internal float radianceScale;
            internal int gridLevelBias;
            internal int entriesNum;
            internal int accumulationFrameNum;
            internal int staleFrameNumMax;
            internal bool enableAntifirefly;

            // SHARC buffers (RenderGraph managed)
            internal BufferHandle sharcHashEntriesBuffer;
            internal BufferHandle sharcLockBuffer;
            internal BufferHandle sharcAccumulationBuffer;
            internal BufferHandle sharcResolvedBuffer;
        }

        static class ShaderConstants
        {
            public static readonly int _RaytracingAccelerationStructure = Shader.PropertyToID("_RaytracingAccelerationStructure");
            public static readonly int _PathTracingOutput = Shader.PropertyToID("_PathTracingOutput");
            public static readonly int _PathTracingHistory = Shader.PropertyToID("_PathTracingHistory");

            // Separate diffuse/specular outputs for NRD denoising
            public static readonly int _PathTracingDiffuseOutput = Shader.PropertyToID("_PathTracingDiffuseOutput");
            public static readonly int _PathTracingSpecularOutput = Shader.PropertyToID("_PathTracingSpecularOutput");
            public static readonly int _PathTracingDiffuseHistory = Shader.PropertyToID("_PathTracingDiffuseHistory");
            public static readonly int _PathTracingSpecularHistory = Shader.PropertyToID("_PathTracingSpecularHistory");

            public static readonly int _GBuffer0 = Shader.PropertyToID("_GBuffer0");
            public static readonly int _GBuffer1 = Shader.PropertyToID("_GBuffer1");
            public static readonly int _GBuffer2 = Shader.PropertyToID("_GBuffer2");
            public static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int _SkyTexture = Shader.PropertyToID("_SkyTexture");

            // Path tracing specific parameters (not in ShaderVariablesRaytracing CB)
            public static readonly int _PathTracingAccumulate = Shader.PropertyToID("_PathTracingAccumulate");
            public static readonly int _PathTracingIntensity = Shader.PropertyToID("_PathTracingIntensity");
            public static readonly int _PathTracingEnvironmentIntensity = Shader.PropertyToID("_PathTracingEnvironmentIntensity");
            public static readonly int _PathTracingIncludeEmissive = Shader.PropertyToID("_PathTracingIncludeEmissive");
            public static readonly int _PathTracingIncludeDirectLighting = Shader.PropertyToID("_PathTracingIncludeDirectLighting");
            public static readonly int _PathTracingDebugVisualizeBounce = Shader.PropertyToID("_PathTracingDebugVisualizeBounce");
            public static readonly int _PathTracingDebugMode = Shader.PropertyToID("_PathTracingDebugMode");

            // NRD parameters
            public static readonly int _NRDHitDistanceParams = Shader.PropertyToID("_NRDHitDistanceParams");

            // SHARC parameters
            public static readonly int _SharcHashEntriesBuffer = Shader.PropertyToID("_SharcHashEntriesBuffer");
            public static readonly int _SharcLockBuffer = Shader.PropertyToID("_SharcLockBuffer");
            public static readonly int _SharcAccumulationBuffer = Shader.PropertyToID("_SharcAccumulationBuffer");
            public static readonly int _SharcResolvedBuffer = Shader.PropertyToID("_SharcResolvedBuffer");
            public static readonly int _SharcCameraPosition = Shader.PropertyToID("_SharcCameraPosition");
            public static readonly int _SharcCameraPositionPrev = Shader.PropertyToID("_SharcCameraPositionPrev");
            public static readonly int _SharcSceneScale = Shader.PropertyToID("_SharcSceneScale");
            public static readonly int _SharcRoughnessThreshold = Shader.PropertyToID("_SharcRoughnessThreshold");
            public static readonly int _SharcEntriesNum = Shader.PropertyToID("_SharcEntriesNum");
            public static readonly int _SharcEnableAntifirefly = Shader.PropertyToID("_SharcEnableAntifirefly");
            public static readonly int _SharcDebug = Shader.PropertyToID("_SharcDebug");
            public static readonly int _SharcAccumulationFrameNum = Shader.PropertyToID("_SharcAccumulationFrameNum");
            public static readonly int _SharcStaleFrameNumMax = Shader.PropertyToID("_SharcStaleFrameNumMax");
            public static readonly int _SharcRadianceScale = Shader.PropertyToID("_SharcRadianceScale");
            public static readonly int _SharcGridLevelBias = Shader.PropertyToID("_SharcGridLevelBias");
            public static readonly int _SharcSampleThreshold = Shader.PropertyToID("_SharcSampleThreshold");

            // SHARC shader keywords
            public static readonly GlobalKeyword SharcUpdateKeyword = GlobalKeyword.Create("SHARC_UPDATE");
            public static readonly GlobalKeyword SharcQueryKeyword = GlobalKeyword.Create("SHARC_QUERY");
        }

        private void InitializePassData(
            RenderGraph renderGraph,
            PassData passData,
            RayTracingSystem rayTracingSystem,
            UniversalCameraData cameraData,
            UniversalResourceData resourceData,
            GlobalIllumination giSettings)
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VividRuntimeShader>();

            // Get ray tracing resources
            passData.pathTracingShader = runtimeShaders.referencedPathTracingRTShader;
            passData.rtas = rayTracingSystem.RequestAccelerationStructure(cameraData);

            // Setup ray tracing constant buffer with path tracing parameters
            passData.rayTracingCB = rayTracingSystem.GetShaderVariablesRaytracingCB(cameraData);
            passData.rayTracingCB._RaytracingMaxRecursion = m_MaxBounces;
            passData.rayTracingCB._RaytracingNumSamples = m_SamplesPerPixel;
            passData.rayTracingCB._RaytracingIntensityClamp = m_FireflyClamp;
            passData.rayTracingCB._RaytracingSampleIndex = m_FrameIndex;
            passData.rayTracingCB._RaytracingRayMaxLength = giSettings.rayLength.value;
            passData.rayTracingCB._nvSER = m_UseNVSER ? 1 : 0;

            // Setup dithered texture set for blue noise sampling
            passData.ditheredTextureHandleSet = RuntimeTextureSystem.instance.DitheredTextureSet8SPP().RenderGraphImport(renderGraph);

            // Input textures
            passData.gBuffer0 = resourceData.gBuffer[0];
            passData.gBuffer1 = resourceData.gBuffer[1];
            passData.gBuffer2 = resourceData.gBuffer[2];
            passData.depthTexture = resourceData.cameraDepthTexture;

            // TODO: Get sky texture from environment settings
            // For now, we'll use a default black texture
            passData.skyTexture = renderGraph.ImportTexture(SkySystem.instance.GetSkyCubemap());

            // Dispatch size
            passData.dispatchWidth = (uint)cameraData.cameraTargetDescriptor.width;
            passData.dispatchHeight = (uint)cameraData.cameraTargetDescriptor.height;

            // Path tracing specific parameters (not in CB)
            passData.accumulate = m_AccumulateFrames;
            passData.intensity = giSettings.pathTracingIntensity.value;
            passData.environmentIntensity = giSettings.environmentIntensity.value;
            passData.includeEmissive = giSettings.includeEmissive.value;
            passData.includeDirectLighting = giSettings.includeDirectLighting.value;
            passData.debugVisualizeBounce = giSettings.debugVisualizeBounce.value;
            passData.debugMode = (int)giSettings.debugMode.value;

            // NRD parameters - use a default value for hit distance normalization
            // This can be exposed in volume settings later
            passData.nrdHitDistanceParams = 0.1f;  // Scene-dependent constant

            // SHARC parameters
            passData.enableSharc = giSettings.enableSharc.value;
            passData.sharcUpdate = giSettings.sharcUpdate.value;
            passData.sharcQuery = giSettings.sharcQuery.value;
            passData.sharcCameraPosition = cameraData.camera.transform.position;
            passData.sharcSceneScale = giSettings.sharcSceneScale.value;
            passData.sharcRoughnessThreshold = giSettings.sharcRoughnessThreshold.value;
            passData.sharcRadianceScale = giSettings.sharcRadianceScale.value;
            passData.sharcGridLevelBias = giSettings.sharcGridLevelBias.value;
            passData.sharcSampleThreshold = giSettings.sharcSampleThreshold.value;
            passData.sharcEntriesNum = giSettings.sharcEntriesK.value * 1024;
            passData.sharcAntiFirefly = giSettings.sharcAntiFirefly.value;
            passData.sharcDebug = giSettings.sharcDebug.value;
            // Note: SHARC buffer handles are set in RecordRenderGraph after importing
        }

        private static void ExecutePathTracing(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            using (new ProfilingScope(cmd, s_ProfilingSampler))
            {
                // Set ray tracing shader pass
                cmd.SetRayTracingShaderPass(data.pathTracingShader, "PathTracingDXR");

                // Bind acceleration structure
                cmd.SetRayTracingAccelerationStructure(data.pathTracingShader, ShaderConstants._RaytracingAccelerationStructure, data.rtas);

                // Push ray tracing constant buffer (contains maxBounces, samplesPerPixel, fireflyClamp, frameIndex, rayLength, nvSER)
                ConstantBuffer.PushGlobal(cmd, data.rayTracingCB, RayTracingSystem._ShaderVariablesRaytracing);

                // Bind dithered texture set (blue noise)
                RuntimeTextureSystem.BindDitheredTextureSet(cmd, data.ditheredTextureHandleSet);

                // Bind input textures
                cmd.SetRayTracingTextureParam(data.pathTracingShader, ShaderConstants._GBuffer0, data.gBuffer0);
                cmd.SetRayTracingTextureParam(data.pathTracingShader, ShaderConstants._GBuffer1, data.gBuffer1);
                cmd.SetRayTracingTextureParam(data.pathTracingShader, ShaderConstants._GBuffer2, data.gBuffer2);
                cmd.SetRayTracingTextureParam(data.pathTracingShader, ShaderConstants._CameraDepthTexture, data.depthTexture);

                // Bind output texture
                cmd.SetRayTracingTextureParam(data.pathTracingShader, ShaderConstants._PathTracingOutput, data.outputTexture);

                // Bind history texture if accumulating
                if (data.accumulate && data.historyTexture.IsValid())
                {
                    cmd.SetRayTracingTextureParam(data.pathTracingShader, ShaderConstants._PathTracingHistory, data.historyTexture);
                }

                // Bind sky texture if available
                if (data.skyTexture.IsValid())
                {
                    cmd.SetRayTracingTextureParam(data.pathTracingShader, ShaderConstants._SkyTexture, data.skyTexture);
                }

                // Set path tracing specific parameters (not in ShaderVariablesRaytracing CB)
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingAccumulate, data.accumulate ? 1 : 0);
                cmd.SetRayTracingFloatParam(data.pathTracingShader, ShaderConstants._PathTracingIntensity, data.intensity);
                cmd.SetRayTracingFloatParam(data.pathTracingShader, ShaderConstants._PathTracingEnvironmentIntensity, data.environmentIntensity);
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingIncludeEmissive, data.includeEmissive ? 1 : 0);
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingIncludeDirectLighting, data.includeDirectLighting ? 1 : 0);
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingDebugVisualizeBounce, data.debugVisualizeBounce);
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingDebugMode, data.debugMode);

                // NRD parameters
                cmd.SetRayTracingFloatParam(data.pathTracingShader, ShaderConstants._NRDHitDistanceParams, data.nrdHitDistanceParams);

                // Bind diffuse/specular output textures for NRD
                if (data.diffuseOutputTexture.IsValid())
                {
                    cmd.SetRayTracingTextureParam(data.pathTracingShader, ShaderConstants._PathTracingDiffuseOutput, data.diffuseOutputTexture);
                }
                if (data.specularOutputTexture.IsValid())
                {
                    cmd.SetRayTracingTextureParam(data.pathTracingShader, ShaderConstants._PathTracingSpecularOutput, data.specularOutputTexture);
                }

                // Bind diffuse/specular history textures if accumulating
                if (data.accumulate)
                {
                    if (data.diffuseHistoryTexture.IsValid())
                    {
                        cmd.SetRayTracingTextureParam(data.pathTracingShader, ShaderConstants._PathTracingDiffuseHistory, data.diffuseHistoryTexture);
                    }
                    if (data.specularHistoryTexture.IsValid())
                    {
                        cmd.SetRayTracingTextureParam(data.pathTracingShader, ShaderConstants._PathTracingSpecularHistory, data.specularHistoryTexture);
                    }
                }

                // Bind SHARC buffers and parameters if enabled
                if (data.enableSharc && data.sharcHashEntriesBuffer.IsValid())
                {
                    // Enable/disable SHARC shader keywords based on volume settings
                    cmd.SetKeyword(ShaderConstants.SharcUpdateKeyword, data.sharcUpdate);
                    cmd.SetKeyword(ShaderConstants.SharcQueryKeyword, data.sharcQuery);

                    // Bind SHARC buffers (RenderGraph managed)
                    cmd.SetRayTracingBufferParam(data.pathTracingShader, ShaderConstants._SharcHashEntriesBuffer, data.sharcHashEntriesBuffer);
                    cmd.SetRayTracingBufferParam(data.pathTracingShader, ShaderConstants._SharcLockBuffer, data.sharcLockBuffer);
                    cmd.SetRayTracingBufferParam(data.pathTracingShader, ShaderConstants._SharcAccumulationBuffer, data.sharcAccumulationBuffer);
                    cmd.SetRayTracingBufferParam(data.pathTracingShader, ShaderConstants._SharcResolvedBuffer, data.sharcResolvedBuffer);

                    // Bind SHARC parameters
                    cmd.SetRayTracingVectorParam(data.pathTracingShader, ShaderConstants._SharcCameraPosition, data.sharcCameraPosition);
                    cmd.SetRayTracingFloatParam(data.pathTracingShader, ShaderConstants._SharcSceneScale, data.sharcSceneScale);
                    cmd.SetRayTracingFloatParam(data.pathTracingShader, ShaderConstants._SharcRoughnessThreshold, data.sharcRoughnessThreshold);
                    cmd.SetRayTracingFloatParam(data.pathTracingShader, ShaderConstants._SharcRadianceScale, data.sharcRadianceScale);
                    cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._SharcGridLevelBias, data.sharcGridLevelBias);
                    cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._SharcSampleThreshold, data.sharcSampleThreshold);
                    cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._SharcEntriesNum, data.sharcEntriesNum);
                    cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._SharcEnableAntifirefly, data.sharcAntiFirefly ? 1 : 0);
                    cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._SharcDebug, data.sharcDebug ? 1 : 0);
                }
                else
                {
                    // Disable SHARC keywords when not enabled
                    cmd.SetKeyword(ShaderConstants.SharcUpdateKeyword, false);
                    cmd.SetKeyword(ShaderConstants.SharcQueryKeyword, false);
                }

                // Dispatch rays
                cmd.DispatchRays(data.pathTracingShader, "RayGenPathTracing", data.dispatchWidth, data.dispatchHeight, 1);
            }
        }

        /// <summary>
        /// History buffer allocator function
        /// </summary>
        static RTHandle PathTracingHistoryBufferAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1; // Ping-pong between 2 buffers

            return rtHandleSystem.Alloc(
                Vector2.one,
                colorFormat: graphicsFormat,
                enableRandomWrite: true,
                useDynamicScale: true,
                name: $"{viewName}_PathTracingHistory{frameIndex}"
            );
        }

        /// <summary>
        /// Diffuse history buffer allocator function (for NRD REBLUR)
        /// Format: RGB = diffuse radiance, A = normalized hit distance
        /// </summary>
        static RTHandle PathTracingDiffuseHistoryBufferAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1; // Ping-pong between 2 buffers

            return rtHandleSystem.Alloc(
                Vector2.one,
                colorFormat: graphicsFormat,
                enableRandomWrite: true,
                useDynamicScale: true,
                name: $"{viewName}_PathTracingDiffuseHistory{frameIndex}"
            );
        }

        /// <summary>
        /// Specular history buffer allocator function (for NRD REBLUR)
        /// Format: RGB = specular radiance, A = normalized hit distance
        /// </summary>
        static RTHandle PathTracingSpecularHistoryBufferAllocator(GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1; // Ping-pong between 2 buffers

            return rtHandleSystem.Alloc(
                Vector2.one,
                colorFormat: graphicsFormat,
                enableRandomWrite: true,
                useDynamicScale: true,
                name: $"{viewName}_PathTracingSpecularHistory{frameIndex}"
            );
        }

        /// <summary>
        /// Reallocate history buffer if needed
        /// </summary>
        internal RTHandle ReAllocateHistoryBufferIfNeeded(HistoryFrameRTSystem historyRTSystem)
        {
            return historyRTSystem.GetCurrentFrameRT(HistoryFrameType.PathTracingHistory)
                   ?? historyRTSystem.AllocHistoryFrameRT(
                       (int)HistoryFrameType.PathTracingHistory,
                       PathTracingHistoryBufferAllocator,
                       GraphicsFormat.R16G16B16A16_SFloat,
                       1
                   );
        }

        /// <summary>
        /// Reallocate diffuse history buffer if needed (for NRD REBLUR)
        /// </summary>
        internal RTHandle ReAllocateDiffuseHistoryBufferIfNeeded(HistoryFrameRTSystem historyRTSystem)
        {
            return historyRTSystem.GetCurrentFrameRT(HistoryFrameType.PathTracingDiffuseHistory)
                   ?? historyRTSystem.AllocHistoryFrameRT(
                       (int)HistoryFrameType.PathTracingDiffuseHistory,
                       PathTracingDiffuseHistoryBufferAllocator,
                       GraphicsFormat.R16G16B16A16_SFloat,
                       1
                   );
        }

        /// <summary>
        /// Reallocate specular history buffer if needed (for NRD REBLUR)
        /// </summary>
        internal RTHandle ReAllocateSpecularHistoryBufferIfNeeded(HistoryFrameRTSystem historyRTSystem)
        {
            return historyRTSystem.GetCurrentFrameRT(HistoryFrameType.PathTracingSpecularHistory)
                   ?? historyRTSystem.AllocHistoryFrameRT(
                       (int)HistoryFrameType.PathTracingSpecularHistory,
                       PathTracingSpecularHistoryBufferAllocator,
                       GraphicsFormat.R16G16B16A16_SFloat,
                       1
                   );
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            // Get volume settings
            var stack = VolumeManager.instance.stack;
            var giSettings = stack.GetComponent<GlobalIllumination>();

            // Check if path tracing is enabled
            if (giSettings == null || !giSettings.IsPathTracingActive())
            {
                return;
            }

            // Apply volume settings
            ApplyVolumeSettings(giSettings);

            // Check if ray tracing is supported
            var rayTracingSystem = RayTracingSystem.instance;
            if (!rayTracingSystem.GetRayTracingState() || !RayTracingSystem.SupportedCamera(cameraData.camera))
            {
                return;
            }

            // Create output texture
            var outputDesc = new TextureDesc(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R16G16B16A16_SFloat,
                name = "PathTracingOutput"
            };
            var outputTexture = renderGraph.CreateTexture(outputDesc);

            // Create separate diffuse/specular output textures for NRD
            // Format: RGB = radiance, A = normalized hit distance
            var diffuseOutputDesc = new TextureDesc(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R16G16B16A16_SFloat,
                name = "PathTracingDiffuseOutput"
            };
            var diffuseOutputTexture = renderGraph.CreateTexture(diffuseOutputDesc);

            var specularOutputDesc = new TextureDesc(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R16G16B16A16_SFloat,
                name = "PathTracingSpecularOutput"
            };
            var specularOutputTexture = renderGraph.CreateTexture(specularOutputDesc);

            // Setup history buffer for temporal accumulation
            var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);
            RTHandle historyRT = null;
            RTHandle diffuseHistoryRT = null;
            RTHandle specularHistoryRT = null;
            TextureHandle historyTexture = TextureHandle.nullHandle;
            TextureHandle diffuseHistoryTexture = TextureHandle.nullHandle;
            TextureHandle specularHistoryTexture = TextureHandle.nullHandle;

            if (m_AccumulateFrames)
            {
                // Combined history buffer
                historyRT = ReAllocateHistoryBufferIfNeeded(historyRTSystem);
                historyTexture = renderGraph.ImportTexture(historyRT);

                // Diffuse/Specular history buffers for NRD REBLUR
                diffuseHistoryRT = ReAllocateDiffuseHistoryBufferIfNeeded(historyRTSystem);
                specularHistoryRT = ReAllocateSpecularHistoryBufferIfNeeded(historyRTSystem);
                diffuseHistoryTexture = renderGraph.ImportTexture(diffuseHistoryRT);
                specularHistoryTexture = renderGraph.ImportTexture(specularHistoryRT);
            }

            // Initialize and import SHARC buffers if enabled
            bool sharcEnabled = giSettings.enableSharc.value;
            BufferHandle sharcHashEntriesBuffer = BufferHandle.nullHandle;
            BufferHandle sharcLockBuffer = BufferHandle.nullHandle;
            BufferHandle sharcAccumulationBuffer = BufferHandle.nullHandle;
            BufferHandle sharcResolvedBuffer = BufferHandle.nullHandle;

            if (sharcEnabled)
            {
                int entriesNum = giSettings.sharcEntriesK.value * 1024;
                SharcSystem.instance.Initialize(entriesNum);

                if (SharcSystem.instance.IsInitialized)
                {
                    // Import SHARC buffers into RenderGraph for proper resource tracking
                    sharcHashEntriesBuffer = renderGraph.ImportBuffer(SharcSystem.instance.HashEntriesBuffer);
                    sharcLockBuffer = renderGraph.ImportBuffer(SharcSystem.instance.LockBuffer);
                    sharcAccumulationBuffer = renderGraph.ImportBuffer(SharcSystem.instance.AccumulationBuffer);
                    sharcResolvedBuffer = renderGraph.ImportBuffer(SharcSystem.instance.ResolvedBuffer);
                }
            }

            // Add path tracing pass
            using (var builder = renderGraph.AddComputePass<PassData>(kPassName, out var passData, s_ProfilingSampler))
            {
                InitializePassData(renderGraph, passData, rayTracingSystem, cameraData, resourceData, giSettings);

                passData.outputTexture = outputTexture;
                passData.historyTexture = historyTexture;

                // NRD diffuse/specular output textures
                passData.diffuseOutputTexture = diffuseOutputTexture;
                passData.specularOutputTexture = specularOutputTexture;
                passData.diffuseHistoryTexture = diffuseHistoryTexture;
                passData.specularHistoryTexture = specularHistoryTexture;

                // Pass imported SHARC buffer handles
                passData.sharcHashEntriesBuffer = sharcHashEntriesBuffer;
                passData.sharcLockBuffer = sharcLockBuffer;
                passData.sharcAccumulationBuffer = sharcAccumulationBuffer;
                passData.sharcResolvedBuffer = sharcResolvedBuffer;

                // Use input textures
                passData.ditheredTextureHandleSet.Use(builder);
                builder.UseTexture(passData.gBuffer0);
                builder.UseTexture(passData.gBuffer1);
                builder.UseTexture(passData.gBuffer2);
                builder.UseTexture(passData.depthTexture);

                // Use output textures
                builder.UseTexture(passData.outputTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.diffuseOutputTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.specularOutputTexture, AccessFlags.ReadWrite);

                // Use history texture if available
                if (passData.historyTexture.IsValid())
                {
                    builder.UseTexture(passData.historyTexture, AccessFlags.Read);
                }
                if (passData.diffuseHistoryTexture.IsValid())
                {
                    builder.UseTexture(passData.diffuseHistoryTexture, AccessFlags.Read);
                }
                if (passData.specularHistoryTexture.IsValid())
                {
                    builder.UseTexture(passData.specularHistoryTexture, AccessFlags.Read);
                }

                // Use sky texture if available
                if (passData.skyTexture.IsValid())
                {
                    builder.UseTexture(passData.skyTexture);
                }

                // Use SHARC buffers if enabled (read/write for update, read for query)
                if (sharcEnabled && sharcHashEntriesBuffer.IsValid())
                {
                    builder.UseBuffer(sharcHashEntriesBuffer, AccessFlags.ReadWrite);
                    builder.UseBuffer(sharcLockBuffer, AccessFlags.ReadWrite);
                    builder.UseBuffer(sharcAccumulationBuffer, AccessFlags.ReadWrite);
                    builder.UseBuffer(sharcResolvedBuffer, AccessFlags.Read);
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc<PassData>(ExecutePathTracing);
            }

            // SHARC Resolve Pass - must run after path tracing to merge accumulated samples
            if (sharcEnabled && sharcHashEntriesBuffer.IsValid())
            {
                var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VividRuntimeShader>();

                using (var builder = renderGraph.AddComputePass<SharcResolvePassData>("SHARC Resolve", out var passData))
                {
                    passData.resolveShader = runtimeShaders.sharcResolveCS;
                    passData.resolveKernel = runtimeShaders.sharcResolveCS.FindKernel("CSResolveEntries");
                    passData.cameraPosition = cameraData.camera.transform.position;
                    passData.cameraPositionPrev = SharcSystem.instance.PreviousCameraPosition;
                    passData.sceneScale = giSettings.sharcSceneScale.value;
                    passData.radianceScale = giSettings.sharcRadianceScale.value;
                    passData.gridLevelBias = giSettings.sharcGridLevelBias.value;
                    passData.entriesNum = giSettings.sharcEntriesK.value * 1024;
                    passData.accumulationFrameNum = giSettings.sharcAccumulationFrames.value;
                    passData.staleFrameNumMax = giSettings.sharcStaleFrames.value;
                    passData.enableAntifirefly = giSettings.sharcAntiFirefly.value;

                    // Pass imported SHARC buffer handles
                    passData.sharcHashEntriesBuffer = sharcHashEntriesBuffer;
                    passData.sharcLockBuffer = sharcLockBuffer;
                    passData.sharcAccumulationBuffer = sharcAccumulationBuffer;
                    passData.sharcResolvedBuffer = sharcResolvedBuffer;

                    // Declare buffer usage for RenderGraph
                    builder.UseBuffer(sharcHashEntriesBuffer, AccessFlags.ReadWrite);
                    builder.UseBuffer(sharcLockBuffer, AccessFlags.ReadWrite);
                    builder.UseBuffer(sharcAccumulationBuffer, AccessFlags.ReadWrite);
                    builder.UseBuffer(sharcResolvedBuffer, AccessFlags.ReadWrite);

                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc<SharcResolvePassData>((data, context) =>
                    {
                        var cmd = context.cmd;

                        // Bind SHARC buffers (RenderGraph managed)
                        cmd.SetComputeBufferParam(data.resolveShader, data.resolveKernel, ShaderConstants._SharcHashEntriesBuffer, data.sharcHashEntriesBuffer);
                        cmd.SetComputeBufferParam(data.resolveShader, data.resolveKernel, ShaderConstants._SharcLockBuffer, data.sharcLockBuffer);
                        cmd.SetComputeBufferParam(data.resolveShader, data.resolveKernel, ShaderConstants._SharcAccumulationBuffer,
                            data.sharcAccumulationBuffer);
                        cmd.SetComputeBufferParam(data.resolveShader, data.resolveKernel, ShaderConstants._SharcResolvedBuffer, data.sharcResolvedBuffer);

                        // Bind SHARC parameters (volume-controlled)
                        cmd.SetComputeVectorParam(data.resolveShader, ShaderConstants._SharcCameraPosition, data.cameraPosition);
                        cmd.SetComputeVectorParam(data.resolveShader, ShaderConstants._SharcCameraPositionPrev, data.cameraPositionPrev);
                        cmd.SetComputeFloatParam(data.resolveShader, ShaderConstants._SharcSceneScale, data.sceneScale);
                        cmd.SetComputeFloatParam(data.resolveShader, ShaderConstants._SharcRadianceScale, data.radianceScale);
                        cmd.SetComputeIntParam(data.resolveShader, ShaderConstants._SharcGridLevelBias, data.gridLevelBias);
                        cmd.SetComputeIntParam(data.resolveShader, ShaderConstants._SharcEntriesNum, data.entriesNum);
                        cmd.SetComputeIntParam(data.resolveShader, ShaderConstants._SharcAccumulationFrameNum, data.accumulationFrameNum);
                        cmd.SetComputeIntParam(data.resolveShader, ShaderConstants._SharcStaleFrameNumMax, data.staleFrameNumMax);
                        cmd.SetComputeIntParam(data.resolveShader, ShaderConstants._SharcEnableAntifirefly, data.enableAntifirefly ? 1 : 0);

                        // Dispatch: process all entries
                        int threadGroups = (data.entriesNum + 63) / 64;
                        cmd.DispatchCompute(data.resolveShader, data.resolveKernel, threadGroups, 1, 1);

                        // Update previous camera position for next frame
                        SharcSystem.instance.UpdatePreviousCameraPosition(data.cameraPosition);
                    });
                }
            }

            // Copy output to history buffer for next frame's temporal accumulation
            if (m_AccumulateFrames && historyTexture.IsValid())
            {
                using (var builder = renderGraph.AddRasterRenderPass<CopyToHistoryPassData>("Path Tracing - Copy to History", out var passData))
                {
                    passData.source = outputTexture;
                    passData.destination = historyTexture;

                    builder.UseTexture(passData.source);
                    builder.SetRenderAttachment(passData.destination, 0, AccessFlags.Write);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc<CopyToHistoryPassData>((data, context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
                    });
                }
            }

            // Copy diffuse output to history buffer for NRD REBLUR
            if (m_AccumulateFrames && diffuseHistoryTexture.IsValid())
            {
                using (var builder = renderGraph.AddRasterRenderPass<CopyToHistoryPassData>("Path Tracing - Copy Diffuse to History", out var passData))
                {
                    passData.source = diffuseOutputTexture;
                    passData.destination = diffuseHistoryTexture;

                    builder.UseTexture(passData.source);
                    builder.SetRenderAttachment(passData.destination, 0, AccessFlags.Write);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc<CopyToHistoryPassData>((data, context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
                    });
                }
            }

            // Copy specular output to history buffer for NRD REBLUR
            if (m_AccumulateFrames && specularHistoryTexture.IsValid())
            {
                using (var builder = renderGraph.AddRasterRenderPass<CopyToHistoryPassData>("Path Tracing - Copy Specular to History", out var passData))
                {
                    passData.source = specularOutputTexture;
                    passData.destination = specularHistoryTexture;

                    builder.UseTexture(passData.source);
                    builder.SetRenderAttachment(passData.destination, 0, AccessFlags.Write);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc<CopyToHistoryPassData>((data, context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
                    });
                }
            }

            // Debug visualization: Blit path tracing output directly to screen
            // This is useful for viewing the raw path tracing result before full integration
            if (giSettings.debugShowPathTracingOnly.value)
            {
                using (var builder = renderGraph.AddRasterRenderPass<DebugVisualizationPassData>("Path Tracing - Debug Visualization", out var passData))
                {
                    passData.source = outputTexture;
                    passData.destination = resourceData.activeColorTexture;

                    builder.UseTexture(passData.source);
                    builder.SetRenderAttachment(passData.destination, 0, AccessFlags.Write);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc<DebugVisualizationPassData>((data, context) =>
                    {
                        // Simple blit - direct copy of path tracing result to screen
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
                    });
                }
            }

            // TODO: Apply denoising if enabled
            // TODO: Composite path tracing result with main rendering (additive blend for GI)

            // Increment frame index for temporal sampling
            m_FrameIndex++;
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // Reset frame index when camera changes
        }
    }
}