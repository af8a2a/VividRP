using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;
using NameAndTooltip = UnityEngine.Rendering.DebugUI.Widget.NameAndTooltip;

namespace VividRP.Runtime
{
    public enum VirtualTextureDebugMode
    {
        None = 0,
        Residency = 1,
        MipBias = 2,
        PhysicalPageId = 3,
    }

    public enum VirtualTextureVisualizationMode
    {
        None = 0,
        [InspectorName("Physical Cache")]
        PhysicalCache = 2,
        [InspectorName("Page Table / Residency")]
        PageTableResidency = 3,
        [InspectorName("Physical Cache + Residency")]
        PhysicalCacheAndPageTableResidency = 4,
        [InspectorName("Page Table / Resolved Mip")]
        PageTableResolvedMip = 5,
        [InspectorName("Page Table / Physical Page")]
        PageTablePhysicalPage = 6,
    }

    public enum VirtualTextureVisualizationTarget
    {
        [InspectorName("Auto (Prefer GPUDriven)")]
        Auto = 0,
        [InspectorName("GPUDriven Virtual Texture")]
        GPUDriven = 1,
        [InspectorName("First Public Space")]
        FirstPublic = 2,
        [InspectorName("First Available Space")]
        FirstAvailable = 3,
    }

    public enum VirtualTextureVisualizationLayer
    {
        BaseColor = 0,
        Normal = 1,
        Mask = 2,
    }

    internal sealed class VividRenderingDebugDisplaySettings
        : DebugDisplaySettings<VividRenderingDebugDisplaySettings>
    {
        internal static VividRenderingDebugSettingsData Data =>
            DebugDisplaySerializer.GetOrCreate<VividRenderingDebugSettingsData>();

        public override void Reset()
        {
            base.Reset();
            Add(Data);
        }
    }

    [Serializable]
    internal sealed class VividRenderingDebugSettingsData : IDebugDisplaySettingsData, ISerializedDebugDisplaySettings
    {
        internal const ReGIRDebugVisualizationMode DefaultReGIRDebugMode = ReGIRDebugVisualizationMode.None;
        internal const float DefaultReGIRDebugOpacity = 0.45f;
        internal const float DefaultVisibilityBufferWireframeThickness = 10f;
        internal const ReferencedPathTracingTransportDebugMode
            DefaultReferencedPathTracingTransportDebugMode =
                ReferencedPathTracingTransportDebugMode.Combined;
        internal const ReferencedPathTracingEnvironmentDebugMode
            DefaultReferencedPathTracingEnvironmentDebugMode =
                ReferencedPathTracingEnvironmentDebugMode.Combined;

        [SerializeField]
        private TileClusterDebug m_TileClusterDebug = TileClusterDebug.None;

        [SerializeField]
        private TileClusterCategoryDebug m_TileClusterDebugByCategory = TileClusterCategoryDebug.Punctual;

        [SerializeField]
        private MaterialFeatureVariantDebug m_MaterialFeatureVariantDebug = MaterialFeatureVariantDebug.All;

        [SerializeField]
        private ClusterDebugMode m_ClusterDebugMode = ClusterDebugMode.VisualizeOpaque;

        [SerializeField]
        private float m_ClusterDebugDistance = 1f;

        [SerializeField]
        private ReGIRDebugVisualizationMode m_ReGIRDebugMode = DefaultReGIRDebugMode;

        [SerializeField]
        private float m_ReGIRDebugOpacity = DefaultReGIRDebugOpacity;

        [SerializeField]
        private ReferencedPathTracingTransportDebugMode
            m_ReferencedPathTracingTransportDebugMode =
                DefaultReferencedPathTracingTransportDebugMode;

        [SerializeField]
        private ReferencedPathTracingEnvironmentDebugMode
            m_ReferencedPathTracingEnvironmentDebugMode =
                DefaultReferencedPathTracingEnvironmentDebugMode;

        [SerializeField]
        private ExposureDebugMode m_ExposureMode = ExposureDebugMode.None;

        [SerializeField]
        private float m_DebugExposure;

        [SerializeField]
        private bool m_CenterHistogramAroundMiddleGrey;

        [SerializeField]
        private bool m_ShowTonemapCurveAlongHistogramView = true;

        [SerializeField]
        private bool m_DisplayMaskOnly;

        [SerializeField]
        private bool m_DisplayOnSceneOverlay = true;

        [SerializeField]
        private float m_OverlayAmount;

        [SerializeField]
        private int m_ArraySlice;

        [SerializeField]
        private float m_OverlayExposure;

        [SerializeField]
        private float m_OverlayOpacity = 1f;

        [SerializeField]
        private OverlayDebugVisualizationMode m_VisualizationMode = OverlayDebugVisualizationMode.Auto;

        [SerializeField]
        private OverlayDebugDepthMode m_DepthMode = OverlayDebugDepthMode.Raw;

        [SerializeField]
        private float m_DepthMipLevel;

        [SerializeField]
        private bool m_DepthRemapEnabled;

        [SerializeField]
        private float m_DepthRemapMin;

        [SerializeField]
        private float m_DepthRemapMax = 1f;

        [SerializeField]
        private OverlayDebugChannelMode m_ChannelMode = OverlayDebugChannelMode.RGB;

        [SerializeField]
        private MaterialDebugVisualizationMode m_MaterialDebugMode = MaterialDebugVisualizationMode.None;

        [SerializeField]
        private float m_MaterialDebugExposure;

        [SerializeField]
        private VisibilityBufferDebugVisualizationMode m_VisibilityBufferDebugMode =
            VisibilityBufferDebugVisualizationMode.Cluster;

        [SerializeField]
        private float m_VisibilityBufferDebugExposure;

        [SerializeField]
        private float m_VisibilityBufferWireframeThickness =
            DefaultVisibilityBufferWireframeThickness;

        [SerializeField]
        private bool m_ForceMeshletCullingFromMainCamera;

        [SerializeField]
        private ReflectionProbeAtlasDebugMode m_ReflectionProbeAtlasDebugMode = ReflectionProbeAtlasDebugMode.None;

        [SerializeField]
        private int m_ReflectionProbeAtlasArraySlice;

        [SerializeField]
        private int m_ReflectionProbeAtlasMipLevel;

        [SerializeField]
        private float m_ReflectionProbeAtlasExposure;

        [SerializeField]
        private float m_Slider = 50f;

        [SerializeField]
        private VirtualTextureDebugMode m_VirtualTextureDebugMode = VirtualTextureDebugMode.None;

        [SerializeField]
        private VirtualTextureVisualizationMode m_VirtualTextureVisualizationMode = VirtualTextureVisualizationMode.None;

        [SerializeField]
        private VirtualTextureVisualizationTarget m_VirtualTextureVisualizationTarget = VirtualTextureVisualizationTarget.Auto;

        [SerializeField]
        private VirtualTextureVisualizationLayer m_VirtualTextureVisualizationLayer = VirtualTextureVisualizationLayer.BaseColor;

        [SerializeField]
        private float m_VirtualTextureVisualizationOverlayAmount;

        [SerializeField]
        private float m_VirtualTextureVisualizationOpacity = 1f;

        [SerializeField]
        private VirtualTextureStatsViewMode m_VirtualTextureStatsViewMode = VirtualTextureStatsViewMode.Auto;

        [NonSerialized]
        private Camera m_VirtualTextureStatsCamera;

        internal TileClusterDebug tileClusterDebug
        {
            get => m_TileClusterDebug;
            set => m_TileClusterDebug = value;
        }

        internal TileClusterCategoryDebug tileClusterDebugByCategory
        {
            get => NormalizeTileClusterCategory(m_TileClusterDebugByCategory);
            set => m_TileClusterDebugByCategory = NormalizeTileClusterCategory(value);
        }

        internal MaterialFeatureVariantDebug materialFeatureVariantDebug
        {
            get => NormalizeMaterialFeatureVariantDebug(m_MaterialFeatureVariantDebug);
            set => m_MaterialFeatureVariantDebug = NormalizeMaterialFeatureVariantDebug(value);
        }

        internal ClusterDebugMode clusterDebugMode
        {
            get => m_ClusterDebugMode;
            set => m_ClusterDebugMode = value;
        }

        internal float clusterDebugDistance
        {
            get => m_ClusterDebugDistance;
            set => m_ClusterDebugDistance = value;
        }

        internal ReGIRDebugVisualizationMode reGIRDebugMode
        {
            get => ReGIRDebugPass.NormalizeVisualizationMode(m_ReGIRDebugMode);
            set => m_ReGIRDebugMode = ReGIRDebugPass.NormalizeVisualizationMode(value);
        }

        internal float reGIRDebugOpacity
        {
            get => m_ReGIRDebugOpacity;
            set => m_ReGIRDebugOpacity = value;
        }

        internal ReferencedPathTracingTransportDebugMode
            referencedPathTracingTransportDebugMode
        {
            get => NormalizeReferencedPathTracingTransportDebugMode(
                m_ReferencedPathTracingTransportDebugMode);
            set => m_ReferencedPathTracingTransportDebugMode =
                NormalizeReferencedPathTracingTransportDebugMode(value);
        }

        internal ReferencedPathTracingEnvironmentDebugMode
            referencedPathTracingEnvironmentDebugMode
        {
            get => NormalizeReferencedPathTracingEnvironmentDebugMode(
                m_ReferencedPathTracingEnvironmentDebugMode);
            set => m_ReferencedPathTracingEnvironmentDebugMode =
                NormalizeReferencedPathTracingEnvironmentDebugMode(value);
        }

        internal ExposureDebugMode exposureMode
        {
            get => m_ExposureMode;
            set => m_ExposureMode = value;
        }

        internal float debugExposure
        {
            get => m_DebugExposure;
            set => m_DebugExposure = value;
        }

        internal bool centerHistogramAroundMiddleGrey
        {
            get => m_CenterHistogramAroundMiddleGrey;
            set => m_CenterHistogramAroundMiddleGrey = value;
        }

        internal bool showTonemapCurveAlongHistogramView
        {
            get => m_ShowTonemapCurveAlongHistogramView;
            set => m_ShowTonemapCurveAlongHistogramView = value;
        }

        internal bool displayMaskOnly
        {
            get => m_DisplayMaskOnly;
            set => m_DisplayMaskOnly = value;
        }

        internal bool displayOnSceneOverlay
        {
            get => m_DisplayOnSceneOverlay;
            set => m_DisplayOnSceneOverlay = value;
        }

        internal float overlayAmount
        {
            get => m_OverlayAmount;
            set => m_OverlayAmount = value;
        }

        internal int arraySlice
        {
            get => m_ArraySlice;
            set => m_ArraySlice = value;
        }

        internal float overlayExposure
        {
            get => m_OverlayExposure;
            set => m_OverlayExposure = value;
        }

        internal float overlayOpacity
        {
            get => m_OverlayOpacity;
            set => m_OverlayOpacity = value;
        }

        internal OverlayDebugVisualizationMode visualizationMode
        {
            get => OverlayDebugPass.NormalizeVisualizationMode(m_VisualizationMode);
            set => m_VisualizationMode = OverlayDebugPass.NormalizeVisualizationMode(value);
        }

        internal OverlayDebugDepthMode depthMode
        {
            get => m_DepthMode;
            set => m_DepthMode = value;
        }

        internal float depthMipLevel
        {
            get => Mathf.Clamp01(m_DepthMipLevel);
            set => m_DepthMipLevel = Mathf.Clamp01(value);
        }

        internal bool depthRemapEnabled
        {
            get => m_DepthRemapEnabled;
            set => m_DepthRemapEnabled = value;
        }

        internal float depthRemapMin
        {
            get => Mathf.Min(Mathf.Clamp01(m_DepthRemapMin), depthRemapMax);
            set => m_DepthRemapMin = Mathf.Min(Mathf.Clamp01(value), depthRemapMax);
        }

        internal float depthRemapMax
        {
            get => Mathf.Max(Mathf.Clamp(m_DepthRemapMax, 0.01f, 1f), Mathf.Clamp01(m_DepthRemapMin));
            set => m_DepthRemapMax = Mathf.Max(Mathf.Clamp(value, 0.01f, 1f), Mathf.Clamp01(m_DepthRemapMin));
        }

        internal OverlayDebugChannelMode channelMode
        {
            get => OverlayDebugPass.NormalizeChannelMode(m_ChannelMode);
            set => m_ChannelMode = OverlayDebugPass.NormalizeChannelMode(value);
        }

        internal MaterialDebugVisualizationMode materialDebugMode
        {
            get => m_MaterialDebugMode;
            set => m_MaterialDebugMode = value;
        }

        internal float materialDebugExposure
        {
            get => m_MaterialDebugExposure;
            set => m_MaterialDebugExposure = value;
        }

        internal VisibilityBufferDebugVisualizationMode visibilityBufferDebugMode
        {
            get => NormalizeVisibilityBufferDebugMode(m_VisibilityBufferDebugMode);
            set => m_VisibilityBufferDebugMode = NormalizeVisibilityBufferDebugMode(value);
        }

        internal float visibilityBufferDebugExposure
        {
            get => Mathf.Clamp(m_VisibilityBufferDebugExposure, -16f, 16f);
            set => m_VisibilityBufferDebugExposure = Mathf.Clamp(value, -16f, 16f);
        }

        internal float visibilityBufferWireframeThickness
        {
            get => Mathf.Max(0.1f, m_VisibilityBufferWireframeThickness);
            set => m_VisibilityBufferWireframeThickness = Mathf.Max(0.1f, value);
        }

        internal bool forceMeshletCullingFromMainCamera
        {
            get => m_ForceMeshletCullingFromMainCamera;
            set => m_ForceMeshletCullingFromMainCamera = value;
        }

        internal ReflectionProbeAtlasDebugMode reflectionProbeAtlasDebugMode
        {
            get => ReflectionProbeAtlasDebugPass.NormalizeDebugMode(m_ReflectionProbeAtlasDebugMode);
            set => m_ReflectionProbeAtlasDebugMode = ReflectionProbeAtlasDebugPass.NormalizeDebugMode(value);
        }

        internal int reflectionProbeAtlasArraySlice
        {
            get => ClampReflectionProbeAtlasArraySlice(m_ReflectionProbeAtlasArraySlice);
            set => m_ReflectionProbeAtlasArraySlice = ClampReflectionProbeAtlasArraySlice(value);
        }

        internal int reflectionProbeAtlasMipLevel
        {
            get => ClampReflectionProbeAtlasMipLevel(m_ReflectionProbeAtlasMipLevel);
            set => m_ReflectionProbeAtlasMipLevel = ClampReflectionProbeAtlasMipLevel(value);
        }

        internal float reflectionProbeAtlasExposure
        {
            get => m_ReflectionProbeAtlasExposure;
            set => m_ReflectionProbeAtlasExposure = value;
        }

        internal float slider
        {
            get => m_Slider;
            set => m_Slider = value;
        }

        internal VirtualTextureDebugMode virtualTextureDebugMode
        {
            get => m_VirtualTextureDebugMode;
            set => m_VirtualTextureDebugMode = value;
        }

        internal VirtualTextureVisualizationMode virtualTextureVisualizationMode
        {
            get => NormalizeVirtualTextureVisualizationMode(m_VirtualTextureVisualizationMode);
            set => m_VirtualTextureVisualizationMode = NormalizeVirtualTextureVisualizationMode(value);
        }

        internal VirtualTextureVisualizationTarget virtualTextureVisualizationTarget
        {
            get => NormalizeVirtualTextureVisualizationTarget(m_VirtualTextureVisualizationTarget);
            set => m_VirtualTextureVisualizationTarget = NormalizeVirtualTextureVisualizationTarget(value);
        }

        internal VirtualTextureVisualizationLayer virtualTextureVisualizationLayer
        {
            get => NormalizeVirtualTextureVisualizationLayer(m_VirtualTextureVisualizationLayer);
            set => m_VirtualTextureVisualizationLayer = NormalizeVirtualTextureVisualizationLayer(value);
        }

        internal float virtualTextureVisualizationOverlayAmount
        {
            get => Mathf.Clamp01(m_VirtualTextureVisualizationOverlayAmount);
            set => m_VirtualTextureVisualizationOverlayAmount = Mathf.Clamp01(value);
        }

        internal float virtualTextureVisualizationOpacity
        {
            get => Mathf.Clamp01(m_VirtualTextureVisualizationOpacity);
            set => m_VirtualTextureVisualizationOpacity = Mathf.Clamp01(value);
        }

        internal VirtualTextureStatsViewMode virtualTextureStatsViewMode
        {
            get => m_VirtualTextureStatsViewMode;
            set => m_VirtualTextureStatsViewMode = value;
        }

        internal Camera virtualTextureStatsCamera
        {
            get => m_VirtualTextureStatsCamera;
            set => m_VirtualTextureStatsCamera = value;
        }

        public bool AreAnySettingsActive =>
            m_TileClusterDebug != TileClusterDebug.None
            || tileClusterDebugByCategory != TileClusterCategoryDebug.Punctual
            || materialFeatureVariantDebug != MaterialFeatureVariantDebug.All
            || m_ClusterDebugMode != ClusterDebugMode.VisualizeOpaque
            || !Mathf.Approximately(m_ClusterDebugDistance, 1f)
            || reGIRDebugMode != DefaultReGIRDebugMode
            || !Mathf.Approximately(m_ReGIRDebugOpacity, DefaultReGIRDebugOpacity)
            || referencedPathTracingTransportDebugMode
                != DefaultReferencedPathTracingTransportDebugMode
            || referencedPathTracingEnvironmentDebugMode
                != DefaultReferencedPathTracingEnvironmentDebugMode
            || m_ExposureMode != ExposureDebugMode.None
            || !Mathf.Approximately(m_DebugExposure, 0f)
            || m_CenterHistogramAroundMiddleGrey
            || !m_ShowTonemapCurveAlongHistogramView
            || m_DisplayMaskOnly
            || !m_DisplayOnSceneOverlay
            || !Mathf.Approximately(m_OverlayAmount, 0f)
            || m_ArraySlice != 0
            || !Mathf.Approximately(m_OverlayExposure, 0f)
            || !Mathf.Approximately(m_OverlayOpacity, 1f)
            || visualizationMode != OverlayDebugVisualizationMode.Auto
            || m_DepthMode != OverlayDebugDepthMode.Raw
            || !Mathf.Approximately(depthMipLevel, 0f)
            || m_DepthRemapEnabled
            || !Mathf.Approximately(depthRemapMin, 0f)
            || !Mathf.Approximately(depthRemapMax, 1f)
            || channelMode != OverlayDebugChannelMode.RGB
            || m_MaterialDebugMode != MaterialDebugVisualizationMode.None
            || !Mathf.Approximately(m_MaterialDebugExposure, 0f)
            || m_VisibilityBufferDebugMode != VisibilityBufferDebugVisualizationMode.Cluster
            || !Mathf.Approximately(m_VisibilityBufferDebugExposure, 0f)
            || !Mathf.Approximately(
                visibilityBufferWireframeThickness,
                DefaultVisibilityBufferWireframeThickness)
            || m_ForceMeshletCullingFromMainCamera
            || reflectionProbeAtlasDebugMode != ReflectionProbeAtlasDebugMode.None
            || m_ReflectionProbeAtlasArraySlice != 0
            || m_ReflectionProbeAtlasMipLevel != 0
            || !Mathf.Approximately(m_ReflectionProbeAtlasExposure, 0f)
            || !Mathf.Approximately(m_Slider, 50f)
            || m_VirtualTextureDebugMode != VirtualTextureDebugMode.None
            || virtualTextureVisualizationMode != VirtualTextureVisualizationMode.None
            || m_VirtualTextureStatsViewMode != VirtualTextureStatsViewMode.Auto
            || m_VirtualTextureStatsCamera != null;

        public IDebugDisplaySettingsPanelDisposable CreatePanel()
        {
            return new SettingsPanel(this);
        }

        public void Reset()
        {
            m_TileClusterDebug = TileClusterDebug.None;
            m_TileClusterDebugByCategory = TileClusterCategoryDebug.Punctual;
            m_MaterialFeatureVariantDebug = MaterialFeatureVariantDebug.All;
            m_ClusterDebugMode = ClusterDebugMode.VisualizeOpaque;
            m_ClusterDebugDistance = 1f;
            m_ReGIRDebugMode = DefaultReGIRDebugMode;
            m_ReGIRDebugOpacity = DefaultReGIRDebugOpacity;
            m_ReferencedPathTracingTransportDebugMode =
                DefaultReferencedPathTracingTransportDebugMode;
            m_ReferencedPathTracingEnvironmentDebugMode =
                DefaultReferencedPathTracingEnvironmentDebugMode;
            m_ExposureMode = ExposureDebugMode.None;
            m_DebugExposure = 0f;
            m_CenterHistogramAroundMiddleGrey = false;
            m_ShowTonemapCurveAlongHistogramView = true;
            m_DisplayMaskOnly = false;
            m_DisplayOnSceneOverlay = true;
            m_OverlayAmount = 0f;
            m_ArraySlice = 0;
            m_OverlayExposure = 0f;
            m_OverlayOpacity = 1f;
            m_VisualizationMode = OverlayDebugVisualizationMode.Auto;
            m_DepthMode = OverlayDebugDepthMode.Raw;
            m_DepthMipLevel = 0f;
            m_DepthRemapEnabled = false;
            m_DepthRemapMin = 0f;
            m_DepthRemapMax = 1f;
            m_ChannelMode = OverlayDebugChannelMode.RGB;
            m_MaterialDebugMode = MaterialDebugVisualizationMode.None;
            m_MaterialDebugExposure = 0f;
            m_VisibilityBufferDebugMode = VisibilityBufferDebugVisualizationMode.Cluster;
            m_VisibilityBufferDebugExposure = 0f;
            m_VisibilityBufferWireframeThickness =
                DefaultVisibilityBufferWireframeThickness;
            m_ForceMeshletCullingFromMainCamera = false;
            m_ReflectionProbeAtlasDebugMode = ReflectionProbeAtlasDebugMode.None;
            m_ReflectionProbeAtlasArraySlice = 0;
            m_ReflectionProbeAtlasMipLevel = 0;
            m_ReflectionProbeAtlasExposure = 0f;
            m_Slider = 50f;
            m_VirtualTextureDebugMode = VirtualTextureDebugMode.None;
            m_VirtualTextureVisualizationMode = VirtualTextureVisualizationMode.None;
            m_VirtualTextureVisualizationTarget = VirtualTextureVisualizationTarget.Auto;
            m_VirtualTextureVisualizationLayer = VirtualTextureVisualizationLayer.BaseColor;
            m_VirtualTextureVisualizationOverlayAmount = 0f;
            m_VirtualTextureVisualizationOpacity = 1f;
            m_VirtualTextureStatsViewMode = VirtualTextureStatsViewMode.Auto;
            m_VirtualTextureStatsCamera = null;
        }

        private static VirtualTextureVisualizationMode NormalizeVirtualTextureVisualizationMode(
            VirtualTextureVisualizationMode value)
        {
            return value == VirtualTextureVisualizationMode.None
                || value is >= VirtualTextureVisualizationMode.PhysicalCache
                    and <= VirtualTextureVisualizationMode.PageTablePhysicalPage
                    ? value
                    : VirtualTextureVisualizationMode.None;
        }

        private static VisibilityBufferDebugVisualizationMode NormalizeVisibilityBufferDebugMode(
            VisibilityBufferDebugVisualizationMode value)
        {
            return value is >= VisibilityBufferDebugVisualizationMode.Instance
                and <= VisibilityBufferDebugVisualizationMode.BarycentricCoordinates
                    ? value
                    : VisibilityBufferDebugVisualizationMode.Cluster;
        }

        private static VirtualTextureVisualizationTarget NormalizeVirtualTextureVisualizationTarget(
            VirtualTextureVisualizationTarget value)
        {
            return value is >= VirtualTextureVisualizationTarget.Auto
                and <= VirtualTextureVisualizationTarget.FirstAvailable
                    ? value
                    : VirtualTextureVisualizationTarget.Auto;
        }

        private static VirtualTextureVisualizationLayer NormalizeVirtualTextureVisualizationLayer(
            VirtualTextureVisualizationLayer value)
        {
            return value is >= VirtualTextureVisualizationLayer.BaseColor
                and <= VirtualTextureVisualizationLayer.Mask
                    ? value
                    : VirtualTextureVisualizationLayer.BaseColor;
        }

        private static ReferencedPathTracingTransportDebugMode
            NormalizeReferencedPathTracingTransportDebugMode(
                ReferencedPathTracingTransportDebugMode value)
        {
            return value is >= ReferencedPathTracingTransportDebugMode.NeePdfs
                and <= ReferencedPathTracingTransportDebugMode
                    .StochasticTransparency
                    ? value
                    : ReferencedPathTracingTransportDebugMode.Combined;
        }

        private static ReferencedPathTracingEnvironmentDebugMode
            NormalizeReferencedPathTracingEnvironmentDebugMode(
                ReferencedPathTracingEnvironmentDebugMode value)
        {
            return value is
                ReferencedPathTracingEnvironmentDebugMode.EnvironmentOnly
                or ReferencedPathTracingEnvironmentDebugMode.PrimaryBackgroundOnly
                or ReferencedPathTracingEnvironmentDebugMode.IndirectMissOnly
                    ? value
                    : ReferencedPathTracingEnvironmentDebugMode.Combined;
        }

        private static TileClusterCategoryDebug NormalizeTileClusterCategory(TileClusterCategoryDebug value)
        {
            const int supportedMask =
                (int)TileClusterCategoryDebug.Punctual
                | (int)TileClusterCategoryDebug.Area
                | (int)TileClusterCategoryDebug.Environment
                | (int)TileClusterCategoryDebug.Decal;
            var normalized = (TileClusterCategoryDebug)((int)value & supportedMask);
            return normalized == 0
                ? TileClusterCategoryDebug.Punctual
                : normalized;
        }

        private static MaterialFeatureVariantDebug NormalizeMaterialFeatureVariantDebug(MaterialFeatureVariantDebug value)
        {
            return value switch
            {
                MaterialFeatureVariantDebug.All => MaterialFeatureVariantDebug.All,
                MaterialFeatureVariantDebug.Lit => MaterialFeatureVariantDebug.Lit,
                MaterialFeatureVariantDebug.Fabric => MaterialFeatureVariantDebug.Fabric,
                MaterialFeatureVariantDebug.ClearCoat => MaterialFeatureVariantDebug.ClearCoat,
                MaterialFeatureVariantDebug.SSRReceive => MaterialFeatureVariantDebug.SSRReceive,
                MaterialFeatureVariantDebug.DecalReceive => MaterialFeatureVariantDebug.DecalReceive,
                _ => MaterialFeatureVariantDebug.All,
            };
        }

        private static int ClampReflectionProbeAtlasArraySlice(int value)
        {
            var sliceCount = VividReflectionProbeAtlasSystem.GetAtlasDebugSliceCount();
            return sliceCount > 0
                ? Mathf.Clamp(value, 0, sliceCount - 1)
                : Mathf.Max(0, value);
        }

        private static int ClampReflectionProbeAtlasMipLevel(int value)
        {
            var mipCount = VividReflectionProbeAtlasSystem.GetAtlasDebugMipCount();
            return mipCount > 0
                ? Mathf.Clamp(value, 0, mipCount - 1)
                : Mathf.Max(0, value);
        }

        private static class Strings
        {
            public const string RootName = "VividRP Debug";
            public const string ClusterName = "Cluster";
            public const string ReGIRName = "ReGIR";
            public const string ReferencedPathTracingName =
                "Reference Path Tracing";
            public const string ExposureName = "Exposure";
            public const string OverlayName = "Overlay";
            public const string MaterialName = "Material";
            public const string VisibilityBufferName = "Visibility Buffer";
            public const string ReflectionProbeAtlasName = "Reflection Probe Atlas";
            public const string SliderName = "Slider";
            public const string VirtualTextureName = "Virtual Texture";

            public static readonly NameAndTooltip TileClusterDebug = new()
            {
                name = "Mode",
                tooltip = "Select the cluster debug visualization mode."
            };

            public static readonly NameAndTooltip TileClusterDebugByCategory = new()
            {
                name = "Categories",
                tooltip = "Select which light categories are included in the cluster debug view."
            };

            public static readonly NameAndTooltip MaterialFeatureVariantDebug = new()
            {
                name = "Material Feature",
                tooltip = "Select which material feature coverage is shown in the material feature variants tile view."
            };

            public static readonly NameAndTooltip ClusterDebugMode = new()
            {
                name = "Slice Mode",
                tooltip = "Choose how the cluster debug pass visualizes clustered lighting."
            };

            public static readonly NameAndTooltip ClusterDebugDistance = new()
            {
                name = "Distance",
                tooltip = "Distance used when visualizing cluster slices."
            };

            public static readonly NameAndTooltip ReGIRDebugMode = new()
            {
                name = "Mode",
                tooltip = "Select the ReGIR debug visualization mode."
            };

            public static readonly NameAndTooltip ReGIRDebugOpacity = new()
            {
                name = "Opacity",
                tooltip = "Opacity of the ReGIR debug overlay."
            };

            public static readonly NameAndTooltip
                ReferencedPathTracingTransportDebugMode = new()
                {
                    name = "Transport",
                    tooltip =
                        "Select the Reference Path Tracing transport diagnostic written to DebugTexture."
                };

            public static readonly NameAndTooltip
                ReferencedPathTracingEnvironmentDebugMode = new()
                {
                    name = "Environment",
                    tooltip =
                        "Select the Reference Path Tracing environment diagnostic written to DebugTexture."
                };

            public static readonly NameAndTooltip ExposureMode = new()
            {
                name = "Mode",
                tooltip = "Select the exposure debug visualization mode."
            };

            public static readonly NameAndTooltip DebugExposure = new()
            {
                name = "Debug Exposure",
                tooltip = "Exposure override used by the exposure debug views."
            };

            public static readonly NameAndTooltip CenterHistogramAroundMiddleGrey = new()
            {
                name = "Center Histogram Around Middle Grey",
                tooltip = "Center the histogram around middle grey."
            };

            public static readonly NameAndTooltip ShowTonemapCurveAlongHistogramView = new()
            {
                name = "Show Tonemap Curve Along Histogram View",
                tooltip = "Overlay the tonemap curve in histogram mode."
            };

            public static readonly NameAndTooltip DisplayMaskOnly = new()
            {
                name = "Display Mask Only",
                tooltip = "Only show the exposure metering mask."
            };

            public static readonly NameAndTooltip DisplayOnSceneOverlay = new()
            {
                name = "Display On Scene Overlay",
                tooltip = "Draw the exposure debug visualization as a scene overlay."
            };

            public static readonly NameAndTooltip OverlayAmount = new()
            {
                name = "Overlay Amount",
                tooltip = "Controls the size of the overlay viewport."
            };

            public static readonly NameAndTooltip ArraySlice = new()
            {
                name = "Array Slice",
                tooltip = "Slice index for texture array debug overlays."
            };

            public static readonly NameAndTooltip OverlayExposure = new()
            {
                name = "Exposure",
                tooltip = "Exposure compensation applied to the overlay."
            };

            public static readonly NameAndTooltip OverlayOpacity = new()
            {
                name = "Opacity",
                tooltip = "Opacity of the overlay debug image."
            };

            public static readonly NameAndTooltip VisualizationMode = new()
            {
                name = "Visualization Mode",
                tooltip = "Select how the overlay debug texture is visualized."
            };

            public static readonly NameAndTooltip DepthMode = new()
            {
                name = "Depth Mode",
                tooltip = "Select how depth textures are visualized in the overlay."
            };

            public static readonly NameAndTooltip DepthMipLevel = new()
            {
                name = "Depth Mip Level",
                tooltip = "Normalized mip level used when sampling depth pyramid debug textures."
            };

            public static readonly NameAndTooltip DepthRemapEnabled = new()
            {
                name = "Enable Depth Remap",
                tooltip = "Remap depth visualization to a normalized eye-depth range."
            };

            public static readonly NameAndTooltip DepthRemapMin = new()
            {
                name = "Depth Remap Min",
                tooltip = "Minimum normalized depth range used by depth remapping."
            };

            public static readonly NameAndTooltip DepthRemapMax = new()
            {
                name = "Depth Remap Max",
                tooltip = "Maximum normalized depth range used by depth remapping."
            };

            public static readonly NameAndTooltip ChannelMode = new()
            {
                name = "Channel Mode",
                tooltip = "Select which debug texture channel is displayed in the overlay."
            };

            public static readonly NameAndTooltip MaterialDebugMode = new()
            {
                name = "Mode",
                tooltip = "Select the material GBuffer value to visualize."
            };

            public static readonly NameAndTooltip MaterialDebugExposure = new()
            {
                name = "Exposure",
                tooltip = "Exposure compensation applied to HDR material debug values."
            };

            public static readonly NameAndTooltip VisibilityBufferDebugMode = new()
            {
                name = "Mode",
                tooltip = "Select how visibility buffer values are visualized."
            };

            public static readonly NameAndTooltip VisibilityBufferDebugExposure = new()
            {
                name = "Exposure",
                tooltip = "Exposure compensation applied to the visibility buffer debug view."
            };

            public static readonly NameAndTooltip VisibilityBufferWireframeThickness = new()
            {
                name = "Wireframe Thickness",
                tooltip = "Wire thickness in pixels when the resolve visualization mode is Wireframe."
            };

            public static readonly NameAndTooltip ForceMeshletCullingFromMainCamera = new()
            {
                name = "Force Culling From Main Camera",
                tooltip = "Use the scene MainCamera and its camera-relative HZB history when building meshlet GPU culling parameters."
            };

            public static readonly NameAndTooltip ReflectionProbeAtlasDebugMode = new()
            {
                name = "Mode",
                tooltip = "Select the reflection probe atlas debug visualization mode."
            };

            public static readonly NameAndTooltip ReflectionProbeAtlasArraySlice = new()
            {
                name = "Slice",
                tooltip = "Array slice sampled from the reflection probe atlas."
            };

            public static readonly NameAndTooltip ReflectionProbeAtlasMipLevel = new()
            {
                name = "Mip",
                tooltip = "Mip level sampled from the reflection probe atlas."
            };

            public static readonly NameAndTooltip ReflectionProbeAtlasExposure = new()
            {
                name = "Exposure",
                tooltip = "Exposure compensation applied to the reflection probe atlas debug view."
            };

            public static readonly NameAndTooltip Slider = new()
            {
                name = "Slider",
                tooltip = "Split position for the slider debug pass."
            };

            public static readonly NameAndTooltip VirtualTextureDebugMode = new()
            {
                name = "Mode",
                tooltip = "Select the virtual texture debug visualization mode."
            };

            public static readonly NameAndTooltip VirtualTextureVisualizationMode = new()
            {
                name = "Visualization Mode",
                tooltip = "Select the virtual texture visualization. None disables the visualization pass output."
            };

            public static readonly NameAndTooltip VirtualTextureVisualizationTarget = new()
            {
                name = "Visualization Target",
                tooltip = "Select the virtual texture space visualized by the pass."
            };

            public static readonly NameAndTooltip VirtualTextureVisualizationLayer = new()
            {
                name = "Physical Cache Layer",
                tooltip = "Select the Base Color, Normal, or Mask layer displayed by physical-cache views."
            };

            public static readonly NameAndTooltip VirtualTextureVisualizationOverlayAmount = new()
            {
                name = "Overlay Size",
                tooltip = "Set the visualization size from the minimum corner overlay to full screen."
            };

            public static readonly NameAndTooltip VirtualTextureVisualizationOpacity = new()
            {
                name = "Opacity",
                tooltip = "Set the virtual texture visualization opacity."
            };

            public static readonly NameAndTooltip VirtualTextureStatsViewMode = new()
            {
                name = "Stats Source",
                tooltip = "Select which camera view supplies the virtual texture statistics."
            };

            public static readonly NameAndTooltip VirtualTextureStatsCamera = new()
            {
                name = "Stats Camera",
                tooltip = "Camera used when the virtual texture stats source is set to Selected Camera."
            };
        }

        [DisplayInfo(name = "Rendering", order = 6)]
        private sealed class SettingsPanel : DebugDisplaySettingsPanel<VividRenderingDebugSettingsData>
        {
            public SettingsPanel(VividRenderingDebugSettingsData data)
                : base(data)
            {
                AddWidget(CreateRootFoldout(data));
            }

            private static DebugUI.Foldout CreateRootFoldout(VividRenderingDebugSettingsData data)
            {
                var root = new DebugUI.Foldout
                {
                    displayName = Strings.RootName,
                    isHeader = true,
                    opened = true,
                };

                root.children.Add(CreateClusterFoldout(data));
                root.children.Add(CreateReGIRFoldout(data));
                root.children.Add(
                    CreateReferencedPathTracingFoldout(data));
                root.children.Add(CreateExposureFoldout(data));
                root.children.Add(CreateOverlayFoldout(data));
                root.children.Add(CreateMaterialFoldout(data));
                root.children.Add(CreateVisibilityBufferFoldout(data));
                root.children.Add(CreateReflectionProbeAtlasFoldout(data));
                root.children.Add(CreateSliderFoldout(data));
                root.children.Add(CreateVirtualTextureFoldout(data));
                return root;
            }

            private static DebugUI.Foldout CreateClusterFoldout(VividRenderingDebugSettingsData data)
            {
                var foldout = new DebugUI.Foldout
                {
                    displayName = Strings.ClusterName,
                    opened = true,
                };

                foldout.children.Add(CreateEnumField(
                    Strings.TileClusterDebug,
                    () => data.tileClusterDebug,
                    value => data.tileClusterDebug = value));
                foldout.children.Add(new DebugUI.BitField
                {
                    nameAndTooltip = Strings.TileClusterDebugByCategory,
                    enumType = typeof(TileClusterCategoryDebug),
                    getter = () => data.tileClusterDebugByCategory,
                    setter = value => data.tileClusterDebugByCategory = (TileClusterCategoryDebug)value,
                    isHiddenCallback = () => data.tileClusterDebug == TileClusterDebug.MaterialFeatureVariants,
                });
                var materialFeatureField = CreateEnumField(
                    Strings.MaterialFeatureVariantDebug,
                    () => data.materialFeatureVariantDebug,
                    value => data.materialFeatureVariantDebug = value);
                materialFeatureField.isHiddenCallback = () => data.tileClusterDebug != TileClusterDebug.MaterialFeatureVariants;
                foldout.children.Add(materialFeatureField);

                var clusterModeField = CreateEnumField(
                    Strings.ClusterDebugMode,
                    () => data.clusterDebugMode,
                    value => data.clusterDebugMode = value);
                clusterModeField.isHiddenCallback = () => data.tileClusterDebug != TileClusterDebug.Cluster;
                foldout.children.Add(clusterModeField);
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.ClusterDebugDistance,
                    getter = () => data.clusterDebugDistance,
                    setter = value => data.clusterDebugDistance = value,
                    min = () => 0f,
                    isHiddenCallback = () => data.tileClusterDebug != TileClusterDebug.Cluster,
                });
                return foldout;
            }

            private static DebugUI.Foldout
                CreateReferencedPathTracingFoldout(
                    VividRenderingDebugSettingsData data)
            {
                var foldout = new DebugUI.Foldout
                {
                    displayName = Strings.ReferencedPathTracingName,
                    opened = true,
                };

                foldout.children.Add(CreateEnumField(
                    Strings.ReferencedPathTracingTransportDebugMode,
                    () => data.referencedPathTracingTransportDebugMode,
                    value =>
                        data.referencedPathTracingTransportDebugMode = value));
                foldout.children.Add(CreateEnumField(
                    Strings.ReferencedPathTracingEnvironmentDebugMode,
                    () => data.referencedPathTracingEnvironmentDebugMode,
                    value =>
                        data.referencedPathTracingEnvironmentDebugMode = value));
                return foldout;
            }

            private static DebugUI.Foldout CreateReGIRFoldout(VividRenderingDebugSettingsData data)
            {
                var foldout = new DebugUI.Foldout
                {
                    displayName = Strings.ReGIRName,
                    opened = true,
                };

                foldout.children.Add(CreateEnumField(
                    Strings.ReGIRDebugMode,
                    () => data.reGIRDebugMode,
                    value => data.reGIRDebugMode = value));
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.ReGIRDebugOpacity,
                    getter = () => data.reGIRDebugOpacity,
                    setter = value => data.reGIRDebugOpacity = value,
                    min = () => 0f,
                    max = () => 1f,
                });
                return foldout;
            }

            private static DebugUI.Foldout CreateExposureFoldout(VividRenderingDebugSettingsData data)
            {
                var foldout = new DebugUI.Foldout
                {
                    displayName = Strings.ExposureName,
                    opened = true,
                };

                foldout.children.Add(CreateEnumField(
                    Strings.ExposureMode,
                    () => data.exposureMode,
                    value => data.exposureMode = value));
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.DebugExposure,
                    getter = () => data.debugExposure,
                    setter = value => data.debugExposure = value,
                    min = () => -16f,
                    max = () => 16f,
                });
                foldout.children.Add(new DebugUI.BoolField
                {
                    nameAndTooltip = Strings.CenterHistogramAroundMiddleGrey,
                    getter = () => data.centerHistogramAroundMiddleGrey,
                    setter = value => data.centerHistogramAroundMiddleGrey = value,
                });
                foldout.children.Add(new DebugUI.BoolField
                {
                    nameAndTooltip = Strings.ShowTonemapCurveAlongHistogramView,
                    getter = () => data.showTonemapCurveAlongHistogramView,
                    setter = value => data.showTonemapCurveAlongHistogramView = value,
                });
                foldout.children.Add(new DebugUI.BoolField
                {
                    nameAndTooltip = Strings.DisplayMaskOnly,
                    getter = () => data.displayMaskOnly,
                    setter = value => data.displayMaskOnly = value,
                });
                foldout.children.Add(new DebugUI.BoolField
                {
                    nameAndTooltip = Strings.DisplayOnSceneOverlay,
                    getter = () => data.displayOnSceneOverlay,
                    setter = value => data.displayOnSceneOverlay = value,
                });
                return foldout;
            }

            private static DebugUI.Foldout CreateOverlayFoldout(VividRenderingDebugSettingsData data)
            {
                var foldout = new DebugUI.Foldout
                {
                    displayName = Strings.OverlayName,
                    opened = true,
                };

                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.OverlayAmount,
                    getter = () => data.overlayAmount,
                    setter = value => data.overlayAmount = value,
                    min = () => 0f,
                    max = () => 1f,
                });
                foldout.children.Add(new DebugUI.IntField
                {
                    nameAndTooltip = Strings.ArraySlice,
                    getter = () => data.arraySlice,
                    setter = value => data.arraySlice = value,
                    min = () => 0,
                });
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.OverlayExposure,
                    getter = () => data.overlayExposure,
                    setter = value => data.overlayExposure = value,
                    min = () => -16f,
                    max = () => 16f,
                });
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.OverlayOpacity,
                    getter = () => data.overlayOpacity,
                    setter = value => data.overlayOpacity = value,
                    min = () => 0f,
                    max = () => 1f,
                });
                foldout.children.Add(CreateEnumField(
                    Strings.VisualizationMode,
                    () => data.visualizationMode,
                    value => data.visualizationMode = value));
                foldout.children.Add(CreateEnumField(
                    Strings.DepthMode,
                    () => data.depthMode,
                    value => data.depthMode = value));
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.DepthMipLevel,
                    getter = () => data.depthMipLevel,
                    setter = value => data.depthMipLevel = value,
                    min = () => 0f,
                    max = () => 1f,
                });
                foldout.children.Add(new DebugUI.BoolField
                {
                    nameAndTooltip = Strings.DepthRemapEnabled,
                    getter = () => data.depthRemapEnabled,
                    setter = value => data.depthRemapEnabled = value,
                });
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.DepthRemapMin,
                    getter = () => data.depthRemapMin,
                    setter = value => data.depthRemapMin = value,
                    min = () => 0f,
                    max = () => data.depthRemapMax,
                    isHiddenCallback = () => !data.depthRemapEnabled,
                });
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.DepthRemapMax,
                    getter = () => data.depthRemapMax,
                    setter = value => data.depthRemapMax = value,
                    min = () => data.depthRemapMin,
                    max = () => 1f,
                    isHiddenCallback = () => !data.depthRemapEnabled,
                });
                foldout.children.Add(CreateEnumField(
                    Strings.ChannelMode,
                    () => data.channelMode,
                    value => data.channelMode = value));
                return foldout;
            }

            private static DebugUI.Foldout CreateMaterialFoldout(VividRenderingDebugSettingsData data)
            {
                var foldout = new DebugUI.Foldout
                {
                    displayName = Strings.MaterialName,
                    opened = true,
                };

                foldout.children.Add(CreateEnumField(
                    Strings.MaterialDebugMode,
                    () => data.materialDebugMode,
                    value => data.materialDebugMode = value));
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.MaterialDebugExposure,
                    getter = () => data.materialDebugExposure,
                    setter = value => data.materialDebugExposure = value,
                    min = () => -16f,
                    max = () => 16f,
                });
                return foldout;
            }

            private static DebugUI.Foldout CreateVisibilityBufferFoldout(VividRenderingDebugSettingsData data)
            {
                var foldout = new DebugUI.Foldout
                {
                    displayName = Strings.VisibilityBufferName,
                    opened = true,
                };

                foldout.children.Add(CreateEnumField(
                    Strings.VisibilityBufferDebugMode,
                    () => data.visibilityBufferDebugMode,
                    value => data.visibilityBufferDebugMode = value));
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.VisibilityBufferDebugExposure,
                    getter = () => data.visibilityBufferDebugExposure,
                    setter = value => data.visibilityBufferDebugExposure = value,
                    min = () => -16f,
                    max = () => 16f,
                });
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.VisibilityBufferWireframeThickness,
                    getter = () => data.visibilityBufferWireframeThickness,
                    setter = value => data.visibilityBufferWireframeThickness = value,
                    min = () => 0.1f,
                    isHiddenCallback = () =>
                        data.visibilityBufferDebugMode
                            != VisibilityBufferDebugVisualizationMode.Wireframe,
                });
                foldout.children.Add(new DebugUI.BoolField
                {
                    nameAndTooltip = Strings.ForceMeshletCullingFromMainCamera,
                    getter = () => data.forceMeshletCullingFromMainCamera,
                    setter = value => data.forceMeshletCullingFromMainCamera = value,
                });
                foldout.children.Add(new DebugUI.Value
                {
                    displayName = "Occlusion Observation",
                    getter = GetGPUDrivenOcclusionObservationStatus,
                    isHiddenCallback = () => !data.forceMeshletCullingFromMainCamera,
                });
                return foldout;
            }

            private static object GetGPUDrivenOcclusionObservationStatus()
            {
                var asset = VividRenderPipelineAsset.GetActiveAsset();
                if (asset == null || !asset.EnableGPUDrivenOcclusionCulling)
                    return "Disabled in Pipeline Asset";

                Camera mainCamera = Camera.main;
                if (mainCamera == null)
                    mainCamera = UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Exclude);
                if (mainCamera == null)
                    return "Waiting for culling camera";

                string cameraName = string.IsNullOrEmpty(mainCamera.name) ? "Camera" : mainCamera.name;
                return GPUDriven.VividGPUDrivenOcclusionHistorySystem.TryGetObservationParameters(
                    mainCamera,
                    out _,
                    out _)
                    ? $"Ready ({cameraName} HZB pair)"
                    : $"Waiting for two HZB frames ({cameraName})";
            }

            private static DebugUI.Foldout CreateReflectionProbeAtlasFoldout(VividRenderingDebugSettingsData data)
            {
                var foldout = new DebugUI.Foldout
                {
                    displayName = Strings.ReflectionProbeAtlasName,
                    opened = true,
                };

                foldout.children.Add(CreateEnumField(
                    Strings.ReflectionProbeAtlasDebugMode,
                    () => data.reflectionProbeAtlasDebugMode,
                    value => data.reflectionProbeAtlasDebugMode = value));
                foldout.children.Add(new DebugUI.IntField
                {
                    nameAndTooltip = Strings.ReflectionProbeAtlasArraySlice,
                    getter = () => data.reflectionProbeAtlasArraySlice,
                    setter = value => data.reflectionProbeAtlasArraySlice = value,
                    min = () => 0,
                    max = () => Mathf.Max(0, VividReflectionProbeAtlasSystem.GetAtlasDebugSliceCount() - 1),
                });
                foldout.children.Add(new DebugUI.IntField
                {
                    nameAndTooltip = Strings.ReflectionProbeAtlasMipLevel,
                    getter = () => data.reflectionProbeAtlasMipLevel,
                    setter = value => data.reflectionProbeAtlasMipLevel = value,
                    min = () => 0,
                    max = () => Mathf.Max(0, VividReflectionProbeAtlasSystem.GetAtlasDebugMipCount() - 1),
                });
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.ReflectionProbeAtlasExposure,
                    getter = () => data.reflectionProbeAtlasExposure,
                    setter = value => data.reflectionProbeAtlasExposure = value,
                    min = () => -16f,
                    max = () => 16f,
                });
                return foldout;
            }

            private static DebugUI.Foldout CreateSliderFoldout(VividRenderingDebugSettingsData data)
            {
                var foldout = new DebugUI.Foldout
                {
                    displayName = Strings.SliderName,
                    opened = true,
                };

                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.Slider,
                    getter = () => data.slider,
                    setter = value => data.slider = value,
                    min = () => 0f,
                    max = () => 100f,
                });
                return foldout;
            }

            private static DebugUI.Foldout CreateVirtualTextureFoldout(VividRenderingDebugSettingsData data)
            {
                var foldout = new DebugUI.Foldout
                {
                    displayName = Strings.VirtualTextureName,
                    opened = true,
                };

                foldout.children.Add(CreateEnumField(
                    Strings.VirtualTextureDebugMode,
                    () => data.virtualTextureDebugMode,
                    value => data.virtualTextureDebugMode = value));
                foldout.children.Add(CreateEnumField(
                    Strings.VirtualTextureVisualizationMode,
                    () => data.virtualTextureVisualizationMode,
                    value => data.virtualTextureVisualizationMode = value));
                var targetField = CreateEnumField(
                    Strings.VirtualTextureVisualizationTarget,
                    () => data.virtualTextureVisualizationTarget,
                    value => data.virtualTextureVisualizationTarget = value);
                targetField.isHiddenCallback = () =>
                    data.virtualTextureVisualizationMode == VirtualTextureVisualizationMode.None;
                foldout.children.Add(targetField);
                var layerField = CreateEnumField(
                    Strings.VirtualTextureVisualizationLayer,
                    () => data.virtualTextureVisualizationLayer,
                    value => data.virtualTextureVisualizationLayer = value);
                layerField.isHiddenCallback = () =>
                    data.virtualTextureVisualizationMode != VirtualTextureVisualizationMode.PhysicalCache
                    && data.virtualTextureVisualizationMode
                        != VirtualTextureVisualizationMode.PhysicalCacheAndPageTableResidency;
                foldout.children.Add(layerField);
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.VirtualTextureVisualizationOverlayAmount,
                    getter = () => data.virtualTextureVisualizationOverlayAmount,
                    setter = value => data.virtualTextureVisualizationOverlayAmount = value,
                    min = () => 0f,
                    max = () => 1f,
                    isHiddenCallback = () =>
                        data.virtualTextureVisualizationMode == VirtualTextureVisualizationMode.None,
                });
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.VirtualTextureVisualizationOpacity,
                    getter = () => data.virtualTextureVisualizationOpacity,
                    setter = value => data.virtualTextureVisualizationOpacity = value,
                    min = () => 0f,
                    max = () => 1f,
                    isHiddenCallback = () =>
                        data.virtualTextureVisualizationMode == VirtualTextureVisualizationMode.None,
                });
                foldout.children.Add(CreateEnumField(
                    Strings.VirtualTextureStatsViewMode,
                    () => data.virtualTextureStatsViewMode,
                    value => data.virtualTextureStatsViewMode = value));
                foldout.children.Add(CreateVirtualTextureStatsCameraSelector(data));
                foldout.children.Add(CreateStatsValue("View", () => GetVirtualTextureDisplayStats(data).ViewLabel));
                foldout.children.Add(CreateStatsValue("Camera Frame", () => FormatFrameIndex(GetVirtualTextureDisplayStats(data).CameraFrameIndex)));
                foldout.children.Add(CreateStatsValue("Last Readback Frame", () => FormatFrameIndex(GetVirtualTextureDisplayStats(data).LastReadbackFrame)));
                foldout.children.Add(CreateStatsValue("Render Size", () => GetVirtualTextureDisplayStats(data).RenderSizeLabel));
                foldout.children.Add(CreateStatsValue("Pixel Size", () => GetVirtualTextureDisplayStats(data).PixelSizeLabel));
                foldout.children.Add(CreateStatsValue("Feedback Supported", () => GetVirtualTextureDisplayStats(data).FeedbackSupported));
                foldout.children.Add(CreateStatsValue("Feedback Capacity", () => GetVirtualTextureDisplayStats(data).FeedbackCapacity));
                foldout.children.Add(CreateStatsValue("Physical Pools", () => GetVirtualTextureDisplayStats(data).PhysicalPoolCount));
                foldout.children.Add(CreateStatsValue("Pool Resident Pages", () => GetVirtualTextureDisplayStats(data).PhysicalPoolResidentPageCount));
                foldout.children.Add(CreateStatsValue("Pool Free Pages", () => GetVirtualTextureDisplayStats(data).PhysicalPoolFreePageCount));
                foldout.children.Add(CreateStatsValue("Pool Locked Pages", () => GetVirtualTextureDisplayStats(data).PhysicalPoolLockedPageCount));
                foldout.children.Add(CreateStatsValue("Pool Evicted Pages", () => GetVirtualTextureDisplayStats(data).PhysicalPoolEvictedPageCount));
                foldout.children.Add(CreateStatsValue("Resident Pages", () => GetVirtualTextureDisplayStats(data).ResidentPageCount));
                foldout.children.Add(CreateStatsValue("Free Pages", () => GetVirtualTextureDisplayStats(data).FreePageCount));
                foldout.children.Add(CreateStatsValue("Pending Uploads", () => GetVirtualTextureDisplayStats(data).PendingUploadCount));
                foldout.children.Add(CreateStatsValue("Evictions", () => GetVirtualTextureDisplayStats(data).EvictionCount));
                foldout.children.Add(CreateStatsValue("Faults", () => GetVirtualTextureDisplayStats(data).FaultCount));
                foldout.children.Add(CreateStatsValue("Deduplicated Requests", () => GetVirtualTextureDisplayStats(data).DeduplicatedRequestCount));
                foldout.children.Add(CreateStatsValue("Feedback Overflow", () => GetVirtualTextureDisplayStats(data).FeedbackOverflowCount));
                foldout.children.Add(CreateStatsValue("Pending Mip Gap Avg", () => GetVirtualTextureDisplayStats(data).PendingMipGapAverage));
                foldout.children.Add(CreateStatsValue("Pending Mip Gap Max", () => GetVirtualTextureDisplayStats(data).PendingMipGapMax));
                foldout.children.Add(CreateStatsValue("Prefetch Requests", () => GetVirtualTextureDisplayStats(data).PrefetchRequestCount));
                foldout.children.Add(CreateStatsValue("In-Flight Upload Batches", () => GetVirtualTextureDisplayStats(data).InFlightUploadBatchCount));
                foldout.children.Add(CreateStatsValue("Duplicate Uploads", () => GetVirtualTextureDisplayStats(data).DuplicateUploadCount));
                foldout.children.Add(CreateStatsValue("Skipped Uploads", () => GetVirtualTextureDisplayStats(data).SkippedUploadCount));
                foldout.children.Add(CreateStatsValue("Fallback Samples", () => GetVirtualTextureDisplayStats(data).FallbackSampleCount));
                foldout.children.Add(new DebugUI.Value
                {
                    displayName = "Status",
                    getter = () =>
                    {
                        string status = GetVirtualTextureDisplayStats(data).StatusMessage;
                        return string.IsNullOrEmpty(status) ? "OK" : status;
                    },
                });
                return foldout;
            }

            private static DebugUI.CameraSelector CreateVirtualTextureStatsCameraSelector(
                VividRenderingDebugSettingsData data)
            {
                return new DebugUI.CameraSelector
                {
                    nameAndTooltip = Strings.VirtualTextureStatsCamera,
                    getter = () => data.virtualTextureStatsCamera,
                    setter = value => data.virtualTextureStatsCamera = value as Camera,
                    isHiddenCallback = () => data.virtualTextureStatsViewMode != VirtualTextureStatsViewMode.SelectedCamera,
                };
            }

            private static DebugUI.EnumField CreateEnumField<TEnum>(
                NameAndTooltip nameAndTooltip,
                Func<TEnum> getter,
                Action<TEnum> setter)
                where TEnum : Enum
            {
                var enumType = typeof(TEnum);
                var values = GetEnumValuesInDisplayOrder<TEnum>();

                return new DebugUI.EnumField
                {
                    nameAndTooltip = nameAndTooltip,
                    autoEnum = enumType,
                    getter = () => Convert.ToInt32(getter()),
                    setter = value => setter((TEnum)Enum.ToObject(enumType, value)),
                    getIndex = () => Mathf.Max(0, Array.IndexOf(values, getter())),
                    setIndex = value => setter(values[Mathf.Clamp(value, 0, values.Length - 1)]),
                };
            }

            private static TEnum[] GetEnumValuesInDisplayOrder<TEnum>()
                where TEnum : Enum
            {
                var enumType = typeof(TEnum);
                var values = new List<TEnum>();
                var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
                foreach (var field in fields)
                {
                    if (field.IsDefined(typeof(ObsoleteAttribute), false)
                        || field.IsDefined(typeof(HideInInspector), false))
                    {
                        continue;
                    }

                    values.Add((TEnum)field.GetValue(null));
                }

                return values.ToArray();
            }

            private static object FormatFrameIndex(int frameIndex)
            {
                return frameIndex >= 0 ? frameIndex : "N/A";
            }

            private static VirtualTextureStats GetVirtualTextureDisplayStats(
                VividRenderingDebugSettingsData data)
            {
                return VirtualTextureStatsRegistry.GetDisplayStats(
                    data.virtualTextureStatsViewMode,
                    data.virtualTextureStatsCamera);
            }

            private static DebugUI.Value CreateStatsValue(string displayName, Func<object> getter)
            {
                return new DebugUI.Value
                {
                    displayName = displayName,
                    getter = getter,
                };
            }
        }
    }
}
