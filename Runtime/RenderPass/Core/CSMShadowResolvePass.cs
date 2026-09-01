using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class CSMShadowResolvePass : ComputePass
    {
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const int ScreenSpaceShadowTileSize = 16;
        private const int IndirectDispatchArgsElementCount = 3;
        private const int BendWaveSize = 64;
        private const int BendMaxDispatchCount = 8;
        private const string KernelName = "CSMShadowResolve";
        private const string ClearTilesKernelName = "CSMShadowClearTiles";
        private const string ClassifyTilesKernelName = "CSMShadowClassifyTiles";
        private const string ResolveTilesKernelName = "CSMShadowResolveTiles";
        private const string CopyFilterSourceKernelName = "CSMShadowCopyFilterSource";
        private const string BilateralFilterHKernelName = "CSMShadowBilateralFilterH";
        private const string BilateralFilterVKernelName = "CSMShadowBilateralFilterV";
        private const string BendCompositeLowKernelName = "CSMShadowBendCompositeLow";
        private const string BendCompositeMediumKernelName = "CSMShadowBendCompositeMedium";
        private const string BendCompositeHighKernelName = "CSMShadowBendCompositeHigh";
        private const string BendCompositeVeryHighKernelName = "CSMShadowBendCompositeVeryHigh";

        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int CSMShadowAtlasId = Shader.PropertyToID("_CSMShadowAtlas");
        private static readonly int VSMPrototypePhysicalPageId = Shader.PropertyToID("_VSMPrototypePhysicalPage");
        private static readonly int VSMPrototypePageTableId = Shader.PropertyToID("_VSMPrototypePageTable");
        private static readonly int VSMPrototypeEnabledId = Shader.PropertyToID("_VSMPrototypeEnabled");
        private static readonly int VSMPrototypePageSizeId = Shader.PropertyToID("_VSMPrototypePageSize");
        private static readonly int VSMPrototypeVirtualResolutionId = Shader.PropertyToID("_VSMPrototypeVirtualResolution");
        private static readonly int VSMPrototypePagesPerAxisId = Shader.PropertyToID("_VSMPrototypePagesPerAxis");
        private static readonly int VSMPrototypePhysicalPagesPerRowId = Shader.PropertyToID("_VSMPrototypePhysicalPagesPerRow");
        private static readonly int DirectionalShadowTextureId = Shader.PropertyToID("_DirectionalShadowTexture");
        private static readonly int CSMShadowTileListId = Shader.PropertyToID("_CSMShadowTileList");
        private static readonly int CSMShadowDispatchIndirectArgsId = Shader.PropertyToID("_CSMShadowDispatchIndirectArgs");
        private static readonly int CSMShadowFilterSourceId = Shader.PropertyToID("_CSMShadowFilterSource");
        private static readonly int CSMShadowFilterTextureId = Shader.PropertyToID("_CSMShadowFilterTexture");
        private static readonly int CSMViewProjMatricesId = Shader.PropertyToID("_CSMViewProjMatrices");
        private static readonly int CSMCascadeSpheresId = Shader.PropertyToID("_CSMCascadeSpheres");
        private static readonly int CSMCascadeCountId = Shader.PropertyToID("_CSMCascadeCount");
        private static readonly int CSMMaxShadowDistanceId = Shader.PropertyToID("_CSMMaxShadowDistance");
        private static readonly int CSMNormalBiasId = Shader.PropertyToID("_CSMNormalBias");
        private static readonly int CSMInvViewProjMatrixId = Shader.PropertyToID("_CSMInvViewProjMatrix");
        private static readonly int CSMOutputWidthId = Shader.PropertyToID("_CSMOutputWidth");
        private static readonly int CSMOutputHeightId = Shader.PropertyToID("_CSMOutputHeight");
        private static readonly int CSMLightDirectionWSId = Shader.PropertyToID("_CSMLightDirectionWS");
        private static readonly int CSMCascadeResolutionId = Shader.PropertyToID("_CSMCascadeResolution");
        private static readonly int CSMCascadeWorldTexelSizesId = Shader.PropertyToID("_CSMCascadeWorldTexelSizes");
        private static readonly int CSMCascadeBordersId = Shader.PropertyToID("_CSMCascadeBorders");
        private static readonly int CSMShadowQualityId = Shader.PropertyToID("_CSMShadowQuality");
        private static readonly int CSMLightAngularDiameterId = Shader.PropertyToID("_CSMLightAngularDiameter");
        private static readonly int CSMFrameIndexId = Shader.PropertyToID("_CSMFrameIndex");
        private static readonly int CSMPCSSBlockerSampleCountId = Shader.PropertyToID("_CSMPCSSBlockerSampleCount");
        private static readonly int CSMPCSSFilterSampleCountId = Shader.PropertyToID("_CSMPCSSFilterSampleCount");
        private static readonly int CSMPCSSMaxPenumbraSizeId = Shader.PropertyToID("_CSMPCSSMaxPenumbraSize");
        private static readonly int CSMPCSSMaxSamplingDistanceId = Shader.PropertyToID("_CSMPCSSMaxSamplingDistance");
        private static readonly int CSMPCSSMinFilterSizeTexelsId = Shader.PropertyToID("_CSMPCSSMinFilterSizeTexels");
        private static readonly int CSMPCSSMinFilterMaxAngularDiameterId = Shader.PropertyToID("_CSMPCSSMinFilterMaxAngularDiameter");
        private static readonly int CSMPCSSBlockerSearchAngularDiameterId = Shader.PropertyToID("_CSMPCSSBlockerSearchAngularDiameter");
        private static readonly int CSMPCSSBlockerSamplingClumpExponentId = Shader.PropertyToID("_CSMPCSSBlockerSamplingClumpExponent");
        private static readonly int CSMBendLightCoordinateId = Shader.PropertyToID("_CSMBendLightCoordinate");
        private static readonly int CSMBendWaveOffsetId = Shader.PropertyToID("_CSMBendWaveOffset");
        private static readonly int CSMBendDepthTextureSizeId = Shader.PropertyToID("_CSMBendDepthTextureSize");
        private static readonly int CSMBendViewProjMatrixId = Shader.PropertyToID("_CSMBendViewProjMatrix");
        private static readonly int CSMBendMaxRayDistanceId = Shader.PropertyToID("_CSMBendMaxRayDistance");
        private static readonly int CSMBendSurfaceThicknessId = Shader.PropertyToID("_CSMBendSurfaceThickness");
        private static readonly int CSMBendBilinearThresholdId = Shader.PropertyToID("_CSMBendBilinearThreshold");
        private static readonly int CSMBendShadowContrastId = Shader.PropertyToID("_CSMBendShadowContrast");
        private static readonly int CSMBendIgnoreEdgePixelsId = Shader.PropertyToID("_CSMBendIgnoreEdgePixels");
        private static readonly int CSMBendUsePrecisionOffsetId = Shader.PropertyToID("_CSMBendUsePrecisionOffset");
        private static readonly int CSMBendBilinearSamplingOffsetModeId = Shader.PropertyToID("_CSMBendBilinearSamplingOffsetMode");

        private static readonly string[] s_BendCompositeKernelNames =
        {
            BendCompositeLowKernelName,
            BendCompositeMediumKernelName,
            BendCompositeHighKernelName,
            BendCompositeVeryHighKernelName
        };

        internal struct BendDispatchData
        {
            public Vector3Int WaveCount;
            public Vector2Int WaveOffset;
        }

        internal readonly struct BendDispatchList
        {
            public BendDispatchList(Vector4 lightCoordinate, BendDispatchData[] dispatches, int dispatchCount)
            {
                LightCoordinate = lightCoordinate;
                Dispatches = dispatches;
                DispatchCount = dispatchCount;
            }

            public Vector4 LightCoordinate { get; }

            public BendDispatchData[] Dispatches { get; }

            public int DispatchCount { get; }
        }

        internal readonly struct BendQualitySettings
        {
            public BendQualitySettings(
                float surfaceThickness,
                float bilinearThreshold,
                float shadowContrast,
                float maxRayDistance = VividAdditionalLightData.DefaultDirLightBendSSSMaxRayDistance,
                bool ignoreEdgePixels = false,
                bool usePrecisionOffset = false,
                bool bilinearSamplingOffsetMode = false)
            {
                SurfaceThickness = surfaceThickness;
                BilinearThreshold = bilinearThreshold;
                ShadowContrast = shadowContrast;
                MaxRayDistance = maxRayDistance;
                IgnoreEdgePixels = ignoreEdgePixels;
                UsePrecisionOffset = usePrecisionOffset;
                BilinearSamplingOffsetMode = bilinearSamplingOffsetMode;
            }

            public float SurfaceThickness { get; }

            public float BilinearThreshold { get; }

            public float ShadowContrast { get; }

            public float MaxRayDistance { get; }

            public bool IgnoreEdgePixels { get; }

            public bool UsePrecisionOffset { get; }

            public bool BilinearSamplingOffsetMode { get; }
        }

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.Read)]
        private RenderGraphTexture m_CSMShadowAtlas;

        [RenderGraphResource(Name = "DirectionalShadowTexture", Access = AccessFlags.Write)]
        private RenderGraphTexture m_DirectionalShadowTexture;

        [RenderGraphResource(Name = "CSMShadowTileList", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphBuffer m_TileListBuffer;

        [RenderGraphResource(Name = "CSMShadowDispatchIndirectArgs", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphBuffer m_DispatchIndirectArgsBuffer;

        [RenderGraphResource(Name = "CSMShadowFilterTexture", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_FilterTexture;

        private ComputeShader m_ResolveCompute;
        private int m_Kernel = -1;
        private int m_ClearTilesKernel = -1;
        private int m_ClassifyTilesKernel = -1;
        private int m_ResolveTilesKernel = -1;
        private int m_CopyFilterSourceKernel = -1;
        private int m_BilateralFilterHKernel = -1;
        private int m_BilateralFilterVKernel = -1;
        private readonly int[] m_BendCompositeKernels = { -1, -1, -1, -1 };
        private readonly BendDispatchData[] m_BendDispatches = new BendDispatchData[BendMaxDispatchCount];
        private bool m_IsActive;
        private bool m_EnableTiledResolve;
        private bool m_EnableBilateralDenoise;
        private bool m_EnableBendComposite;
        private bool m_VirtualShadowMapPrototypeActive;
        private TextureHandle m_VirtualShadowMapPrototypePhysicalPage;
        private BufferHandle m_VirtualShadowMapPrototypePageTable;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private int m_TileCountX = 1;
        private int m_TileCountY = 1;
        private Matrix4x4 m_ViewProjMatrix = Matrix4x4.identity;
        private Matrix4x4 m_InvViewProjMatrix = Matrix4x4.identity;
        private BendDispatchList m_BendDispatchList;
        private BendQualitySettings m_BendQualitySettings = ResolveBendQualitySettings(
            (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low);
        private Vector4 m_BendDepthTextureSize = Vector4.zero;
        private Vector4 m_LightDirectionWS;

        // Cached shadow data for shader upload
        private readonly Matrix4x4[] m_ViewProjMatrices = new Matrix4x4[VividShadowData.MaxCascadeCount];
        private readonly Vector4[] m_CascadeSpheres = new Vector4[VividShadowData.MaxCascadeCount];
        private Vector4 m_CascadeWorldTexelSizes = Vector4.zero;
        private Vector4 m_CascadeBorders = Vector4.zero;
        private int m_CascadeCount;
        private float m_MaxShadowDistance;
        private float m_NormalBias;
        private int m_CascadeResolution;
        private int m_ShadowQuality = (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low;
        private float m_LightAngularDiameter = VividAdditionalLightData.DefaultCelestialBodyAngularDiameter;
        private int m_FrameIndex;
        private int m_PCSSBlockerSampleCount = VividAdditionalLightData.DefaultDirLightPCSSBlockerSampleCount;
        private int m_PCSSFilterSampleCount = VividAdditionalLightData.DefaultDirLightPCSSFilterSampleCount;
        private float m_PCSSMaxPenumbraSize = VividAdditionalLightData.DefaultDirLightPCSSMaxPenumbraSize;
        private float m_PCSSMaxSamplingDistance = VividAdditionalLightData.DefaultDirLightPCSSMaxSamplingDistance;
        private float m_PCSSMinFilterSizeTexels = VividAdditionalLightData.DefaultDirLightPCSSMinFilterSizeTexels;
        private float m_PCSSMinFilterMaxAngularDiameter = VividAdditionalLightData.DefaultDirLightPCSSMinFilterMaxAngularDiameter;
        private float m_PCSSBlockerSearchAngularDiameter = VividAdditionalLightData.DefaultDirLightPCSSBlockerSearchAngularDiameter;
        private float m_PCSSBlockerSamplingClumpExponent = VividAdditionalLightData.DefaultDirLightPCSSBlockerSamplingClumpExponent;

        public CSMShadowResolvePass()
        {
            profilingSampler = new ProfilingSampler(nameof(CSMShadowResolvePass));
            m_BendDispatchList = CreateEmptyBendDispatchList();
            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_CSMShadowAtlas = RenderGraphTexture.CreateInput("CSMShadowAtlas", GraphicsFormat.None, DepthBits.Depth16);
            m_CSMShadowAtlas.desc.IsShadowMap = true;
            m_CSMShadowAtlas.desc.Dimension = TextureDimension.Tex2DArray;
            m_CSMShadowAtlas.desc.Slices = VividShadowData.MaxCascadeCount;
            m_DirectionalShadowTexture = RenderGraphTexture.CreateOutput("DirectionalShadowTexture", GraphicsFormat.R16_SFloat);
            m_DirectionalShadowTexture.desc.ClearBuffer = true;
            m_DirectionalShadowTexture.desc.ClearColor = Color.white;
            m_DirectionalShadowTexture.desc.FilterMode = FilterMode.Point;
            m_DirectionalShadowTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_DirectionalShadowTexture.desc.EnableRandomWrite = true;
            m_TileListBuffer = RenderGraphBuffer.CreateStructured("CSMShadowTileList", 1, sizeof(uint));
            m_DispatchIndirectArgsBuffer = new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = IndirectDispatchArgsElementCount,
                    Stride = sizeof(uint),
                    Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                    Name = "CSMShadowDispatchIndirectArgs"
                }
            };
            m_FilterTexture = RenderGraphTexture.CreateOutput("CSMShadowFilterTexture", GraphicsFormat.R16_SFloat);
            m_FilterTexture.desc.ClearBuffer = true;
            m_FilterTexture.desc.ClearColor = Color.white;
            m_FilterTexture.desc.FilterMode = FilterMode.Point;
            m_FilterTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_FilterTexture.desc.EnableRandomWrite = true;
        }

        public override void Create()
        {
            m_ResolveCompute = PipelineResourceManager.Get<VividRPCoreResources>()?.CSMShadowResolveCompute;

            if (m_ResolveCompute == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find compute shader resource for {nameof(CSMShadowResolvePass)}.");
                return;
            }

            m_Kernel = FindKernelOrInvalid(m_ResolveCompute, KernelName);
            m_ClearTilesKernel = FindKernelOrInvalid(m_ResolveCompute, ClearTilesKernelName);
            m_ClassifyTilesKernel = FindKernelOrInvalid(m_ResolveCompute, ClassifyTilesKernelName);
            m_ResolveTilesKernel = FindKernelOrInvalid(m_ResolveCompute, ResolveTilesKernelName);
            m_CopyFilterSourceKernel = FindKernelOrInvalid(m_ResolveCompute, CopyFilterSourceKernelName);
            m_BilateralFilterHKernel = FindKernelOrInvalid(m_ResolveCompute, BilateralFilterHKernelName);
            m_BilateralFilterVKernel = FindKernelOrInvalid(m_ResolveCompute, BilateralFilterVKernelName);

            for (var i = 0; i < s_BendCompositeKernelNames.Length; i++)
                m_BendCompositeKernels[i] = FindKernelOrInvalid(m_ResolveCompute, s_BendCompositeKernelNames[i]);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_IsActive = false;
            m_EnableTiledResolve = false;
            m_EnableBilateralDenoise = false;
            m_EnableBendComposite = false;
            m_VirtualShadowMapPrototypeActive = false;
            m_VirtualShadowMapPrototypePhysicalPage = default;
            m_VirtualShadowMapPrototypePageTable = default;
            m_LightDirectionWS = Vector4.zero;
            m_BendDispatchList = CreateEmptyBendDispatchList();
            m_BendDepthTextureSize = Vector4.zero;
            m_ViewProjMatrix = Matrix4x4.identity;
            m_InvViewProjMatrix = Matrix4x4.identity;
            m_ShadowQuality = (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low;
            m_BendQualitySettings = ResolveBendQualitySettings(m_ShadowQuality);
            m_LightAngularDiameter = VividAdditionalLightData.DefaultCelestialBodyAngularDiameter;
            m_FrameIndex = 0;
            m_CascadeWorldTexelSizes = Vector4.zero;
            m_CascadeBorders = Vector4.zero;
            m_PCSSBlockerSampleCount = VividAdditionalLightData.DefaultDirLightPCSSBlockerSampleCount;
            m_PCSSFilterSampleCount = VividAdditionalLightData.DefaultDirLightPCSSFilterSampleCount;
            m_PCSSMaxPenumbraSize = VividAdditionalLightData.DefaultDirLightPCSSMaxPenumbraSize;
            m_PCSSMaxSamplingDistance = VividAdditionalLightData.DefaultDirLightPCSSMaxSamplingDistance;
            m_PCSSMinFilterSizeTexels = VividAdditionalLightData.DefaultDirLightPCSSMinFilterSizeTexels;
            m_PCSSMinFilterMaxAngularDiameter = VividAdditionalLightData.DefaultDirLightPCSSMinFilterMaxAngularDiameter;
            m_PCSSBlockerSearchAngularDiameter = VividAdditionalLightData.DefaultDirLightPCSSBlockerSearchAngularDiameter;
            m_PCSSBlockerSamplingClumpExponent = VividAdditionalLightData.DefaultDirLightPCSSBlockerSamplingClumpExponent;

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var width = cameraData.actualWidth;
            var height = cameraData.actualHeight;

            m_DirectionalShadowTexture.Resize(width, height);
            m_FilterTexture.Resize(width, height);
            m_BendDepthTextureSize = CreateBendDepthTextureSize(width, height);
            m_DispatchGroupCountX = CoreUtils.DivRoundUp(width, ThreadGroupSizeX);
            m_DispatchGroupCountY = CoreUtils.DivRoundUp(height, ThreadGroupSizeY);
            m_TileCountX = CoreUtils.DivRoundUp(width, ScreenSpaceShadowTileSize);
            m_TileCountY = CoreUtils.DivRoundUp(height, ScreenSpaceShadowTileSize);
            ResizeStructuredBuffer(m_TileListBuffer, Mathf.Max(1, m_TileCountX * m_TileCountY), sizeof(uint));
            ResizeIndirectArgsBuffer(m_DispatchIndirectArgsBuffer);

            var shadowData = frameData.GetOrCreate<VividShadowData>();
            if (!shadowData.isCSMActive)
                return;

            m_IsActive = true;
            m_ViewProjMatrix = cameraData.GetGPUViewProjectionMatrix(renderIntoTexture: true);
            m_InvViewProjMatrix = m_ViewProjMatrix.inverse;

            m_CascadeCount = shadowData.cascadeCount;
            m_MaxShadowDistance = shadowData.maxShadowDistance;
            m_NormalBias = shadowData.normalBias;
            m_CascadeResolution = shadowData.cascadeResolution;
            m_CascadeWorldTexelSizes = Vector4.zero;
            m_CascadeBorders = Vector4.zero;
            m_FrameIndex = Time.frameCount;

            for (int i = 0; i < VividShadowData.MaxCascadeCount; i++)
            {
                m_ViewProjMatrices[i] = shadowData.viewProjMatrices[i];
                m_CascadeSpheres[i] = shadowData.cascadeSpheres[i];
                m_CascadeWorldTexelSizes[i] = shadowData.cascadeWorldTexelSizes[i];
                m_CascadeBorders[i] = shadowData.cascadeBorders[i];
            }

            var lightData = frameData.GetOrCreate<VividLightData>();
            if (lightData.hasMainDirectionalLight)
            {
                var dir = lightData.mainDirectionalLight.directionWS;
                m_LightDirectionWS = new Vector4(dir.x, dir.y, dir.z, 0f);

                if (DirectionalRayTracedShadowPass.TryResolveMainDirectionalLight(lightData, out _, out var additionalLightData)
                    && additionalLightData != null)
                {
                    m_ShadowQuality = (int)additionalLightData.screenSpaceShadowQuality;
                    m_LightAngularDiameter = Mathf.Max(additionalLightData.angularDiameter, 0.0f);
                    m_PCSSBlockerSampleCount = additionalLightData.dirLightPCSSBlockerSampleCount;
                    m_PCSSFilterSampleCount = additionalLightData.dirLightPCSSFilterSampleCount;
                    m_PCSSMaxPenumbraSize = additionalLightData.dirLightPCSSMaxPenumbraSize;
                    m_PCSSMaxSamplingDistance = additionalLightData.dirLightPCSSMaxSamplingDistance;
                    m_PCSSMinFilterSizeTexels = additionalLightData.dirLightPCSSMinFilterSizeTexels;
                    m_PCSSMinFilterMaxAngularDiameter = additionalLightData.dirLightPCSSMinFilterMaxAngularDiameter;
                    m_PCSSBlockerSearchAngularDiameter = additionalLightData.dirLightPCSSBlockerSearchAngularDiameter;
                    m_PCSSBlockerSamplingClumpExponent = additionalLightData.dirLightPCSSBlockerSamplingClumpExponent;
                    if (IsUnrealScreenSpaceShadowQuality(m_ShadowQuality))
                    {
                        m_BendQualitySettings = new BendQualitySettings(
                            additionalLightData.dirLightBendSSSSurfaceThickness,
                            additionalLightData.dirLightBendSSSBilinearThreshold,
                            additionalLightData.dirLightBendSSSShadowContrast,
                            additionalLightData.dirLightBendSSSMaxRayDistance,
                            additionalLightData.dirLightBendSSSIgnoreEdgePixels,
                            additionalLightData.dirLightBendSSSUsePrecisionOffset,
                            additionalLightData.dirLightBendSSSBilinearSamplingOffsetMode);
                    }
                }

                if (!IsUnrealScreenSpaceShadowQuality(m_ShadowQuality))
                    m_BendQualitySettings = ResolveBendQualitySettings(m_ShadowQuality);
                if (IsUnrealScreenSpaceShadowQuality(m_ShadowQuality))
                {
                    m_BendDispatchList = BuildBendDispatchList(
                        m_BendDispatches,
                        m_ViewProjMatrix * m_LightDirectionWS,
                        new Vector2Int(width, height),
                        Vector2Int.zero,
                        new Vector2Int(width, height));
                    m_EnableBendComposite = m_BendDispatchList.DispatchCount > 0
                        && GetBendCompositeKernel() >= 0
                        && width > 0
                        && height > 0
                        && m_LightDirectionWS.sqrMagnitude > 1.0e-6f;
                }
            }

            var csmSettings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            m_EnableBilateralDenoise = csmSettings != null && csmSettings.screenSpaceShadowDenoise.value;
            m_EnableTiledResolve = IsVividTiledPCSSQuality(m_ShadowQuality)
                && CanUseTiledResolveKernels();

            if (VirtualShadowMapPrototypeRuntime.EnsurePhysicalPageForBinding())
            {
                m_VirtualShadowMapPrototypePhysicalPage = PassRecorder.ImportTextureForPass(
                    this,
                    VirtualShadowMapPrototypeRuntime.PhysicalPage,
                    AccessFlags.Read);
                m_VirtualShadowMapPrototypePageTable = PassRecorder.ImportBufferForPass(
                    this,
                    VirtualShadowMapPrototypeRuntime.PageTable,
                    AccessFlags.Read);
            }

            if (csmSettings != null
                && csmSettings.enableVirtualShadowMapPrototype.value
                && m_VirtualShadowMapPrototypePhysicalPage.IsValid()
                && m_VirtualShadowMapPrototypePageTable.IsValid()
                && VirtualShadowMapPrototypeRuntime.IsFrameActive)
            {
                m_VirtualShadowMapPrototypeActive = true;
                if (m_VirtualShadowMapPrototypeActive)
                {
                    m_EnableTiledResolve = false;
                    m_EnableBilateralDenoise = false;
                    m_EnableBendComposite = false;
                }
            }
        }

        public override void Record(ComputePassContext context)
        {
            if (!m_IsActive || m_ResolveCompute == null || m_Kernel < 0)
                return;

            if (!m_DepthTexture.innerHandle.IsValid()
                || !m_GBuffer1.innerHandle.IsValid()
                || !m_CSMShadowAtlas.innerHandle.IsValid()
                || !m_DirectionalShadowTexture.innerHandle.IsValid())
                return;

            var cmd = context.cmd;

            if (m_EnableTiledResolve
                && m_TileListBuffer?.innerHandle.IsValid() == true
                && m_DispatchIndirectArgsBuffer?.innerHandle.IsValid() == true)
            {
                RecordTiledScreenSpaceResolve(cmd);
            }
            else
            {
                RecordFullScreenCSMResolve(cmd);
            }

            RecordBendScreenSpaceContactShadow(cmd);
        }

        public override void Dispose()
        {
            m_ResolveCompute = null;
            m_Kernel = -1;
            m_ClearTilesKernel = -1;
            m_ClassifyTilesKernel = -1;
            m_ResolveTilesKernel = -1;
            m_CopyFilterSourceKernel = -1;
            m_BilateralFilterHKernel = -1;
            m_BilateralFilterVKernel = -1;
            for (var i = 0; i < m_BendCompositeKernels.Length; i++)
                m_BendCompositeKernels[i] = -1;
            m_IsActive = false;
            m_EnableTiledResolve = false;
            m_EnableBilateralDenoise = false;
            m_EnableBendComposite = false;
            m_VirtualShadowMapPrototypeActive = false;
            m_VirtualShadowMapPrototypePhysicalPage = default;
            m_VirtualShadowMapPrototypePageTable = default;
            m_DispatchGroupCountX = 1;
            m_DispatchGroupCountY = 1;
            m_TileCountX = 1;
            m_TileCountY = 1;
            m_FrameIndex = 0;
            m_BendDispatchList = CreateEmptyBendDispatchList();
            m_BendDepthTextureSize = Vector4.zero;
            m_ViewProjMatrix = Matrix4x4.identity;
            m_InvViewProjMatrix = Matrix4x4.identity;
            m_CascadeWorldTexelSizes = Vector4.zero;
            m_CascadeBorders = Vector4.zero;
        }

        private void RecordFullScreenCSMResolve(ComputeCommandBuffer cmd)
        {
            BindCommonTextures(cmd, m_Kernel);
            BindShadowParameters(cmd);

            cmd.DispatchCompute(m_ResolveCompute, m_Kernel,
                m_DispatchGroupCountX, m_DispatchGroupCountY, 1);
        }

        private void RecordTiledScreenSpaceResolve(ComputeCommandBuffer cmd)
        {
            BindShadowParameters(cmd);

            BindTileBuffers(cmd, m_ClearTilesKernel);
            cmd.DispatchCompute(m_ResolveCompute, m_ClearTilesKernel, 1, 1, 1);

            BindCommonTextures(cmd, m_ClassifyTilesKernel);
            BindTileBuffers(cmd, m_ClassifyTilesKernel);
            cmd.DispatchCompute(m_ResolveCompute, m_ClassifyTilesKernel, m_TileCountX, m_TileCountY, 1);

            BindCommonTextures(cmd, m_ResolveTilesKernel);
            BindTileBuffers(cmd, m_ResolveTilesKernel);
            cmd.DispatchCompute(m_ResolveCompute, m_ResolveTilesKernel, m_DispatchIndirectArgsBuffer, 0);

            if (!m_EnableBilateralDenoise || m_FilterTexture?.innerHandle.IsValid() != true)
                return;

            BindFilterTextures(cmd, m_CopyFilterSourceKernel, m_DirectionalShadowTexture, m_FilterTexture);
            cmd.DispatchCompute(m_ResolveCompute, m_CopyFilterSourceKernel, m_DispatchGroupCountX, m_DispatchGroupCountY, 1);

            BindFilterTextures(cmd, m_BilateralFilterHKernel, m_DirectionalShadowTexture, m_FilterTexture);
            BindTileBuffers(cmd, m_BilateralFilterHKernel);
            cmd.DispatchCompute(m_ResolveCompute, m_BilateralFilterHKernel, m_DispatchIndirectArgsBuffer, 0);

            BindFilterTextures(cmd, m_BilateralFilterVKernel, m_FilterTexture, m_DirectionalShadowTexture);
            BindTileBuffers(cmd, m_BilateralFilterVKernel);
            cmd.DispatchCompute(m_ResolveCompute, m_BilateralFilterVKernel, m_DispatchIndirectArgsBuffer, 0);
        }

        private void RecordBendScreenSpaceContactShadow(ComputeCommandBuffer cmd)
        {
            if (!m_EnableBendComposite || m_BendDispatchList.DispatchCount <= 0)
                return;

            var kernel = GetBendCompositeKernel();
            if (kernel < 0)
                return;

            BindBendCompositeParameters(cmd, kernel);

            for (var dispatchIndex = 0; dispatchIndex < m_BendDispatchList.DispatchCount; dispatchIndex++)
            {
                var dispatch = m_BendDispatchList.Dispatches[dispatchIndex];
                if (dispatch.WaveCount.x <= 0 || dispatch.WaveCount.y <= 0 || dispatch.WaveCount.z <= 0)
                    continue;

                cmd.SetComputeVectorParam(
                    m_ResolveCompute,
                    CSMBendWaveOffsetId,
                    new Vector4(dispatch.WaveOffset.x, dispatch.WaveOffset.y, 0.0f, 0.0f));
                cmd.DispatchCompute(
                    m_ResolveCompute,
                    kernel,
                    dispatch.WaveCount.x,
                    dispatch.WaveCount.y,
                    dispatch.WaveCount.z);
            }
        }

        private void BindBendCompositeParameters(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(m_ResolveCompute, kernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, kernel, DirectionalShadowTextureId, m_DirectionalShadowTexture.innerHandle);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMOutputWidthId, m_DirectionalShadowTexture.desc.Width);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMOutputHeightId, m_DirectionalShadowTexture.desc.Height);
            cmd.SetComputeVectorParam(m_ResolveCompute, CSMBendLightCoordinateId, m_BendDispatchList.LightCoordinate);
            cmd.SetComputeVectorParam(m_ResolveCompute, CSMBendDepthTextureSizeId, m_BendDepthTextureSize);
            cmd.SetComputeMatrixParam(m_ResolveCompute, CSMBendViewProjMatrixId, m_ViewProjMatrix);
            cmd.SetComputeMatrixParam(m_ResolveCompute, CSMInvViewProjMatrixId, m_InvViewProjMatrix);
            cmd.SetComputeVectorParam(m_ResolveCompute, CSMLightDirectionWSId, m_LightDirectionWS);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMBendMaxRayDistanceId, m_BendQualitySettings.MaxRayDistance);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMBendSurfaceThicknessId, m_BendQualitySettings.SurfaceThickness);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMBendBilinearThresholdId, m_BendQualitySettings.BilinearThreshold);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMBendShadowContrastId, m_BendQualitySettings.ShadowContrast);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMBendIgnoreEdgePixelsId, m_BendQualitySettings.IgnoreEdgePixels ? 1 : 0);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMBendUsePrecisionOffsetId, m_BendQualitySettings.UsePrecisionOffset ? 1 : 0);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMBendBilinearSamplingOffsetModeId, m_BendQualitySettings.BilinearSamplingOffsetMode ? 1 : 0);
        }

        private void BindCommonTextures(ComputeCommandBuffer cmd, int kernel)
        {
            TextureHandle virtualShadowMapPage = m_VirtualShadowMapPrototypePhysicalPage.IsValid()
                ? m_VirtualShadowMapPrototypePhysicalPage
                : m_GBuffer1.innerHandle;
            cmd.SetComputeTextureParam(m_ResolveCompute, kernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, kernel, CSMShadowAtlasId, m_CSMShadowAtlas.innerHandle);
            cmd.SetComputeTextureParam(
                m_ResolveCompute,
                kernel,
                VSMPrototypePhysicalPageId,
                virtualShadowMapPage);
            if (VirtualShadowMapPrototypeRuntime.PageTable != null)
                cmd.SetComputeBufferParam(
                    m_ResolveCompute,
                    kernel,
                    VSMPrototypePageTableId,
                    VirtualShadowMapPrototypeRuntime.PageTable);
            else
                cmd.SetComputeBufferParam(
                    m_ResolveCompute,
                    kernel,
                    VSMPrototypePageTableId,
                    m_TileListBuffer.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, kernel, DirectionalShadowTextureId, m_DirectionalShadowTexture.innerHandle);
        }

        private void BindFilterTextures(
            ComputeCommandBuffer cmd,
            int kernel,
            RenderGraphTexture source,
            RenderGraphTexture destination)
        {
            cmd.SetComputeTextureParam(m_ResolveCompute, kernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, kernel, CSMShadowFilterSourceId, source.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, kernel, CSMShadowFilterTextureId, destination.innerHandle);
        }

        private void BindTileBuffers(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeBufferParam(m_ResolveCompute, kernel, CSMShadowTileListId, m_TileListBuffer.innerHandle);
            cmd.SetComputeBufferParam(
                m_ResolveCompute,
                kernel,
                CSMShadowDispatchIndirectArgsId,
                m_DispatchIndirectArgsBuffer.innerHandle);
        }

        private void BindShadowParameters(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeMatrixArrayParam(m_ResolveCompute, CSMViewProjMatricesId, m_ViewProjMatrices);
            cmd.SetComputeVectorArrayParam(m_ResolveCompute, CSMCascadeSpheresId, m_CascadeSpheres);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMCascadeCountId, m_CascadeCount);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMMaxShadowDistanceId, m_MaxShadowDistance);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMNormalBiasId, m_NormalBias);
            cmd.SetComputeMatrixParam(m_ResolveCompute, CSMInvViewProjMatrixId, m_InvViewProjMatrix);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMOutputWidthId, m_DirectionalShadowTexture.desc.Width);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMOutputHeightId, m_DirectionalShadowTexture.desc.Height);
            cmd.SetComputeVectorParam(m_ResolveCompute, CSMLightDirectionWSId, m_LightDirectionWS);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMCascadeResolutionId, m_CascadeResolution);
            cmd.SetComputeVectorParam(m_ResolveCompute, CSMCascadeWorldTexelSizesId, m_CascadeWorldTexelSizes);
            cmd.SetComputeVectorParam(m_ResolveCompute, CSMCascadeBordersId, m_CascadeBorders);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMShadowQualityId, ResolveCSMFilteringQuality(m_ShadowQuality));
            cmd.SetComputeIntParam(
                m_ResolveCompute,
                VSMPrototypeEnabledId,
                m_VirtualShadowMapPrototypeActive ? 1 : 0);
            cmd.SetComputeIntParam(
                m_ResolveCompute,
                VSMPrototypePageSizeId,
                VirtualShadowMapPrototypeRuntime.PageSize);
            cmd.SetComputeIntParam(
                m_ResolveCompute,
                VSMPrototypeVirtualResolutionId,
                Mathf.Max(VirtualShadowMapPrototypeRuntime.VirtualResolution, 1));
            cmd.SetComputeIntParam(
                m_ResolveCompute,
                VSMPrototypePagesPerAxisId,
                Mathf.Max(VirtualShadowMapPrototypeRuntime.PagesPerAxis, 1));
            cmd.SetComputeIntParam(
                m_ResolveCompute,
                VSMPrototypePhysicalPagesPerRowId,
                Mathf.Max(VirtualShadowMapPrototypeRuntime.PhysicalPagesPerRow, 1));
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMLightAngularDiameterId, m_LightAngularDiameter);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMFrameIndexId, m_FrameIndex);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMPCSSBlockerSampleCountId, m_PCSSBlockerSampleCount);
            cmd.SetComputeIntParam(m_ResolveCompute, CSMPCSSFilterSampleCountId, m_PCSSFilterSampleCount);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMPCSSMaxPenumbraSizeId, m_PCSSMaxPenumbraSize);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMPCSSMaxSamplingDistanceId, m_PCSSMaxSamplingDistance);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMPCSSMinFilterSizeTexelsId, m_PCSSMinFilterSizeTexels);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMPCSSMinFilterMaxAngularDiameterId, m_PCSSMinFilterMaxAngularDiameter);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMPCSSBlockerSearchAngularDiameterId, m_PCSSBlockerSearchAngularDiameter);
            cmd.SetComputeFloatParam(m_ResolveCompute, CSMPCSSBlockerSamplingClumpExponentId, m_PCSSBlockerSamplingClumpExponent);
        }

        private int GetBendCompositeKernel()
        {
            return IsUnrealScreenSpaceShadowQuality(m_ShadowQuality)
                ? m_BendCompositeKernels[(int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh]
                : -1;
        }

        internal static BendQualitySettings ResolveBendQualitySettings(int quality)
        {
            return (VividAdditionalLightData.CSMScreenSpaceShadowQuality)quality switch
            {
                VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low =>
                    new BendQualitySettings(0.0080f, 0.030f, 3.0f),
                VividAdditionalLightData.CSMScreenSpaceShadowQuality.Medium =>
                    new BendQualitySettings(0.0060f, 0.025f, 3.5f),
                VividAdditionalLightData.CSMScreenSpaceShadowQuality.High =>
                    new BendQualitySettings(0.0050f, 0.020f, 4.0f),
                VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh =>
                    new BendQualitySettings(0.0050f, 0.020f, 4.0f),
                VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal =>
                    new BendQualitySettings(
                        VividAdditionalLightData.DefaultDirLightBendSSSSurfaceThickness,
                        VividAdditionalLightData.DefaultDirLightBendSSSBilinearThreshold,
                        VividAdditionalLightData.DefaultDirLightBendSSSShadowContrast,
                        VividAdditionalLightData.DefaultDirLightBendSSSMaxRayDistance,
                        VividAdditionalLightData.DefaultDirLightBendSSSIgnoreEdgePixels,
                        VividAdditionalLightData.DefaultDirLightBendSSSUsePrecisionOffset,
                        VividAdditionalLightData.DefaultDirLightBendSSSBilinearSamplingOffsetMode),
                _ => new BendQualitySettings(0.0080f, 0.030f, 3.0f)
            };
        }

        internal static int ResolveCSMFilteringQuality(int quality)
        {
            return (VividAdditionalLightData.CSMScreenSpaceShadowQuality)quality switch
            {
                VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low =>
                    (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low,
                VividAdditionalLightData.CSMScreenSpaceShadowQuality.Medium =>
                    (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Medium,
                VividAdditionalLightData.CSMScreenSpaceShadowQuality.High =>
                    (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.High,
                VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh =>
                    (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh,
                VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal =>
                    (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.High,
                _ => (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Medium
            };
        }

        internal static bool IsVividTiledPCSSQuality(int quality)
        {
            return quality == (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.VeryHigh;
        }

        internal static bool IsUnrealScreenSpaceShadowQuality(int quality)
        {
            return quality == (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Unreal;
        }

        internal static Vector4 CreateBendDepthTextureSize(int width, int height)
        {
            var safeWidth = Mathf.Max(1, width);
            var safeHeight = Mathf.Max(1, height);
            return new Vector4(
                1.0f / safeWidth,
                1.0f / safeHeight,
                safeWidth,
                safeHeight);
        }

        internal static BendDispatchList BuildBendDispatchList(
            Vector4 lightProjection,
            Vector2Int viewportSize,
            Vector2Int minRenderBounds,
            Vector2Int maxRenderBounds,
            bool expandedZRange = false,
            int waveSize = BendWaveSize)
        {
            var dispatches = new BendDispatchData[BendMaxDispatchCount];
            return BuildBendDispatchList(
                dispatches,
                lightProjection,
                viewportSize,
                minRenderBounds,
                maxRenderBounds,
                expandedZRange,
                waveSize);
        }

        internal static BendDispatchList BuildBendDispatchList(
            BendDispatchData[] dispatches,
            Vector4 lightProjection,
            Vector2Int viewportSize,
            Vector2Int minRenderBounds,
            Vector2Int maxRenderBounds,
            bool expandedZRange = false,
            int waveSize = BendWaveSize)
        {
            if (dispatches == null || dispatches.Length == 0)
                return new BendDispatchList(Vector4.zero, dispatches, 0);

            var dispatchCount = 0;
            var safeWaveSize = Mathf.Max(1, waveSize);
            var safeViewportWidth = Mathf.Max(1, viewportSize.x);
            var safeViewportHeight = Mathf.Max(1, viewportSize.y);

            var xyLightW = lightProjection.w;
            var floatingPointLimit = 0.000002f * safeWaveSize;
            if (xyLightW >= 0.0f && xyLightW < floatingPointLimit)
                xyLightW = floatingPointLimit;
            else if (xyLightW < 0.0f && xyLightW > -floatingPointLimit)
                xyLightW = -floatingPointLimit;

            var lightCoordinate = new Vector4(
                ((lightProjection.x / xyLightW) * 0.5f + 0.5f) * safeViewportWidth,
                ((lightProjection.y / xyLightW) * -0.5f + 0.5f) * safeViewportHeight,
                lightProjection.w == 0.0f ? 0.0f : lightProjection.z / lightProjection.w,
                lightProjection.w > 0.0f ? 1.0f : -1.0f);

            if (expandedZRange)
                lightCoordinate.z = lightCoordinate.z * 0.5f + 0.5f;

            var lightXY = new Vector2Int(
                (int)(lightCoordinate.x + 0.5f),
                (int)(lightCoordinate.y + 0.5f));

            var biasedMinX = minRenderBounds.x - lightXY.x;
            var biasedMinY = -(maxRenderBounds.y - lightXY.y);
            var biasedMaxX = maxRenderBounds.x - lightXY.x;
            var biasedMaxY = -(minRenderBounds.y - lightXY.y);

            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                var vertical = quadrant == 0 || quadrant == 3;
                var minX = BendMax(0, ((quadrant & 1) != 0 ? biasedMinX : -biasedMaxX)) / safeWaveSize;
                var minY = BendMax(0, ((quadrant & 2) != 0 ? biasedMinY : -biasedMaxY)) / safeWaveSize;
                var maxX = BendMax(0, (((quadrant & 1) != 0 ? biasedMaxX : -biasedMinX)
                    + safeWaveSize * (vertical ? 1 : 2) - 1)) / safeWaveSize;
                var maxY = BendMax(0, (((quadrant & 2) != 0 ? biasedMaxY : -biasedMinY)
                    + safeWaveSize * (vertical ? 2 : 1) - 1)) / safeWaveSize;

                if ((maxX - minX) <= 0 || (maxY - minY) <= 0)
                    continue;

                var biasX = quadrant == 2 || quadrant == 3 ? 1 : 0;
                var biasY = quadrant == 1 || quadrant == 3 ? 1 : 0;
                var dispatch = new BendDispatchData
                {
                    WaveCount = new Vector3Int(safeWaveSize, maxX - minX, maxY - minY),
                    WaveOffset = new Vector2Int(
                        ((quadrant & 1) != 0 ? minX : -maxX) + biasX,
                        ((quadrant & 2) != 0 ? -maxY : minY) + biasY)
                };

                var axisDelta = biasedMinX - biasedMinY;
                if (quadrant == 1)
                    axisDelta = biasedMaxX + biasedMinY;
                if (quadrant == 2)
                    axisDelta = -biasedMinX - biasedMaxY;
                if (quadrant == 3)
                    axisDelta = -biasedMaxX + biasedMaxY;

                axisDelta = (axisDelta + safeWaveSize - 1) / safeWaveSize;

                if (axisDelta > 0)
                {
                    var splitDispatch = dispatch;

                    if (quadrant == 0)
                    {
                        splitDispatch.WaveCount.z = BendMin(dispatch.WaveCount.z, axisDelta);
                        dispatch.WaveCount.z -= splitDispatch.WaveCount.z;
                        splitDispatch.WaveOffset.y = dispatch.WaveOffset.y + dispatch.WaveCount.z;
                        splitDispatch.WaveOffset.x--;
                        splitDispatch.WaveCount.y++;
                    }
                    else if (quadrant == 1)
                    {
                        splitDispatch.WaveCount.y = BendMin(dispatch.WaveCount.y, axisDelta);
                        dispatch.WaveCount.y -= splitDispatch.WaveCount.y;
                        splitDispatch.WaveOffset.x = dispatch.WaveOffset.x + dispatch.WaveCount.y;
                        splitDispatch.WaveCount.z++;
                    }
                    else if (quadrant == 2)
                    {
                        splitDispatch.WaveCount.y = BendMin(dispatch.WaveCount.y, axisDelta);
                        dispatch.WaveCount.y -= splitDispatch.WaveCount.y;
                        dispatch.WaveOffset.x += splitDispatch.WaveCount.y;
                        splitDispatch.WaveCount.z++;
                        splitDispatch.WaveOffset.y--;
                    }
                    else if (quadrant == 3)
                    {
                        splitDispatch.WaveCount.z = BendMin(dispatch.WaveCount.z, axisDelta);
                        dispatch.WaveCount.z -= splitDispatch.WaveCount.z;
                        dispatch.WaveOffset.y += splitDispatch.WaveCount.z;
                        splitDispatch.WaveCount.y++;
                    }

                    AddBendDispatch(dispatches, ref dispatchCount, dispatch);
                    AddBendDispatch(dispatches, ref dispatchCount, splitDispatch);
                }
                else
                {
                    AddBendDispatch(dispatches, ref dispatchCount, dispatch);
                }
            }

            for (var i = 0; i < dispatchCount; i++)
            {
                var dispatch = dispatches[i];
                dispatch.WaveOffset = new Vector2Int(
                    dispatch.WaveOffset.x * safeWaveSize,
                    dispatch.WaveOffset.y * safeWaveSize);
                dispatches[i] = dispatch;
            }

            return new BendDispatchList(lightCoordinate, dispatches, dispatchCount);
        }

        private static void AddBendDispatch(BendDispatchData[] dispatches, ref int dispatchCount, BendDispatchData dispatch)
        {
            if (dispatches == null)
                return;

            if (dispatch.WaveCount.x <= 0 || dispatch.WaveCount.y <= 0 || dispatch.WaveCount.z <= 0)
                return;

            if (dispatchCount >= dispatches.Length)
                return;

            dispatches[dispatchCount++] = dispatch;
        }

        private BendDispatchList CreateEmptyBendDispatchList()
        {
            return new BendDispatchList(Vector4.zero, m_BendDispatches, 0);
        }

        private static int BendMin(int a, int b)
        {
            return a > b ? b : a;
        }

        private static int BendMax(int a, int b)
        {
            return a > b ? a : b;
        }

        private bool CanUseTiledResolveKernels()
        {
            return m_ClearTilesKernel >= 0
                && m_ClassifyTilesKernel >= 0
                && m_ResolveTilesKernel >= 0
                && (!m_EnableBilateralDenoise
                    || (m_CopyFilterSourceKernel >= 0
                        && m_BilateralFilterHKernel >= 0
                        && m_BilateralFilterVKernel >= 0));
        }

        private static int FindKernelOrInvalid(ComputeShader shader, string kernelName)
        {
            return shader != null && shader.HasKernel(kernelName) ? shader.FindKernel(kernelName) : -1;
        }

        private static void ResizeStructuredBuffer(RenderGraphBuffer buffer, int count, int stride)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = Mathf.Max(1, count);
            buffer.desc.Stride = stride;
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
        }

        private static void ResizeIndirectArgsBuffer(RenderGraphBuffer buffer)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = IndirectDispatchArgsElementCount;
            buffer.desc.Stride = sizeof(uint);
            buffer.desc.Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments;
        }
    }
}
