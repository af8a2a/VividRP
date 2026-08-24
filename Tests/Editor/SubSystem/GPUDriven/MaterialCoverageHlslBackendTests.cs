using System;
using System.IO;
using NUnit.Framework;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class MaterialCoverageHlslBackendTests
    {
        [Test]
        public void BuiltinPrograms_EmitTypedLayoutSpecificCoverageArtifacts()
        {
            CompiledMaterialProgram standard = BuildStandard();
            CompiledMaterialProgram horizontal = BuildDual(
                VividDualSlabOperator.HorizontalMix);
            CompiledMaterialProgram vertical = BuildDual(
                VividDualSlabOperator.VerticalLayer);

            AssertArtifact(
                standard.CoverageHlsl,
                MaterialSurfaceHlslPhysicalContract.LegacySingleSlab,
                "VividMaterialData",
                expectedSampleCount: 1);
            AssertArtifact(
                horizontal.CoverageHlsl,
                MaterialSurfaceHlslPhysicalContract.DualSlab,
                "VividDualSlabMaterialData",
                expectedSampleCount: 1);
            AssertArtifact(
                vertical.CoverageHlsl,
                MaterialSurfaceHlslPhysicalContract.DualSlab,
                "VividDualSlabMaterialData",
                expectedSampleCount: 1);

            Assert.That(
                horizontal.CoverageHlsl.PayloadEquals(vertical.CoverageHlsl),
                Is.True);
            Assert.That(
                horizontal.CoverageHlsl.EntryPoint,
                Is.EqualTo(vertical.CoverageHlsl.EntryPoint));
            Assert.That(
                standard.CoverageHlsl.PayloadEquals(horizontal.CoverageHlsl),
                Is.False);
        }

        [Test]
        public void CoverageArtifactSource_IsDeterministicAndUsesOnlyImportedGradients()
        {
            MaterialCoverageHlslArtifact first = BuildStandard().CoverageHlsl;
            MaterialCoverageHlslArtifact second = BuildStandard().CoverageHlsl;

            Assert.That(first.PayloadEquals(second), Is.True);
            Assert.That(second.Source, Is.EqualTo(first.Source));
            Assert.That(second.PayloadHash, Is.EqualTo(first.PayloadHash));
            Assert.That(second.BindingHash, Is.EqualTo(first.BindingHash));
            AssertExplicitGradientContract(first.Source, expectedSampleCount: 1);
        }

        [Test]
        public void BuildSource_UsesFrozenManifestAndEmitsTypedBoundsCheckedDispatcher()
        {
            CompiledMaterialProgram standard = BuildStandard();
            CompiledMaterialProgram horizontal = BuildDual(
                VividDualSlabOperator.HorizontalMix);
            CompiledMaterialProgram vertical = BuildDual(
                VividDualSlabOperator.VerticalLayer);

            MaterialProgramCatalog catalog = BakeBuiltinCatalog(
                standard,
                horizontal,
                vertical);
            string sorted = MaterialCoverageHlslSourceBuilder.BuildSource(catalog);
            string rebuilt = MaterialCoverageHlslSourceBuilder.BuildSource(
                BakeBuiltinCatalog(BuildStandard(), BuildDual(
                    VividDualSlabOperator.HorizontalMix), BuildDual(
                    VividDualSlabOperator.VerticalLayer)));

            Assert.That(rebuilt, Is.EqualTo(sorted));
            Assert.That(
                sorted,
                Does.Contain(
                    $"#define VIVID_MATERIAL_COVERAGE_HLSL_BACKEND_VERSION "
                    + $"{MaterialProgramContract.CoverageHlslBackendVersion}u"));
            Assert.That(sorted, Does.Contain("struct VividAOTCoverageContext"));
            Assert.That(sorted, Does.Contain("bool VividTryEvaluateAOTCoverageProgram("));
            Assert.That(sorted, Does.Contain("switch (runtimeHeader.ProgramID)"));
            Assert.That(sorted, Does.Contain("programData.ParameterLayoutID"));
            Assert.That(sorted, Does.Contain("programData.ResourceLayoutID"));
            Assert.That(sorted, Does.Contain("runtimeHeader.ParameterAddress"));
            Assert.That(sorted, Does.Contain("runtimeHeader.ResourceBindingAddress"));
            Assert.That(sorted, Does.Contain("_MaterialDataCount"));
            Assert.That(sorted, Does.Contain("_DualSlabMaterialDataCount"));
            Assert.That(sorted, Does.Contain("_SurfaceBindingDataCount"));
            Assert.That(sorted, Does.Contain("PullMaterialData("));
            Assert.That(sorted, Does.Contain("PullDualSlabMaterialData("));
            Assert.That(sorted, Does.Contain("PullSurfaceBindingData("));
            Assert.That(
                sorted,
                Does.Contain("#define VIVID_MATERIAL_CATALOG_MANIFEST_HASH_LO"));

            int case0 = sorted.IndexOf("        case 0u:", StringComparison.Ordinal);
            int case1 = sorted.IndexOf("        case 1u:", StringComparison.Ordinal);
            int case2 = sorted.IndexOf("        case 2u:", StringComparison.Ordinal);
            Assert.That(case0, Is.GreaterThanOrEqualTo(0));
            Assert.That(case1, Is.GreaterThan(case0));
            Assert.That(case2, Is.GreaterThan(case1));
            Assert.That(
                Slice(sorted, case0, case1),
                Does.Contain(standard.CoverageHlsl.EntryPoint));
            Assert.That(
                Slice(sorted, case1, case2),
                Does.Contain(horizontal.CoverageHlsl.EntryPoint));
            Assert.That(
                sorted.Substring(case2),
                Does.Contain(vertical.CoverageHlsl.EntryPoint));

            AssertExplicitGradientContract(sorted, expectedSampleCount: 2);
        }

        [Test]
        public void GeneralCoverageMath_CompilesToDistinctAotArtifact()
        {
            CompiledMaterialProgram baseline = BuildStandard();
            CompiledMaterialProgram general = CompiledMaterialProgram.Compile(
                BuildGeneralCoverageModule(),
                MaterialProgramContract.RuntimeAbiVersion);

            Assert.That(
                general.CoverageProgram.ProgramID,
                Is.EqualTo(VividMaterialCoverageProgramID.BaseColorAlpha));
            Assert.That(general.CoverageHlsl.Source, Does.Contain("saturate("));
            Assert.That(general.CoverageHlsl.Source, Does.Contain("asfloat(0x3F000000u)"));
            Assert.That(
                general.CoverageHlsl.PayloadEquals(baseline.CoverageHlsl),
                Is.False);
            Assert.That(
                general.CoverageHlsl.EntryPoint,
                Is.Not.EqualTo(baseline.CoverageHlsl.EntryPoint));
            AssertExplicitGradientContract(general.CoverageHlsl.Source, expectedSampleCount: 1);

            MaterialProgramCatalog catalog = MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.ForProgram("Baseline", baseline),
                MaterialProgramCatalogBakeSlot.ForProgram("General", general));
            Assert.That(
                catalog.GetEntry((VividMaterialProgramID) 0u).ProgramID,
                Is.EqualTo((VividMaterialProgramID) 0u));
            Assert.That(
                catalog.GetEntry((VividMaterialProgramID) 1u).ProgramID,
                Is.EqualTo((VividMaterialProgramID) 1u));
            Assert.That(
                catalog.GetEntry((VividMaterialProgramID) 0u).Program
                    .Lowering.SelectionKey,
                Is.EqualTo(catalog.GetEntry((VividMaterialProgramID) 1u).Program
                    .Lowering.SelectionKey));
        }

        [Test]
        public void FrozenCatalog_OneManifestDrivesRuntimeTableAndBothDispatchers()
        {
            CompiledMaterialProgram baseline = BuildStandard();
            CompiledMaterialProgram general = CompiledMaterialProgram.Compile(
                BuildGeneralCoverageModule(),
                MaterialProgramContract.RuntimeAbiVersion);
            MaterialProgramCatalog catalog = MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.ForProgram("Baseline", baseline),
                MaterialProgramCatalogBakeSlot.ForProgram("General", general));

            VividMaterialProgramData[] runtimeTable =
                catalog.CreateRuntimeProgramTable();
            string coverageSource =
                MaterialCoverageHlslSourceBuilder.BuildSource(catalog);
            string surfaceSource =
                MaterialSurfaceHlslSourceBuilder.BuildSource(catalog);
            string manifestHashLo = $"0x{unchecked((uint) catalog.ManifestHash.Value):X8}u";
            string manifestHashHi =
                $"0x{unchecked((uint) (catalog.ManifestHash.Value >> 32)):X8}u";

            Assert.That(runtimeTable, Has.Length.EqualTo(catalog.RuntimeTableLength));
            Assert.That(runtimeTable[0].Version, Is.EqualTo(
                baseline.RuntimeData.Version));
            Assert.That(runtimeTable[1].Version, Is.EqualTo(
                general.RuntimeData.Version));
            Assert.That(coverageSource, Does.Contain(manifestHashLo));
            Assert.That(coverageSource, Does.Contain(manifestHashHi));
            Assert.That(surfaceSource, Does.Contain(manifestHashLo));
            Assert.That(surfaceSource, Does.Contain(manifestHashHi));
            Assert.That(coverageSource, Does.Contain("        case 0u:"));
            Assert.That(coverageSource, Does.Contain("        case 1u:"));
            Assert.That(surfaceSource, Does.Contain("        case 0u:"));
            Assert.That(surfaceSource, Does.Contain("        case 1u:"));
            Assert.That(
                catalog.ManifestHash,
                Is.EqualTo(MaterialProgramCatalogManifestHashBuilder.Compute(
                    catalog.Slots,
                    catalog.SlotNames)));
            Assert.That(
                catalog.GetEntry((VividMaterialProgramID) 1u).Program.CompiledHash,
                Is.EqualTo(general.CompiledHash));
            Assert.That(
                catalog.GetEntry((VividMaterialProgramID) 1u).LayoutFingerprint,
                Is.EqualTo(general.Lowering.LayoutFingerprint));
            Assert.That(general.CompiledHash, Is.Not.EqualTo(baseline.CompiledHash));
        }

        [Test]
        public void CoverageArtifactVersionAndPayload_ArePartOfCompiledHashContract()
        {
            CompiledMaterialProgram program = BuildStandard();
            MaterialCoverageHlslArtifact artifact = program.CoverageHlsl;

            Assert.That(MaterialProgramContract.CoverageHlslArtifactVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.CoverageHlslBackendVersion, Is.EqualTo(1u));
            Assert.That(artifact.Version, Is.EqualTo(
                MaterialProgramContract.CoverageHlslArtifactVersion));
            Assert.That(artifact.BackendVersion, Is.EqualTo(
                MaterialProgramContract.CoverageHlslBackendVersion));
            Assert.That(program.CompiledHash.Version, Is.EqualTo(
                MaterialProgramContract.CompiledHashVersion));

            CompiledMaterialProgramHash reproduced =
                CompiledMaterialProgramHashBuilder.ComputeNativeTemplate(
                    program.SemanticHash,
                    program.Lowering,
                    artifact,
                    program.SurfaceHlsl);
            var changedArtifact = new MaterialCoverageHlslArtifact(
                artifact.EntryPoint,
                artifact.Source + "// changed artifact payload\n",
                artifact.PhysicalContract,
                artifact.BindingHash,
                artifact.CodeHash);
            CompiledMaterialProgramHash changed =
                CompiledMaterialProgramHashBuilder.ComputeNativeTemplate(
                    program.SemanticHash,
                    program.Lowering,
                    changedArtifact,
                    program.SurfaceHlsl);

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
                "VividMaterialCoverageAOT.generated.hlsl");
            Assert.That(File.Exists(generatedPath), Is.True, generatedPath);

            string expected = MaterialCoverageHlslSourceBuilder.BuildSource(
                GPUDrivenMaterialCompiler.ProgramCatalog);
            Assert.That(File.ReadAllText(generatedPath), Is.EqualTo(expected));
        }

        private static MaterialProgramCatalog BakeBuiltinCatalog(
            CompiledMaterialProgram standard,
            CompiledMaterialProgram horizontal,
            CompiledMaterialProgram vertical)
        {
            return MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "P0.StandardSingleSlab",
                    standard),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "P1.DualSlabHorizontalMix",
                    horizontal),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "P2.DualSlabVerticalLayer",
                    vertical));
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

        private static MaterialIRModule BuildGeneralCoverageModule()
        {
            var valueIR = new MaterialValueIR();
            MaterialValue uv = valueIR.ExternalInput(MaterialExternalInput.UV0);
            MaterialValue sample = valueIR.TextureSampleGrad(
                valueIR.TextureResource(MaterialTextureResource.BaseColor),
                uv,
                valueIR.Ddx(uv),
                valueIR.Ddy(uv));
            MaterialValue baseColor = valueIR.Multiply(
                sample,
                valueIR.Parameter(MaterialParameter.BaseColor));
            MaterialValue coverage = valueIR.Saturate(valueIR.Multiply(
                valueIR.Swizzle(baseColor, MaterialSwizzleMask.W),
                valueIR.Constant(0.5f)));
            MaterialValue roughness = valueIR.Parameter(MaterialParameter.Roughness);
            MaterialValue metallic = valueIR.Parameter(MaterialParameter.Metallic);
            MaterialValue normal =
                valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent =
                valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS);
            var closureGraph = new ClosureExpressionGraph(valueIR);
            MaterialClosure surfaceClosure = closureGraph.Slab(
                baseColor,
                roughness,
                metallic,
                normal,
                tangent,
                ClosureFeatureMask.BaseColorTexture
                | ClosureFeatureMask.NormalTexture
                | ClosureFeatureMask.MaskTexture);
            return new MaterialIRModule(
                valueIR,
                new MaterialOutputRoots(
                    coverage,
                    valueIR.Parameter(MaterialParameter.AlphaClipThreshold),
                    valueIR.Parameter(MaterialParameter.Emission)),
                closureGraph,
                surfaceClosure,
                ClosureTopologyBudget.Prototype,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit
                | MaterialShadingModelMask.Unlit);
        }

        private static void AssertArtifact(
            MaterialCoverageHlslArtifact artifact,
            MaterialSurfaceHlslPhysicalContract expectedPhysicalContract,
            string expectedParameterType,
            int expectedSampleCount)
        {
            Assert.That(artifact, Is.Not.Null);
            Assert.That(
                artifact.EntryPoint,
                Does.StartWith("VividEvaluateAOTCoverage_"));
            Assert.That(artifact.PhysicalContract, Is.EqualTo(expectedPhysicalContract));
            Assert.That(artifact.Source, Does.Contain(
                $"VividMaterialCoverageEvaluation {artifact.EntryPoint}("));
            Assert.That(artifact.Source, Does.Contain(
                $"    const {expectedParameterType} materialParameters,"));
            Assert.That(artifact.Source, Does.Contain("output.Coverage ="));
            Assert.That(artifact.Source, Does.Contain("output.AlphaClipThreshold ="));
            if (expectedPhysicalContract == MaterialSurfaceHlslPhysicalContract.DualSlab)
                Assert.That(artifact.Source, Does.Contain("surfaceBinding1"));

            AssertExplicitGradientContract(artifact.Source, expectedSampleCount);
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
            Assert.That(source, Does.Contain("context.UV0Ddx"));
            Assert.That(source, Does.Contain("context.UV0Ddy"));
            Assert.That(source, Does.Contain("vivid_sample_ddx_"));
            Assert.That(source, Does.Contain("vivid_sample_ddy_"));
            Assert.That(source, Does.Not.Contain("PositionCS"));
            Assert.That(source, Does.Not.Contain("VividSampleBaseColor("));
            Assert.That(source, Does.Not.Contain(".Sample("));
            Assert.That(source, Does.Not.Contain("ddx("));
            Assert.That(source, Does.Not.Contain("ddy("));
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
