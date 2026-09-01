using System;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests.GPUDriven
{
    internal sealed class MaterialGraphCompilerTests
    {
        [Test]
        public void StandardSingleSlab_MatchesBuiltinCompiledProgram()
        {
            CompiledMaterialProgram expected =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);

            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(
                BuildStandardSingleSlabGraph(),
                GPUDrivenMaterialCompiler.ProgramVersion);

            AssertProgramParity(result, expected);
            Assert.That(
                result.Module.ClosureGraph.GetNode(result.Module.SurfaceClosure)
                    .Slab.Features,
                Is.EqualTo(
                    ClosureFeatureMask.BaseColorTexture
                    | ClosureFeatureMask.NormalTexture
                    | ClosureFeatureMask.MaskTexture));
            Assert.That(
                result.Module.MaterialFeatures,
                Is.EqualTo(MaterialFeatureMask.AlphaClip));
            Assert.That(
                result.Module.ShadingModels,
                Is.EqualTo(
                    MaterialShadingModelMask.StandardLit
                    | MaterialShadingModelMask.Unlit));
        }

        [TestCase(VividDualSlabOperator.HorizontalMix)]
        [TestCase(VividDualSlabOperator.VerticalLayer)]
        public void DualSlab_MatchesBuiltinCompiledProgram(
            VividDualSlabOperator layerOperator)
        {
            CompiledMaterialProgram expected =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    layerOperator);

            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(
                BuildDualSlabGraph(layerOperator),
                GPUDrivenMaterialCompiler.ProgramVersion);

            AssertProgramParity(result, expected);
        }

        [Test]
        public void GenericSingleSlab_MatchesContentAddressedProofProgram()
        {
            CompiledMaterialProgram expected =
                MaterialProgramPrototypeBuilder.BuildGenericSingleSlabProof(
                    GPUDrivenMaterialCompiler.ProgramVersion);

            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(
                BuildGenericSingleSlabGraph(),
                GPUDrivenMaterialCompiler.ProgramVersion);

            AssertProgramParity(result, expected);
        }

        [Test]
        public void NamedDeclarations_ReachIRWithoutEnumSemanticMapping()
        {
            var graph = new MaterialGraph();
            SingleSlabSource source = AddSingleSlabSource(graph);
            MaterialGraphValue customTint = graph.Parameter(
                "CustomTint",
                "CustomTint",
                MaterialValueType.Float4);
            MaterialGraphValue customTexture = graph.TextureResource(
                "CustomTexture",
                "CustomAlbedo",
                MaterialValueType.Texture2D);
            MaterialGraphValue customUv = graph.ExternalInput(
                "CustomUV",
                MaterialExternalInput.UV0);
            MaterialGraphValue customSample = graph.TextureSample(
                "CustomSample",
                customTexture,
                customUv);
            MaterialGraphValue tintedBaseColor = graph.Multiply(
                "TintedBaseColor",
                source.BaseColor,
                customTint);
            MaterialGraphValue customBaseColor = graph.Multiply(
                "CustomBaseColor",
                tintedBaseColor,
                customSample);
            MaterialGraphClosure slab = graph.Slab(
                "Slab",
                customBaseColor,
                source.Roughness,
                source.Metallic,
                source.Normal,
                source.Tangent,
                ClosureFeatureMask.BaseColorTexture);
            graph.Output(
                "Output",
                slab,
                source.Coverage,
                source.AlphaClipThreshold,
                source.Emission,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit);

            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(
                graph,
                GPUDrivenMaterialCompiler.ProgramVersion);

            Assert.That(result.Succeeded, Is.True, DiagnosticsToString(result));
            Assert.That(
                result.Module.ClosureGraph.GetNode(result.Module.SurfaceClosure)
                    .Slab.Features,
                Is.EqualTo(ClosureFeatureMask.BaseColorTexture));
            Assert.That(
                result.Module.MaterialFeatures,
                Is.EqualTo(MaterialFeatureMask.AlphaClip));
            Assert.That(
                result.Module.ShadingModels,
                Is.EqualTo(MaterialShadingModelMask.StandardLit));
            Assert.That(
                result.Program.Module.ClosureGraph.GetNode(
                    result.Program.Module.SurfaceClosure).Slab.Features,
                Is.EqualTo(ClosureFeatureMask.BaseColorTexture));
            Assert.That(
                result.Program.Module.MaterialFeatures,
                Is.EqualTo(MaterialFeatureMask.AlphaClip));
            Assert.That(
                result.Program.Module.ShadingModels,
                Is.EqualTo(MaterialShadingModelMask.StandardLit));
            Assert.That(
                result.Module.Values.ParameterDeclarations,
                Does.Contain(new MaterialParameterDeclaration(
                    "CustomTint",
                    MaterialValueType.Float4)));
            Assert.That(
                result.Module.Values.ResourceDeclarations,
                Does.Contain(new MaterialResourceDeclaration(
                    "CustomAlbedo",
                    MaterialValueType.Texture2D)));
            Assert.That(
                result.Module.Values.ResourceDeclarations.Count(declaration =>
                    declaration.Type == MaterialValueType.Texture2D),
                Is.EqualTo(2));
        }

        [Test]
        public void Provenance_PreservesAllAuthorsMergedByCanonicalCse()
        {
            var graph = new MaterialGraph();
            SingleSlabSource source = AddSingleSlabSource(graph);
            MaterialGraphValue firstHalf = graph.Constant("HalfA", 0.5f);
            MaterialGraphValue secondHalf = graph.Constant("HalfB", 0.5f);
            MaterialGraphValue roughness = graph.Multiply(
                "ScaledRoughness",
                source.Roughness,
                firstHalf);
            MaterialGraphValue metallic = graph.Multiply(
                "ScaledMetallic",
                source.Metallic,
                secondHalf);
            AddSingleSlabOutput(graph, source, roughness, metallic, source.Coverage);

            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(
                graph,
                GPUDrivenMaterialCompiler.ProgramVersion);

            Assert.That(result.Succeeded, Is.True, DiagnosticsToString(result));
            Assert.That(
                result.Provenance.TryGetCanonicalValueNodes(
                    "HalfA",
                    out var firstNodes),
                Is.True);
            Assert.That(
                result.Provenance.TryGetCanonicalValueNodes(
                    "HalfB",
                    out var secondNodes),
                Is.True);
            Assert.That(firstNodes, Is.EqualTo(secondNodes));
        }

        [Test]
        public void MissingReference_ReportsConsumerNodeAndPort()
        {
            var graph = new MaterialGraph();
            SingleSlabSource source = AddSingleSlabSource(graph);
            MaterialGraphClosure slab = graph.Slab(
                "Slab",
                source.BaseColor,
                source.Roughness,
                source.Metallic,
                source.Normal,
                source.Tangent);
            graph.Output(
                "Output",
                slab,
                graph.Value("MissingCoverage"),
                source.AlphaClipThreshold,
                source.Emission);

            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(
                graph,
                GPUDrivenMaterialCompiler.ProgramVersion);

            Assert.That(result.Succeeded, Is.False);
            AssertDiagnostic(
                result,
                MaterialGraphDiagnosticCodes.MissingNode,
                "Output",
                "Coverage");
        }

        [Test]
        public void ValueCycle_ReportsStableAuthoringLocation()
        {
            var graph = new MaterialGraph();
            SingleSlabSource source = AddSingleSlabSource(graph);
            MaterialGraphValue one = graph.Constant("One", 1.0f);
            MaterialGraphValue cycleA = graph.Value("CycleA");
            MaterialGraphValue cycleB = graph.Value("CycleB");
            graph.Add("CycleA", cycleB, one);
            graph.Multiply("CycleB", cycleA, one);
            AddSingleSlabOutput(
                graph,
                source,
                cycleA,
                source.Metallic,
                source.Coverage);

            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(
                graph,
                GPUDrivenMaterialCompiler.ProgramVersion);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(
                result.Diagnostics.Any(entry =>
                    entry.Code == MaterialGraphDiagnosticCodes.Cycle
                    && entry.SourceNodeId == "CycleB"
                    && entry.SourcePort == "A"),
                Is.True,
                DiagnosticsToString(result));
        }

        [Test]
        public void ValueTypeMismatch_ReportsAuthorNode()
        {
            var graph = new MaterialGraph();
            SingleSlabSource source = AddSingleSlabSource(graph);
            MaterialGraphValue invalidRoughness = graph.Add(
                "InvalidRoughness",
                source.Roughness,
                source.Emission);
            AddSingleSlabOutput(
                graph,
                source,
                invalidRoughness,
                source.Metallic,
                source.Coverage);

            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(
                graph,
                GPUDrivenMaterialCompiler.ProgramVersion);

            Assert.That(result.Succeeded, Is.False);
            AssertDiagnostic(
                result,
                MaterialIRDiagnosticCodes.OperandTypeMismatch,
                "InvalidRoughness",
                "Out");
        }

        [Test]
        public void UndefinedDerivative_MapsCanonicalDiagnosticBackToAuthorNode()
        {
            var graph = new MaterialGraph();
            SingleSlabSource source = AddSingleSlabSource(graph);
            MaterialGraphValue uv = graph.ExternalInput(
                "CoverageUV",
                MaterialExternalInput.UV0);
            MaterialGraphValue varyingProduct = graph.Multiply(
                "CoverageProduct",
                uv,
                uv);
            MaterialGraphValue derivative = graph.Ddx(
                "CoverageDdx",
                varyingProduct);
            MaterialGraphValue coverage = graph.Swizzle(
                "CoverageX",
                derivative,
                MaterialSwizzleMask.X);
            AddSingleSlabOutput(
                graph,
                source,
                source.Roughness,
                source.Metallic,
                coverage);

            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(
                graph,
                GPUDrivenMaterialCompiler.ProgramVersion);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Module, Is.Not.Null);
            AssertDiagnostic(
                result,
                MaterialIRDiagnosticCodes.DerivativeSourceCannotBeLegalized,
                "CoverageDdx",
                "Out");
        }

        [Test]
        public void ThreeSlabs_ReportsClosureBudgetAtAuthoringRoot()
        {
            var graph = new MaterialGraph();
            SingleSlabSource source = AddSingleSlabSource(graph);
            MaterialGraphClosure first = graph.Slab(
                "SlabA",
                source.BaseColor,
                source.Roughness,
                source.Metallic,
                source.Normal,
                source.Tangent);
            MaterialGraphClosure second = graph.Slab(
                "SlabB",
                source.BaseColor,
                source.Roughness,
                source.Metallic,
                source.Normal,
                source.Tangent);
            MaterialGraphClosure third = graph.Slab(
                "SlabC",
                source.BaseColor,
                source.Roughness,
                source.Metallic,
                source.Normal,
                source.Tangent);
            MaterialGraphValue weight = graph.Parameter(
                "LayerWeight",
                MaterialParameter.LayerWeight);
            MaterialGraphClosure firstMix = graph.HorizontalMix(
                "MixAB",
                first,
                second,
                weight);
            MaterialGraphClosure root = graph.HorizontalMix(
                "MixABC",
                firstMix,
                third,
                weight);
            graph.Output(
                "Output",
                root,
                source.Coverage,
                source.AlphaClipThreshold,
                source.Emission);

            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(
                graph,
                GPUDrivenMaterialCompiler.ProgramVersion);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(
                result.Diagnostics.Any(entry =>
                    entry.Code == MaterialIRDiagnosticCodes.InvalidClosureGraphShape
                    && entry.SourceNodeId == "MixABC"),
                Is.True,
                DiagnosticsToString(result));
        }

        private static MaterialGraph BuildStandardSingleSlabGraph()
        {
            var graph = new MaterialGraph();
            SingleSlabSource source = AddSingleSlabSource(graph);
            AddSingleSlabOutput(
                graph,
                source,
                source.Roughness,
                source.Metallic,
                source.Coverage);
            return graph;
        }

        private static MaterialGraph BuildGenericSingleSlabGraph()
        {
            var graph = new MaterialGraph();
            SingleSlabSource source = AddSingleSlabSource(graph);
            MaterialGraphValue surfaceBaseColor = graph.Multiply(
                "SurfaceBaseColor",
                source.BaseColor,
                graph.Constant(
                    "SurfaceTint",
                    new float4(0.5f, 0.25f, 0.75f, 1.0f)));
            MaterialGraphValue coverage = graph.Saturate(
                "ProofCoverage",
                graph.Multiply(
                    "CoverageHalf",
                    source.Coverage,
                    graph.Constant("CoverageScale", 0.5f)));
            MaterialGraphValue roughness = graph.OneMinus(
                "ProofRoughness",
                source.Roughness);
            MaterialGraphValue metallic = graph.Saturate(
                "ProofMetallic",
                graph.Multiply(
                    "MetallicHalf",
                    source.Metallic,
                    graph.Constant("MetallicScale", 0.5f)));
            MaterialGraphValue emission = graph.Add(
                "ProofEmission",
                source.Emission,
                graph.Constant(
                    "EmissionOffset",
                    new float3(0.05f, 0.1f, 0.15f)));
            MaterialGraphClosure slab = graph.Slab(
                "Slab",
                surfaceBaseColor,
                roughness,
                metallic,
                source.Normal,
                source.Tangent,
                ClosureFeatureMask.BaseColorTexture);
            graph.Output(
                "Output",
                slab,
                coverage,
                source.AlphaClipThreshold,
                emission,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit);
            return graph;
        }

        private static MaterialGraph BuildDualSlabGraph(
            VividDualSlabOperator layerOperator)
        {
            var graph = new MaterialGraph();
            MaterialGraphValue baseColor = AddSampledBaseColor(
                graph,
                "Base",
                MaterialTextureResource.BaseColor,
                MaterialParameter.BaseColor);
            MaterialGraphValue topBaseColor = AddSampledBaseColor(
                graph,
                "Top",
                MaterialTextureResource.TopBaseColor,
                MaterialParameter.TopBaseColor);
            MaterialGraphValue roughness = graph.Parameter(
                "Base.Roughness",
                MaterialParameter.Roughness);
            MaterialGraphValue topRoughness = graph.Parameter(
                "Top.Roughness",
                MaterialParameter.TopRoughness);
            MaterialGraphValue metallic = graph.Parameter(
                "Base.Metallic",
                MaterialParameter.Metallic);
            MaterialGraphValue topMetallic = graph.Parameter(
                "Top.Metallic",
                MaterialParameter.TopMetallic);
            MaterialGraphValue weight = graph.Parameter(
                "LayerWeight",
                MaterialParameter.LayerWeight);
            MaterialGraphValue threshold = graph.Parameter(
                "AlphaClipThreshold",
                MaterialParameter.AlphaClipThreshold);
            MaterialGraphValue emission = graph.Parameter(
                "Emission",
                MaterialParameter.Emission);
            MaterialGraphValue coverage = graph.Swizzle(
                "Coverage",
                baseColor,
                MaterialSwizzleMask.W);
            MaterialGraphValue normal = graph.ExternalInput(
                "GeometryNormal",
                MaterialExternalInput.GeometryNormalWS);
            MaterialGraphValue tangent = graph.ExternalInput(
                "GeometryTangent",
                MaterialExternalInput.GeometryTangentWS);
            MaterialGraphClosure baseSlab = graph.Slab(
                "Base.Slab",
                baseColor,
                roughness,
                metallic,
                normal,
                tangent);
            MaterialGraphClosure topSlab = graph.Slab(
                "Top.Slab",
                topBaseColor,
                topRoughness,
                topMetallic,
                normal,
                tangent);
            MaterialGraphClosure surface = layerOperator
                == VividDualSlabOperator.HorizontalMix
                ? graph.HorizontalMix(
                    "Layer",
                    baseSlab,
                    topSlab,
                    weight)
                : graph.VerticalLayer(
                    "Layer",
                    baseSlab,
                    topSlab,
                    weight);
            graph.Output("Output", surface, coverage, threshold, emission);
            return graph;
        }

        private static SingleSlabSource AddSingleSlabSource(MaterialGraph graph)
        {
            MaterialGraphValue baseColor = AddSampledBaseColor(
                graph,
                "Base",
                MaterialTextureResource.BaseColor,
                MaterialParameter.BaseColor);
            return new SingleSlabSource(
                baseColor,
                graph.Parameter("Roughness", MaterialParameter.Roughness),
                graph.Parameter("Metallic", MaterialParameter.Metallic),
                graph.Parameter(
                    "AlphaClipThreshold",
                    MaterialParameter.AlphaClipThreshold),
                graph.Parameter("Emission", MaterialParameter.Emission),
                graph.ExternalInput(
                    "GeometryNormal",
                    MaterialExternalInput.GeometryNormalWS),
                graph.ExternalInput(
                    "GeometryTangent",
                    MaterialExternalInput.GeometryTangentWS),
                graph.Swizzle("Coverage", baseColor, MaterialSwizzleMask.W));
        }

        private static MaterialGraphValue AddSampledBaseColor(
            MaterialGraph graph,
            string prefix,
            MaterialTextureResource texture,
            MaterialParameter color)
        {
            MaterialGraphValue uv = graph.ExternalInput(
                prefix + ".UV",
                MaterialExternalInput.UV0);
            MaterialGraphValue resource = graph.TextureResource(
                prefix + ".Texture",
                texture);
            MaterialGraphValue sample = graph.TextureSample(
                prefix + ".Sample",
                resource,
                uv);
            return graph.Multiply(
                prefix + ".BaseColor",
                sample,
                graph.Parameter(prefix + ".Color", color));
        }

        private static void AddSingleSlabOutput(
            MaterialGraph graph,
            in SingleSlabSource source,
            MaterialGraphValue roughness,
            MaterialGraphValue metallic,
            MaterialGraphValue coverage)
        {
            MaterialGraphClosure slab = graph.Slab(
                "Slab",
                source.BaseColor,
                roughness,
                metallic,
                source.Normal,
                source.Tangent);
            graph.Output(
                "Output",
                slab,
                coverage,
                source.AlphaClipThreshold,
                source.Emission);
        }

        private static void AssertProgramParity(
            MaterialGraphCompilationResult result,
            CompiledMaterialProgram expected)
        {
            Assert.That(result.Succeeded, Is.True, DiagnosticsToString(result));
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Program.SemanticHash, Is.EqualTo(expected.SemanticHash));
            Assert.That(result.Program.CompiledHash, Is.EqualTo(expected.CompiledHash));
            Assert.That(
                result.Program.Module.CanonicalIR.Payload,
                Is.EqualTo(expected.Module.CanonicalIR.Payload));
            Assert.That(
                result.Program.CoverageHlsl.Source,
                Is.EqualTo(expected.CoverageHlsl.Source));
            Assert.That(
                result.Program.SurfaceHlsl.Source,
                Is.EqualTo(expected.SurfaceHlsl.Source));
        }

        private static void AssertDiagnostic(
            MaterialGraphCompilationResult result,
            string code,
            string nodeId,
            string port)
        {
            Assert.That(
                result.Diagnostics.Any(entry =>
                    entry.Code == code
                    && entry.SourceNodeId == nodeId
                    && entry.SourcePort == port),
                Is.True,
                DiagnosticsToString(result));
        }

        private static string DiagnosticsToString(
            MaterialGraphCompilationResult result)
        {
            return string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(entry =>
                    $"{entry.Code} {entry.SourceNodeId}.{entry.SourcePort}: {entry.Message}"));
        }

        private readonly struct SingleSlabSource
        {
            internal SingleSlabSource(
                MaterialGraphValue baseColor,
                MaterialGraphValue roughness,
                MaterialGraphValue metallic,
                MaterialGraphValue alphaClipThreshold,
                MaterialGraphValue emission,
                MaterialGraphValue normal,
                MaterialGraphValue tangent,
                MaterialGraphValue coverage)
            {
                BaseColor = baseColor;
                Roughness = roughness;
                Metallic = metallic;
                AlphaClipThreshold = alphaClipThreshold;
                Emission = emission;
                Normal = normal;
                Tangent = tangent;
                Coverage = coverage;
            }

            internal MaterialGraphValue BaseColor { get; }
            internal MaterialGraphValue Roughness { get; }
            internal MaterialGraphValue Metallic { get; }
            internal MaterialGraphValue AlphaClipThreshold { get; }
            internal MaterialGraphValue Emission { get; }
            internal MaterialGraphValue Normal { get; }
            internal MaterialGraphValue Tangent { get; }
            internal MaterialGraphValue Coverage { get; }
        }
    }
}
