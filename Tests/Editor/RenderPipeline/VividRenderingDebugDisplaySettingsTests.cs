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
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> ReGIR"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> ReGIR -> Mode"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> ReGIR -> Opacity"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Exposure"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Material"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Material -> Mode"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Material -> Exposure"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Visibility Buffer"), Is.Not.Null);
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
