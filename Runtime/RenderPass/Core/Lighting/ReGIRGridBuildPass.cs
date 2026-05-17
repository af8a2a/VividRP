using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public sealed class ReGIRGridBuildPass : ComputePass
    {
        internal const VividReGIRMode DefaultMode = VividReGIRMode.Grid;
        internal const int DefaultGridSizeX = 16;
        internal const int DefaultGridSizeY = 16;
        internal const int DefaultGridSizeZ = 16;
        internal const int DefaultLightsPerCell = 64;
        internal const float DefaultCellSize = 1.0f;
        internal const int DefaultBuildSamples = 8;
        internal const float DefaultSamplingJitter = 1.0f;
        internal const int DefaultOnionDetailLayerGroups = 5;
        internal const int DefaultOnionCoverageLayers = 10;

        private const string KernelName = "ReGIRGridBuild";
        private const int ThreadGroupSize = 256;
        private const float Pi = Mathf.PI;

        private static readonly int ReGIRLightsId = Shader.PropertyToID("_ReGIRLights");
        private static readonly int ReGIRParametersId = Shader.PropertyToID("_ReGIRParameters");
        private static readonly int ReGIRReservoirsId = Shader.PropertyToID("_ReGIRReservoirs");

        [RenderGraphResource(Name = "ReGIRLights", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_ReGIRLightBuffer;

        [RenderGraphResource(Name = "ReGIRParameters", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_ReGIRParameterBuffer;

        [RenderGraphResource(Name = "ReGIRReservoirs", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_ReGIRReservoirBuffer;

        [SerializeField]
        private VividReGIRMode m_Mode = DefaultMode;

        private ComputeShader m_ReGIRGridBuildCompute;
        private NativeArray<VividReGIRLightData> m_ReGIRLightUploadData;
        private NativeArray<VividReGIRParameters> m_ReGIRParameterUploadData;
        private VividReGIRParameters m_ReGIRParameters;
        private int m_Kernel = -1;
        private int m_ReGIRLightCount;
        private int m_ReGIRSlotCount;
        private int m_DispatchGroupCount;

        public VividReGIRMode Mode
        {
            get => NormalizeMode(m_Mode);
            set => m_Mode = NormalizeMode(value);
        }

        public ReGIRGridBuildPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ReGIRGridBuildPass));
            m_ReGIRLightBuffer = RenderGraphBuffer.CreateStructured("ReGIRLights", 1, VividReGIRLightData.Stride);
            m_ReGIRParameterBuffer = RenderGraphBuffer.CreateStructured("ReGIRParameters", 1, VividReGIRParameters.Stride);
            m_ReGIRReservoirBuffer = RenderGraphBuffer.CreateStructured("ReGIRReservoirs", 1, VividReGIRReservoir.Stride);
        }

        public override void Create()
        {
            m_ReGIRGridBuildCompute = PipelineResourceManager.Get<VividRPCoreResources>()?.ReGIRGridBuildCompute;
            if (m_ReGIRGridBuildCompute == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find compute shader resource 'Shaders/Core/Private/Lighting/ReGIRGridBuild' for {nameof(ReGIRGridBuildPass)}.");
                return;
            }

            try
            {
                m_Kernel = m_ReGIRGridBuildCompute.FindKernel(KernelName);
            }
            catch (ArgumentException)
            {
                Debug.LogWarning($"[VividRP] Could not find kernel '{KernelName}' in ReGIR grid build compute shader.");
                m_ReGIRGridBuildCompute = null;
                m_Kernel = -1;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            var lightData = frameData.GetOrCreate<VividLightData>();
            lightData.CompleteReGIRPrepare();

            m_ReGIRLightCount = Mathf.Clamp(lightData.reGIRLightCount, 0, lightData.reGIRLights?.Length ?? 0);
            m_ReGIRParameters = CreateParameters(frameData);
            m_ReGIRSlotCount = Mathf.Max(1, (int)Math.Min(m_ReGIRParameters.slotCount, int.MaxValue));
            m_DispatchGroupCount = Mathf.Max(1, CoreUtils.DivRoundUp(m_ReGIRSlotCount, ThreadGroupSize));

            ResizeStructuredBuffer(m_ReGIRLightBuffer, Mathf.Max(m_ReGIRLightCount, 1), VividReGIRLightData.Stride);
            ResizeStructuredBuffer(m_ReGIRParameterBuffer, 1, VividReGIRParameters.Stride);
            ResizeStructuredBuffer(m_ReGIRReservoirBuffer, Mathf.Max(m_ReGIRSlotCount, 1), VividReGIRReservoir.Stride);
            EnsureImportedBuffers();

            UploadReGIRLights(lightData);
            UploadReGIRParameters();
        }

        public override void Record(ComputePassContext context)
        {
            if (m_ReGIRGridBuildCompute == null || m_Kernel < 0)
                return;

            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                context.cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_Kernel, ReGIRLightsId, m_ReGIRLightBuffer.innerHandle);
                context.cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_Kernel, ReGIRParametersId, m_ReGIRParameterBuffer.innerHandle);
                context.cmd.SetComputeBufferParam(m_ReGIRGridBuildCompute, m_Kernel, ReGIRReservoirsId, m_ReGIRReservoirBuffer.innerHandle);
                context.cmd.DispatchCompute(m_ReGIRGridBuildCompute, m_Kernel, m_DispatchGroupCount, 1, 1);
            }
        }

        public override void Dispose()
        {
            ReleaseImportedBuffers();
            DisposeNativeUploadData(ref m_ReGIRLightUploadData);
            DisposeNativeUploadData(ref m_ReGIRParameterUploadData);
            m_ReGIRGridBuildCompute = null;
            m_Kernel = -1;
            m_ReGIRLightCount = 0;
            m_ReGIRSlotCount = 0;
            m_DispatchGroupCount = 0;
        }

        private void UploadReGIRLights(VividLightData lightData)
        {
            if (m_ReGIRLightCount > 0)
            {
                EnsureNativeUploadCapacity(ref m_ReGIRLightUploadData, m_ReGIRLightCount);
                NativeArray<VividReGIRLightData>.Copy(lightData.reGIRLights, m_ReGIRLightUploadData, m_ReGIRLightCount);
                m_ReGIRLightBuffer.SetData(m_ReGIRLightUploadData, 0, 0, m_ReGIRLightCount);
                return;
            }

            UploadDefault(m_ReGIRLightBuffer, ref m_ReGIRLightUploadData);
        }

        private void UploadReGIRParameters()
        {
            EnsureNativeUploadCapacity(ref m_ReGIRParameterUploadData, 1);
            m_ReGIRParameterUploadData[0] = m_ReGIRParameters;
            m_ReGIRParameterBuffer.SetData(m_ReGIRParameterUploadData, 0, 0, 1);
        }

        private unsafe VividReGIRParameters CreateParameters(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var mode = NormalizeMode(m_Mode);
            var cellSize = ResolveCellSize(mode);
            var center = ResolveCenter(cameraData, mode, cellSize);
            var cellCount = mode == VividReGIRMode.Onion
                ? ComputeOnionCellCount(DefaultOnionDetailLayerGroups, DefaultOnionCoverageLayers)
                : ComputeGridCellCount(DefaultGridSizeX, DefaultGridSizeY, DefaultGridSizeZ);
            var slotCount = ComputeSlotCount(cellCount, DefaultLightsPerCell);

            var parameters = new VividReGIRParameters
            {
                centerWS = center,
                cellSize = cellSize,
                gridSizeX = DefaultGridSizeX,
                gridSizeY = DefaultGridSizeY,
                gridSizeZ = DefaultGridSizeZ,
                lightsPerCell = DefaultLightsPerCell,
                lightCount = (uint)Mathf.Max(m_ReGIRLightCount, 0),
                slotCount = (uint)Mathf.Max(slotCount, 0),
                buildSamples = DefaultBuildSamples,
                samplingJitter = ResolveSamplingJitter(mode),
                frameIndex = ResolveFrameIndex(cameraData),
                mode = mode,
            };

            if (mode == VividReGIRMode.Onion)
                BuildOnionParameters(ref parameters, DefaultOnionDetailLayerGroups, DefaultOnionCoverageLayers, cellSize);

            return parameters;
        }

        private static Vector3 ResolveCenter(VividCameraData cameraData, VividReGIRMode mode, float cellSize)
        {
            var center = Vector3.zero;
            if (cameraData?.camera != null)
                center = cameraData.camera.transform.position;
            else if (cameraData != null)
                center = cameraData.inverseViewMatrix.GetColumn(3);

            if (mode == VividReGIRMode.Onion)
                return center;

            var alignment = Mathf.Max(cellSize, 1e-5f);
            return new Vector3(
                Mathf.Floor(center.x / alignment) * alignment,
                Mathf.Floor(center.y / alignment) * alignment,
                Mathf.Floor(center.z / alignment) * alignment);
        }

        private static uint ResolveFrameIndex(VividCameraData cameraData)
        {
            if (cameraData != null && cameraData.frameIndex >= 0)
                return (uint)cameraData.frameIndex;

            return (uint)Mathf.Max(Time.frameCount, 0);
        }

        private static int ComputeSlotCount(int gridSizeX, int gridSizeY, int gridSizeZ, int lightsPerCell)
        {
            return ComputeSlotCount(
                ComputeGridCellCount(gridSizeX, gridSizeY, gridSizeZ),
                lightsPerCell);
        }

        internal static int ComputeGridCellCount(int gridSizeX, int gridSizeY, int gridSizeZ)
        {
            var cellCount = (long)Mathf.Max(gridSizeX, 1)
                * Mathf.Max(gridSizeY, 1)
                * Mathf.Max(gridSizeZ, 1);
            return Mathf.Max(1, (int)Math.Min(cellCount, int.MaxValue));
        }

        internal static int ComputeOnionCellCount(int detailLayerGroups, int coverageLayers)
        {
            detailLayerGroups = Mathf.Clamp(detailLayerGroups, 1, VividReGIRParameters.OnionMaxLayerGroups);
            coverageLayers = Mathf.Max(0, coverageLayers);

            var totalCells = 1;
            for (var layerGroupIndex = 0; layerGroupIndex < detailLayerGroups; layerGroupIndex++)
            {
                var partitions = layerGroupIndex * 4 + 8;
                var layerCount = layerGroupIndex < detailLayerGroups - 1
                    ? 1
                    : coverageLayers + 1;
                totalCells += ComputeOnionCellsPerLayer(partitions) * layerCount;
            }

            return Mathf.Max(1, totalCells);
        }

        private static int ComputeSlotCount(int cellCount, int lightsPerCell)
        {
            var slotCount = (long)Mathf.Max(cellCount, 1)
                * Mathf.Max(lightsPerCell, 1);
            return Mathf.Max(1, (int)Math.Min(slotCount, int.MaxValue));
        }

        private static VividReGIRMode NormalizeMode(VividReGIRMode mode)
        {
            return mode switch
            {
                VividReGIRMode.Grid => VividReGIRMode.Grid,
                VividReGIRMode.Onion => VividReGIRMode.Onion,
                _ => DefaultMode,
            };
        }

        private static float ResolveCellSize(VividReGIRMode mode)
        {
            return mode == VividReGIRMode.Onion
                ? DefaultCellSize * 0.5f
                : DefaultCellSize;
        }

        private static float ResolveSamplingJitter(VividReGIRMode mode)
        {
            return mode == VividReGIRMode.Onion
                ? DefaultSamplingJitter * 2.0f
                : DefaultSamplingJitter;
        }

        private static int ComputeOnionCellsPerLayer(int partitions)
        {
            var ringCount = partitions / 4 + 1;
            var cellsPerLayer = partitions;

            for (var ringIndex = 1; ringIndex < ringCount; ringIndex++)
            {
                var ringCellCount = Mathf.Max(1, Mathf.FloorToInt(partitions * Mathf.Cos(ringIndex * (2.0f * Pi / partitions))));
                cellsPerLayer += ringCellCount * 2;
            }

            return cellsPerLayer;
        }

        private static unsafe void BuildOnionParameters(
            ref VividReGIRParameters parameters,
            int detailLayerGroups,
            int coverageLayers,
            float cellSize)
        {
            detailLayerGroups = Mathf.Clamp(detailLayerGroups, 1, VividReGIRParameters.OnionMaxLayerGroups);
            coverageLayers = Mathf.Max(0, coverageLayers);

            var innerRadius = 1.0f;
            var totalCells = 1;
            var ringOffset = 0;
            var layerGroupCount = 0;

            var cubicRootFactors = stackalloc float[VividReGIRParameters.OnionMaxLayerGroups];
            var cubicRootFactorCount = 0;
            var linearFactorSum = 0.0f;
            var linearFactorCount = 0;

            for (var layerGroupIndex = 0; layerGroupIndex < detailLayerGroups; layerGroupIndex++)
            {
                var partitions = layerGroupIndex * 4 + 8;
                var layerCount = layerGroupIndex < detailLayerGroups - 1
                    ? 1
                    : coverageLayers + 1;
                var radiusRatio = (partitions + Pi) / (partitions - Pi);
                var outerRadius = innerRadius * Mathf.Pow(radiusRatio, layerCount);
                var equatorialAngle = 2.0f * Pi / partitions;
                var ringCount = partitions / 4 + 1;
                var cellsPerLayer = WriteOnionRings(ref parameters, partitions, equatorialAngle, ringOffset, ringCount);

                parameters.onionLayerInnerRadius[layerGroupIndex] = innerRadius * cellSize;
                parameters.onionLayerOuterRadius[layerGroupIndex] = outerRadius * cellSize;
                parameters.onionLayerInvLogLayerScale[layerGroupIndex] = 1.0f / Mathf.Log(radiusRatio);
                parameters.onionLayerCount[layerGroupIndex] = (uint)layerCount;
                parameters.onionLayerInvEquatorialCellAngle[layerGroupIndex] = 1.0f / equatorialAngle;
                parameters.onionLayerCellsPerLayer[layerGroupIndex] = (uint)cellsPerLayer;
                parameters.onionLayerRingOffset[layerGroupIndex] = (uint)ringOffset;
                parameters.onionLayerRingCount[layerGroupIndex] = (uint)ringCount;
                parameters.onionLayerEquatorialCellAngle[layerGroupIndex] = equatorialAngle;
                parameters.onionLayerScale[layerGroupIndex] = radiusRatio;
                parameters.onionLayerCellOffset[layerGroupIndex] = (uint)totalCells;

                AccumulateOnionJitterFactors(
                    parameters,
                    layerCount,
                    ringOffset,
                    ringCount,
                    innerRadius,
                    radiusRatio,
                    equatorialAngle,
                    layerGroupIndex < detailLayerGroups - 1,
                    cubicRootFactors,
                    ref cubicRootFactorCount,
                    ref linearFactorSum,
                    ref linearFactorCount);

                totalCells += cellsPerLayer * layerCount;
                innerRadius = outerRadius;
                ringOffset += ringCount;
                layerGroupCount++;
            }

            parameters.onionCellCount = (uint)Mathf.Max(totalCells, 1);
            parameters.onionLayerGroupCount = (uint)layerGroupCount;
            parameters.onionRingCount = (uint)ringOffset;
            parameters.onionCubicRootFactor = ResolveMedian(cubicRootFactors, cubicRootFactorCount);
            parameters.onionLinearFactor = linearFactorCount > 0
                ? linearFactorSum / linearFactorCount
                : 0.0f;
        }

        private static unsafe int WriteOnionRings(
            ref VividReGIRParameters parameters,
            int partitions,
            float equatorialAngle,
            int ringOffset,
            int ringCount)
        {
            WriteOnionRing(ref parameters, ringOffset, partitions, 0);
            var cellsPerLayer = partitions;

            for (var ringIndex = 1; ringIndex < ringCount; ringIndex++)
            {
                var cellCount = Mathf.Max(1, Mathf.FloorToInt(partitions * Mathf.Cos(ringIndex * equatorialAngle)));
                WriteOnionRing(ref parameters, ringOffset + ringIndex, cellCount, cellsPerLayer);
                cellsPerLayer += cellCount * 2;
            }

            return cellsPerLayer;
        }

        private static unsafe void WriteOnionRing(
            ref VividReGIRParameters parameters,
            int ringIndex,
            int cellCount,
            int cellOffset)
        {
            if (ringIndex >= VividReGIRParameters.OnionMaxRings)
                return;

            var invCellAngle = cellCount / (2.0f * Pi);
            parameters.onionRingCellAngle[ringIndex] = 1.0f / invCellAngle;
            parameters.onionRingInvCellAngle[ringIndex] = invCellAngle;
            parameters.onionRingCellOffset[ringIndex] = (uint)cellOffset;
            parameters.onionRingCellCount[ringIndex] = (uint)cellCount;
        }

        private static unsafe void AccumulateOnionJitterFactors(
            VividReGIRParameters parameters,
            int layerCount,
            int ringOffset,
            int ringCount,
            float innerRadius,
            float layerScale,
            float equatorialAngle,
            bool useCubicRoot,
            float* cubicRootFactors,
            ref int cubicRootFactorCount,
            ref float linearFactorSum,
            ref int linearFactorCount)
        {
            for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                var layerInnerRadius = innerRadius * Mathf.Pow(layerScale, layerIndex);
                var layerOuterRadius = layerInnerRadius * layerScale;
                var middleRadius = (layerInnerRadius + layerOuterRadius) * 0.5f;
                var maxCellRadius = 0.0f;

                for (var ringIndex = 0; ringIndex < ringCount; ringIndex++)
                {
                    var globalRingIndex = ringOffset + ringIndex;
                    var middleElevation = equatorialAngle * ringIndex;
                    var vertexElevation = ringIndex == 0
                        ? equatorialAngle * 0.5f
                        : middleElevation - equatorialAngle * 0.5f;
                    var cellRadius = Vector3.Distance(
                        SphericalToCartesian(middleRadius, 0.0f, middleElevation),
                        SphericalToCartesian(layerOuterRadius, parameters.onionRingCellAngle[globalRingIndex], vertexElevation));
                    maxCellRadius = Mathf.Max(maxCellRadius, cellRadius);
                }

                if (useCubicRoot)
                {
                    if (cubicRootFactorCount < VividReGIRParameters.OnionMaxLayerGroups)
                        cubicRootFactors[cubicRootFactorCount++] = maxCellRadius * Mathf.Pow(Mathf.Max(middleRadius, 1e-5f), -1.0f / 3.0f);
                }
                else
                {
                    linearFactorSum += maxCellRadius / Mathf.Max(middleRadius, 1e-5f);
                    linearFactorCount++;
                }
            }
        }

        private static Vector3 SphericalToCartesian(float radius, float azimuth, float elevation)
        {
            var cosElevation = Mathf.Cos(elevation);
            return new Vector3(
                radius * Mathf.Cos(azimuth) * cosElevation,
                radius * Mathf.Sin(elevation),
                radius * Mathf.Sin(azimuth) * cosElevation);
        }

        private static unsafe float ResolveMedian(float* values, int count)
        {
            if (count <= 0)
                return 0.0f;

            for (var i = 1; i < count; i++)
            {
                var value = values[i];
                var j = i - 1;
                while (j >= 0 && values[j] > value)
                {
                    values[j + 1] = values[j];
                    j--;
                }

                values[j + 1] = value;
            }

            return values[count / 2];
        }

        private static void ResizeStructuredBuffer(RenderGraphBuffer buffer, int count, int stride)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = Mathf.Max(1, count);
            buffer.desc.Stride = stride;
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
        }

        private void EnsureImportedBuffers()
        {
            m_ReGIRLightBuffer?.EnsureImportedBuffer();
            m_ReGIRParameterBuffer?.EnsureImportedBuffer();
            m_ReGIRReservoirBuffer?.EnsureImportedBuffer();
        }

        private void ReleaseImportedBuffers()
        {
            m_ReGIRLightBuffer?.ClearImportedBuffer();
            m_ReGIRParameterBuffer?.ClearImportedBuffer();
            m_ReGIRReservoirBuffer?.ClearImportedBuffer();
        }

        private static void UploadDefault<T>(RenderGraphBuffer buffer, ref NativeArray<T> uploadData)
            where T : struct
        {
            if (buffer == null)
                return;

            EnsureNativeUploadCapacity(ref uploadData, 1);
            uploadData[0] = default;
            buffer.SetData(uploadData, 0, 0, 1);
        }

        private static void EnsureNativeUploadCapacity<T>(ref NativeArray<T> uploadData, int requiredCapacity)
            where T : struct
        {
            requiredCapacity = Mathf.Max(1, requiredCapacity);
            if (uploadData.IsCreated && uploadData.Length >= requiredCapacity)
                return;

            DisposeNativeUploadData(ref uploadData);
            uploadData = new NativeArray<T>(requiredCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void DisposeNativeUploadData<T>(ref NativeArray<T> uploadData)
            where T : struct
        {
            if (!uploadData.IsCreated)
                return;

            uploadData.Dispose();
            uploadData = default;
        }
    }
}
