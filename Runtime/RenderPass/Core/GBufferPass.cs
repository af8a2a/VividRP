using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class GBufferPass : RasterPass, IAllowGlobalStateModificationPass
    {
        internal const string GBufferShaderTagName = "VividGBuffer";
        internal const string GPUDrivenDecalGBufferShaderTagName = "VividGBufferGPUDrivenDecal";

        private static readonly string[] s_DefaultGBufferShaderTagNames =
        {
            GBufferShaderTagName,
        };

        private static readonly string[] s_GPUDrivenDecalGBufferShaderTagNames =
        {
            GPUDrivenDecalGBufferShaderTagName,
        };

        private static readonly int DecalDataId = Shader.PropertyToID("_DecalData");
        private static readonly int ClusteredDecalGridEnabledId = Shader.PropertyToID("_ClusteredDecalGridEnabled");
        private static readonly int LayeredLightListId = Shader.PropertyToID("g_vLayeredLightList");
        private static readonly int LayeredOffsetId = Shader.PropertyToID("g_LayeredOffset");
        private static readonly int LogBaseBufferId = Shader.PropertyToID("g_logBaseBuffer");
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
        private static readonly VividLightData.DecalClusterData[] s_DefaultDecalBufferData =
        {
            new()
            {
                baseColorTextureIndex = uint.MaxValue,
                normalTextureIndex = uint.MaxValue,
            }
        };
        private static readonly uint[] s_DefaultUintBufferData = { 0u };
        private static readonly float[] s_DefaultFloatBufferData = { 0.0f };

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(Name = "DecalData", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_DecalDataBuffer;

        [RenderGraphResource(Name = "LayeredOffset", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredOffsetBuffer;

        [RenderGraphResource(Name = "LayeredLightList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredLightListBuffer;

        [RenderGraphResource(Name = "LogBaseBuffer", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LogBaseBuffer;

        [RenderGraphResource(
            Name = "GBuffer0",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(
            Name = "GBuffer1",
            Access = AccessFlags.Write,
            AttachmentIndex = 1,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(
            Name = "GBuffer2",
            Access = AccessFlags.Write,
            AttachmentIndex = 2,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer2;

        [RenderGraphResource(
            Name = "GBuffer3",
            Access = AccessFlags.Write,
            AttachmentIndex = 3,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer3;

        [RenderGraphResource(
            Name = "GBuffer4",
            Access = AccessFlags.Write,
            AttachmentIndex = 4,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer4;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_GBufferDepth;

        private readonly RenderGraphBuffer m_LocalDecalDataBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredOffsetBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredLightListBuffer;
        private readonly RenderGraphBuffer m_LocalLogBaseBuffer;
        private RenderGraphBuffer m_ResolvedDecalDataBuffer;
        private RenderGraphBuffer m_ResolvedLayeredOffsetBuffer;
        private RenderGraphBuffer m_ResolvedLayeredLightListBuffer;
        private RenderGraphBuffer m_ResolvedLogBaseBuffer;
        private bool m_GPUDrivenDecalEnabled;
        private bool m_SupportsClusteredDecals;
        private bool m_IsLogBaseBufferEnabled;
        private int m_ClusterTileSize = LightGridPass.ClusterTileSize;
        private int m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
        private int m_ClusterTileCountX = 1;
        private int m_ClusterTileCountY = 1;
        private float m_ClusterNearClip = 0.1f;
        private float m_ClusterFarClip = 1000.0f;
        private int m_ClusterIsOrthographic;
        private float m_ClusterScale;
        private float m_ClusterBase = LightGridPass.ClusterLogBase;
        private int m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;

        public GBufferPass()
        {
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateOpaque(GBufferShaderTagName)
            };
            m_RenderList.desc.RendererConfiguration = PerObjectData.Lightmaps;

            m_LocalDecalDataBuffer = RenderGraphBuffer.CreateStructured("DecalData", VividLightData.DecalClusterData.Stride);
            m_LocalLayeredOffsetBuffer = RenderGraphBuffer.CreateStructured("LayeredOffset", sizeof(uint));
            m_LocalLayeredLightListBuffer = RenderGraphBuffer.CreateStructured("LayeredLightList", sizeof(uint));
            m_LocalLogBaseBuffer = RenderGraphBuffer.CreateStructured("LogBaseBuffer", sizeof(float));
            m_DecalDataBuffer = m_LocalDecalDataBuffer;
            m_LayeredOffsetBuffer = m_LocalLayeredOffsetBuffer;
            m_LayeredLightListBuffer = m_LocalLayeredLightListBuffer;
            m_LogBaseBuffer = m_LocalLogBaseBuffer;
            m_ResolvedDecalDataBuffer = m_LocalDecalDataBuffer;
            m_ResolvedLayeredOffsetBuffer = m_LocalLayeredOffsetBuffer;
            m_ResolvedLayeredLightListBuffer = m_LocalLayeredLightListBuffer;
            m_ResolvedLogBaseBuffer = m_LocalLogBaseBuffer;

            m_GBuffer0 = RenderGraphTexture.CreateColorTarget("GBuffer0", GraphicsFormat.R8G8B8A8_SRGB);
            m_GBuffer1 = RenderGraphTexture.CreateColorTarget("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_GBuffer2 = RenderGraphTexture.CreateColorTarget("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer3 = RenderGraphTexture.CreateColorTarget("GBuffer3", GraphicsFormat.B10G11R11_UFloatPack32);
            m_GBuffer3.desc.EnableRandomWrite = true;
            m_GBuffer4 = RenderGraphTexture.CreateColorTarget("GBuffer4", GraphicsFormat.R16G16B16A16_SFloat);
            m_GBufferDepth = RenderGraphTexture.CreateDepthTarget("GBufferDepth");
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            m_GBuffer0.Resize(width, height);
            m_GBuffer1.Resize(width, height);
            m_GBuffer2.Resize(width, height);
            m_GBuffer3.Resize(width, height);
            m_GBuffer4.Resize(width, height);
            m_GBufferDepth.Resize(width, height);

            UpdateRenderListShaderTags(frameData);
            PrepareClusteredDecalParameters(frameData, width, height);
        }

        public override void Record(RasterPassContext context)
        {
            if (m_RenderList == null || !m_RenderList.IsValid)
                return;

            ApplyClusteredDecalProperties(context.cmd);
            context.cmd.DrawRendererList(m_RenderList);
        }

        public override void Dispose()
        {
            m_ResolvedDecalDataBuffer = m_LocalDecalDataBuffer;
            m_ResolvedLayeredOffsetBuffer = m_LocalLayeredOffsetBuffer;
            m_ResolvedLayeredLightListBuffer = m_LocalLayeredLightListBuffer;
            m_ResolvedLogBaseBuffer = m_LocalLogBaseBuffer;
            m_LocalDecalDataBuffer?.ClearImportedBuffer();
            m_LocalLayeredOffsetBuffer?.ClearImportedBuffer();
            m_LocalLayeredLightListBuffer?.ClearImportedBuffer();
            m_LocalLogBaseBuffer?.ClearImportedBuffer();
            m_GPUDrivenDecalEnabled = false;
            m_SupportsClusteredDecals = false;
            m_IsLogBaseBufferEnabled = false;
            m_ClusterTileSize = LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = 1;
            m_ClusterTileCountY = 1;
            m_ClusterNearClip = 0.1f;
            m_ClusterFarClip = 1000.0f;
            m_ClusterIsOrthographic = 0;
            m_ClusterScale = 0.0f;
            m_ClusterBase = LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;
        }

        internal static bool ShouldUseGPUDrivenDecalShaderTag(ContextContainer frameData)
        {
            return frameData != null
                && frameData.Contains<VividGPUDrivenDecalData>()
                && frameData.Get<VividGPUDrivenDecalData>().isEnabled;
        }

        private void UpdateRenderListShaderTags(ContextContainer frameData)
        {
            if (m_RenderList?.desc == null)
                return;

            m_RenderList.desc.ShaderTagNames = ShouldUseGPUDrivenDecalShaderTag(frameData)
                ? (string[])s_GPUDrivenDecalGBufferShaderTagNames.Clone()
                : (string[])s_DefaultGBufferShaderTagNames.Clone();
        }

        private void PrepareClusteredDecalParameters(ContextContainer frameData, int width, int height)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var camera = cameraData.camera;
            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();

            ResolveClusteredDecalBuffers(clusteredLightingData);

            m_GPUDrivenDecalEnabled = ShouldUseGPUDrivenDecalShaderTag(frameData);
            m_SupportsClusteredDecals = false;
            m_IsLogBaseBufferEnabled = false;
            m_ClusterTileSize = LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = Mathf.Max(1, Mathf.CeilToInt(width / (float)m_ClusterTileSize));
            m_ClusterTileCountY = Mathf.Max(1, Mathf.CeilToInt(height / (float)m_ClusterTileSize));
            m_ClusterNearClip = Mathf.Max(camera != null ? camera.nearClipPlane : 0.1f, 0.01f);
            m_ClusterFarClip = Mathf.Max(camera != null ? camera.farClipPlane : 1000.0f, m_ClusterNearClip + 0.01f);
            m_ClusterIsOrthographic = camera != null && camera.orthographic ? 1 : 0;
            m_ClusterScale = 0.0f;
            m_ClusterBase = LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;

            if (!m_GPUDrivenDecalEnabled)
                return;

            if (!HasClusteredLightingData(clusteredLightingData))
                return;

            m_ClusterTileSize = clusteredLightingData.clusterTileSize > 0
                ? clusteredLightingData.clusterTileSize
                : LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = clusteredLightingData.clusterSliceCount > 0
                ? clusteredLightingData.clusterSliceCount
                : LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = clusteredLightingData.clusterTileCountX > 0
                ? clusteredLightingData.clusterTileCountX
                : Mathf.Max(1, Mathf.CeilToInt(width / (float)Mathf.Max(m_ClusterTileSize, 1)));
            m_ClusterTileCountY = clusteredLightingData.clusterTileCountY > 0
                ? clusteredLightingData.clusterTileCountY
                : Mathf.Max(1, Mathf.CeilToInt(height / (float)Mathf.Max(m_ClusterTileSize, 1)));
            m_ClusterNearClip = clusteredLightingData.clusterNearClip > 0.0f
                ? clusteredLightingData.clusterNearClip
                : m_ClusterNearClip;
            m_ClusterFarClip = clusteredLightingData.clusterFarClip > m_ClusterNearClip
                ? clusteredLightingData.clusterFarClip
                : Mathf.Max(m_ClusterNearClip + 0.01f, m_ClusterFarClip);
            m_ClusterIsOrthographic = clusteredLightingData.clusterIsOrthographic;
            m_ClusterScale = clusteredLightingData.clusterScale;
            m_ClusterBase = clusteredLightingData.clusterBase > 0.0f
                ? clusteredLightingData.clusterBase
                : LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = clusteredLightingData.clusterLog2SliceCount > 0
                ? clusteredLightingData.clusterLog2SliceCount
                : LightGridPass.ClusterLog2SliceCount;

            var supportsClusteredFiniteLights = clusteredLightingData.supportsClusteredPunctualLights;
            m_SupportsClusteredDecals = supportsClusteredFiniteLights
                && clusteredLightingData.decalCount > 0
                && HasBoundDecalResources();
            m_IsLogBaseBufferEnabled = m_SupportsClusteredDecals
                && clusteredLightingData.isLogBaseBufferEnabled
                && !ReferenceEquals(m_ResolvedLogBaseBuffer, m_LocalLogBaseBuffer);
        }

        private void ApplyClusteredDecalProperties(RasterCommandBuffer cmd)
        {
            if (cmd == null)
                return;

            cmd.SetGlobalInt(ClusteredDecalGridEnabledId, m_SupportsClusteredDecals ? 1 : 0);

            if (!m_GPUDrivenDecalEnabled)
                return;

            cmd.SetGlobalInt(ClusterTileSizeId, m_ClusterTileSize);
            cmd.SetGlobalInt(ClusterSliceCountId, m_ClusterSliceCount);
            cmd.SetGlobalInt(ClusterTileCountXId, m_ClusterTileCountX);
            cmd.SetGlobalInt(ClusterTileCountYId, m_ClusterTileCountY);
            cmd.SetGlobalInt(ClusterIsOrthographicId, m_ClusterIsOrthographic);
            cmd.SetGlobalFloat(ClusterNearClipId, m_ClusterNearClip);
            cmd.SetGlobalFloat(ClusterFarClipId, m_ClusterFarClip);
            cmd.SetGlobalFloat(ClusterScaleId, m_ClusterScale);
            cmd.SetGlobalFloat(ClusterBaseId, m_ClusterBase);
            cmd.SetGlobalFloat(NearPlaneId, m_ClusterNearClip);
            cmd.SetGlobalFloat(FarPlaneId, m_ClusterFarClip);
            cmd.SetGlobalInt(Log2NumClustersId, m_ClusterLog2SliceCount);
            cmd.SetGlobalInt(IsLogBaseBufferEnabledId, m_IsLogBaseBufferEnabled ? 1 : 0);
            cmd.SetGlobalInt(NumTileClusteredXId, m_ClusterTileCountX);
            cmd.SetGlobalInt(NumTileClusteredYId, m_ClusterTileCountY);

            SetGlobalBuffer(cmd, DecalDataId, m_ResolvedDecalDataBuffer, m_LocalDecalDataBuffer, s_DefaultDecalBufferData);
            SetGlobalBuffer(cmd, LayeredOffsetId, m_ResolvedLayeredOffsetBuffer, m_LocalLayeredOffsetBuffer, s_DefaultUintBufferData);
            SetGlobalBuffer(cmd, LayeredLightListId, m_ResolvedLayeredLightListBuffer, m_LocalLayeredLightListBuffer, s_DefaultUintBufferData);
            SetGlobalBuffer(cmd, LogBaseBufferId, m_ResolvedLogBaseBuffer, m_LocalLogBaseBuffer, s_DefaultFloatBufferData);
        }

        private bool HasBoundDecalResources()
        {
            return !ReferenceEquals(m_ResolvedDecalDataBuffer, m_LocalDecalDataBuffer)
                && !ReferenceEquals(m_ResolvedLayeredOffsetBuffer, m_LocalLayeredOffsetBuffer)
                && !ReferenceEquals(m_ResolvedLayeredLightListBuffer, m_LocalLayeredLightListBuffer)
                && !ReferenceEquals(m_ResolvedLogBaseBuffer, m_LocalLogBaseBuffer)
                && m_ResolvedDecalDataBuffer?.ImportedGraphicsBuffer != null
                && m_ResolvedLayeredOffsetBuffer?.ImportedGraphicsBuffer != null
                && m_ResolvedLayeredLightListBuffer?.ImportedGraphicsBuffer != null
                && m_ResolvedLogBaseBuffer?.ImportedGraphicsBuffer != null;
        }

        private static bool HasClusteredLightingData(VividClusteredLightingData clusteredLightingData)
        {
            return clusteredLightingData != null
                && (clusteredLightingData.decalData != null
                    || clusteredLightingData.layeredOffset != null
                    || clusteredLightingData.layeredLightList != null
                    || clusteredLightingData.logBaseBuffer != null
                    || clusteredLightingData.clusterTileSize > 0
                    || clusteredLightingData.clusterSliceCount > 0
                    || clusteredLightingData.decalCount > 0);
        }

        private void ResolveClusteredDecalBuffers(VividClusteredLightingData clusteredLightingData)
        {
            m_ResolvedDecalDataBuffer = ResolveClusteredBuffer(
                m_DecalDataBuffer,
                m_LocalDecalDataBuffer,
                clusteredLightingData?.decalData);
            m_ResolvedLayeredOffsetBuffer = ResolveClusteredBuffer(
                m_LayeredOffsetBuffer,
                m_LocalLayeredOffsetBuffer,
                clusteredLightingData?.layeredOffset);
            m_ResolvedLayeredLightListBuffer = ResolveClusteredBuffer(
                m_LayeredLightListBuffer,
                m_LocalLayeredLightListBuffer,
                clusteredLightingData?.layeredLightList);
            m_ResolvedLogBaseBuffer = ResolveClusteredBuffer(
                m_LogBaseBuffer,
                m_LocalLogBaseBuffer,
                clusteredLightingData?.logBaseBuffer);
        }

        private static RenderGraphBuffer ResolveClusteredBuffer(
            RenderGraphBuffer graphBuffer,
            RenderGraphBuffer localFallback,
            RenderGraphBuffer frameBuffer)
        {
            if (graphBuffer != null && !ReferenceEquals(graphBuffer, localFallback))
                return graphBuffer;

            return frameBuffer ?? localFallback;
        }

        private static void SetGlobalBuffer(
            RasterCommandBuffer cmd,
            int propertyId,
            RenderGraphBuffer buffer,
            RenderGraphBuffer fallbackBuffer,
            Array fallbackData)
        {
            var graphicsBuffer = buffer?.ImportedGraphicsBuffer;

            if (graphicsBuffer == null && fallbackBuffer != null)
            {
                if (fallbackData != null)
                    fallbackBuffer.SetData(fallbackData);
                else
                    fallbackBuffer.EnsureImportedBuffer();

                graphicsBuffer = fallbackBuffer.ImportedGraphicsBuffer;
            }

            if (graphicsBuffer != null)
                cmd.SetGlobalBuffer(propertyId, graphicsBuffer);
        }
    }
}
