using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class RenderGraphTemplateTests
    {
        private const string TempFolder = "Assets/VividRPRenderGraphTemplateTests";
        private const string TempGraphAssetPath = TempFolder + "/StandardRenderGraph.vrdg";

        [Test]
        public void StandardTemplate_CompilesDrawObjectPassWithEmbeddedRenderListDescriptor()
        {
            var content = RenderGraphEditorGraph.LoadStandardGraphTemplateContent();

            Assert.That(content, Is.Not.Null.And.Not.Empty);

            EnsureFolder(TempFolder);

            try
            {
                File.WriteAllText(TempGraphAssetPath, content);
                AssetDatabase.ImportAsset(
                    TempGraphAssetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                var graph = GraphDatabase.LoadGraph<RenderGraphEditorGraph>(TempGraphAssetPath);
                Assert.That(graph, Is.Not.Null);

                var result = RenderGraphCompiler.Compile(graph);
                var drawPasses = result.Passes
                    .Where(pass => pass.PassType.StartsWith(typeof(DrawObjectPass).FullName))
                    .ToArray();

                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(PreDepthPass).FullName)), Is.True);
                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(GBufferPass).FullName)), Is.True);
                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(MaterialClassificationPass).FullName)), Is.True);
                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(DeferredLightingPass).FullName)), Is.True);
                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(VisibilityBufferPass).FullName)), Is.False);
                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(VisibilityBufferGBufferResolvePass).FullName)), Is.False);
                Assert.That(drawPasses, Has.Length.EqualTo(1));
                Assert.That(drawPasses[0].RenderListDescParameters, Has.Count.EqualTo(1));
                Assert.That(drawPasses[0].RenderListDescParameters[0].FieldName, Is.EqualTo("m_RenderListDesc"));
                Assert.That(
                    drawPasses[0].ResourceBindings.Any(binding => binding.FieldName == "m_RenderList"),
                    Is.False);
                Assert.That(result.RenderListDescriptors, Is.Empty);
            }
            finally
            {
                AssetDatabase.DeleteAsset(TempGraphAssetPath);
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        [Test]
        public void StandardTemplate_AllGeneratedNodeTypesCanBeResolved()
        {
            var content = RenderGraphEditorGraph.LoadStandardGraphTemplateContent();
            var generatedNodeTypes = Regex.Matches(
                    content,
                    @"type: \{class: ([^,]+), ns: VividRP\.Editor\.RenderGraph\.Generated, asm: ([^}]+)\}")
                .Cast<Match>()
                .Select(match => $"VividRP.Editor.RenderGraph.Generated.{match.Groups[1].Value}, {match.Groups[2].Value}")
                .Distinct()
                .ToArray();

            Assert.That(generatedNodeTypes, Is.Not.Empty);
            foreach (var typeName in generatedNodeTypes)
                Assert.That(Type.GetType(typeName), Is.Not.Null, $"Template node type '{typeName}' cannot be resolved.");
        }

        [Test]
        public void StandardTemplateMenuPath_IsRegisteredUnderVividRpCreateMenu()
        {
            Assert.That(
                RenderGraphEditorGraph.StandardGraphTemplateMenuPath,
                Is.EqualTo("Assets/Create/VividRP/Standard Render Graph"));
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            var separator = assetPath.LastIndexOf('/');
            var parent = assetPath.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, assetPath.Substring(separator + 1));
        }
    }
}
