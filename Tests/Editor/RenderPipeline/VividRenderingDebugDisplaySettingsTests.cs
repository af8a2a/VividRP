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
            Assert.That(
                DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Reference Path Tracing"),
                Is.Not.Null);
            Assert.That(
                DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Reference Path Tracing -> Transport"),
                Is.Not.Null);
            Assert.That(
                DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Reference Path Tracing -> Environment"),
                Is.Not.Null);
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
            Assert.That(
                DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Visibility Buffer -> Mode"),
                Is.Not.Null);
            Assert.That(
                DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Visibility Buffer -> Exposure"),
                Is.Not.Null);
            Assert.That(
                DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Visibility Buffer -> Wireframe Thickness"),
                Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas -> Mode"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas -> Slice"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas -> Mip"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Reflection Probe Atlas -> Exposure"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Slider"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Terrain RVT Visualization"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Visualization Mode"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Visualization Target"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Visualization Layer"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> World Units Per Page"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Mip Bias Override"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Feedback Overflow Count Override"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Fallback Sample Count Override"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Fresh Feedback (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Measured Overflow (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Overflow Input (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Overflow Pressure (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Measured Fallback (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Fallback Input (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Fallback Coverage (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Total Pressure (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Target Mip Bias (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Feedback Fault Overflow (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Feedback Resident Overflow (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Feedback Request Readback Errors (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Feedback Counter Readback Errors (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Fallback Nonresident Samples (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Fallback Resident Samples (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Resolved VT Samples (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Last Fresh Frame (Global)"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Last Fresh Resolved VT Samples (Global)"), Is.Not.Null);
            var resetVirtualTextureState = DebugManager.instance.GetItem(
                "Rendering -> VividRP Debug -> Virtual Texture -> Reset VT State") as DebugUI.Button;
            Assert.That(resetVirtualTextureState, Is.Not.Null);
            Assert.That(resetVirtualTextureState.action, Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Overlay Size"), Is.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Opacity"), Is.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Stats Source"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Stats Camera"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> View"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Camera Frame"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Last Readback Frame"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Render Size"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Pixel Size"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Feedback Supported"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Feedback Capacity"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> GPU VT Allocated"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Physical Atlas Allocated"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Physical Atlas Resident"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Page Tables"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Decoded Stream Cache"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Deduplicated Requests"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Feedback Overflow"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Pending Mip Gap Avg"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Pending Mip Gap Max"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Prefetch Requests"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> In-Flight Upload Batches"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Duplicate Uploads"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Skipped Uploads"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Fallback Samples"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Stream Saturated Requests"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture -> Adaptive Mip Bias"), Is.Not.Null);
        }

        [Test]
        public void Reset_RestoresVirtualTextureDebugDefaults()
        {
            VividRenderingDebugDisplaySettings.Data.virtualTextureDebugMode = VirtualTextureDebugMode.PhysicalPageId;
            VividRenderingDebugDisplaySettings.Data.terrainRuntimeVirtualTextureDebugMode =
                TerrainRuntimeVirtualTextureDebugMode.PageResidency;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationMode = VirtualTextureVisualizationMode.PageTableResidency;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationTarget = VirtualTextureVisualizationTarget.FirstPublic;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationLayer = VirtualTextureVisualizationLayer.Mask;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationWorldPageSize = 16f;
            VividRenderingDebugDisplaySettings.Data.virtualTextureAdaptiveMipBiasOverride = 2f;
            VividRenderingDebugDisplaySettings.Data.virtualTextureFeedbackOverflowCountOverride = 3;
            VividRenderingDebugDisplaySettings.Data.virtualTextureFallbackSampleCountOverride = 11;
            VividRenderingDebugDisplaySettings.Data.virtualTextureStatsViewMode = VirtualTextureStatsViewMode.SelectedCamera;

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(VividRenderingDebugDisplaySettings.Data.virtualTextureDebugMode, Is.EqualTo(VirtualTextureDebugMode.None));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.terrainRuntimeVirtualTextureDebugMode,
                Is.EqualTo(TerrainRuntimeVirtualTextureDebugMode.None));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationMode,
                Is.EqualTo(VirtualTextureVisualizationMode.None));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationTarget,
                Is.EqualTo(VirtualTextureVisualizationTarget.Auto));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationLayer,
                Is.EqualTo(VirtualTextureVisualizationLayer.BaseColor));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationWorldPageSize,
                Is.EqualTo(VividRenderingDebugSettingsData.DefaultVirtualTextureVisualizationWorldPageSize));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureAdaptiveMipBiasOverride,
                Is.EqualTo(VividRenderingDebugSettingsData.DefaultVirtualTextureAdaptiveMipBiasOverride));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureFeedbackOverflowCountOverride,
                Is.EqualTo(VividRenderingDebugSettingsData.DefaultVirtualTextureFeedbackOverflowCountOverride));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureFallbackSampleCountOverride,
                Is.EqualTo(VividRenderingDebugSettingsData.DefaultVirtualTextureFallbackSampleCountOverride));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureStatsViewMode,
                Is.EqualTo(VirtualTextureStatsViewMode.Auto));
        }

        [Test]
        public void TerrainRuntimeVirtualTextureVisualizationWidget_ControlsAndNormalizesMode()
        {
            var widget = DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Virtual Texture -> Terrain RVT Visualization")
                as DebugUI.EnumField;
            var layerWidget = DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Virtual Texture -> Visualization Layer")
                as DebugUI.EnumField;

            Assert.That(widget, Is.Not.Null);
            Assert.That(layerWidget, Is.Not.Null);
            int resolvedSurfaceIndex = Array.IndexOf(
                widget.enumValues,
                (int)TerrainRuntimeVirtualTextureDebugMode.ResolvedSurface);
            Assert.That(resolvedSurfaceIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(layerWidget.isHiddenCallback(), Is.True);

            widget.setIndex(resolvedSurfaceIndex);

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.terrainRuntimeVirtualTextureDebugMode,
                Is.EqualTo(TerrainRuntimeVirtualTextureDebugMode.ResolvedSurface));
            Assert.That(
                widget.getter(),
                Is.EqualTo((int)TerrainRuntimeVirtualTextureDebugMode.ResolvedSurface));
            Assert.That(layerWidget.isHiddenCallback(), Is.False);

            VividRenderingDebugDisplaySettings.Data.terrainRuntimeVirtualTextureDebugMode =
                (TerrainRuntimeVirtualTextureDebugMode)99;

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.terrainRuntimeVirtualTextureDebugMode,
                Is.EqualTo(TerrainRuntimeVirtualTextureDebugMode.None));
        }

        [Test]
        public void VirtualTextureFeedbackPressureOverrideWidgets_UseMeasuredCountSentinel()
        {
            var overflowWidget = DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Virtual Texture -> Feedback Overflow Count Override")
                as DebugUI.IntField;
            var fallbackWidget = DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Virtual Texture -> Fallback Sample Count Override")
                as DebugUI.IntField;

            Assert.That(overflowWidget, Is.Not.Null);
            Assert.That(fallbackWidget, Is.Not.Null);
            Assert.That(overflowWidget.min(), Is.EqualTo(-1));
            Assert.That(fallbackWidget.min(), Is.EqualTo(-1));

            overflowWidget.setter(3);
            fallbackWidget.setter(11);

            Assert.That(overflowWidget.getter(), Is.EqualTo(3));
            Assert.That(fallbackWidget.getter(), Is.EqualTo(11));

            overflowWidget.setter(-2);
            fallbackWidget.setter(-2);

            Assert.That(overflowWidget.getter(), Is.EqualTo(-1));
            Assert.That(fallbackWidget.getter(), Is.EqualTo(-1));
        }

        [Test]
        public void VirtualTextureVisualizationSettings_NormalizeLegacyAndInvalidValues()
        {
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationMode =
                (VirtualTextureVisualizationMode)1;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationTarget =
                (VirtualTextureVisualizationTarget)99;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationLayer =
                (VirtualTextureVisualizationLayer)99;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationWorldPageSize = -1f;

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationMode,
                Is.EqualTo(VirtualTextureVisualizationMode.None));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationTarget,
                Is.EqualTo(VirtualTextureVisualizationTarget.Auto));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationLayer,
                Is.EqualTo(VirtualTextureVisualizationLayer.BaseColor));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationWorldPageSize,
                Is.EqualTo(0.001f));
        }

        [Test]
        public void VirtualTextureVisualizationWidgets_ControlSettingsAndVisibility()
        {
            var modeWidget = DebugManager.instance.GetItem(
                "Rendering -> VividRP Debug -> Virtual Texture -> Visualization Mode") as DebugUI.EnumField;
            var targetWidget = DebugManager.instance.GetItem(
                "Rendering -> VividRP Debug -> Virtual Texture -> Visualization Target") as DebugUI.EnumField;
            var layerWidget = DebugManager.instance.GetItem(
                "Rendering -> VividRP Debug -> Virtual Texture -> Visualization Layer") as DebugUI.EnumField;
            var worldPageSizeWidget = DebugManager.instance.GetItem(
                "Rendering -> VividRP Debug -> Virtual Texture -> World Units Per Page") as DebugUI.FloatField;

            Assert.That(modeWidget, Is.Not.Null);
            Assert.That(targetWidget, Is.Not.Null);
            Assert.That(layerWidget, Is.Not.Null);
            Assert.That(worldPageSizeWidget, Is.Not.Null);
            Assert.That(targetWidget.isHiddenCallback(), Is.True);
            Assert.That(layerWidget.isHiddenCallback(), Is.True);
            Assert.That(worldPageSizeWidget.isHiddenCallback(), Is.True);

            int physicalAtlasIndex = Array.IndexOf(
                modeWidget.enumValues,
                (int)VirtualTextureVisualizationMode.PhysicalAtlas);
            modeWidget.setIndex(physicalAtlasIndex);
            targetWidget.setter((int)VirtualTextureVisualizationTarget.GPUDriven);
            layerWidget.setter((int)VirtualTextureVisualizationLayer.Mask);

            Assert.That(targetWidget.isHiddenCallback(), Is.False);
            Assert.That(layerWidget.isHiddenCallback(), Is.False);
            Assert.That(worldPageSizeWidget.isHiddenCallback(), Is.True);
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationTarget,
                Is.EqualTo(VirtualTextureVisualizationTarget.GPUDriven));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationLayer,
                Is.EqualTo(VirtualTextureVisualizationLayer.Mask));

            int resolvedWorldIndex = Array.IndexOf(
                modeWidget.enumValues,
                (int)VirtualTextureVisualizationMode.ResolvedWorldPosition);
            modeWidget.setIndex(resolvedWorldIndex);
            worldPageSizeWidget.setter(32f);

            Assert.That(layerWidget.isHiddenCallback(), Is.False);
            Assert.That(worldPageSizeWidget.isHiddenCallback(), Is.False);
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationWorldPageSize,
                Is.EqualTo(32f));
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
            VividRenderingDebugDisplaySettings.Data.visibilityBufferWireframeThickness = 4f;
            VividRenderingDebugDisplaySettings.Data.forceMeshletCullingFromMainCamera = true;

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugMode,
                Is.EqualTo(VisibilityBufferDebugVisualizationMode.Cluster));
            Assert.That(VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugExposure, Is.EqualTo(0f));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.visibilityBufferWireframeThickness,
                Is.EqualTo(VividRenderingDebugSettingsData.DefaultVisibilityBufferWireframeThickness));
            Assert.That(VividRenderingDebugDisplaySettings.Data.forceMeshletCullingFromMainCamera, Is.False);
        }

        [Test]
        public void VisibilityBufferWidgets_ExposeOneSharedModeAndWireframeVisibility()
        {
            var modeWidget = DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Visibility Buffer -> Mode")
                as DebugUI.EnumField;
            var exposureWidget = DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Visibility Buffer -> Exposure")
                as DebugUI.FloatField;
            var wireframeWidget = DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Visibility Buffer -> Wireframe Thickness")
                as DebugUI.FloatField;
            var observationStatusWidget = DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Visibility Buffer -> Occlusion Observation")
                as DebugUI.Value;

            Assert.That(modeWidget, Is.Not.Null);
            Assert.That(exposureWidget, Is.Not.Null);
            Assert.That(wireframeWidget, Is.Not.Null);
            Assert.That(observationStatusWidget, Is.Not.Null);
            Assert.That(wireframeWidget.isHiddenCallback(), Is.True);
            Assert.That(observationStatusWidget.isHiddenCallback(), Is.True);

            var clusterLODIndex = Array.IndexOf(
                modeWidget.enumValues,
                (int)VisibilityBufferDebugVisualizationMode.ClusterLOD);
            Assert.That(clusterLODIndex, Is.GreaterThanOrEqualTo(0));
            modeWidget.setIndex(clusterLODIndex);
            exposureWidget.setter(32f);

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugMode,
                Is.EqualTo(VisibilityBufferDebugVisualizationMode.ClusterLOD));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugExposure,
                Is.EqualTo(16f));
            Assert.That(wireframeWidget.isHiddenCallback(), Is.True);

            modeWidget.setter((int)VisibilityBufferDebugVisualizationMode.Wireframe);
            wireframeWidget.setter(0f);

            Assert.That(wireframeWidget.isHiddenCallback(), Is.False);
            VividRenderingDebugDisplaySettings.Data.forceMeshletCullingFromMainCamera = true;
            Assert.That(observationStatusWidget.isHiddenCallback(), Is.False);
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.visibilityBufferWireframeThickness,
                Is.EqualTo(0.1f));
            Assert.That(
                DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Visibility Buffer -> Resolve Mode"),
                Is.Null);
            Assert.That(
                DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Visibility Buffer -> Resolve Exposure"),
                Is.Null);
            VividRenderingDebugDisplaySettings.Data.forceMeshletCullingFromMainCamera = false;
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
        public void Reset_RestoresReferencedPathTracingDebugDefaults()
        {
            VividRenderingDebugDisplaySettings.Data
                .referencedPathTracingTransportDebugMode =
                    ReferencedPathTracingTransportDebugMode
                        .BsdfSegmentMisWeight;
            VividRenderingDebugDisplaySettings.Data
                .referencedPathTracingEnvironmentDebugMode =
                    ReferencedPathTracingEnvironmentDebugMode.EnvironmentOnly;

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(
                VividRenderingDebugDisplaySettings.Data
                    .referencedPathTracingTransportDebugMode,
                Is.EqualTo(
                    VividRenderingDebugSettingsData
                        .DefaultReferencedPathTracingTransportDebugMode));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data
                    .referencedPathTracingEnvironmentDebugMode,
                Is.EqualTo(
                    VividRenderingDebugSettingsData
                        .DefaultReferencedPathTracingEnvironmentDebugMode));
        }

        [Test]
        public void ReferencedPathTracingDebugWidgets_MapToDebuggerState()
        {
            var transportWidget = DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Reference Path Tracing -> Transport")
                as DebugUI.EnumField;
            var environmentWidget = DebugManager.instance.GetItem(
                    "Rendering -> VividRP Debug -> Reference Path Tracing -> Environment")
                as DebugUI.EnumField;

            Assert.That(transportWidget, Is.Not.Null);
            Assert.That(environmentWidget, Is.Not.Null);
            var transportIndex = Array.IndexOf(
                transportWidget.enumValues,
                (int)ReferencedPathTracingTransportDebugMode
                    .StochasticTransparency);
            var environmentIndex = Array.IndexOf(
                environmentWidget.enumValues,
                (int)ReferencedPathTracingEnvironmentDebugMode
                    .IndirectMissOnly);
            Assert.That(transportIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(environmentIndex, Is.GreaterThanOrEqualTo(0));

            transportWidget.setIndex(transportIndex);
            environmentWidget.setIndex(environmentIndex);

            Assert.That(
                VividRenderingDebugDisplaySettings.Data
                    .referencedPathTracingTransportDebugMode,
                Is.EqualTo(
                    ReferencedPathTracingTransportDebugMode
                        .StochasticTransparency));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data
                    .referencedPathTracingEnvironmentDebugMode,
                Is.EqualTo(
                    ReferencedPathTracingEnvironmentDebugMode
                        .IndirectMissOnly));
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
        public void AreAnySettingsActive_UsesVirtualTextureVisualizationModeAsTheEnableSwitch()
        {
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationTarget =
                VirtualTextureVisualizationTarget.FirstAvailable;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationLayer =
                VirtualTextureVisualizationLayer.Mask;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationMode =
                VirtualTextureVisualizationMode.PageTableResidency;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);
        }

        [Test]
        public void AreAnySettingsActive_TracksTerrainRuntimeVirtualTextureVisualization()
        {
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.terrainRuntimeVirtualTextureDebugMode =
                TerrainRuntimeVirtualTextureDebugMode.ClipmapLevel;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);
        }

        [Test]
        public void AreAnySettingsActive_TracksVirtualTextureFeedbackPressureOverrides()
        {
            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.False);

            VividRenderingDebugDisplaySettings.Data.virtualTextureFeedbackOverflowCountOverride = 0;

            Assert.That(VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive, Is.True);

            VividRenderingDebugDisplaySettings.Data.Reset();
            VividRenderingDebugDisplaySettings.Data.virtualTextureFallbackSampleCountOverride = 0;

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

            VividRenderingDebugDisplaySettings.Data.Reset();
            VividRenderingDebugDisplaySettings.Data.visibilityBufferWireframeThickness = 2f;

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
        public void AreAnySettingsActive_TracksReferencedPathTracingDebugModes()
        {
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive,
                Is.False);

            VividRenderingDebugDisplaySettings.Data
                .referencedPathTracingTransportDebugMode =
                    ReferencedPathTracingTransportDebugMode.NeeMisWeight;

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive,
                Is.True);

            VividRenderingDebugDisplaySettings.Data.Reset();
            VividRenderingDebugDisplaySettings.Data
                .referencedPathTracingEnvironmentDebugMode =
                    ReferencedPathTracingEnvironmentDebugMode.EnvironmentOnly;

            Assert.That(
                VividRenderingDebugDisplaySettings.Data.AreAnySettingsActive,
                Is.True);
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
