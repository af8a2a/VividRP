//------------------------------------------------------------------------------
// DLSSRRDenoiser.cs - DLSS Ray Reconstruction Denoiser for VividRP
//------------------------------------------------------------------------------
// Provides DLSS-RR based denoising for path tracing.
//
// Enable with scripting define: DLSS_PLUGIN_INTEGRATE
//------------------------------------------------------------------------------

#if DLSS_PLUGIN_INTEGRATE

using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using DLSS;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// DLSS Ray Reconstruction (RR) denoiser for path tracing.
    /// Uses NVIDIA's AI-based denoiser with integrated upscaling.
    /// </summary>
    public class DLSSRRDenoiser : IDisposable
    {
        private bool m_Initialized;
        private uint m_ViewId;
        private DLSSDimensions m_InputResolution;
        private DLSSDimensions m_OutputResolution;
        private DLSSQuality m_Quality;
        private bool m_ContextCreated;

        // Resource preparation
        private ComputeShader m_ResourcePrepCS;
        private int m_ExtractHitDistancesKernel;
        private int m_GenerateSpecularAlbedoKernel;

        // Internal RTHandle buffers for DLSS-RR
        private RTHandle m_DiffuseHitDistanceRT;
        private RTHandle m_SpecularHitDistanceRT;
        private RTHandle m_SpecularAlbedoRT;
        private RTHandle m_OutputRT;

        // Shader property IDs
        private static class ShaderIDs
        {
            public static readonly int _DiffuseRadianceInput = Shader.PropertyToID("_DiffuseRadianceInput");
            public static readonly int _SpecularRadianceInput = Shader.PropertyToID("_SpecularRadianceInput");
            public static readonly int _GBuffer0 = Shader.PropertyToID("_GBuffer0");
            public static readonly int _GBuffer1 = Shader.PropertyToID("_GBuffer1");
            public static readonly int _GBuffer2 = Shader.PropertyToID("_GBuffer2");
            public static readonly int _DepthTexture = Shader.PropertyToID("_DepthTexture");
            public static readonly int _DiffuseHitDistanceOutput = Shader.PropertyToID("_DiffuseHitDistanceOutput");
            public static readonly int _SpecularHitDistanceOutput = Shader.PropertyToID("_SpecularHitDistanceOutput");
            public static readonly int _SpecularAlbedoOutput = Shader.PropertyToID("_SpecularAlbedoOutput");
            public static readonly int _TextureSize = Shader.PropertyToID("_TextureSize");
            public static readonly int _HitDistanceScale = Shader.PropertyToID("_HitDistanceScale");
            public static readonly int _ViewZScale = Shader.PropertyToID("_ViewZScale");
        }

        /// <summary>
        /// Settings for DLSS-RR denoiser
        /// </summary>
        public struct Settings
        {
            public DLSSQuality quality;
            public bool resetHistory;
            public float preExposure;
            public float frameTimeDeltaMs;
            public float hitDistanceScale;  // Scale for denormalizing hit distance

            public static Settings Default => new Settings
            {
                quality = DLSSQuality.Balanced,
                resetHistory = false,
                preExposure = 1.0f,
                frameTimeDeltaMs = 16.67f,  // ~60fps
                hitDistanceScale = 1000.0f   // Default scene scale
            };
        }

        /// <summary>
        /// Input textures for DLSS-RR resource preparation
        /// </summary>
        public struct ResourceInputs
        {
            public RTHandle diffuseRadiance;     // RGB = radiance, A = normalized hit distance
            public RTHandle specularRadiance;    // RGB = radiance, A = normalized hit distance
            public RTHandle colorInput;          // Combined noisy color
            public RTHandle depth;
            public RTHandle motionVectors;
            public RTHandle gbuffer0;            // Diffuse albedo
            public RTHandle gbuffer1;            // Specular + metallic
            public RTHandle gbuffer2;            // Normal + roughness
        }

        /// <summary>
        /// Check if DLSS-RR is available on the current system
        /// </summary>
        public static bool IsSupported
        {
            get
            {
                if (!DLSSManager.IsInitialized)
                    return false;

                if (DLSSManager.TryGetCapabilities(out var caps))
                    return caps.IsRRAvailable;

                return false;
            }
        }

        /// <summary>
        /// Create a new DLSS-RR denoiser instance
        /// </summary>
        public DLSSRRDenoiser(uint viewId)
        {
            m_ViewId = viewId;
            m_Initialized = false;
            m_ContextCreated = false;

            // Try to load the resource preparation compute shader
            LoadResourcePrepShader();
        }

        private void LoadResourcePrepShader()
        {
            // Load compute shader from VividRuntimeShader settings
            var vividShaders = GraphicsSettings.GetRenderPipelineSettings<VividRuntimeShader>();
            if (vividShaders != null)
            {
                m_ResourcePrepCS = vividShaders.dlssRRResourcePrepCS;
            }

            if (m_ResourcePrepCS != null)
            {
                m_ExtractHitDistancesKernel = m_ResourcePrepCS.FindKernel("CSExtractHitDistances");
                m_GenerateSpecularAlbedoKernel = m_ResourcePrepCS.FindKernel("CSGenerateSpecularAlbedo");
            }
        }

        /// <summary>
        /// Initialize or update the DLSS-RR context for the given resolution
        /// </summary>
        public bool Initialize(int inputWidth, int inputHeight, int outputWidth, int outputHeight, DLSSQuality quality)
        {
            if (!DLSSManager.IsInitialized)
            {
                Debug.LogError("[DLSSRRDenoiser] DLSS not initialized. Ensure DLSSExtension is initialized.");
                return false;
            }

            var newInputRes = new DLSSDimensions((uint)inputWidth, (uint)inputHeight);
            var newOutputRes = new DLSSDimensions((uint)outputWidth, (uint)outputHeight);

            // Sync m_ContextCreated with native state (handles domain reload, etc.)
            bool nativeContextExists = DLSSManager.HasContext(m_ViewId);
            if (nativeContextExists && !m_ContextCreated)
            {
                // Native context exists but we don't track it - sync state
                m_ContextCreated = true;
            }

            // Check if we need to recreate the context
            bool needsRecreate = !m_ContextCreated ||
                                 m_InputResolution.width != newInputRes.width ||
                                 m_InputResolution.height != newInputRes.height ||
                                 m_OutputResolution.width != newOutputRes.width ||
                                 m_OutputResolution.height != newOutputRes.height ||
                                 m_Quality != quality;

            if (!needsRecreate)
                return true;

            // Destroy existing context if any (check native state, not just C# flag)
            if (DLSSManager.HasContext(m_ViewId))
            {
                DLSSManager.DestroyContext(m_ViewId);
            }
            m_ContextCreated = false;

            // Reallocate internal buffers
            ReallocateInternalBuffers(inputWidth, inputHeight, outputWidth, outputHeight);

            // Create new context using DLSSManager
            var flags = DLSSFeatureFlags.DepthInverted  // Unity uses reversed-Z
                      | DLSSFeatureFlags.MVLowRes        // Motion vectors at render resolution
                      | DLSSFeatureFlags.IsHDR;          // HDR input

            if (!DLSSManager.CreateRRContext(
                m_ViewId,
                quality,
                newInputRes.width,
                newInputRes.height,
                newOutputRes.width,
                newOutputRes.height,
                flags,
                DLSSDepthType.Hardware,
                DLSSRoughnessMode.Unpacked))
            {
                Debug.LogError("[DLSSRRDenoiser] Failed to create DLSS-RR context");
                return false;
            }

            m_InputResolution = newInputRes;
            m_OutputResolution = newOutputRes;
            m_Quality = quality;
            m_ContextCreated = true;
            m_Initialized = true;

            return true;
        }

        private void ReallocateInternalBuffers(int inputWidth, int inputHeight, int outputWidth, int outputHeight)
        {
            // Release existing buffers
            ReleaseInternalBuffers();

            // Allocate hit distance buffers (R16F for efficiency)
            m_DiffuseHitDistanceRT = RTHandles.Alloc(
                inputWidth, inputHeight,
                colorFormat: GraphicsFormat.R16_SFloat,
                enableRandomWrite: true,
                name: "DLSS_RR_DiffuseHitDistance"
            );

            m_SpecularHitDistanceRT = RTHandles.Alloc(
                inputWidth, inputHeight,
                colorFormat: GraphicsFormat.R16_SFloat,
                enableRandomWrite: true,
                name: "DLSS_RR_SpecularHitDistance"
            );

            // Allocate specular albedo buffer (RGBA16F)
            m_SpecularAlbedoRT = RTHandles.Alloc(
                inputWidth, inputHeight,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite: true,
                name: "DLSS_RR_SpecularAlbedo"
            );

            // Allocate output buffer
            m_OutputRT = RTHandles.Alloc(
                outputWidth, outputHeight,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite: true,
                name: "DLSS_RR_Output"
            );
        }

        private void ReleaseInternalBuffers()
        {
            m_DiffuseHitDistanceRT?.Release();
            m_DiffuseHitDistanceRT = null;

            m_SpecularHitDistanceRT?.Release();
            m_SpecularHitDistanceRT = null;

            m_SpecularAlbedoRT?.Release();
            m_SpecularAlbedoRT = null;

            m_OutputRT?.Release();
            m_OutputRT = null;
        }

        /// <summary>
        /// Prepare resources for DLSS-RR (extract hit distances, generate specular albedo)
        /// </summary>
        public void PrepareResources(CommandBuffer cmd, ResourceInputs inputs, float hitDistanceScale)
        {
            if (m_ResourcePrepCS == null)
            {
                Debug.LogWarning("[DLSSRRDenoiser] Resource preparation compute shader not found");
                return;
            }

            int width = (int)m_InputResolution.width;
            int height = (int)m_InputResolution.height;

            Vector4 textureSize = new Vector4(width, height, 1.0f / width, 1.0f / height);

            // Extract hit distances
            cmd.SetComputeVectorParam(m_ResourcePrepCS, ShaderIDs._TextureSize, textureSize);
            cmd.SetComputeFloatParam(m_ResourcePrepCS, ShaderIDs._HitDistanceScale, hitDistanceScale);

            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._DiffuseRadianceInput, inputs.diffuseRadiance);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._SpecularRadianceInput, inputs.specularRadiance);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._DiffuseHitDistanceOutput, m_DiffuseHitDistanceRT);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._SpecularHitDistanceOutput, m_SpecularHitDistanceRT);

            int threadGroupsX = (width + 7) / 8;
            int threadGroupsY = (height + 7) / 8;
            cmd.DispatchCompute(m_ResourcePrepCS, m_ExtractHitDistancesKernel, threadGroupsX, threadGroupsY, 1);

            // Generate specular albedo
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._GBuffer0, inputs.gbuffer0);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._GBuffer1, inputs.gbuffer1);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._GBuffer2, inputs.gbuffer2);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._SpecularAlbedoOutput, m_SpecularAlbedoRT);

            cmd.DispatchCompute(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, threadGroupsX, threadGroupsY, 1);
        }

        /// <summary>
        /// Get optimal render resolution for DLSS-RR
        /// </summary>
        public static bool TryGetOptimalRenderSize(DLSSQuality quality, int outputWidth, int outputHeight, out Vector2Int renderSize)
        {
            renderSize = new Vector2Int(outputWidth, outputHeight);

            if (!DLSSManager.IsInitialized)
                return false;

            if (DLSSManager.TryGetOptimalSettings(
                DLSSMode.RayReconstruction,
                quality,
                (uint)outputWidth,
                (uint)outputHeight,
                out var settings))
            {
                renderSize = settings.OptimalRenderSize;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Execute DLSS-RR denoising with prepared resources
        /// </summary>
        public bool Execute(
            CommandBuffer cmd,
            ResourceInputs inputs,
            Vector2 jitterOffset,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            Settings settings)
        {
            if (!m_Initialized || !m_ContextCreated)
            {
                Debug.LogError("[DLSSRRDenoiser] Denoiser not initialized");
                return false;
            }

            // Prepare resources (extract hit distances, generate specular albedo)
            PrepareResources(cmd, inputs, settings.hitDistanceScale);

            // Execute DLSS-RR
            return ExecuteInternal(
                cmd,
                inputs.colorInput?.rt,
                m_OutputRT?.rt,
                inputs.depth?.rt,
                inputs.motionVectors?.rt,
                inputs.gbuffer0?.rt,          // Diffuse albedo
                m_SpecularAlbedoRT?.rt,       // Generated specular albedo
                inputs.gbuffer2?.rt,          // Normal + roughness
                null,                          // Roughness is in gbuffer2.a
                m_DiffuseHitDistanceRT?.rt,   // Extracted diffuse hit distance
                m_SpecularHitDistanceRT?.rt,  // Extracted specular hit distance
                jitterOffset,
                worldToView,
                viewToClip,
                settings
            );
        }

        /// <summary>
        /// Execute DLSS-RR denoising with RenderTexture inputs (RenderGraph compatible)
        /// This method handles resource preparation internally and is suitable for use with
        /// context.resources.GetTexture() in RenderGraph unsafe passes.
        /// </summary>
        public bool ExecuteWithRenderTextures(
            CommandBuffer cmd,
            RenderTexture colorInput,
            RenderTexture depth,
            RenderTexture motionVectors,
            RenderTexture diffuseAlbedo,
            RenderTexture gbuffer1,
            RenderTexture normalRoughness,
            RenderTexture diffuseRadiance,  // RGB = radiance, A = hit distance
            RenderTexture specularRadiance, // RGB = radiance, A = hit distance
            Vector2 jitterOffset,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            Settings settings)
        {
            if (!m_Initialized || !m_ContextCreated)
            {
                Debug.LogError("[DLSSRRDenoiser] Denoiser not initialized");
                return false;
            }

            if (m_ResourcePrepCS == null)
            {
                Debug.LogError("[DLSSRRDenoiser] Resource preparation compute shader not found");
                return false;
            }

            int width = (int)m_InputResolution.width;
            int height = (int)m_InputResolution.height;

            Vector4 textureSize = new Vector4(width, height, 1.0f / width, 1.0f / height);
            int threadGroupsX = (width + 7) / 8;
            int threadGroupsY = (height + 7) / 8;

            // Extract hit distances from diffuse/specular radiance alpha channels
            cmd.SetComputeVectorParam(m_ResourcePrepCS, ShaderIDs._TextureSize, textureSize);
            cmd.SetComputeFloatParam(m_ResourcePrepCS, ShaderIDs._HitDistanceScale, settings.hitDistanceScale);

            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._DiffuseRadianceInput, diffuseRadiance);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._SpecularRadianceInput, specularRadiance);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._DiffuseHitDistanceOutput, m_DiffuseHitDistanceRT);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._SpecularHitDistanceOutput, m_SpecularHitDistanceRT);

            cmd.DispatchCompute(m_ResourcePrepCS, m_ExtractHitDistancesKernel, threadGroupsX, threadGroupsY, 1);

            // Generate specular albedo using EnvBRDFApprox2
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._GBuffer0, diffuseAlbedo);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._GBuffer1, gbuffer1);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._GBuffer2, normalRoughness);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._SpecularAlbedoOutput, m_SpecularAlbedoRT);

            cmd.DispatchCompute(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, threadGroupsX, threadGroupsY, 1);

            // Execute DLSS-RR with prepared resources
            return ExecuteInternal(
                cmd,
                colorInput,
                m_OutputRT?.rt,
                depth,
                motionVectors,
                diffuseAlbedo,          // Diffuse albedo
                m_SpecularAlbedoRT?.rt, // Generated specular albedo
                normalRoughness,        // Normal + roughness
                null,                   // Roughness is in normalRoughness.a
                m_DiffuseHitDistanceRT?.rt,   // Extracted diffuse hit distance
                m_SpecularHitDistanceRT?.rt,  // Extracted specular hit distance
                jitterOffset,
                worldToView,
                viewToClip,
                settings
            );
        }

        /// <summary>
        /// Execute DLSS-RR denoising with RTHandle inputs
        /// </summary>
        public bool Execute(
            CommandBuffer cmd,
            RTHandle colorInput,
            RTHandle colorOutput,
            RTHandle depth,
            RTHandle motionVectors,
            RTHandle diffuseAlbedo,
            RTHandle specularAlbedo,
            RTHandle normals,
            RTHandle roughness,
            RTHandle diffuseHitDistance,
            RTHandle specularHitDistance,
            Vector2 jitterOffset,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            Settings settings)
        {
            return ExecuteInternal(cmd,
                colorInput?.rt, colorOutput?.rt,
                depth?.rt, motionVectors?.rt,
                diffuseAlbedo?.rt, specularAlbedo?.rt, normals?.rt, roughness?.rt,
                diffuseHitDistance?.rt, specularHitDistance?.rt,
                jitterOffset, worldToView, viewToClip, settings);
        }

        /// <summary>
        /// Execute DLSS-RR denoising with RenderTexture inputs (RenderGraph compatible)
        /// </summary>
        private bool ExecuteInternal(
            CommandBuffer cmd,
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            RenderTexture diffuseAlbedo,
            RenderTexture specularAlbedo,
            RenderTexture normals,
            RenderTexture roughness,
            RenderTexture diffuseHitDistance,
            RenderTexture specularHitDistance,
            Vector2 jitterOffset,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            Settings settings)
        {
            if (!m_Initialized || !m_ContextCreated)
            {
                Debug.LogError("[DLSSRRDenoiser] Denoiser not initialized");
                return false;
            }

            if (colorInput == null || colorOutput == null || depth == null || motionVectors == null)
            {
                Debug.LogError("[DLSSRRDenoiser] Required textures are null");
                return false;
            }

            var executeParams = new DLSSExecuteParams
            {
                mode = DLSSMode.RayReconstruction,

                textures = new DLSSCommonTextures
                {
                    colorInput = colorInput.GetNativeTexturePtr(),
                    colorOutput = colorOutput.GetNativeTexturePtr(),
                    depth = depth.GetNativeTexturePtr(),
                    motionVectors = motionVectors.GetNativeTexturePtr()
                },

                common = new DLSSCommonParams
                {
                    jitterOffsetX = jitterOffset.x,
                    jitterOffsetY = jitterOffset.y,
                    mvScaleX = m_InputResolution.width,
                    mvScaleY = m_InputResolution.height,
                    renderSubrectDimensions = m_InputResolution,
                    reset = settings.resetHistory ? (byte)1 : (byte)0,
                    preExposure = settings.preExposure,
                    exposureScale = 1.0f
                },

                rrParams = new DLSSRRParams
                {
                    gbuffer = new DLSSRRGBufferTextures
                    {
                        diffuseAlbedo = diffuseAlbedo != null ? diffuseAlbedo.GetNativeTexturePtr() : IntPtr.Zero,
                        specularAlbedo = specularAlbedo != null ? specularAlbedo.GetNativeTexturePtr() : IntPtr.Zero,
                        normals = normals != null ? normals.GetNativeTexturePtr() : IntPtr.Zero,
                        roughness = roughness != null ? roughness.GetNativeTexturePtr() : IntPtr.Zero
                    },

                    rays = new DLSSRRRayTextures
                    {
                        diffuseHitDistance = diffuseHitDistance != null ? diffuseHitDistance.GetNativeTexturePtr() : IntPtr.Zero,
                        specularHitDistance = specularHitDistance != null ? specularHitDistance.GetNativeTexturePtr() : IntPtr.Zero,
                        diffuseRayDirection = IntPtr.Zero,
                        specularRayDirection = IntPtr.Zero
                    },

                    worldToViewMatrix = worldToView,
                    viewToClipMatrix = viewToClip,
                    frameTimeDeltaMs = settings.frameTimeDeltaMs
                }
            };

            // Use DLSSManager to execute
            if (!DLSSManager.Execute(m_ViewId, ref executeParams))
            {
                Debug.LogError("[DLSSRRDenoiser] DLSS-RR execute failed");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get the internal output RTHandle
        /// </summary>
        public RTHandle OutputRT => m_OutputRT;

        /// <summary>
        /// Dispose of the denoiser and release resources
        /// </summary>
        public void Dispose()
        {
            if (m_ContextCreated)
            {
                DLSSManager.DestroyContext(m_ViewId);
                m_ContextCreated = false;
            }

            ReleaseInternalBuffers();
            m_Initialized = false;
        }

        /// <summary>
        /// Check if the denoiser is initialized and ready
        /// </summary>
        public bool IsReady => m_Initialized && m_ContextCreated;

        /// <summary>
        /// Current view ID
        /// </summary>
        public uint ViewId => m_ViewId;

        /// <summary>
        /// Current input resolution
        /// </summary>
        public Vector2Int InputResolution => new Vector2Int((int)m_InputResolution.width, (int)m_InputResolution.height);

        /// <summary>
        /// Current output resolution
        /// </summary>
        public Vector2Int OutputResolution => new Vector2Int((int)m_OutputResolution.width, (int)m_OutputResolution.height);
    }
}

#endif
