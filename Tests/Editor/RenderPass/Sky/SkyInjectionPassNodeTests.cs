using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class SkyInjectionPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredSkyInjectionPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(SkyInjectionPass);
        }

        [Test]
        public void SkyInjectionPassNode_DefinesDepthShadowSkyViewInputsAndColorOutputPorts()
        {
            var node = new AutoRegisteredSkyInjectionPassNode();

            Assert.That(node.GetInputPortByName("m_ColorTarget_In"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_ColorTarget_Out"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_DepthTexture_In"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_DepthTexture_Out"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_CSMShadowAtlas"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_DirectionalShadowTexture"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_SkyViewLUT"), Is.Not.Null);
        }
    }
}
