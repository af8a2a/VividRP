using System;
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
        UsePassSettings = 0,
        None = 1,
        PhysicalCache = 2,
        PageTableResidency = 3,
        PhysicalCacheAndPageTableResidency = 4,
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

        [SerializeField]
        private TileClusterDebug m_TileClusterDebug = TileClusterDebug.None;

        [SerializeField]
        private TileClusterCategoryDebug m_TileClusterDebugByCategory = TileClusterCategoryDebug.Punctual;

        [SerializeField]
        private ClusterDebugMode m_ClusterDebugMode = ClusterDebugMode.VisualizeOpaque;

        [SerializeField]
        private float m_ClusterDebugDistance = 1f;

        [SerializeField]
        private ReGIRDebugVisualizationMode m_ReGIRDebugMode = DefaultReGIRDebugMode;

        [SerializeField]
        private float m_ReGIRDebugOpacity = DefaultReGIRDebugOpacity;

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
        private VisibilityBufferDebugVisualizationMode m_VisibilityBufferDebugMode =
            VisibilityBufferDebugVisualizationMode.Cluster;

        [SerializeField]
        private float m_VisibilityBufferDebugExposure;

        [SerializeField]
        private bool m_ForceMeshletCullingFromMainCamera;

        [SerializeField]
        private float m_Slider = 50f;

        [SerializeField]
        private VirtualTextureDebugMode m_VirtualTextureDebugMode = VirtualTextureDebugMode.None;

        [SerializeField]
        private VirtualTextureVisualizationMode m_VirtualTextureVisualizationMode = VirtualTextureVisualizationMode.UsePassSettings;

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

        internal VisibilityBufferDebugVisualizationMode visibilityBufferDebugMode
        {
            get => m_VisibilityBufferDebugMode;
            set => m_VisibilityBufferDebugMode = value;
        }

        internal float visibilityBufferDebugExposure
        {
            get => m_VisibilityBufferDebugExposure;
            set => m_VisibilityBufferDebugExposure = value;
        }

        internal bool forceMeshletCullingFromMainCamera
        {
            get => m_ForceMeshletCullingFromMainCamera;
            set => m_ForceMeshletCullingFromMainCamera = value;
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
            get => m_VirtualTextureVisualizationMode;
            set => m_VirtualTextureVisualizationMode = value;
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
            || m_ClusterDebugMode != ClusterDebugMode.VisualizeOpaque
            || !Mathf.Approximately(m_ClusterDebugDistance, 1f)
            || reGIRDebugMode != DefaultReGIRDebugMode
            || !Mathf.Approximately(m_ReGIRDebugOpacity, DefaultReGIRDebugOpacity)
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
            || m_VisibilityBufferDebugMode != VisibilityBufferDebugVisualizationMode.Cluster
            || !Mathf.Approximately(m_VisibilityBufferDebugExposure, 0f)
            || m_ForceMeshletCullingFromMainCamera
            || !Mathf.Approximately(m_Slider, 50f)
            || m_VirtualTextureDebugMode != VirtualTextureDebugMode.None
            || m_VirtualTextureVisualizationMode != VirtualTextureVisualizationMode.UsePassSettings
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
            m_ClusterDebugMode = ClusterDebugMode.VisualizeOpaque;
            m_ClusterDebugDistance = 1f;
            m_ReGIRDebugMode = DefaultReGIRDebugMode;
            m_ReGIRDebugOpacity = DefaultReGIRDebugOpacity;
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
            m_VisibilityBufferDebugMode = VisibilityBufferDebugVisualizationMode.Cluster;
            m_VisibilityBufferDebugExposure = 0f;
            m_ForceMeshletCullingFromMainCamera = false;
            m_Slider = 50f;
            m_VirtualTextureDebugMode = VirtualTextureDebugMode.None;
            m_VirtualTextureVisualizationMode = VirtualTextureVisualizationMode.UsePassSettings;
            m_VirtualTextureStatsViewMode = VirtualTextureStatsViewMode.Auto;
            m_VirtualTextureStatsCamera = null;
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

        private static class Strings
        {
            public const string RootName = "VividRP Debug";
            public const string ClusterName = "Cluster";
            public const string ReGIRName = "ReGIR";
            public const string ExposureName = "Exposure";
            public const string OverlayName = "Overlay";
            public const string VisibilityBufferName = "Visibility Buffer";
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

            public static readonly NameAndTooltip ForceMeshletCullingFromMainCamera = new()
            {
                name = "Force Culling From Main Camera",
                tooltip = "Use the scene MainCamera when building meshlet GPU culling parameters."
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
                name = "Overlay Mode",
                tooltip = "Override the virtual texture visualization pass mode, or keep the pass-defined setting."
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
                root.children.Add(CreateExposureFoldout(data));
                root.children.Add(CreateOverlayFoldout(data));
                root.children.Add(CreateVisibilityBufferFoldout(data));
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
                });
                foldout.children.Add(CreateEnumField(
                    Strings.ClusterDebugMode,
                    () => data.clusterDebugMode,
                    value => data.clusterDebugMode = value));
                foldout.children.Add(new DebugUI.FloatField
                {
                    nameAndTooltip = Strings.ClusterDebugDistance,
                    getter = () => data.clusterDebugDistance,
                    setter = value => data.clusterDebugDistance = value,
                    min = () => 0f,
                });
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
                foldout.children.Add(new DebugUI.BoolField
                {
                    nameAndTooltip = Strings.ForceMeshletCullingFromMainCamera,
                    getter = () => data.forceMeshletCullingFromMainCamera,
                    setter = value => data.forceMeshletCullingFromMainCamera = value,
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
                return new DebugUI.EnumField
                {
                    nameAndTooltip = nameAndTooltip,
                    autoEnum = typeof(TEnum),
                    getter = () => Convert.ToInt32(getter()),
                    setter = value => setter((TEnum)Enum.ToObject(typeof(TEnum), value)),
                    getIndex = () => Convert.ToInt32(getter()),
                    setIndex = value => setter((TEnum)Enum.ToObject(typeof(TEnum), value)),
                };
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
