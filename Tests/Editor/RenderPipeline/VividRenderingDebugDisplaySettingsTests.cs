using NUnit.Framework;
using UnityEngine.Rendering;
using VividRP.Runtime;

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
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Exposure"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Overlay"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Slider"), Is.Not.Null);
            Assert.That(DebugManager.instance.GetItem("Rendering -> VividRP Debug -> Virtual Texture"), Is.Not.Null);
        }

        [Test]
        public void Reset_RestoresVirtualTextureDebugDefaults()
        {
            VividRenderingDebugDisplaySettings.Data.virtualTextureDebugMode = VirtualTextureDebugMode.PhysicalPageId;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationMode = VirtualTextureVisualizationMode.PageTableResidency;

            VividRenderingDebugDisplaySettings.Data.Reset();

            Assert.That(VividRenderingDebugDisplaySettings.Data.virtualTextureDebugMode, Is.EqualTo(VirtualTextureDebugMode.None));
            Assert.That(
                VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationMode,
                Is.EqualTo(VirtualTextureVisualizationMode.UsePassSettings));
        }
    }
}
