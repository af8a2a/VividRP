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
