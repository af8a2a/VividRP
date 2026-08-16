using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Experimental.Material
{
    public sealed class ExperimentalClosureDeferredLightingPass
        : ComputePass, IStablePassResourceLayout
    {
        private static readonly string[] KernelNames =
        {
            "ExperimentalClosureLit_Fast",
            "ExperimentalClosureLit_Single",
            "ExperimentalClosureLit_Complex"
        };

        private static readonly int[] ClosureBufferIds =
        {
            Shader.PropertyToID("_ExperimentalClosureBuffer0"),
            Shader.PropertyToID("_ExperimentalClosureBuffer1"),
            Shader.PropertyToID("_ExperimentalClosureBuffer2"),
            Shader.PropertyToID("_ExperimentalClosureBuffer3"),
            Shader.PropertyToID("_ExperimentalClosureBuffer4"),
            Shader.PropertyToID("_ExperimentalClosureBuffer5")
        };

        private static readonly int DepthTextureId =
            Shader.PropertyToID("_DepthTexture");
        private static readonly int DirectionalShadowTextureId =
            Shader.PropertyToID("_DirectionalShadowTexture");
        private static readonly int GTAOTextureId =
            Shader.PropertyToID("_GTAOTexture");
        private static readonly int ScreenSpaceReflectionTextureId =
            Shader.PropertyToID("_ScreenSpaceReflectionTexture");
        private static readonly int ScreenSpaceReflectionEnabledId =
            Shader.PropertyToID("_ScreenSpaceReflectionEnabled");
        private static readonly int SkyTextureId =
            Shader.PropertyToID("_SkyTexture");
        private static readonly int SkyTextureTintId =
            Shader.PropertyToID("_SkyTextureTint");
        private static readonly int SkyTextureParamsId =
            Shader.PropertyToID("_SkyTextureParams");
        private static readonly int TileListId =
            Shader.PropertyToID("_ExperimentalClosureTileList");
        private static readonly int TileListOffsetId =
            Shader.PropertyToID("_ExperimentalClosureTileListOffset");
        private static readonly int LightingTextureId =
            Shader.PropertyToID("_ExperimentalClosureLightingTexture");
        private static readonly int DebugTextureId =
            Shader.PropertyToID("_ExperimentalClosureDebugTexture");
        private static readonly int DirectionalLightsId =
            Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId =
            Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId =
            Shader.PropertyToID("_MainDirectionalLightIndex");
        private static readonly int PunctualLightsId =
            Shader.PropertyToID("_PunctualLights");
        private static readonly int PunctualLightCountId =
            Shader.PropertyToID("_PunctualLightCount");
        private static readonly int AreaLightsId =
            Shader.PropertyToID("_AreaLights");
        private static readonly int AreaLightCountId =
            Shader.PropertyToID("_AreaLightCount");
        private static readonly int ReflectionProbesId =
            Shader.PropertyToID("_ReflectionProbes");
        private static readonly int ReflectionProbeCountId =
            Shader.PropertyToID("_ReflectionProbeCount");
        private static readonly int DecalCountId =
            Shader.PropertyToID("_DecalCount");
        private static readonly int ClusteredPunctualLightGridEnabledId =
            Shader.PropertyToID("_ClusteredPunctualLightGridEnabled");
        private static readonly int ClusteredAreaLightGridEnabledId =
            Shader.PropertyToID("_ClusteredAreaLightGridEnabled");
        private static readonly int ClusteredReflectionProbeGridEnabledId =
            Shader.PropertyToID("_ClusteredReflectionProbeGridEnabled");
        private static readonly int ClusteredDecalGridEnabledId =
            Shader.PropertyToID("_ClusteredDecalGridEnabled");
        private static readonly int LayeredLightListId =
            Shader.PropertyToID("g_vLayeredLightList");
        private static readonly int LayeredOffsetId =
            Shader.PropertyToID("g_LayeredOffset");
        private static readonly int LogBaseBufferId =
            Shader.PropertyToID("g_logBaseBuffer");
        private static readonly int ClusterScaleId =
            Shader.PropertyToID("g_fClustScale");
        private static readonly int ClusterBaseId =
            Shader.PropertyToID("g_fClustBase");
        private static readonly int NearPlaneId =
            Shader.PropertyToID("g_fNearPlane");
        private static readonly int FarPlaneId =
            Shader.PropertyToID("g_fFarPlane");
        private static readonly int Log2NumClustersId =
            Shader.PropertyToID("g_iLog2NumClusters");
        private static readonly int IsLogBaseBufferEnabledId =
            Shader.PropertyToID("g_isLogBaseBufferEnabled");
        private static readonly int NumTileClusteredXId =
            Shader.PropertyToID("_NumTileClusteredX");
        private static readonly int NumTileClusteredYId =
            Shader.PropertyToID("_NumTileClusteredY");
        private static readonly int ClusterTileSizeId =
            Shader.PropertyToID("_ClusterTileSize");
        private static readonly int ClusterSliceCountId =
            Shader.PropertyToID("_ClusterSliceCount");
        private static readonly int ClusterTileCountXId =
            Shader.PropertyToID("_ClusterTileCountX");
        private static readonly int ClusterTileCountYId =
            Shader.PropertyToID("_ClusterTileCountY");
        private static readonly int ClusterNearClipId =
            Shader.PropertyToID("_ClusterNearClip");
        private static readonly int ClusterFarClipId =
            Shader.PropertyToID("_ClusterFarClip");
        private static readonly int ClusterIsOrthographicId =
            Shader.PropertyToID("_ClusterIsOrthographic");

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer0",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer0;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer1",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer1;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer2",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer2;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer3",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer3;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer4",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer4;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer5",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer5;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "DirectionalShadowTexture",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_DirectionalShadowTexture;

        [RenderGraphResource(Name = "GTAOTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GTAOTexture;

        [RenderGraphResource(
            Name = "ScreenSpaceReflectionOutput",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ScreenSpaceReflectionTexture;

        [RenderGraphResource(Name = "SkyIBLCubemap", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SkyIBLCubemap;

        [RenderGraphResource(
            Name = "ExperimentalClosureTileList",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_TileList;

        [RenderGraphResource(
            Name = "ExperimentalClosureIndirectArgs",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_IndirectArgs;

        [RenderGraphResource(Name = "DirectionalLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_DirectionalLights;

        [RenderGraphResource(Name = "PunctualLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_PunctualLights;

        [RenderGraphResource(Name = "AreaLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_AreaLights;

        [RenderGraphResource(Name = "ReflectionProbes", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReflectionProbes;

        [RenderGraphResource(Name = "LayeredOffset", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredOffset;

        [RenderGraphResource(Name = "LayeredLightList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredLightList;

        [RenderGraphResource(Name = "LogBaseBuffer", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LogBaseBuffer;

        [RenderGraphResource(
            Name = "ExperimentalClosureLighting",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_LightingTexture;

        [RenderGraphResource(
            Name = "ExperimentalClosureDebug",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DebugTexture;

        private readonly RenderGraphTexture m_LocalDirectionalShadowTexture;
        private readonly RenderGraphTexture m_LocalGTAOTexture;
        private readonly RenderGraphTexture m_LocalScreenSpaceReflectionTexture;
        private readonly RenderGraphBuffer m_LocalDirectionalLights;
        private readonly RenderGraphBuffer m_LocalPunctualLights;
        private readonly RenderGraphBuffer m_LocalAreaLights;
        private readonly RenderGraphBuffer m_LocalReflectionProbes;
        private readonly RenderGraphBuffer m_LocalLayeredOffset;
        private readonly RenderGraphBuffer m_LocalLayeredLightList;
        private readonly RenderGraphBuffer m_LocalLogBaseBuffer;
        private readonly int[] m_Kernels = { -1, -1, -1 };
        private ComputeShader m_Compute;
        private int m_Width = 1;
        private int m_Height = 1;
        private int m_TileCount = 1;
        private int m_DirectionalLightCount;
        private int m_PunctualLightCount;
        private int m_AreaLightCount;
        private int m_ReflectionProbeCount;
        private int m_MainDirectionalLightIndex = -1;
        private int m_ClusterTileSize = LightGridPass.ClusterTileSize;
        private int m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
        private int m_ClusterTileCountX = 1;
        private int m_ClusterTileCountY = 1;
        private float m_ClusterNearClip = 0.1f;
        private float m_ClusterFarClip = 1000.0f;
        private int m_ClusterIsOrthographic;
        private float m_ClusterScale;
        private float m_ClusterBase = LightGridPass.ClusterLogBase;
        private int m_ClusterLog2SliceCount =
            LightGridPass.ClusterLog2SliceCount;
        private bool m_SupportsClusteredPunctualLights;
        private bool m_SupportsClusteredAreaLights;
        private bool m_SupportsClusteredReflectionProbes;
        private bool m_IsLogBaseBufferEnabled;
        private RenderGraphTexture m_FrameContextScreenSpaceReflectionTexture;
        private bool m_IsPassResourceLayoutDirty;
        private Color m_SkyTextureTint = Color.white;
        private Vector4 m_SkyTextureParams;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public ExperimentalClosureDeferredLightingPass()
        {
            m_ClosureBuffer0 = CreateClosureInput(
                "ExperimentalClosureBuffer0",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_ClosureBuffer1 = CreateClosureInput(
                "ExperimentalClosureBuffer1",
                GraphicsFormat.A2B10G10R10_UNormPack32);
            m_ClosureBuffer2 = CreateClosureInput(
                "ExperimentalClosureBuffer2",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_ClosureBuffer3 = CreateClosureInput(
                "ExperimentalClosureBuffer3",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_ClosureBuffer4 = CreateClosureInput(
                "ExperimentalClosureBuffer4",
                GraphicsFormat.B10G11R11_UFloatPack32);
            m_ClosureBuffer5 = CreateClosureInput(
                "ExperimentalClosureBuffer5",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DepthTexture = RenderGraphTexture.CreateInput(
                "Depth",
                GraphicsFormat.None,
                DepthBits.Depth32);
            m_LocalDirectionalShadowTexture = CreateFallbackTexture(
                "DirectionalShadowTexture",
                GraphicsFormat.R16_SFloat,
                Color.white);
            m_DirectionalShadowTexture = m_LocalDirectionalShadowTexture;
            m_LocalGTAOTexture = CreateFallbackTexture(
                "GTAOTexture",
                GraphicsFormat.R8_UNorm,
                Color.white);
            m_GTAOTexture = m_LocalGTAOTexture;
            m_LocalScreenSpaceReflectionTexture = CreateFallbackTexture(
                "ScreenSpaceReflectionOutput",
                GraphicsFormat.R16G16B16A16_SFloat,
                Color.clear);
            m_ScreenSpaceReflectionTexture =
                m_LocalScreenSpaceReflectionTexture;
            m_SkyIBLCubemap = CreateSkyIBLCubemapTexture("SkyIBLCubemap");
            m_TileList = RenderGraphBuffer.CreateStructured(
                "ExperimentalClosureTileList",
                sizeof(uint));
            m_IndirectArgs = RenderGraphBuffer.CreateStructured(
                "ExperimentalClosureIndirectArgs",
                ExperimentalClosureClassificationPass.VariantCount
                    * ExperimentalClosureClassificationPass.IndirectArgsElementCount,
                sizeof(uint),
                GraphicsBuffer.Target.Structured
                    | GraphicsBuffer.Target.IndirectArguments);
            m_LocalDirectionalLights = RenderGraphBuffer.CreateStructured(
                "DirectionalLights",
                VividLightData.DirectionalLightData.Stride);
            m_LocalPunctualLights = RenderGraphBuffer.CreateStructured(
                "PunctualLights",
                VividLightData.PunctualLightData.Stride);
            m_LocalAreaLights = RenderGraphBuffer.CreateStructured(
                "AreaLights",
                VividLightData.AreaLightData.Stride);
            m_LocalReflectionProbes = RenderGraphBuffer.CreateStructured(
                "ReflectionProbes",
                VividLightData.ReflectionProbeData.Stride);
            m_LocalLayeredOffset = RenderGraphBuffer.CreateStructured(
                "LayeredOffset",
                sizeof(uint));
            m_LocalLayeredLightList = RenderGraphBuffer.CreateStructured(
                "LayeredLightList",
                sizeof(uint));
            m_LocalLogBaseBuffer = RenderGraphBuffer.CreateStructured(
                "LogBaseBuffer",
                sizeof(float));
            m_DirectionalLights = m_LocalDirectionalLights;
            m_PunctualLights = m_LocalPunctualLights;
            m_AreaLights = m_LocalAreaLights;
            m_ReflectionProbes = m_LocalReflectionProbes;
            m_LayeredOffset = m_LocalLayeredOffset;
            m_LayeredLightList = m_LocalLayeredLightList;
            m_LogBaseBuffer = m_LocalLogBaseBuffer;
            m_LightingTexture = CreateOutput("ExperimentalClosureLighting");
            m_DebugTexture = CreateOutput("ExperimentalClosureDebug");
        }

        public override void Create()
        {
            m_Compute = PipelineResourceManager.Get<VividRPCoreResources>()
                ?.ExperimentalClosureDeferredLitCompute;
            if (m_Compute == null)
                return;

            for (var i = 0; i < KernelNames.Length; i++)
                m_Kernels[i] = m_Compute.FindKernel(KernelNames[i]);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_Width = cameraData.actualWidth > 0
                ? cameraData.actualWidth
                : cameraData.pixelWidth;
            m_Height = cameraData.actualHeight > 0
                ? cameraData.actualHeight
                : cameraData.pixelHeight;

            if (m_Width <= 0)
                m_Width = Mathf.Max(1, Screen.width);
            if (m_Height <= 0)
                m_Height = Mathf.Max(1, Screen.height);

            PrepareScreenSpaceReflectionResource(frameData);

            for (var i = 0; i < ClosureBufferIds.Length; i++)
                GetClosureBuffer(i).Resize(m_Width, m_Height);
            m_DepthTexture.Resize(m_Width, m_Height);
            m_DirectionalShadowTexture.Resize(m_Width, m_Height);
            m_GTAOTexture.Resize(m_Width, m_Height);
            m_ScreenSpaceReflectionTexture.Resize(m_Width, m_Height);
            m_LightingTexture.Resize(m_Width, m_Height);
            m_DebugTexture.Resize(m_Width, m_Height);

            var tileCountX = Mathf.Max(
                1,
                (m_Width + ExperimentalClosureClassificationPass.TileSize - 1)
                    / ExperimentalClosureClassificationPass.TileSize);
            var tileCountY = Mathf.Max(
                1,
                (m_Height + ExperimentalClosureClassificationPass.TileSize - 1)
                    / ExperimentalClosureClassificationPass.TileSize);
            m_TileCount = Mathf.Max(1, tileCountX * tileCountY);
            PrepareClusteredLightingParameters(frameData);
            PrepareSkyTextureState(frameData.GetOrCreate<VividSkyData>());
        }

        public override void Record(ComputePassContext context)
        {
            if (m_Compute == null)
                return;

            var cmd = context.cmd;
            for (var variant = 0; variant < m_Kernels.Length; variant++)
            {
                var kernel = m_Kernels[variant];
                if (kernel < 0)
                    continue;

                BindKernel(context, cmd, kernel, variant);
                var indirectArgsOffset = (uint)(
                    variant
                    * ExperimentalClosureClassificationPass.IndirectArgsElementCount
                    * sizeof(uint));
                cmd.DispatchCompute(
                    m_Compute,
                    kernel,
                    m_IndirectArgs,
                    indirectArgsOffset);
            }
        }

        public override void Dispose()
        {
            m_Compute = null;
            for (var i = 0; i < m_Kernels.Length; i++)
                m_Kernels[i] = -1;
            m_SkyIBLCubemap.ClearImportedHandle();
            ResetLightingState();
            m_ScreenSpaceReflectionTexture =
                m_LocalScreenSpaceReflectionTexture;
            m_FrameContextScreenSpaceReflectionTexture = null;
            m_IsPassResourceLayoutDirty = false;
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        private void BindKernel(
            ComputePassContext context,
            ComputeCommandBuffer cmd,
            int kernel,
            int variant)
        {
            for (var i = 0; i < ClosureBufferIds.Length; i++)
            {
                cmd.SetComputeTextureParam(
                    m_Compute,
                    kernel,
                    ClosureBufferIds[i],
                    GetClosureBuffer(i).innerHandle);
            }

            cmd.SetComputeTextureParam(
                m_Compute,
                kernel,
                DepthTextureId,
                m_DepthTexture.innerHandle);
            BindLightingTextures(context, cmd, kernel);
            cmd.SetComputeTextureParam(
                m_Compute,
                kernel,
                LightingTextureId,
                m_LightingTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_Compute,
                kernel,
                DebugTextureId,
                m_DebugTexture.innerHandle);
            cmd.SetComputeBufferParam(
                m_Compute,
                kernel,
                TileListId,
                m_TileList.innerHandle);
            BindLightLoopParameters(cmd, kernel);
            cmd.SetComputeIntParam(
                m_Compute,
                TileListOffsetId,
                variant * m_TileCount);
        }

        private void BindLightingTextures(
            ComputePassContext context,
            ComputeCommandBuffer cmd,
            int kernel)
        {
            var defaultResources = context.renderGraphContext.defaultResources;
            cmd.SetComputeTextureParam(
                m_Compute,
                kernel,
                DirectionalShadowTextureId,
                IsBoundTexture(
                    m_DirectionalShadowTexture,
                    m_LocalDirectionalShadowTexture)
                    ? m_DirectionalShadowTexture.innerHandle
                    : defaultResources.whiteTexture);
            cmd.SetComputeTextureParam(
                m_Compute,
                kernel,
                GTAOTextureId,
                IsBoundTexture(m_GTAOTexture, m_LocalGTAOTexture)
                    ? m_GTAOTexture.innerHandle
                    : defaultResources.whiteTexture);

            var hasScreenSpaceReflection = IsBoundTexture(
                m_ScreenSpaceReflectionTexture,
                m_LocalScreenSpaceReflectionTexture);
            cmd.SetComputeTextureParam(
                m_Compute,
                kernel,
                ScreenSpaceReflectionTextureId,
                hasScreenSpaceReflection
                    ? m_ScreenSpaceReflectionTexture.innerHandle
                    : defaultResources.blackTexture);
            cmd.SetComputeIntParam(
                m_Compute,
                ScreenSpaceReflectionEnabledId,
                hasScreenSpaceReflection ? 1 : 0);

            cmd.SetComputeTextureParam(
                m_Compute,
                kernel,
                SkyTextureId,
                m_SkyIBLCubemap.innerHandle);
            cmd.SetComputeVectorParam(
                m_Compute,
                SkyTextureTintId,
                m_SkyTextureTint);
            cmd.SetComputeVectorParam(
                m_Compute,
                SkyTextureParamsId,
                m_SkyTextureParams);
        }

        private void BindLightLoopParameters(
            ComputeCommandBuffer cmd,
            int kernel)
        {
            cmd.SetComputeIntParam(
                m_Compute,
                DirectionalLightCountId,
                m_DirectionalLightCount);
            cmd.SetComputeIntParam(
                m_Compute,
                MainDirectionalLightIndexId,
                m_MainDirectionalLightIndex);
            cmd.SetComputeIntParam(
                m_Compute,
                PunctualLightCountId,
                m_PunctualLightCount);
            cmd.SetComputeIntParam(
                m_Compute,
                AreaLightCountId,
                m_AreaLightCount);
            cmd.SetComputeIntParam(
                m_Compute,
                ReflectionProbeCountId,
                m_ReflectionProbeCount);
            cmd.SetComputeIntParam(m_Compute, DecalCountId, 0);
            cmd.SetComputeIntParam(
                m_Compute,
                ClusteredPunctualLightGridEnabledId,
                m_SupportsClusteredPunctualLights ? 1 : 0);
            cmd.SetComputeIntParam(
                m_Compute,
                ClusteredAreaLightGridEnabledId,
                m_SupportsClusteredAreaLights ? 1 : 0);
            cmd.SetComputeIntParam(
                m_Compute,
                ClusteredReflectionProbeGridEnabledId,
                m_SupportsClusteredReflectionProbes ? 1 : 0);
            cmd.SetComputeIntParam(
                m_Compute,
                ClusteredDecalGridEnabledId,
                0);
            cmd.SetComputeIntParam(
                m_Compute,
                ClusterTileSizeId,
                m_ClusterTileSize);
            cmd.SetComputeIntParam(
                m_Compute,
                ClusterSliceCountId,
                m_ClusterSliceCount);
            cmd.SetComputeIntParam(
                m_Compute,
                ClusterTileCountXId,
                m_ClusterTileCountX);
            cmd.SetComputeIntParam(
                m_Compute,
                ClusterTileCountYId,
                m_ClusterTileCountY);
            cmd.SetComputeIntParam(
                m_Compute,
                ClusterIsOrthographicId,
                m_ClusterIsOrthographic);
            cmd.SetComputeFloatParam(
                m_Compute,
                ClusterNearClipId,
                m_ClusterNearClip);
            cmd.SetComputeFloatParam(
                m_Compute,
                ClusterFarClipId,
                m_ClusterFarClip);
            cmd.SetComputeFloatParam(
                m_Compute,
                ClusterScaleId,
                m_ClusterScale);
            cmd.SetComputeFloatParam(
                m_Compute,
                ClusterBaseId,
                m_ClusterBase);
            cmd.SetComputeFloatParam(
                m_Compute,
                NearPlaneId,
                m_ClusterNearClip);
            cmd.SetComputeFloatParam(
                m_Compute,
                FarPlaneId,
                m_ClusterFarClip);
            cmd.SetComputeIntParam(
                m_Compute,
                Log2NumClustersId,
                m_ClusterLog2SliceCount);
            cmd.SetComputeIntParam(
                m_Compute,
                IsLogBaseBufferEnabledId,
                m_IsLogBaseBufferEnabled ? 1 : 0);
            cmd.SetComputeIntParam(
                m_Compute,
                NumTileClusteredXId,
                m_ClusterTileCountX);
            cmd.SetComputeIntParam(
                m_Compute,
                NumTileClusteredYId,
                m_ClusterTileCountY);

            SetLightLoopBuffer(
                cmd,
                kernel,
                DirectionalLightsId,
                m_DirectionalLights);
            SetLightLoopBuffer(
                cmd,
                kernel,
                PunctualLightsId,
                m_PunctualLights);
            SetLightLoopBuffer(
                cmd,
                kernel,
                AreaLightsId,
                m_AreaLights);
            SetLightLoopBuffer(
                cmd,
                kernel,
                ReflectionProbesId,
                m_ReflectionProbes);
            SetLightLoopBuffer(
                cmd,
                kernel,
                LayeredOffsetId,
                m_LayeredOffset);
            SetLightLoopBuffer(
                cmd,
                kernel,
                LayeredLightListId,
                m_LayeredLightList);
            SetLightLoopBuffer(
                cmd,
                kernel,
                LogBaseBufferId,
                m_LogBaseBuffer);
        }

        private void PrepareClusteredLightingParameters(
            ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var clusteredLightingData =
                frameData.GetOrCreate<VividClusteredLightingData>();
            var camera = cameraData.camera;

            ResetLightingState();
            m_ClusterTileCountX = Mathf.Max(
                1,
                Mathf.CeilToInt(m_Width / (float)m_ClusterTileSize));
            m_ClusterTileCountY = Mathf.Max(
                1,
                Mathf.CeilToInt(m_Height / (float)m_ClusterTileSize));
            m_ClusterNearClip = Mathf.Max(
                camera != null ? camera.nearClipPlane : 0.1f,
                0.01f);
            m_ClusterFarClip = Mathf.Max(
                camera != null ? camera.farClipPlane : 1000.0f,
                m_ClusterNearClip + 0.01f);
            m_ClusterIsOrthographic =
                camera != null && camera.orthographic ? 1 : 0;

            if (!HasClusteredLightingData(clusteredLightingData))
                return;

            m_MainDirectionalLightIndex =
                clusteredLightingData.mainDirectionalLightIndex;
            m_ClusterTileSize = clusteredLightingData.clusterTileSize > 0
                ? clusteredLightingData.clusterTileSize
                : LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = clusteredLightingData.clusterSliceCount > 0
                ? clusteredLightingData.clusterSliceCount
                : LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = clusteredLightingData.clusterTileCountX > 0
                ? clusteredLightingData.clusterTileCountX
                : Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        m_Width / (float)Mathf.Max(m_ClusterTileSize, 1)));
            m_ClusterTileCountY = clusteredLightingData.clusterTileCountY > 0
                ? clusteredLightingData.clusterTileCountY
                : Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        m_Height / (float)Mathf.Max(m_ClusterTileSize, 1)));
            m_ClusterNearClip = clusteredLightingData.clusterNearClip > 0.0f
                ? clusteredLightingData.clusterNearClip
                : m_ClusterNearClip;
            m_ClusterFarClip =
                clusteredLightingData.clusterFarClip > m_ClusterNearClip
                    ? clusteredLightingData.clusterFarClip
                    : Mathf.Max(
                        m_ClusterNearClip + 0.01f,
                        m_ClusterFarClip);
            m_ClusterIsOrthographic =
                clusteredLightingData.clusterIsOrthographic;
            m_ClusterScale = clusteredLightingData.clusterScale;
            m_ClusterBase = clusteredLightingData.clusterBase > 0.0f
                ? clusteredLightingData.clusterBase
                : LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount =
                clusteredLightingData.clusterLog2SliceCount > 0
                    ? clusteredLightingData.clusterLog2SliceCount
                    : LightGridPass.ClusterLog2SliceCount;

            if (HasBoundDirectionalLightBuffer())
            {
                m_DirectionalLightCount = Mathf.Max(
                    0,
                    clusteredLightingData.directionalLightCount);
            }
            else
            {
                m_MainDirectionalLightIndex = -1;
            }

            var supportsClusteredFiniteLights =
                clusteredLightingData.supportsClusteredPunctualLights;
            m_SupportsClusteredPunctualLights =
                supportsClusteredFiniteLights
                && clusteredLightingData.punctualLightCount > 0
                && HasBoundPunctualLightResources();
            m_PunctualLightCount = m_SupportsClusteredPunctualLights
                ? Mathf.Max(0, clusteredLightingData.punctualLightCount)
                : 0;
            m_SupportsClusteredAreaLights =
                supportsClusteredFiniteLights
                && clusteredLightingData.areaLightCount > 0
                && HasBoundAreaLightResources();
            m_AreaLightCount = m_SupportsClusteredAreaLights
                ? Mathf.Max(0, clusteredLightingData.areaLightCount)
                : 0;
            m_SupportsClusteredReflectionProbes =
                supportsClusteredFiniteLights
                && clusteredLightingData.reflectionProbeCount > 0
                && HasBoundReflectionProbeResources();
            m_ReflectionProbeCount = m_SupportsClusteredReflectionProbes
                ? Mathf.Max(0, clusteredLightingData.reflectionProbeCount)
                : 0;
            m_IsLogBaseBufferEnabled = supportsClusteredFiniteLights
                && clusteredLightingData.isLogBaseBufferEnabled
                && !ReferenceEquals(m_LogBaseBuffer, m_LocalLogBaseBuffer);
        }

        private void ResetLightingState()
        {
            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
            m_ReflectionProbeCount = 0;
            m_MainDirectionalLightIndex = -1;
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
            m_SupportsClusteredPunctualLights = false;
            m_SupportsClusteredAreaLights = false;
            m_SupportsClusteredReflectionProbes = false;
            m_IsLogBaseBufferEnabled = false;
        }

        private bool HasBoundDirectionalLightBuffer()
        {
            return !ReferenceEquals(
                m_DirectionalLights,
                m_LocalDirectionalLights);
        }

        private bool HasBoundPunctualLightResources()
        {
            return !ReferenceEquals(m_PunctualLights, m_LocalPunctualLights)
                && !ReferenceEquals(m_LayeredOffset, m_LocalLayeredOffset)
                && !ReferenceEquals(
                    m_LayeredLightList,
                    m_LocalLayeredLightList)
                && !ReferenceEquals(m_LogBaseBuffer, m_LocalLogBaseBuffer);
        }

        private bool HasBoundAreaLightResources()
        {
            return !ReferenceEquals(m_AreaLights, m_LocalAreaLights)
                && !ReferenceEquals(m_LayeredOffset, m_LocalLayeredOffset)
                && !ReferenceEquals(
                    m_LayeredLightList,
                    m_LocalLayeredLightList);
        }

        private bool HasBoundReflectionProbeResources()
        {
            return !ReferenceEquals(
                    m_ReflectionProbes,
                    m_LocalReflectionProbes)
                && !ReferenceEquals(m_LayeredOffset, m_LocalLayeredOffset)
                && !ReferenceEquals(
                    m_LayeredLightList,
                    m_LocalLayeredLightList);
        }

        private static bool HasClusteredLightingData(
            VividClusteredLightingData clusteredLightingData)
        {
            return clusteredLightingData != null
                && (clusteredLightingData.directionalLights != null
                    || clusteredLightingData.punctualLights != null
                    || clusteredLightingData.areaLights != null
                    || clusteredLightingData.reflectionProbes != null
                    || clusteredLightingData.clusterTileSize > 0
                    || clusteredLightingData.clusterSliceCount > 0
                    || clusteredLightingData.reflectionProbeCount > 0
                    || clusteredLightingData.areaLightCount > 0);
        }

        private void SetLightLoopBuffer(
            ComputeCommandBuffer cmd,
            int kernel,
            int propertyId,
            RenderGraphBuffer buffer)
        {
            if (buffer == null || !buffer.innerHandle.IsValid())
                return;

            cmd.SetComputeBufferParam(
                m_Compute,
                kernel,
                propertyId,
                buffer.innerHandle);
        }

        private void PrepareSkyTextureState(VividSkyData skyData)
        {
            var hasActiveSky =
                skyData != null && skyData.activeSkyType != SkyType.None;
            var skyMaxMip = hasActiveSky
                ? SkyManager.GetSpecularCubemapMaxMip(skyData)
                : 0;
            SkyManager.ImportSpecularCubemap(m_SkyIBLCubemap, skyData);
            m_SkyTextureTint = hasActiveSky ? skyData.tint : Color.white;
            m_SkyTextureParams = new Vector4(
                Mathf.Max(hasActiveSky ? skyData.exposure : 1.0f, 0.0f),
                hasActiveSky ? skyData.rotation : 0.0f,
                Mathf.Max(skyMaxMip, 0),
                hasActiveSky ? 1.0f : 0.0f);
        }

        private void PrepareScreenSpaceReflectionResource(
            ContextContainer frameData)
        {
            if (!ReferenceEquals(
                    m_ScreenSpaceReflectionTexture,
                    m_LocalScreenSpaceReflectionTexture)
                && !ReferenceEquals(
                    m_ScreenSpaceReflectionTexture,
                    m_FrameContextScreenSpaceReflectionTexture))
            {
                return;
            }

            var resolvedTexture = m_LocalScreenSpaceReflectionTexture;
            if (frameData != null
                && frameData.Contains<VividScreenSpaceReflectionData>())
            {
                var screenSpaceReflectionData =
                    frameData.Get<VividScreenSpaceReflectionData>();
                if (screenSpaceReflectionData?.hasValidTexture == true
                    && screenSpaceReflectionData.reflectionTexture != null)
                {
                    resolvedTexture =
                        screenSpaceReflectionData.reflectionTexture;
                }
            }

            if (ReferenceEquals(
                    m_ScreenSpaceReflectionTexture,
                    resolvedTexture))
            {
                return;
            }

            m_ScreenSpaceReflectionTexture = resolvedTexture;
            m_FrameContextScreenSpaceReflectionTexture = ReferenceEquals(
                resolvedTexture,
                m_LocalScreenSpaceReflectionTexture)
                ? null
                : resolvedTexture;
            m_IsPassResourceLayoutDirty = true;
        }

        private static bool IsBoundTexture(
            RenderGraphTexture texture,
            RenderGraphTexture localFallback)
        {
            return texture != null
                && !ReferenceEquals(texture, localFallback)
                && texture.innerHandle.IsValid();
        }

        private static RenderGraphTexture CreateClosureInput(
            string name,
            GraphicsFormat format)
        {
            return RenderGraphTexture.CreateInput(name, format);
        }

        private static RenderGraphTexture CreateFallbackTexture(
            string name,
            GraphicsFormat format,
            Color clearColor)
        {
            var texture = RenderGraphTexture.CreateColorTarget(name, format);
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = clearColor;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static RenderGraphTexture CreateSkyIBLCubemapTexture(
            string name)
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    Dimension = TextureDimension.Cube,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    DepthBufferBits = DepthBits.None,
                    FilterMode = FilterMode.Trilinear,
                    WrapMode = TextureWrapMode.Clamp,
                    UseMipMap = true,
                    AutoGenerateMips = false,
                    Name = name
                }
            };
        }

        private RenderGraphTexture GetClosureBuffer(int index)
        {
            return index switch
            {
                0 => m_ClosureBuffer0,
                1 => m_ClosureBuffer1,
                2 => m_ClosureBuffer2,
                3 => m_ClosureBuffer3,
                4 => m_ClosureBuffer4,
                5 => m_ClosureBuffer5,
                _ => null,
            };
        }

        private static RenderGraphTexture CreateOutput(string name)
        {
            var texture = RenderGraphTexture.CreateOutput(
                name,
                GraphicsFormat.R16G16B16A16_SFloat);
            texture.desc.EnableRandomWrite = true;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            return texture;
        }
    }
}
