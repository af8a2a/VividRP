using System;
using NUnit.Framework;
using Unity.Mathematics;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests.GPUDriven
{
    // These cases exercise the real authoring adapter without GraphToolkit or
    // AssetDatabase calls and can run in the focused managed compiler runner.
    internal sealed class MaterialOpenPBROpaqueAuthoringTests
    {
        [Test]
        public void PortNames_MatchFrozenSemanticOrder()
        {
            string[] expected =
            {
                "BaseWeight", "BaseColor", "BaseDiffuseRoughness", "BaseMetalness",
                "SpecularWeight", "SpecularColor", "SpecularRoughness", "SpecularIor",
                "NormalWS", "EmissionLuminance", "EmissionColor",
            };
            Assert.That(expected.Length, Is.EqualTo(OpenPBROpaqueContract.FieldCount));
            for (int field = 0; field < expected.Length; ++field)
            {
                Assert.That(
                    MaterialOpenPBROpaqueAuthoring.GetPortName((OpenPBROpaqueFieldSemantic) field),
                    Is.EqualTo(expected[field]));
            }
        }

        [TestCase(OpenPBROpaqueFieldSemantic.BaseWeight, MaterialValueType.Float, 1.0f)]
        [TestCase(OpenPBROpaqueFieldSemantic.BaseColor, MaterialValueType.Float3, 0.8f)]
        [TestCase(OpenPBROpaqueFieldSemantic.BaseDiffuseRoughness, MaterialValueType.Float, 0.0f)]
        [TestCase(OpenPBROpaqueFieldSemantic.BaseMetalness, MaterialValueType.Float, 0.0f)]
        [TestCase(OpenPBROpaqueFieldSemantic.SpecularWeight, MaterialValueType.Float, 1.0f)]
        [TestCase(OpenPBROpaqueFieldSemantic.SpecularColor, MaterialValueType.Float3, 1.0f)]
        [TestCase(OpenPBROpaqueFieldSemantic.SpecularRoughness, MaterialValueType.Float, 0.3f)]
        [TestCase(OpenPBROpaqueFieldSemantic.SpecularIor, MaterialValueType.Float, 1.5f)]
        [TestCase(OpenPBROpaqueFieldSemantic.EmissionLuminance, MaterialValueType.Float, 0.0f)]
        [TestCase(OpenPBROpaqueFieldSemantic.EmissionColor, MaterialValueType.Float3, 1.0f)]
        public void UnconnectedInput_UsesFrozenConstant(
            OpenPBROpaqueFieldSemantic semantic, MaterialValueType type, float value)
        {
            var graph = new MaterialGraph();
            MaterialGraphValue input = MaterialOpenPBROpaqueAuthoring.CreateDefaultInput(
                graph, "Native", semantic);
            MaterialGraphValueNode node = graph.ValueNodes[input.NodeId];

            Assert.That(node.Opcode, Is.EqualTo(MaterialGraphValueOpcode.Constant));
            Assert.That(node.ConstantType, Is.EqualTo(type));
            Assert.That(node.Constant.x, Is.EqualTo(value));
            if (type == MaterialValueType.Float3)
                Assert.That(node.Constant.xyz, Is.EqualTo(new float3(value)));
        }

        [Test]
        public void UnconnectedNormal_UsesCurrentGeometryWithoutChangingContractDefault()
        {
            var graph = new MaterialGraph();
            MaterialGraphValue normal = MaterialOpenPBROpaqueAuthoring.CreateDefaultInput(
                graph, "Native", OpenPBROpaqueFieldSemantic.NormalWS);
            MaterialGraphValueNode node = graph.ValueNodes[normal.NodeId];

            Assert.That(node.Opcode, Is.EqualTo(MaterialGraphValueOpcode.ExternalInput));
            Assert.That(node.Semantic, Is.EqualTo((int) MaterialExternalInput.GeometryNormalWS));
            Assert.That(OpenPBROpaqueContract.CreateDefault().NormalWS,
                Is.EqualTo(new float3(0.0f, 0.0f, 1.0f)));
        }

        [Test]
        public void InjectedDefaults_CompileAsNativeProfileWithZeroLegacyEmission()
        {
            var graph = new MaterialGraph();
            var inputs = new MaterialGraphValue[OpenPBROpaqueContract.FieldCount];
            for (int field = 0; field < inputs.Length; ++field)
            {
                inputs[field] = MaterialOpenPBROpaqueAuthoring.CreateDefaultInput(
                    graph, "Native", (OpenPBROpaqueFieldSemantic) field);
            }
            MaterialGraphClosure surface = graph.OpenPBROpaque(
                "Native", inputs[0], inputs[1], inputs[2], inputs[3], inputs[4],
                inputs[5], inputs[6], inputs[7], inputs[8], inputs[9], inputs[10]);
            graph.Output("Output", surface,
                graph.Constant("Coverage", 1.0f),
                graph.Constant("Threshold", 0.0f),
                graph.Constant("LegacyEmission", float3.zero),
                MaterialFeatureMask.None, MaterialShadingModelMask.OpenPBROpaque);

            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(
                graph, GPUDrivenMaterialCompiler.ProgramVersion);

            Assert.That(result.Succeeded, Is.True,
                result.Diagnostics.Count > 0 ? result.Diagnostics[0].Message : string.Empty);
            Assert.That(result.Module.ShadingModels, Is.EqualTo(MaterialShadingModelMask.OpenPBROpaque));
            Assert.That(result.Module.Topology.ClosureCount, Is.EqualTo(1));
            Assert.That(result.Module.Topology.OperatorCount, Is.Zero);
            Assert.That(result.Module.Values.GetNode(result.Module.Outputs.Emission).Constant.xyz,
                Is.EqualTo(float3.zero));
        }

        [Test]
        public void UnknownField_IsRejectedBeforeModifyingGraph()
        {
            var graph = new MaterialGraph();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MaterialOpenPBROpaqueAuthoring.CreateDefaultInput(
                    graph, "Native", (OpenPBROpaqueFieldSemantic) 999u));
            Assert.That(graph.ValueNodes.Count, Is.Zero);
        }
    }
}
