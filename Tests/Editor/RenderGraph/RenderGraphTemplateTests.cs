using NUnit.Framework;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class RenderGraphTemplateTests
    {
        [Test]
        public void StandardTemplate_LoadsHybridBasedGraphContent()
        {
            var content = RenderGraphEditorGraph.LoadStandardGraphTemplateContent();

            Assert.That(content, Is.Not.Null.And.Not.Empty);
            Assert.That(content, Does.StartWith("%YAML 1.1"));
            Assert.That(content, Does.Contain("m_Name: Standard Vivid Render Graph"));
            Assert.That(content, Does.Contain("type: {class: RenderGraphEditorGraph"));
            Assert.That(content, Does.Contain("type: {class: PreDepthPass"));
            Assert.That(content, Does.Contain("type: {class: GBufferPass"));
            Assert.That(content, Does.Contain("type: {class: DeferredLightingPass"));
            Assert.That(content, Does.Contain("m_Title: MaterialTileFeatureFlags (R)"));
            Assert.That(content, Does.Contain("type: {class: ClusterDebugPass"));
            Assert.That(content, Does.Contain("- rid: 8515053000000000005"));
            Assert.That(content, Does.Contain("Hash: 0123456789abcdef0123456789abcdeb"));
            Assert.That(content, Does.Contain("- rid: 8515053000000000007"));
            Assert.That(content, Does.Contain("Hash: 0123456789abcdef0123456789abcdec"));
            Assert.That(content, Does.Contain("- rid: 8515053000000000010"));
            Assert.That(content, Does.Contain("Hash: 0123456789abcdef0123456789abcded"));
            Assert.That(content, Does.Contain("m_Title: MaterialFeatureTileList (R)"));
            Assert.That(content, Does.Contain("m_Title: MaterialFeatureIndirectArgs (R)"));
            Assert.That(content, Does.Contain("type: {class: AntialiasingPass"));
            Assert.That(content, Does.Contain("type: {class: FinalBlitPass"));
        }

        [Test]
        public void StandardTemplateMenuPath_IsRegisteredUnderVividRpCreateMenu()
        {
            Assert.That(
                RenderGraphEditorGraph.StandardGraphTemplateMenuPath,
                Is.EqualTo("Assets/Create/VividRP/Standard Render Graph"));
        }
    }
}
