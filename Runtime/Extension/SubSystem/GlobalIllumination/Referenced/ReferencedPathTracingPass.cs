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

            // Ray tracing resources
            internal RayTracingShader pathTracingShader;
            internal RayTracingAccelerationStructure rtas;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal RuntimeTextureSystem.DitheredTextureHandleSet ditheredTextureHandleSet;

            // Dispatch parameters
            internal uint dispatchWidth;
            internal uint dispatchHeight;

            // Path tracing parameters
            internal int maxBounces;
            internal int samplesPerPixel;
            internal float fireflyClamp;
            internal bool useNVSER;
            internal int frameIndex;
            internal bool accumulate;
            internal float intensity;
            internal float rayLength;
            internal float environmentIntensity;
            internal bool includeEmissive;
            internal bool includeDirectLighting;
            internal int debugVisualizeBounce;
        }

        static class ShaderConstants
        {
            public static readonly int _RaytracingAccelerationStructure = Shader.PropertyToID("_RaytracingAccelerationStructure");
            public static readonly int _PathTracingOutput = Shader.PropertyToID("_PathTracingOutput");
            public static readonly int _PathTracingHistory = Shader.PropertyToID("_PathTracingHistory");
            public static readonly int _GBuffer0 = Shader.PropertyToID("_GBuffer0");
            public static readonly int _GBuffer1 = Shader.PropertyToID("_GBuffer1");
            public static readonly int _GBuffer2 = Shader.PropertyToID("_GBuffer2");
            public static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int _SkyTexture = Shader.PropertyToID("_SkyTexture");
            public static readonly int _PathTracingMaxBounces = Shader.PropertyToID("_PathTracingMaxBounces");
            public static readonly int _PathTracingSamplesPerPixel = Shader.PropertyToID("_PathTracingSamplesPerPixel");
            public static readonly int _PathTracingFireflyClamp = Shader.PropertyToID("_PathTracingFireflyClamp");
            public static readonly int _PathTracingFrameIndex = Shader.PropertyToID("_PathTracingFrameIndex");
            public static readonly int _UseNVSER = Shader.PropertyToID("_UseNVSER");
            public static readonly int _PathTracingAccumulate = Shader.PropertyToID("_PathTracingAccumulate");
            public static readonly int _PathTracingIntensity = Shader.PropertyToID("_PathTracingIntensity");
            public static readonly int _RaytracingRayMaxLength = Shader.PropertyToID("_RaytracingRayMaxLength");
            public static readonly int _PathTracingEnvironmentIntensity = Shader.PropertyToID("_PathTracingEnvironmentIntensity");
            public static readonly int _PathTracingIncludeEmissive = Shader.PropertyToID("_PathTracingIncludeEmissive");
            public static readonly int _PathTracingIncludeDirectLighting = Shader.PropertyToID("_PathTracingIncludeDirectLighting");
            public static readonly int _PathTracingDebugVisualizeBounce = Shader.PropertyToID("_PathTracingDebugVisualizeBounce");
            public static readonly int _OwenScrambledTexture = Shader.PropertyToID("_OwenScrambledTexture");
            public static readonly int _ScramblingTileXSPP = Shader.PropertyToID("_ScramblingTileXSPP");
            public static readonly int _RankingTileXSPP = Shader.PropertyToID("_RankingTileXSPP");
            public static readonly int _ScramblingTexture = Shader.PropertyToID("_ScramblingTexture");
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

            // Setup ray tracing constant buffer
            passData.rayTracingCB = rayTracingSystem.GetShaderVariablesRaytracingCB(cameraData);

            // Setup dithered texture set for blue noise sampling
            passData.ditheredTextureHandleSet = RuntimeTextureSystem.instance.DitheredTextureSet8SPP().RenderGraphImport(renderGraph);

            // Input textures
            passData.gBuffer0 = resourceData.gBuffer[0];
            passData.gBuffer1 = resourceData.gBuffer[1];
            passData.gBuffer2 = resourceData.gBuffer[2];
            passData.depthTexture = resourceData.cameraDepthTexture;

            // TODO: Get sky texture from environment settings
            // For now, we'll use a default black texture
            passData.skyTexture = TextureHandle.nullHandle;

            // Dispatch size
            passData.dispatchWidth = (uint)cameraData.cameraTargetDescriptor.width;
            passData.dispatchHeight = (uint)cameraData.cameraTargetDescriptor.height;

            // Path tracing parameters from volume settings
            passData.maxBounces = m_MaxBounces;
            passData.samplesPerPixel = m_SamplesPerPixel;
            passData.fireflyClamp = m_FireflyClamp;
            passData.useNVSER = m_UseNVSER;
            passData.frameIndex = m_FrameIndex;
            passData.accumulate = m_AccumulateFrames;
            passData.intensity = giSettings.pathTracingIntensity.value;
            passData.rayLength = giSettings.rayLength.value;
            passData.environmentIntensity = giSettings.environmentIntensity.value;
            passData.includeEmissive = giSettings.includeEmissive.value;
            passData.includeDirectLighting = giSettings.includeDirectLighting.value;
            passData.debugVisualizeBounce = giSettings.debugVisualizeBounce.value;
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

                // Push ray tracing constant buffer
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

                // Set path tracing parameters
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingMaxBounces, data.maxBounces);
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingSamplesPerPixel, data.samplesPerPixel);
                cmd.SetRayTracingFloatParam(data.pathTracingShader, ShaderConstants._PathTracingFireflyClamp, data.fireflyClamp);
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingFrameIndex, data.frameIndex);
                cmd.SetRayTracingFloatParam(data.pathTracingShader, ShaderConstants._UseNVSER, data.useNVSER ? 1.0f : 0.0f);
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingAccumulate, data.accumulate ? 1 : 0);
                cmd.SetRayTracingFloatParam(data.pathTracingShader, ShaderConstants._PathTracingIntensity, data.intensity);
                cmd.SetRayTracingFloatParam(data.pathTracingShader, ShaderConstants._RaytracingRayMaxLength, data.rayLength);
                cmd.SetRayTracingFloatParam(data.pathTracingShader, ShaderConstants._PathTracingEnvironmentIntensity, data.environmentIntensity);
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingIncludeEmissive, data.includeEmissive ? 1 : 0);
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingIncludeDirectLighting, data.includeDirectLighting ? 1 : 0);
                cmd.SetRayTracingIntParam(data.pathTracingShader, ShaderConstants._PathTracingDebugVisualizeBounce, data.debugVisualizeBounce);

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

            // Setup history buffer for temporal accumulation
            var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);
            RTHandle historyRT = null;
            TextureHandle historyTexture = TextureHandle.nullHandle;

            if (m_AccumulateFrames)
            {
                historyRT = ReAllocateHistoryBufferIfNeeded(historyRTSystem);
                historyTexture = renderGraph.ImportTexture(historyRT);
            }

            // Add path tracing pass
            using (var builder = renderGraph.AddComputePass<PassData>(kPassName, out var passData, s_ProfilingSampler))
            {
                InitializePassData(renderGraph, passData, rayTracingSystem, cameraData, resourceData, giSettings);

                passData.outputTexture = outputTexture;
                passData.historyTexture = historyTexture;

                // Use input textures
                passData.ditheredTextureHandleSet.Use(builder);
                builder.UseTexture(passData.gBuffer0);
                builder.UseTexture(passData.gBuffer1);
                builder.UseTexture(passData.gBuffer2);
                builder.UseTexture(passData.depthTexture);

                // Use output texture
                builder.UseTexture(passData.outputTexture, AccessFlags.ReadWrite);

                // Use history texture if available
                if (passData.historyTexture.IsValid())
                {
                    builder.UseTexture(passData.historyTexture, AccessFlags.Read);
                }

                // Use sky texture if available
                if (passData.skyTexture.IsValid())
                {
                    builder.UseTexture(passData.skyTexture);
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc<PassData>(ExecutePathTracing);
            }

            // TODO: Copy output to history buffer for next frame
            // TODO: Apply denoising if enabled
            // TODO: Composite path tracing result with main rendering

            // Increment frame index for temporal sampling
            m_FrameIndex++;
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // Reset frame index when camera changes
        }
    }
}