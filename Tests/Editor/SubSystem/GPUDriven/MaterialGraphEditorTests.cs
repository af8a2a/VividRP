using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using VividRP.Editor.GPUDriven;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.GPUDriven;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests.GPUDriven
{
    internal sealed class MaterialGraphEditorTests
    {
        private const string TestGraphFolder = "Assets/Temp/VividRPMaterialGraphTests";
        private const string ImporterScriptPath =
            "Packages/Custom_URP/Editor/GPUDriven/Material/MaterialGraphImporter.cs";

        [Test]
        public void StandardSingleSlabGraph_MatchesBuiltinCompiledProgram()
        {
            MaterialGraphEditorGraph graph = CreateGraph();
            try
            {
                BuildStandardSingleSlabGraph(graph);

                MaterialGraphCompilationResult result =
                    MaterialGraphEditorCompiler.Compile(graph);
                CompiledMaterialProgram expected =
                    MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                        GPUDrivenMaterialCompiler.ProgramVersion);

                Assert.That(result.Succeeded, Is.True, DiagnosticsToString(result));
                Assert.That(result.Program.SemanticHash, Is.EqualTo(expected.SemanticHash));
                Assert.That(result.Program.CompiledHash, Is.EqualTo(expected.CompiledHash));
                Assert.That(
                    result.Program.Module.CanonicalIR.Payload,
                    Is.EqualTo(expected.Module.CanonicalIR.Payload));
            }
            finally
            {
                DeleteGraph(graph);
            }
        }

        [TestCase(VividDualSlabOperator.HorizontalMix)]
        [TestCase(VividDualSlabOperator.VerticalLayer)]
        public void DualSlabGraph_MatchesBuiltinCompiledProgram(
            VividDualSlabOperator layerOperator)
        {
            MaterialGraphEditorGraph graph = CreateGraph();
            try
            {
                BuildDualSlabGraph(graph, layerOperator);

                MaterialGraphCompilationResult result =
                    MaterialGraphEditorCompiler.Compile(graph);
                CompiledMaterialProgram expected =
                    MaterialProgramPrototypeBuilder.BuildDualSlab(
                        GPUDrivenMaterialCompiler.ProgramVersion,
                        layerOperator);

                Assert.That(result.Succeeded, Is.True, DiagnosticsToString(result));
                Assert.That(result.Program.SemanticHash, Is.EqualTo(expected.SemanticHash));
                Assert.That(result.Program.CompiledHash, Is.EqualTo(expected.CompiledHash));
                Assert.That(
                    result.Program.Module.CanonicalIR.Payload,
                    Is.EqualTo(expected.Module.CanonicalIR.Payload));
            }
            finally
            {
                DeleteGraph(graph);
            }
        }

        [Test]
        public void DisconnectedOutput_ReportsGraphToolkitNodeIdAndPort()
        {
            MaterialGraphEditorGraph graph = CreateGraph();
            try
            {
                MaterialOutputNode output = AddNode<MaterialOutputNode>(graph);

                MaterialGraphCompilationResult result =
                    MaterialGraphEditorCompiler.Compile(graph);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.Diagnostics.Any(diagnostic =>
                        diagnostic.Code == MaterialGraphDiagnosticCodes.MissingNode
                        && diagnostic.SourceNodeId == output.ID.ToString()
                        && diagnostic.SourcePort == MaterialOutputNode.SurfacePortName),
                    Is.True,
                    DiagnosticsToString(result));
            }
            finally
            {
                DeleteGraph(graph);
            }
        }

        [Test]
        public void PreviewCostViewModel_UsesCompiledProgramAndFrozenCatalog()
        {
            MaterialGraphEditorGraph graph = CreateGraph();
            try
            {
                BuildStandardSingleSlabGraph(graph);

                MaterialGraphPreviewCostViewModel viewModel =
                    MaterialGraphPreviewCostViewModel.Build(graph);

                Assert.That(
                    viewModel.Status,
                    Is.EqualTo(MaterialGraphPreviewStatus.Ready));
                Assert.That(viewModel.CanPreview, Is.True);
                Assert.That(
                    viewModel.ProgramID,
                    Is.EqualTo(VividMaterialProgramID.StandardSingleSlab));
                Assert.That(viewModel.CompiledHash, Is.Not.Empty);
                Assert.That(viewModel.SemanticHash, Is.Not.Empty);
                Assert.That(viewModel.ClosureCount, Is.EqualTo(1));
                Assert.That(viewModel.OperatorCount, Is.Zero);
                Assert.That(viewModel.Metrics, Has.Count.EqualTo(10));
                Assert.That(viewModel.Metrics.Any(metric => metric.IsExceeded), Is.False);
                Assert.That(viewModel.Stages, Has.Count.EqualTo(2));
                Assert.That(viewModel.Diagnostics, Is.Empty);
            }
            finally
            {
                DeleteGraph(graph);
            }
        }

        [Test]
        public void PreviewCostViewModel_ReportsCompileErrorWithoutInventingCost()
        {
            MaterialGraphEditorGraph graph = CreateGraph();
            try
            {
                AddNode<MaterialOutputNode>(graph);

                MaterialGraphPreviewCostViewModel viewModel =
                    MaterialGraphPreviewCostViewModel.Build(graph);

                Assert.That(
                    viewModel.Status,
                    Is.EqualTo(MaterialGraphPreviewStatus.CompileError));
                Assert.That(viewModel.CanPreview, Is.False);
                Assert.That(
                    viewModel.ProgramID,
                    Is.EqualTo(VividMaterialProgramID.Invalid));
                Assert.That(viewModel.Metrics, Is.Empty);
                Assert.That(viewModel.Stages, Is.Empty);
                Assert.That(
                    viewModel.Diagnostics.Any(diagnostic =>
                        diagnostic.Contains(MaterialGraphDiagnosticCodes.MissingNode)),
                    Is.True);
            }
            finally
            {
                DeleteGraph(graph);
            }
        }

        [Test]
        public void MaterialGraphFactory_OnlyExposesMaterialNodes()
        {
            Type factoryType = Type.GetType(
                "Unity.GraphToolkit.Editor.Implementation.PublicGraphFactory, UnityEditor.GraphToolkitModule",
                throwOnError: true);
            MethodInfo getNodeTypesMethod = factoryType.GetMethod(
                "GetNodeTypes",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Type) },
                null);

            Assert.That(getNodeTypesMethod, Is.Not.Null);
            var materialNodeTypes = ((IEnumerable) getNodeTypesMethod.Invoke(
                    null,
                    new object[] { typeof(MaterialGraphEditorGraph) }))
                .Cast<Type>()
                .ToArray();
            var renderGraphNodeTypes = ((IEnumerable) getNodeTypesMethod.Invoke(
                    null,
                    new object[] { typeof(RenderGraphEditorGraph) }))
                .Cast<Type>()
                .ToArray();

            Assert.That(materialNodeTypes, Does.Contain(typeof(MaterialOutputNode)));
            Assert.That(materialNodeTypes, Does.Contain(typeof(MaterialStandardSlabNode)));
            Assert.That(materialNodeTypes.Contains(typeof(TextureResourceNodeData)), Is.False);
            Assert.That(renderGraphNodeTypes, Does.Contain(typeof(TextureResourceNodeData)));
            Assert.That(renderGraphNodeTypes.Contains(typeof(MaterialOutputNode)), Is.False);
        }

        [Test]
        public void Importer_StoresCompiledProgramIdentity()
        {
            MaterialGraphEditorGraph graph = CreateGraph();
            string assetPath = GraphDatabase.GetGraphAssetPath(graph);
            try
            {
                BuildStandardSingleSlabGraph(graph);
                GraphDatabase.SaveGraph(graph);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                var importer = AssetImporter.GetAtPath(assetPath) as MaterialGraphImporter;
                Assert.That(importer, Is.Not.Null);
                MonoScript importerScript =
                    AssetDatabase.LoadAssetAtPath<MonoScript>(ImporterScriptPath);
                Assert.That(importerScript, Is.Not.Null);
                Assert.That(importerScript.GetClass(), Is.EqualTo(typeof(MaterialGraphImporter)));
                Assert.That(
                    Path.GetFileNameWithoutExtension(
                        AssetDatabase.GetAssetPath(importerScript)),
                    Is.EqualTo(nameof(MaterialGraphImporter)));
                using var serializedImporter = new SerializedObject(importer);
                SerializedProperty importerScriptProperty =
                    serializedImporter.FindProperty("m_Script");
                Assert.That(importerScriptProperty, Is.Not.Null);
                Assert.That(importerScriptProperty.objectReferenceValue, Is.EqualTo(importerScript));

                MaterialGraphImportAsset asset =
                    AssetDatabase.LoadAssetAtPath<MaterialGraphImportAsset>(assetPath);
                CompiledMaterialProgram expected =
                    MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                        GPUDrivenMaterialCompiler.ProgramVersion);

                Assert.That(asset, Is.Not.Null);
                Assert.That(asset.Succeeded, Is.True);
                Assert.That(asset.ProgramVersion, Is.EqualTo(GPUDrivenMaterialCompiler.ProgramVersion));
                Assert.That(asset.SemanticHash, Is.EqualTo(expected.SemanticHash.ToString()));
                Assert.That(asset.CompiledHash, Is.EqualTo(expected.CompiledHash.ToString()));
                Assert.That(asset.IsCataloged, Is.True);
                Assert.That(
                    asset.ProgramID,
                    Is.EqualTo(VividMaterialProgramID.StandardSingleSlab));
                Assert.That(
                    asset.CatalogManifestHash,
                    Is.EqualTo(GPUDrivenMaterialCompiler.ProgramCatalog.ManifestHash));
                Assert.That(asset.CompiledProgramHash, Is.EqualTo(expected.CompiledHash));
                Assert.That(
                    asset.LayoutFingerprint,
                    Is.EqualTo(expected.Lowering.LayoutFingerprint));
                Assert.That(asset.ContentVersion, Is.Not.Zero);
                Assert.That(asset.Diagnostics, Is.Empty);

                var proxy =
                    ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                try
                {
                    proxy.MaterialGraph = asset;
                    GPUDrivenCompiledMaterialInstance compiled =
                        GPUDrivenMaterialCompiler.CompileStandardSingleSlab(
                            proxy,
                            parameterAddress: 2u,
                            surfaceBindingIndex: 3u);
                    Assert.That(
                        compiled.ProgramID,
                        Is.EqualTo(VividMaterialProgramID.StandardSingleSlab));
                }
                finally
                {
                    Object.DestroyImmediate(proxy);
                }
            }
            finally
            {
                DeleteGraph(graph);
            }
        }

        private static void BuildStandardSingleSlabGraph(
            MaterialGraphEditorGraph graph)
        {
            MaterialExternalInputNode uv = AddNode<MaterialExternalInputNode>(graph);
            MaterialTextureResourceNode texture = AddNode<MaterialTextureResourceNode>(graph);
            MaterialTextureSampleNode sample = AddNode<MaterialTextureSampleNode>(graph);
            MaterialParameterNode color = AddParameter(
                graph,
                MaterialParameter.BaseColor);
            MaterialBinaryNode baseColor = AddNode<MaterialBinaryNode>(graph);
            MaterialSwizzleNode coverage = AddNode<MaterialSwizzleNode>(graph);
            SetOption(
                coverage,
                MaterialSwizzleNode.SwizzleOptionName,
                MaterialGraphSwizzle.W);
            MaterialParameterNode roughness = AddParameter(
                graph,
                MaterialParameter.Roughness);
            MaterialParameterNode metallic = AddParameter(
                graph,
                MaterialParameter.Metallic);
            MaterialParameterNode threshold = AddParameter(
                graph,
                MaterialParameter.AlphaClipThreshold);
            MaterialParameterNode emission = AddParameter(
                graph,
                MaterialParameter.Emission);
            MaterialExternalInputNode normal = AddExternalInput(
                graph,
                MaterialExternalInput.GeometryNormalWS);
            MaterialExternalInputNode tangent = AddExternalInput(
                graph,
                MaterialExternalInput.GeometryTangentWS);
            MaterialStandardSlabNode slab = AddNode<MaterialStandardSlabNode>(graph);
            MaterialOutputNode output = AddNode<MaterialOutputNode>(graph);

            ConnectValue(graph, texture, sample, MaterialTextureSampleNode.TexturePortName);
            ConnectValue(graph, uv, sample, MaterialTextureSampleNode.UVPortName);
            ConnectValue(graph, sample, baseColor, MaterialBinaryNode.LeftPortName);
            ConnectValue(graph, color, baseColor, MaterialBinaryNode.RightPortName);
            ConnectValue(graph, baseColor, coverage, MaterialSwizzleNode.InputPortName);
            ConnectValue(graph, baseColor, slab, MaterialStandardSlabNode.BaseColorPortName);
            ConnectValue(graph, roughness, slab, MaterialStandardSlabNode.RoughnessPortName);
            ConnectValue(graph, metallic, slab, MaterialStandardSlabNode.MetallicPortName);
            ConnectValue(graph, normal, slab, MaterialStandardSlabNode.NormalPortName);
            ConnectValue(graph, tangent, slab, MaterialStandardSlabNode.TangentPortName);
            ConnectClosure(graph, slab, output, MaterialOutputNode.SurfacePortName);
            ConnectValue(graph, coverage, output, MaterialOutputNode.CoveragePortName);
            ConnectValue(
                graph,
                threshold,
                output,
                MaterialOutputNode.AlphaClipThresholdPortName);
            ConnectValue(graph, emission, output, MaterialOutputNode.EmissionPortName);
        }

        private static void BuildDualSlabGraph(
            MaterialGraphEditorGraph graph,
            VividDualSlabOperator layerOperator)
        {
            MaterialExternalInputNode uv = AddNode<MaterialExternalInputNode>(graph);
            MaterialBinaryNode baseColor = AddSampledBaseColor(
                graph,
                uv,
                MaterialTextureResource.BaseColor,
                MaterialParameter.BaseColor);
            MaterialBinaryNode topBaseColor = AddSampledBaseColor(
                graph,
                uv,
                MaterialTextureResource.TopBaseColor,
                MaterialParameter.TopBaseColor);
            MaterialSwizzleNode coverage = AddNode<MaterialSwizzleNode>(graph);
            SetOption(
                coverage,
                MaterialSwizzleNode.SwizzleOptionName,
                MaterialGraphSwizzle.W);
            ConnectValue(graph, baseColor, coverage, MaterialSwizzleNode.InputPortName);

            MaterialParameterNode roughness = AddParameter(
                graph,
                MaterialParameter.Roughness);
            MaterialParameterNode topRoughness = AddParameter(
                graph,
                MaterialParameter.TopRoughness);
            MaterialParameterNode metallic = AddParameter(
                graph,
                MaterialParameter.Metallic);
            MaterialParameterNode topMetallic = AddParameter(
                graph,
                MaterialParameter.TopMetallic);
            MaterialParameterNode weight = AddParameter(
                graph,
                MaterialParameter.LayerWeight);
            MaterialParameterNode threshold = AddParameter(
                graph,
                MaterialParameter.AlphaClipThreshold);
            MaterialParameterNode emission = AddParameter(
                graph,
                MaterialParameter.Emission);
            MaterialExternalInputNode normal = AddExternalInput(
                graph,
                MaterialExternalInput.GeometryNormalWS);
            MaterialExternalInputNode tangent = AddExternalInput(
                graph,
                MaterialExternalInput.GeometryTangentWS);
            MaterialStandardSlabNode baseSlab = AddNode<MaterialStandardSlabNode>(graph);
            MaterialStandardSlabNode topSlab = AddNode<MaterialStandardSlabNode>(graph);
            ConnectSlab(
                graph,
                baseSlab,
                baseColor,
                roughness,
                metallic,
                normal,
                tangent);
            ConnectSlab(
                graph,
                topSlab,
                topBaseColor,
                topRoughness,
                topMetallic,
                normal,
                tangent);

            Node layer;
            if (layerOperator == VividDualSlabOperator.HorizontalMix)
            {
                var horizontal = AddNode<MaterialHorizontalMixNode>(graph);
                ConnectClosure(
                    graph,
                    baseSlab,
                    horizontal,
                    MaterialHorizontalMixNode.BackgroundPortName);
                ConnectClosure(
                    graph,
                    topSlab,
                    horizontal,
                    MaterialHorizontalMixNode.ForegroundPortName);
                ConnectValue(
                    graph,
                    weight,
                    horizontal,
                    MaterialHorizontalMixNode.WeightPortName);
                layer = horizontal;
            }
            else
            {
                var vertical = AddNode<MaterialVerticalLayerNode>(graph);
                ConnectClosure(
                    graph,
                    baseSlab,
                    vertical,
                    MaterialVerticalLayerNode.BottomPortName);
                ConnectClosure(
                    graph,
                    topSlab,
                    vertical,
                    MaterialVerticalLayerNode.TopPortName);
                ConnectValue(
                    graph,
                    weight,
                    vertical,
                    MaterialVerticalLayerNode.WeightPortName);
                layer = vertical;
            }

            MaterialOutputNode output = AddNode<MaterialOutputNode>(graph);
            ConnectClosure(graph, layer, output, MaterialOutputNode.SurfacePortName);
            ConnectValue(graph, coverage, output, MaterialOutputNode.CoveragePortName);
            ConnectValue(
                graph,
                threshold,
                output,
                MaterialOutputNode.AlphaClipThresholdPortName);
            ConnectValue(graph, emission, output, MaterialOutputNode.EmissionPortName);
        }

        private static MaterialBinaryNode AddSampledBaseColor(
            Graph graph,
            MaterialExternalInputNode uv,
            MaterialTextureResource resource,
            MaterialParameter color)
        {
            MaterialTextureResourceNode texture =
                AddNode<MaterialTextureResourceNode>(graph);
            SetOption(
                texture,
                MaterialTextureResourceNode.ResourceOptionName,
                resource);
            MaterialTextureSampleNode sample = AddNode<MaterialTextureSampleNode>(graph);
            MaterialParameterNode colorParameter = AddParameter(graph, color);
            MaterialBinaryNode baseColor = AddNode<MaterialBinaryNode>(graph);
            ConnectValue(graph, texture, sample, MaterialTextureSampleNode.TexturePortName);
            ConnectValue(graph, uv, sample, MaterialTextureSampleNode.UVPortName);
            ConnectValue(graph, sample, baseColor, MaterialBinaryNode.LeftPortName);
            ConnectValue(graph, colorParameter, baseColor, MaterialBinaryNode.RightPortName);
            return baseColor;
        }

        private static void ConnectSlab(
            Graph graph,
            MaterialStandardSlabNode slab,
            Node baseColor,
            Node roughness,
            Node metallic,
            Node normal,
            Node tangent)
        {
            ConnectValue(graph, baseColor, slab, MaterialStandardSlabNode.BaseColorPortName);
            ConnectValue(graph, roughness, slab, MaterialStandardSlabNode.RoughnessPortName);
            ConnectValue(graph, metallic, slab, MaterialStandardSlabNode.MetallicPortName);
            ConnectValue(graph, normal, slab, MaterialStandardSlabNode.NormalPortName);
            ConnectValue(graph, tangent, slab, MaterialStandardSlabNode.TangentPortName);
        }

        private static MaterialParameterNode AddParameter(
            Graph graph,
            MaterialParameter parameter)
        {
            MaterialParameterNode node = AddNode<MaterialParameterNode>(graph);
            SetOption(node, MaterialParameterNode.ParameterOptionName, parameter);
            return node;
        }

        private static MaterialExternalInputNode AddExternalInput(
            Graph graph,
            MaterialExternalInput input)
        {
            MaterialExternalInputNode node = AddNode<MaterialExternalInputNode>(graph);
            SetOption(node, MaterialExternalInputNode.InputOptionName, input);
            return node;
        }

        private static T AddNode<T>(Graph graph) where T : Node, new()
        {
            var node = new T();
            graph.AddNode(node);
            return node;
        }

        private static void SetOption<T>(Node node, string optionName, T value)
        {
            INodeOption option = node.GetNodeOptionByName(optionName);
            Assert.That(option, Is.Not.Null, $"Missing option '{optionName}'.");
            Assert.That(option.TrySetValue(value), Is.True);
        }

        private static void ConnectValue(
            Graph graph,
            Node source,
            Node target,
            string inputPortName)
        {
            Assert.That(
                graph.Connect(
                    source.GetOutputPortByName(MaterialGraphEditorNode.ValueOutputPortName),
                    target.GetInputPortByName(inputPortName)),
                Is.True);
        }

        private static void ConnectClosure(
            Graph graph,
            Node source,
            Node target,
            string inputPortName)
        {
            Assert.That(
                graph.Connect(
                    source.GetOutputPortByName(MaterialGraphEditorNode.ClosureOutputPortName),
                    target.GetInputPortByName(inputPortName)),
                Is.True);
        }

        private static MaterialGraphEditorGraph CreateGraph()
        {
            EnsureFolderExists("Assets/Temp");
            EnsureFolderExists(TestGraphFolder);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{TestGraphFolder}/MaterialGraphTest_{Guid.NewGuid():N}." +
                MaterialGraphEditorGraph.AssetExtension);
            return GraphDatabase.CreateGraph<MaterialGraphEditorGraph>(assetPath);
        }

        private static void DeleteGraph(Graph graph)
        {
            if (graph == null)
                return;
            string assetPath = GraphDatabase.GetGraphAssetPath(graph);
            if (!string.IsNullOrEmpty(assetPath))
                AssetDatabase.DeleteAsset(assetPath);
        }

        private static void EnsureFolderExists(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;
            int separatorIndex = assetPath.LastIndexOf('/');
            string parent = assetPath.Substring(0, separatorIndex);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolderExists(parent);
            AssetDatabase.CreateFolder(
                parent,
                assetPath.Substring(separatorIndex + 1));
        }

        private static string DiagnosticsToString(
            MaterialGraphCompilationResult result)
        {
            return string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Code} {diagnostic.SourceNodeId}." +
                    $"{diagnostic.SourcePort}: {diagnostic.Message}"));
        }
    }
}
