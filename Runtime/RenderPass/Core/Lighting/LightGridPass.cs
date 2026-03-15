using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class LightGridPass : UnsafePass
    {
        internal const int ClusterTileSize = 32;
        internal const int ClusterBigTileSize = 64;
        internal const int ClusterLog2SliceCount = 6;
        internal const int ClusterSliceCount = 1 << ClusterLog2SliceCount;
        internal const float ClusterLogBase = 1.02f;
        internal const int ClusterMaxLightsPerCluster = 63;

        private const int MaxViews = 1;
        private const int HdrpLightCategoryCount = 4;
        private const int HdrpFptlMaxLightCount = 63;
        private const int MaxNrBigTileLightsPlusOne = 512;
        private const int LightsPerScreenAabbGroup = 16;
        private const int ClearLightListThreadGroupSize = 64;
        private const int MaxClearLightListDispatchGroups = 65535;

        private static readonly Matrix4x4 s_FlipMatrixLhsRhs = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));

        private static readonly int DirectionalLightsId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId = Shader.PropertyToID("_MainDirectionalLightIndex");
        private static readonly int PunctualLightsId = Shader.PropertyToID("_PunctualLights");
        private static readonly int PunctualLightCountId = Shader.PropertyToID("_PunctualLightCount");
        private static readonly int FiniteLightBoundsId = Shader.PropertyToID("g_data");
        private static readonly int LightVolumeDataId = Shader.PropertyToID("_LightVolumeData");
        private static readonly int ScreenSpaceBoundsId = Shader.PropertyToID("g_vBoundsBuffer");
        private static readonly int PackedBigTileLightListId = Shader.PropertyToID("g_vLightList");
        private static readonly int BigTileLightListId = Shader.PropertyToID("g_vBigTileLightList");
        private static readonly int LayeredLightListId = Shader.PropertyToID("g_vLayeredLightList");
        private static readonly int LayeredOffsetId = Shader.PropertyToID("g_LayeredOffset");
        private static readonly int LayeredLightListCounterId = Shader.PropertyToID("g_LayeredSingleIdxBuffer");
        private static readonly int LogBaseBufferId = Shader.PropertyToID("g_logBaseBuffer");
        private static readonly int ShaderVariablesLightListId = Shader.PropertyToID("ShaderVariablesLightList");
        private static readonly int LightListToClearId = Shader.PropertyToID("_LightListToClear");
        private static readonly int LightListEntriesAndOffsetId = Shader.PropertyToID("_LightListEntriesAndOffset");
        private static readonly int DepthTextureId = Shader.PropertyToID("g_depth_tex");
        private static readonly int ClusterScaleId = Shader.PropertyToID("g_fClustScale");
        private static readonly int ClusterBaseId = Shader.PropertyToID("g_fClustBase");
        private static readonly int NearPlaneId = Shader.PropertyToID("g_fNearPlane");
        private static readonly int FarPlaneId = Shader.PropertyToID("g_fFarPlane");
        private static readonly int Log2NumClustersId = Shader.PropertyToID("g_iLog2NumClusters");
        private static readonly int IsLogBaseBufferEnabledId = Shader.PropertyToID("g_isLogBaseBufferEnabled");
        private static readonly int NumTileClusteredXId = Shader.PropertyToID("_NumTileClusteredX");
        private static readonly int NumTileClusteredYId = Shader.PropertyToID("_NumTileClusteredY");
        private static readonly int ClusterTileSizeId = Shader.PropertyToID("_ClusterTileSize");
        private static readonly int ClusterSliceCountId = Shader.PropertyToID("_ClusterSliceCount");
        private static readonly int ClusterTileCountXId = Shader.PropertyToID("_ClusterTileCountX");
        private static readonly int ClusterTileCountYId = Shader.PropertyToID("_ClusterTileCountY");
        private static readonly int ClusterNearClipId = Shader.PropertyToID("_ClusterNearClip");
        private static readonly int ClusterFarClipId = Shader.PropertyToID("_ClusterFarClip");
        private static readonly int ClusterIsOrthographicId = Shader.PropertyToID("_ClusterIsOrthographic");

        private static readonly VividLightData.DirectionalLightData[] s_EmptyDirectionalLights = { default };
        private static readonly VividLightData.PunctualLightData[] s_EmptyPunctualLights = { default };
        private static readonly VividLightData.SFiniteLightBound[] s_EmptyFiniteLightBounds = { default };
        private static readonly VividLightData.LightVolumeData[] s_EmptyLightVolumeData = { default };

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        private GraphicsBuffer m_DirectionalLightBuffer;
        private GraphicsBuffer m_PunctualLightBuffer;
        private GraphicsBuffer m_FiniteLightBoundBuffer;
        private GraphicsBuffer m_LightVolumeDataBuffer;
        private GraphicsBuffer m_ScreenSpaceBoundsBuffer;
        private GraphicsBuffer m_BigTileLightListBuffer;
        private GraphicsBuffer m_LayeredOffsetBuffer;
        private GraphicsBuffer m_LayeredLightListBuffer;
        private GraphicsBuffer m_LayeredLightListCounterBuffer;
        private GraphicsBuffer m_LogBaseBuffer;
        private ComputeShader m_ClearLightListsCompute;
        private ComputeShader m_ClearClusterAtomicIndexCompute;
        private ComputeShader m_BuildScreenAabbCompute;
        private ComputeShader m_BuildPerBigTileLightListCompute;
        private ComputeShader m_BuildPerVoxelLightListCompute;
        private readonly Matrix4x4[] m_InvScreenProjectionMatrices = new Matrix4x4[2];
        private readonly Matrix4x4[] m_ScreenProjectionMatrices = new Matrix4x4[2];
        private readonly Matrix4x4[] m_InvProjectionMatrices = new Matrix4x4[2];
        private readonly Matrix4x4[] m_ProjectionMatrices = new Matrix4x4[2];
        private ShaderVariablesLightList m_ShaderVariablesLightListCB;
        private int m_DirectionalLightCount;
        private int m_PunctualLightCount;
        private int m_MainDirectionalLightIndex;
        private int m_LightingWidth;
        private int m_LightingHeight;
        private int m_ClusterTileCountX;
        private int m_ClusterTileCountY;
        private int m_ClusterTileCount;
        private int m_ClusterCount;
        private int m_ClusterBigTileCountX;
        private int m_ClusterBigTileCountY;
        private int m_ClusterBigTileCount;
        private int m_ClusterLightIndexCapacity;
        private int m_ClusterBigTileLightIndexCapacity;
        private int m_LayeredOffsetCapacity;
        private float m_ClusterNearClip;
        private float m_ClusterFarClip;
        private float m_ClusterScale;
        private int m_ClusterIsOrthographic;
        private int m_ClearLightListsKernel = -1;
        private int m_ClearClusterAtomicIndexKernel = -1;
        private int m_BuildScreenAabbKernel = -1;
        private int m_BuildPerBigTileLightListKernel = -1;
        private int m_BuildPerVoxelLightListDepthKernel = -1;
        private int m_BuildPerVoxelLightListNoDepthKernel = -1;

        public LightGridPass()
        {
            profilingSampler = new ProfilingSampler(nameof(LightGridPass));
            m_DepthTexture = CreateDepthTexture("Depth");
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var lightData = frameData.GetOrCreate<VividLightData>();
            var camera = cameraData.camera;

            m_DirectionalLightCount = lightData.directionalLightCount;
            m_PunctualLightCount = lightData.punctualLightCount;
            m_MainDirectionalLightIndex = lightData.mainDirectionalLightIndex;
            m_LightingWidth = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            m_LightingHeight = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (m_LightingWidth <= 0)
                m_LightingWidth = Mathf.Max(1, Screen.width);

            if (m_LightingHeight <= 0)
                m_LightingHeight = Mathf.Max(1, Screen.height);

            ResizeDepthTexture(m_DepthTexture, m_LightingWidth, m_LightingHeight);
            m_ClusterTileCountX = Mathf.Max(1, Mathf.CeilToInt(m_LightingWidth / (float)ClusterTileSize));
            m_ClusterTileCountY = Mathf.Max(1, Mathf.CeilToInt(m_LightingHeight / (float)ClusterTileSize));
            m_ClusterTileCount = Mathf.Max(1, m_ClusterTileCountX * m_ClusterTileCountY);
            m_ClusterCount = Mathf.Max(1, m_ClusterTileCount * ClusterSliceCount * MaxViews);
            m_ClusterBigTileCountX = Mathf.Max(1, Mathf.CeilToInt(m_LightingWidth / (float)ClusterBigTileSize));
            m_ClusterBigTileCountY = Mathf.Max(1, Mathf.CeilToInt(m_LightingHeight / (float)ClusterBigTileSize));
            m_ClusterBigTileCount = Mathf.Max(1, m_ClusterBigTileCountX * m_ClusterBigTileCountY * MaxViews);
            m_ClusterLightIndexCapacity = ComputeClusteredLightListCapacity(m_ClusterTileCount * MaxViews);
            m_ClusterBigTileLightIndexCapacity = ComputeBigTileLightListCapacity(m_ClusterBigTileCount);
            m_LayeredOffsetCapacity = ComputeLayeredOffsetCapacity(m_ClusterCount);

            UpdateClusterCameraParameters(camera);
            UpdateLightListMatrices(camera);
            EnsureDirectionalLightBuffer(Mathf.Max(m_DirectionalLightCount, 1));
            EnsurePunctualLightBuffer(Mathf.Max(m_PunctualLightCount, 1));
            EnsureFiniteLightBoundBuffer(Mathf.Max(m_PunctualLightCount, 1));
            EnsureLightVolumeDataBuffer(Mathf.Max(m_PunctualLightCount, 1));
            EnsureScreenSpaceBoundsBuffer(Mathf.Max(m_PunctualLightCount * 2, 1));
            EnsureBigTileLightListBuffer(Mathf.Max(m_ClusterBigTileLightIndexCapacity, 1));
            EnsureLayeredOffsetBuffer(Mathf.Max(m_LayeredOffsetCapacity, 1));
            EnsureLayeredLightListBuffer(Mathf.Max(m_ClusterLightIndexCapacity, 1));
            EnsureLayeredLightListCounterBuffer();
            EnsureLogBaseBuffer(Mathf.Max(m_ClusterTileCount, 1));

            if (m_DirectionalLightCount > 0)
                m_DirectionalLightBuffer.SetData(lightData.directionalLights, 0, 0, m_DirectionalLightCount);
            else
                m_DirectionalLightBuffer.SetData(s_EmptyDirectionalLights);

            if (m_PunctualLightCount > 0)
            {
                var cullParameters = VividLightData.CreatePunctualLightScreenSpaceBoundsParameters(
                    camera,
                    m_LightingWidth,
                    m_LightingHeight,
                    ClusterTileSize,
                    ClusterSliceCount,
                    ClusterBigTileSize);
                lightData.UpdatePunctualLightClusteredCullData(cullParameters);
                m_PunctualLightBuffer.SetData(lightData.punctualLights, 0, 0, m_PunctualLightCount);
                m_FiniteLightBoundBuffer.SetData(lightData.punctualLightBounds, 0, 0, m_PunctualLightCount);
                m_LightVolumeDataBuffer.SetData(lightData.punctualLightVolumeData, 0, 0, m_PunctualLightCount);
            }
            else
            {
                m_PunctualLightBuffer.SetData(s_EmptyPunctualLights);
                m_FiniteLightBoundBuffer.SetData(s_EmptyFiniteLightBounds);
                m_LightVolumeDataBuffer.SetData(s_EmptyLightVolumeData);
            }

            UpdateShaderVariablesLightListConstantBuffer();
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ClearLightListsCompute = resources?.ClearLightListsCompute;
            m_ClearClusterAtomicIndexCompute = resources?.ClearClusterAtomicIndexCompute;
            m_BuildScreenAabbCompute = resources?.BuildScreenAABBCompute;
            m_BuildPerBigTileLightListCompute = resources?.BuildPerBigTileLightListCompute;
            m_BuildPerVoxelLightListCompute = resources?.BuildPerVoxelLightListCompute;

            if (m_ClearLightListsCompute == null
                || m_ClearClusterAtomicIndexCompute == null
                || m_BuildScreenAabbCompute == null
                || m_BuildPerBigTileLightListCompute == null
                || m_BuildPerVoxelLightListCompute == null)
            {
                Debug.LogWarning("[VividRP] Missing one or more HDRP clustered light culling compute shaders. Clustered punctual lighting will be disabled.");
                return;
            }

            try
            {
                m_ClearLightListsKernel = m_ClearLightListsCompute.FindKernel("ClearList");
                m_ClearClusterAtomicIndexKernel = m_ClearClusterAtomicIndexCompute.FindKernel("ClearAtomic");
                m_BuildScreenAabbKernel = m_BuildScreenAabbCompute.FindKernel("main");
                m_BuildPerBigTileLightListKernel = m_BuildPerBigTileLightListCompute.FindKernel("BigTileLightListGen");
                m_BuildPerVoxelLightListDepthKernel = m_BuildPerVoxelLightListCompute.FindKernel("TileLightListGen_DepthRT_SrcBigTile");
                m_BuildPerVoxelLightListNoDepthKernel = m_BuildPerVoxelLightListCompute.FindKernel("TileLightListGen_NoDepthRT_SrcBigTile");
            }
            catch (ArgumentException)
            {
                Debug.LogWarning("[VividRP] Could not find one or more HDRP clustered light list kernels. Clustered punctual lighting will be disabled.");
                m_ClearLightListsCompute = null;
                m_ClearClusterAtomicIndexCompute = null;
                m_BuildScreenAabbCompute = null;
                m_BuildPerBigTileLightListCompute = null;
                m_BuildPerVoxelLightListCompute = null;
                m_ClearLightListsKernel = -1;
                m_ClearClusterAtomicIndexKernel = -1;
                m_BuildScreenAabbKernel = -1;
                m_BuildPerBigTileLightListKernel = -1;
                m_BuildPerVoxelLightListDepthKernel = -1;
                m_BuildPerVoxelLightListNoDepthKernel = -1;
            }
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (m_DirectionalLightBuffer == null
                || m_PunctualLightBuffer == null
                || m_FiniteLightBoundBuffer == null
                || m_LightVolumeDataBuffer == null
                || m_ScreenSpaceBoundsBuffer == null
                || m_BigTileLightListBuffer == null
                || m_LayeredOffsetBuffer == null
                || m_LayeredLightListBuffer == null
                || m_LayeredLightListCounterBuffer == null
                || m_LogBaseBuffer == null)
            {
                return;
            }

            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(cmd, profilingSampler))
            {
                var hasDepthTexture = m_DepthTexture != null && m_DepthTexture.innerHandle.IsValid();
                var canBuildClusteredLights = m_PunctualLightCount > 0
                    && m_ClearLightListsCompute != null
                    && m_ClearClusterAtomicIndexCompute != null
                    && m_BuildScreenAabbCompute != null
                    && m_BuildPerBigTileLightListCompute != null
                    && m_BuildPerVoxelLightListCompute != null
                    && m_ClearLightListsKernel >= 0
                    && m_ClearClusterAtomicIndexKernel >= 0
                    && m_BuildScreenAabbKernel >= 0
                    && m_BuildPerBigTileLightListKernel >= 0
                    && ((hasDepthTexture && m_BuildPerVoxelLightListDepthKernel >= 0)
                        || m_BuildPerVoxelLightListNoDepthKernel >= 0);

                SetSharedLightLoopGlobals(cmd, canBuildClusteredLights && hasDepthTexture);

                if (canBuildClusteredLights)
                {
                    DispatchClearLightLists(cmd);
                    DispatchScreenSpaceAabb(cmd);
                    DispatchBigTilePrepass(cmd);
                    DispatchClearClusterAtomicIndex(cmd);
                    DispatchClusteredLightList(cmd, hasDepthTexture);
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
                cmd.SetGlobalBuffer(DirectionalLightsId, m_DirectionalLightBuffer);
                cmd.SetGlobalBuffer(PunctualLightsId, m_PunctualLightBuffer);
                cmd.SetGlobalBuffer(LayeredOffsetId, m_LayeredOffsetBuffer);
                cmd.SetGlobalBuffer(LayeredLightListId, m_LayeredLightListBuffer);
                cmd.SetGlobalBuffer(LogBaseBufferId, m_LogBaseBuffer);
            }
        }

        public override void Dispose()
        {
            m_DirectionalLightBuffer?.Dispose();
            m_PunctualLightBuffer?.Dispose();
            m_FiniteLightBoundBuffer?.Dispose();
            m_LightVolumeDataBuffer?.Dispose();
            m_ScreenSpaceBoundsBuffer?.Dispose();
            m_BigTileLightListBuffer?.Dispose();
            m_LayeredOffsetBuffer?.Dispose();
            m_LayeredLightListBuffer?.Dispose();
            m_LayeredLightListCounterBuffer?.Dispose();
            m_LogBaseBuffer?.Dispose();
            m_DirectionalLightBuffer = null;
            m_PunctualLightBuffer = null;
            m_FiniteLightBoundBuffer = null;
            m_LightVolumeDataBuffer = null;
            m_ScreenSpaceBoundsBuffer = null;
            m_BigTileLightListBuffer = null;
            m_LayeredOffsetBuffer = null;
            m_LayeredLightListBuffer = null;
            m_LayeredLightListCounterBuffer = null;
            m_LogBaseBuffer = null;
            m_ClearLightListsCompute = null;
            m_ClearClusterAtomicIndexCompute = null;
            m_BuildScreenAabbCompute = null;
            m_BuildPerBigTileLightListCompute = null;
            m_BuildPerVoxelLightListCompute = null;
            m_ShaderVariablesLightListCB = default;
            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_MainDirectionalLightIndex = -1;
            m_LightingWidth = 0;
            m_LightingHeight = 0;
            m_ClusterTileCountX = 0;
            m_ClusterTileCountY = 0;
            m_ClusterTileCount = 0;
            m_ClusterCount = 0;
            m_ClusterBigTileCountX = 0;
            m_ClusterBigTileCountY = 0;
            m_ClusterBigTileCount = 0;
            m_ClusterLightIndexCapacity = 0;
            m_ClusterBigTileLightIndexCapacity = 0;
            m_LayeredOffsetCapacity = 0;
            m_ClusterNearClip = 0.0f;
            m_ClusterFarClip = 0.0f;
            m_ClusterScale = 0.0f;
            m_ClusterIsOrthographic = 0;
            m_ClearLightListsKernel = -1;
            m_ClearClusterAtomicIndexKernel = -1;
            m_BuildScreenAabbKernel = -1;
            m_BuildPerBigTileLightListKernel = -1;
            m_BuildPerVoxelLightListDepthKernel = -1;
            m_BuildPerVoxelLightListNoDepthKernel = -1;
        }

        private void DispatchClearLightLists(CommandBuffer cmd)
        {
            DispatchClearLightList(cmd, m_BigTileLightListBuffer);
            DispatchClearLightList(cmd, m_LayeredOffsetBuffer);
        }

        private void DispatchClearLightList(CommandBuffer cmd, GraphicsBuffer buffer)
        {
            if (buffer == null)
                return;

            cmd.SetComputeBufferParam(m_ClearLightListsCompute, m_ClearLightListsKernel, LightListToClearId, buffer);

            var remainingGroupCount = Mathf.CeilToInt(buffer.count / (float)ClearLightListThreadGroupSize);
            var dispatchOffset = 0;
            while (remainingGroupCount > 0)
            {
                var currentGroupCount = Mathf.Min(remainingGroupCount, MaxClearLightListDispatchGroups);
                cmd.SetComputeVectorParam(
                    m_ClearLightListsCompute,
                    LightListEntriesAndOffsetId,
                    new Vector4(buffer.count, dispatchOffset, 0.0f, 0.0f));
                cmd.DispatchCompute(m_ClearLightListsCompute, m_ClearLightListsKernel, currentGroupCount, 1, 1);
                remainingGroupCount -= currentGroupCount;
                dispatchOffset += currentGroupCount * ClearLightListThreadGroupSize;
            }
        }

        private void DispatchScreenSpaceAabb(CommandBuffer cmd)
        {
            cmd.SetComputeBufferParam(m_BuildScreenAabbCompute, m_BuildScreenAabbKernel, FiniteLightBoundsId, m_FiniteLightBoundBuffer);
            cmd.SetComputeBufferParam(m_BuildScreenAabbCompute, m_BuildScreenAabbKernel, ScreenSpaceBoundsId, m_ScreenSpaceBoundsBuffer);
            PushShaderVariablesLightList(cmd, m_BuildScreenAabbCompute);
            cmd.DispatchCompute(
                m_BuildScreenAabbCompute,
                m_BuildScreenAabbKernel,
                Mathf.Max(1, Mathf.CeilToInt(m_PunctualLightCount / (float)LightsPerScreenAabbGroup)),
                MaxViews,
                1);
        }

        private void DispatchBigTilePrepass(CommandBuffer cmd)
        {
            cmd.SetComputeBufferParam(m_BuildPerBigTileLightListCompute, m_BuildPerBigTileLightListKernel, ScreenSpaceBoundsId, m_ScreenSpaceBoundsBuffer);
            cmd.SetComputeBufferParam(m_BuildPerBigTileLightListCompute, m_BuildPerBigTileLightListKernel, LightVolumeDataId, m_LightVolumeDataBuffer);
            cmd.SetComputeBufferParam(m_BuildPerBigTileLightListCompute, m_BuildPerBigTileLightListKernel, FiniteLightBoundsId, m_FiniteLightBoundBuffer);
            cmd.SetComputeBufferParam(m_BuildPerBigTileLightListCompute, m_BuildPerBigTileLightListKernel, PackedBigTileLightListId, m_BigTileLightListBuffer);
            PushShaderVariablesLightList(cmd, m_BuildPerBigTileLightListCompute);
            cmd.DispatchCompute(
                m_BuildPerBigTileLightListCompute,
                m_BuildPerBigTileLightListKernel,
                m_ClusterBigTileCountX,
                m_ClusterBigTileCountY,
                MaxViews);
        }

        private void DispatchClearClusterAtomicIndex(CommandBuffer cmd)
        {
            if (m_ClearClusterAtomicIndexCompute == null || m_ClearClusterAtomicIndexKernel < 0)
                return;

            cmd.SetComputeBufferParam(
                m_ClearClusterAtomicIndexCompute,
                m_ClearClusterAtomicIndexKernel,
                LayeredLightListCounterId,
                m_LayeredLightListCounterBuffer);
            cmd.DispatchCompute(m_ClearClusterAtomicIndexCompute, m_ClearClusterAtomicIndexKernel, 1, 1, 1);
        }

        private void DispatchClusteredLightList(CommandBuffer cmd, bool hasDepthTexture)
        {
            var kernel = hasDepthTexture ? m_BuildPerVoxelLightListDepthKernel : m_BuildPerVoxelLightListNoDepthKernel;
            if (kernel < 0)
                return;

            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, ScreenSpaceBoundsId, m_ScreenSpaceBoundsBuffer);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, LightVolumeDataId, m_LightVolumeDataBuffer);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, FiniteLightBoundsId, m_FiniteLightBoundBuffer);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, BigTileLightListId, m_BigTileLightListBuffer);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, LayeredLightListId, m_LayeredLightListBuffer);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, LayeredOffsetId, m_LayeredOffsetBuffer);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, LayeredLightListCounterId, m_LayeredLightListCounterBuffer);
            PushShaderVariablesLightList(cmd, m_BuildPerVoxelLightListCompute);

            if (hasDepthTexture)
            {
                cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, LogBaseBufferId, m_LogBaseBuffer);
                cmd.SetComputeTextureParam(m_BuildPerVoxelLightListCompute, kernel, DepthTextureId, m_DepthTexture.innerHandle);
            }

            cmd.DispatchCompute(
                m_BuildPerVoxelLightListCompute,
                kernel,
                m_ClusterTileCountX,
                m_ClusterTileCountY,
                MaxViews);
        }

        private void SetSharedLightLoopGlobals(CommandBuffer cmd, bool enableLogBaseBuffer)
        {
            cmd.SetGlobalFloat(ClusterScaleId, m_ClusterScale);
            cmd.SetGlobalFloat(ClusterBaseId, ClusterLogBase);
            cmd.SetGlobalFloat(NearPlaneId, m_ClusterNearClip);
            cmd.SetGlobalFloat(FarPlaneId, m_ClusterFarClip);
            cmd.SetGlobalInt(Log2NumClustersId, ClusterLog2SliceCount);
            cmd.SetGlobalInt(IsLogBaseBufferEnabledId, enableLogBaseBuffer ? 1 : 0);
            cmd.SetGlobalInt(NumTileClusteredXId, m_ClusterTileCountX);
            cmd.SetGlobalInt(NumTileClusteredYId, m_ClusterTileCountY);
        }

        private void UpdateShaderVariablesLightListConstantBuffer()
        {
            m_ShaderVariablesLightListCB.g_mInvScrProjectionArr0 = m_InvScreenProjectionMatrices[0];
            m_ShaderVariablesLightListCB.g_mInvScrProjectionArr1 = m_InvScreenProjectionMatrices[1];
            m_ShaderVariablesLightListCB.g_mScrProjectionArr0 = m_ScreenProjectionMatrices[0];
            m_ShaderVariablesLightListCB.g_mScrProjectionArr1 = m_ScreenProjectionMatrices[1];
            m_ShaderVariablesLightListCB.g_mInvProjectionArr0 = m_InvProjectionMatrices[0];
            m_ShaderVariablesLightListCB.g_mInvProjectionArr1 = m_InvProjectionMatrices[1];
            m_ShaderVariablesLightListCB.g_mProjectionArr0 = m_ProjectionMatrices[0];
            m_ShaderVariablesLightListCB.g_mProjectionArr1 = m_ProjectionMatrices[1];
            m_ShaderVariablesLightListCB.g_screenSize = new Vector4(
                m_LightingWidth,
                m_LightingHeight,
                1.0f / Mathf.Max(m_LightingWidth, 1),
                1.0f / Mathf.Max(m_LightingHeight, 1));
            m_ShaderVariablesLightListCB.g_viDimensions = new ShaderVariablesLightListInt2(m_LightingWidth, m_LightingHeight);
            m_ShaderVariablesLightListCB.g_iNrVisibLights = m_PunctualLightCount;
            m_ShaderVariablesLightListCB.g_isOrthographic = (uint)m_ClusterIsOrthographic;
            m_ShaderVariablesLightListCB.g_BaseFeatureFlags = 0u;
            m_ShaderVariablesLightListCB.g_iNumSamplesMSAA = 1;
            m_ShaderVariablesLightListCB._EnvLightIndexShift = 0u;
            m_ShaderVariablesLightListCB._DecalIndexShift = 0u;
        }

        private void PushShaderVariablesLightList(CommandBuffer cmd, ComputeShader computeShader)
        {
            ConstantBuffer.Push(cmd, m_ShaderVariablesLightListCB, computeShader, ShaderVariablesLightListId);
        }

        private void UpdateClusterCameraParameters(Camera camera)
        {
            m_ClusterNearClip = Mathf.Max(camera != null ? camera.nearClipPlane : 0.1f, 0.01f);
            m_ClusterFarClip = Mathf.Max(camera != null ? camera.farClipPlane : 1000.0f, m_ClusterNearClip + 0.01f);
            m_ClusterIsOrthographic = camera != null && camera.orthographic ? 1 : 0;

            var clusterCount = (float)(1 << ClusterLog2SliceCount);
            var geometricSeries = (1.0f - Mathf.Pow(ClusterLogBase, clusterCount)) / (1.0f - ClusterLogBase);
            m_ClusterScale = geometricSeries / Mathf.Max(m_ClusterFarClip - m_ClusterNearClip, 1e-4f);
        }

        private void UpdateLightListMatrices(Camera camera)
        {
            var aspect = m_LightingHeight > 0 ? m_LightingWidth / (float)m_LightingHeight : 1.0f;
            var projection = camera != null
                ? camera.projectionMatrix
                : Matrix4x4.Perspective(60.0f, aspect, m_ClusterNearClip, m_ClusterFarClip);
            var lightListProjection = projection * s_FlipMatrixLhsRhs;
            var screenToPixel = Matrix4x4.identity;
            screenToPixel.SetRow(0, new Vector4(0.5f * m_LightingWidth, 0.0f, 0.0f, 0.5f * m_LightingWidth));
            screenToPixel.SetRow(1, new Vector4(0.0f, 0.5f * m_LightingHeight, 0.0f, 0.5f * m_LightingHeight));
            screenToPixel.SetRow(2, new Vector4(0.0f, 0.0f, 0.5f, 0.5f));
            screenToPixel.SetRow(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            var screenProjection = screenToPixel * lightListProjection;
            var projectionToUnit = Matrix4x4.identity;
            projectionToUnit.SetRow(2, new Vector4(0.0f, 0.0f, 0.5f, 0.5f));
            var clippedProjection = projectionToUnit * lightListProjection;

            for (var viewIndex = 0; viewIndex < m_InvScreenProjectionMatrices.Length; viewIndex++)
            {
                m_ScreenProjectionMatrices[viewIndex] = screenProjection;
                m_InvScreenProjectionMatrices[viewIndex] = screenProjection.inverse;
                m_ProjectionMatrices[viewIndex] = clippedProjection;
                m_InvProjectionMatrices[viewIndex] = clippedProjection.inverse;
            }
        }

        private void EnsureDirectionalLightBuffer(int requiredCount)
        {
            EnsureStructuredBuffer(ref m_DirectionalLightBuffer, requiredCount, VividLightData.DirectionalLightData.Stride);
        }

        private void EnsurePunctualLightBuffer(int requiredCount)
        {
            EnsureStructuredBuffer(ref m_PunctualLightBuffer, requiredCount, VividLightData.PunctualLightData.Stride);
        }

        private void EnsureFiniteLightBoundBuffer(int requiredCount)
        {
            EnsureStructuredBuffer(ref m_FiniteLightBoundBuffer, requiredCount, VividLightData.SFiniteLightBound.Stride);
        }

        private void EnsureLightVolumeDataBuffer(int requiredCount)
        {
            EnsureStructuredBuffer(ref m_LightVolumeDataBuffer, requiredCount, VividLightData.LightVolumeData.Stride);
        }

        private void EnsureScreenSpaceBoundsBuffer(int requiredCount)
        {
            EnsureStructuredBuffer(ref m_ScreenSpaceBoundsBuffer, requiredCount, sizeof(float) * 4);
        }

        private void EnsureBigTileLightListBuffer(int requiredCount)
        {
            EnsureStructuredBuffer(ref m_BigTileLightListBuffer, requiredCount, sizeof(uint));
        }

        private void EnsureLayeredOffsetBuffer(int requiredCount)
        {
            EnsureStructuredBuffer(ref m_LayeredOffsetBuffer, requiredCount, sizeof(uint));
        }

        private void EnsureLayeredLightListBuffer(int requiredCount)
        {
            EnsureStructuredBuffer(ref m_LayeredLightListBuffer, requiredCount, sizeof(uint));
        }

        private void EnsureLayeredLightListCounterBuffer()
        {
            EnsureStructuredBuffer(ref m_LayeredLightListCounterBuffer, 1, sizeof(uint));
        }

        private void EnsureLogBaseBuffer(int requiredCount)
        {
            EnsureStructuredBuffer(ref m_LogBaseBuffer, requiredCount, sizeof(float));
        }

        private static void EnsureStructuredBuffer(ref GraphicsBuffer buffer, int requiredCount, int stride)
        {
            if (buffer != null && buffer.count >= requiredCount && buffer.stride == stride)
                return;

            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(requiredCount, 1), stride);
        }

        private static int ComputeClusteredLightListCapacity(int clusterTileCount)
        {
            var perTileCapacity = (long)(HdrpFptlMaxLightCount + 1) * (1 << (ClusterLog2SliceCount + 1));
            return ClampToPositiveInt(perTileCapacity * Mathf.Max(clusterTileCount, 1));
        }

        private static int ComputeBigTileLightListCapacity(int bigTileCount)
        {
            return ClampToPositiveInt((long)MaxNrBigTileLightsPlusOne * Mathf.Max(bigTileCount, 1) / 2L);
        }

        private static int ComputeLayeredOffsetCapacity(int clusterCount)
        {
            return ClampToPositiveInt((long)HdrpLightCategoryCount * Mathf.Max(clusterCount, 1));
        }

        private static int ClampToPositiveInt(long value)
        {
            return Mathf.Max(1, (int)Math.Min(value, int.MaxValue));
        }

        private static RenderGraphTexture CreateDepthTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth32)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            return texture;
        }

        private static void ResizeDepthTexture(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
        }
    }
}
