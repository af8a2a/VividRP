using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    /// <summary>
    /// Manages local-light uploads and world-space Onion ReGIR builds for Lumen Surface Cache
    /// sampled direct lighting (single-pass multi-iteration RIS).
    /// </summary>
    internal sealed class SdfSurfaceCacheSampledLighting : IDisposable
    {
        internal const int DirectEvalLightThreshold = 4;
        internal const int DefaultLocalIterations = 4;
        internal const int DefaultLocalCandidates = 4;

        private const string BuildKernelName = "ReGIRGridBuild";
        private const string PrepareLightPdfKernelName = "ReGIRPrepareLightPdf";
        private const string ReduceLightPdfKernelName = "ReGIRReduceLightPdf";
        private const string PresampleLocalLightsKernelName = "ReGIRPresampleLocalLights";

        private static readonly ProfilingSampler ReGIRBuildSampler = new("Surface Cache Sampled Lighting ReGIR Build");

        private static readonly int ReGIRLightsId = Shader.PropertyToID("_ReGIRLights");
        private static readonly int ReGIRParametersId = Shader.PropertyToID("_ReGIRParameters");
        private static readonly int ReGIRReservoirsId = Shader.PropertyToID("_ReGIRReservoirs");
        private static readonly int ReGIRLightPdfTextureId = Shader.PropertyToID("_ReGIRLightPdfTexture");
        private static readonly int ReGIRLightPdfMipInputId = Shader.PropertyToID("_ReGIRLightPdfMipInput");
        private static readonly int ReGIRLightPdfMipOutputId = Shader.PropertyToID("_ReGIRLightPdfMipOutput");
        private static readonly int ReGIRLightPdfSourceMipId = Shader.PropertyToID("_ReGIRLightPdfSourceMip");
        private static readonly int ReGIRPresampledLightsId = Shader.PropertyToID("_ReGIRPresampledLights");

        private static readonly int SampledLightingLocalIterationsId = Shader.PropertyToID("_SdfSampledLightingLocalIterations");
        private static readonly int SampledLightingLocalCandidatesId = Shader.PropertyToID("_SdfSampledLightingLocalCandidates");
        private static readonly int SampledLightingEvaluateAllId = Shader.PropertyToID("_SdfSampledLightingEvaluateAll");
        private static readonly int SampledLightingUseReGIRId = Shader.PropertyToID("_SdfSampledLightingUseReGIR");
        private static readonly int SampledLightingFrameId = Shader.PropertyToID("_SdfSampledLightingFrame");
        private static readonly int AccelerationStructureId = Shader.PropertyToID("_AccelerationStructure");

#if UNITY_EDITOR
        // -1 = auto (direct eval <= 4 lights, Onion ReGIR RIS > 4 lights),
        //  0 = force Onion ReGIR RIS even for <= 4 lights,
        //  1 = force EvaluateAllLights golden reference.
        internal static int EditorEvaluateAllLightsOverride = -1;
        internal static RayTracingAccelerationStructure EditorRayTracingAccelerationStructureOverride;
#endif

        private ComputeShader m_ReGIRGridBuildCompute;
        private int m_PrepareLightPdfKernel = -1;
        private int m_ReduceLightPdfKernel = -1;
        private int m_PresampleLocalLightsKernel = -1;
        private int m_BuildKernel = -1;

        private GraphicsBuffer m_LightBuffer;
        private GraphicsBuffer m_ParameterBuffer;
        private GraphicsBuffer m_ReservoirBuffer;
        private GraphicsBuffer m_PresampledLightBuffer;
        private RTHandle m_LightPdfTexture;
        private TextureHandle m_LightPdfHandle;

        private NativeArray<VividReGIRLightData> m_LightUploadData;
        private NativeArray<VividReGIRParameters> m_ParameterUploadData;
        private VividLightData m_FrameLightData;
        private VividRayTracingSettingsData m_FrameRayTracingData;

        private int m_LightPdfTextureSize;
        private int m_LightPdfTexelCount;
        private int m_LightPdfMipCount;
        private int m_LightPdfPrepareDispatchGroupCount;
        private int m_PresampledLightDispatchGroupCount;
        private int m_BuildDispatchGroupCount;
        private bool m_GpuPrepared;

        internal bool IsSupported => m_ReGIRGridBuildCompute != null && m_BuildKernel >= 0;
        internal bool HasLocalLights { get; private set; }
        internal int LocalLightCount { get; private set; }
        internal bool EvaluateAllLights { get; private set; }
        internal bool NeedsReGIRBuild { get; private set; }
        internal bool UseHardwareRayTracing { get; private set; }
        internal int LocalIterations { get; private set; } = DefaultLocalIterations;
        internal int LocalCandidates { get; private set; } = DefaultLocalCandidates;
        internal ulong LightStateHash { get; private set; }
        internal uint ModeKey { get; private set; }

        internal void Initialize(VividRPCoreResources resources)
        {
            m_ReGIRGridBuildCompute = resources != null ? resources.ReGIRGridBuildCompute : null;
            m_PrepareLightPdfKernel = -1;
            m_ReduceLightPdfKernel = -1;
            m_PresampleLocalLightsKernel = -1;
            m_BuildKernel = -1;
            if (m_ReGIRGridBuildCompute == null)
                return;

            m_PrepareLightPdfKernel = TryFindKernel(m_ReGIRGridBuildCompute, PrepareLightPdfKernelName);
            m_ReduceLightPdfKernel = TryFindKernel(m_ReGIRGridBuildCompute, ReduceLightPdfKernelName);
            m_PresampleLocalLightsKernel = TryFindKernel(m_ReGIRGridBuildCompute, PresampleLocalLightsKernelName);
            m_BuildKernel = TryFindKernel(m_ReGIRGridBuildCompute, BuildKernelName);
            if (m_PrepareLightPdfKernel < 0
                || m_ReduceLightPdfKernel < 0
                || m_PresampleLocalLightsKernel < 0
                || m_BuildKernel < 0)
            {
                m_ReGIRGridBuildCompute = null;
                m_PrepareLightPdfKernel = -1;
                m_ReduceLightPdfKernel = -1;
                m_PresampleLocalLightsKernel = -1;
                m_BuildKernel = -1;
            }
        }

        internal void PrepareLightState(
            ContextContainer frameData,
            bool enabled = true,
            bool allowHardwareRayTracing = true,
            bool rayQueryKernelAvailable = false,
            int localIterations = DefaultLocalIterations,
            int localCandidates = DefaultLocalCandidates)
        {
            HasLocalLights = false;
            LocalLightCount = 0;
            EvaluateAllLights = false;
            NeedsReGIRBuild = false;
            UseHardwareRayTracing = false;
            LocalIterations = Mathf.Clamp(localIterations, 1, 8);
            LocalCandidates = Mathf.Clamp(localCandidates, 1, 16);
            LightStateHash = 0UL;
            ModeKey = 0u;
            m_GpuPrepared = false;
            m_LightPdfHandle = default;
            m_FrameLightData = null;
            m_FrameRayTracingData = null;

            if (!enabled || frameData == null)
                return;

            var lightData = frameData.GetOrCreate<VividLightData>();
            lightData.CompleteReGIRPrepare();
            int count = lightData.reGIRLights != null
                ? Mathf.Clamp(lightData.reGIRLightCount, 0, lightData.reGIRLights.Length)
                : 0;
            if (count <= 0)
                return;

            bool hasRtasCandidate = false;
            if (allowHardwareRayTracing && rayQueryKernelAvailable && SystemInfo.supportsInlineRayTracing)
            {
#if UNITY_EDITOR
                if (EditorRayTracingAccelerationStructureOverride != null)
                    hasRtasCandidate = true;
#endif
                if (!hasRtasCandidate && frameData.Contains<VividRayTracingSettingsData>())
                {
                    m_FrameRayTracingData = frameData.Get<VividRayTracingSettingsData>();
                    hasRtasCandidate = m_FrameRayTracingData != null
                        && m_FrameRayTracingData.sceneAccelerationStructure != null;
                }
            }

            bool evaluateAll = ResolveEvaluateAll(count);
            HasLocalLights = true;
            LocalLightCount = count;
            EvaluateAllLights = evaluateAll;
            UseHardwareRayTracing = hasRtasCandidate;
            LightStateHash = ComputeLocalLightHash(lightData.reGIRLights, count);
            ModeKey = BuildModeKey(evaluateAll, hasRtasCandidate, LocalIterations, LocalCandidates);
            m_FrameLightData = lightData;
        }

        internal RayTracingAccelerationStructure ResolveBuiltAccelerationStructure()
        {
            if (!UseHardwareRayTracing)
                return null;

#if UNITY_EDITOR
            if (EditorRayTracingAccelerationStructureOverride != null)
                return EditorRayTracingAccelerationStructureOverride;
#endif
            if (m_FrameRayTracingData != null
                && m_FrameRayTracingData.hasBuiltAccelerationStructure
                && m_FrameRayTracingData.sceneAccelerationStructure != null)
            {
                return m_FrameRayTracingData.sceneAccelerationStructure;
            }

            return null;
        }

        internal void PrepareGpuResources(IRenderPass pass, Camera camera, uint frameIndex)
        {
            m_GpuPrepared = false;
            NeedsReGIRBuild = false;
            m_LightPdfHandle = default;

            if (!HasLocalLights || LocalLightCount <= 0 || m_FrameLightData == null || m_FrameLightData.reGIRLights == null)
                return;

            bool useReGIR = !EvaluateAllLights && IsSupported;
            VividReGIRParameters parameters;
            int reservoirCount;
            int presampledCount;
            if (useReGIR)
            {
                Vector3 centerWS = camera != null ? camera.transform.position : Vector3.zero;
                parameters = CreateOnionParameters(
                    centerWS,
                    LocalLightCount,
                    frameIndex,
                    out m_LightPdfTextureSize,
                    out m_LightPdfTexelCount,
                    out m_LightPdfMipCount);
                reservoirCount = Mathf.Max(1, (int)Math.Min(parameters.slotCount, int.MaxValue));
                presampledCount = ReGIRGridBuildPass.DefaultPresampledLightTileSize * ReGIRGridBuildPass.DefaultPresampledLightTileCount;
                m_LightPdfPrepareDispatchGroupCount = Mathf.Max(1, CoreUtils.DivRoundUp(m_LightPdfTexelCount, ReGIRGridBuildPass.ThreadGroupSize));
                m_PresampledLightDispatchGroupCount = Mathf.Max(1, CoreUtils.DivRoundUp(presampledCount, ReGIRGridBuildPass.ThreadGroupSize));
                m_BuildDispatchGroupCount = Mathf.Max(1, CoreUtils.DivRoundUp(reservoirCount, ReGIRGridBuildPass.ThreadGroupSize));
                EnsureLightPdfTexture(m_LightPdfTextureSize, m_LightPdfMipCount);
                m_LightPdfHandle = PassRecorder.ImportTextureForPass(pass, m_LightPdfTexture, AccessFlags.ReadWrite);
                NeedsReGIRBuild = m_LightPdfHandle.IsValid();
                if (!NeedsReGIRBuild)
                {
                    EvaluateAllLights = true;
                    ModeKey = BuildModeKey(true, UseHardwareRayTracing, LocalIterations, LocalCandidates);
                    reservoirCount = 1;
                    presampledCount = 1;
                }
            }
            else
            {
                EvaluateAllLights = true;
                ModeKey = BuildModeKey(true, UseHardwareRayTracing, LocalIterations, LocalCandidates);
                parameters = new VividReGIRParameters
                {
                    lightCount = (uint)LocalLightCount,
                    lightsPerCell = 1u,
                    slotCount = 1u,
                    frameIndex = frameIndex,
                    mode = VividReGIRMode.Onion,
                    sourceSamplingMode = VividReGIRSourceSamplingMode.PowerRIS,
                };
                reservoirCount = 1;
                presampledCount = 1;
            }

            EnsureStructuredBuffer(ref m_LightBuffer, LocalLightCount, VividReGIRLightData.Stride);
            EnsureStructuredBuffer(ref m_ParameterBuffer, 1, VividReGIRParameters.Stride);
            EnsureStructuredBuffer(ref m_ReservoirBuffer, reservoirCount, VividReGIRReservoir.Stride);
            EnsureStructuredBuffer(ref m_PresampledLightBuffer, presampledCount, ReGIRGridBuildPass.PresampledLightEntryStride);

            EnsureNativeCapacity(ref m_LightUploadData, LocalLightCount);
            NativeArray<VividReGIRLightData>.Copy(m_FrameLightData.reGIRLights, m_LightUploadData, LocalLightCount);
            m_LightBuffer.SetData(m_LightUploadData, 0, 0, LocalLightCount);

            EnsureNativeCapacity(ref m_ParameterUploadData, 1);
            m_ParameterUploadData[0] = parameters;
            m_ParameterBuffer.SetData(m_ParameterUploadData, 0, 0, 1);

            m_GpuPrepared = true;
        }

        internal void RecordBuild(ComputeCommandBuffer cmd)
        {
            if (!m_GpuPrepared || !NeedsReGIRBuild || !IsSupported || !m_LightPdfHandle.IsValid())
                return;

            using var scope = new ProfilingScope(cmd, ReGIRBuildSampler);

            // 1. Build 2D light-power PDF mip pyramid (Z-curve layout).
            cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_PrepareLightPdfKernel, ReGIRLightsId, m_LightBuffer);
            cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_PrepareLightPdfKernel, ReGIRParametersId, m_ParameterBuffer);
            cmd.SetComputeTextureParam(m_ReGIRGridBuildCompute, m_PrepareLightPdfKernel, ReGIRLightPdfMipOutputId, m_LightPdfHandle, 0);
            cmd.DispatchCompute(m_ReGIRGridBuildCompute, m_PrepareLightPdfKernel, m_LightPdfPrepareDispatchGroupCount, 1, 1);

            for (int mipIndex = 1; mipIndex < m_LightPdfMipCount; mipIndex++)
            {
                int mipSize = Mathf.Max(1, m_LightPdfTextureSize >> mipIndex);
                cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_ReduceLightPdfKernel, ReGIRParametersId, m_ParameterBuffer);
                cmd.SetComputeTextureParam(m_ReGIRGridBuildCompute, m_ReduceLightPdfKernel, ReGIRLightPdfMipInputId, m_LightPdfHandle, mipIndex - 1);
                cmd.SetComputeTextureParam(m_ReGIRGridBuildCompute, m_ReduceLightPdfKernel, ReGIRLightPdfMipOutputId, m_LightPdfHandle, mipIndex);
                cmd.SetComputeIntParam(m_ReGIRGridBuildCompute, ReGIRLightPdfSourceMipId, mipIndex - 1);
                int groups = Mathf.Max(1, CoreUtils.DivRoundUp(mipSize, ReGIRGridBuildPass.PdfReduceThreadGroupSize));
                cmd.DispatchCompute(m_ReGIRGridBuildCompute, m_ReduceLightPdfKernel, groups, groups, 1);
            }

            // 2. Presample local lights by radiant power (used both by Onion build and outside-Onion fallback).
            cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_PresampleLocalLightsKernel, ReGIRLightsId, m_LightBuffer);
            cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_PresampleLocalLightsKernel, ReGIRParametersId, m_ParameterBuffer);
            cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_PresampleLocalLightsKernel, ReGIRPresampledLightsId, m_PresampledLightBuffer);
            cmd.SetComputeTextureParam(m_ReGIRGridBuildCompute, m_PresampleLocalLightsKernel, ReGIRLightPdfTextureId, m_LightPdfHandle);
            cmd.DispatchCompute(m_ReGIRGridBuildCompute, m_PresampleLocalLightsKernel, m_PresampledLightDispatchGroupCount, 1, 1);

            // 3. Populate world-space Onion ReGIR cell reservoirs.
            cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_BuildKernel, ReGIRLightsId, m_LightBuffer);
            cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_BuildKernel, ReGIRParametersId, m_ParameterBuffer);
            cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_BuildKernel, ReGIRReservoirsId, m_ReservoirBuffer);
            cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_BuildKernel, ReGIRPresampledLightsId, m_PresampledLightBuffer);
            cmd.SetComputeTextureParam(m_ReGIRGridBuildCompute, m_BuildKernel, ReGIRLightPdfTextureId, m_LightPdfHandle);
            cmd.DispatchCompute(m_ReGIRGridBuildCompute, m_BuildKernel, m_BuildDispatchGroupCount, 1, 1);
        }

        internal void BindRayQueryAccelerationStructure(
            ComputeCommandBuffer cmd,
            ComputeShader shader,
            int kernel,
            RayTracingAccelerationStructure accelerationStructure)
        {
            if (accelerationStructure == null)
                return;

            cmd.SetRayTracingAccelerationStructure(shader, kernel, AccelerationStructureId, accelerationStructure);
        }

        internal void BindForCardDirectLighting(ComputeCommandBuffer cmd, ComputeShader shader, int kernel, uint frameIndex)
        {
            if (!m_GpuPrepared)
                return;

            cmd.SetComputeBufferParam(shader, kernel, ReGIRLightsId, m_LightBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ReGIRParametersId, m_ParameterBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ReGIRReservoirsId, m_ReservoirBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ReGIRPresampledLightsId, m_PresampledLightBuffer);
            cmd.SetComputeIntParam(shader, SampledLightingLocalIterationsId, LocalIterations);
            cmd.SetComputeIntParam(shader, SampledLightingLocalCandidatesId, LocalCandidates);
            cmd.SetComputeIntParam(shader, SampledLightingEvaluateAllId, EvaluateAllLights ? 1 : 0);
            cmd.SetComputeIntParam(shader, SampledLightingUseReGIRId, NeedsReGIRBuild ? 1 : 0);
            cmd.SetComputeIntParam(shader, SampledLightingFrameId, (int)frameIndex);
        }

        internal static VividReGIRParameters CreateOnionParameters(
            Vector3 centerWS,
            int lightCount,
            uint frameIndex,
            out int lightPdfTextureSize,
            out int lightPdfTexelCount,
            out int lightPdfMipCount)
        {
            ReGIRGridBuildPass.ResolveLightPdfTextureLayout(
                lightCount,
                out lightPdfTextureSize,
                out lightPdfTexelCount,
                out lightPdfMipCount);

            float cellSize = ReGIRGridBuildPass.ResolveCellSize(VividReGIRMode.Onion);
            int cellCount = ReGIRGridBuildPass.ComputeOnionCellCount(
                ReGIRGridBuildPass.DefaultOnionDetailLayerGroups,
                ReGIRGridBuildPass.DefaultOnionCoverageLayers);
            int slotCount = ReGIRGridBuildPass.ComputeSlotCount(cellCount, ReGIRGridBuildPass.DefaultLightsPerCell);

            var parameters = new VividReGIRParameters
            {
                centerWS = centerWS,
                cellSize = cellSize,
                gridSizeX = ReGIRGridBuildPass.DefaultGridSizeX,
                gridSizeY = ReGIRGridBuildPass.DefaultGridSizeY,
                gridSizeZ = ReGIRGridBuildPass.DefaultGridSizeZ,
                lightsPerCell = ReGIRGridBuildPass.DefaultLightsPerCell,
                lightCount = (uint)Mathf.Max(lightCount, 0),
                slotCount = (uint)Mathf.Max(slotCount, 0),
                buildSamples = ReGIRGridBuildPass.DefaultBuildSamples,
                samplingJitter = ReGIRGridBuildPass.ResolveSamplingJitter(VividReGIRMode.Onion),
                frameIndex = frameIndex,
                mode = VividReGIRMode.Onion,
                sourceSamplingMode = VividReGIRSourceSamplingMode.PowerRIS,
                lightPdfTextureWidth = (uint)Mathf.Max(lightPdfTextureSize, 1),
                lightPdfTextureHeight = (uint)Mathf.Max(lightPdfTextureSize, 1),
                lightPdfTextureMipCount = (uint)Mathf.Max(lightPdfMipCount, 1),
            };

            ReGIRGridBuildPass.BuildOnionParameters(
                ref parameters,
                ReGIRGridBuildPass.DefaultOnionDetailLayerGroups,
                ReGIRGridBuildPass.DefaultOnionCoverageLayers,
                cellSize);

            return parameters;
        }

        internal static ulong ComputeLocalLightHash(VividReGIRLightData[] lights, int count)
        {
            if (lights == null || count <= 0)
                return 0UL;

            int clamped = count < lights.Length ? count : lights.Length;
            ulong hash = 14695981039346656037UL;
            hash = MixUInt(hash, (uint)clamped);
            for (int i = 0; i < clamped; i++)
            {
                VividReGIRLightData light = lights[i];
                hash = MixFloat(hash, light.positionWS.x);
                hash = MixFloat(hash, light.positionWS.y);
                hash = MixFloat(hash, light.positionWS.z);
                hash = MixFloat(hash, light.range);
                hash = MixFloat(hash, light.color.x);
                hash = MixFloat(hash, light.color.y);
                hash = MixFloat(hash, light.color.z);
                hash = MixUInt(hash, (uint)light.lightType);
                hash = MixFloat(hash, light.directionWS.x);
                hash = MixFloat(hash, light.directionWS.y);
                hash = MixFloat(hash, light.directionWS.z);
                hash = MixFloat(hash, light.angleScale);
                hash = MixFloat(hash, light.rightWS.x);
                hash = MixFloat(hash, light.rightWS.y);
                hash = MixFloat(hash, light.rightWS.z);
                hash = MixFloat(hash, light.angleOffset);
                hash = MixFloat(hash, light.upWS.x);
                hash = MixFloat(hash, light.upWS.y);
                hash = MixFloat(hash, light.upWS.z);
                hash = MixFloat(hash, light.shapeRadius);
                hash = MixFloat(hash, light.areaSize.x);
                hash = MixFloat(hash, light.areaSize.y);
                hash = MixUInt(hash, light.renderingLayerMask);
                hash = MixFloat(hash, light.cosBarnDoorAngle);
                hash = MixFloat(hash, light.barnDoorLength);
                hash = MixFloat(hash, light.shadowStrength);
                hash = MixFloat(hash, light.rangeAttenuationScale);
                hash = MixFloat(hash, light.rangeAttenuationBias);
                hash = MixFloat(hash, light.indirectMultiplier);
            }

            return hash != 0UL ? hash : 1UL;
        }

        public void Dispose()
        {
            ReleaseBuffer(ref m_LightBuffer);
            ReleaseBuffer(ref m_ParameterBuffer);
            ReleaseBuffer(ref m_ReservoirBuffer);
            ReleaseBuffer(ref m_PresampledLightBuffer);
            ReleaseLightPdfTexture();
            DisposeNativeArray(ref m_LightUploadData);
            DisposeNativeArray(ref m_ParameterUploadData);

            m_ReGIRGridBuildCompute = null;
            m_PrepareLightPdfKernel = -1;
            m_ReduceLightPdfKernel = -1;
            m_PresampleLocalLightsKernel = -1;
            m_BuildKernel = -1;
            m_FrameLightData = null;
            m_FrameRayTracingData = null;
            m_LightPdfHandle = default;
            m_GpuPrepared = false;
            HasLocalLights = false;
            LocalLightCount = 0;
            EvaluateAllLights = false;
            NeedsReGIRBuild = false;
            UseHardwareRayTracing = false;
            LocalIterations = DefaultLocalIterations;
            LocalCandidates = DefaultLocalCandidates;
            LightStateHash = 0UL;
            ModeKey = 0u;
        }

        internal static uint BuildModeKey(bool evaluateAll, bool useHardwareRayTracing, int localIterations, int localCandidates)
        {
            uint evalMode = evaluateAll ? 1u : 2u;
            uint rqBit = useHardwareRayTracing ? 1u : 0u;
            return evalMode
                | (rqBit << 2)
                | ((uint)Mathf.Clamp(localIterations, 1, 8) << 4)
                | ((uint)Mathf.Clamp(localCandidates, 1, 16) << 8);
        }

        private bool ResolveEvaluateAll(int lightCount)
        {
#if UNITY_EDITOR
            if (EditorEvaluateAllLightsOverride == 1)
                return true;
            if (EditorEvaluateAllLightsOverride == 0 && IsSupported)
                return false;
#endif
            return lightCount <= DirectEvalLightThreshold || !IsSupported;
        }

        private void EnsureLightPdfTexture(int textureSize, int mipCount)
        {
            textureSize = Mathf.Max(textureSize, 1);
            if (m_LightPdfTexture != null
                && m_LightPdfTexture.rt != null
                && m_LightPdfTexture.rt.width == textureSize
                && m_LightPdfTexture.rt.height == textureSize
                && m_LightPdfTexture.rt.mipmapCount == Mathf.Max(mipCount, 1))
            {
                return;
            }

            ReleaseLightPdfTexture();
            m_LightPdfTexture = RTHandles.Alloc(
                width: textureSize,
                height: textureSize,
                colorFormat: GraphicsFormat.R32_SFloat,
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp,
                enableRandomWrite: true,
                useMipMap: true,
                autoGenerateMips: false,
                name: "SdfSurfaceCacheReGIRLightPdf");
        }

        private void ReleaseLightPdfTexture()
        {
            if (m_LightPdfTexture == null)
                return;

            RTHandles.Release(m_LightPdfTexture);
            m_LightPdfTexture = null;
        }

        private static void EnsureStructuredBuffer(ref GraphicsBuffer buffer, int count, int stride)
        {
            count = Mathf.Max(count, 1);
            if (buffer != null && buffer.IsValid() && buffer.count == count && buffer.stride == stride)
                return;

            ReleaseBuffer(ref buffer);
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);
        }

        private static void ReleaseBuffer(ref GraphicsBuffer buffer)
        {
            buffer?.Dispose();
            buffer = null;
        }

        private static void EnsureNativeCapacity<T>(ref NativeArray<T> array, int requiredCapacity)
            where T : struct
        {
            requiredCapacity = Mathf.Max(requiredCapacity, 1);
            if (array.IsCreated && array.Length >= requiredCapacity)
                return;

            DisposeNativeArray(ref array);
            array = new NativeArray<T>(requiredCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void DisposeNativeArray<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (!array.IsCreated)
                return;

            array.Dispose();
            array = default;
        }

        private static int TryFindKernel(ComputeShader shader, string name)
        {
            try
            {
                return shader.FindKernel(name);
            }
            catch (ArgumentException)
            {
                return -1;
            }
        }

        private static ulong MixUInt(ulong hash, uint value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
            return hash;
        }

        private static ulong MixFloat(ulong hash, float value)
        {
            // Canonicalize -0.0f / non-finite to 0 while preserving full HDR dynamic range.
            uint bits = float.IsFinite(value) && value != 0.0f
                ? unchecked((uint)BitConverter.SingleToInt32Bits(value))
                : 0u;
            return MixUInt(hash, bits);
        }
    }
}
