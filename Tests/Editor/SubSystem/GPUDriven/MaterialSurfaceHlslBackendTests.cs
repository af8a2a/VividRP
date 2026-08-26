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
                MaterialProgramTopologySpecialization.SingleSlab,
                MaterialSurfaceHlslPhysicalContract.LegacySingleSlab,
                "VividMaterialData",
                expectedClosureCount: 1u,
                expectedLayerOperator: 0u,
                expectedSampleCount: 1);
            AssertArtifact(
                horizontal.SurfaceHlsl,
                MaterialProgramTopologySpecialization.HorizontalMix,
                MaterialSurfaceHlslPhysicalContract.DualSlab,
                "VividDualSlabMaterialData",
                expectedClosureCount: 2u,
                expectedLayerOperator: 1u,
                expectedSampleCount: 2);
            AssertArtifact(
                vertical.SurfaceHlsl,
                MaterialProgramTopologySpecialization.VerticalLayer,
                MaterialSurfaceHlslPhysicalContract.DualSlab,
                "VividDualSlabMaterialData",
                expectedClosureCount: 2u,
                expectedLayerOperator: 2u,
                expectedSampleCount: 2);
        }

        [Test]
        public void SurfaceEntryPoint_IsStableAcrossCanonicalAllocationAndCatalogIdentity()
        {
            CompiledMaterialProgram baseline = BuildStandard();
            CompiledMaterialProgram reordered = CompiledMaterialProgram.Compile(
                BuildSingleSlabModule(
                    alternateDeclarationOrder: true,
                    useGeneralSurfaceMath: false),
                MaterialProgramContract.RuntimeAbiVersion);

            Assert.That(
                reordered.Module.CanonicalIR.PayloadEquals(
                    baseline.Module.CanonicalIR),
                Is.True);
            Assert.That(
                reordered.SurfaceHlsl.EntryPoint,
                Is.EqualTo(baseline.SurfaceHlsl.EntryPoint));
            Assert.That(
                reordered.SurfaceHlsl.CodeHash,
                Is.EqualTo(baseline.SurfaceHlsl.CodeHash));

            MaterialProgramCatalog lowIdentity = MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "canonical-standard-low",
                    baseline));
            MaterialProgramCatalog highIdentity = MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.Reserved("reserved-0"),
                MaterialProgramCatalogBakeSlot.Reserved("reserved-1"),
                MaterialProgramCatalogBakeSlot.Reserved("reserved-2"),
                MaterialProgramCatalogBakeSlot.Reserved("reserved-3"),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "canonical-standard-high",
                    reordered));
            MaterialProgramCatalog.ManifestEntry lowEntry = lowIdentity.GetEntry(
                (VividMaterialProgramID) 0u);
            MaterialProgramCatalog.ManifestEntry highEntry = highIdentity.GetEntry(
                (VividMaterialProgramID) 4u);

            Assert.That(highEntry.ProgramID, Is.Not.EqualTo(lowEntry.ProgramID));
            Assert.That(
                highEntry.Program.SurfaceHlsl.EntryPoint,
                Is.EqualTo(lowEntry.Program.SurfaceHlsl.EntryPoint));
        }

        [Test]
        public void SameTopologyDifferentSurfaceMath_EmitsDistinctCoexistingArtifacts()
        {
            CompiledMaterialProgram baseline = BuildStandard();
            CompiledMaterialProgram general = CompiledMaterialProgram.Compile(
                BuildSingleSlabModule(
                    alternateDeclarationOrder: false,
                    useGeneralSurfaceMath: true),
                MaterialProgramContract.RuntimeAbiVersion);

            Assert.That(
                general.Lowering.SelectionKey.Topology,
                Is.EqualTo(baseline.Lowering.SelectionKey.Topology));
            Assert.That(general.SurfaceHlsl.Source, Does.Contain("saturate("));
            Assert.That(general.SurfaceHlsl.Source, Does.Contain("asfloat(0x3F000000u)"));
            Assert.That(
                general.SurfaceHlsl.EntryPoint,
                Is.Not.EqualTo(baseline.SurfaceHlsl.EntryPoint));
            Assert.That(
                general.SurfaceHlsl.CodeHash,
                Is.Not.EqualTo(baseline.SurfaceHlsl.CodeHash));
            Assert.That(
                general.SurfaceHlsl.PayloadEquals(baseline.SurfaceHlsl),
                Is.False);

            MaterialProgramCatalog catalog = MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "single-slab-baseline",
                    baseline),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "single-slab-general-math",
                    general));
            string source = MaterialSurfaceHlslSourceBuilder.BuildSource(catalog);
            Assert.That(
                CountOccurrences(source, baseline.SurfaceHlsl.EntryPoint),
                Is.EqualTo(2));
            Assert.That(
                CountOccurrences(source, general.SurfaceHlsl.EntryPoint),
                Is.EqualTo(2));
            Assert.That(
                Slice(
                    source,
                    source.IndexOf("        case 0u:", StringComparison.Ordinal),
                    source.IndexOf("        case 1u:", StringComparison.Ordinal)),
                Does.Contain(baseline.SurfaceHlsl.EntryPoint));
            Assert.That(
                source.Substring(source.IndexOf(
                    "        case 1u:",
                    StringComparison.Ordinal)),
                Does.Contain(general.SurfaceHlsl.EntryPoint));
        }

        [Test]
        public void SameSurfaceArtifactDifferentShadingMasks_EmitExactCoexistingContracts()
        {
            CompiledMaterialProgram lit = CompiledMaterialProgram.Compile(
                BuildSingleSlabModule(
                    alternateDeclarationOrder: false,
                    useGeneralSurfaceMath: false,
                    shadingModels: MaterialShadingModelMask.StandardLit),
                MaterialProgramContract.RuntimeAbiVersion);
            CompiledMaterialProgram unlit = CompiledMaterialProgram.Compile(
                BuildSingleSlabModule(
                    alternateDeclarationOrder: false,
                    useGeneralSurfaceMath: false,
                    shadingModels: MaterialShadingModelMask.Unlit),
                MaterialProgramContract.RuntimeAbiVersion);
            CompiledMaterialProgram selectable = CompiledMaterialProgram.Compile(
                BuildSingleSlabModule(
                    alternateDeclarationOrder: false,
                    useGeneralSurfaceMath: false,
                    shadingModels: MaterialShadingModelMask.StandardLit
                    | MaterialShadingModelMask.Unlit),
                MaterialProgramContract.RuntimeAbiVersion);

            Assert.That(
                unlit.Lowering.SelectionKey.Topology,
                Is.EqualTo(lit.Lowering.SelectionKey.Topology));
            Assert.That(
                selectable.Lowering.SelectionKey.Topology,
                Is.EqualTo(lit.Lowering.SelectionKey.Topology));
            Assert.That(unlit.SurfaceHlsl.EntryPoint, Is.EqualTo(lit.SurfaceHlsl.EntryPoint));
            Assert.That(
                selectable.SurfaceHlsl.EntryPoint,
                Is.EqualTo(lit.SurfaceHlsl.EntryPoint));
            Assert.That(unlit.SurfaceHlsl.PayloadEquals(lit.SurfaceHlsl), Is.True);
            Assert.That(selectable.SurfaceHlsl.PayloadEquals(lit.SurfaceHlsl), Is.True);
            Assert.That(unlit.CompiledHash, Is.Not.EqualTo(lit.CompiledHash));
            Assert.That(selectable.CompiledHash, Is.Not.EqualTo(lit.CompiledHash));
            Assert.That(selectable.CompiledHash, Is.Not.EqualTo(unlit.CompiledHash));

            MaterialProgramCatalog catalog = MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.ForProgram("P0.Lit", lit),
                MaterialProgramCatalogBakeSlot.ForProgram("P1.Unlit", unlit),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "P2.RuntimeSelectable",
                    selectable));
            Assert.That(
                catalog.GetEntry((VividMaterialProgramID) 0u).Program,
                Is.SameAs(lit));
            Assert.That(
                catalog.GetEntry((VividMaterialProgramID) 1u).Program,
                Is.SameAs(unlit));
            Assert.That(
                catalog.GetEntry((VividMaterialProgramID) 2u).Program,
                Is.SameAs(selectable));

            string source = MaterialSurfaceHlslSourceBuilder.BuildSource(catalog);
            int case0 = source.IndexOf("        case 0u:", StringComparison.Ordinal);
            int case1 = source.IndexOf("        case 1u:", StringComparison.Ordinal);
            int case2 = source.IndexOf("        case 2u:", StringComparison.Ordinal);
            Assert.That(case0, Is.GreaterThanOrEqualTo(0));
            Assert.That(case1, Is.GreaterThan(case0));
            Assert.That(case2, Is.GreaterThan(case1));
            AssertDispatcherDeferredExportContract(
                Slice(source, case0, case1),
                shadingModelMask: 1u,
                litClass: 2u,
                sidecarAbi: 0u,
                policyFlags: 7u);
            AssertDispatcherDeferredExportContract(
                Slice(source, case1, case2),
                shadingModelMask: 2u,
                litClass: 0u,
                sidecarAbi: 0u,
                policyFlags: 4u);
            AssertDispatcherDeferredExportContract(
                source.Substring(case2),
                shadingModelMask: 3u,
                litClass: 2u,
                sidecarAbi: 0u,
                policyFlags: 7u);
        }

        [Test]
        public void DualSlabCrossSlabBaseColorResourceMapping_IsRejected()
        {
            NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
                CompiledMaterialProgram.Compile(
                    BuildCrossSlabResourceModule(),
                    MaterialProgramContract.RuntimeAbiVersion));

            Assert.That(
                exception.Message,
                Does.Contain("BaseSlab base-color sample maps to 'TopBaseColor'"));
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
        public void BuildSource_EmitsTopologyPreservingCatalogDispatcher()
        {
            CompiledMaterialProgram standard = BuildStandard();
            CompiledMaterialProgram horizontal = BuildDual(
                VividDualSlabOperator.HorizontalMix);
            CompiledMaterialProgram vertical = BuildDual(
                VividDualSlabOperator.VerticalLayer);

            string sorted = MaterialSurfaceHlslSourceBuilder.BuildSource(
                BakeCatalog(standard, horizontal, vertical));
            string rebuilt = MaterialSurfaceHlslSourceBuilder.BuildSource(
                BakeCatalog(
                    BuildStandard(),
                    BuildDual(VividDualSlabOperator.HorizontalMix),
                    BuildDual(VividDualSlabOperator.VerticalLayer)));

            Assert.That(rebuilt, Is.EqualTo(sorted));
            Assert.That(
                sorted,
                Does.Contain(
                    $"#define VIVID_MATERIAL_SURFACE_HLSL_BACKEND_VERSION "
                    + $"{MaterialProgramContract.SurfaceHlslBackendVersion}u"));
            Assert.That(sorted, Does.Contain("bool VividTryEvaluateAOTSurfaceProgram("));
            Assert.That(
                sorted,
                Does.Contain(
                    "out VividAOTDeferredExportContract deferredExportContract"));
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
                Slice(sorted, case0, case1),
                Does.Contain("deferredExportContract.LitClass = 2u;"));
            Assert.That(
                Slice(sorted, case0, case1),
                Does.Contain("deferredExportContract.DualSlabSidecarAbi = 0u;"));
            Assert.That(
                Slice(sorted, case0, case1),
                Does.Contain("deferredExportContract.ShadingModelMask = 3u;"));
            Assert.That(
                Slice(sorted, case0, case1),
                Does.Contain("deferredExportContract.ExpectedClosureCount = 1u;"));
            Assert.That(
                Slice(sorted, case0, case1),
                Does.Contain("deferredExportContract.Topology = 0u;"));
            Assert.That(
                Slice(sorted, case0, case1),
                Does.Contain("deferredExportContract.PayloadFlags = 3u;"));
            Assert.That(
                Slice(sorted, case0, case1),
                Does.Contain("deferredExportContract.PolicyFlags = 7u;"));
            Assert.That(
                Slice(sorted, case1, case2),
                Does.Contain(horizontal.SurfaceHlsl.EntryPoint));
            Assert.That(
                Slice(sorted, case1, case2),
                Does.Contain("deferredExportContract.LitClass = 4u;"));
            Assert.That(
                Slice(sorted, case1, case2),
                Does.Contain("deferredExportContract.DualSlabSidecarAbi = 1u;"));
            Assert.That(
                Slice(sorted, case1, case2),
                Does.Contain("deferredExportContract.ShadingModelMask = 3u;"));
            Assert.That(
                Slice(sorted, case1, case2),
                Does.Contain("deferredExportContract.ExpectedClosureCount = 2u;"));
            Assert.That(
                Slice(sorted, case1, case2),
                Does.Contain("deferredExportContract.Topology = 1u;"));
            Assert.That(
                Slice(sorted, case1, case2),
                Does.Contain("deferredExportContract.PayloadFlags = 15u;"));
            Assert.That(
                Slice(sorted, case1, case2),
                Does.Contain("deferredExportContract.PolicyFlags = 15u;"));
            Assert.That(
                sorted.Substring(case2),
                Does.Contain(vertical.SurfaceHlsl.EntryPoint));
            Assert.That(
                sorted.Substring(case2),
                Does.Contain("deferredExportContract.Topology = 2u;"));

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

            Assert.That(MaterialProgramContract.SurfaceHlslArtifactVersion, Is.EqualTo(3u));
            Assert.That(MaterialProgramContract.SurfaceHlslBackendVersion, Is.EqualTo(4u));
            Assert.That(MaterialProgramContract.CoverageHlslArtifactVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.CoverageHlslBackendVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.CompiledHashVersion, Is.EqualTo(6u));
            Assert.That(MaterialProgramContract.CompilerVersion, Is.EqualTo(11u));
            Assert.That(MaterialProgramContract.NativeTemplateBackendVersion, Is.EqualTo(6u));
            Assert.That(MaterialProgramContract.ProgramCatalogVersion, Is.EqualTo(3u));
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
                artifact.BindingHash,
                artifact.CodeHash);
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
                GPUDrivenMaterialCompiler.ProgramCatalog);
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

        private static MaterialProgramCatalog BakeCatalog(
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

        private static MaterialIRModule BuildSingleSlabModule(
            bool alternateDeclarationOrder,
            bool useGeneralSurfaceMath,
            MaterialShadingModelMask shadingModels =
                MaterialShadingModelMask.StandardLit
                | MaterialShadingModelMask.Unlit)
        {
            var valueIR = new MaterialValueIR();
            MaterialValue baseColor;
            MaterialValue roughness;
            MaterialValue metallic;
            MaterialValue alphaClipThreshold;
            MaterialValue emission;
            MaterialValue normal;
            MaterialValue tangent;
            if (alternateDeclarationOrder)
            {
                roughness = valueIR.Parameter(MaterialParameter.Roughness);
                metallic = valueIR.Parameter(MaterialParameter.Metallic);
                alphaClipThreshold =
                    valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
                emission = valueIR.Parameter(MaterialParameter.Emission);
                normal = valueIR.ExternalInput(
                    MaterialExternalInput.GeometryNormalWS);
                tangent = valueIR.ExternalInput(
                    MaterialExternalInput.GeometryTangentWS);
                valueIR.Constant(123.0f);
                valueIR.Parameter(MaterialParameter.TopRoughness);
                valueIR.TextureResource(MaterialTextureResource.TopBaseColor);
                baseColor = BuildSampledBaseColor(
                    valueIR,
                    MaterialTextureResource.BaseColor,
                    MaterialParameter.BaseColor);
            }
            else
            {
                baseColor = BuildSampledBaseColor(
                    valueIR,
                    MaterialTextureResource.BaseColor,
                    MaterialParameter.BaseColor);
                roughness = valueIR.Parameter(MaterialParameter.Roughness);
                metallic = valueIR.Parameter(MaterialParameter.Metallic);
                alphaClipThreshold =
                    valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
                emission = valueIR.Parameter(MaterialParameter.Emission);
                normal = valueIR.ExternalInput(
                    MaterialExternalInput.GeometryNormalWS);
                tangent = valueIR.ExternalInput(
                    MaterialExternalInput.GeometryTangentWS);
            }

            MaterialValue surfaceRoughness = useGeneralSurfaceMath
                ? valueIR.Saturate(valueIR.Multiply(
                    roughness,
                    valueIR.Constant(0.5f)))
                : roughness;
            MaterialValue coverage = valueIR.Swizzle(
                baseColor,
                MaterialSwizzleMask.W);
            var closureGraph = new ClosureExpressionGraph(valueIR);
            MaterialClosure surfaceClosure = closureGraph.Slab(
                baseColor,
                surfaceRoughness,
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
                    alphaClipThreshold,
                    emission),
                closureGraph,
                surfaceClosure,
                ClosureTopologyBudget.Prototype,
                MaterialFeatureMask.AlphaClip,
                shadingModels);
        }

        private static MaterialIRModule BuildCrossSlabResourceModule()
        {
            var valueIR = new MaterialValueIR();
            MaterialValue baseColor = BuildSampledBaseColor(
                valueIR,
                MaterialTextureResource.TopBaseColor,
                MaterialParameter.BaseColor);
            MaterialValue topBaseColor = BuildSampledBaseColor(
                valueIR,
                MaterialTextureResource.BaseColor,
                MaterialParameter.TopBaseColor);
            MaterialValue roughness =
                valueIR.Parameter(MaterialParameter.Roughness);
            MaterialValue topRoughness =
                valueIR.Parameter(MaterialParameter.TopRoughness);
            MaterialValue metallic =
                valueIR.Parameter(MaterialParameter.Metallic);
            MaterialValue topMetallic =
                valueIR.Parameter(MaterialParameter.TopMetallic);
            MaterialValue layerWeight =
                valueIR.Parameter(MaterialParameter.LayerWeight);
            MaterialValue alphaClipThreshold =
                valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
            MaterialValue emission =
                valueIR.Parameter(MaterialParameter.Emission);
            MaterialValue normal = valueIR.ExternalInput(
                MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent = valueIR.ExternalInput(
                MaterialExternalInput.GeometryTangentWS);
            MaterialValue coverage = valueIR.Swizzle(
                baseColor,
                MaterialSwizzleMask.W);
            var closureGraph = new ClosureExpressionGraph(valueIR);
            ClosureFeatureMask features = ClosureFeatureMask.BaseColorTexture
                | ClosureFeatureMask.NormalTexture
                | ClosureFeatureMask.MaskTexture;
            MaterialClosure baseSlab = closureGraph.Slab(
                baseColor,
                roughness,
                metallic,
                normal,
                tangent,
                features);
            MaterialClosure topSlab = closureGraph.Slab(
                topBaseColor,
                topRoughness,
                topMetallic,
                normal,
                tangent,
                features);
            MaterialClosure surfaceClosure = closureGraph.HorizontalMix(
                baseSlab,
                topSlab,
                layerWeight);
            return new MaterialIRModule(
                valueIR,
                new MaterialOutputRoots(
                    coverage,
                    alphaClipThreshold,
                    emission),
                closureGraph,
                surfaceClosure,
                ClosureTopologyBudget.Prototype,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit
                | MaterialShadingModelMask.Unlit);
        }

        private static MaterialValue BuildSampledBaseColor(
            MaterialValueIR valueIR,
            MaterialTextureResource textureResource,
            MaterialParameter colorParameter)
        {
            MaterialValue uv = valueIR.ExternalInput(MaterialExternalInput.UV0);
            MaterialValue texture = valueIR.TextureResource(textureResource);
            MaterialValue sample = valueIR.TextureSampleGrad(
                texture,
                uv,
                valueIR.Ddx(uv),
                valueIR.Ddy(uv));
            return valueIR.Multiply(
                sample,
                valueIR.Parameter(colorParameter));
        }

        private static void AssertArtifact(
            MaterialSurfaceHlslArtifact artifact,
            MaterialProgramTopologySpecialization expectedTopology,
            MaterialSurfaceHlslPhysicalContract expectedPhysicalContract,
            string expectedParameterType,
            uint expectedClosureCount,
            uint expectedLayerOperator,
            int expectedSampleCount)
        {
            Assert.That(artifact, Is.Not.Null);
            Assert.That(
                artifact.EntryPoint,
                Is.EqualTo($"VividEvaluateAOTSurface_{artifact.CodeHash:X16}"));
            Assert.That(artifact.Topology, Is.EqualTo(expectedTopology));
            Assert.That(artifact.PhysicalContract, Is.EqualTo(expectedPhysicalContract));
            Assert.That(artifact.Source, Does.Contain(
                $"VividAOTSurfaceProgramOutput {artifact.EntryPoint}("));
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

        private static void AssertDispatcherDeferredExportContract(
            string caseSource,
            uint shadingModelMask,
            uint litClass,
            uint sidecarAbi,
            uint policyFlags)
        {
            Assert.That(
                caseSource,
                Does.Contain("deferredExportContract.Version = 1u;"));
            Assert.That(
                caseSource,
                Does.Contain("deferredExportContract.SurfaceSummaryAbi = 1u;"));
            Assert.That(
                caseSource,
                Does.Contain(
                    $"deferredExportContract.DualSlabSidecarAbi = {sidecarAbi}u;"));
            Assert.That(
                caseSource,
                Does.Contain(
                    $"deferredExportContract.ShadingModelMask = {shadingModelMask}u;"));
            Assert.That(
                caseSource,
                Does.Contain($"deferredExportContract.LitClass = {litClass}u;"));
            Assert.That(
                caseSource,
                Does.Contain("deferredExportContract.ExpectedClosureCount = 1u;"));
            Assert.That(
                caseSource,
                Does.Contain("deferredExportContract.Topology = 0u;"));
            Assert.That(
                caseSource,
                Does.Contain("deferredExportContract.PayloadFlags = 3u;"));
            Assert.That(
                caseSource,
                Does.Contain(
                    $"deferredExportContract.PolicyFlags = {policyFlags}u;"));
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
