using System;
using System.Runtime.InteropServices;
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
        private static readonly int PunctualLightCountId = Shader.PropertyToID("_PunctualLightCount");
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

        private static readonly PunctualLightUploadData[] s_EmptyPunctualLights =
        {
            default
        };

        private static readonly uint[] s_ZeroCounterData =
        {
            0u
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct PunctualLightUploadData
        {
            public Vector3 positionWS;
            public float range;
            public Vector3 color;
            public uint lightType;
            public Vector3 directionWS;
            public float angleScale;
            public float angleOffset;
            public float inverseRangeSquared;
            public float shadowStrength;
            public uint renderingLayerMask;
            public Vector3 cullingCenterVS;
            public float cullingRadius;

            public static int Stride => Marshal.SizeOf<PunctualLightUploadData>();
        }

        private GraphicsBuffer m_DirectionalLightBuffer;
        private GraphicsBuffer m_PunctualLightBuffer;
        private GraphicsBuffer m_ClusterLightGridBuffer;
        private GraphicsBuffer m_ClusterLightIndicesBuffer;
        private GraphicsBuffer m_ClusterAllocationCounterBuffer;
        private ComputeShader m_LightingSetupCompute;
        private PunctualLightUploadData[] m_PunctualLights = Array.Empty<PunctualLightUploadData>();
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
            EnsurePunctualLightCapacity(Mathf.Max(lightData.punctualLightCount, 1));

            m_LightingWidth = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            m_LightingHeight = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (m_LightingWidth <= 0)
                m_LightingWidth = Mathf.Max(1, Screen.width);

            if (m_LightingHeight <= 0)
                m_LightingHeight = Mathf.Max(1, Screen.height);

            m_ClusterTileCountX = Mathf.Max(1, Mathf.CeilToInt(m_LightingWidth / (float)ClusterTileSize));
            m_ClusterTileCountY = Mathf.Max(1, Mathf.CeilToInt(m_LightingHeight / (float)ClusterTileSize));

            var clusterCount = Mathf.Max(1, m_ClusterTileCountX * m_ClusterTileCountY * ClusterSliceCount);
            var perClusterLightCapacity = m_PunctualLightCount > 0
                ? Mathf.Min(m_PunctualLightCount, ClusterMaxLightsPerCluster)
                : 1;

            m_ClusterLightIndexCapacity = m_PunctualLightCount > 0
                ? Mathf.Max(1, clusterCount * perClusterLightCapacity)
                : 1;

            EnsurePunctualLightBuffer(Mathf.Max(m_PunctualLightCount, 1));
            EnsureClusterLightGridBuffer(clusterCount);
            EnsureClusterLightIndicesBuffer(m_ClusterLightIndexCapacity);
            EnsureClusterAllocationCounterBuffer();
            UpdateClusterProjectionData(camera);

            if (m_DirectionalLightCount > 0)
                m_DirectionalLightBuffer.SetData(lightData.directionalLights, 0, 0, m_DirectionalLightCount);

            else
                m_DirectionalLightBuffer.SetData(s_EmptyDirectionalLights);

            if (m_PunctualLightCount > 0)
            {
                BuildPunctualLightUploadData(lightData, camera);
                m_PunctualLightBuffer.SetData(m_PunctualLights, 0, 0, m_PunctualLightCount);
            }
            else
            {
                m_PunctualLightBuffer.SetData(s_EmptyPunctualLights);
            }

            m_ClusterAllocationCounterBuffer.SetData(s_ZeroCounterData);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_LightingSetupCompute = resources?.MaterialClassificationCompute;

            if (m_LightingSetupCompute == null)
                return;

            try
            {
                m_ClearClusterLightCounterKernel = m_LightingSetupCompute.FindKernel("ClearClusterLightCounter");
                m_BuildClusteredLightListKernel = m_LightingSetupCompute.FindKernel("BuildClusteredLightList");
            }
            catch (ArgumentException)
            {
                Debug.LogWarning($"[VividRP] Could not find clustered light kernels in {m_LightingSetupCompute.name}. Punctual clustered lighting will be disabled.");
                m_LightingSetupCompute = null;
                m_ClearClusterLightCounterKernel = -1;
                m_BuildClusteredLightListKernel = -1;
            }
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (m_DirectionalLightBuffer == null || m_PunctualLightBuffer == null)
                return;

            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            var canBuildClusteredLights = m_PunctualLightCount > 0
                && m_LightingSetupCompute != null
                && m_ClearClusterLightCounterKernel >= 0
                && m_BuildClusteredLightListKernel >= 0
                && m_ClusterLightGridBuffer != null
                && m_ClusterLightIndicesBuffer != null
                && m_ClusterAllocationCounterBuffer != null;

            if (canBuildClusteredLights)
            {
                cmd.SetComputeIntParam(m_LightingSetupCompute, PunctualLightCountId, m_PunctualLightCount);
                cmd.SetComputeIntParam(m_LightingSetupCompute, ClusterTileSizeId, ClusterTileSize);
                cmd.SetComputeIntParam(m_LightingSetupCompute, ClusterSliceCountId, ClusterSliceCount);
                cmd.SetComputeIntParam(m_LightingSetupCompute, ClusterScreenWidthId, m_LightingWidth);
                cmd.SetComputeIntParam(m_LightingSetupCompute, ClusterScreenHeightId, m_LightingHeight);
                cmd.SetComputeIntParam(m_LightingSetupCompute, ClusterTileCountXId, m_ClusterTileCountX);
                cmd.SetComputeIntParam(m_LightingSetupCompute, ClusterTileCountYId, m_ClusterTileCountY);
                cmd.SetComputeFloatParam(m_LightingSetupCompute, ClusterNearClipId, m_ClusterNearClip);
                cmd.SetComputeFloatParam(m_LightingSetupCompute, ClusterFarClipId, m_ClusterFarClip);
                cmd.SetComputeFloatParam(m_LightingSetupCompute, ClusterLogDepthScaleId, m_ClusterLogDepthScale);
                cmd.SetComputeFloatParam(m_LightingSetupCompute, ClusterLinearDepthScaleId, m_ClusterLinearDepthScale);
                cmd.SetComputeFloatParam(m_LightingSetupCompute, ClusterTanHalfFovXId, m_ClusterTanHalfFovX);
                cmd.SetComputeFloatParam(m_LightingSetupCompute, ClusterTanHalfFovYId, m_ClusterTanHalfFovY);
                cmd.SetComputeFloatParam(m_LightingSetupCompute, ClusterOrthoHalfWidthId, m_ClusterOrthoHalfWidth);
                cmd.SetComputeFloatParam(m_LightingSetupCompute, ClusterOrthoHalfHeightId, m_ClusterOrthoHalfHeight);
                cmd.SetComputeIntParam(m_LightingSetupCompute, ClusterIsOrthographicId, m_ClusterIsOrthographic);
                cmd.SetComputeBufferParam(m_LightingSetupCompute, m_ClearClusterLightCounterKernel, ClusterAllocationCounterId, m_ClusterAllocationCounterBuffer);
                cmd.SetComputeBufferParam(m_LightingSetupCompute, m_BuildClusteredLightListKernel, PunctualLightsId, m_PunctualLightBuffer);
                cmd.SetComputeBufferParam(m_LightingSetupCompute, m_BuildClusteredLightListKernel, ClusterLightGridId, m_ClusterLightGridBuffer);
                cmd.SetComputeBufferParam(m_LightingSetupCompute, m_BuildClusteredLightListKernel, ClusterLightIndicesId, m_ClusterLightIndicesBuffer);
                cmd.SetComputeBufferParam(m_LightingSetupCompute, m_BuildClusteredLightListKernel, ClusterAllocationCounterId, m_ClusterAllocationCounterBuffer);
                cmd.DispatchCompute(m_LightingSetupCompute, m_ClearClusterLightCounterKernel, 1, 1, 1);
                cmd.DispatchCompute(
                    m_LightingSetupCompute,
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
            m_ClusterLightGridBuffer?.Dispose();
            m_ClusterLightIndicesBuffer?.Dispose();
            m_ClusterAllocationCounterBuffer?.Dispose();
            m_DirectionalLightBuffer = null;
            m_PunctualLightBuffer = null;
            m_ClusterLightGridBuffer = null;
            m_ClusterLightIndicesBuffer = null;
            m_ClusterAllocationCounterBuffer = null;
            m_LightingSetupCompute = null;
            m_PunctualLights = Array.Empty<PunctualLightUploadData>();
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

        private void EnsurePunctualLightCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= m_PunctualLights.Length)
                return;

            m_PunctualLights = new PunctualLightUploadData[requiredCapacity];
        }

        private void EnsurePunctualLightBuffer(int requiredBufferCount)
        {
            if (m_PunctualLightBuffer != null
                && m_PunctualLightBuffer.count >= requiredBufferCount
                && m_PunctualLightBuffer.stride == PunctualLightUploadData.Stride)
                return;

            m_PunctualLightBuffer?.Dispose();
            m_PunctualLightBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                requiredBufferCount,
                PunctualLightUploadData.Stride);
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

        private void UpdateClusterProjectionData(Camera camera)
        {
            var nearClip = camera != null ? camera.nearClipPlane : 0.1f;
            var farClip = camera != null ? camera.farClipPlane : 1000.0f;
            var aspect = m_LightingHeight > 0 ? m_LightingWidth / (float)m_LightingHeight : 1.0f;

            m_ClusterNearClip = Mathf.Max(nearClip, 0.01f);
            m_ClusterFarClip = Mathf.Max(farClip, m_ClusterNearClip + 0.01f);
            m_ClusterLogDepthScale = ClusterSliceCount / Mathf.Max(Mathf.Log(m_ClusterFarClip / m_ClusterNearClip, 2.0f), 0.0001f);
            m_ClusterLinearDepthScale = ClusterSliceCount / Mathf.Max(m_ClusterFarClip - m_ClusterNearClip, 0.0001f);

            if (camera != null && camera.orthographic)
            {
                m_ClusterIsOrthographic = 1;
                m_ClusterOrthoHalfHeight = Mathf.Max(camera.orthographicSize, 0.01f);
                m_ClusterOrthoHalfWidth = m_ClusterOrthoHalfHeight * aspect;
                m_ClusterTanHalfFovX = 0.0f;
                m_ClusterTanHalfFovY = 0.0f;
                return;
            }

            var halfVerticalFov = Mathf.Deg2Rad * (camera != null ? camera.fieldOfView : 60.0f) * 0.5f;
            m_ClusterIsOrthographic = 0;
            m_ClusterTanHalfFovY = Mathf.Max(Mathf.Tan(halfVerticalFov), 0.0001f);
            m_ClusterTanHalfFovX = m_ClusterTanHalfFovY * aspect;
            m_ClusterOrthoHalfWidth = 0.0f;
            m_ClusterOrthoHalfHeight = 0.0f;
        }

        private void BuildPunctualLightUploadData(VividLightData lightData, Camera camera)
        {
            var worldToView = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity;

            for (var lightIndex = 0; lightIndex < m_PunctualLightCount; lightIndex++)
            {
                m_PunctualLights[lightIndex] = BuildPunctualLightUploadData(lightData.punctualLights[lightIndex], worldToView);
            }
        }

        private static PunctualLightUploadData BuildPunctualLightUploadData(VividLightData.PunctualLightData source, Matrix4x4 worldToView)
        {
            var positionVS = worldToView.MultiplyPoint3x4(source.positionWS);
            positionVS.z = -positionVS.z;

            var directionVS = worldToView.MultiplyVector(source.directionWS);
            directionVS.z = -directionVS.z;
            if (directionVS.sqrMagnitude > 1e-6f)
                directionVS.Normalize();
            else
                directionVS = Vector3.forward;

            var cullingCenterVS = positionVS;
            var cullingRadius = source.range;

            if (source.lightType == 1u)
            {
                var outerCos = Mathf.Clamp01(-source.angleOffset / Mathf.Max(source.angleScale, 1e-6f));
                var tanOuter = Mathf.Sqrt(Mathf.Max(1.0f / Mathf.Max(outerCos * outerCos, 1e-6f) - 1.0f, 0.0f));
                float centerDistance;

                if (tanOuter <= 1.0f)
                {
                    centerDistance = 0.5f * source.range * (1.0f + tanOuter * tanOuter);
                    cullingRadius = centerDistance;
                }
                else
                {
                    centerDistance = source.range;
                    cullingRadius = source.range * tanOuter;
                }

                cullingCenterVS = positionVS + directionVS * centerDistance;
            }

            return new PunctualLightUploadData
            {
                positionWS = source.positionWS,
                range = source.range,
                color = source.color,
                lightType = source.lightType,
                directionWS = source.directionWS,
                angleScale = source.angleScale,
                angleOffset = source.angleOffset,
                inverseRangeSquared = source.inverseRangeSquared,
                shadowStrength = source.shadowStrength,
                renderingLayerMask = source.renderingLayerMask,
                cullingCenterVS = cullingCenterVS,
                cullingRadius = cullingRadius,
            };
        }
    }
}
