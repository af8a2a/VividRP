using System;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class TileDebugPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredTileDebugPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(TileDebugPass).AssemblyQualifiedName;
        }

        [Test]
        public void TileDebugPassNode_DefinesSourceTileAndIndirectInputs_AlongsideColorOutput()
        {
            var node = new AutoRegisteredTileDebugPassNode();

            Assert.That(node.GetInputPortByName("m_SourceTexture"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_TileIndices"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_IndirectArgs"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
        }
    }
}
