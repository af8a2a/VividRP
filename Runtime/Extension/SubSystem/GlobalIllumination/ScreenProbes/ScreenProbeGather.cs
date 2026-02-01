using System;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Screen probe irradiance format
    /// Must match shader defines
    /// </summary>
    public enum ScreenProbeIrradianceFormat
    {
        /// <summary>Spherical Harmonics L2 (9 coefficients)</summary>
        SH2 = 0,
        /// <summary>Octahedral mapping (direct storage)</summary>
        Octahedral = 1
    }

    /// <summary>
    /// Screen probe parameters for gathering indirect lighting
    /// </summary>
    internal struct ScreenProbeParameters
    {
        // Probe configuration
        public uint DownsampleFactor;           // Screen space downsampling (4 or 8)
        public uint TracingOctahedronResolution; // Ray tracing resolution (4, 6, 8)
        public uint GatherOctahedronResolution;  // Gather resolution (6, 8)

        // Screen dimensions
        public int2 ScreenProbeViewSize;         // Probe grid size
        public int2 ScreenProbeAtlasSize;        // Atlas size for all probes

        // Quality settings
        public float MaxRayDistance;             // Maximum ray trace distance
        public float NearFieldMaxDistance;       // Distance to switch to far-field cache
        public bool UseImportanceSampling;       // BRDF-based importance sampling
        public bool UseRadianceCacheFallback;    // Use surface cache for far-field

        // Filtering
        public float TemporalFilterStrength;     // Temporal accumulation (0-1)
        public float SpatialFilterRadius;        // Spatial filter radius in pixels
        public uint SpatialFilterSamples;        // Number of spatial samples

        // Rejection thresholds
        public float DepthRejectionThreshold;    // Depth difference threshold
        public float NormalRejectionThreshold;   // Normal difference threshold
        public bool EnableVarianceClamping;      // Variance-based history clamping
    }

    /// <summary>
    /// Screen probe gather pass data
    /// </summary>
    internal class ScreenProbeGatherPassData
    {
        // Compute shaders
        public ComputeShader ProbeTracingShader;
        public ComputeShader ProbeFilteringShader;
        public ComputeShader ProbeUpsamplingShader;

        // Kernels
        public int TracingKernel;
        public int TemporalFilterKernel;
        public int SpatialFilterKernel;
        public int UpsamplingKernel;

        // Thread group sizes
        public uint3 TracingThreadGroupSize;
        public uint3 FilteringThreadGroupSize;
        public uint3 UpsamplingThreadGroupSize;

        // Input textures
        public TextureHandle DepthTexture;
        public TextureHandle NormalTexture;
        public TextureHandle MotionVectorTexture;
        public TextureHandle RoughnessTexture;

        // Probe textures
        public TextureHandle ProbeRadianceAtlas;
        public TextureHandle ProbeHitDistanceAtlas;
        public TextureHandle ProbeDepthAtlas;

        // History textures
        public TextureHandle PrevProbeRadianceAtlas;
        public TextureHandle PrevProbeDepthAtlas;

        // Output
        public TextureHandle FilteredRadiance;
        public TextureHandle UpsampledIrradiance;

        // Parameters
        public ScreenProbeParameters Parameters;
        public int2 ScreenSize;
        public int2 ProbeGridSize;
        public Matrix4x4 ViewMatrix;
        public Matrix4x4 ProjectionMatrix;
        public Matrix4x4 ViewProjectionMatrix;
        public Matrix4x4 InvViewProjectionMatrix;
        public Matrix4x4 PrevViewProjectionMatrix;
        public uint FrameIndex;

        // Surface cache integration
        public bool UseSurfaceCacheFallback;
        public GraphicsBuffer SurfaceCacheCellPatchIndices;
        public GraphicsBuffer SurfaceCachePatchIrradiances;
        public GraphicsBuffer SurfaceCacheCascadeOffsets;
        public uint SurfaceCacheGridResolution;
        public uint SurfaceCacheCascadeCount;
        public float SurfaceCacheVoxelMinSize;
        public Vector3 SurfaceCacheVolumeCenter;
    }

    /// <summary>
    /// Screen probe resource set
    /// </summary>
    internal class ScreenProbeResourceSet : IDisposable
    {
        public ComputeShader ProbeTracingShader;
        public ComputeShader ProbeFilteringShader;
        public ComputeShader ProbeUpsamplingShader;

        public int TracingKernel;
        public int TemporalFilterKernel;
        public int SpatialFilterKernel;
        public int UpsamplingKernel;

        public uint3 TracingThreadGroupSize;
        public uint3 FilteringThreadGroupSize;
        public uint3 UpsamplingThreadGroupSize;

        public bool LoadFromRenderPipelineResources()
        {
            var resources = GraphicsSettings.GetRenderPipelineSettings<ScreenProbeRenderPipelineResourceSet>();
            if (resources == null)
            {
                Debug.LogError("ScreenProbeRenderPipelineResourceSet not found in Graphics Settings");
                return false;
            }

            ProbeTracingShader = resources.probeTracingShader;
            ProbeFilteringShader = resources.probeFilteringShader;
            ProbeUpsamplingShader = resources.probeUpsamplingShader;

            if (ProbeTracingShader == null)
            {
                Debug.LogError("Screen Probe Tracing Shader is null. Please assign it in Graphics Settings.");
                return false;
            }

            if (ProbeFilteringShader == null)
            {
                Debug.LogError("Screen Probe Filtering Shader is null. Please assign it in Graphics Settings.");
                return false;
            }

            if (ProbeUpsamplingShader == null)
            {
                Debug.LogError("Screen Probe Upsampling Shader is null. Please assign it in Graphics Settings.");
                return false;
            }

            // Verify shaders have kernels (check for compilation errors)
            if (!ProbeTracingShader.HasKernel("TraceScreenProbes"))
            {
                Debug.LogError("ProbeTracingShader does not have kernel 'TraceScreenProbes'. " +
                    "The shader may have compilation errors. Check the Console for shader errors.");
                return false;
            }

            if (!ProbeFilteringShader.HasKernel("TemporalFilter"))
            {
                Debug.LogError("ProbeFilteringShader does not have kernel 'TemporalFilter'. " +
                    "The shader may have compilation errors. Check the Console for shader errors.");
                return false;
            }

            if (!ProbeFilteringShader.HasKernel("SpatialFilter"))
            {
                Debug.LogError("ProbeFilteringShader does not have kernel 'SpatialFilter'. " +
                    "The shader may have compilation errors. Check the Console for shader errors.");
                return false;
            }

            if (!ProbeUpsamplingShader.HasKernel("UpsampleToScreen"))
            {
                Debug.LogError("ProbeUpsamplingShader does not have kernel 'UpsampleToScreen'. " +
                    "The shader may have compilation errors. Check the Console for shader errors.");
                return false;
            }

            // Find kernels
            TracingKernel = ProbeTracingShader.FindKernel("TraceScreenProbes");
            if (TracingKernel < 0)
            {
                Debug.LogError("Kernel 'TraceScreenProbes' not found in ProbeTracingShader");
                return false;
            }

            TemporalFilterKernel = ProbeFilteringShader.FindKernel("TemporalFilter");
            if (TemporalFilterKernel < 0)
            {
                Debug.LogError("Kernel 'TemporalFilter' not found in ProbeFilteringShader");
                return false;
            }

            SpatialFilterKernel = ProbeFilteringShader.FindKernel("SpatialFilter");
            if (SpatialFilterKernel < 0)
            {
                Debug.LogError("Kernel 'SpatialFilter' not found in ProbeFilteringShader");
                return false;
            }

            UpsamplingKernel = ProbeUpsamplingShader.FindKernel("UpsampleToScreen");
            if (UpsamplingKernel < 0)
            {
                Debug.LogError("Kernel 'UpsampleToScreen' not found in ProbeUpsamplingShader");
                return false;
            }

            // Get thread group sizes with error handling
            try
            {
                ProbeTracingShader.GetKernelThreadGroupSizes(TracingKernel,
                    out TracingThreadGroupSize.x, out TracingThreadGroupSize.y, out TracingThreadGroupSize.z);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to get thread group sizes for ProbeTracingShader kernel 'TraceScreenProbes' (index {TracingKernel}). " +
                    $"The shader may have compilation errors. Check the Console for shader compilation errors. Error: {e.Message}");
                return false;
            }

            try
            {
                ProbeFilteringShader.GetKernelThreadGroupSizes(TemporalFilterKernel,
                    out FilteringThreadGroupSize.x, out FilteringThreadGroupSize.y, out FilteringThreadGroupSize.z);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to get thread group sizes for ProbeFilteringShader kernel 'TemporalFilter' (index {TemporalFilterKernel}). " +
                    $"The shader may have compilation errors. Check the Console for shader compilation errors. Error: {e.Message}");
                return false;
            }

            try
            {
                ProbeUpsamplingShader.GetKernelThreadGroupSizes(UpsamplingKernel,
                    out UpsamplingThreadGroupSize.x, out UpsamplingThreadGroupSize.y, out UpsamplingThreadGroupSize.z);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to get thread group sizes for ProbeUpsamplingShader kernel 'UpsampleToScreen' (index {UpsamplingKernel}). " +
                    $"The shader may have compilation errors. Check the Console for shader compilation errors. Error: {e.Message}");
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            // Resources are managed by RenderPipelineResources
        }
    }

    /// <summary>
    /// Screen probe gather system for near-field GI
    /// </summary>
    internal class ScreenProbeGather : IDisposable
    {
        private ScreenProbeResourceSet _resources;
        private ScreenProbeParameters _parameters;

        // Persistent textures
        private RTHandle _probeRadianceAtlas;
        private RTHandle _probeHitDistanceAtlas;
        private RTHandle _probeDepthAtlas;
        private RTHandle _prevProbeRadianceAtlas;
        private RTHandle _prevProbeDepthAtlas;
        private RTHandle _filteredRadiance;

        private int2 _currentScreenSize;
        private int2 _currentProbeGridSize;
        private uint _frameIndex;

        public ScreenProbeGather()
        {
            _frameIndex = 0;
        }

        public bool Initialize(ScreenProbeParameters parameters)
        {
            _parameters = parameters;
            _resources = new ScreenProbeResourceSet();

            if (!_resources.LoadFromRenderPipelineResources())
            {
                Debug.LogError("Failed to load Screen Probe resources");
                return false;
            }

            return true;
        }

        public void UpdateParameters(ScreenProbeParameters parameters)
        {
            bool needsReallocation =
                _parameters.DownsampleFactor != parameters.DownsampleFactor ||
                _parameters.TracingOctahedronResolution != parameters.TracingOctahedronResolution ||
                _parameters.GatherOctahedronResolution != parameters.GatherOctahedronResolution;

            _parameters = parameters;

            if (needsReallocation)
            {
                ReleaseTextures();
            }
        }

        private void AllocateTextures(int2 screenSize)
        {
            if (_currentScreenSize.Equals(screenSize) && _probeRadianceAtlas != null)
                return;

            ReleaseTextures();

            _currentScreenSize = screenSize;
            _currentProbeGridSize = new int2(
                (screenSize.x + (int)_parameters.DownsampleFactor - 1) / (int)_parameters.DownsampleFactor,
                (screenSize.y + (int)_parameters.DownsampleFactor - 1) / (int)_parameters.DownsampleFactor
            );

            int atlasWidth = _currentProbeGridSize.x * (int)_parameters.GatherOctahedronResolution;
            int atlasHeight = _currentProbeGridSize.y * (int)_parameters.GatherOctahedronResolution;

            // Radiance atlas (RGB + hit distance in A)
            _probeRadianceAtlas = RTHandles.Alloc(
                atlasWidth, atlasHeight, 1,
                DepthBits.None, GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Bilinear, TextureWrapMode.Clamp,
                TextureDimension.Tex2D, true,
                name: "_ScreenProbeRadianceAtlas"
            );

            // Hit distance atlas
            _probeHitDistanceAtlas = RTHandles.Alloc(
                atlasWidth, atlasHeight, 1,
                DepthBits.None, GraphicsFormat.R16_SFloat,
                FilterMode.Point, TextureWrapMode.Clamp,
                TextureDimension.Tex2D, true,
                name: "_ScreenProbeHitDistanceAtlas"
            );

            // Depth atlas (for reprojection)
            _probeDepthAtlas = RTHandles.Alloc(
                _currentProbeGridSize.x, _currentProbeGridSize.y, 1,
                DepthBits.None, GraphicsFormat.R32_SFloat,
                FilterMode.Point, TextureWrapMode.Clamp,
                TextureDimension.Tex2D, true,
                name: "_ScreenProbeDepthAtlas"
            );

            // History textures
            _prevProbeRadianceAtlas = RTHandles.Alloc(
                atlasWidth, atlasHeight, 1,
                DepthBits.None, GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Bilinear, TextureWrapMode.Clamp,
                TextureDimension.Tex2D, true,
                name: "_PrevScreenProbeRadianceAtlas"
            );

            _prevProbeDepthAtlas = RTHandles.Alloc(
                _currentProbeGridSize.x, _currentProbeGridSize.y, 1,
                DepthBits.None, GraphicsFormat.R32_SFloat,
                FilterMode.Point, TextureWrapMode.Clamp,
                TextureDimension.Tex2D, true,
                name: "_PrevScreenProbeDepthAtlas"
            );

            // Filtered radiance (full resolution)
            _filteredRadiance = RTHandles.Alloc(
                screenSize.x, screenSize.y, 1,
                DepthBits.None, GraphicsFormat.R16G16B16A16_SFloat,
                FilterMode.Bilinear, TextureWrapMode.Clamp,
                TextureDimension.Tex2D, true,
                name: "_ScreenProbeFilteredRadiance"
            );
        }

        private void ReleaseTextures()
        {
            _probeRadianceAtlas?.Release();
            _probeHitDistanceAtlas?.Release();
            _probeDepthAtlas?.Release();
            _prevProbeRadianceAtlas?.Release();
            _prevProbeDepthAtlas?.Release();
            _filteredRadiance?.Release();

            _probeRadianceAtlas = null;
            _probeHitDistanceAtlas = null;
            _probeDepthAtlas = null;
            _prevProbeRadianceAtlas = null;
            _prevProbeDepthAtlas = null;
            _filteredRadiance = null;
        }

        public void Dispose()
        {
            ReleaseTextures();
            _resources?.Dispose();
        }

        public TextureHandle RecordRenderGraph(
            RenderGraph renderGraph,
            UniversalResourceData resourceData,
            UniversalCameraData cameraData,
            int2 screenSize,
            // Surface cache integration (optional)
            GraphicsBuffer surfaceCacheCellPatchIndices = null,
            GraphicsBuffer surfaceCachePatchIrradiances = null,
            GraphicsBuffer surfaceCacheCascadeOffsets = null,
            uint surfaceCacheGridResolution = 0,
            uint surfaceCacheCascadeCount = 0,
            float surfaceCacheVoxelMinSize = 0,
            Vector3 surfaceCacheVolumeCenter = default)
        {
            AllocateTextures(screenSize);

            // Import persistent textures
            var probeRadianceAtlasHandle = renderGraph.ImportTexture(_probeRadianceAtlas);
            var probeHitDistanceAtlasHandle = renderGraph.ImportTexture(_probeHitDistanceAtlas);
            var probeDepthAtlasHandle = renderGraph.ImportTexture(_probeDepthAtlas);
            var prevProbeRadianceAtlasHandle = renderGraph.ImportTexture(_prevProbeRadianceAtlas);
            var prevProbeDepthAtlasHandle = renderGraph.ImportTexture(_prevProbeDepthAtlas);
            var filteredRadianceHandle = renderGraph.ImportTexture(_filteredRadiance);

            // Trace screen probes
            TraceScreenProbes(renderGraph, resourceData, cameraData,
                probeRadianceAtlasHandle, probeHitDistanceAtlasHandle, probeDepthAtlasHandle,
                surfaceCacheCellPatchIndices, surfaceCachePatchIrradiances, surfaceCacheCascadeOffsets,
                surfaceCacheGridResolution, surfaceCacheCascadeCount, surfaceCacheVoxelMinSize, surfaceCacheVolumeCenter);

            // Temporal filtering
            TemporalFilter(renderGraph, resourceData, cameraData,
                probeRadianceAtlasHandle, probeDepthAtlasHandle,
                prevProbeRadianceAtlasHandle, prevProbeDepthAtlasHandle);

            // Spatial upsampling to full resolution
            SpatialUpsample(renderGraph, resourceData, cameraData,
                probeRadianceAtlasHandle, filteredRadianceHandle);

            // Copy current to previous for next frame
            CopyToPrevious(renderGraph, probeRadianceAtlasHandle, prevProbeRadianceAtlasHandle,
                probeDepthAtlasHandle, prevProbeDepthAtlasHandle);

            _frameIndex++;

            return filteredRadianceHandle;
        }

        private void TraceScreenProbes(
            RenderGraph renderGraph,
            UniversalResourceData resourceData,
            UniversalCameraData cameraData,
            TextureHandle probeRadianceAtlas,
            TextureHandle probeHitDistanceAtlas,
            TextureHandle probeDepthAtlas,
            GraphicsBuffer surfaceCacheCellPatchIndices,
            GraphicsBuffer surfaceCachePatchIrradiances,
            GraphicsBuffer surfaceCacheCascadeOffsets,
            uint surfaceCacheGridResolution,
            uint surfaceCacheCascadeCount,
            float surfaceCacheVoxelMinSize,
            Vector3 surfaceCacheVolumeCenter)
        {
            using (var builder = renderGraph.AddComputePass("Screen Probe Tracing", out ScreenProbeGatherPassData passData))
            {
                passData.ProbeTracingShader = _resources.ProbeTracingShader;
                passData.TracingKernel = _resources.TracingKernel;
                passData.TracingThreadGroupSize = _resources.TracingThreadGroupSize;

                passData.DepthTexture = resourceData.cameraDepthTexture;
                passData.NormalTexture = resourceData.cameraNormalsTexture;
                passData.ProbeRadianceAtlas = probeRadianceAtlas;
                passData.ProbeHitDistanceAtlas = probeHitDistanceAtlas;
                passData.ProbeDepthAtlas = probeDepthAtlas;

                passData.Parameters = _parameters;
                passData.ScreenSize = _currentScreenSize;
                passData.ProbeGridSize = _currentProbeGridSize;
                passData.ViewMatrix = cameraData.GetViewMatrix();
                passData.ProjectionMatrix = cameraData.GetGPUProjectionMatrix(true);
                passData.ViewProjectionMatrix = passData.ProjectionMatrix * passData.ViewMatrix;
                passData.InvViewProjectionMatrix = passData.ViewProjectionMatrix.inverse;
                passData.FrameIndex = _frameIndex;

                // Surface cache integration
                passData.UseSurfaceCacheFallback = surfaceCacheCellPatchIndices != null && _parameters.UseRadianceCacheFallback;
                passData.SurfaceCacheCellPatchIndices = surfaceCacheCellPatchIndices;
                passData.SurfaceCachePatchIrradiances = surfaceCachePatchIrradiances;
                passData.SurfaceCacheCascadeOffsets = surfaceCacheCascadeOffsets;
                passData.SurfaceCacheGridResolution = surfaceCacheGridResolution;
                passData.SurfaceCacheCascadeCount = surfaceCacheCascadeCount;
                passData.SurfaceCacheVoxelMinSize = surfaceCacheVoxelMinSize;
                passData.SurfaceCacheVolumeCenter = surfaceCacheVolumeCenter;

                builder.UseTexture(passData.DepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.NormalTexture, AccessFlags.Read);
                builder.UseTexture(passData.ProbeRadianceAtlas, AccessFlags.Write);
                builder.UseTexture(passData.ProbeHitDistanceAtlas, AccessFlags.Write);
                builder.UseTexture(passData.ProbeDepthAtlas, AccessFlags.Write);

                builder.SetRenderFunc((ScreenProbeGatherPassData data, ComputeGraphContext context) =>
                {
                    ExecuteProbeTracing(data, context);
                });
            }
        }

        private void TemporalFilter(
            RenderGraph renderGraph,
            UniversalResourceData resourceData,
            UniversalCameraData cameraData,
            TextureHandle currentRadiance,
            TextureHandle currentDepth,
            TextureHandle prevRadiance,
            TextureHandle prevDepth)
        {
            // Temporal filtering implementation
            // This will be implemented in the shader
        }

        private void SpatialUpsample(
            RenderGraph renderGraph,
            UniversalResourceData resourceData,
            UniversalCameraData cameraData,
            TextureHandle probeRadiance,
            TextureHandle outputRadiance)
        {
            // Spatial upsampling implementation
            // This will be implemented in the shader
        }

        private void CopyToPrevious(
            RenderGraph renderGraph,
            TextureHandle currentRadiance,
            TextureHandle prevRadiance,
            TextureHandle currentDepth,
            TextureHandle prevDepth)
        {
            // Copy current frame to history
            // Simple blit operations
        }

        private static void ExecuteProbeTracing(ScreenProbeGatherPassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;
            var shader = data.ProbeTracingShader;
            var kernel = data.TracingKernel;

            // Set parameters
            cmd.SetComputeIntParam(shader, "_FrameIndex", (int)data.FrameIndex);
            cmd.SetComputeIntParam(shader, "_DownsampleFactor", (int)data.Parameters.DownsampleFactor);
            cmd.SetComputeIntParam(shader, "_TracingOctahedronResolution", (int)data.Parameters.TracingOctahedronResolution);
            cmd.SetComputeFloatParam(shader, "_MaxRayDistance", data.Parameters.MaxRayDistance);
            cmd.SetComputeFloatParam(shader, "_NearFieldMaxDistance", data.Parameters.NearFieldMaxDistance);
            cmd.SetComputeMatrixParam(shader, "_InvViewProjectionMatrix", data.InvViewProjectionMatrix);
            cmd.SetComputeMatrixParam(shader, "_ViewProjectionMatrix", data.ViewProjectionMatrix);

            // Set screen parameters
            int2 screenSize = data.ScreenSize;
            cmd.SetComputeVectorParam(shader, "_ScreenParams",
                new Vector4(screenSize.x, screenSize.y, 1.0f + 1.0f / screenSize.x, 1.0f + 1.0f / screenSize.y));

            // Set textures
            cmd.SetComputeTextureParam(shader, kernel, "_DepthTexture", data.DepthTexture);
            cmd.SetComputeTextureParam(shader, kernel, "_NormalTexture", data.NormalTexture);
            cmd.SetComputeTextureParam(shader, kernel, "_ProbeRadianceAtlas", data.ProbeRadianceAtlas);
            cmd.SetComputeTextureParam(shader, kernel, "_ProbeHitDistanceAtlas", data.ProbeHitDistanceAtlas);
            cmd.SetComputeTextureParam(shader, kernel, "_ProbeDepthAtlas", data.ProbeDepthAtlas);

            // Surface cache integration
            if (data.UseSurfaceCacheFallback)
            {
                cmd.SetComputeIntParam(shader, "_UseSurfaceCacheFallback", 1);
                cmd.SetComputeBufferParam(shader, kernel, "_SurfaceCacheCellPatchIndices", data.SurfaceCacheCellPatchIndices);
                cmd.SetComputeBufferParam(shader, kernel, "_SurfaceCachePatchIrradiances", data.SurfaceCachePatchIrradiances);
                cmd.SetComputeBufferParam(shader, kernel, "_SurfaceCacheCascadeOffsets", data.SurfaceCacheCascadeOffsets);
                cmd.SetComputeIntParam(shader, "_SurfaceCacheGridResolution", (int)data.SurfaceCacheGridResolution);
                cmd.SetComputeIntParam(shader, "_SurfaceCacheCascadeCount", (int)data.SurfaceCacheCascadeCount);
                cmd.SetComputeFloatParam(shader, "_SurfaceCacheVoxelMinSize", data.SurfaceCacheVoxelMinSize);
                cmd.SetComputeVectorParam(shader, "_SurfaceCacheVolumeCenter", data.SurfaceCacheVolumeCenter);
            }
            else
            {
                cmd.SetComputeIntParam(shader, "_UseSurfaceCacheFallback", 0);
            }

            // Dispatch
            int2 probeGridSize = data.ProbeGridSize;

            uint3 threadGroups = new uint3(
                (uint)((probeGridSize.x + (int)data.TracingThreadGroupSize.x - 1) / (int)data.TracingThreadGroupSize.x),
                (uint)((probeGridSize.y + (int)data.TracingThreadGroupSize.y - 1) / (int)data.TracingThreadGroupSize.y),
                1
            );

            cmd.DispatchCompute(shader, kernel, (int)threadGroups.x, (int)threadGroups.y, (int)threadGroups.z);
        }
    }
}
