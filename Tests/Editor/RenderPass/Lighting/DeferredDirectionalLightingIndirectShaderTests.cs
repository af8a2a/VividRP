using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class DeferredDirectionalLightingIndirectShaderTests
    {

        [Test]
        public void GeneratedNodeRegistry_ContainsDeferredLightingPassNode()
        {
            var deferredNodeType = RenderPassNodeRegistry.GetNodeType(typeof(DeferredLightingPass));
            var directionalNodeType = RenderPassNodeRegistry.GetNodeType(typeof(DeferredDirectionalLightingPass));

            Assert.That(deferredNodeType, Is.Not.Null);
            Assert.That(deferredNodeType.Name, Is.EqualTo(nameof(DeferredLightingPass)));
            Assert.That(directionalNodeType, Is.Not.Null);
            Assert.That(directionalNodeType.Name, Is.EqualTo(nameof(DeferredDirectionalLightingPass)));
            Assert.That(
                TypeCache.GetTypesDerivedFrom<RenderPassNodeData>()
                    .Any(nodeType => nodeType.Name.Contains("PreIntegratedFGD")),
                Is.False);
        }
    }
}
