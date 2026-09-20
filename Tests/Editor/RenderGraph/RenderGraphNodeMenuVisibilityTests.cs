using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class RenderGraphNodeMenuVisibilityTests
    {
        [TestCase(typeof(RenderGraphEditorGraph))]
        [TestCase(typeof(RenderGraphSubSystemGraph))]
        public void RenderGraph_DoesNotExposeTestAssemblyNodeTypes_InGraphToolkitFactory(Type graphType)
        {
            var offendingTypes = GetNodeTypes(graphType)
                .Where(type => type != null && type.Assembly == typeof(RenderGraphNodeMenuVisibilityTests).Assembly)
                .Select(type => type.FullName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(offendingTypes, Is.Empty);
        }

        [TestCase(typeof(RenderGraphEditorGraph))]
        [TestCase(typeof(RenderGraphSubSystemGraph))]
        public void RenderGraph_ExposesEachNodeExactlyOnce_InGraphToolkitFactory(Type graphType)
        {
            var nodeTypes = GetNodeTypes(graphType);
            var expectedTypes = typeof(RenderGraphNodeData).Assembly.GetTypes()
                .Where(type => typeof(RenderGraphNodeData).IsAssignableFrom(type) && !type.IsAbstract)
                .ToArray();

            Assert.That(expectedTypes, Is.Not.Empty);
            Assert.That(nodeTypes, Is.Unique);
            Assert.That(nodeTypes, Is.SupersetOf(expectedTypes));
        }

        private static Type[] GetNodeTypes(Type graphType)
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

            var nodeTypes = getNodeTypesMethod.Invoke(null, new object[] { graphType }) as System.Collections.IEnumerable;
            Assert.That(nodeTypes, Is.Not.Null);

            return nodeTypes.Cast<Type>().ToArray();
        }
    }
}
