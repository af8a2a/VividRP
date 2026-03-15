using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class LightGridPass : UnsafePass
    {
        internal const int ClusterTileSize = 32;
        internal const int ClusterSliceCount = 24;
        internal const int ClusterMaxLightsPerCluster = 64;

        private const int ClusterBuildGroupSizeX = 8;
        private const int ClusterBuildGroupSizeY = 8;

        private static readonly int DirectionalLightsId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId = Shader.PropertyToID("_MainDirectionalLightIndex");
        private static readonly int PunctualLightsId = Shader.PropertyToID("_PunctualLights");
        private static readonly int PunctualLightCullDataId = Shader.PropertyToID("_PunctualLightCullData");
        private static readonly int PunctualLightCountId = Shader.PropertyToID("_PunctualLightCount");
        private static readonly int ClusterCoarseLightRangesId = Shader.PropertyToID("_ClusterCoarseLightRanges");
        private static readonly int ClusterCoarseLightRecordsId = Shader.PropertyToID("_ClusterCoarseLightRecords");
        private static readonly int ClusterLightGridId = Shader.PropertyToID("_ClusterLightGrid");
        private static readonly int ClusterLightIndicesId = Shader.PropertyToID("_ClusterLightIndices");
        private static readonly int ClusterAllocationCounterId = Shader.PropertyToID("_ClusterAllocationCounter");
        private static readonly int ClusterTileSizeId = Shader.PropertyToID("_ClusterTileSize");
        private static readonly int ClusterSliceCountId = Shader.PropertyToID("_ClusterSliceCount");
        private static readonly int ClusterScreenWidthId = Shader.PropertyToID("_ClusterScreenWidth");
        private static readonly int ClusterScreenHeightId = Shader.PropertyToID("_ClusterScreenHeight");
        private static readonly int ClusterTileCountXId = Shader.PropertyToID("_ClusterTileCountX");
        private static readonly int ClusterTileCountYId = Shader.PropertyToID("_ClusterTileCountY");
        private static readonly int ClusterNearClipId = Shader.PropertyToID("_ClusterNearClip");
        private static readonly int ClusterFarClipId = Shader.PropertyToID("_ClusterFarClip");
        private static readonly int ClusterLogDepthScaleId = Shader.PropertyToID("_ClusterLogDepthScale");
        private static readonly int ClusterLinearDepthScaleId = Shader.PropertyToID("_ClusterLinearDepthScale");
        private static readonly int ClusterTanHalfFovXId = Shader.PropertyToID("_ClusterTanHalfFovX");
        private static readonly int ClusterTanHalfFovYId = Shader.PropertyToID("_ClusterTanHalfFovY");
        private static readonly int ClusterOrthoHalfWidthId = Shader.PropertyToID("_ClusterOrthoHalfWidth");
        private static readonly int ClusterOrthoHalfHeightId = Shader.PropertyToID("_ClusterOrthoHalfHeight");
        private static readonly int ClusterIsOrthographicId = Shader.PropertyToID("_ClusterIsOrthographic");

        private static readonly VividLightData.DirectionalLightData[] s_EmptyDirectionalLights =
        {
            default
        };

        private static readonly VividLightData.PunctualLightData[] s_EmptyPunctualLights =
        {
            default
        };

        private static readonly VividLightData.PunctualLightViewSpaceCullData[] s_EmptyPunctualLightCullData =
        {
            default
        };

        private static readonly VividLightData.PunctualLightCoarseRange[] s_EmptyClusterCoarseLightRanges =
        {
            default
        };

        private static readonly VividLightData.PunctualLightCoarseRecord[] s_EmptyClusterCoarseLightRecords =
        {
            default
        };

        private static readonly uint[] s_ZeroCounterData =
        {
            0u
        };

        private GraphicsBuffer m_DirectionalLightBuffer;
        private GraphicsBuffer m_PunctualLightBuffer;
        private GraphicsBuffer m_PunctualLightCullBuffer;
        private GraphicsBuffer m_ClusterCoarseLightRangesBuffer;
        private GraphicsBuffer m_ClusterCoarseLightRecordsBuffer;
        private GraphicsBuffer m_ClusterLightGridBuffer;
        private GraphicsBuffer m_ClusterLightIndicesBuffer;
        private GraphicsBuffer m_ClusterAllocationCounterBuffer;
        private ComputeShader m_ClusteredLightCullCompute;
        private int m_DirectionalLightCount;
        private int m_PunctualLightCount;
        private int m_MainDirectionalLightIndex;
        private int m_LightingWidth;
        private int m_LightingHeight;
        private int m_ClusterTileCountX;
        private int m_ClusterTileCountY;
        private int m_ClusterLightIndexCapacity;
        private float m_ClusterNearClip;
        private float m_ClusterFarClip;
        private float m_ClusterLogDepthScale;
        private float m_ClusterLinearDepthScale;
        private float m_ClusterTanHalfFovX;
        private float m_ClusterTanHalfFovY;
        private float m_ClusterOrthoHalfWidth;
        private float m_ClusterOrthoHalfHeight;
        private int m_ClusterIsOrthographic;
        private int m_ClusterCoarseLightRecordCount;
        private int m_ClearClusterLightCounterKernel = -1;
        private int m_BuildClusteredLightListKernel = -1;

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var lightData = frameData.GetOrCreate<VividLightData>();
            var camera = cameraData.camera;

            m_DirectionalLightCount = lightData.directionalLightCount;
            m_PunctualLightCount = lightData.punctualLightCount;
            m_MainDirectionalLightIndex = lightData.mainDirectionalLightIndex;

            var requiredBufferCount = Mathf.Max(lightData.directionalLightCount, 1);
            EnsureDirectionalLightBuffer(requiredBufferCount);

            m_LightingWidth = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            m_LightingHeight = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (m_LightingWidth <= 0)
                m_LightingWidth = Mathf.Max(1, Screen.width);

            if (m_LightingHeight <= 0)
                m_LightingHeight = Mathf.Max(1, Screen.height);

            var clusteredLightListParameters = VividLightData.CreatePunctualLightClusteredLightListParameters(
                camera,
                m_LightingWidth,
                m_LightingHeight,
                ClusterTileSize,
                ClusterSliceCount,
                m_PunctualLightCount,
                ClusterMaxLightsPerCluster);
            m_ClusterTileCountX = clusteredLightListParameters.tileCountX;
            m_ClusterTileCountY = clusteredLightListParameters.tileCountY;
            m_ClusterLightIndexCapacity = clusteredLightListParameters.lightIndexCapacity;

            EnsurePunctualLightBuffer(Mathf.Max(m_PunctualLightCount, 1));
            EnsurePunctualLightCullBuffer(Mathf.Max(m_PunctualLightCount, 1));
            EnsureClusterLightGridBuffer(clusteredLightListParameters.clusterCount);
            EnsureClusterLightIndicesBuffer(m_ClusterLightIndexCapacity);
            EnsureClusterAllocationCounterBuffer();
            UpdateClusterProjectionData(clusteredLightListParameters.screenSpaceBoundsParameters);

            if (m_DirectionalLightCount > 0)
                m_DirectionalLightBuffer.SetData(lightData.directionalLights, 0, 0, m_DirectionalLightCount);

            else
                m_DirectionalLightBuffer.SetData(s_EmptyDirectionalLights);

            if (m_PunctualLightCount > 0)
            {
                var clusteredLightListBuildResult = lightData.UpdatePunctualLightClusteredLightListData(clusteredLightListParameters);
                m_ClusterCoarseLightRecordCount = clusteredLightListBuildResult.coarseRecordCount;
                m_PunctualLightBuffer.SetData(lightData.punctualLights, 0, 0, m_PunctualLightCount);
                m_PunctualLightCullBuffer.SetData(lightData.punctualLightViewSpaceCullData, 0, 0, m_PunctualLightCount);
                EnsureClusterCoarseLightRangesBuffer(Mathf.Max(clusteredLightListBuildResult.coarseRangeCount, 1));
                EnsureClusterCoarseLightRecordsBuffer(Mathf.Max(m_ClusterCoarseLightRecordCount, 1));
                m_ClusterCoarseLightRangesBuffer.SetData(
                    lightData.punctualLightCoarseRanges,
                    0,
                    0,
                    lightData.punctualLightCoarseRangeCount);

                if (m_ClusterCoarseLightRecordCount > 0)
                    m_ClusterCoarseLightRecordsBuffer.SetData(lightData.punctualLightCoarseRecords, 0, 0, m_ClusterCoarseLightRecordCount);
                else
                    m_ClusterCoarseLightRecordsBuffer.SetData(s_EmptyClusterCoarseLightRecords);
            }
            else
            {
                m_ClusterCoarseLightRecordCount = 0;
                m_PunctualLightBuffer.SetData(s_EmptyPunctualLights);
                m_PunctualLightCullBuffer.SetData(s_EmptyPunctualLightCullData);
                EnsureClusterCoarseLightRangesBuffer(1);
                EnsureClusterCoarseLightRecordsBuffer(1);
                m_ClusterCoarseLightRangesBuffer.SetData(s_EmptyClusterCoarseLightRanges);
                m_ClusterCoarseLightRecordsBuffer.SetData(s_EmptyClusterCoarseLightRecords);
            }

            m_ClusterAllocationCounterBuffer.SetData(s_ZeroCounterData);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ClusteredLightCullCompute = resources?.ClusteredLightCullCompute;

            if (m_ClusteredLightCullCompute == null)
                return;

            try
            {
                m_ClearClusterLightCounterKernel = m_ClusteredLightCullCompute.FindKernel("ClearClusterLightCounter");
                m_BuildClusteredLightListKernel = m_ClusteredLightCullCompute.FindKernel("BuildClusteredLightList");
            }
            catch (ArgumentException)
            {
                Debug.LogWarning($"[VividRP] Could not find clustered light kernels in {m_ClusteredLightCullCompute.name}. Punctual clustered lighting will be disabled.");
                m_ClusteredLightCullCompute = null;
                m_ClearClusterLightCounterKernel = -1;
                m_BuildClusteredLightListKernel = -1;
            }
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (m_DirectionalLightBuffer == null
                || m_PunctualLightBuffer == null
                || m_PunctualLightCullBuffer == null
                || m_ClusterCoarseLightRangesBuffer == null
                || m_ClusterCoarseLightRecordsBuffer == null)
                return;

            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            var canBuildClusteredLights = m_PunctualLightCount > 0
                && m_ClusteredLightCullCompute != null
                && m_ClearClusterLightCounterKernel >= 0
                && m_BuildClusteredLightListKernel >= 0
                && m_PunctualLightCullBuffer != null
                && m_ClusterCoarseLightRangesBuffer != null
                && m_ClusterCoarseLightRecordsBuffer != null
                && m_ClusterLightGridBuffer != null
                && m_ClusterLightIndicesBuffer != null
                && m_ClusterAllocationCounterBuffer != null;

            if (canBuildClusteredLights)
            {
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, PunctualLightCountId, m_PunctualLightCount);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterTileSizeId, ClusterTileSize);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterSliceCountId, ClusterSliceCount);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterScreenWidthId, m_LightingWidth);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterScreenHeightId, m_LightingHeight);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterTileCountXId, m_ClusterTileCountX);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterTileCountYId, m_ClusterTileCountY);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterNearClipId, m_ClusterNearClip);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterFarClipId, m_ClusterFarClip);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterLogDepthScaleId, m_ClusterLogDepthScale);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterLinearDepthScaleId, m_ClusterLinearDepthScale);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterTanHalfFovXId, m_ClusterTanHalfFovX);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterTanHalfFovYId, m_ClusterTanHalfFovY);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterOrthoHalfWidthId, m_ClusterOrthoHalfWidth);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterOrthoHalfHeightId, m_ClusterOrthoHalfHeight);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterIsOrthographicId, m_ClusterIsOrthographic);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_ClearClusterLightCounterKernel, ClusterAllocationCounterId, m_ClusterAllocationCounterBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, PunctualLightCullDataId, m_PunctualLightCullBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, ClusterCoarseLightRangesId, m_ClusterCoarseLightRangesBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, ClusterCoarseLightRecordsId, m_ClusterCoarseLightRecordsBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, ClusterLightGridId, m_ClusterLightGridBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, ClusterLightIndicesId, m_ClusterLightIndicesBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, ClusterAllocationCounterId, m_ClusterAllocationCounterBuffer);
                cmd.DispatchCompute(m_ClusteredLightCullCompute, m_ClearClusterLightCounterKernel, 1, 1, 1);
                cmd.DispatchCompute(
                    m_ClusteredLightCullCompute,
                    m_BuildClusteredLightListKernel,
                    Mathf.CeilToInt(m_ClusterTileCountX / (float)ClusterBuildGroupSizeX),
                    Mathf.CeilToInt(m_ClusterTileCountY / (float)ClusterBuildGroupSizeY),
                    ClusterSliceCount);
            }

            cmd.SetGlobalInt(DirectionalLightCountId, m_DirectionalLightCount);
            cmd.SetGlobalInt(MainDirectionalLightIndexId, m_MainDirectionalLightIndex);
            cmd.SetGlobalInt(PunctualLightCountId, canBuildClusteredLights ? m_PunctualLightCount : 0);
            cmd.SetGlobalInt(ClusterTileSizeId, ClusterTileSize);
            cmd.SetGlobalInt(ClusterSliceCountId, ClusterSliceCount);
            cmd.SetGlobalInt(ClusterTileCountXId, m_ClusterTileCountX);
            cmd.SetGlobalInt(ClusterTileCountYId, m_ClusterTileCountY);
            cmd.SetGlobalInt(ClusterIsOrthographicId, m_ClusterIsOrthographic);
            cmd.SetGlobalFloat(ClusterNearClipId, m_ClusterNearClip);
            cmd.SetGlobalFloat(ClusterFarClipId, m_ClusterFarClip);
            cmd.SetGlobalFloat(ClusterLogDepthScaleId, m_ClusterLogDepthScale);
            cmd.SetGlobalFloat(ClusterLinearDepthScaleId, m_ClusterLinearDepthScale);
            cmd.SetGlobalFloat(ClusterTanHalfFovXId, m_ClusterTanHalfFovX);
            cmd.SetGlobalFloat(ClusterTanHalfFovYId, m_ClusterTanHalfFovY);
            cmd.SetGlobalFloat(ClusterOrthoHalfWidthId, m_ClusterOrthoHalfWidth);
            cmd.SetGlobalFloat(ClusterOrthoHalfHeightId, m_ClusterOrthoHalfHeight);
            cmd.SetGlobalBuffer(DirectionalLightsId, m_DirectionalLightBuffer);
            cmd.SetGlobalBuffer(PunctualLightsId, m_PunctualLightBuffer);
            cmd.SetGlobalBuffer(ClusterLightGridId, m_ClusterLightGridBuffer);
            cmd.SetGlobalBuffer(ClusterLightIndicesId, m_ClusterLightIndicesBuffer);
        }

        public override void Dispose()
        {
            m_DirectionalLightBuffer?.Dispose();
            m_PunctualLightBuffer?.Dispose();
            m_PunctualLightCullBuffer?.Dispose();
            m_ClusterCoarseLightRangesBuffer?.Dispose();
            m_ClusterCoarseLightRecordsBuffer?.Dispose();
            m_ClusterLightGridBuffer?.Dispose();
            m_ClusterLightIndicesBuffer?.Dispose();
            m_ClusterAllocationCounterBuffer?.Dispose();
            m_DirectionalLightBuffer = null;
            m_PunctualLightBuffer = null;
            m_PunctualLightCullBuffer = null;
            m_ClusterCoarseLightRangesBuffer = null;
            m_ClusterCoarseLightRecordsBuffer = null;
            m_ClusterLightGridBuffer = null;
            m_ClusterLightIndicesBuffer = null;
            m_ClusterAllocationCounterBuffer = null;
            m_ClusteredLightCullCompute = null;
            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_MainDirectionalLightIndex = -1;
            m_LightingWidth = 0;
            m_LightingHeight = 0;
            m_ClusterTileCountX = 0;
            m_ClusterTileCountY = 0;
            m_ClusterLightIndexCapacity = 0;
            m_ClusterNearClip = 0.0f;
            m_ClusterFarClip = 0.0f;
            m_ClusterLogDepthScale = 0.0f;
            m_ClusterLinearDepthScale = 0.0f;
            m_ClusterTanHalfFovX = 0.0f;
            m_ClusterTanHalfFovY = 0.0f;
            m_ClusterOrthoHalfWidth = 0.0f;
            m_ClusterOrthoHalfHeight = 0.0f;
            m_ClusterIsOrthographic = 0;
            m_ClusterCoarseLightRecordCount = 0;
            m_ClearClusterLightCounterKernel = -1;
            m_BuildClusteredLightListKernel = -1;
        }

        private void EnsureDirectionalLightBuffer(int requiredBufferCount)
        {
            if (m_DirectionalLightBuffer != null
                && m_DirectionalLightBuffer.count >= requiredBufferCount
                && m_DirectionalLightBuffer.stride == VividLightData.DirectionalLightData.Stride)
                return;

            m_DirectionalLightBuffer?.Dispose();
            m_DirectionalLightBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                VividLightData.DirectionalLightData.Stride);
        }

        private void EnsurePunctualLightBuffer(int requiredBufferCount)
        {
            if (m_PunctualLightBuffer != null
                && m_PunctualLightBuffer.count >= requiredBufferCount
                && m_PunctualLightBuffer.stride == VividLightData.PunctualLightData.Stride)
                return;

            m_PunctualLightBuffer?.Dispose();
            m_PunctualLightBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                VividLightData.PunctualLightData.Stride);
        }

        private void EnsurePunctualLightCullBuffer(int requiredBufferCount)
        {
            if (m_PunctualLightCullBuffer != null
                && m_PunctualLightCullBuffer.count >= requiredBufferCount
                && m_PunctualLightCullBuffer.stride == VividLightData.PunctualLightViewSpaceCullData.Stride)
                return;

            m_PunctualLightCullBuffer?.Dispose();
            m_PunctualLightCullBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                VividLightData.PunctualLightViewSpaceCullData.Stride);
        }

        private void EnsureClusterCoarseLightRangesBuffer(int requiredBufferCount)
        {
            if (m_ClusterCoarseLightRangesBuffer != null
                && m_ClusterCoarseLightRangesBuffer.count >= requiredBufferCount
                && m_ClusterCoarseLightRangesBuffer.stride == VividLightData.PunctualLightCoarseRange.Stride)
            {
                return;
            }

            m_ClusterCoarseLightRangesBuffer?.Dispose();
            m_ClusterCoarseLightRangesBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                VividLightData.PunctualLightCoarseRange.Stride);
        }

        private void EnsureClusterCoarseLightRecordsBuffer(int requiredBufferCount)
        {
            if (m_ClusterCoarseLightRecordsBuffer != null
                && m_ClusterCoarseLightRecordsBuffer.count >= requiredBufferCount
                && m_ClusterCoarseLightRecordsBuffer.stride == VividLightData.PunctualLightCoarseRecord.Stride)
            {
                return;
            }

            m_ClusterCoarseLightRecordsBuffer?.Dispose();
            m_ClusterCoarseLightRecordsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                VividLightData.PunctualLightCoarseRecord.Stride);
        }

        private void EnsureClusterLightGridBuffer(int requiredClusterCount)
        {
            if (m_ClusterLightGridBuffer != null
                && m_ClusterLightGridBuffer.count >= requiredClusterCount
                && m_ClusterLightGridBuffer.stride == sizeof(uint) * 2)
                return;

            m_ClusterLightGridBuffer?.Dispose();
            m_ClusterLightGridBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredClusterCount,
                sizeof(uint) * 2);
        }

        private void EnsureClusterLightIndicesBuffer(int requiredCapacity)
        {
            if (m_ClusterLightIndicesBuffer != null
                && m_ClusterLightIndicesBuffer.count >= requiredCapacity
                && m_ClusterLightIndicesBuffer.stride == sizeof(uint))
                return;

            m_ClusterLightIndicesBuffer?.Dispose();
            m_ClusterLightIndicesBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredCapacity,
                sizeof(uint));
        }

        private void EnsureClusterAllocationCounterBuffer()
        {
            if (m_ClusterAllocationCounterBuffer != null
                && m_ClusterAllocationCounterBuffer.count >= 1
                && m_ClusterAllocationCounterBuffer.stride == sizeof(uint))
                return;

            m_ClusterAllocationCounterBuffer?.Dispose();
            m_ClusterAllocationCounterBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(uint));
        }

        private void UpdateClusterProjectionData(VividLightData.PunctualLightScreenSpaceBoundsParameters projectionParameters)
        {
            m_ClusterNearClip = projectionParameters.nearClip;
            m_ClusterFarClip = projectionParameters.farClip;
            m_ClusterLogDepthScale = projectionParameters.logDepthScale;
            m_ClusterLinearDepthScale = projectionParameters.linearDepthScale;
            m_ClusterTanHalfFovX = projectionParameters.tanHalfFovX;
            m_ClusterTanHalfFovY = projectionParameters.tanHalfFovY;
            m_ClusterOrthoHalfWidth = projectionParameters.orthoHalfWidth;
            m_ClusterOrthoHalfHeight = projectionParameters.orthoHalfHeight;
            m_ClusterIsOrthographic = projectionParameters.isOrthographic;
        }

    }
}
