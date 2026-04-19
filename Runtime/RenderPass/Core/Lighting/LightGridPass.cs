using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class LightGridPass : ComputePass
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
        private const uint HdrpLightCategoryArea = 1u;
        private const int LightClusterPackingOffsetBits = 26;
        private const uint LightClusterPackingCountMask = 63u;
        private const uint LightClusterPackingOffsetMask = 67108863u;

        private static readonly Matrix4x4 s_FlipMatrixLhsRhs = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));

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
        private static readonly int HzbDepthTextureId = Shader.PropertyToID("g_depth_tex_hiz");
        private static readonly int ClusterScaleId = Shader.PropertyToID("g_fClustScale");
        private static readonly int ClusterBaseId = Shader.PropertyToID("g_fClustBase");
        private static readonly int NearPlaneId = Shader.PropertyToID("g_fNearPlane");
        private static readonly int FarPlaneId = Shader.PropertyToID("g_fFarPlane");
        private static readonly int Log2NumClustersId = Shader.PropertyToID("g_iLog2NumClusters");
        private static readonly int IsLogBaseBufferEnabledId = Shader.PropertyToID("g_isLogBaseBufferEnabled");
        private static readonly int NumTileClusteredXId = Shader.PropertyToID("_NumTileClusteredX");
        private static readonly int NumTileClusteredYId = Shader.PropertyToID("_NumTileClusteredY");

        private static readonly VividLightData.DirectionalLightData[] s_EmptyDirectionalLights = { default };
        private static readonly VividLightData.PunctualLightData[] s_EmptyPunctualLights = { default };
        private static readonly VividLightData.AreaLightData[] s_EmptyAreaLights = { default };
        private static readonly VividLightData.SFiniteLightBound[] s_EmptyFiniteLightBounds = { default };
        private static readonly VividLightData.LightVolumeData[] s_EmptyLightVolumeData = { default };
        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "HZB", Access = AccessFlags.Read)]
        private RenderGraphTexture m_HzbDepthTexture;

        [RenderGraphResource(
            Name = "DirectionalLights",
            Access = AccessFlags.Write)]
        private RenderGraphBuffer m_DirectionalLightBuffer;

        [RenderGraphResource(
            Name = "PunctualLights",
            Access = AccessFlags.Write)]
        private RenderGraphBuffer m_PunctualLightBuffer;

        [RenderGraphResource( Name = "AreaLights",  Access = AccessFlags.Write)]
        private RenderGraphBuffer m_AreaLightBuffer;

        [RenderGraphResource(Name = "FiniteLightBounds", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_FiniteLightBoundBuffer;

        [RenderGraphResource(Name = "LightVolumeData", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_LightVolumeDataBuffer;

        [RenderGraphResource(Name = "ScreenSpaceBounds", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_ScreenSpaceBoundsBuffer;

        [RenderGraphResource(Name = "BigTileLightList", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_BigTileLightListBuffer;

        [RenderGraphResource(
            Name = "LayeredOffset",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_LayeredOffsetBuffer;

        [RenderGraphResource(
            Name = "LayeredLightList",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_LayeredLightListBuffer;

        [RenderGraphResource(Name = "LayeredLightListCounter", Access = AccessFlags.Write)]
        private RenderGraphBuffer m_LayeredLightListCounterBuffer;

        [RenderGraphResource(
            Name = "LogBaseBuffer",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphBuffer m_LogBaseBuffer;

        private GraphicsBuffer m_DirectionalLightImportedBuffer;
        private GraphicsBuffer m_PunctualLightImportedBuffer;
        private GraphicsBuffer m_AreaLightImportedBuffer;
        private GraphicsBuffer m_FiniteLightBoundImportedBuffer;
        private GraphicsBuffer m_LightVolumeDataImportedBuffer;
        private GraphicsBuffer m_ScreenSpaceBoundsImportedBuffer;
        private GraphicsBuffer m_BigTileLightListImportedBuffer;
        private GraphicsBuffer m_LayeredOffsetImportedBuffer;
        private GraphicsBuffer m_LayeredLightListImportedBuffer;
        private GraphicsBuffer m_LayeredLightListCounterImportedBuffer;
        private GraphicsBuffer m_LogBaseImportedBuffer;
        private ComputeShader m_ClearLightListsCompute;
        private ComputeShader m_ClearClusterAtomicIndexCompute;
        private ComputeShader m_BuildScreenAabbCompute;
        private ComputeShader m_BuildPerBigTileLightListCompute;
        private ComputeShader m_BuildPerVoxelLightListCompute;
        private readonly Matrix4x4[] m_InvScreenProjectionMatrices = new Matrix4x4[2];
        private readonly Matrix4x4[] m_ScreenProjectionMatrices = new Matrix4x4[2];
        private readonly Matrix4x4[] m_InvProjectionMatrices = new Matrix4x4[2];
        private readonly Matrix4x4[] m_ProjectionMatrices = new Matrix4x4[2];
        private VividLightData.DirectionalLightData[] m_DirectionalLightUploadData = Array.Empty<VividLightData.DirectionalLightData>();
        private uint[] m_LayeredOffsetUploadData = Array.Empty<uint>();
        private uint[] m_AreaLightListUploadData = Array.Empty<uint>();
        private ShaderVariablesLightList m_ShaderVariablesLightListCB;
        private int m_DirectionalLightCount;
        private int m_PunctualLightCount;
        private int m_AreaLightCount;
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
        private int m_PunctualLightIndexCapacity;
        private int m_ClusterLightIndexCapacity;
        private int m_AreaLightListStartOffset;
        private int m_ClusteredAreaLightCount;
        private int m_ClusterBigTileLightIndexCapacity;
        private int m_LayeredOffsetCapacity;
        private float m_ClusterNearClip;
        private float m_ClusterFarClip;
        private float m_ClusterScale;
        private int m_ClusterIsOrthographic;
        private bool m_SupportsClusteredPunctualLights;
        private int m_ClearLightListsKernel = -1;
        private int m_ClearClusterAtomicIndexKernel = -1;
        private int m_BuildScreenAabbKernel = -1;
        private int m_BuildPerBigTileLightListKernel = -1;
        private int m_BuildPerVoxelLightListDepthKernel = -1;
        private int m_BuildPerVoxelLightListNoDepthKernel = -1;

        public LightGridPass()
        {
            profilingSampler = new ProfilingSampler(nameof(LightGridPass));
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_HzbDepthTexture = RenderGraphTexture.CreateInput("HZB", GraphicsFormat.R16G16B16A16_SFloat);
            m_DirectionalLightBuffer = CreateStructuredBuffer("DirectionalLights", 1, VividLightData.DirectionalLightData.Stride);
            m_PunctualLightBuffer = CreateStructuredBuffer("PunctualLights", 1, VividLightData.PunctualLightData.Stride);
            m_AreaLightBuffer = CreateStructuredBuffer("AreaLights", 1, VividLightData.AreaLightData.Stride);
            m_FiniteLightBoundBuffer = CreateStructuredBuffer("FiniteLightBounds", 1, VividLightData.SFiniteLightBound.Stride);
            m_LightVolumeDataBuffer = CreateStructuredBuffer("LightVolumeData", 1, VividLightData.LightVolumeData.Stride);
            m_ScreenSpaceBoundsBuffer = CreateStructuredBuffer("ScreenSpaceBounds", 1, sizeof(float) * 4);
            m_BigTileLightListBuffer = CreateStructuredBuffer("BigTileLightList", 1, sizeof(uint));
            m_LayeredOffsetBuffer = CreateStructuredBuffer("LayeredOffset", 1, sizeof(uint));
            m_LayeredLightListBuffer = CreateStructuredBuffer("LayeredLightList", 1, sizeof(uint));
            m_LayeredLightListCounterBuffer = CreateStructuredBuffer("LayeredLightListCounter", 1, sizeof(uint));
            m_LogBaseBuffer = CreateStructuredBuffer("LogBaseBuffer", 1, sizeof(float));
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var lightData = frameData.GetOrCreate<VividLightData>();
            var camera = cameraData.camera;

            m_DirectionalLightCount = lightData.directionalLightCount;
            m_PunctualLightCount = lightData.punctualLightCount;
            m_AreaLightCount = lightData.areaLightCount;
            m_MainDirectionalLightIndex = lightData.mainDirectionalLightIndex;
            m_LightingWidth = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            m_LightingHeight = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (m_LightingWidth <= 0)
                m_LightingWidth = Mathf.Max(1, Screen.width);

            if (m_LightingHeight <= 0)
                m_LightingHeight = Mathf.Max(1, Screen.height);

            m_DepthTexture.Resize(m_LightingWidth, m_LightingHeight);
            m_ClusterTileCountX = Mathf.Max(1, Mathf.CeilToInt(m_LightingWidth / (float)ClusterTileSize));
            m_ClusterTileCountY = Mathf.Max(1, Mathf.CeilToInt(m_LightingHeight / (float)ClusterTileSize));
            m_ClusterTileCount = Mathf.Max(1, m_ClusterTileCountX * m_ClusterTileCountY);
            m_ClusterCount = Mathf.Max(1, m_ClusterTileCount * ClusterSliceCount * MaxViews);
            m_ClusterBigTileCountX = Mathf.Max(1, Mathf.CeilToInt(m_LightingWidth / (float)ClusterBigTileSize));
            m_ClusterBigTileCountY = Mathf.Max(1, Mathf.CeilToInt(m_LightingHeight / (float)ClusterBigTileSize));
            m_ClusterBigTileCount = Mathf.Max(1, m_ClusterBigTileCountX * m_ClusterBigTileCountY * MaxViews);
            m_PunctualLightIndexCapacity = ComputeClusteredLightListCapacity(m_ClusterTileCount * MaxViews);
            m_ClusteredAreaLightCount = Mathf.Min(m_AreaLightCount, (int)LightClusterPackingCountMask);
            m_AreaLightListStartOffset = m_PunctualLightIndexCapacity;
            m_ClusterLightIndexCapacity = ComputeCombinedClusteredLightListCapacity(
                m_PunctualLightIndexCapacity,
                m_ClusteredAreaLightCount);
            m_ClusterBigTileLightIndexCapacity = ComputeBigTileLightListCapacity(m_ClusterBigTileCount);
            m_LayeredOffsetCapacity = ComputeLayeredOffsetCapacity(m_ClusterCount);

            UpdateClusterCameraParameters(camera);
            UpdateLightListMatrices(camera);

            ResizeStructuredBuffer(m_DirectionalLightBuffer, Mathf.Max(m_DirectionalLightCount, 1), VividLightData.DirectionalLightData.Stride);
            ResizeStructuredBuffer(m_PunctualLightBuffer, Mathf.Max(m_PunctualLightCount, 1), VividLightData.PunctualLightData.Stride);
            ResizeStructuredBuffer(m_AreaLightBuffer, Mathf.Max(m_AreaLightCount, 1), VividLightData.AreaLightData.Stride);
            ResizeStructuredBuffer(m_FiniteLightBoundBuffer, Mathf.Max(m_PunctualLightCount, 1), VividLightData.SFiniteLightBound.Stride);
            ResizeStructuredBuffer(m_LightVolumeDataBuffer, Mathf.Max(m_PunctualLightCount, 1), VividLightData.LightVolumeData.Stride);
            ResizeStructuredBuffer(m_ScreenSpaceBoundsBuffer, Mathf.Max(m_PunctualLightCount * 2, 1), sizeof(float) * 4);
            ResizeStructuredBuffer(m_BigTileLightListBuffer, Mathf.Max(m_ClusterBigTileLightIndexCapacity, 1), sizeof(uint));
            ResizeStructuredBuffer(m_LayeredOffsetBuffer, Mathf.Max(m_LayeredOffsetCapacity, 1), sizeof(uint));
            ResizeStructuredBuffer(m_LayeredLightListBuffer, Mathf.Max(m_ClusterLightIndexCapacity, 1), sizeof(uint));
            ResizeStructuredBuffer(m_LayeredLightListCounterBuffer, 1, sizeof(uint));
            ResizeStructuredBuffer(m_LogBaseBuffer, Mathf.Max(m_ClusterTileCount, 1), sizeof(float));
            EnsureImportedBuffers();

            if (m_DirectionalLightCount > 0)
            {
                UpdateDirectionalLightUploadData(lightData, camera);
                m_DirectionalLightImportedBuffer.SetData(m_DirectionalLightUploadData, 0, 0, m_DirectionalLightCount);
            }
            else
                m_DirectionalLightImportedBuffer.SetData(s_EmptyDirectionalLights);

            if (m_AreaLightCount > 0)
                m_AreaLightImportedBuffer.SetData(lightData.areaLights, 0, 0, m_AreaLightCount);
            else
                m_AreaLightImportedBuffer.SetData(s_EmptyAreaLights);

            UploadAreaLightGridData();

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
                m_PunctualLightImportedBuffer.SetData(lightData.punctualLights, 0, 0, m_PunctualLightCount);
                m_FiniteLightBoundImportedBuffer.SetData(lightData.punctualLightBounds, 0, 0, m_PunctualLightCount);
                m_LightVolumeDataImportedBuffer.SetData(lightData.punctualLightVolumeData, 0, 0, m_PunctualLightCount);
            }
            else
            {
                m_PunctualLightImportedBuffer.SetData(s_EmptyPunctualLights);
                m_FiniteLightBoundImportedBuffer.SetData(s_EmptyFiniteLightBounds);
                m_LightVolumeDataImportedBuffer.SetData(s_EmptyLightVolumeData);
            }

            m_SupportsClusteredPunctualLights = CanBuildClusteredPunctualLights();
            UpdateShaderVariablesLightListConstantBuffer();
            UpdateClusteredLightingFrameData(frameData);
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

        public override void Record(ComputeGraphContext context)
        {
            if (m_ClearLightListsCompute == null
                || m_ClearClusterAtomicIndexCompute == null
                || m_BuildScreenAabbCompute == null
                || m_BuildPerBigTileLightListCompute == null
                || m_BuildPerVoxelLightListCompute == null)
            {
                return;
            }

            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                var hasDepthTexture = m_DepthTexture != null && m_DepthTexture.innerHandle.IsValid();
                var hasHzbTexture = HasUsableHzbTexture();
                var canUseDepthBackplane = hasDepthTexture && hasHzbTexture;
                var canBuildClusteredLights = m_SupportsClusteredPunctualLights
                    && ((canUseDepthBackplane && m_BuildPerVoxelLightListDepthKernel >= 0)
                        || m_BuildPerVoxelLightListNoDepthKernel >= 0);

                if (!canBuildClusteredLights)
                    return;

                DispatchClearLightLists(context.cmd);
                DispatchScreenSpaceAabb(context.cmd, canUseDepthBackplane);
                DispatchBigTilePrepass(context.cmd, canUseDepthBackplane);
                DispatchClearClusterAtomicIndex(context.cmd);
                DispatchClusteredLightList(context.cmd, canUseDepthBackplane);
            }
        }

        public override void Dispose()
        {
            ReleaseImportedBuffers();
            m_ClearLightListsCompute = null;
            m_ClearClusterAtomicIndexCompute = null;
            m_BuildScreenAabbCompute = null;
            m_BuildPerBigTileLightListCompute = null;
            m_BuildPerVoxelLightListCompute = null;
            m_ShaderVariablesLightListCB = default;
            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
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
            m_PunctualLightIndexCapacity = 0;
            m_ClusterLightIndexCapacity = 0;
            m_AreaLightListStartOffset = 0;
            m_ClusteredAreaLightCount = 0;
            m_ClusterBigTileLightIndexCapacity = 0;
            m_LayeredOffsetCapacity = 0;
            m_ClusterNearClip = 0.0f;
            m_ClusterFarClip = 0.0f;
            m_ClusterScale = 0.0f;
            m_ClusterIsOrthographic = 0;
            m_SupportsClusteredPunctualLights = false;
            m_ClearLightListsKernel = -1;
            m_ClearClusterAtomicIndexKernel = -1;
            m_BuildScreenAabbKernel = -1;
            m_BuildPerBigTileLightListKernel = -1;
            m_BuildPerVoxelLightListDepthKernel = -1;
            m_BuildPerVoxelLightListNoDepthKernel = -1;
            m_DirectionalLightUploadData = Array.Empty<VividLightData.DirectionalLightData>();
            m_LayeredOffsetUploadData = Array.Empty<uint>();
            m_AreaLightListUploadData = Array.Empty<uint>();
        }

        private void DispatchClearLightLists(ComputeCommandBuffer cmd)
        {
            DispatchClearLightList(cmd, m_BigTileLightListBuffer);
        }

        private void DispatchClearLightList(ComputeCommandBuffer cmd, RenderGraphBuffer buffer)
        {
            if (buffer == null || buffer.desc == null)
                return;

            cmd.SetComputeBufferParam(m_ClearLightListsCompute, m_ClearLightListsKernel, LightListToClearId, buffer.innerHandle);

            var remainingGroupCount = Mathf.CeilToInt(buffer.desc.Count / (float)ClearLightListThreadGroupSize);
            var dispatchOffset = 0;
            while (remainingGroupCount > 0)
            {
                var currentGroupCount = Mathf.Min(remainingGroupCount, MaxClearLightListDispatchGroups);
                cmd.SetComputeVectorParam(
                    m_ClearLightListsCompute,
                    LightListEntriesAndOffsetId,
                    new Vector4(buffer.desc.Count, dispatchOffset, 0.0f, 0.0f));
                cmd.DispatchCompute(m_ClearLightListsCompute, m_ClearLightListsKernel, currentGroupCount, 1, 1);
                remainingGroupCount -= currentGroupCount;
                dispatchOffset += currentGroupCount * ClearLightListThreadGroupSize;
            }
        }

        private void DispatchScreenSpaceAabb(ComputeCommandBuffer cmd,bool hasDepthTexture)
        {
            BindSharedLightLoopConstants(cmd, m_BuildScreenAabbCompute, hasDepthTexture);
            cmd.SetComputeBufferParam(m_BuildScreenAabbCompute, m_BuildScreenAabbKernel, FiniteLightBoundsId, m_FiniteLightBoundBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_BuildScreenAabbCompute, m_BuildScreenAabbKernel, ScreenSpaceBoundsId, m_ScreenSpaceBoundsBuffer.innerHandle);
            PushShaderVariablesLightList(cmd, m_BuildScreenAabbCompute);
            cmd.DispatchCompute(
                m_BuildScreenAabbCompute,
                m_BuildScreenAabbKernel,
                Mathf.Max(1, Mathf.CeilToInt(m_PunctualLightCount / (float)LightsPerScreenAabbGroup)),
                MaxViews,
                1);
        }

        private void DispatchBigTilePrepass(ComputeCommandBuffer cmd,bool hasDepthTexture)
        {
            BindSharedLightLoopConstants(cmd, m_BuildPerBigTileLightListCompute, hasDepthTexture);
            cmd.SetComputeBufferParam(m_BuildPerBigTileLightListCompute, m_BuildPerBigTileLightListKernel, ScreenSpaceBoundsId, m_ScreenSpaceBoundsBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_BuildPerBigTileLightListCompute, m_BuildPerBigTileLightListKernel, LightVolumeDataId, m_LightVolumeDataBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_BuildPerBigTileLightListCompute, m_BuildPerBigTileLightListKernel, FiniteLightBoundsId, m_FiniteLightBoundBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_BuildPerBigTileLightListCompute, m_BuildPerBigTileLightListKernel, PackedBigTileLightListId, m_BigTileLightListBuffer.innerHandle);
            PushShaderVariablesLightList(cmd, m_BuildPerBigTileLightListCompute);
            cmd.DispatchCompute(
                m_BuildPerBigTileLightListCompute,
                m_BuildPerBigTileLightListKernel,
                m_ClusterBigTileCountX,
                m_ClusterBigTileCountY,
                MaxViews);
        }

        private void DispatchClearClusterAtomicIndex(ComputeCommandBuffer cmd)
        {
            if (m_ClearClusterAtomicIndexCompute == null || m_ClearClusterAtomicIndexKernel < 0)
                return;

            cmd.SetComputeBufferParam(
                m_ClearClusterAtomicIndexCompute,
                m_ClearClusterAtomicIndexKernel,
                LayeredLightListCounterId,
                m_LayeredLightListCounterBuffer.innerHandle);
            cmd.DispatchCompute(m_ClearClusterAtomicIndexCompute, m_ClearClusterAtomicIndexKernel, 1, 1, 1);
        }

        private void DispatchClusteredLightList(ComputeCommandBuffer cmd, bool hasDepthTexture)
        {
            var kernel = hasDepthTexture ? m_BuildPerVoxelLightListDepthKernel : m_BuildPerVoxelLightListNoDepthKernel;
            if (kernel < 0)
                return;

            BindSharedLightLoopConstants(cmd, m_BuildPerVoxelLightListCompute, hasDepthTexture);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, ScreenSpaceBoundsId, m_ScreenSpaceBoundsBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, LightVolumeDataId, m_LightVolumeDataBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, FiniteLightBoundsId, m_FiniteLightBoundBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, BigTileLightListId, m_BigTileLightListBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, LayeredLightListId, m_LayeredLightListBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, LayeredOffsetId, m_LayeredOffsetBuffer.innerHandle);
            cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, LayeredLightListCounterId, m_LayeredLightListCounterBuffer.innerHandle);
            PushShaderVariablesLightList(cmd, m_BuildPerVoxelLightListCompute);

            if (hasDepthTexture)
            {
                cmd.SetComputeBufferParam(m_BuildPerVoxelLightListCompute, kernel, LogBaseBufferId, m_LogBaseBuffer.innerHandle);
                cmd.SetComputeTextureParam(m_BuildPerVoxelLightListCompute, kernel, DepthTextureId, m_DepthTexture.innerHandle);
                cmd.SetComputeTextureParam(m_BuildPerVoxelLightListCompute, kernel, HzbDepthTextureId, m_HzbDepthTexture.innerHandle);
            }

            cmd.DispatchCompute(
                m_BuildPerVoxelLightListCompute,
                kernel,
                m_ClusterTileCountX,
                m_ClusterTileCountY,
                MaxViews);
        }

        private void BindSharedLightLoopConstants(ComputeCommandBuffer cmd, ComputeShader computeShader, bool enableLogBaseBuffer)
        {
            cmd.SetComputeFloatParam(computeShader, ClusterScaleId, m_ClusterScale);
            cmd.SetComputeFloatParam(computeShader, ClusterBaseId, ClusterLogBase);
            cmd.SetComputeFloatParam(computeShader, NearPlaneId, m_ClusterNearClip);
            cmd.SetComputeFloatParam(computeShader, FarPlaneId, m_ClusterFarClip);
            cmd.SetComputeIntParam(computeShader, Log2NumClustersId, ClusterLog2SliceCount);
            cmd.SetComputeIntParam(computeShader, IsLogBaseBufferEnabledId, enableLogBaseBuffer ? 1 : 0);
            cmd.SetComputeIntParam(computeShader, NumTileClusteredXId, m_ClusterTileCountX);
            cmd.SetComputeIntParam(computeShader, NumTileClusteredYId, m_ClusterTileCountY);
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

        private void UpdateDirectionalLightUploadData(VividLightData lightData, Camera camera)
        {
            if (lightData == null)
                return;

            EnsureDirectionalLightUploadCapacity(m_DirectionalLightCount);
            Array.Copy(lightData.directionalLights, m_DirectionalLightUploadData, m_DirectionalLightCount);

            if (camera == null
                || !lightData.hasVisibleLights
                || !PhysicallyBasedSkyAtmosphericAttenuation.TryCreate(camera, out var attenuationContext))
            {
                return;
            }

            var directionalIndex = 0;
            for (var visibleLightIndex = 0;
                 visibleLightIndex < lightData.visibleLights.Length && directionalIndex < m_DirectionalLightCount;
                 visibleLightIndex++)
            {
                var visibleLight = lightData.visibleLights[visibleLightIndex];
                if (visibleLight.lightType != LightType.Directional)
                    continue;

                var light = visibleLight.light;
                if (ShouldDirectionalLightInteractWithSky(light))
                {
                    var attenuation = PhysicallyBasedSkyAtmosphericAttenuation.Evaluate(
                        in attenuationContext,
                        m_DirectionalLightUploadData[directionalIndex].directionWS);
                    var color = m_DirectionalLightUploadData[directionalIndex].color;
                    m_DirectionalLightUploadData[directionalIndex].color = new Vector3(
                        color.x * attenuation.x,
                        color.y * attenuation.y,
                        color.z * attenuation.z);
                }

                directionalIndex++;
            }
        }

        private void UploadAreaLightGridData()
        {
            if (m_LayeredOffsetImportedBuffer == null || m_LayeredLightListImportedBuffer == null || m_LayeredOffsetCapacity <= 0)
                return;

            EnsureLayeredOffsetUploadCapacity(m_LayeredOffsetCapacity);
            Array.Clear(m_LayeredOffsetUploadData, 0, m_LayeredOffsetCapacity);

            if (m_ClusteredAreaLightCount > 0)
            {
                var packedAreaLightCell = PackLightCell((uint)m_AreaLightListStartOffset, (uint)m_ClusteredAreaLightCount);
                var areaCategoryStart = (int)(HdrpLightCategoryArea * (uint)m_ClusterCount);
                for (var clusterIndex = 0; clusterIndex < m_ClusterCount; clusterIndex++)
                    m_LayeredOffsetUploadData[areaCategoryStart + clusterIndex] = packedAreaLightCell;

                EnsureAreaLightListUploadCapacity(m_ClusteredAreaLightCount);
                for (var lightIndex = 0; lightIndex < m_ClusteredAreaLightCount; lightIndex++)
                    m_AreaLightListUploadData[lightIndex] = (uint)lightIndex;

                m_LayeredLightListImportedBuffer.SetData(
                    m_AreaLightListUploadData,
                    0,
                    m_AreaLightListStartOffset,
                    m_ClusteredAreaLightCount);
            }

            m_LayeredOffsetImportedBuffer.SetData(m_LayeredOffsetUploadData, 0, 0, m_LayeredOffsetCapacity);
        }

        private void PushShaderVariablesLightList(ComputeCommandBuffer cmd, ComputeShader computeShader)
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
                ? CameraProjectionMatrixUtility.GetProjectionMatrix(camera)
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

        private void UpdateClusteredLightingFrameData(ContextContainer frameData)
        {
            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            clusteredLightingData.directionalLights = m_DirectionalLightBuffer;
            clusteredLightingData.punctualLights = m_PunctualLightBuffer;
            clusteredLightingData.areaLights = m_AreaLightBuffer;
            clusteredLightingData.layeredOffset = m_LayeredOffsetBuffer;
            clusteredLightingData.layeredLightList = m_LayeredLightListBuffer;
            clusteredLightingData.logBaseBuffer = m_LogBaseBuffer;
            clusteredLightingData.directionalLightCount = m_DirectionalLightCount;
            clusteredLightingData.punctualLightCount = m_PunctualLightCount;
            clusteredLightingData.areaLightCount = m_AreaLightCount;
            clusteredLightingData.mainDirectionalLightIndex = m_MainDirectionalLightIndex;
            clusteredLightingData.clusterTileSize = ClusterTileSize;
            clusteredLightingData.clusterSliceCount = ClusterSliceCount;
            clusteredLightingData.clusterTileCountX = m_ClusterTileCountX;
            clusteredLightingData.clusterTileCountY = m_ClusterTileCountY;
            clusteredLightingData.clusterNearClip = m_ClusterNearClip;
            clusteredLightingData.clusterFarClip = m_ClusterFarClip;
            clusteredLightingData.clusterIsOrthographic = m_ClusterIsOrthographic;
            clusteredLightingData.clusterScale = m_ClusterScale;
            clusteredLightingData.clusterBase = ClusterLogBase;
            clusteredLightingData.clusterLog2SliceCount = ClusterLog2SliceCount;
            clusteredLightingData.supportsClusteredPunctualLights = m_SupportsClusteredPunctualLights;
            clusteredLightingData.isLogBaseBufferEnabled = m_SupportsClusteredPunctualLights && HasUsableHzbDescriptor();
        }

        private bool CanBuildClusteredPunctualLights()
        {
            return m_PunctualLightCount > 0
                && m_ClearLightListsCompute != null
                && m_ClearClusterAtomicIndexCompute != null
                && m_BuildScreenAabbCompute != null
                && m_BuildPerBigTileLightListCompute != null
                && m_BuildPerVoxelLightListCompute != null
                && m_ClearLightListsKernel >= 0
                && m_ClearClusterAtomicIndexKernel >= 0
                && m_BuildScreenAabbKernel >= 0
                && m_BuildPerBigTileLightListKernel >= 0
                && (m_BuildPerVoxelLightListDepthKernel >= 0 || m_BuildPerVoxelLightListNoDepthKernel >= 0);
        }

        private bool HasUsableHzbDescriptor()
        {
            return m_HzbDepthTexture?.desc != null
                && m_HzbDepthTexture.desc.UseMipMap
                && m_HzbDepthTexture.desc.MipCount > 1;
        }

        private bool HasUsableHzbTexture()
        {
            return m_HzbDepthTexture?.innerHandle.IsValid() == true && HasUsableHzbDescriptor();
        }

        private void EnsureImportedBuffers()
        {
            EnsureImportedBuffer(ref m_DirectionalLightImportedBuffer, m_DirectionalLightBuffer);
            EnsureImportedBuffer(ref m_PunctualLightImportedBuffer, m_PunctualLightBuffer);
            EnsureImportedBuffer(ref m_AreaLightImportedBuffer, m_AreaLightBuffer);
            EnsureImportedBuffer(ref m_FiniteLightBoundImportedBuffer, m_FiniteLightBoundBuffer);
            EnsureImportedBuffer(ref m_LightVolumeDataImportedBuffer, m_LightVolumeDataBuffer);
            EnsureImportedBuffer(ref m_ScreenSpaceBoundsImportedBuffer, m_ScreenSpaceBoundsBuffer);
            EnsureImportedBuffer(ref m_BigTileLightListImportedBuffer, m_BigTileLightListBuffer);
            EnsureImportedBuffer(ref m_LayeredOffsetImportedBuffer, m_LayeredOffsetBuffer);
            EnsureImportedBuffer(ref m_LayeredLightListImportedBuffer, m_LayeredLightListBuffer);
            EnsureImportedBuffer(ref m_LayeredLightListCounterImportedBuffer, m_LayeredLightListCounterBuffer);
            EnsureImportedBuffer(ref m_LogBaseImportedBuffer, m_LogBaseBuffer);
        }

        private void ReleaseImportedBuffers()
        {
            ReleaseImportedBuffer(ref m_DirectionalLightImportedBuffer, m_DirectionalLightBuffer);
            ReleaseImportedBuffer(ref m_PunctualLightImportedBuffer, m_PunctualLightBuffer);
            ReleaseImportedBuffer(ref m_AreaLightImportedBuffer, m_AreaLightBuffer);
            ReleaseImportedBuffer(ref m_FiniteLightBoundImportedBuffer, m_FiniteLightBoundBuffer);
            ReleaseImportedBuffer(ref m_LightVolumeDataImportedBuffer, m_LightVolumeDataBuffer);
            ReleaseImportedBuffer(ref m_ScreenSpaceBoundsImportedBuffer, m_ScreenSpaceBoundsBuffer);
            ReleaseImportedBuffer(ref m_BigTileLightListImportedBuffer, m_BigTileLightListBuffer);
            ReleaseImportedBuffer(ref m_LayeredOffsetImportedBuffer, m_LayeredOffsetBuffer);
            ReleaseImportedBuffer(ref m_LayeredLightListImportedBuffer, m_LayeredLightListBuffer);
            ReleaseImportedBuffer(ref m_LayeredLightListCounterImportedBuffer, m_LayeredLightListCounterBuffer);
            ReleaseImportedBuffer(ref m_LogBaseImportedBuffer, m_LogBaseBuffer);
        }

        private static RenderGraphBuffer CreateStructuredBuffer(string name, int count, int stride)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = count,
                    Stride = stride,
                    Target = GraphicsBuffer.Target.Structured,
                    Name = name
                }
            };
        }


        private static void ResizeStructuredBuffer(RenderGraphBuffer buffer, int count, int stride)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = Mathf.Max(1, count);
            buffer.desc.Stride = stride;
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
        }

        private static void EnsureImportedBuffer(ref GraphicsBuffer graphicsBuffer, RenderGraphBuffer renderGraphBuffer)
        {
            if (renderGraphBuffer?.desc == null)
                return;

            var requiredCount = Mathf.Max(1, renderGraphBuffer.desc.Count);
            var requiredStride = Mathf.Max(1, renderGraphBuffer.desc.Stride);
            var requiredTarget = renderGraphBuffer.desc.Target;

            if (graphicsBuffer == null
                || graphicsBuffer.count < requiredCount
                || graphicsBuffer.stride != requiredStride)
            {
                graphicsBuffer?.Dispose();
                graphicsBuffer = new GraphicsBuffer(requiredTarget, requiredCount, requiredStride);
            }

            renderGraphBuffer.SetImportedBuffer(graphicsBuffer);
        }

        private static void ReleaseImportedBuffer(ref GraphicsBuffer graphicsBuffer, RenderGraphBuffer renderGraphBuffer)
        {
            renderGraphBuffer?.ClearImportedBuffer();
            graphicsBuffer?.Dispose();
            graphicsBuffer = null;
        }

        private void EnsureDirectionalLightUploadCapacity(int requiredCapacity)
        {
            if (requiredCapacity > m_DirectionalLightUploadData.Length)
                m_DirectionalLightUploadData = new VividLightData.DirectionalLightData[requiredCapacity];
        }

        private void EnsureLayeredOffsetUploadCapacity(int requiredCapacity)
        {
            if (requiredCapacity > m_LayeredOffsetUploadData.Length)
                m_LayeredOffsetUploadData = new uint[requiredCapacity];
        }

        private void EnsureAreaLightListUploadCapacity(int requiredCapacity)
        {
            if (requiredCapacity > m_AreaLightListUploadData.Length)
                m_AreaLightListUploadData = new uint[requiredCapacity];
        }

        private static bool ShouldDirectionalLightInteractWithSky(Light light)
        {
            if (light == null || light.type != LightType.Directional)
                return false;

            return !light.TryGetComponent<VividAdditionalLightData>(out var additionalLightData)
                   || additionalLightData.interactsWithSky;
        }

        private static int ComputeClusteredLightListCapacity(int clusterTileCount)
        {
            var perTileCapacity = (long)(HdrpFptlMaxLightCount + 1) * (1 << (ClusterLog2SliceCount + 1));
            return ClampToPositiveInt(perTileCapacity * Mathf.Max(clusterTileCount, 1));
        }

        private static int ComputeCombinedClusteredLightListCapacity(int punctualCapacity, int areaLightCount)
        {
            return ClampToPositiveInt((long)Mathf.Max(punctualCapacity, 1) + Mathf.Max(areaLightCount, 0));
        }

        private static int ComputeBigTileLightListCapacity(int bigTileCount)
        {
            return ClampToPositiveInt((long)MaxNrBigTileLightsPlusOne * Mathf.Max(bigTileCount, 1) / 2L);
        }

        private static int ComputeLayeredOffsetCapacity(int clusterCount)
        {
            return ClampToPositiveInt((long)HdrpLightCategoryCount * Mathf.Max(clusterCount, 1));
        }

        private static uint PackLightCell(uint offset, uint count)
        {
            var safeOffset = offset & LightClusterPackingOffsetMask;
            var safeCount = Mathf.Min((int)count, (int)LightClusterPackingCountMask);
            return safeOffset | ((uint)safeCount << LightClusterPackingOffsetBits);
        }

        private static int ClampToPositiveInt(long value)
        {
            return Mathf.Max(1, (int)Math.Min(value, int.MaxValue));
        }
    }
}
