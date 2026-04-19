using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class RenderGraphNodeMenuVisibilityTests
    {
        [Test]
        public void RenderGraphEditor_DoesNotExposeTestAssemblyNodeTypes_InGraphToolkitFactory()
        {
            var factoryType = Type.GetType(
                "Unity.GraphToolkit.Editor.Implementation.PublicGraphFactory, UnityEditor.GraphToolkitModule",
                throwOnError: true);
            var getNodeTypesMethod = factoryType.GetMethod(
                "GetNodeTypes",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Type) },
                null);

            Assert.That(getNodeTypesMethod, Is.Not.Null);

            var nodeTypes = getNodeTypesMethod.Invoke(null, new object[] { typeof(RenderGraphEditorGraph) }) as System.Collections.IEnumerable;
            Assert.That(nodeTypes, Is.Not.Null);

            var offendingTypes = nodeTypes
                .Cast<Type>()
                .Where(type => type != null && type.Assembly == typeof(RenderGraphNodeMenuVisibilityTests).Assembly)
                .Select(type => type.FullName)
                .OrderBy(name => name, System.StringComparer.Ordinal)
                .ToArray();

            Assert.That(offendingTypes, Is.Empty);
        }
    }
}
