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

            MaterialValue uv = valueIR.Parameter(MaterialValueParameter.UV0);
            MaterialValue duplicateUV = valueIR.Parameter(MaterialValueParameter.UV0);
            MaterialValue uvDdx = valueIR.Ddx(uv);
            MaterialValue uvDdy = valueIR.Ddy(uv);
            MaterialValue texture = valueIR.Parameter(MaterialValueParameter.BaseColorTexture);
            MaterialValue sample = valueIR.TextureSampleGrad(texture, uv, uvDdx, uvDdy);
            MaterialValue baseColor = valueIR.Parameter(MaterialValueParameter.BaseColor);
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
            MaterialValue foreignUV = foreignIR.Parameter(MaterialValueParameter.UV0);
            Assert.That(valueIR.Owns(foreignUV), Is.False);
            Assert.Throws<ArgumentException>(() => valueIR.Ddx(foreignUV));
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
                Assert.That(ReferenceEquals(program, second.MaterialProgram), Is.True);
                Assert.That(program.ProgramID, Is.EqualTo(VividMaterialProgramID.StandardSingleSlab));
                Assert.That(program.Topology.ClosureCount, Is.EqualTo(1));
                Assert.That(program.Topology.OperatorCount, Is.Zero);
                Assert.That(program.Topology.NormalBases.Count, Is.EqualTo(1));
                Assert.That(program.Topology.Slabs[0].IsTop, Is.True);
                Assert.That(program.Topology.Slabs[0].IsBottom, Is.True);
                Assert.That(program.Topology.IsWithinBudget, Is.True);
                Assert.That(
                    program.RuntimeData.SurfaceProgramID,
                    Is.EqualTo(VividMaterialSurfaceProgramID.StandardSingleSlab));
                Assert.That(
                    program.RuntimeData.ParameterLayoutID,
                    Is.EqualTo(VividMaterialParameterLayoutID.LegacyMaterialData));
                Assert.That(
                    program.ValueIR.Nodes.Any(
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
        public void CompileDualSlab_SeparatesTopologyFromStableProgram1Abi()
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

                Assert.That(program.ProgramID, Is.EqualTo(VividMaterialProgramID.DualSlab));
                Assert.That(program.Topology.ClosureCount, Is.EqualTo(2));
                Assert.That(program.Topology.OperatorCount, Is.EqualTo(1));
                Assert.That(
                    program.Topology.Operators[0].Kind,
                    Is.EqualTo(ClosureOperatorKind.VerticalLayer));
                Assert.That(program.Topology.Slabs[0].IsBottom, Is.True);
                Assert.That(program.Topology.Slabs[0].IsTop, Is.False);
                Assert.That(program.Topology.Slabs[1].IsTop, Is.True);
                Assert.That(program.Topology.Slabs[1].IsBottom, Is.False);
                Assert.That(program.Topology.Slabs[0].NormalBasisIndex, Is.Zero);
                Assert.That(program.Topology.Slabs[1].NormalBasisIndex, Is.Zero);
                Assert.That(program.Topology.IsWithinBudget, Is.True);
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
                prototype.ValueIR,
                new[] { prototype.Topology.NormalBases[0] },
                new[] { prototype.Topology.Slabs[0], prototype.Topology.Slabs[1] },
                new[] { prototype.Topology.Operators[0] },
                budget));
        }
    }
}
