using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class LightGridPass : UnsafePass
    {
        internal const int ClusterTileSize = 32;
        internal const int ClusterBigTileSize = 64;
        internal const int ClusterSliceCount = 24;
        internal const int ClusterMaxLightsPerCluster = 64;

        private const int ClusterBuildGroupSizeX = 8;
        private const int ClusterBuildGroupSizeY = 8;

        private static readonly int DirectionalLightsId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId = Shader.PropertyToID("_MainDirectionalLightIndex");
        private static readonly int PunctualLightsId = Shader.PropertyToID("_PunctualLights");
        private static readonly int PunctualLightCullDataId = Shader.PropertyToID("_PunctualLightCullData");
        private static readonly int PunctualLightScreenSpaceBoundsId = Shader.PropertyToID("_PunctualLightScreenSpaceBounds");
        private static readonly int PunctualLightCountId = Shader.PropertyToID("_PunctualLightCount");
        private static readonly int ClusterBigTileLightCountsId = Shader.PropertyToID("_ClusterBigTileLightCounts");
        private static readonly int ClusterBigTileLightRangesId = Shader.PropertyToID("_ClusterBigTileLightRanges");
        private static readonly int ClusterBigTileLightIndicesId = Shader.PropertyToID("_ClusterBigTileLightIndices");
        private static readonly int ClusterLightGridId = Shader.PropertyToID("_ClusterLightGrid");
        private static readonly int ClusterLightIndicesId = Shader.PropertyToID("_ClusterLightIndices");
        private static readonly int ClusterAllocationCounterId = Shader.PropertyToID("_ClusterAllocationCounter");
        private static readonly int ClusterTileSizeId = Shader.PropertyToID("_ClusterTileSize");
        private static readonly int ClusterBigTileSizeId = Shader.PropertyToID("_ClusterBigTileSize");
        private static readonly int ClusterSliceCountId = Shader.PropertyToID("_ClusterSliceCount");
        private static readonly int ClusterScreenWidthId = Shader.PropertyToID("_ClusterScreenWidth");
        private static readonly int ClusterScreenHeightId = Shader.PropertyToID("_ClusterScreenHeight");
        private static readonly int ClusterTileCountXId = Shader.PropertyToID("_ClusterTileCountX");
        private static readonly int ClusterTileCountYId = Shader.PropertyToID("_ClusterTileCountY");
        private static readonly int ClusterBigTileCountXId = Shader.PropertyToID("_ClusterBigTileCountX");
        private static readonly int ClusterBigTileCountYId = Shader.PropertyToID("_ClusterBigTileCountY");
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

        private static readonly VividLightData.PunctualLightScreenSpaceBounds[] s_EmptyPunctualLightScreenSpaceBounds =
        {
            default
        };

        private static readonly VividLightData.PunctualLightCoarseRange[] s_EmptyClusterBigTileLightRanges =
        {
            default
        };

        private static readonly uint[] s_EmptyClusterBigTileLightIndices =
        {
            0u
        };

        private static readonly uint[] s_EmptyClusterBigTileLightCounts =
        {
            0u
        };

        private static readonly uint[] s_ZeroCounterData =
        {
            0u
        };

        private GraphicsBuffer m_DirectionalLightBuffer;
        private GraphicsBuffer m_PunctualLightBuffer;
        private GraphicsBuffer m_PunctualLightCullBuffer;
        private GraphicsBuffer m_PunctualLightScreenSpaceBoundsBuffer;
        private GraphicsBuffer m_ClusterBigTileLightCountsBuffer;
        private GraphicsBuffer m_ClusterBigTileLightRangesBuffer;
        private GraphicsBuffer m_ClusterBigTileLightIndicesBuffer;
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
        private int m_ClusterBigTileCountX;
        private int m_ClusterBigTileCountY;
        private int m_ClusterLightIndexCapacity;
        private int m_ClusterBigTileLightIndexCapacity;
        private float m_ClusterNearClip;
        private float m_ClusterFarClip;
        private float m_ClusterLogDepthScale;
        private float m_ClusterLinearDepthScale;
        private float m_ClusterTanHalfFovX;
        private float m_ClusterTanHalfFovY;
        private float m_ClusterOrthoHalfWidth;
        private float m_ClusterOrthoHalfHeight;
        private int m_ClusterIsOrthographic;
        private int m_ClearClusterLightCounterKernel = -1;
        private int m_CountClusterBigTileLightsKernel = -1;
        private int m_BuildClusterBigTileLightRangesKernel = -1;
        private int m_BuildClusterBigTileLightListKernel = -1;
        private int m_BuildClusteredLightListKernel = -1;

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var lightData = frameData.GetOrCreate<VividLightData>();
            var camera = cameraData.camera;

            m_DirectionalLightCount = lightData.directionalLightCount;
            m_PunctualLightCount = lightData.punctualLightCount;
            m_MainDirectionalLightIndex = lightData.mainDirectionalLightIndex;

            EnsureDirectionalLightBuffer(Mathf.Max(lightData.directionalLightCount, 1));

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
                ClusterBigTileSize,
                ClusterSliceCount,
                m_PunctualLightCount,
                ClusterMaxLightsPerCluster);
            m_ClusterTileCountX = clusteredLightListParameters.tileCountX;
            m_ClusterTileCountY = clusteredLightListParameters.tileCountY;
            m_ClusterBigTileCountX = clusteredLightListParameters.bigTileCountX;
            m_ClusterBigTileCountY = clusteredLightListParameters.bigTileCountY;
            m_ClusterLightIndexCapacity = clusteredLightListParameters.lightIndexCapacity;
            m_ClusterBigTileLightIndexCapacity = 1;

            EnsurePunctualLightBuffer(Mathf.Max(m_PunctualLightCount, 1));
            EnsurePunctualLightCullBuffer(Mathf.Max(m_PunctualLightCount, 1));
            EnsurePunctualLightScreenSpaceBoundsBuffer(Mathf.Max(m_PunctualLightCount, 1));
            EnsureClusterBigTileLightCountsBuffer(m_PunctualLightCount > 0 ? Mathf.Max(clusteredLightListParameters.bigTileCount, 1) : 1);
            EnsureClusterBigTileLightRangesBuffer(m_PunctualLightCount > 0 ? Mathf.Max(clusteredLightListParameters.bigTileCount, 1) : 1);
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
                lightData.UpdatePunctualLightClusteredCullData(clusteredLightListParameters.screenSpaceBoundsParameters);
                m_ClusterBigTileLightIndexCapacity = lightData.GetPunctualLightBigTileLightIndexCapacityEstimate();
                EnsureClusterBigTileLightIndicesBuffer(Mathf.Max(m_ClusterBigTileLightIndexCapacity, 1));
                m_PunctualLightBuffer.SetData(lightData.punctualLights, 0, 0, m_PunctualLightCount);
                m_PunctualLightCullBuffer.SetData(lightData.punctualLightViewSpaceCullData, 0, 0, m_PunctualLightCount);
                m_PunctualLightScreenSpaceBoundsBuffer.SetData(lightData.punctualLightScreenSpaceBounds, 0, 0, m_PunctualLightCount);
            }
            else
            {
                EnsureClusterBigTileLightIndicesBuffer(1);
                m_PunctualLightBuffer.SetData(s_EmptyPunctualLights);
                m_PunctualLightCullBuffer.SetData(s_EmptyPunctualLightCullData);
                m_PunctualLightScreenSpaceBoundsBuffer.SetData(s_EmptyPunctualLightScreenSpaceBounds);
                m_ClusterBigTileLightCountsBuffer.SetData(s_EmptyClusterBigTileLightCounts);
                m_ClusterBigTileLightRangesBuffer.SetData(s_EmptyClusterBigTileLightRanges);
                m_ClusterBigTileLightIndicesBuffer.SetData(s_EmptyClusterBigTileLightIndices);
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
                m_CountClusterBigTileLightsKernel = m_ClusteredLightCullCompute.FindKernel("CountClusterBigTileLights");
                m_BuildClusterBigTileLightRangesKernel = m_ClusteredLightCullCompute.FindKernel("BuildClusterBigTileLightRanges");
                m_BuildClusterBigTileLightListKernel = m_ClusteredLightCullCompute.FindKernel("BuildClusterBigTileLightList");
                m_BuildClusteredLightListKernel = m_ClusteredLightCullCompute.FindKernel("BuildClusteredLightList");
            }
            catch (ArgumentException)
            {
                Debug.LogWarning($"[VividRP] Could not find clustered light kernels in {m_ClusteredLightCullCompute.name}. Punctual clustered lighting will be disabled.");
                m_ClusteredLightCullCompute = null;
                m_ClearClusterLightCounterKernel = -1;
                m_CountClusterBigTileLightsKernel = -1;
                m_BuildClusterBigTileLightRangesKernel = -1;
                m_BuildClusterBigTileLightListKernel = -1;
                m_BuildClusteredLightListKernel = -1;
            }
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (m_DirectionalLightBuffer == null
                || m_PunctualLightBuffer == null
                || m_PunctualLightCullBuffer == null
                || m_PunctualLightScreenSpaceBoundsBuffer == null
                || m_ClusterBigTileLightCountsBuffer == null
                || m_ClusterBigTileLightRangesBuffer == null
                || m_ClusterBigTileLightIndicesBuffer == null)
            {
                return;
            }

            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            var canBuildClusteredLights = m_PunctualLightCount > 0
                && m_ClusteredLightCullCompute != null
                && m_ClearClusterLightCounterKernel >= 0
                && m_CountClusterBigTileLightsKernel >= 0
                && m_BuildClusterBigTileLightRangesKernel >= 0
                && m_BuildClusterBigTileLightListKernel >= 0
                && m_BuildClusteredLightListKernel >= 0
                && m_PunctualLightCullBuffer != null
                && m_PunctualLightScreenSpaceBoundsBuffer != null
                && m_ClusterBigTileLightCountsBuffer != null
                && m_ClusterBigTileLightRangesBuffer != null
                && m_ClusterBigTileLightIndicesBuffer != null
                && m_ClusterLightGridBuffer != null
                && m_ClusterLightIndicesBuffer != null
                && m_ClusterAllocationCounterBuffer != null;

            if (canBuildClusteredLights)
            {
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, PunctualLightCountId, m_PunctualLightCount);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterTileSizeId, ClusterTileSize);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterBigTileSizeId, ClusterBigTileSize);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterSliceCountId, ClusterSliceCount);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterScreenWidthId, m_LightingWidth);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterScreenHeightId, m_LightingHeight);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterTileCountXId, m_ClusterTileCountX);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterTileCountYId, m_ClusterTileCountY);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterBigTileCountXId, m_ClusterBigTileCountX);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterBigTileCountYId, m_ClusterBigTileCountY);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterNearClipId, m_ClusterNearClip);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterFarClipId, m_ClusterFarClip);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterLogDepthScaleId, m_ClusterLogDepthScale);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterLinearDepthScaleId, m_ClusterLinearDepthScale);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterTanHalfFovXId, m_ClusterTanHalfFovX);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterTanHalfFovYId, m_ClusterTanHalfFovY);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterOrthoHalfWidthId, m_ClusterOrthoHalfWidth);
                cmd.SetComputeFloatParam(m_ClusteredLightCullCompute, ClusterOrthoHalfHeightId, m_ClusterOrthoHalfHeight);
                cmd.SetComputeIntParam(m_ClusteredLightCullCompute, ClusterIsOrthographicId, m_ClusterIsOrthographic);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_CountClusterBigTileLightsKernel, PunctualLightScreenSpaceBoundsId, m_PunctualLightScreenSpaceBoundsBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_CountClusterBigTileLightsKernel, ClusterBigTileLightCountsId, m_ClusterBigTileLightCountsBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusterBigTileLightRangesKernel, ClusterBigTileLightCountsId, m_ClusterBigTileLightCountsBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusterBigTileLightRangesKernel, ClusterBigTileLightRangesId, m_ClusterBigTileLightRangesBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusterBigTileLightListKernel, PunctualLightScreenSpaceBoundsId, m_PunctualLightScreenSpaceBoundsBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusterBigTileLightListKernel, ClusterBigTileLightRangesId, m_ClusterBigTileLightRangesBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusterBigTileLightListKernel, ClusterBigTileLightIndicesId, m_ClusterBigTileLightIndicesBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, PunctualLightCullDataId, m_PunctualLightCullBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, PunctualLightScreenSpaceBoundsId, m_PunctualLightScreenSpaceBoundsBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, ClusterBigTileLightRangesId, m_ClusterBigTileLightRangesBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, ClusterBigTileLightIndicesId, m_ClusterBigTileLightIndicesBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, ClusterLightGridId, m_ClusterLightGridBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, ClusterLightIndicesId, m_ClusterLightIndicesBuffer);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_BuildClusteredLightListKernel, ClusterAllocationCounterId, m_ClusterAllocationCounterBuffer);
                cmd.DispatchCompute(
                    m_ClusteredLightCullCompute,
                    m_CountClusterBigTileLightsKernel,
                    Mathf.CeilToInt(m_ClusterBigTileCountX / (float)ClusterBuildGroupSizeX),
                    Mathf.CeilToInt(m_ClusterBigTileCountY / (float)ClusterBuildGroupSizeY),
                    1);
                cmd.DispatchCompute(m_ClusteredLightCullCompute, m_BuildClusterBigTileLightRangesKernel, 1, 1, 1);
                cmd.DispatchCompute(
                    m_ClusteredLightCullCompute,
                    m_BuildClusterBigTileLightListKernel,
                    Mathf.CeilToInt(m_ClusterBigTileCountX / (float)ClusterBuildGroupSizeX),
                    Mathf.CeilToInt(m_ClusterBigTileCountY / (float)ClusterBuildGroupSizeY),
                    1);
                cmd.SetComputeBufferParam(m_ClusteredLightCullCompute, m_ClearClusterLightCounterKernel, ClusterAllocationCounterId, m_ClusterAllocationCounterBuffer);
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
            m_PunctualLightScreenSpaceBoundsBuffer?.Dispose();
            m_ClusterBigTileLightCountsBuffer?.Dispose();
            m_ClusterBigTileLightRangesBuffer?.Dispose();
            m_ClusterBigTileLightIndicesBuffer?.Dispose();
            m_ClusterLightGridBuffer?.Dispose();
            m_ClusterLightIndicesBuffer?.Dispose();
            m_ClusterAllocationCounterBuffer?.Dispose();
            m_DirectionalLightBuffer = null;
            m_PunctualLightBuffer = null;
            m_PunctualLightCullBuffer = null;
            m_PunctualLightScreenSpaceBoundsBuffer = null;
            m_ClusterBigTileLightCountsBuffer = null;
            m_ClusterBigTileLightRangesBuffer = null;
            m_ClusterBigTileLightIndicesBuffer = null;
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
            m_ClusterBigTileCountX = 0;
            m_ClusterBigTileCountY = 0;
            m_ClusterLightIndexCapacity = 0;
            m_ClusterBigTileLightIndexCapacity = 0;
            m_ClusterNearClip = 0.0f;
            m_ClusterFarClip = 0.0f;
            m_ClusterLogDepthScale = 0.0f;
            m_ClusterLinearDepthScale = 0.0f;
            m_ClusterTanHalfFovX = 0.0f;
            m_ClusterTanHalfFovY = 0.0f;
            m_ClusterOrthoHalfWidth = 0.0f;
            m_ClusterOrthoHalfHeight = 0.0f;
            m_ClusterIsOrthographic = 0;
            m_ClearClusterLightCounterKernel = -1;
            m_CountClusterBigTileLightsKernel = -1;
            m_BuildClusterBigTileLightRangesKernel = -1;
            m_BuildClusterBigTileLightListKernel = -1;
            m_BuildClusteredLightListKernel = -1;
        }

        private void EnsureDirectionalLightBuffer(int requiredBufferCount)
        {
            if (m_DirectionalLightBuffer != null
                && m_DirectionalLightBuffer.count >= requiredBufferCount
                && m_DirectionalLightBuffer.stride == VividLightData.DirectionalLightData.Stride)
            {
                return;
            }

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
            {
                return;
            }

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
            {
                return;
            }

            m_PunctualLightCullBuffer?.Dispose();
            m_PunctualLightCullBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                VividLightData.PunctualLightViewSpaceCullData.Stride);
        }

        private void EnsurePunctualLightScreenSpaceBoundsBuffer(int requiredBufferCount)
        {
            if (m_PunctualLightScreenSpaceBoundsBuffer != null
                && m_PunctualLightScreenSpaceBoundsBuffer.count >= requiredBufferCount
                && m_PunctualLightScreenSpaceBoundsBuffer.stride == VividLightData.PunctualLightScreenSpaceBounds.Stride)
            {
                return;
            }

            m_PunctualLightScreenSpaceBoundsBuffer?.Dispose();
            m_PunctualLightScreenSpaceBoundsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                VividLightData.PunctualLightScreenSpaceBounds.Stride);
        }

        private void EnsureClusterBigTileLightRangesBuffer(int requiredBufferCount)
        {
            if (m_ClusterBigTileLightRangesBuffer != null
                && m_ClusterBigTileLightRangesBuffer.count >= requiredBufferCount
                && m_ClusterBigTileLightRangesBuffer.stride == VividLightData.PunctualLightCoarseRange.Stride)
            {
                return;
            }

            m_ClusterBigTileLightRangesBuffer?.Dispose();
            m_ClusterBigTileLightRangesBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                VividLightData.PunctualLightCoarseRange.Stride);
        }

        private void EnsureClusterBigTileLightCountsBuffer(int requiredBufferCount)
        {
            if (m_ClusterBigTileLightCountsBuffer != null
                && m_ClusterBigTileLightCountsBuffer.count >= requiredBufferCount
                && m_ClusterBigTileLightCountsBuffer.stride == sizeof(uint))
            {
                return;
            }

            m_ClusterBigTileLightCountsBuffer?.Dispose();
            m_ClusterBigTileLightCountsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                sizeof(uint));
        }

        private void EnsureClusterBigTileLightIndicesBuffer(int requiredBufferCount)
        {
            if (m_ClusterBigTileLightIndicesBuffer != null
                && m_ClusterBigTileLightIndicesBuffer.count >= requiredBufferCount
                && m_ClusterBigTileLightIndicesBuffer.stride == sizeof(uint))
            {
                return;
            }

            m_ClusterBigTileLightIndicesBuffer?.Dispose();
            m_ClusterBigTileLightIndicesBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                sizeof(uint));
        }

        private void EnsureClusterLightGridBuffer(int requiredClusterCount)
        {
            if (m_ClusterLightGridBuffer != null
                && m_ClusterLightGridBuffer.count >= requiredClusterCount
                && m_ClusterLightGridBuffer.stride == sizeof(uint) * 2)
            {
                return;
            }

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
            {
                return;
            }

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
            {
                return;
            }

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
