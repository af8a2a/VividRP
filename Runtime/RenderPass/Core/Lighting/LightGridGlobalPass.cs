using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class LightGridGlobalPass : UnsafePass, IAllowGlobalStateModificationPass
    {
        private static readonly int DirectionalLightsId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId = Shader.PropertyToID("_MainDirectionalLightIndex");
        private static readonly int PunctualLightsId = Shader.PropertyToID("_PunctualLights");
        private static readonly int PunctualLightCountId = Shader.PropertyToID("_PunctualLightCount");
        private static readonly int AreaLightsId = Shader.PropertyToID("_AreaLights");
        private static readonly int AreaLightCountId = Shader.PropertyToID("_AreaLightCount");
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

        [RenderGraphResource(Name = "DirectionalLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_DirectionalLightBuffer;

        [RenderGraphResource(Name = "PunctualLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_PunctualLightBuffer;

        [RenderGraphResource(Name = "AreaLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_AreaLightBuffer;

        [RenderGraphResource(Name = "LayeredOffset", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredOffsetBuffer;

        [RenderGraphResource(Name = "LayeredLightList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredLightListBuffer;

        [RenderGraphResource(Name = "LogBaseBuffer", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LogBaseBuffer;
        private int m_DirectionalLightCount;
        private int m_PunctualLightCount;
        private int m_AreaLightCount;
        private int m_MainDirectionalLightIndex;
        private int m_ClusterTileSize;
        private int m_ClusterSliceCount;
        private int m_ClusterTileCountX;
        private int m_ClusterTileCountY;
        private float m_ClusterNearClip;
        private float m_ClusterFarClip;
        private int m_ClusterIsOrthographic;
        private float m_ClusterScale;
        private float m_ClusterBase;
        private int m_ClusterLog2SliceCount;
        private bool m_SupportsClusteredPunctualLights;
        private bool m_SupportsClusteredAreaLights;
        private bool m_IsLogBaseBufferEnabled;

        public LightGridGlobalPass()
        {
            profilingSampler = new ProfilingSampler(nameof(LightGridGlobalPass));
            m_DirectionalLightBuffer = CreateStructuredBuffer("DirectionalLights", VividLightData.DirectionalLightData.Stride);
            m_PunctualLightBuffer = CreateStructuredBuffer("PunctualLights", VividLightData.PunctualLightData.Stride);
            m_AreaLightBuffer = CreateStructuredBuffer("AreaLights", VividLightData.AreaLightData.Stride);
            m_LayeredOffsetBuffer = CreateStructuredBuffer("LayeredOffset", sizeof(uint));
            m_LayeredLightListBuffer = CreateStructuredBuffer("LayeredLightList", sizeof(uint));
            m_LogBaseBuffer = CreateStructuredBuffer("LogBaseBuffer", sizeof(float));
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            m_DirectionalLightBuffer = clusteredLightingData.directionalLights;
            m_PunctualLightBuffer = clusteredLightingData.punctualLights;
            m_AreaLightBuffer = clusteredLightingData.areaLights;
            m_LayeredOffsetBuffer = clusteredLightingData.layeredOffset;
            m_LayeredLightListBuffer = clusteredLightingData.layeredLightList;
            m_LogBaseBuffer = clusteredLightingData.logBaseBuffer;
            m_DirectionalLightCount = clusteredLightingData.directionalLightCount;
            m_PunctualLightCount = clusteredLightingData.punctualLightCount;
            m_AreaLightCount = clusteredLightingData.areaLightCount;
            m_MainDirectionalLightIndex = clusteredLightingData.mainDirectionalLightIndex;
            m_ClusterTileSize = clusteredLightingData.clusterTileSize;
            m_ClusterSliceCount = clusteredLightingData.clusterSliceCount;
            m_ClusterTileCountX = clusteredLightingData.clusterTileCountX;
            m_ClusterTileCountY = clusteredLightingData.clusterTileCountY;
            m_ClusterNearClip = clusteredLightingData.clusterNearClip;
            m_ClusterFarClip = clusteredLightingData.clusterFarClip;
            m_ClusterIsOrthographic = clusteredLightingData.clusterIsOrthographic;
            m_ClusterScale = clusteredLightingData.clusterScale;
            m_ClusterBase = clusteredLightingData.clusterBase;
            m_ClusterLog2SliceCount = clusteredLightingData.clusterLog2SliceCount;
            m_SupportsClusteredPunctualLights = clusteredLightingData.supportsClusteredPunctualLights;
            m_SupportsClusteredAreaLights = HasAreaLightResources();
            m_IsLogBaseBufferEnabled = clusteredLightingData.isLogBaseBufferEnabled;
        }

        public override void Record(UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetGlobalInt(DirectionalLightCountId, m_DirectionalLightCount);
                cmd.SetGlobalInt(MainDirectionalLightIndexId, m_MainDirectionalLightIndex);
                cmd.SetGlobalInt(PunctualLightCountId, m_SupportsClusteredPunctualLights ? m_PunctualLightCount : 0);
                cmd.SetGlobalInt(AreaLightCountId, m_SupportsClusteredAreaLights ? m_AreaLightCount : 0);
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

                if (m_DirectionalLightBuffer?.ImportedGraphicsBuffer != null)
                    cmd.SetGlobalBuffer(DirectionalLightsId, m_DirectionalLightBuffer.ImportedGraphicsBuffer);

                if (m_PunctualLightBuffer?.ImportedGraphicsBuffer != null)
                    cmd.SetGlobalBuffer(PunctualLightsId, m_PunctualLightBuffer.ImportedGraphicsBuffer);

                if (m_SupportsClusteredAreaLights && m_AreaLightBuffer?.ImportedGraphicsBuffer != null)
                    cmd.SetGlobalBuffer(AreaLightsId, m_AreaLightBuffer.ImportedGraphicsBuffer);

                if (m_LayeredOffsetBuffer?.ImportedGraphicsBuffer != null)
                    cmd.SetGlobalBuffer(LayeredOffsetId, m_LayeredOffsetBuffer.ImportedGraphicsBuffer);

                if (m_LayeredLightListBuffer?.ImportedGraphicsBuffer != null)
                    cmd.SetGlobalBuffer(LayeredLightListId, m_LayeredLightListBuffer.ImportedGraphicsBuffer);

                if (m_LogBaseBuffer?.ImportedGraphicsBuffer != null)
                    cmd.SetGlobalBuffer(LogBaseBufferId, m_LogBaseBuffer.ImportedGraphicsBuffer);
            }
        }

        public override void Dispose()
        {
            m_DirectionalLightBuffer = null;
            m_PunctualLightBuffer = null;
            m_AreaLightBuffer = null;
            m_LayeredOffsetBuffer = null;
            m_LayeredLightListBuffer = null;
            m_LogBaseBuffer = null;
            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
            m_MainDirectionalLightIndex = -1;
            m_ClusterTileSize = 0;
            m_ClusterSliceCount = 0;
            m_ClusterTileCountX = 0;
            m_ClusterTileCountY = 0;
            m_ClusterNearClip = 0.0f;
            m_ClusterFarClip = 0.0f;
            m_ClusterIsOrthographic = 0;
            m_ClusterScale = 0.0f;
            m_ClusterBase = 0.0f;
            m_ClusterLog2SliceCount = 0;
            m_SupportsClusteredPunctualLights = false;
            m_SupportsClusteredAreaLights = false;
            m_IsLogBaseBufferEnabled = false;
        }

        private bool HasAreaLightResources()
        {
            return m_AreaLightBuffer?.ImportedGraphicsBuffer != null
                && m_LayeredOffsetBuffer?.ImportedGraphicsBuffer != null
                && m_LayeredLightListBuffer?.ImportedGraphicsBuffer != null;
        }

        private static RenderGraphBuffer CreateStructuredBuffer(string name, int stride)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = 1,
                    Stride = stride,
                    Target = GraphicsBuffer.Target.Structured,
                    Name = name
                }
            };
        }
    }
}
