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
                module.Outputs.AlphaClipThreshold.Type,
                Is.EqualTo(MaterialValueType.Float));
            Assert.That(module.GetDebugDump(), Does.Contain("external_input UV0"));
            Assert.That(module.GetDebugDump(), Does.Contain("texture_resource BaseColor"));
            Assert.That(module.GetDebugDump(), Does.Contain("coverage=%"));
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
                module.Topology));
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
                topology);

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
            Assert.That(standardDump, Does.Contain("cost_model=typed_ir_structural_v1"));
            Assert.That(standardDump, Does.Contain("status=ok"));
            Assert.That(standardDump, Does.Contain("MPC0001"));
            Assert.That(standardDump, Does.Contain("not represented"));
        }

        [Test]
        public void CostBudget_RejectsProgramWhenCombinedNodeLimitIsExceeded()
        {
            CompiledMaterialProgram prototype =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            var budget = new MaterialProgramCostBudget(
                maxCombinedValueNodes: 11,
                maxTextureSamples: 2,
                maxParameters: 8,
                maxTextureResources: 2,
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
                Does.Contain("combined value nodes cost 12 exceeds prototype budget 11"));
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
                    GPUDrivenMaterialCompiler.CompileDualSlab(baseProxy, 3u, 6u, 7u);
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
                MaterialParameter.BaseColor,
                MaterialValueType.Float4,
                byteOffset: 0);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialParameter.Roughness,
                MaterialValueType.Float,
                byteOffset: 88);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialParameter.Metallic,
                MaterialValueType.Float,
                byteOffset: 92);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialParameter.AlphaClipThreshold,
                MaterialValueType.Float,
                byteOffset: 116);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.BaseColor,
                recordOffset: 0,
                byteOffset: 0);
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
                MaterialParameter.BaseColor,
                MaterialValueType.Float4,
                byteOffset: 0);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialParameter.TopBaseColor,
                MaterialValueType.Float4,
                byteOffset: 80);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialParameter.Roughness,
                MaterialValueType.Float,
                byteOffset: 68);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialParameter.TopRoughness,
                MaterialValueType.Float,
                byteOffset: 148);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialParameter.Metallic,
                MaterialValueType.Float,
                byteOffset: 72);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialParameter.TopMetallic,
                MaterialValueType.Float,
                byteOffset: 152);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialParameter.LayerWeight,
                MaterialValueType.Float,
                byteOffset: 180);
            AssertParameterBinding(
                layout.ParameterLayout,
                MaterialParameter.AlphaClipThreshold,
                MaterialValueType.Float,
                byteOffset: 184);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.BaseColor,
                recordOffset: 0,
                byteOffset: 0);
            AssertResourceBinding(
                layout.ResourceLayout,
                MaterialTextureResource.TopBaseColor,
                recordOffset: 1,
                byteOffset: 0);
        }

        private static void AssertParameterBinding(
            CompiledParameterLayout layout,
            MaterialParameter parameter,
            MaterialValueType type,
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
                    features: ClosureFeatureMask.AlphaClip,
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
                topology);
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
                topology);
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
