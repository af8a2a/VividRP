//------------------------------------------------------------------------------
// DLSSRRDenoiser.cs - DLSS Ray Reconstruction Denoiser for VividRP
//------------------------------------------------------------------------------
// Provides DLSS-RR based denoising for path tracing.
// Uses the simplified DLSSRayReconstruction wrapper internally.
//
// Enable with scripting define: DLSS_PLUGIN_INTEGRATE
//------------------------------------------------------------------------------

#if DLSS_PLUGIN_INTEGRATE

using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// DLSS Ray Reconstruction (RR) denoiser for path tracing.
    /// Uses NVIDIA's AI-based denoiser with integrated upscaling.
    /// </summary>
    public class DLSSRRDenoiser : IDisposable
    {
        private DLSSRayReconstruction m_DlssRR;
        private bool m_Initialized;
        private int m_InputWidth;
        private int m_InputHeight;
        private int m_OutputWidth;
        private int m_OutputHeight;

        // Resource preparation
        private ComputeShader m_ResourcePrepCS;
        private int m_ExtractHitDistancesKernel;
        private int m_GenerateSpecularAlbedoKernel;
        private int m_PrepareNormalRoughnessKernel;
        private int m_PrepareRayDirectionsKernel;
        private int m_GenerateRayDirectionsKernel;

        // Internal RTHandle buffers for DLSS-RR
        private RTHandle m_DiffuseHitDistanceRT;
        private RTHandle m_SpecularHitDistanceRT;
        private RTHandle m_SpecularAlbedoRT;
        private RTHandle m_NormalRoughnessRT;
        private RTHandle m_DiffuseRayDirectionRT;
        private RTHandle m_SpecularRayDirectionRT;
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
            public static readonly int _NormalRoughnessOutput = Shader.PropertyToID("_NormalRoughnessOutput");
            public static readonly int _DiffuseRayDirectionInput = Shader.PropertyToID("_DiffuseRayDirectionInput");
            public static readonly int _SpecularRayDirectionInput = Shader.PropertyToID("_SpecularRayDirectionInput");
            public static readonly int _DiffuseRayDirectionOutput = Shader.PropertyToID("_DiffuseRayDirectionOutput");
            public static readonly int _SpecularRayDirectionOutput = Shader.PropertyToID("_SpecularRayDirectionOutput");
            public static readonly int _TextureSize = Shader.PropertyToID("_TextureSize");
            public static readonly int _HitDistanceScale = Shader.PropertyToID("_HitDistanceScale");
            public static readonly int _ViewZScale = Shader.PropertyToID("_ViewZScale");
            public static readonly int _InvViewProjMatrix = Shader.PropertyToID("_InvViewProjMatrix");
            public static readonly int _CameraPosition = Shader.PropertyToID("_CameraPosition");
        }

        /// <summary>
        /// Settings for DLSS-RR denoiser
        /// </summary>
        public struct Settings
        {
            public DLSSQuality quality;
            public bool resetHistory;
            public float preExposure;
            public float exposureScale;
            public float frameTimeDeltaMs;
            public float hitDistanceScale;
            public float sharpness;
            public bool autoExposure;
            public bool isHDR;

            public static Settings Default => new Settings
            {
                quality = DLSSQuality.Balanced,
                resetHistory = false,
                preExposure = 1.0f,
                exposureScale = 1.0f,
                frameTimeDeltaMs = 16.67f,
                hitDistanceScale = 1000.0f,
                sharpness = 0.0f,
                autoExposure = false,
                isHDR = true
            };
        }

        /// <summary>
        /// Input textures for DLSS-RR resource preparation
        /// </summary>
        public struct ResourceInputs
        {
            public RTHandle diffuseRadiance;
            public RTHandle specularRadiance;
            public RTHandle colorInput;
            public RTHandle depth;
            public RTHandle motionVectors;
            public RTHandle gbuffer0;
            public RTHandle gbuffer1;
            public RTHandle gbuffer2;
        }

        /// <summary>
        /// Check if DLSS-RR is available on the current system
        /// </summary>
        public static bool IsSupported => DLSSExtension.Instance?.IsRRSupported ?? false;

        /// <summary>
        /// Create a new DLSS-RR denoiser instance
        /// </summary>
        public DLSSRRDenoiser()
        {
            m_Initialized = false;
            LoadResourcePrepShader();
        }

        private void LoadResourcePrepShader()
        {
            var vividShaders = GraphicsSettings.GetRenderPipelineSettings<VividRuntimeShader>();
            if (vividShaders != null)
            {
                m_ResourcePrepCS = vividShaders.dlssRRResourcePrepCS;
            }

            if (m_ResourcePrepCS != null)
            {
                m_ExtractHitDistancesKernel = m_ResourcePrepCS.FindKernel("CSExtractHitDistances");
                m_GenerateSpecularAlbedoKernel = m_ResourcePrepCS.FindKernel("CSGenerateSpecularAlbedo");
                m_PrepareNormalRoughnessKernel = m_ResourcePrepCS.FindKernel("CSPrepareNormalRoughness");
                m_PrepareRayDirectionsKernel = m_ResourcePrepCS.FindKernel("CSPrepareRayDirections");
                m_GenerateRayDirectionsKernel = m_ResourcePrepCS.FindKernel("CSGenerateRayDirections");
            }
        }

        /// <summary>
        /// Initialize or update the DLSS-RR context for the given resolution
        /// </summary>
        public bool Initialize(int inputWidth, int inputHeight, int outputWidth, int outputHeight,
            DLSSQuality quality, bool isHDR = true, bool autoExposure = false)
        {
            if (!IsSupported)
            {
                Debug.LogError("[DLSSRRDenoiser] DLSS-RR not supported on this system");
                return false;
            }

            // Map user-facing quality to internal NGX value
            var ngxQuality = quality.ToNGXQuality();

            bool needsRecreate = !m_Initialized ||
                                 m_InputWidth != inputWidth ||
                                 m_InputHeight != inputHeight ||
                                 m_OutputWidth != outputWidth ||
                                 m_OutputHeight != outputHeight;

            if (!needsRecreate && m_DlssRR != null)
            {
                m_DlssRR.SetQuality(ngxQuality);
                return true;
            }

            // Dispose existing wrapper
            m_DlssRR?.Dispose();

            // Reallocate internal buffers
            ReallocateInternalBuffers(inputWidth, inputHeight, outputWidth, outputHeight);

            // Create feature flags
            var flags = NVSDK_NGX_DLSS_Feature_Flags.DepthInverted;
            if (isHDR)
                flags |= NVSDK_NGX_DLSS_Feature_Flags.IsHDR;
            if (autoExposure)
                flags |= NVSDK_NGX_DLSS_Feature_Flags.AutoExposure;

            // Create new wrapper
            m_DlssRR = new DLSSRayReconstruction(
                flags,
                ngxQuality,
                DLSSRayReconstruction.DepthType.Hardware,
                DLSSRayReconstruction.RoughnessMode.PackedInNormalsW
            );

            m_InputWidth = inputWidth;
            m_InputHeight = inputHeight;
            m_OutputWidth = outputWidth;
            m_OutputHeight = outputHeight;
            m_Initialized = true;

            return true;
        }

        private void ReallocateInternalBuffers(int inputWidth, int inputHeight, int outputWidth, int outputHeight)
        {
            ReleaseInternalBuffers();

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

            m_SpecularAlbedoRT = RTHandles.Alloc(
                inputWidth, inputHeight,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite: true,
                name: "DLSS_RR_SpecularAlbedo"
            );

            m_NormalRoughnessRT = RTHandles.Alloc(
                inputWidth, inputHeight,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite: true,
                name: "DLSS_RR_NormalRoughness"
            );

            m_DiffuseRayDirectionRT = RTHandles.Alloc(
                inputWidth, inputHeight,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite: true,
                name: "DLSS_RR_DiffuseRayDirection"
            );

            m_SpecularRayDirectionRT = RTHandles.Alloc(
                inputWidth, inputHeight,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite: true,
                name: "DLSS_RR_SpecularRayDirection"
            );

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

            m_NormalRoughnessRT?.Release();
            m_NormalRoughnessRT = null;

            m_DiffuseRayDirectionRT?.Release();
            m_DiffuseRayDirectionRT = null;

            m_SpecularRayDirectionRT?.Release();
            m_SpecularRayDirectionRT = null;

            m_OutputRT?.Release();
            m_OutputRT = null;
        }

        /// <summary>
        /// Execute DLSS-RR denoising with RenderTexture inputs
        /// </summary>
        public bool ExecuteWithRenderTextures(
            CommandBuffer cmd,
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            RenderTexture diffuseAlbedo,
            RenderTexture gbuffer1,
            RenderTexture normalRoughness,
            RenderTexture diffuseRadiance,
            RenderTexture specularRadiance,
            RenderTexture diffuseRayDirection,
            RenderTexture specularRayDirection,
            Vector2 jitterOffset,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            Vector3 cameraPosition,
            Settings settings)
        {
            if (!m_Initialized || m_DlssRR == null)
            {
                Debug.LogError("[DLSSRRDenoiser] Denoiser not initialized");
                return false;
            }

            if (m_ResourcePrepCS == null)
            {
                Debug.LogError("[DLSSRRDenoiser] Resource preparation compute shader not found");
                return false;
            }

            if (colorInput == null || colorOutput == null || depth == null || motionVectors == null)
            {
                Debug.LogError("[DLSSRRDenoiser] Required textures are null");
                return false;
            }

            int width = m_InputWidth;
            int height = m_InputHeight;

            Vector4 textureSize = new Vector4(width, height, 1.0f / width, 1.0f / height);
            int threadGroupsX = (width + 7) / 8;
            int threadGroupsY = (height + 7) / 8;

            cmd.SetComputeVectorParam(m_ResourcePrepCS, ShaderIDs._TextureSize, textureSize);
            cmd.SetComputeFloatParam(m_ResourcePrepCS, ShaderIDs._HitDistanceScale, settings.hitDistanceScale);

            // Extract hit distances
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._DiffuseRadianceInput, diffuseRadiance);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._SpecularRadianceInput, specularRadiance);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._DiffuseHitDistanceOutput, m_DiffuseHitDistanceRT);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_ExtractHitDistancesKernel, ShaderIDs._SpecularHitDistanceOutput, m_SpecularHitDistanceRT);
            cmd.DispatchCompute(m_ResourcePrepCS, m_ExtractHitDistancesKernel, threadGroupsX, threadGroupsY, 1);

            // Prepare normal/roughness
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_PrepareNormalRoughnessKernel, ShaderIDs._GBuffer2, normalRoughness);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_PrepareNormalRoughnessKernel, ShaderIDs._NormalRoughnessOutput, m_NormalRoughnessRT);
            cmd.DispatchCompute(m_ResourcePrepCS, m_PrepareNormalRoughnessKernel, threadGroupsX, threadGroupsY, 1);

            // Generate specular albedo
            Matrix4x4 invViewProjMatrix = (viewToClip * worldToView).inverse;
            cmd.SetComputeMatrixParam(m_ResourcePrepCS, ShaderIDs._InvViewProjMatrix, invViewProjMatrix);
            cmd.SetComputeVectorParam(m_ResourcePrepCS, ShaderIDs._CameraPosition, cameraPosition);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._GBuffer0, diffuseAlbedo);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._GBuffer1, gbuffer1);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._GBuffer2, normalRoughness);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._DepthTexture, depth);
            cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, ShaderIDs._SpecularAlbedoOutput, m_SpecularAlbedoRT);
            cmd.DispatchCompute(m_ResourcePrepCS, m_GenerateSpecularAlbedoKernel, threadGroupsX, threadGroupsY, 1);

            // Prepare ray directions
            if (diffuseRayDirection != null && specularRayDirection != null)
            {
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_PrepareRayDirectionsKernel, ShaderIDs._DiffuseRayDirectionInput, diffuseRayDirection);
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_PrepareRayDirectionsKernel, ShaderIDs._SpecularRayDirectionInput, specularRayDirection);
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_PrepareRayDirectionsKernel, ShaderIDs._DiffuseRayDirectionOutput, m_DiffuseRayDirectionRT);
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_PrepareRayDirectionsKernel, ShaderIDs._SpecularRayDirectionOutput, m_SpecularRayDirectionRT);
                cmd.DispatchCompute(m_ResourcePrepCS, m_PrepareRayDirectionsKernel, threadGroupsX, threadGroupsY, 1);
            }
            else
            {
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateRayDirectionsKernel, ShaderIDs._GBuffer2, normalRoughness);
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateRayDirectionsKernel, ShaderIDs._DepthTexture, depth);
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateRayDirectionsKernel, ShaderIDs._DiffuseRayDirectionOutput, m_DiffuseRayDirectionRT);
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateRayDirectionsKernel, ShaderIDs._SpecularRayDirectionOutput, m_SpecularRayDirectionRT);
                cmd.DispatchCompute(m_ResourcePrepCS, m_GenerateRayDirectionsKernel, threadGroupsX, threadGroupsY, 1);
            }

            // Build GBuffer and ray inputs for the wrapper
            var gbuffer = new DLSSRRGBuffer
            {
                DiffuseAlbedo = diffuseAlbedo,
                SpecularAlbedo = m_SpecularAlbedoRT?.rt,
                Normals = m_NormalRoughnessRT?.rt,
                Roughness = null // Packed in normals.w
            };

            var rayInputs = new DLSSRRRayInputs
            {
                DiffuseRayDirection = m_DiffuseRayDirectionRT?.rt,
                DiffuseHitDistance = m_DiffuseHitDistanceRT?.rt,
                SpecularRayDirection = m_SpecularRayDirectionRT?.rt,
                SpecularHitDistance = m_SpecularHitDistanceRT?.rt
            };

            // Execute via wrapper
            return m_DlssRR.Render(
                cmd,
                colorInput,
                colorOutput,
                depth,
                motionVectors,
                gbuffer,
                rayInputs,
                worldToView,
                viewToClip,
                jitterOffset.x * m_InputWidth,  // Convert to pixel space
                jitterOffset.y * m_InputHeight,
                -(float)m_InputWidth,  // Unity convention
                -(float)m_InputHeight,
                settings.resetHistory,
                settings.frameTimeDeltaMs
            );
        }

        /// <summary>
        /// Execute DLSS-RR with pre-prepared inputs (skips resource preparation).
        /// Use this when raytracing GBuffer provides DLSS-RR native format directly.
        /// </summary>
        /// <param name="cmd">Command buffer for rendering commands</param>
        /// <param name="colorInput">Input color texture</param>
        /// <param name="colorOutput">Output color texture</param>
        /// <param name="depth">Depth texture</param>
        /// <param name="motionVectors">Motion vectors texture</param>
        /// <param name="diffuseAlbedo">Pre-computed diffuse albedo (albedo * (1-metallic))</param>
        /// <param name="specularAlbedo">Pre-computed specular albedo (EnvBRDFApprox2)</param>
        /// <param name="normalRoughness">World normal + sqrt(alphaRoughness) in alpha</param>
        /// <param name="diffuseHitDistance">Diffuse hit distance (optional)</param>
        /// <param name="specularHitDistance">Specular hit distance (optional)</param>
        /// <param name="jitterOffset">Camera jitter offset in normalized coordinates</param>
        /// <param name="worldToView">World to view matrix</param>
        /// <param name="viewToClip">View to clip matrix</param>
        /// <param name="settings">Denoiser settings</param>
        /// <returns>True on success</returns>
        public bool ExecuteWithPreparedInputs(
            CommandBuffer cmd,
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            RenderTexture diffuseAlbedo,
            RenderTexture specularAlbedo,
            RenderTexture normalRoughness,
            RenderTexture diffuseHitDistance,
            RenderTexture specularHitDistance,
            Vector2 jitterOffset,
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            Settings settings)
        {
            if (!m_Initialized || m_DlssRR == null)
            {
                Debug.LogError("[DLSSRRDenoiser] Denoiser not initialized");
                return false;
            }

            if (colorInput == null || colorOutput == null || depth == null || motionVectors == null)
            {
                Debug.LogError("[DLSSRRDenoiser] Required textures are null");
                return false;
            }

            // Generate ray directions from normal/roughness if not provided
            if (m_ResourcePrepCS != null)
            {
                int width = m_InputWidth;
                int height = m_InputHeight;
                Vector4 textureSize = new Vector4(width, height, 1.0f / width, 1.0f / height);
                int threadGroupsX = (width + 7) / 8;
                int threadGroupsY = (height + 7) / 8;

                cmd.SetComputeVectorParam(m_ResourcePrepCS, ShaderIDs._TextureSize, textureSize);
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateRayDirectionsKernel, ShaderIDs._GBuffer2, normalRoughness);
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateRayDirectionsKernel, ShaderIDs._DepthTexture, depth);
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateRayDirectionsKernel, ShaderIDs._DiffuseRayDirectionOutput, m_DiffuseRayDirectionRT);
                cmd.SetComputeTextureParam(m_ResourcePrepCS, m_GenerateRayDirectionsKernel, ShaderIDs._SpecularRayDirectionOutput, m_SpecularRayDirectionRT);
                cmd.DispatchCompute(m_ResourcePrepCS, m_GenerateRayDirectionsKernel, threadGroupsX, threadGroupsY, 1);
            }

            // Build GBuffer from pre-prepared inputs (no resource prep needed)
            var gbuffer = new DLSSRRGBuffer
            {
                DiffuseAlbedo = diffuseAlbedo,
                SpecularAlbedo = specularAlbedo,
                Normals = normalRoughness,
                Roughness = null  // Packed in normals.w
            };

            var rayInputs = new DLSSRRRayInputs
            {
                DiffuseRayDirection = m_DiffuseRayDirectionRT?.rt,
                DiffuseHitDistance = diffuseHitDistance,
                SpecularRayDirection = m_SpecularRayDirectionRT?.rt,
                SpecularHitDistance = specularHitDistance
            };

            // Execute via wrapper
            return m_DlssRR.Render(
                cmd,
                colorInput,
                colorOutput,
                depth,
                motionVectors,
                gbuffer,
                rayInputs,
                worldToView,
                viewToClip,
                jitterOffset.x * m_InputWidth,   // Convert to pixel space
                jitterOffset.y * m_InputHeight,
                -(float)m_InputWidth,   // Unity convention
                -(float)m_InputHeight,
                settings.resetHistory,
                settings.frameTimeDeltaMs
            );
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
            m_DlssRR?.Dispose();
            m_DlssRR = null;

            ReleaseInternalBuffers();
            m_Initialized = false;
        }

        /// <summary>
        /// Check if the denoiser is initialized and ready
        /// </summary>
        public bool IsReady => m_Initialized && m_DlssRR != null;

        /// <summary>
        /// Current input resolution
        /// </summary>
        public Vector2Int InputResolution => new Vector2Int(m_InputWidth, m_InputHeight);

        /// <summary>
        /// Current output resolution
        /// </summary>
        public Vector2Int OutputResolution => new Vector2Int(m_OutputWidth, m_OutputHeight);
    }
}

#endif
