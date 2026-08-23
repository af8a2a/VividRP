using System;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class MaterialProgramPrototypeTests
    {
        [Test]
        public void MaterialValueIR_EmitsTypedDagAndDeduplicatesEquivalentValues()
        {
            var valueIR = new MaterialValueIR();

            MaterialValue uv = valueIR.ExternalInput(MaterialExternalInput.UV0);
            MaterialValue duplicateUV = valueIR.ExternalInput(MaterialExternalInput.UV0);
            MaterialValue uvDdx = valueIR.Ddx(uv);
            MaterialValue uvDdy = valueIR.Ddy(uv);
            MaterialValue texture = valueIR.TextureResource(MaterialTextureResource.BaseColor);
            MaterialValue sample = valueIR.TextureSampleGrad(texture, uv, uvDdx, uvDdy);
            MaterialValue baseColor = valueIR.Parameter(MaterialParameter.BaseColor);
            MaterialValue shadedColor = valueIR.Multiply(sample, baseColor);
            MaterialValue condition = valueIR.Constant(true);
            MaterialValue selectedColor = valueIR.Select(condition, shadedColor, sample);
            MaterialValue duplicateSelection = valueIR.Select(condition, shadedColor, sample);
            MaterialValue constantColor = valueIR.Constant(new float4(0.5f));
            MaterialValue duplicateConstantColor = valueIR.Constant(new float4(0.5f));

            Assert.That(duplicateUV, Is.EqualTo(uv));
            Assert.That(duplicateSelection, Is.EqualTo(selectedColor));
            Assert.That(duplicateConstantColor, Is.EqualTo(constantColor));
            Assert.That(valueIR.GetNode(sample).Opcode, Is.EqualTo(MaterialValueOpcode.TextureSampleGrad));
            Assert.That(valueIR.GetNode(sample).Type, Is.EqualTo(MaterialValueType.Float4));
            Assert.That(valueIR.GetNode(uvDdx).Opcode, Is.EqualTo(MaterialValueOpcode.Ddx));
            Assert.That(valueIR.GetNode(uvDdy).Opcode, Is.EqualTo(MaterialValueOpcode.Ddy));
            Assert.That(valueIR.GetNode(shadedColor).Opcode, Is.EqualTo(MaterialValueOpcode.Multiply));
            Assert.That(valueIR.NodeCount, Is.EqualTo(10));
            Assert.Throws<ArgumentException>(() => valueIR.Add(uv, sample));

            var foreignIR = new MaterialValueIR();
            MaterialValue foreignUV = foreignIR.ExternalInput(MaterialExternalInput.UV0);
            Assert.That(valueIR.Owns(foreignUV), Is.False);
            Assert.Throws<ArgumentException>(() => valueIR.Ddx(foreignUV));
        }

        [Test]
        public void MaterialIRModule_FreezesValuesAndProducesDeterministicHashAndDump()
        {
            CompiledMaterialProgram first =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            CompiledMaterialProgram second =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            CompiledMaterialProgram horizontal =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.HorizontalMix);
            CompiledMaterialProgram vertical =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.VerticalLayer);

            MaterialIRModule module = first.Module;
            Assert.That(module.Values.IsFrozen, Is.True);
            Assert.That(module.StructuralHash, Is.EqualTo(second.Module.StructuralHash));
            Assert.That(module.GetDebugDump(), Is.EqualTo(second.Module.GetDebugDump()));
            Assert.That(horizontal.Module.StructuralHash, Is.Not.EqualTo(vertical.Module.StructuralHash));
            Assert.That(
                horizontal.ProgramID,
                Is.EqualTo(VividMaterialProgramID.DualSlabHorizontalMix));
            Assert.That(
                vertical.ProgramID,
                Is.EqualTo(VividMaterialProgramID.DualSlabVerticalLayer));
            Assert.That(horizontal.ProgramID, Is.Not.EqualTo(vertical.ProgramID));
            Assert.That(module.Values.Owns(module.Outputs.CoverageValue), Is.True);
            Assert.That(module.Outputs.CoverageValue.Type, Is.EqualTo(MaterialValueType.Float4));
            Assert.That(
                module.MaterialFeatures,
                Is.EqualTo(
                    MaterialFeatureMask.AlphaClip
                    | MaterialFeatureMask.Emission
                    | MaterialFeatureMask.Unlit));
            Assert.That(
                module.Topology.FeatureMask,
                Is.EqualTo(
                    ClosureFeatureMask.BaseColorTexture
                    | ClosureFeatureMask.NormalTexture
                    | ClosureFeatureMask.MaskTexture));
            Assert.That(
                first.RuntimeData.CapabilityFlags,
                Is.EqualTo(
                    VividMaterialProgramCapabilities.LegacyGBufferExport
                    | VividMaterialProgramCapabilities.AlphaClip
                    | VividMaterialProgramCapabilities.Unlit));
            Assert.That(
                module.Outputs.AlphaClipThreshold.Type,
                Is.EqualTo(MaterialValueType.Float));
            Assert.That(module.GetDebugDump(), Does.Contain("external_input UV0"));
            Assert.That(module.GetDebugDump(), Does.Contain("texture_resource BaseColor"));
            Assert.That(module.GetDebugDump(), Does.Contain("coverage=%"));
            Assert.That(
                module.GetDebugDump(),
                Does.Contain("material_features=AlphaClip, Emission, Unlit"));
            var noMaterialFeatures = new MaterialIRModule(
                module.Values,
                module.Outputs,
                module.Topology,
                MaterialFeatureMask.None);
            Assert.That(noMaterialFeatures.StructuralHash, Is.Not.EqualTo(module.StructuralHash));
            Assert.Throws<InvalidOperationException>(() =>
                module.Values.Parameter(MaterialParameter.Roughness));

            var foreignValues = new MaterialValueIR();
            MaterialValue foreignCoverage =
                foreignValues.Parameter(MaterialParameter.BaseColor);
            Assert.Throws<ArgumentException>(() => new MaterialIRModule(
                module.Values,
                new MaterialOutputRoots(
                    foreignCoverage,
                    module.Outputs.AlphaClipThreshold),
                module.Topology,
                module.MaterialFeatures));
        }

        [Test]
        public void MaterialIRModule_StructuralHashIsCanonicalAcrossValueAllocationOrder()
        {
            MaterialIRModule first = BuildCanonicalHashModule(useAlternateValueOrder: false);
            MaterialIRModule reordered = BuildCanonicalHashModule(useAlternateValueOrder: true);

            Assert.That(first.Values.NodeCount, Is.Not.EqualTo(reordered.Values.NodeCount));
            Assert.That(first.GetDebugDump(), Is.Not.EqualTo(reordered.GetDebugDump()));
            Assert.That(first.StructuralHash, Is.EqualTo(reordered.StructuralHash));
        }

        [Test]
        public void CompilationContract_ProgramCatalog0To2HasFrozenAbi()
        {
            Assert.That(MaterialProgramContract.IRSchemaVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.SemanticHashVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.CompiledHashVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.CompilerVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.NativeTemplateBackendVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.RuntimeAbiVersion, Is.EqualTo(1u));
            Assert.That(GPUDrivenMaterialCompiler.RuntimeAbiVersion, Is.EqualTo(1u));
            Assert.That(GPUDrivenMaterialCompiler.ProgramVersion, Is.EqualTo(1u));
            Assert.That((uint) MaterialProgramBackendKind.NativeTemplate, Is.Zero);

            Assert.That((uint) VividMaterialProgramID.StandardSingleSlab, Is.Zero);
            Assert.That((uint) VividMaterialProgramID.DualSlabHorizontalMix, Is.EqualTo(1u));
            Assert.That((uint) VividMaterialProgramID.DualSlabVerticalLayer, Is.EqualTo(2u));
            Assert.That((uint) VividMaterialProgramID.Invalid, Is.EqualTo(uint.MaxValue));
            Assert.That((uint) VividMaterialCoverageProgramID.BaseColorAlpha, Is.Zero);
            Assert.That((uint) VividMaterialSurfaceProgramID.StandardSingleSlab, Is.Zero);
            Assert.That((uint) VividMaterialSurfaceProgramID.DualSlab, Is.EqualTo(1u));
            Assert.That((uint) VividMaterialTransportProgramID.None, Is.Zero);
            Assert.That((uint) VividMaterialParameterLayoutID.LegacyMaterialData, Is.Zero);
            Assert.That((uint) VividMaterialParameterLayoutID.DualSlabMaterialData, Is.EqualTo(1u));
            Assert.That((uint) VividMaterialResourceLayoutID.LegacySurfaceBinding, Is.Zero);
            Assert.That((uint) VividMaterialResourceLayoutID.DualSurfaceBinding, Is.EqualTo(1u));

            VividMaterialProgramData[] runtimePrograms =
                GPUDrivenMaterialCompiler.CreateRuntimeProgramTable();
            Assert.That(
                runtimePrograms.Length,
                Is.EqualTo(MaterialProgramContract.BuiltinProgramCount));

            var expectedRuntimePrograms = new[]
            {
                new uint[] { 1u, 0u, 0u, 0u, 0u, 0u, 7u, 0u },
                new uint[] { 1u, 0u, 1u, 0u, 1u, 1u, 7u, 0u },
                new uint[] { 1u, 0u, 1u, 0u, 1u, 1u, 7u, 0u },
            };
            var expectedSemanticHashes = new[]
            {
                0x28BD8897839120B1ul,
                0xA9FA2E736EB8F056ul,
                0xE0D2EB2E6A59C9D9ul,
            };
            var expectedCompiledHashes = new[]
            {
                0xD77FC4F037F599DCul,
                0xEB723FA3CC3807D4ul,
                0x9FF98369DD679A20ul,
            };

            for (int programIndex = 0; programIndex < runtimePrograms.Length; programIndex++)
            {
                var programID = (VividMaterialProgramID) (uint) programIndex;
                CompiledMaterialProgram program =
                    GPUDrivenMaterialCompiler.GetMaterialProgram(programID);
                Assert.That((uint) program.ProgramID, Is.EqualTo((uint) programIndex));
                AssertRuntimeProgramData(program.RuntimeData, expectedRuntimePrograms[programIndex]);
                AssertRuntimeProgramData(runtimePrograms[programIndex], expectedRuntimePrograms[programIndex]);
                Assert.That(
                    program.SemanticHash.IRSchemaVersion,
                    Is.EqualTo(MaterialProgramContract.IRSchemaVersion));
                Assert.That(
                    program.SemanticHash.Version,
                    Is.EqualTo(MaterialProgramContract.SemanticHashVersion));
                Assert.That(
                    program.SemanticHash.Value,
                    Is.EqualTo(expectedSemanticHashes[programIndex]));
                Assert.That(program.Module.StructuralHash, Is.EqualTo(program.SemanticHash.Value));
                Assert.That(
                    program.CompiledHash.Version,
                    Is.EqualTo(MaterialProgramContract.CompiledHashVersion));
                Assert.That(
                    program.CompiledHash.Value,
                    Is.EqualTo(expectedCompiledHashes[programIndex]));

                if (programIndex == 0)
                    AssertStandardMaterialLayout(program);
                else
                    AssertDualSlabMaterialLayout(program);
            }

            CollectionAssert.AllItemsAreUnique(expectedCompiledHashes);
        }

        [Test]
        public void CompilationContract_CanonicalModulesShareCompiledIdentity()
        {
            MaterialIRModule firstModule =
                BuildCanonicalHashModule(useAlternateValueOrder: false);
            MaterialIRModule reorderedModule =
                BuildCanonicalHashModule(useAlternateValueOrder: true);
            CompiledMaterialProgram first = CompiledMaterialProgram.Compile(
                firstModule,
                MaterialProgramContract.RuntimeAbiVersion);
            CompiledMaterialProgram reordered = CompiledMaterialProgram.Compile(
                reorderedModule,
                MaterialProgramContract.RuntimeAbiVersion);

            Assert.That(first.SemanticHash, Is.EqualTo(reordered.SemanticHash));
            Assert.That(first.CompiledHash, Is.EqualTo(reordered.CompiledHash));
            Assert.That(first.ProgramID, Is.EqualTo(reordered.ProgramID));
        }

        [Test]
        public void CompilationContract_ProgramIdIsNotCompiledIdentity()
        {
            CompiledMaterialProgram prototype =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    MaterialProgramContract.RuntimeAbiVersion);
            MaterialIRModule prototypeModule = prototype.Module;
            var withoutEmission = new MaterialIRModule(
                prototypeModule.Values,
                prototypeModule.Outputs,
                prototypeModule.Topology,
                prototypeModule.MaterialFeatures & ~MaterialFeatureMask.Emission);
            CompiledMaterialProgram compiledWithoutEmission =
                CompiledMaterialProgram.Compile(
                    withoutEmission,
                    MaterialProgramContract.RuntimeAbiVersion);

            Assert.That(compiledWithoutEmission.ProgramID, Is.EqualTo(prototype.ProgramID));
            AssertRuntimeProgramData(
                compiledWithoutEmission.RuntimeData,
                new uint[] { 1u, 0u, 0u, 0u, 0u, 0u, 7u, 0u });
            Assert.That(compiledWithoutEmission.SemanticHash, Is.Not.EqualTo(prototype.SemanticHash));
            Assert.That(compiledWithoutEmission.CompiledHash, Is.Not.EqualTo(prototype.CompiledHash));
        }

        [Test]
        public void CompilationContract_RejectsUnsupportedRuntimeAbiVersion()
        {
            MaterialIRModule module = BuildCanonicalHashModule(useAlternateValueOrder: false);

            ArgumentOutOfRangeException exception =
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    CompiledMaterialProgram.Compile(
                        module,
                        MaterialProgramContract.RuntimeAbiVersion + 1u));

            Assert.That(exception.ParamName, Is.EqualTo("programVersion"));
            Assert.That(exception.Message, Does.Contain("Only material runtime ABI version 1"));
        }

        [Test]
        public void CoverageLowering_ConsumesOnlyCoverageRootsForProgram0AndProgram1()
        {
            CompiledMaterialProgram standard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            CompiledMaterialProgram dualSlab =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.VerticalLayer);

            AssertCoverageRequirements(standard);
            AssertCoverageRequirements(dualSlab);
            Assert.That(
                standard.CoverageProgram.ValueSlice.Contains(
                    standard.Module.Topology.Slabs[0].Roughness),
                Is.False);
            Assert.That(
                standard.CoverageProgram.ValueSlice.Contains(
                    standard.Module.Topology.Slabs[0].Metallic),
                Is.False);
            Assert.That(
                dualSlab.CoverageProgram.ValueSlice.Contains(
                    dualSlab.Module.Topology.Slabs[1].BaseColor),
                Is.False);
            Assert.That(
                dualSlab.CoverageProgram.ValueSlice.Contains(
                    dualSlab.Module.Topology.Operators[0].Weight),
                Is.False);

            CollectionAssert.AreEqual(
                GetValueSliceSignature(standard.CoverageProgram.ValueSlice),
                GetValueSliceSignature(dualSlab.CoverageProgram.ValueSlice));
        }

        [Test]
        public void CoverageLowering_RejectsUnmappedCoverageValueIR()
        {
            MaterialIRModule module = BuildUnsupportedCoverageModule();

            Assert.Throws<NotSupportedException>(() =>
                CompiledMaterialProgram.Compile(
                    module,
                    GPUDrivenMaterialCompiler.ProgramVersion));
        }

        [Test]
        public void SurfaceMatcher_ConsumesSlabTopologyForProgram0AndProgram1()
        {
            CompiledMaterialProgram standard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            CompiledMaterialProgram horizontal =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.HorizontalMix);
            CompiledMaterialProgram vertical =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.VerticalLayer);

            AssertStandardSurfaceRequirements(standard);
            AssertDualSlabSurfaceRequirements(horizontal);
            AssertDualSlabSurfaceRequirements(vertical);
            CollectionAssert.AreEqual(
                GetValueSliceSignature(horizontal.SurfaceProgram.ValueSlice),
                GetValueSliceSignature(vertical.SurfaceProgram.ValueSlice));
        }

        [Test]
        public void SurfaceMatcher_RejectsUnmappedSlabValueIR()
        {
            MaterialIRModule module = BuildUnsupportedSurfaceModule();

            Assert.Throws<NotSupportedException>(() =>
                SurfaceProgramMatcher.Compile(module));
        }

        [Test]
        public void SurfaceMatcher_RejectsUnsupportedClosureOperator()
        {
            CompiledMaterialProgram prototype =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.HorizontalMix);
            MaterialIRModule prototypeModule = prototype.Module;
            ClosureTopology prototypeTopology = prototypeModule.Topology;
            var topology = new ClosureTopology(
                prototypeModule.Values,
                prototypeTopology.NormalBases.ToArray(),
                prototypeTopology.Slabs.ToArray(),
                new[]
                {
                    new ClosureOperator(
                        (ClosureOperatorKind) 99,
                        backgroundSlabIndex: 0,
                        foregroundSlabIndex: 1,
                        weight: prototypeTopology.Operators[0].Weight),
                },
                ClosureTopologyBudget.Prototype);
            var module = new MaterialIRModule(
                prototypeModule.Values,
                prototypeModule.Outputs,
                topology,
                prototypeModule.MaterialFeatures);

            Assert.Throws<NotSupportedException>(() =>
                SurfaceProgramMatcher.Compile(module));
        }

        [Test]
        public void LayoutLowering_MapsProgram0AndProgram1RequirementsToStableAbi()
        {
            CompiledMaterialProgram standard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            CompiledMaterialProgram dualSlab =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.VerticalLayer);

            AssertStandardMaterialLayout(standard);
            AssertDualSlabMaterialLayout(dualSlab);
        }

        [Test]
        public void LayoutLowering_RejectsRequirementsFromDifferentSurfaceAbi()
        {
            CompiledMaterialProgram standard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            CompiledMaterialProgram dualSlab =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.HorizontalMix);
            var mismatchedSurfaceProgram = new CompiledSurfaceProgram(
                VividMaterialSurfaceProgramID.StandardSingleSlab,
                dualSlab.SurfaceProgram.ValueSlice,
                dualSlab.SurfaceProgram.Requirements);

            Assert.Throws<NotSupportedException>(() =>
                MaterialLayoutLowerer.Compile(
                    standard.CoverageProgram,
                    mismatchedSurfaceProgram));
        }

        [Test]
        public void CostModel_ReportsDeterministicProgram0AndProgram1StructuralCosts()
        {
            CompiledMaterialProgram firstStandard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            CompiledMaterialProgram secondStandard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            CompiledMaterialProgram horizontal =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.HorizontalMix);
            CompiledMaterialProgram vertical =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.VerticalLayer);

            MaterialProgramCost standardCost = firstStandard.Diagnostics.Cost;
            AssertStageCost(
                standardCost.Coverage,
                nodes: 8,
                textureSamples: 1,
                derivatives: 2,
                arithmeticNodes: 1,
                parameters: 2,
                textureResources: 1,
                externalInputs: 1);
            AssertStageCost(
                standardCost.Surface,
                nodes: 11,
                textureSamples: 1,
                derivatives: 2,
                arithmeticNodes: 1,
                parameters: 3,
                textureResources: 1,
                externalInputs: 3);
            AssertStageCost(
                standardCost.Combined,
                nodes: 12,
                textureSamples: 1,
                derivatives: 2,
                arithmeticNodes: 1,
                parameters: 4,
                textureResources: 1,
                externalInputs: 3);
            Assert.That(standardCost.ClosureCount, Is.EqualTo(1));
            Assert.That(standardCost.OperatorCount, Is.Zero);
            Assert.That(standardCost.WorstCaseCoverageTextureSamples, Is.EqualTo(1));
            Assert.That(standardCost.WorstCaseSurfaceTextureSamples, Is.EqualTo(3));
            Assert.That(standardCost.WorstCaseTotalTextureSamples, Is.EqualTo(4));
            Assert.That(standardCost.ParameterBindingCount, Is.EqualTo(10));
            Assert.That(standardCost.ResourceBindingCount, Is.EqualTo(3));
            Assert.That(standardCost.ParameterBytes, Is.EqualTo(128));
            Assert.That(standardCost.ResourceBindingRecords, Is.EqualTo(1));

            MaterialProgramCost dualCost = vertical.Diagnostics.Cost;
            AssertStageCost(
                dualCost.Coverage,
                nodes: 8,
                textureSamples: 1,
                derivatives: 2,
                arithmeticNodes: 1,
                parameters: 2,
                textureResources: 1,
                externalInputs: 1);
            AssertStageCost(
                dualCost.Surface,
                nodes: 18,
                textureSamples: 2,
                derivatives: 2,
                arithmeticNodes: 2,
                parameters: 7,
                textureResources: 2,
                externalInputs: 3);
            AssertStageCost(
                dualCost.Combined,
                nodes: 19,
                textureSamples: 2,
                derivatives: 2,
                arithmeticNodes: 2,
                parameters: 8,
                textureResources: 2,
                externalInputs: 3);
            Assert.That(dualCost.ClosureCount, Is.EqualTo(2));
            Assert.That(dualCost.OperatorCount, Is.EqualTo(1));
            Assert.That(dualCost.WorstCaseCoverageTextureSamples, Is.EqualTo(1));
            Assert.That(dualCost.WorstCaseSurfaceTextureSamples, Is.EqualTo(6));
            Assert.That(dualCost.WorstCaseTotalTextureSamples, Is.EqualTo(7));
            Assert.That(dualCost.ParameterBindingCount, Is.EqualTo(20));
            Assert.That(dualCost.ResourceBindingCount, Is.EqualTo(6));
            Assert.That(dualCost.ParameterBytes, Is.EqualTo(192));
            Assert.That(dualCost.ResourceBindingRecords, Is.EqualTo(2));

            string standardDump = firstStandard.Diagnostics.GetDebugDump();
            Assert.That(firstStandard.Diagnostics.IsWithinBudget, Is.True);
            Assert.That(vertical.Diagnostics.IsWithinBudget, Is.True);
            Assert.That(
                standardDump,
                Is.EqualTo(secondStandard.Diagnostics.GetDebugDump()));
            Assert.That(
                horizontal.Diagnostics.GetDebugDump(),
                Is.EqualTo(vertical.Diagnostics.GetDebugDump()));
            Assert.That(standardDump, Does.Contain("cost_model=lowered_program_worst_case_v2"));
            Assert.That(
                standardDump,
                Does.Contain("lowered texture_samples coverage=1 surface=3 total=4"));
            Assert.That(standardDump, Does.Contain("status=ok"));
            Assert.That(firstStandard.Diagnostics.Entries, Is.Empty);
        }

        [Test]
        public void CostBudget_RejectsProgramWhenCombinedNodeLimitIsExceeded()
        {
            CompiledMaterialProgram prototype =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            var budget = new MaterialProgramCostBudget(
                maxCombinedValueNodes: 11,
                maxCoverageTextureSamples: 1,
                maxSurfaceTextureSamples: 6,
                maxTotalTextureSamples: 7,
                maxParameterBindings: 20,
                maxResourceBindings: 6,
                maxClosures: 2,
                maxOperators: 1,
                maxParameterBytes: 192,
                maxResourceBindingRecords: 2);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                CompiledMaterialProgram.Compile(
                    prototype.Module,
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    budget));

            Assert.That(exception.Message, Does.Contain("status=over_budget"));
            Assert.That(exception.Message, Does.Contain("MPC1001"));
            Assert.That(
                exception.Message,
                Does.Contain("combined value nodes cost 12 exceeds budget 11"));
        }

        [Test]
        public void CostBudget_RejectsProgramWhenLoweredLimitsAreExceeded()
        {
            CompiledMaterialProgram prototype =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            var budget = new MaterialProgramCostBudget(
                maxCombinedValueNodes: 24,
                maxCoverageTextureSamples: 1,
                maxSurfaceTextureSamples: 2,
                maxTotalTextureSamples: 7,
                maxParameterBindings: 9,
                maxResourceBindings: 2,
                maxClosures: 2,
                maxOperators: 1,
                maxParameterBytes: 192,
                maxResourceBindingRecords: 2);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                CompiledMaterialProgram.Compile(
                    prototype.Module,
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    budget));

            Assert.That(exception.Message, Does.Contain("MPC1003"));
            Assert.That(exception.Message, Does.Contain("MPC1005"));
            Assert.That(exception.Message, Does.Contain("MPC1006"));
            Assert.That(
                exception.Message,
                Does.Contain("surface texture samples cost 3 exceeds budget 2"));
            Assert.That(
                exception.Message,
                Does.Contain("parameter bindings cost 10 exceeds budget 9"));
            Assert.That(
                exception.Message,
                Does.Contain("resource bindings cost 3 exceeds budget 2"));
        }

        [Test]
        public void CompileStandardSingleSlab_ProducesSingleClosureProgramPrototype()
        {
            var firstProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var secondProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            try
            {
                GPUDrivenCompiledMaterialInstance first =
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(firstProxy, 0u, 2u);
                GPUDrivenCompiledMaterialInstance second =
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(secondProxy, 1u, 4u);

                CompiledMaterialProgram program = first.MaterialProgram;
                ClosureTopology topology = program.Module.Topology;
                Assert.That(ReferenceEquals(program, second.MaterialProgram), Is.True);
                Assert.That(program.ProgramID, Is.EqualTo(VividMaterialProgramID.StandardSingleSlab));
                Assert.That(topology.ClosureCount, Is.EqualTo(1));
                Assert.That(topology.OperatorCount, Is.Zero);
                Assert.That(topology.NormalBases.Count, Is.EqualTo(1));
                Assert.That(topology.Slabs[0].IsTop, Is.True);
                Assert.That(topology.Slabs[0].IsBottom, Is.True);
                Assert.That(topology.IsWithinBudget, Is.True);
                Assert.That(
                    program.RuntimeData.SurfaceProgramID,
                    Is.EqualTo(VividMaterialSurfaceProgramID.StandardSingleSlab));
                Assert.That(
                    program.RuntimeData.ParameterLayoutID,
                    Is.EqualTo(VividMaterialParameterLayoutID.LegacyMaterialData));
                Assert.That(
                    program.Module.Values.Nodes.Any(
                        node => node.Opcode == MaterialValueOpcode.TextureSampleGrad),
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(secondProxy);
                UnityEngine.Object.DestroyImmediate(firstProxy);
            }
        }

        [Test]
        public void CompileDualSlab_AssignsTopologySpecificStableProgramID()
        {
            var baseProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var topProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var definition =
                ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();
            try
            {
                baseProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                definition.TopSlab = topProxy;
                definition.Operator = VividDualSlabOperator.VerticalLayer;
                baseProxy.DualSlabDefinition = definition;

                GPUDrivenCompiledMaterialInstance compiled =
                    GPUDrivenMaterialCompiler.CompileDualSlab(baseProxy, 3u, 6u);
                CompiledMaterialProgram program = compiled.MaterialProgram;
                ClosureTopology topology = program.Module.Topology;

                Assert.That(
                    program.ProgramID,
                    Is.EqualTo(VividMaterialProgramID.DualSlabVerticalLayer));
                Assert.That(topology.ClosureCount, Is.EqualTo(2));
                Assert.That(topology.OperatorCount, Is.EqualTo(1));
                Assert.That(
                    topology.Operators[0].Kind,
                    Is.EqualTo(ClosureOperatorKind.VerticalLayer));
                Assert.That(topology.Slabs[0].IsBottom, Is.True);
                Assert.That(topology.Slabs[0].IsTop, Is.False);
                Assert.That(topology.Slabs[1].IsTop, Is.True);
                Assert.That(topology.Slabs[1].IsBottom, Is.False);
                Assert.That(topology.Slabs[0].NormalBasisIndex, Is.Zero);
                Assert.That(topology.Slabs[1].NormalBasisIndex, Is.Zero);
                Assert.That(topology.IsWithinBudget, Is.True);
                Assert.That(
                    program.RuntimeData.SurfaceProgramID,
                    Is.EqualTo(VividMaterialSurfaceProgramID.DualSlab));
                Assert.That(
                    program.RuntimeData.ParameterLayoutID,
                    Is.EqualTo(VividMaterialParameterLayoutID.DualSlabMaterialData));
                Assert.That(compiled.RuntimeHeader.ProgramID, Is.EqualTo(program.ProgramID));
                Assert.That(
                    compiled.DualSlabMaterialData.LayerOperator,
                    Is.EqualTo(VividDualSlabOperator.VerticalLayer));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(topProxy);
                UnityEngine.Object.DestroyImmediate(baseProxy);
            }
        }

        [Test]
        public void ClosureTopology_RejectsDualSlabWhenBudgetAllowsOnlyOneClosure()
        {
            CompiledMaterialProgram prototype =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.HorizontalMix);
            var budget = new ClosureTopologyBudget(maxClosureCount: 1, maxOperatorCount: 0);

            Assert.That(budget.Allows(1, 0), Is.True);
            Assert.That(budget.Allows(2, 1), Is.False);
            Assert.Throws<InvalidOperationException>(() => new ClosureTopology(
                prototype.Module.Values,
                new[] { prototype.Module.Topology.NormalBases[0] },
                new[] { prototype.Module.Topology.Slabs[0], prototype.Module.Topology.Slabs[1] },
                new[] { prototype.Module.Topology.Operators[0] },
                budget));
        }

        private static void AssertCoverageRequirements(CompiledMaterialProgram program)
        {
            CompiledCoverageProgram coverage = program.CoverageProgram;
            Assert.That(
                coverage.ProgramID,
                Is.EqualTo(VividMaterialCoverageProgramID.BaseColorAlpha));
            Assert.That(program.RuntimeData.CoverageProgramID, Is.EqualTo(coverage.ProgramID));
            Assert.That(coverage.ValueSlice.NodeCount, Is.EqualTo(8));
            Assert.That(
                coverage.ValueSlice.Contains(program.Module.Outputs.CoverageValue),
                Is.True);
            Assert.That(
                coverage.ValueSlice.Contains(program.Module.Outputs.AlphaClipThreshold),
                Is.True);
            CollectionAssert.AreEqual(
                new[] { MaterialParameter.BaseColor, MaterialParameter.AlphaClipThreshold },
                coverage.RequiredParameters);
            CollectionAssert.AreEqual(
                new[] { MaterialTextureResource.BaseColor },
                coverage.RequiredTextureResources);
            CollectionAssert.AreEqual(
                new[] { MaterialExternalInput.UV0 },
                coverage.RequiredExternalInputs);
        }

        private static void AssertStandardSurfaceRequirements(
            CompiledMaterialProgram program)
        {
            CompiledSurfaceProgram surface = program.SurfaceProgram;
            Assert.That(
                surface.ProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.StandardSingleSlab));
            Assert.That(program.RuntimeData.SurfaceProgramID, Is.EqualTo(surface.ProgramID));
            Assert.That(surface.ValueSlice.NodeCount, Is.EqualTo(11));
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Topology.Slabs[0].Roughness),
                Is.True);
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Topology.NormalBases[0].Normal),
                Is.True);
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Outputs.AlphaClipThreshold),
                Is.False);
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialParameter.BaseColor,
                    MaterialParameter.Roughness,
                    MaterialParameter.Metallic,
                },
                surface.RequiredParameters);
            CollectionAssert.AreEqual(
                new[] { MaterialTextureResource.BaseColor },
                surface.RequiredTextureResources);
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialExternalInput.UV0,
                    MaterialExternalInput.GeometryNormalWS,
                    MaterialExternalInput.GeometryTangentWS,
                },
                surface.RequiredExternalInputs);
        }

        private static void AssertDualSlabSurfaceRequirements(
            CompiledMaterialProgram program)
        {
            CompiledSurfaceProgram surface = program.SurfaceProgram;
            Assert.That(
                surface.ProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.DualSlab));
            Assert.That(program.RuntimeData.SurfaceProgramID, Is.EqualTo(surface.ProgramID));
            Assert.That(surface.ValueSlice.NodeCount, Is.EqualTo(18));
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Topology.Slabs[1].BaseColor),
                Is.True);
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Topology.Operators[0].Weight),
                Is.True);
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Outputs.AlphaClipThreshold),
                Is.False);
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialParameter.BaseColor,
                    MaterialParameter.TopBaseColor,
                    MaterialParameter.Roughness,
                    MaterialParameter.TopRoughness,
                    MaterialParameter.Metallic,
                    MaterialParameter.TopMetallic,
                    MaterialParameter.LayerWeight,
                },
                surface.RequiredParameters);
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialTextureResource.BaseColor,
                    MaterialTextureResource.TopBaseColor,
                },
                surface.RequiredTextureResources);
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialExternalInput.UV0,
                    MaterialExternalInput.GeometryNormalWS,
                    MaterialExternalInput.GeometryTangentWS,
                },
                surface.RequiredExternalInputs);
        }

        private static void AssertStandardMaterialLayout(CompiledMaterialProgram program)
        {
            CompiledMaterialLayout layout = program.MaterialLayout;
            Assert.That(
                layout.ParameterLayout.LayoutID,
                Is.EqualTo(VividMaterialParameterLayoutID.LegacyMaterialData));
            Assert.That(
                layout.ResourceLayout.LayoutID,
                Is.EqualTo(VividMaterialResourceLayoutID.LegacySurfaceBinding));
            Assert.That(
                program.RuntimeData.ParameterLayoutID,
                Is.EqualTo(layout.ParameterLayout.LayoutID));
            Assert.That(
                program.RuntimeData.ResourceLayoutID,
                Is.EqualTo(layout.ResourceLayout.LayoutID));
            Assert.That(layout.ParameterLayout.Stride, Is.EqualTo(128));
            Assert.That(layout.ResourceLayout.RecordStride, Is.EqualTo(32));
            Assert.That(layout.ResourceLayout.RecordCount, Is.EqualTo(1));
            Assert.That(layout.ParameterLayout.Bindings.Count, Is.EqualTo(10));
            Assert.That(layout.ResourceLayout.Bindings.Count, Is.EqualTo(3));
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialParameter.BaseColor,
                    MaterialParameter.Roughness,
                    MaterialParameter.Metallic,
                    MaterialParameter.AlphaClipThreshold,
                },
                layout.Requirements.Parameters);
            CollectionAssert.AreEqual(
                new[] { MaterialTextureResource.BaseColor },
                layout.Requirements.TextureResources);
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialExternalInput.UV0,
                    MaterialExternalInput.GeometryNormalWS,
                    MaterialExternalInput.GeometryTangentWS,
                },
                layout.Requirements.ExternalInputs);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseColor,
                MaterialLayoutValueType.Float4,
                byteOffset: 0);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseTextureTilingOffset,
                MaterialLayoutValueType.Float4,
                byteOffset: 16);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.Emission,
                MaterialLayoutValueType.Float4,
                byteOffset: 32);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseMetallicSmoothnessRemap,
                MaterialLayoutValueType.Float4,
                byteOffset: 48);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseAmbientOcclusionRemap,
                MaterialLayoutValueType.Float4,
                byteOffset: 64);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseNormalsStrength,
                MaterialLayoutValueType.Float,
                byteOffset: 84);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.Roughness,
                MaterialLayoutValueType.Float,
                byteOffset: 88);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.Metallic,
                MaterialLayoutValueType.Float,
                byteOffset: 92);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseMaskMode,
                MaterialLayoutValueType.UInt,
                byteOffset: 120);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.AlphaClipThreshold,
                MaterialLayoutValueType.Float,
                byteOffset: 116);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.BaseColor,
                recordOffset: 0,
                byteOffset: 0);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.BaseNormal,
                recordOffset: 0,
                byteOffset: 4);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.BaseMask,
                recordOffset: 0,
                byteOffset: 8);
        }

        private static void AssertDualSlabMaterialLayout(CompiledMaterialProgram program)
        {
            CompiledMaterialLayout layout = program.MaterialLayout;
            Assert.That(
                layout.ParameterLayout.LayoutID,
                Is.EqualTo(VividMaterialParameterLayoutID.DualSlabMaterialData));
            Assert.That(
                layout.ResourceLayout.LayoutID,
                Is.EqualTo(VividMaterialResourceLayoutID.DualSurfaceBinding));
            Assert.That(
                program.RuntimeData.ParameterLayoutID,
                Is.EqualTo(layout.ParameterLayout.LayoutID));
            Assert.That(
                program.RuntimeData.ResourceLayoutID,
                Is.EqualTo(layout.ResourceLayout.LayoutID));
            Assert.That(layout.ParameterLayout.Stride, Is.EqualTo(192));
            Assert.That(layout.ResourceLayout.RecordStride, Is.EqualTo(32));
            Assert.That(layout.ResourceLayout.RecordCount, Is.EqualTo(2));
            Assert.That(layout.ParameterLayout.Bindings.Count, Is.EqualTo(20));
            Assert.That(layout.ResourceLayout.Bindings.Count, Is.EqualTo(6));
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialParameter.BaseColor,
                    MaterialParameter.TopBaseColor,
                    MaterialParameter.Roughness,
                    MaterialParameter.TopRoughness,
                    MaterialParameter.Metallic,
                    MaterialParameter.TopMetallic,
                    MaterialParameter.LayerWeight,
                    MaterialParameter.AlphaClipThreshold,
                },
                layout.Requirements.Parameters);
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialTextureResource.BaseColor,
                    MaterialTextureResource.TopBaseColor,
                },
                layout.Requirements.TextureResources);
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialExternalInput.UV0,
                    MaterialExternalInput.GeometryNormalWS,
                    MaterialExternalInput.GeometryTangentWS,
                },
                layout.Requirements.ExternalInputs);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseColor,
                MaterialLayoutValueType.Float4,
                byteOffset: 0);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseTextureTilingOffset,
                MaterialLayoutValueType.Float4,
                byteOffset: 16);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseMetallicSmoothnessRemap,
                MaterialLayoutValueType.Float4,
                byteOffset: 32);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseAmbientOcclusionRemap,
                MaterialLayoutValueType.Float4,
                byteOffset: 48);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseNormalsStrength,
                MaterialLayoutValueType.Float,
                byteOffset: 64);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.Roughness,
                MaterialLayoutValueType.Float,
                byteOffset: 68);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.Metallic,
                MaterialLayoutValueType.Float,
                byteOffset: 72);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.BaseMaskMode,
                MaterialLayoutValueType.UInt,
                byteOffset: 76);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.TopBaseColor,
                MaterialLayoutValueType.Float4,
                byteOffset: 80);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.TopTextureTilingOffset,
                MaterialLayoutValueType.Float4,
                byteOffset: 96);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.TopMetallicSmoothnessRemap,
                MaterialLayoutValueType.Float4,
                byteOffset: 112);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.TopAmbientOcclusionRemap,
                MaterialLayoutValueType.Float4,
                byteOffset: 128);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.TopNormalsStrength,
                MaterialLayoutValueType.Float,
                byteOffset: 144);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.TopRoughness,
                MaterialLayoutValueType.Float,
                byteOffset: 148);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.TopMetallic,
                MaterialLayoutValueType.Float,
                byteOffset: 152);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.TopMaskMode,
                MaterialLayoutValueType.UInt,
                byteOffset: 156);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.Emission,
                MaterialLayoutValueType.Float4,
                byteOffset: 160);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.LayerOperator,
                MaterialLayoutValueType.UInt,
                byteOffset: 176);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.LayerWeight,
                MaterialLayoutValueType.Float,
                byteOffset: 180);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialRuntimeParameter.AlphaClipThreshold,
                MaterialLayoutValueType.Float,
                byteOffset: 184);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.BaseColor,
                recordOffset: 0,
                byteOffset: 0);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.BaseNormal,
                recordOffset: 0,
                byteOffset: 4);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.BaseMask,
                recordOffset: 0,
                byteOffset: 8);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.TopBaseColor,
                recordOffset: 1,
                byteOffset: 0);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.TopNormal,
                recordOffset: 1,
                byteOffset: 4);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.TopMask,
                recordOffset: 1,
                byteOffset: 8);
        }

        private static void AssertParameterBinding(
            CompiledParameterLayout layout,
            MaterialRuntimeParameter parameter,
            MaterialLayoutValueType type,
            int byteOffset)
        {
            Assert.That(
                layout.TryGetBinding(
                    parameter,
                    out MaterialParameterLayoutBinding binding),
                Is.True);
            Assert.That(binding.Type, Is.EqualTo(type));
            Assert.That(binding.ByteOffset, Is.EqualTo(byteOffset));
        }

        private static void AssertResourceBinding(
            CompiledResourceLayout layout,
            MaterialTextureResource resource,
            int recordOffset,
            int byteOffset)
        {
            Assert.That(
                layout.TryGetBinding(
                    resource,
                    out MaterialResourceLayoutBinding binding),
                Is.True);
            Assert.That(binding.RecordOffset, Is.EqualTo(recordOffset));
            Assert.That(binding.ByteOffset, Is.EqualTo(byteOffset));
        }

        private static void AssertStageCost(
            in MaterialStageCost cost,
            int nodes,
            int textureSamples,
            int derivatives,
            int arithmeticNodes,
            int parameters,
            int textureResources,
            int externalInputs)
        {
            Assert.That(cost.ValueNodeCount, Is.EqualTo(nodes));
            Assert.That(cost.TextureSampleCount, Is.EqualTo(textureSamples));
            Assert.That(cost.DerivativeCount, Is.EqualTo(derivatives));
            Assert.That(cost.ArithmeticNodeCount, Is.EqualTo(arithmeticNodes));
            Assert.That(cost.ParameterCount, Is.EqualTo(parameters));
            Assert.That(cost.TextureResourceCount, Is.EqualTo(textureResources));
            Assert.That(cost.ExternalInputCount, Is.EqualTo(externalInputs));
        }

        private static void AssertRuntimeProgramData(
            in VividMaterialProgramData runtimeData,
            uint[] expected)
        {
            CollectionAssert.AreEqual(
                expected,
                new[]
                {
                    runtimeData.Version,
                    (uint) runtimeData.CoverageProgramID,
                    (uint) runtimeData.SurfaceProgramID,
                    (uint) runtimeData.TransportProgramID,
                    (uint) runtimeData.ParameterLayoutID,
                    (uint) runtimeData.ResourceLayoutID,
                    (uint) runtimeData.CapabilityFlags,
                    (uint) runtimeData.ExecutionClass,
                });
        }

        private static string[] GetValueSliceSignature(MaterialValueSlice valueSlice)
        {
            return valueSlice.NodeIndices.Select(index =>
            {
                MaterialValueNode node = valueSlice.Values.Nodes[index];
                return $"{node.Opcode}:{node.Type}:{node.Semantic}";
            }).ToArray();
        }

        private static MaterialIRModule BuildUnsupportedCoverageModule()
        {
            var valueIR = new MaterialValueIR();
            MaterialValue baseColor = valueIR.Parameter(MaterialParameter.BaseColor);
            MaterialValue roughness = valueIR.Parameter(MaterialParameter.Roughness);
            MaterialValue metallic = valueIR.Parameter(MaterialParameter.Metallic);
            MaterialValue alphaClipThreshold =
                valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
            MaterialValue coverageValue = valueIR.Constant(new float4(1.0f));
            var normalBases = new[]
            {
                new ClosureNormalBasis(
                    valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS),
                    valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS)),
            };
            var slabs = new[]
            {
                new ClosureSlab(
                    baseColor,
                    roughness,
                    metallic,
                    normalBasisIndex: 0,
                    features: ClosureFeatureMask.None,
                    isTop: true,
                    isBottom: true),
            };
            var topology = new ClosureTopology(
                valueIR,
                normalBases,
                slabs,
                Array.Empty<ClosureOperator>(),
                ClosureTopologyBudget.Prototype);
            return new MaterialIRModule(
                valueIR,
                new MaterialOutputRoots(coverageValue, alphaClipThreshold),
                topology,
                MaterialFeatureMask.AlphaClip);
        }

        private static MaterialIRModule BuildCanonicalHashModule(bool useAlternateValueOrder)
        {
            var valueIR = new MaterialValueIR();
            MaterialValue baseColor;
            MaterialValue roughness;
            MaterialValue metallic;
            MaterialValue alphaClipThreshold;
            MaterialValue normal;
            MaterialValue tangent;

            if (useAlternateValueOrder)
            {
                roughness = valueIR.Parameter(MaterialParameter.Roughness);
                metallic = valueIR.Parameter(MaterialParameter.Metallic);
                alphaClipThreshold = valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
                normal = valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS);
                tangent = valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS);
                valueIR.Constant(123.0f);
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
                normal = valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS);
                tangent = valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS);
                roughness = valueIR.Parameter(MaterialParameter.Roughness);
                metallic = valueIR.Parameter(MaterialParameter.Metallic);
                alphaClipThreshold = valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
            }

            var topology = new ClosureTopology(
                valueIR,
                new[] { new ClosureNormalBasis(normal, tangent) },
                new[]
                {
                    new ClosureSlab(
                        baseColor,
                        roughness,
                        metallic,
                        normalBasisIndex: 0,
                        features:
                            ClosureFeatureMask.BaseColorTexture
                            | ClosureFeatureMask.NormalTexture
                            | ClosureFeatureMask.MaskTexture,
                        isTop: true,
                        isBottom: true),
                },
                Array.Empty<ClosureOperator>(),
                ClosureTopologyBudget.Prototype);
            return new MaterialIRModule(
                valueIR,
                new MaterialOutputRoots(baseColor, alphaClipThreshold),
                topology,
                MaterialFeatureMask.AlphaClip
                | MaterialFeatureMask.Emission
                | MaterialFeatureMask.Unlit);
        }

        private static MaterialIRModule BuildUnsupportedSurfaceModule()
        {
            var valueIR = new MaterialValueIR();
            MaterialValue baseColor = BuildSampledBaseColor(
                valueIR,
                MaterialTextureResource.BaseColor,
                MaterialParameter.BaseColor);
            MaterialValue roughness = valueIR.Constant(0.5f);
            MaterialValue metallic = valueIR.Parameter(MaterialParameter.Metallic);
            MaterialValue alphaClipThreshold =
                valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
            var normalBases = new[]
            {
                new ClosureNormalBasis(
                    valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS),
                    valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS)),
            };
            var topology = new ClosureTopology(
                valueIR,
                normalBases,
                new[]
                {
                    new ClosureSlab(
                        baseColor,
                        roughness,
                        metallic,
                        normalBasisIndex: 0,
                        features: ClosureFeatureMask.BaseColorTexture,
                        isTop: true,
                        isBottom: true),
                },
                Array.Empty<ClosureOperator>(),
                ClosureTopologyBudget.Prototype);
            return new MaterialIRModule(
                valueIR,
                new MaterialOutputRoots(baseColor, alphaClipThreshold),
                topology,
                MaterialFeatureMask.AlphaClip);
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
            return valueIR.Multiply(sample, valueIR.Parameter(colorParameter));
        }
    }
}
