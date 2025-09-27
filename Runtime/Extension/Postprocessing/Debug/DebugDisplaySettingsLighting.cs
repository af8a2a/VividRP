using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using NameAndTooltip = UnityEngine.Rendering.DebugUI.Widget.NameAndTooltip;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Lighting-related Rendering Debugger settings.
    /// </summary>
    [URPHelpURL("features/rendering-debugger-reference", "lighting")]
    public class DebugDisplaySettingsLighting : IDebugDisplaySettingsData
    {
        /// <summary>
        /// Current debug lighting mode.
        /// </summary>
        public DebugLightingMode lightingDebugMode { get; set; }

        /// <summary>
        /// Current debug lighting feature flags mask that allows selective disabling individual lighting components.
        /// </summary>
        public DebugLightingFeatureFlags lightingFeatureFlags { get; set; }

        /// <summary>
        /// Current HDR debug mode.
        /// </summary>
        public HDRDebugMode hdrDebugMode { get; set; }
        
        
        /// <summary>
        /// Current Tile and Cluster Debug Mode.
        /// </summary>
        public DebugTileClusterMode tileClusterDebugMode { get; set; }

        public DebugClusterCategory clusterCategoryDebugMode { get; set; }

        public int clusterDebugID = 0;

        
        internal RTASDebugView rtasDebugView = RTASDebugView.None;
        internal RTASDebugMode rtasDebugMode = RTASDebugMode.InstanceID;


        static internal class Strings
        {
            public static readonly NameAndTooltip LightingDebugMode = new() { name = "Lighting Debug Mode", tooltip = "Use the drop-down to select which lighting and shadow debug information to overlay on the screen." };
            public static readonly NameAndTooltip LightingFeatures = new() { name = "Lighting Features", tooltip = "Filter and debug selected lighting features in the system." };
            public static readonly NameAndTooltip HDRDebugMode = new() { name = "HDR Debug Mode", tooltip = "Select which HDR brightness debug information to overlay on the screen." };
            public static readonly NameAndTooltip TileClusterDebug = new() { name = "Tile/Cluster Debug", tooltip = "Use the drop-down to select the Light type that you want to show the Tile/Cluster debug information for." };
            public static readonly NameAndTooltip ClusterDebugID = new() { name = "Cluster Debug ID", tooltip = "Select cluster ID to display." };
            public static readonly NameAndTooltip ClusterCategoryDebug = new() { name = "Cluster Category", tooltip = "Change cluster debug category." };
            
            public static readonly DebugUI.Widget.NameAndTooltip RTASDebugView = new()
            {
                name = "Ray Tracing Acceleration Structure View",
                tooltip = "Use the drop-down to select a rendering view to display the ray tracing acceleration structure."
            };

            public static readonly DebugUI.Widget.NameAndTooltip RTASDebugMode = new()
            {
                name = "Ray Tracing Acceleration Structure Mode",
                tooltip = "Use the drop-down to select a rendering mode to display the ray tracing acceleration structure."
            };


        }

        internal static class WidgetFactory
        {
            internal static DebugUI.Widget CreateLightingDebugMode(SettingsPanel panel) => new DebugUI.EnumField
            {
                nameAndTooltip = Strings.LightingDebugMode,
                autoEnum = typeof(DebugLightingMode),
                getter = () => (int)panel.data.lightingDebugMode,
                setter = (value) => panel.data.lightingDebugMode = (DebugLightingMode)value,
                getIndex = () => (int)panel.data.lightingDebugMode,
                setIndex = (value) => panel.data.lightingDebugMode = (DebugLightingMode)value
            };

            internal static DebugUI.Widget CreateLightingFeatures(SettingsPanel panel) => new DebugUI.BitField
            {
                nameAndTooltip = Strings.LightingFeatures,
                getter = () => panel.data.lightingFeatureFlags,
                setter = (value) => panel.data.lightingFeatureFlags = (DebugLightingFeatureFlags)value,
                enumType = typeof(DebugLightingFeatureFlags),
            };

            internal static DebugUI.Widget CreateHDRDebugMode(SettingsPanel panel) => new DebugUI.EnumField
            {
                nameAndTooltip = Strings.HDRDebugMode,
                autoEnum = typeof(HDRDebugMode),
                getter = () => (int)panel.data.hdrDebugMode,
                setter = (value) => panel.data.hdrDebugMode = (HDRDebugMode)value,
                getIndex = () => (int)panel.data.hdrDebugMode,
                setIndex = (value) => panel.data.hdrDebugMode = (HDRDebugMode)value
            };


            internal static DebugUI.Widget CreateTileClusterDebugMode(SettingsPanel panel) => new DebugUI.EnumField
            {
                nameAndTooltip = Strings.TileClusterDebug,
                autoEnum = typeof(DebugTileClusterMode),
                getter = () => (int)panel.data.tileClusterDebugMode,
                setter = (value) => panel.data.tileClusterDebugMode = (DebugTileClusterMode)value,
                getIndex = () => (int)panel.data.tileClusterDebugMode,
                setIndex = (value) => panel.data.tileClusterDebugMode = (DebugTileClusterMode)value,
            };

            internal static DebugUI.Widget CreateClusterCategoryDebugMode(SettingsPanel panel) => new DebugUI.Container
            {
                isHiddenCallback = () =>
                    panel.data.tileClusterDebugMode != DebugTileClusterMode.ClusterForOpaque &&
                    panel.data.tileClusterDebugMode != DebugTileClusterMode.ClusterForTile,
                children =
                {
                    new DebugUI.EnumField
                    {
                        nameAndTooltip = Strings.ClusterCategoryDebug,
                        autoEnum = typeof(DebugClusterCategory),
                        getter = () => (int)panel.data.clusterCategoryDebugMode,
                        setter = (value) => panel.data.clusterCategoryDebugMode = (DebugClusterCategory)value,
                        getIndex = () => (int)panel.data.clusterCategoryDebugMode,
                        setIndex = (value) => panel.data.clusterCategoryDebugMode = (DebugClusterCategory)value,
                    }
                }
            };

            internal static DebugUI.Widget CreateClusterIDSelect(SettingsPanel panel) => new DebugUI.Container()
            {
                isHiddenCallback = () => panel.data.tileClusterDebugMode != DebugTileClusterMode.ClusterForTile,
                children =
                {
                    new DebugUI.IntField
                    {
                        nameAndTooltip = Strings.ClusterDebugID,
                        getter = () => panel.data.clusterDebugID,
                        setter = value => panel.data.clusterDebugID = value,
                        incStep = 1,
                        min = () => 0,
                        max = () => 64
                    }
                }
            };
            
            internal static DebugUI.Widget CreateRTASDebugView(SettingsPanel panel) => new DebugUI.EnumField
            {
                nameAndTooltip = Strings.RTASDebugView,
                getter = () => (int)panel.data.rtasDebugView,
                setter = value => panel.data.rtasDebugView = (RTASDebugView)value,
                autoEnum = typeof(RTASDebugView),
                getIndex = () => (int)panel.data.rtasDebugView,
                setIndex = (value) => panel.data.rtasDebugView = (RTASDebugView)value,
            };

            internal static DebugUI.Widget CreateRTASDebugDebugMode(SettingsPanel panel) => new DebugUI.Container
            {
                isHiddenCallback = () => panel.data.rtasDebugView == RTASDebugView.None,
                children =
                {
                    new DebugUI.EnumField
                    {
                        isHiddenCallback = () => panel.data.rtasDebugView == RTASDebugView.None,
                        nameAndTooltip = Strings.RTASDebugMode,
                        getter = () => (int)panel.data.rtasDebugMode,
                        setter = value => panel.data.rtasDebugMode = (RTASDebugMode)value,
                        autoEnum = typeof(RTASDebugMode),
                        getIndex = () => (int)panel.data.rtasDebugMode,
                        setIndex = value => { panel.data.rtasDebugMode = (RTASDebugMode)value; }
                    }
                }
            };

        }

        [DisplayInfo(name = "Lighting", order = 3)]
        internal class SettingsPanel : DebugDisplaySettingsPanel<DebugDisplaySettingsLighting>
        {
            public SettingsPanel(DebugDisplaySettingsLighting data)
                : base(data)
            {
                AddWidget(new DebugUI.RuntimeDebugShadersMessageBox());

                AddWidget(new DebugUI.Foldout
                {
                    displayName = "Lighting Debug Modes",
                    flags = DebugUI.Flags.FrequentlyUsed,
                    opened = true,
                    children =
                    {
                        WidgetFactory.CreateLightingDebugMode(this),
                        WidgetFactory.CreateHDRDebugMode(this),
                        WidgetFactory.CreateLightingFeatures(this),
                        WidgetFactory.CreateTileClusterDebugMode(this),
                        WidgetFactory.CreateClusterIDSelect(this),
                        WidgetFactory.CreateClusterCategoryDebugMode(this),
                        WidgetFactory.CreateRTASDebugView(this),
                        WidgetFactory.CreateRTASDebugDebugMode(this),
                    }
                });
            }
        }

        #region IDebugDisplaySettingsData

        /// <inheritdoc/>
        public bool AreAnySettingsActive => (lightingDebugMode != DebugLightingMode.None) 
                                            || (lightingFeatureFlags != DebugLightingFeatureFlags.None) 
                                            || (hdrDebugMode != HDRDebugMode.None) 
                                            || (tileClusterDebugMode != DebugTileClusterMode.None)
                                            || rtasDebugView != RTASDebugView.None;

        /// <inheritdoc/>
        public bool IsPostProcessingAllowed => (lightingDebugMode != DebugLightingMode.Reflections && lightingDebugMode != DebugLightingMode.ReflectionsWithSmoothness);

        /// <inheritdoc/>
        public bool IsLightingActive => true;

        /// <inheritdoc/>
        IDebugDisplaySettingsPanelDisposable IDebugDisplaySettingsData.CreatePanel()
        {
            return new SettingsPanel(this);
        }

        #endregion
    }
}
