using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public sealed class ReGIRGridBuildPass : ComputePass
    {
        internal const int DefaultGridSizeX = 16;
        internal const int DefaultGridSizeY = 16;
        internal const int DefaultGridSizeZ = 16;
        internal const int DefaultLightsPerCell = 64;
        internal const float DefaultCellSize = 1.0f;
        internal const int DefaultBuildSamples = 8;
        internal const float DefaultSamplingJitter = 1.0f;

        private const string KernelName = "ReGIRGridBuild";
        private const int ThreadGroupSize = 256;

        private static readonly int ReGIRLightsId = Shader.PropertyToID("_ReGIRLights");
        private static readonly int ReGIRParametersId = Shader.PropertyToID("_ReGIRParameters");
        private static readonly int ReGIRReservoirsId = Shader.PropertyToID("_ReGIRReservoirs");

        [RenderGraphResource(Name = "ReGIRLights", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_ReGIRLightBuffer;

        [RenderGraphResource(Name = "ReGIRParameters", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_ReGIRParameterBuffer;

        [RenderGraphResource(Name = "ReGIRReservoirs", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_ReGIRReservoirBuffer;

        private ComputeShader m_ReGIRGridBuildCompute;
        private NativeArray<VividReGIRLightData> m_ReGIRLightUploadData;
        private NativeArray<VividReGIRParameters> m_ReGIRParameterUploadData;
        private int m_Kernel = -1;
        private int m_ReGIRLightCount;
        private int m_ReGIRSlotCount;
        private int m_DispatchGroupCount;

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
            m_ReGIRSlotCount = ComputeSlotCount(DefaultGridSizeX, DefaultGridSizeY, DefaultGridSizeZ, DefaultLightsPerCell);
            m_DispatchGroupCount = Mathf.Max(1, CoreUtils.DivRoundUp(m_ReGIRSlotCount, ThreadGroupSize));

            ResizeStructuredBuffer(m_ReGIRLightBuffer, Mathf.Max(m_ReGIRLightCount, 1), VividReGIRLightData.Stride);
            ResizeStructuredBuffer(m_ReGIRParameterBuffer, 1, VividReGIRParameters.Stride);
            ResizeStructuredBuffer(m_ReGIRReservoirBuffer, Mathf.Max(m_ReGIRSlotCount, 1), VividReGIRReservoir.Stride);
            EnsureImportedBuffers();

            UploadReGIRLights(lightData);
            UploadReGIRParameters(frameData);
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

        private void UploadReGIRParameters(ContextContainer frameData)
        {
            EnsureNativeUploadCapacity(ref m_ReGIRParameterUploadData, 1);
            m_ReGIRParameterUploadData[0] = CreateParameters(frameData);
            m_ReGIRParameterBuffer.SetData(m_ReGIRParameterUploadData, 0, 0, 1);
        }

        private VividReGIRParameters CreateParameters(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var center = ResolveGridCenter(cameraData, DefaultCellSize);

            return new VividReGIRParameters
            {
                centerWS = center,
                cellSize = DefaultCellSize,
                gridSizeX = DefaultGridSizeX,
                gridSizeY = DefaultGridSizeY,
                gridSizeZ = DefaultGridSizeZ,
                lightsPerCell = DefaultLightsPerCell,
                lightCount = (uint)Mathf.Max(m_ReGIRLightCount, 0),
                slotCount = (uint)Mathf.Max(m_ReGIRSlotCount, 0),
                buildSamples = DefaultBuildSamples,
                samplingJitter = DefaultSamplingJitter,
                frameIndex = ResolveFrameIndex(cameraData),
            };
        }

        private static Vector3 ResolveGridCenter(VividCameraData cameraData, float cellSize)
        {
            var center = Vector3.zero;
            if (cameraData?.camera != null)
                center = cameraData.camera.transform.position;
            else if (cameraData != null)
                center = cameraData.inverseViewMatrix.GetColumn(3);

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
            var slotCount = (long)Mathf.Max(gridSizeX, 1)
                * Mathf.Max(gridSizeY, 1)
                * Mathf.Max(gridSizeZ, 1)
                * Mathf.Max(lightsPerCell, 1);
            return Mathf.Max(1, (int)Math.Min(slotCount, int.MaxValue));
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
