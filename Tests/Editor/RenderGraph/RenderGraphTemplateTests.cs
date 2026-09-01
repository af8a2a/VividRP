using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
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
                Assert.That(
                    RenderGraphDrawObjectPassMigration.Migrate(
                        graph,
                        TempGraphAssetPath),
                    Is.False);

                var result = RenderGraphCompiler.Compile(graph);
                var drawPasses = result.Passes
                    .Where(pass => pass.PassType.StartsWith(typeof(DrawObjectPass).FullName))
                    .ToArray();

                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(PreDepthPass).FullName)), Is.True);
                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(GBufferPass).FullName)), Is.False);
                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(MaterialClassificationPass).FullName)), Is.True);
                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(DeferredLightingPass).FullName)), Is.True);
                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(VisibilityBufferPass).FullName)), Is.True);
                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(VisibilityBufferGBufferResolvePass).FullName)), Is.True);
                Assert.That(result.Passes.Any(pass => pass.PassType.StartsWith(typeof(VisibilityBufferResolvePass).FullName)), Is.False);
                Assert.That(drawPasses, Has.Length.EqualTo(1));
                Assert.That(drawPasses[0].RenderListDescParameters, Has.Count.EqualTo(1));
                Assert.That(drawPasses[0].RenderListDescParameters[0].FieldName, Is.EqualTo("m_RenderListDesc"));
                Assert.That(
                    drawPasses[0].ResourceBindings.Any(binding => binding.FieldName == "m_RenderList"),
                    Is.False);
                Assert.That(result.RenderListDescriptors, Is.Empty);

                var preDepthIndex = FindPassIndex<PreDepthPass>(result);
                var visibilityIndex = FindPassIndex<VisibilityBufferPass>(result);
                var resolveIndex = FindPassIndex<VisibilityBufferGBufferResolvePass>(result);
                var classificationIndex = FindPassIndex<MaterialClassificationPass>(result);
                var deferredIndex = FindPassIndex<DeferredLightingPass>(result);
                var csmShadowIndex = FindPassIndex<CSMShadowPass>(result);
                var csmResolveIndex = FindPassIndex<CSMShadowResolvePass>(result);
                Assert.That(preDepthIndex, Is.LessThan(visibilityIndex));
                Assert.That(visibilityIndex, Is.LessThan(resolveIndex));
                Assert.That(resolveIndex, Is.LessThan(classificationIndex));
                Assert.That(classificationIndex, Is.LessThan(deferredIndex));

                AssertPassFieldBinding(
                    result,
                    csmResolveIndex,
                    "m_CSMShadowAtlas",
                    csmShadowIndex,
                    "m_ShadowAtlas");

                AssertPassFieldBinding(
                    result,
                    classificationIndex,
                    "m_GBuffer0",
                    resolveIndex,
                    "m_GBuffer0");
                AssertPassFieldBinding(
                    result,
                    classificationIndex,
                    "m_GBuffer1",
                    resolveIndex,
                    "m_GBuffer1");
                AssertPassFieldBinding(
                    result,
                    deferredIndex,
                    "m_MaterialTileFeatureFlags",
                    classificationIndex,
                    "m_MaterialTileFeatureFlags");
                AssertPassFieldBinding(
                    result,
                    deferredIndex,
                    "m_MaterialFeatureTileList",
                    classificationIndex,
                    "m_MaterialFeatureTileList");
                AssertPassFieldBinding(
                    result,
                    deferredIndex,
                    "m_MaterialFeatureIndirectArgs",
                    classificationIndex,
                    "m_MaterialFeatureIndirectArgs");
                AssertPassFieldBinding(
                    result,
                    deferredIndex,
                    "m_GBuffer4",
                    resolveIndex,
                    "m_GBuffer4");
                AssertPassFieldBinding(
                    result,
                    deferredIndex,
                    "m_LayerAux0",
                    resolveIndex,
                    "m_LayerAux0");
                AssertPassFieldBinding(
                    result,
                    deferredIndex,
                    "m_LayerAux1",
                    resolveIndex,
                    "m_LayerAux1");

            }
            finally
            {
                AssetDatabase.DeleteAsset(TempGraphAssetPath);
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        [Test]
        public void StandardTemplate_PersistsDualSlabSidecarAndCurrentSchema()
        {
            var content = RenderGraphEditorGraph.LoadStandardGraphTemplateContent();

            Assert.That(
                Regex.Matches(content, @"m_SchemaVersion: 4").Count,
                Is.EqualTo(3));
            StringAssert.Contains("m_Name: LayerAux0 (R)", content);
            StringAssert.Contains("m_Name: LayerAux1 (R)", content);
            StringAssert.Contains("m_UniqueId: m_LayerAux0_Out", content);
            StringAssert.Contains("m_UniqueId: m_LayerAux1_Out", content);
            StringAssert.Contains("m_UniqueId: m_LayerAux0", content);
            StringAssert.Contains("m_UniqueId: m_LayerAux1", content);
            StringAssert.Contains("__option_Override_m_LayerAux0", content);
            StringAssert.Contains("__option_Override_m_LayerAux1", content);
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
        public void StandardTemplate_GBufferPassDoesNotContainVirtualTextureInputs()
        {
            var content = RenderGraphEditorGraph.LoadStandardGraphTemplateContent();

            Assert.That(content, Does.Not.Contain("m_VirtualTextureRenderList"));
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

        private static int FindPassIndex<TPass>(RenderGraphCompilationResult result)
        {
            var passType = typeof(TPass);
            return result.Passes.FindIndex(pass => pass.PassType.StartsWith(passType.FullName));
        }

        private static void AssertPassFieldBinding(
            RenderGraphCompilationResult result,
            int consumerPassIndex,
            string consumerFieldName,
            int sourcePassIndex,
            string sourceFieldName)
        {
            Assert.That(consumerPassIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(sourcePassIndex, Is.GreaterThanOrEqualTo(0));
            var binding = result.Passes[consumerPassIndex].ResourceBindings.Single(
                candidate => candidate.FieldName == consumerFieldName);
            Assert.That(binding.SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.PassField));
            Assert.That(binding.SourcePassIndex, Is.EqualTo(sourcePassIndex));
            Assert.That(binding.SourceFieldName, Is.EqualTo(sourceFieldName));
        }
    }
}
