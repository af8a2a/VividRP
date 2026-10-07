using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests.GPUDriven
{
    internal sealed class OpenPBROpaqueMaterialIRTests
    {
        [Test]
        public void NativeLeaf_PreservesAllFieldsAndHasNoLegacyTopologyPayload()
        {
            MaterialIRModule module = BuildModule();
            ClosureExpressionNode node = module.ClosureGraph.GetNode(module.SurfaceClosure);
            Assert.That(node.Opcode, Is.EqualTo(ClosureExpressionOpcode.OpenPBROpaque));
            Assert.That(module.ShadingModels, Is.EqualTo(MaterialShadingModelMask.OpenPBROpaque));
            Assert.That(module.Topology.ClosureCount, Is.EqualTo(1));
            Assert.That(module.Topology.OperatorCount, Is.Zero);
            Assert.That(module.Topology.Slabs, Is.Empty);
            Assert.That(module.Topology.NormalBases, Is.Empty);
            Assert.That(module.Topology.OpenPBROpaqueClosures.Count, Is.EqualTo(1));
            Assert.That(module.GetDebugDump(), Does.Contain("OpenPBROpaqueV1"));
            OpenPBROpaqueInputs defaults = OpenPBROpaqueContract.CreateDefault();
            for (int field = 0; field < OpenPBROpaqueContract.FieldCount; field++)
            {
                MaterialValue value = node.OpenPBROpaque.GetValue(field);
                Assert.That(value.Type, Is.EqualTo(ClosureOpenPBROpaqueExpression.GetFieldType(field)));
                Assert.That(module.Values.GetNode(value).Constant, Is.EqualTo(GetDefaultConstant(defaults, field)));
            }
            ClosureExpressionGraph roundTrip = ClosureExpressionGraph.FromTopology(
                module.Topology, out MaterialClosure root);
            Assert.That(roundTrip.GetNode(root).OpenPBROpaque.NormalWS,
                Is.EqualTo(node.OpenPBROpaque.NormalWS));
        }

        [Test]
        public void Canonicalization_PrunesDeadNodesAndIgnoresAllocationOrder()
        {
            MaterialIRModule baseline = BuildModule(dynamicFields: true);
            MaterialIRModule reordered = BuildModule(dynamicFields: true, reverse: true, dead: true);
            Assert.That(reordered.CanonicalIR.Payload, Is.EqualTo(baseline.CanonicalIR.Payload));
            Assert.That(reordered.SemanticHash, Is.EqualTo(baseline.SemanticHash));
            Assert.That(reordered.ClosureGraph.NodeCount, Is.EqualTo(1));
            Assert.That(reordered.Values.ParameterDeclarations.Count, Is.EqualTo(11));
        }

        [Test]
        public void EveryNativeField_ContributesToSemanticIdentityAndSurfaceSlice()
        {
            MaterialIRModule baseline = BuildModule(dynamicFields: true);
            for (int changed = 0; changed < OpenPBROpaqueContract.FieldCount; changed++)
            {
                MaterialIRModule module = BuildModule(dynamicFields: true, changedField: changed);
                Assert.That(module.SemanticHash, Is.Not.EqualTo(baseline.SemanticHash),
                    ((OpenPBROpaqueFieldSemantic) changed).ToString());
                ClosureOpenPBROpaqueExpression inputs = module.ClosureGraph.GetNode(module.SurfaceClosure).OpenPBROpaque;
                var roots = new MaterialValue[OpenPBROpaqueContract.FieldCount];
                for (int field = 0; field < roots.Length; field++)
                    roots[field] = inputs.GetValue(field);
                MaterialStageLIR surface = module.CreateStageLIR(MaterialEvaluationStage.Surface, roots);
                Assert.That(surface.Roots.Count, Is.EqualTo(11));
                for (int field = 0; field < roots.Length; field++)
                    Assert.That(surface.TryGetValue(roots[field], out _), Is.True);
                MaterialStageLIR coverage = module.CreateStageLIR(
                    MaterialEvaluationStage.Coverage, module.Outputs.CoverageValue, module.Outputs.AlphaClipThreshold);
                Assert.That(coverage.TryGetValue(inputs.GetValue(changed), out _), Is.False);
            }
        }

        [Test]
        public void CanonicalPayload_PinsFrozenProfileIdentityAndOrderedFieldReferences()
        {
            MaterialIRModule module = BuildModule(dynamicFields: true);
            byte[] payload = module.CanonicalIR.Payload;
            // One native leaf is followed by the root index. Its record carries
            // opcode, contract identity, and all eleven references in semantic order.
            int start = payload.Length - sizeof(uint) * (1 + 4 + 11 + 1);
            Assert.That(ReadUInt32(payload, start), Is.EqualTo((uint) ClosureExpressionOpcode.OpenPBROpaque));
            Assert.That(ReadUInt32(payload, start + 4), Is.EqualTo(OpenPBROpaqueContract.Version));
            Assert.That(ReadUInt32(payload, start + 8), Is.EqualTo(OpenPBROpaqueContract.FingerprintVersion));
            Assert.That(ReadUInt32(payload, start + 12), Is.EqualTo((uint) OpenPBROpaqueContract.Fingerprint));
            Assert.That(ReadUInt32(payload, start + 16), Is.EqualTo((uint) (OpenPBROpaqueContract.Fingerprint >> 32)));
            ClosureOpenPBROpaqueExpression inputs = module.ClosureGraph.GetNode(module.SurfaceClosure).OpenPBROpaque;
            for (int field = 0; field < OpenPBROpaqueContract.FieldCount; field++)
                Assert.That(ReadUInt32(payload, start + 20 + field * 4), Is.EqualTo((uint) inputs.GetValue(field).Index));
        }

        [Test]
        public void EachField_RejectsWrongTypeAndForeignValueOwner()
        {
            for (int field = 0; field < OpenPBROpaqueContract.FieldCount; field++)
            {
                var values = new MaterialValueIR();
                MaterialValue[] fields = CreateFields(values);
                fields[field] = ClosureOpenPBROpaqueExpression.GetFieldType(field) == MaterialValueType.Float
                    ? values.Constant(new float3(1.0f)) : values.Constant(1.0f);
                Assert.That(VerifyCandidate(values, fields).IsValid, Is.False);
                fields = CreateFields(values);
                var foreign = new MaterialValueIR();
                fields[field] = ClosureOpenPBROpaqueExpression.GetFieldType(field) == MaterialValueType.Float
                    ? foreign.Constant(1.0f) : foreign.Constant(new float3(1.0f));
                Assert.That(VerifyCandidate(values, fields).IsValid, Is.False);
            }
        }

        [Test]
        public void EachLiteralField_RejectsNonFiniteAndOutOfProfileValues()
        {
            for (int field = 0; field < OpenPBROpaqueContract.FieldCount; field++)
            {
                int components = ClosureOpenPBROpaqueExpression.GetFieldType(field) == MaterialValueType.Float3 ? 3 : 1;
                for (int component = 0; component < components; component++)
                {
                    foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    {
                        var values = new MaterialValueIR();
                        MaterialValue[] fields = CreateFields(values);
                        float4 constant = values.GetNode(fields[field]).Constant;
                        constant[component] = invalid;
                        fields[field] = Constant(values, field, constant);
                        AssertDiagnostic(VerifyCandidate(values, fields), MaterialIRDiagnosticCodes.InvalidOpenPBROpaqueConstant);
                    }
                }
                var rangeValues = new MaterialValueIR();
                MaterialValue[] rangeFields = CreateFields(rangeValues);
                float4 outside = rangeValues.GetNode(rangeFields[field]).Constant;
                outside.x = field == (int) OpenPBROpaqueFieldSemantic.SpecularIor ? 3.1f : -1.0f;
                if (field == (int) OpenPBROpaqueFieldSemantic.NormalWS)
                    outside = default;
                rangeFields[field] = Constant(rangeValues, field, outside);
                AssertDiagnostic(VerifyCandidate(rangeValues, rangeFields), MaterialIRDiagnosticCodes.InvalidOpenPBROpaqueConstant);
            }
        }

        [Test]
        public void LiteralEmissionProductOverflow_IsRejected()
        {
            var values = new MaterialValueIR();
            MaterialValue[] fields = CreateFields(values);
            fields[(int) OpenPBROpaqueFieldSemantic.EmissionLuminance] = values.Constant(float.MaxValue);
            fields[(int) OpenPBROpaqueFieldSemantic.EmissionColor] = values.Constant(new float3(2.0f));
            AssertDiagnostic(VerifyCandidate(values, fields), MaterialIRDiagnosticCodes.InvalidOpenPBROpaqueConstant);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeClosure_CannotBeMixedOrLayered(bool layer)
        {
            var values = new MaterialValueIR();
            MaterialValue[] fields = CreateFields(values);
            var graph = new ClosureExpressionGraph(values);
            MaterialClosure native = AddNative(graph, fields);
            MaterialClosure legacy = graph.Slab(values.Constant(new float4(1.0f)),
                values.Constant(0.3f), values.Constant(0.0f), fields[8],
                values.Constant(new float4(1.0f, 0.0f, 0.0f, 1.0f)), ClosureFeatureMask.None);
            MaterialClosure root = layer ? graph.VerticalLayer(legacy, native, values.Constant(0.5f))
                : graph.HorizontalMix(native, legacy, values.Constant(0.5f));
            AssertDiagnostic(MaterialIRVerifier.VerifyClosureGraph(graph, root, ClosureTopologyBudget.Prototype),
                MaterialIRDiagnosticCodes.InvalidClosureGraphShape);
        }

        [Test]
        public void NativeOutput_RequiresExclusiveProfileAndConstantZeroLegacyEmission()
        {
            foreach (MaterialShadingModelMask model in new[]
            {
                MaterialShadingModelMask.StandardLit,
                MaterialShadingModelMask.OpenPBROpaque | MaterialShadingModelMask.StandardLit,
                MaterialShadingModelMask.OpenPBROpaque | MaterialShadingModelMask.Unlit,
            })
            {
                var values = new MaterialValueIR();
                var graph = new ClosureExpressionGraph(values);
                MaterialClosure root = AddNative(graph, CreateFields(values));
                AssertDiagnostic(VerifyModule(values, graph, root, values.Constant(new float3(0.0f)), model),
                    MaterialIRDiagnosticCodes.InvalidShadingModel);
            }
            for (int variant = 0; variant < 3; variant++)
            {
                var values = new MaterialValueIR();
                var graph = new ClosureExpressionGraph(values);
                MaterialClosure root = AddNative(graph, CreateFields(values));
                MaterialValue emission = variant == 0 ? values.Constant(new float3(0.1f))
                    : variant == 1 ? values.Parameter(new MaterialParameterDeclaration("ExternalEmission", MaterialValueType.Float3))
                    : values.Compose(values.Constant(0.0f), values.Constant(0.0f), values.Constant(0.0f));
                AssertDiagnostic(VerifyModule(values, graph, root, emission, MaterialShadingModelMask.OpenPBROpaque),
                    MaterialIRDiagnosticCodes.InvalidOpenPBROpaqueOutput);
            }
        }

        [Test]
        public void LegacyLeaf_CannotClaimNativeOpenPBRProfile()
        {
            var values = new MaterialValueIR();
            var graph = new ClosureExpressionGraph(values);
            MaterialClosure root = graph.Slab(values.Constant(new float4(1.0f)), values.Constant(0.3f),
                values.Constant(0.0f), values.Constant(new float3(0.0f, 0.0f, 1.0f)),
                values.Constant(new float4(1.0f, 0.0f, 0.0f, 1.0f)), ClosureFeatureMask.None);
            AssertDiagnostic(VerifyModule(values, graph, root, values.Constant(new float3(0.0f)),
                MaterialShadingModelMask.OpenPBROpaque), MaterialIRDiagnosticCodes.InvalidShadingModel);
        }

        [Test]
        public void NativeGraph_DefaultHelperAndAllParameterOriginsReachCompilation()
        {
            var defaultsGraph = new MaterialGraph();
            MaterialGraphClosure defaultLeaf = defaultsGraph.OpenPBROpaqueDefault("Native",
                defaultsGraph.ExternalInput("Normal", MaterialExternalInput.GeometryNormalWS));
            AddOutput(defaultsGraph, defaultLeaf);
            MaterialGraphCompilationResult defaults = MaterialGraphCompiler.Compile(defaultsGraph,
                MaterialProgramContract.RuntimeAbiVersion);
            Assert.That(defaults.Succeeded, Is.True, GraphDiagnostics(defaults));
            Assert.That(defaults.Module.Values.ParameterDeclarations, Is.Empty);
            var graph = new MaterialGraph();
            var fields = new MaterialGraphValue[OpenPBROpaqueContract.FieldCount];
            for (int field = 0; field < fields.Length; field++)
                fields[field] = graph.Parameter("Field" + field, "Native" + field,
                    ClosureOpenPBROpaqueExpression.GetFieldType(field));
            MaterialGraphClosure leaf = graph.OpenPBROpaque("Native", fields[0], fields[1], fields[2], fields[3],
                fields[4], fields[5], fields[6], fields[7], fields[8], fields[9], fields[10]);
            AddOutput(graph, leaf);
            MaterialGraphCompilationResult result = MaterialGraphCompiler.Compile(graph,
                MaterialProgramContract.RuntimeAbiVersion);
            Assert.That(result.Succeeded, Is.True, GraphDiagnostics(result));
            Assert.That(result.Program.Lowering.GenericLayout.ParameterBindings.Count, Is.EqualTo(11));
            for (int field = 0; field < fields.Length; field++)
                Assert.That(result.Provenance.TryGetCanonicalValueNodes("Field" + field, out _), Is.True);
            Assert.That(result.Provenance.TryGetCanonicalClosureNodes("Native", out _), Is.True);
        }

        private static MaterialIRModule BuildModule(bool dynamicFields = false, bool reverse = false,
            bool dead = false, int changedField = -1)
        {
            var values = new MaterialValueIR();
            MaterialValue[] fields = CreateFields(values, dynamicFields, reverse, changedField);
            var graph = new ClosureExpressionGraph(values);
            if (dead)
            {
                AddNative(graph, CreateFields(values));
                values.Parameter(new MaterialParameterDeclaration("Unused", MaterialValueType.Float4));
            }
            MaterialClosure root = AddNative(graph, fields);
            return new MaterialIRModule(values,
                new MaterialOutputRoots(values.Constant(1.0f), values.Constant(0.5f), values.Constant(new float3(0.0f))),
                graph, root, ClosureTopologyBudget.Prototype, MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.OpenPBROpaque);
        }

        private static MaterialValue[] CreateFields(MaterialValueIR values, bool dynamic = false,
            bool reverse = false, int changedField = -1)
        {
            OpenPBROpaqueInputs defaults = OpenPBROpaqueContract.CreateDefault();
            var fields = new MaterialValue[OpenPBROpaqueContract.FieldCount];
            for (int index = 0; index < fields.Length; index++)
            {
                int field = reverse ? fields.Length - 1 - index : index;
                fields[field] = dynamic
                    ? values.Parameter(new MaterialParameterDeclaration(
                        "Native" + field + (field == changedField ? "Changed" : string.Empty),
                        ClosureOpenPBROpaqueExpression.GetFieldType(field)))
                    : Constant(values, field, GetDefaultConstant(defaults, field));
            }
            return fields;
        }

        private static MaterialValue Constant(MaterialValueIR values, int field, float4 constant)
        {
            return ClosureOpenPBROpaqueExpression.GetFieldType(field) == MaterialValueType.Float3
                ? values.Constant(constant.xyz) : values.Constant(constant.x);
        }

        private static float4 GetDefaultConstant(OpenPBROpaqueInputs inputs, int field)
        {
            switch ((OpenPBROpaqueFieldSemantic) field)
            {
                case OpenPBROpaqueFieldSemantic.BaseWeight: return new float4(inputs.BaseWeight, 0.0f, 0.0f, 0.0f);
                case OpenPBROpaqueFieldSemantic.BaseColor: return new float4(inputs.BaseColor, 0.0f);
                case OpenPBROpaqueFieldSemantic.BaseDiffuseRoughness: return new float4(inputs.BaseDiffuseRoughness, 0.0f, 0.0f, 0.0f);
                case OpenPBROpaqueFieldSemantic.BaseMetalness: return new float4(inputs.BaseMetalness, 0.0f, 0.0f, 0.0f);
                case OpenPBROpaqueFieldSemantic.SpecularWeight: return new float4(inputs.SpecularWeight, 0.0f, 0.0f, 0.0f);
                case OpenPBROpaqueFieldSemantic.SpecularColor: return new float4(inputs.SpecularColor, 0.0f);
                case OpenPBROpaqueFieldSemantic.SpecularRoughness: return new float4(inputs.SpecularRoughness, 0.0f, 0.0f, 0.0f);
                case OpenPBROpaqueFieldSemantic.SpecularIor: return new float4(inputs.SpecularIor, 0.0f, 0.0f, 0.0f);
                case OpenPBROpaqueFieldSemantic.NormalWS: return new float4(inputs.NormalWS, 0.0f);
                case OpenPBROpaqueFieldSemantic.EmissionLuminance: return new float4(inputs.EmissionLuminance, 0.0f, 0.0f, 0.0f);
                case OpenPBROpaqueFieldSemantic.EmissionColor: return new float4(inputs.EmissionColor, 0.0f);
                default: throw new ArgumentOutOfRangeException(nameof(field));
            }
        }

        private static MaterialClosure AddNative(ClosureExpressionGraph graph, MaterialValue[] f)
        {
            return graph.OpenPBROpaque(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10]);
        }

        private static MaterialIRVerificationResult VerifyCandidate(MaterialValueIR values, MaterialValue[] f)
        {
            return MaterialIRVerifier.VerifyCandidateClosureNode(new ClosureExpressionGraph(values),
                new ClosureExpressionNode(new ClosureOpenPBROpaqueExpression(
                    f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10])));
        }

        private static MaterialIRVerificationResult VerifyModule(MaterialValueIR values, ClosureExpressionGraph graph,
            MaterialClosure root, MaterialValue emission, MaterialShadingModelMask model)
        {
            return MaterialIRVerifier.VerifyModule(values,
                new MaterialOutputRoots(values.Constant(1.0f), values.Constant(0.5f), emission),
                graph, root, ClosureTopologyBudget.Prototype, MaterialFeatureMask.None, model);
        }

        private static void AssertDiagnostic(MaterialIRVerificationResult result, string code)
        {
            Assert.That(result.IsValid, Is.False);
            bool found = false;
            foreach (MaterialIRDiagnostic diagnostic in result.Diagnostics)
                found |= diagnostic.Code == code;
            Assert.That(found, Is.True, code);
        }

        private static void AddOutput(MaterialGraph graph, MaterialGraphClosure leaf)
        {
            graph.Output("Output", leaf, graph.Constant("Coverage", 1.0f),
                graph.Constant("Threshold", 0.5f), graph.Constant("LegacyEmission", new float3(0.0f)),
                MaterialFeatureMask.AlphaClip, MaterialShadingModelMask.OpenPBROpaque);
        }

        private static string GraphDiagnostics(MaterialGraphCompilationResult result)
        {
            var messages = new List<string>();
            foreach (MaterialGraphDiagnostic diagnostic in result.Diagnostics)
                messages.Add(diagnostic.Code + ": " + diagnostic.Message);
            return string.Join(Environment.NewLine, messages);
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint) bytes[offset] | ((uint) bytes[offset + 1] << 8)
                | ((uint) bytes[offset + 2] << 16) | ((uint) bytes[offset + 3] << 24);
        }
    }
}
