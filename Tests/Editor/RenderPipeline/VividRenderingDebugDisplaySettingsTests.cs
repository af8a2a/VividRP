using System;
using NUnit.Framework;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VividRenderingDebugDisplaySettingsTests
    {
        private DebugDisplaySettingsUI m_DebugDisplaySettingsUI;

        [SetUp]
        public void SetUp()
        {
            VividRenderingDebugDisplaySettings.Data.Reset();
            m_DebugDisplaySettingsUI = new DebugDisplaySettingsUI();
            m_DebugDisplaySettingsUI.RegisterDebug(VividRenderingDebugDisplaySettings.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            m_DebugDisplaySettingsUI?.UnregisterDebug();
            m_DebugDisplaySettingsUI = null;
            VividRenderingDebugDisplaySettings.Data.Reset();
        }

        [Test]
        public void RegisterDebug_AddsVividRpDebugFoldoutsToRenderingPanel()
        {
            Assert.That(DebugManager.instance.PanelIndex("Rendering"), Is.GreaterThanOrEqualTo(0));
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Cluster"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Cluster -> Material Feature"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> ReGIR"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> ReGIR -> Mode"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> ReGIR -> Opacity"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Exposure"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay -> Channel Mode"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay -> Depth Mip Level"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay -> Enable Depth Remap"), Is.Not.Null);
            VividRenderingDebugDisplaySettings.Data.depthRemapEnabled = true;
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay -> Depth Remap Min"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay -> Depth Remap Max"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Material"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Material -> Mode"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Material -> Exposure"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Visibility Buffer"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas -> Mode"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas -> Slice"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas -> Mip"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas -> Exposure"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Slider"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Stats Source"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Stats Camera"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> View"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Camera Frame"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Last Readback Frame"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Render Size"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Pixel Size"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Feedback Supported"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Feedback Capacity"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Deduplicated Requests"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Feedback Overflow"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Pending Mip Gap Avg"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Pending Mip Gap Max"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Prefetch Requests"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> In-Flight Upload Batches"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Duplicate Uploads"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Skipped Uploads"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Fallback Samples"), Is.Not.Null);
        }

        [Test]
        public void Reset_RestoresVirtualTextureDebugDefaults()
        {
            VividRenderingDebugDisplaySettings.Data.virtualTextureDebugMode = VirtualTextureDebugMode.PhysicalPageId;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationMode = VirtualTextureVisualizationMode.PageTableResidency;
            VividRenderingDebugDisplaySettings.Data.virtualTextureStatsViewMode = VirtualTextureStatsViewMode.SelectedCamera;

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(VividRenderingDebugDisplaySettings.Data.virtualTextureDebugMode, Is.EqualTo(VirtualTextureDebugMode.None));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationMode,
                Is.EqualTo(VirtualTextureVisualizationMode.UsePassSettings));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureStatsViewMode,
                Is.EqualTo(VirtualTextureStatsViewMode.Auto));
        }

        [Test]
        public void Reset_RestoresMaterialDebugDefaults()
        {
            VividRenderingDebugDisplaySettings.Data.materialDebugMode = MaterialDebugVisualizationMode.Emissive;
            VividRenderingDebugDisplaySettings.Data.materialDebugExposure = 3f;

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.materialDebugMode,
                Is.EqualTo(MaterialDebugVisualizationMode.None));
            Assert.That(VividRenderingDebugDisplaySettings.Data.materialDebugExposure, Is.EqualTo(0f));
        }

        [Test]
        public void Reset_RestoresOverlayDepthDebugDefaults()
        {
            VividRenderingDebugDisplaySettings.Data.depthMipLevel = 0.75f;
            VividRenderingDebugDisplaySettings.Data.depthRemapEnabled = true;
            VividRenderingDebugDisplaySettings.Data.depthRemapMin = 0.2f;
            VividRenderingDebugDisplaySettings.Data.depthRemapMax = 0.8f;

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(VividRenderingDebugDisplaySettings.Data.depthMipLevel, Is.EqualTo(0f));
            Assert.That(VividRenderingDebugDisplaySettings.Data.depthRemapEnabled, Is.False);
            Assert.That(VividRenderingDebugDisplaySettings.Data.depthRemapMin, Is.EqualTo(0f));
            Assert.That(VividRenderingDebugDisplaySettings.Data.depthRemapMax, Is.EqualTo(1f));
        }

        [Test]
        public void OverlayDepthRemapWidgets_ClampRangeAndTrackVisibility()
        {
            var remapEnabledWidget = DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay -> Enable Depth Remap")
                as DebugUI.BoolField;
            var remapMinWidget = DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay -> Depth Remap Min")
                as DebugUI.FloatField;
            var remapMaxWidget = DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay -> Depth Remap Max")
                as DebugUI.FloatField;

            Assert.That(remapEnabledWidget, Is.Not.Null);
            Assert.That(remapMinWidget, Is.Not.Null);
            Assert.That(remapMaxWidget, Is.Not.Null);
            Assert.That(remapMinWidget.isHiddenCallback(), Is.True);
            Assert.That(remapMaxWidget.isHiddenCallback(), Is.True);

            remapEnabledWidget.setter(true);
            remapMaxWidget.setter(0.5f);
            remapMinWidget.setter(0.75f);

            Assert.That(remapMinWidget.isHiddenCallback(), Is.False);
            Assert.That(remapMaxWidget.isHiddenCallback(), Is.False);
            Assert.That(VividRenderingDebugDisplaySettings.Data.depthRemapMin, Is.EqualTo(0.5f));
            Assert.That(VividRenderingDebugDisplaySettings.Data.depthRemapMax, Is.EqualTo(0.5f));
        }

        [Test]
        public void OverlayChannelModeWidget_MapsDropdownIndexToEnumValue()
        {
            var widget = DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay -> Channel Mode")
                as DebugUI.EnumField;

            Assert.That(widget, Is.Not.Null);
            var alphaIndex = Array.IndexOf(
                widget.enumValues,
                (int)OverlayDebugChannelMode.Alpha);
            Assert.That(alphaIndex, Is.GreaterThanOrEqualTo(0));

            widget.setIndex(alphaIndex);

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.channelMode,
                Is.EqualTo(OverlayDebugChannelMode.Alpha));
            Assert.That(widget.getIndex(), Is.EqualTo(alphaIndex));
            Assert.That(widget.getter(), Is.EqualTo((int)OverlayDebugChannelMode.Alpha));
        }

        [Test]
        public void Reset_RestoresClusterDebugDefaults()
        {
            VividRenderingDebugDisplaySettings.Data.tileClusterDebug = TileClusterDebug.MaterialFeatureVariants;
            VividRenderingDebugDisplaySettings.Data.materialFeatureVariantDebug = MaterialFeatureVariantDebug.Fabric;
            VividRenderingDebugDisplaySettings.Data.clusterDebugMode = ClusterDebugMode.VisualizeSlice;
            VividRenderingDebugDisplaySettings.Data.clusterDebugDistance = 8f;

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(VividRenderingDebugDisplaySettings.Data.tileClusterDebug, Is.EqualTo(TileClusterDebug.None));
            Assert.That(VividRenderingDebugDisplaySettings.Data.materialFeatureVariantDebug, Is.EqualTo(MaterialFeatureVariantDebug.All));
            Assert.That(VividRenderingDebugDisplaySettings.Data.clusterDebugMode, Is.EqualTo(ClusterDebugMode.VisualizeOpaque));
            Assert.That(VividRenderingDebugDisplaySettings.Data.clusterDebugDistance, Is.EqualTo(1f));
        }

        [Test]
        public void MaterialDebugModeWidget_MapsDropdownIndexToEnumValue()
        {
            var widget = DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Material -> Mode")
                as DebugUI.EnumField;

            Assert.That(widget, Is.Not.Null);
            var baseColorIndex = Array.IndexOf(
                widget.enumValues,
                (int)MaterialDebugVisualizationMode.BaseColor);
            Assert.That(baseColorIndex, Is.GreaterThanOrEqualTo(0));

            widget.setIndex(baseColorIndex);

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.materialDebugMode,
                Is.EqualTo(MaterialDebugVisualizationMode.BaseColor));
            Assert.That(widget.getIndex(), Is.EqualTo(baseColorIndex));
            Assert.That(widget.getter(), Is.EqualTo((int)MaterialDebugVisualizationMode.BaseColor));
        }

        [Test]
        public void ClusterMaterialFeatureWidget_MapsDropdownIndexToEnumValue()
        {
            var widget = DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Cluster -> Material Feature")
                as DebugUI.EnumField;

            Assert.That(widget, Is.Not.Null);
            var fabricIndex = Array.IndexOf(
                widget.enumValues,
                (int)MaterialFeatureVariantDebug.Fabric);
            Assert.That(fabricIndex, Is.GreaterThanOrEqualTo(0));

            widget.setIndex(fabricIndex);

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.materialFeatureVariantDebug,
                Is.EqualTo(MaterialFeatureVariantDebug.Fabric));
            Assert.That(widget.getIndex(), Is.EqualTo(fabricIndex));
            Assert.That(widget.getter(), Is.EqualTo((int)MaterialFeatureVariantDebug.Fabric));
        }

        [Test]
        public void Reset_RestoresVisibilityBufferDebugDefaults()
        {
            VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugMode =
                VisibilityBufferDebugVisualizationMode.ClusterLOD;
            VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugExposure = 3f;
            VividRenderingDebugDisplaySettings.Data.forceMeshletCullingFromMainCamera = true;

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugMode,
                Is.EqualTo(VisibilityBufferDebugVisualizationMode.Cluster));
            Assert.That(VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugExposure, Is.EqualTo(0f));
            Assert.That(VividRenderingDebugDisplaySettings.Data.forceMeshletCullingFromMainCamera, Is.False);
        }

        [Test]
        public void Reset_RestoresReGIRDebugDefaults()
        {
            VividRenderingDebugDisplaySettings.Data.reGIRDebugMode =
                ReGIRDebugVisualizationMode.ReservoirWeight;
            VividRenderingDebugDisplaySettings.Data.reGIRDebugOpacity = 0.2f;

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.reGIRDebugMode,
                Is.EqualTo(VividRenderingDebugSettingsData.DefaultReGIRDebugMode));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.reGIRDebugOpacity,
                Is.EqualTo(VividRenderingDebugSettingsData.DefaultReGIRDebugOpacity));
        }

        [Test]
        public void Reset_RestoresReflectionProbeAtlasDebugDefaults()
        {
            VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasDebugMode =
                ReflectionProbeAtlasDebugMode.Atlas;
            VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasArraySlice = 3;
            VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasMipLevel = 2;
            VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasExposure = 1.5f;

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasDebugMode,
                Is.EqualTo(ReflectionProbeAtlasDebugMode.None));
            Assert.That(VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasArraySlice, Is.Zero);
            Assert.That(VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasMipLevel, Is.Zero);
            Assert.That(VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasExposure, Is.Zero);
        }

        [Test]
        public void ReflectionProbeAtlasIndexWidgets_UseDynamicAtlasLimits()
        {
            var sliceWidget = DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas -> Slice")
                as DebugUI.IntField;
            var mipWidget = DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas -> Mip")
                as DebugUI.IntField;

            Assert.That(sliceWidget, Is.Not.Null);
            Assert.That(mipWidget, Is.Not.Null);
            Assert.That(sliceWidget.min(), Is.Zero);
            Assert.That(mipWidget.min(), Is.Zero);
            Assert.That(sliceWidget.max, Is.Not.Null);
            Assert.That(mipWidget.max, Is.Not.Null);
            Assert.That(sliceWidget.max(), Is.GreaterThanOrEqualTo(0));
            Assert.That(mipWidget.max(), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void AreAnySettingsActive_TracksMaterialDebugOverrides()
        {
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.materialDebugMode = MaterialDebugVisualizationMode.NormalWS;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);

            VividRenderingDebugDisplaySettings.Data.Reset();
            VividRenderingDebugDisplaySettings.Data.materialDebugExposure = 1f;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);
        }

        [Test]
        public void AreAnySettingsActive_TracksOverlayDepthOverrides()
        {
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.depthMipLevel = 0.5f;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);

            VividRenderingDebugDisplaySettings.Data.Reset();
            VividRenderingDebugDisplaySettings.Data.depthRemapEnabled = true;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);

            VividRenderingDebugDisplaySettings.Data.Reset();
            VividRenderingDebugDisplaySettings.Data.depthRemapMax = 0.5f;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);
        }

        [Test]
        public void AreAnySettingsActive_TracksOverlayChannelMode()
        {
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.channelMode = OverlayDebugChannelMode.Blue;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(VividRenderingDebugDisplaySettings.Data.channelMode, Is.EqualTo(OverlayDebugChannelMode.RGB));
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);
        }

        [Test]
        public void AreAnySettingsActive_TracksClusterMaterialFeatureOverrides()
        {
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.materialFeatureVariantDebug = MaterialFeatureVariantDebug.DecalReceive;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);
        }

        [Test]
        public void AreAnySettingsActive_TracksVisibilityBufferDebugOverrides()
        {
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugMode =
                VisibilityBufferDebugVisualizationMode.Triangle;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);

            VividRenderingDebugDisplaySettings.Data.Reset();
            VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugExposure = 1f;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);
        }

        [Test]
        public void AreAnySettingsActive_TracksReGIRDebugOverrides()
        {
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.reGIRDebugMode =
                ReGIRDebugVisualizationMode.Cells;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);

            VividRenderingDebugDisplaySettings.Data.Reset();
            VividRenderingDebugDisplaySettings.Data.reGIRDebugOpacity = 0.25f;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);
        }

        [Test]
        public void AreAnySettingsActive_TracksReflectionProbeAtlasDebugOverrides()
        {
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasDebugMode =
                ReflectionProbeAtlasDebugMode.Atlas;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);

            VividRenderingDebugDisplaySettings.Data.Reset();
            VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasMipLevel = 1;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);
        }

        [Test]
        public void AreAnySettingsActive_TracksMeshletCullingMainCameraLock()
        {
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.forceMeshletCullingFromMainCamera = true;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);
        }

        [Test]
        public void TileClusterCategoryDebug_PreservesSupportedFlags()
        {
            VividRenderingDebugDisplaySettings.Data.tileClusterDebugByCategory =
                TileClusterCategoryDebug.Area
                | TileClusterCategoryDebug.Environment
                | TileClusterCategoryDebug.Decal;

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.tileClusterDebugByCategory,
                Is.EqualTo(
                    TileClusterCategoryDebug.Area
                    | TileClusterCategoryDebug.Environment
                    | TileClusterCategoryDebug.Decal));
        }
    }
}
