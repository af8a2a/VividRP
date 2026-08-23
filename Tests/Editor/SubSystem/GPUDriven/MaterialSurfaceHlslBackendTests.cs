using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class MaterialSurfaceHlslBackendTests
    {
        [Test]
        public void BuiltinPrograms_EmitTypedTopologySpecificSurfaceArtifacts()
        {
            CompiledMaterialProgram standard = BuildStandard();
            CompiledMaterialProgram horizontal = BuildDual(
                VividDualSlabOperator.HorizontalMix);
            CompiledMaterialProgram vertical = BuildDual(
                VividDualSlabOperator.VerticalLayer);

            AssertArtifact(
                standard.SurfaceHlsl,
                "VividEvaluateAOTSurface_StandardSingleSlab",
                MaterialProgramTopologySpecialization.SingleSlab,
                MaterialSurfaceHlslPhysicalContract.LegacySingleSlab,
                "VividMaterialData",
                expectedClosureCount: 1u,
                expectedLayerOperator: 0u,
                expectedSampleCount: 1);
            AssertArtifact(
                horizontal.SurfaceHlsl,
                "VividEvaluateAOTSurface_DualSlabHorizontalMix",
                MaterialProgramTopologySpecialization.HorizontalMix,
                MaterialSurfaceHlslPhysicalContract.DualSlab,
                "VividDualSlabMaterialData",
                expectedClosureCount: 2u,
                expectedLayerOperator: 1u,
                expectedSampleCount: 2);
            AssertArtifact(
                vertical.SurfaceHlsl,
                "VividEvaluateAOTSurface_DualSlabVerticalLayer",
                MaterialProgramTopologySpecialization.VerticalLayer,
                MaterialSurfaceHlslPhysicalContract.DualSlab,
                "VividDualSlabMaterialData",
                expectedClosureCount: 2u,
                expectedLayerOperator: 2u,
                expectedSampleCount: 2);
        }

        [Test]
        public void SurfaceArtifactSource_IsDeterministicAndUsesOnlyExplicitGradients()
        {
            CompiledMaterialProgram first = BuildStandard();
            CompiledMaterialProgram second = BuildStandard();
            MaterialSurfaceHlslArtifact firstArtifact = first.SurfaceHlsl;
            MaterialSurfaceHlslArtifact secondArtifact = second.SurfaceHlsl;

            Assert.That(firstArtifact.PayloadEquals(secondArtifact), Is.True);
            Assert.That(secondArtifact.Source, Is.EqualTo(firstArtifact.Source));
            Assert.That(secondArtifact.PayloadHash, Is.EqualTo(firstArtifact.PayloadHash));
            Assert.That(secondArtifact.BindingHash, Is.EqualTo(firstArtifact.BindingHash));
            Assert.That(second.CompiledHash, Is.EqualTo(first.CompiledHash));

            AssertExplicitGradientContract(firstArtifact.Source, expectedSampleCount: 1);
            Assert.That(firstArtifact.Source, Does.Contain("context.UV0Ddx"));
            Assert.That(firstArtifact.Source, Does.Contain("context.UV0Ddy"));
            Assert.That(firstArtifact.Source, Does.Contain(".TextureTilingOffset.zw"));
            Assert.That(
                CountOccurrences(firstArtifact.Source, ".TextureTilingOffset.xy"),
                Is.EqualTo(3));
        }

        [Test]
        public void BuildSource_SortsProgramIdsAndEmitsTopologyPreservingDispatcher()
        {
            CompiledMaterialProgram standard = BuildStandard();
            CompiledMaterialProgram horizontal = BuildDual(
                VividDualSlabOperator.HorizontalMix);
            CompiledMaterialProgram vertical = BuildDual(
                VividDualSlabOperator.VerticalLayer);

            string sorted = MaterialSurfaceHlslSourceBuilder.BuildSource(
                new[] { standard, horizontal, vertical });
            string shuffled = MaterialSurfaceHlslSourceBuilder.BuildSource(
                new[] { vertical, standard, horizontal });

            Assert.That(shuffled, Is.EqualTo(sorted));
            Assert.That(
                sorted,
                Does.Contain(
                    $"#define VIVID_MATERIAL_SURFACE_HLSL_BACKEND_VERSION "
                    + $"{MaterialProgramContract.SurfaceHlslBackendVersion}u"));
            Assert.That(sorted, Does.Contain("bool VividTryEvaluateAOTSurfaceProgram("));
            Assert.That(sorted, Does.Contain("switch (programID)"));

            int standardEntry = sorted.IndexOf(
                standard.SurfaceHlsl.EntryPoint,
                StringComparison.Ordinal);
            int horizontalEntry = sorted.IndexOf(
                horizontal.SurfaceHlsl.EntryPoint,
                StringComparison.Ordinal);
            int verticalEntry = sorted.IndexOf(
                vertical.SurfaceHlsl.EntryPoint,
                StringComparison.Ordinal);
            Assert.That(standardEntry, Is.GreaterThanOrEqualTo(0));
            Assert.That(horizontalEntry, Is.GreaterThan(standardEntry));
            Assert.That(verticalEntry, Is.GreaterThan(horizontalEntry));

            int case0 = sorted.IndexOf("        case 0u:", StringComparison.Ordinal);
            int case1 = sorted.IndexOf("        case 1u:", StringComparison.Ordinal);
            int case2 = sorted.IndexOf("        case 2u:", StringComparison.Ordinal);
            Assert.That(case0, Is.GreaterThan(verticalEntry));
            Assert.That(case1, Is.GreaterThan(case0));
            Assert.That(case2, Is.GreaterThan(case1));
            Assert.That(
                Slice(sorted, case0, case1),
                Does.Contain(standard.SurfaceHlsl.EntryPoint));
            Assert.That(
                Slice(sorted, case1, case2),
                Does.Contain(horizontal.SurfaceHlsl.EntryPoint));
            Assert.That(
                sorted.Substring(case2),
                Does.Contain(vertical.SurfaceHlsl.EntryPoint));

            Assert.That(sorted, Does.Contain("output.LayerOperator = 1u;"));
            Assert.That(sorted, Does.Contain("output.LayerOperator = 2u;"));
            Assert.That(sorted, Does.Contain("float3 NormalTS;"));
            Assert.That(sorted, Does.Contain("float AmbientOcclusion;"));
            Assert.That(sorted, Does.Contain("uint HasNormal;"));
            AssertExplicitGradientContract(sorted, expectedSampleCount: 5);
            AssertAotDetailContract(sorted, expectedSlabCount: 5);
        }

        [Test]
        public void SurfaceArtifactVersionAndPayload_ArePartOfCompiledHashContract()
        {
            CompiledMaterialProgram program = BuildStandard();
            MaterialSurfaceHlslArtifact artifact = program.SurfaceHlsl;

            Assert.That(MaterialProgramContract.SurfaceHlslArtifactVersion, Is.EqualTo(2u));
            Assert.That(MaterialProgramContract.SurfaceHlslBackendVersion, Is.EqualTo(2u));
            Assert.That(MaterialProgramContract.CoverageHlslArtifactVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.CoverageHlslBackendVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.CompiledHashVersion, Is.EqualTo(4u));
            Assert.That(MaterialProgramContract.CompilerVersion, Is.EqualTo(9u));
            Assert.That(MaterialProgramContract.NativeTemplateBackendVersion, Is.EqualTo(6u));
            Assert.That(MaterialProgramContract.ProgramCatalogVersion, Is.EqualTo(1u));
            Assert.That(artifact.Version, Is.EqualTo(
                MaterialProgramContract.SurfaceHlslArtifactVersion));
            Assert.That(artifact.BackendVersion, Is.EqualTo(
                MaterialProgramContract.SurfaceHlslBackendVersion));
            Assert.That(program.CompiledHash.Version, Is.EqualTo(
                MaterialProgramContract.CompiledHashVersion));

            CompiledMaterialProgramHash reproduced =
                CompiledMaterialProgramHashBuilder.ComputeNativeTemplate(
                    program.SemanticHash,
                    program.Lowering,
                    program.CoverageHlsl,
                    artifact);
            var changedArtifact = new MaterialSurfaceHlslArtifact(
                artifact.EntryPoint,
                artifact.Source + "// changed artifact payload\n",
                artifact.Topology,
                artifact.PhysicalContract,
                artifact.BindingHash);
            CompiledMaterialProgramHash changed =
                CompiledMaterialProgramHashBuilder.ComputeNativeTemplate(
                    program.SemanticHash,
                    program.Lowering,
                    program.CoverageHlsl,
                    changedArtifact);

            Assert.That(reproduced, Is.EqualTo(program.CompiledHash));
            Assert.That(changedArtifact.PayloadHash, Is.Not.EqualTo(artifact.PayloadHash));
            Assert.That(changed, Is.Not.EqualTo(program.CompiledHash));
        }

        [Test]
        public void GeneratedInclude_IsSynchronizedWithBuiltinProgramCatalog()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(CompiledMaterialProgram).Assembly);
            Assert.That(package, Is.Not.Null);
            string generatedPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividMaterialSurfaceAOT.generated.hlsl");
            Assert.That(File.Exists(generatedPath), Is.True, generatedPath);

            string expected = MaterialSurfaceHlslSourceBuilder.BuildSource(
                new[]
                {
                    BuildStandard(),
                    BuildDual(VividDualSlabOperator.HorizontalMix),
                    BuildDual(VividDualSlabOperator.VerticalLayer),
                });
            Assert.That(File.ReadAllText(generatedPath), Is.EqualTo(expected));
        }

        private static CompiledMaterialProgram BuildStandard()
        {
            return MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                MaterialProgramContract.RuntimeAbiVersion);
        }

        private static CompiledMaterialProgram BuildDual(
            VividDualSlabOperator layerOperator)
        {
            return MaterialProgramPrototypeBuilder.BuildDualSlab(
                MaterialProgramContract.RuntimeAbiVersion,
                layerOperator);
        }

        private static void AssertArtifact(
            MaterialSurfaceHlslArtifact artifact,
            string expectedEntryPoint,
            MaterialProgramTopologySpecialization expectedTopology,
            MaterialSurfaceHlslPhysicalContract expectedPhysicalContract,
            string expectedParameterType,
            uint expectedClosureCount,
            uint expectedLayerOperator,
            int expectedSampleCount)
        {
            Assert.That(artifact, Is.Not.Null);
            Assert.That(artifact.EntryPoint, Is.EqualTo(expectedEntryPoint));
            Assert.That(artifact.Topology, Is.EqualTo(expectedTopology));
            Assert.That(artifact.PhysicalContract, Is.EqualTo(expectedPhysicalContract));
            Assert.That(artifact.Source, Does.Contain(
                $"VividAOTSurfaceProgramOutput {expectedEntryPoint}("));
            Assert.That(artifact.Source, Does.Contain(
                $"    const {expectedParameterType} materialParameters,"));
            Assert.That(artifact.Source, Does.Contain(
                $"output.ClosureCount = {expectedClosureCount}u;"));
            Assert.That(artifact.Source, Does.Contain(
                $"output.LayerOperator = {expectedLayerOperator}u;"));
            if (expectedPhysicalContract == MaterialSurfaceHlslPhysicalContract.DualSlab)
                Assert.That(artifact.Source, Does.Contain("surfaceBinding1"));

            AssertExplicitGradientContract(artifact.Source, expectedSampleCount);
            AssertAotDetailContract(artifact.Source, expectedSampleCount);
        }

        private static void AssertExplicitGradientContract(
            string source,
            int expectedSampleCount)
        {
            Assert.That(
                CountOccurrences(source, "VividCreateSurfaceSampleContextGrad("),
                Is.EqualTo(expectedSampleCount));
            Assert.That(
                CountOccurrences(source, "VividSampleBaseColorGrad("),
                Is.EqualTo(expectedSampleCount));
            Assert.That(source, Does.Contain("vivid_sample_ddx_"));
            Assert.That(source, Does.Contain("vivid_sample_ddy_"));
            Assert.That(source, Does.Not.Contain("VividSampleBaseColor("));
            Assert.That(source, Does.Not.Contain(".Sample("));
            Assert.That(source, Does.Not.Contain("ddx("));
            Assert.That(source, Does.Not.Contain("ddy("));
        }

        private static void AssertAotDetailContract(
            string source,
            int expectedSlabCount)
        {
            const string detailFunction = "VividEvaluateAOTSlabSurfaceDetail";
            List<string> detailCalls = ExtractCalls(source, detailFunction);
            List<string> sampleContexts = ExtractDeclaredIdentifiers(
                source,
                "const VividSurfaceSampleContext ");

            Assert.That(detailCalls.Count, Is.EqualTo(expectedSlabCount));
            Assert.That(sampleContexts.Count, Is.EqualTo(expectedSlabCount));
            Assert.That(
                CountOccurrences(source, ".NormalTS ="),
                Is.EqualTo(expectedSlabCount));
            Assert.That(
                CountOccurrences(source, ".AmbientOcclusion ="),
                Is.EqualTo(expectedSlabCount));
            Assert.That(
                CountOccurrences(source, ".HasNormal ="),
                Is.EqualTo(expectedSlabCount));

            var contextDeclarationCounts = new Dictionary<string, int>();
            for (int contextIndex = 0;
                 contextIndex < sampleContexts.Count;
                 contextIndex++)
            {
                string contextName = sampleContexts[contextIndex];
                contextDeclarationCounts.TryGetValue(
                    contextName,
                    out int declarationCount);
                contextDeclarationCounts[contextName] = declarationCount + 1;
            }

            foreach (KeyValuePair<string, int> contextDeclaration in
                     contextDeclarationCounts)
            {
                int matchingDetailCalls = 0;
                for (int callIndex = 0;
                     callIndex < detailCalls.Count;
                     callIndex++)
                {
                    if (detailCalls[callIndex].IndexOf(
                            contextDeclaration.Key,
                            StringComparison.Ordinal) >= 0)
                    {
                        matchingDetailCalls++;
                    }
                }

                Assert.That(
                    matchingDetailCalls,
                    Is.EqualTo(contextDeclaration.Value),
                    $"Sample context '{contextDeclaration.Key}' must be reused once per declaration.");
            }

            for (int callIndex = 0; callIndex < detailCalls.Count; callIndex++)
            {
                Assert.That(
                    CountOccurrences(detailCalls[callIndex], "true"),
                    Is.EqualTo(2),
                    "The frozen P0-P2 catalog enables Normal and Mask detail evaluation.");
            }
        }

        private static List<string> ExtractCalls(
            string source,
            string functionName)
        {
            var calls = new List<string>();
            string marker = functionName + "(";
            int searchOffset = 0;
            while (true)
            {
                int callStart = source.IndexOf(
                    marker,
                    searchOffset,
                    StringComparison.Ordinal);
                if (callStart < 0)
                    return calls;

                int depth = 1;
                int cursor = callStart + marker.Length;
                for (; cursor < source.Length && depth > 0; cursor++)
                {
                    if (source[cursor] == '(')
                        depth++;
                    else if (source[cursor] == ')')
                        depth--;
                }

                Assert.That(
                    depth,
                    Is.Zero,
                    $"Generated call to '{functionName}' is not balanced.");
                calls.Add(source.Substring(callStart, cursor - callStart));
                searchOffset = cursor;
            }
        }

        private static List<string> ExtractDeclaredIdentifiers(
            string source,
            string declarationPrefix)
        {
            var identifiers = new List<string>();
            int searchOffset = 0;
            while (true)
            {
                int prefixStart = source.IndexOf(
                    declarationPrefix,
                    searchOffset,
                    StringComparison.Ordinal);
                if (prefixStart < 0)
                    return identifiers;

                int identifierStart = prefixStart + declarationPrefix.Length;
                int identifierEnd = source.IndexOf(' ', identifierStart);
                Assert.That(identifierEnd, Is.GreaterThan(identifierStart));
                identifiers.Add(source.Substring(
                    identifierStart,
                    identifierEnd - identifierStart));
                searchOffset = identifierEnd;
            }
        }

        private static int CountOccurrences(string value, string pattern)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(
                       pattern,
                       offset,
                       StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += pattern.Length;
            }
            return count;
        }

        private static string Slice(string value, int start, int end)
        {
            return value.Substring(start, end - start);
        }
    }
}
