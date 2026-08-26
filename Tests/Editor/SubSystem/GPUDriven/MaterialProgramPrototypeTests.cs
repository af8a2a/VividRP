using System;
using System.Collections.Generic;
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
            MaterialIRVerificationException typeException =
                Assert.Throws<MaterialIRVerificationException>(() => valueIR.Add(uv, sample));
            AssertDiagnostic(
                typeException.Diagnostics,
                MaterialIRDiagnosticCodes.OperandTypeMismatch,
                nodeIndex: 10);

            var foreignIR = new MaterialValueIR();
            MaterialValue foreignUV = foreignIR.ExternalInput(MaterialExternalInput.UV0);
            Assert.That(valueIR.Owns(foreignUV), Is.False);
            Assert.Throws<ArgumentException>(() => valueIR.Ddx(foreignUV));
        }

        [Test]
        public void MaterialDeclarations_AreTypedAndRoundTripNativeTemplateSemantics()
        {
            foreach (MaterialParameter parameter in Enum.GetValues(typeof(MaterialParameter)))
            {
                MaterialParameterDeclaration declaration =
                    MaterialNativeTemplateDeclarationAdapter.GetParameter(parameter);
                Assert.That(declaration.Symbol, Is.EqualTo(parameter.ToString()));
                Assert.That(
                    MaterialNativeTemplateDeclarationAdapter.TryGetParameter(
                        declaration,
                        out MaterialParameter roundTrip),
                    Is.True);
                Assert.That(roundTrip, Is.EqualTo(parameter));
            }

            foreach (MaterialTextureResource resource in
                     Enum.GetValues(typeof(MaterialTextureResource)))
            {
                MaterialResourceDeclaration declaration =
                    MaterialNativeTemplateDeclarationAdapter.GetTexture(resource);
                Assert.That(declaration.Symbol, Is.EqualTo(resource.ToString()));
                Assert.That(declaration.Type, Is.EqualTo(MaterialValueType.Texture2D));
                Assert.That(
                    MaterialNativeTemplateDeclarationAdapter.TryGetTexture(
                        declaration,
                        out MaterialTextureResource roundTrip),
                    Is.True);
                Assert.That(roundTrip, Is.EqualTo(resource));
            }

            Assert.That(
                MaterialNativeTemplateDeclarationAdapter.GetParameter(
                    MaterialParameter.Emission).Type,
                Is.EqualTo(MaterialValueType.Float3));
            Assert.That(
                new MaterialParameterDeclaration("BaseColor", MaterialValueType.Float4),
                Is.EqualTo(
                    MaterialNativeTemplateDeclarationAdapter.GetParameter(
                        MaterialParameter.BaseColor)));
            Assert.That(
                new MaterialParameterDeclaration("baseColor", MaterialValueType.Float4),
                Is.Not.EqualTo(
                    MaterialNativeTemplateDeclarationAdapter.GetParameter(
                        MaterialParameter.BaseColor)));
            Assert.That(
                new MaterialParameterDeclaration("BaseColor", MaterialValueType.Float3),
                Is.Not.EqualTo(
                    MaterialNativeTemplateDeclarationAdapter.GetParameter(
                        MaterialParameter.BaseColor)));

            var valueIR = new MaterialValueIR();
            var customParameter = new MaterialParameterDeclaration(
                "CustomTint",
                MaterialValueType.Float3);
            MaterialValue firstParameter = valueIR.Parameter(customParameter);
            MaterialValue duplicateParameter = valueIR.Parameter(customParameter);
            var customResource = new MaterialResourceDeclaration(
                "CustomTexture",
                MaterialValueType.Texture2D);
            MaterialValue firstResource = valueIR.TextureResource(customResource);
            MaterialValue duplicateResource = valueIR.TextureResource(customResource);

            Assert.That(duplicateParameter, Is.EqualTo(firstParameter));
            Assert.That(duplicateResource, Is.EqualTo(firstResource));
            Assert.That(firstParameter.Type, Is.EqualTo(MaterialValueType.Float3));
            Assert.That(firstResource.Type, Is.EqualTo(MaterialValueType.Texture2D));
            Assert.That(valueIR.ParameterDeclarations, Has.Count.EqualTo(1));
            Assert.That(valueIR.ResourceDeclarations, Has.Count.EqualTo(1));
            Assert.That(
                MaterialNativeTemplateDeclarationAdapter.TryGetParameter(
                    customParameter,
                    out _),
                Is.False);
            Assert.That(
                MaterialNativeTemplateDeclarationAdapter.TryGetTexture(
                    customResource,
                    out _),
                Is.False);
        }

        [Test]
        public void MaterialOpcodeTable_CoversEveryMaterialIRV2Opcode()
        {
            MaterialValueOpcode[] opcodes =
                (MaterialValueOpcode[]) Enum.GetValues(typeof(MaterialValueOpcode));
            var expectedOpcodes = new[]
            {
                MaterialValueOpcode.Constant,
                MaterialValueOpcode.ExternalInput,
                MaterialValueOpcode.Parameter,
                MaterialValueOpcode.TextureResource,
                MaterialValueOpcode.TextureSampleGrad,
                MaterialValueOpcode.Ddx,
                MaterialValueOpcode.Ddy,
                MaterialValueOpcode.Add,
                MaterialValueOpcode.Multiply,
                MaterialValueOpcode.Lerp,
                MaterialValueOpcode.Select,
                MaterialValueOpcode.Swizzle,
                MaterialValueOpcode.Compose,
                MaterialValueOpcode.Subtract,
                MaterialValueOpcode.Divide,
                MaterialValueOpcode.Min,
                MaterialValueOpcode.Max,
                MaterialValueOpcode.Saturate,
                MaterialValueOpcode.OneMinus,
                MaterialValueOpcode.Dot,
                MaterialValueOpcode.Normalize,
                MaterialValueOpcode.Compare,
            };
            CollectionAssert.AreEqual(expectedOpcodes, opcodes);
            for (int opcodeIndex = 0; opcodeIndex < expectedOpcodes.Length; opcodeIndex++)
                Assert.That((int) expectedOpcodes[opcodeIndex], Is.EqualTo(opcodeIndex));

            foreach (MaterialValueOpcode opcode in opcodes)
            {
                Assert.That(
                    MaterialOpcodeTable.TryGetInfo(opcode, out MaterialOpcodeInfo info),
                    Is.True,
                    opcode.ToString());
                Assert.That(info.Opcode, Is.EqualTo(opcode));
                Assert.That(info.Name, Is.Not.Empty);
                Assert.That(info.MinOperandCount, Is.GreaterThanOrEqualTo(0));
                Assert.That(info.MaxOperandCount, Is.GreaterThanOrEqualTo(info.MinOperandCount));
                Assert.That(info.EvaluationStages, Is.EqualTo(MaterialEvaluationStageMask.All));

                bool expectedCommutative = opcode == MaterialValueOpcode.Add
                    || opcode == MaterialValueOpcode.Multiply
                    || opcode == MaterialValueOpcode.Min
                    || opcode == MaterialValueOpcode.Max
                    || opcode == MaterialValueOpcode.Dot;
                Assert.That(
                    (info.Flags & MaterialOpcodeFlags.Commutative) != 0,
                    Is.EqualTo(expectedCommutative),
                    opcode.ToString());
            }

            MaterialOpcodeTable.TryGetInfo(
                MaterialValueOpcode.TextureSampleGrad,
                out MaterialOpcodeInfo textureSample);
            MaterialOpcodeTable.TryGetInfo(
                MaterialValueOpcode.Ddx,
                out MaterialOpcodeInfo ddx);
            MaterialOpcodeTable.TryGetInfo(
                MaterialValueOpcode.Ddy,
                out MaterialOpcodeInfo ddy);
            Assert.That(
                textureSample.DerivativePolicy,
                Is.EqualTo(MaterialDerivativePolicy.RequiresExplicitGradients));
            Assert.That(
                ddx.DerivativePolicy,
                Is.EqualTo(MaterialDerivativePolicy.ProducesDerivative));
            Assert.That(
                ddy.DerivativePolicy,
                Is.EqualTo(MaterialDerivativePolicy.ProducesDerivative));

            Assert.That(
                MaterialOpcodeTable.TryGetInfo((MaterialValueOpcode) 999, out _),
                Is.False);
        }

        [Test]
        public void MaterialValueIR_V2OperationsAreTypedAndDeduplicated()
        {
            var valueIR = new MaterialValueIR();
            MaterialValue x = valueIR.Constant(1.0f);
            MaterialValue y = valueIR.Constant(2.0f);
            MaterialValue z = valueIR.Constant(3.0f);
            MaterialValue w = valueIR.Constant(4.0f);
            MaterialValue left = valueIR.Constant(new float3(1.0f, 2.0f, 3.0f));
            MaterialValue right = valueIR.Constant(new float3(4.0f, 5.0f, 6.0f));

            MaterialValue swizzle = valueIR.Swizzle(left, MaterialSwizzleMask.XYZ);
            MaterialValue compose2 = valueIR.Compose(x, y);
            MaterialValue compose3 = valueIR.Compose(x, y, z);
            MaterialValue compose4 = valueIR.Compose(x, y, z, w);
            MaterialValue subtract = valueIR.Subtract(left, right);
            MaterialValue divide = valueIR.Divide(left, right);
            MaterialValue min = valueIR.Min(left, right);
            MaterialValue max = valueIR.Max(left, right);
            MaterialValue saturate = valueIR.Saturate(left);
            MaterialValue oneMinus = valueIR.OneMinus(left);
            MaterialValue dot = valueIR.Dot(left, right);
            MaterialValue normalize = valueIR.Normalize(left);
            MaterialValue compare = valueIR.Compare(x, y, MaterialComparison.Less);

            Assert.That(swizzle.Type, Is.EqualTo(MaterialValueType.Float3));
            Assert.That(compose2.Type, Is.EqualTo(MaterialValueType.Float2));
            Assert.That(compose3.Type, Is.EqualTo(MaterialValueType.Float3));
            Assert.That(compose4.Type, Is.EqualTo(MaterialValueType.Float4));
            Assert.That(subtract.Type, Is.EqualTo(MaterialValueType.Float3));
            Assert.That(divide.Type, Is.EqualTo(MaterialValueType.Float3));
            Assert.That(min.Type, Is.EqualTo(MaterialValueType.Float3));
            Assert.That(max.Type, Is.EqualTo(MaterialValueType.Float3));
            Assert.That(saturate.Type, Is.EqualTo(MaterialValueType.Float3));
            Assert.That(oneMinus.Type, Is.EqualTo(MaterialValueType.Float3));
            Assert.That(dot.Type, Is.EqualTo(MaterialValueType.Float));
            Assert.That(normalize.Type, Is.EqualTo(MaterialValueType.Float3));
            Assert.That(compare.Type, Is.EqualTo(MaterialValueType.Bool));

            Assert.That(valueIR.Swizzle(left, MaterialSwizzleMask.XYZ), Is.EqualTo(swizzle));
            Assert.That(valueIR.Compose(x, y), Is.EqualTo(compose2));
            Assert.That(valueIR.Compose(x, y, z), Is.EqualTo(compose3));
            Assert.That(valueIR.Compose(x, y, z, w), Is.EqualTo(compose4));
            Assert.That(valueIR.Subtract(left, right), Is.EqualTo(subtract));
            Assert.That(valueIR.Divide(left, right), Is.EqualTo(divide));
            Assert.That(valueIR.Min(left, right), Is.EqualTo(min));
            Assert.That(valueIR.Max(left, right), Is.EqualTo(max));
            Assert.That(valueIR.Saturate(left), Is.EqualTo(saturate));
            Assert.That(valueIR.OneMinus(left), Is.EqualTo(oneMinus));
            Assert.That(valueIR.Dot(left, right), Is.EqualTo(dot));
            Assert.That(valueIR.Normalize(left), Is.EqualTo(normalize));
            Assert.That(
                valueIR.Compare(x, y, MaterialComparison.Less),
                Is.EqualTo(compare));
        }

        [Test]
        public void MaterialValueIR_CompareRequiresScalarFloatOperands()
        {
            var values = new MaterialValueIR();
            MaterialValue left = values.Constant(new float3(1.0f, 2.0f, 3.0f));
            MaterialValue right = values.Constant(new float3(4.0f, 5.0f, 6.0f));
            int candidateNodeIndex = values.NodeCount;

            MaterialIRVerificationException exception =
                Assert.Throws<MaterialIRVerificationException>(() => values.Compare(
                    left,
                    right,
                    MaterialComparison.Less));

            AssertDiagnostic(
                exception.Diagnostics,
                MaterialIRDiagnosticCodes.OperandTypeMismatch,
                candidateNodeIndex);
            Assert.That(values.NodeCount, Is.EqualTo(candidateNodeIndex));
        }

        [Test]
        public void MaterialIRVerifier_CandidateDiagnosticsHaveStableCodesAndNodeIndices()
        {
            var values = new MaterialValueIR();
            MaterialValue left = values.Constant(1.0f);
            MaterialValue right = values.Constant(2.0f);
            MaterialValue condition = values.Constant(true);

            AssertCandidateDiagnostic(
                values,
                new MaterialValueNode(
                    (MaterialValueOpcode) 999,
                    MaterialValueType.Float,
                    0,
                    default,
                    -1,
                    -1,
                    -1,
                    -1),
                MaterialIRDiagnosticCodes.UnknownOpcode);
            AssertCandidateDiagnostic(
                values,
                new MaterialValueNode(
                    MaterialValueOpcode.Constant,
                    (MaterialValueType) 999,
                    0,
                    default,
                    -1,
                    -1,
                    -1,
                    -1),
                MaterialIRDiagnosticCodes.UnknownValueType);
            AssertCandidateDiagnostic(
                values,
                new MaterialValueNode(
                    MaterialValueOpcode.Add,
                    MaterialValueType.Float,
                    0,
                    default,
                    left.Index,
                    -1,
                    -1,
                    -1),
                MaterialIRDiagnosticCodes.InvalidOperandEncoding);
            AssertCandidateDiagnostic(
                values,
                new MaterialValueNode(
                    MaterialValueOpcode.Add,
                    MaterialValueType.Float,
                    0,
                    default,
                    left.Index,
                    values.NodeCount,
                    -1,
                    -1),
                MaterialIRDiagnosticCodes.NonTopologicalOperand);
            AssertCandidateDiagnostic(
                values,
                new MaterialValueNode(
                    MaterialValueOpcode.Add,
                    MaterialValueType.Float,
                    0,
                    default,
                    left.Index,
                    condition.Index,
                    -1,
                    -1),
                MaterialIRDiagnosticCodes.OperandTypeMismatch);
            AssertCandidateDiagnostic(
                values,
                new MaterialValueNode(
                    MaterialValueOpcode.Parameter,
                    MaterialValueType.Float,
                    values.ParameterDeclarations.Count,
                    default,
                    -1,
                    -1,
                    -1,
                    -1),
                MaterialIRDiagnosticCodes.InvalidSemantic);
            AssertCandidateDiagnostic(
                values,
                new MaterialValueNode(
                    MaterialValueOpcode.Add,
                    MaterialValueType.Float,
                    1,
                    default,
                    left.Index,
                    right.Index,
                    -1,
                    -1),
                MaterialIRDiagnosticCodes.NonCanonicalPayload);
        }

        [Test]
        public void ClosureExpressionGraph_EmitsOrderedOccurrencesAndFreezes()
        {
            var values = new MaterialValueIR();
            MaterialValue baseColor = values.Parameter(MaterialParameter.BaseColor);
            MaterialValue topBaseColor = values.Parameter(MaterialParameter.TopBaseColor);
            MaterialValue roughness = values.Parameter(MaterialParameter.Roughness);
            MaterialValue topRoughness = values.Parameter(MaterialParameter.TopRoughness);
            MaterialValue metallic = values.Parameter(MaterialParameter.Metallic);
            MaterialValue topMetallic = values.Parameter(MaterialParameter.TopMetallic);
            MaterialValue normal =
                values.ExternalInput(MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent =
                values.ExternalInput(MaterialExternalInput.GeometryTangentWS);
            MaterialValue weight = values.Parameter(MaterialParameter.LayerWeight);
            var graph = new ClosureExpressionGraph(values);
            MaterialClosure background = graph.Slab(
                baseColor,
                roughness,
                metallic,
                normal,
                tangent,
                ClosureFeatureMask.None);
            MaterialClosure foreground = graph.Slab(
                topBaseColor,
                topRoughness,
                topMetallic,
                normal,
                tangent,
                ClosureFeatureMask.None);
            MaterialClosure forward = graph.HorizontalMix(
                background,
                foreground,
                weight);
            MaterialClosure reversed = graph.HorizontalMix(
                foreground,
                background,
                weight);

            Assert.That(graph.ValueIR, Is.SameAs(values));
            Assert.That(graph.NodeCount, Is.EqualTo(4));
            Assert.That(graph.GetNode(background).Opcode, Is.EqualTo(ClosureExpressionOpcode.Slab));
            Assert.That(graph.GetNode(forward).Opcode, Is.EqualTo(ClosureExpressionOpcode.HorizontalMix));
            Assert.That(graph.GetNode(forward).Operand0, Is.EqualTo(background.Index));
            Assert.That(graph.GetNode(forward).Operand1, Is.EqualTo(foreground.Index));
            Assert.That(graph.GetNode(reversed).Operand0, Is.EqualTo(foreground.Index));
            Assert.That(graph.GetNode(reversed).Operand1, Is.EqualTo(background.Index));
            Assert.That(forward, Is.Not.EqualTo(reversed));

            MaterialIRVerificationResult invalidWeight =
                MaterialIRVerifier.VerifyCandidateClosureNode(
                    graph,
                    new ClosureExpressionNode(
                        ClosureExpressionOpcode.VerticalLayer,
                        default,
                        background.Index,
                        foreground.Index,
                        baseColor));
            AssertDiagnostic(
                invalidWeight.Diagnostics,
                MaterialIRDiagnosticCodes.InvalidClosureValue,
                nodeIndex: graph.NodeCount);

            graph.Freeze();
            Assert.That(graph.IsFrozen, Is.True);
            Assert.Throws<InvalidOperationException>(() => graph.VerticalLayer(
                background,
                foreground,
                weight));
        }

        [Test]
        public void ClosureExpressionGraph_VerifierReportsStableMalformedGraphDiagnostics()
        {
            var values = new MaterialValueIR();
            MaterialValue baseColor = values.Parameter(MaterialParameter.BaseColor);
            MaterialValue roughness = values.Parameter(MaterialParameter.Roughness);
            MaterialValue metallic = values.Parameter(MaterialParameter.Metallic);
            MaterialValue normal =
                values.ExternalInput(MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent =
                values.ExternalInput(MaterialExternalInput.GeometryTangentWS);
            MaterialValue weight = values.Parameter(MaterialParameter.LayerWeight);
            var graph = new ClosureExpressionGraph(values);
            var slabExpression = new ClosureSlabExpression(
                baseColor,
                roughness,
                metallic,
                normal,
                tangent,
                ClosureFeatureMask.None);
            MaterialClosure slab = graph.Slab(slabExpression);

            AssertDiagnostic(
                MaterialIRVerifier.VerifyCandidateClosureNode(
                    graph,
                    new ClosureExpressionNode(
                        (ClosureExpressionOpcode) 99,
                        default,
                        -1,
                        -1,
                        default)).Diagnostics,
                MaterialIRDiagnosticCodes.UnknownClosureOpcode,
                nodeIndex: graph.NodeCount);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyCandidateClosureNode(
                    graph,
                    new ClosureExpressionNode(
                        ClosureExpressionOpcode.Slab,
                        slabExpression,
                        slab.Index,
                        -1,
                        default)).Diagnostics,
                MaterialIRDiagnosticCodes.InvalidClosureOperandEncoding,
                nodeIndex: graph.NodeCount);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyCandidateClosureNode(
                    graph,
                    new ClosureExpressionNode(
                        ClosureExpressionOpcode.HorizontalMix,
                        default,
                        graph.NodeCount + 1,
                        slab.Index,
                        weight)).Diagnostics,
                MaterialIRDiagnosticCodes.ClosureOperandOutOfRange,
                nodeIndex: graph.NodeCount);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyCandidateClosureNode(
                    graph,
                    new ClosureExpressionNode(
                        ClosureExpressionOpcode.HorizontalMix,
                        default,
                        graph.NodeCount,
                        slab.Index,
                        weight)).Diagnostics,
                MaterialIRDiagnosticCodes.NonTopologicalClosureOperand,
                nodeIndex: graph.NodeCount);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyCandidateClosureNode(
                    graph,
                    new ClosureExpressionNode(
                        ClosureExpressionOpcode.Slab,
                        new ClosureSlabExpression(
                            baseColor,
                            roughness,
                            metallic,
                            normal,
                            tangent,
                            (ClosureFeatureMask) (1 << 8)),
                        -1,
                        -1,
                        default)).Diagnostics,
                MaterialIRDiagnosticCodes.InvalidClosureFeature,
                nodeIndex: graph.NodeCount);

            var otherGraph = new ClosureExpressionGraph(values);
            MaterialClosure foreignRoot = otherGraph.Slab(slabExpression);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyClosureGraph(
                    graph,
                    foreignRoot,
                    ClosureTopologyBudget.Prototype).Diagnostics,
                MaterialIRDiagnosticCodes.ClosureRootNotOwned);

            var moduleValues = new MaterialValueIR();
            MaterialValue coverage = moduleValues.Constant(1.0f);
            MaterialValue alphaClipThreshold = moduleValues.Constant(0.5f);
            MaterialValue emission = moduleValues.Constant(new float3(0.0f));
            AssertDiagnostic(
                MaterialIRVerifier.VerifyModule(
                    moduleValues,
                    new MaterialOutputRoots(
                        coverage,
                        alphaClipThreshold,
                        emission),
                    graph,
                    slab,
                    ClosureTopologyBudget.Prototype,
                    MaterialFeatureMask.None,
                    MaterialShadingModelMask.StandardLit).Diagnostics,
                MaterialIRDiagnosticCodes.ClosureGraphOwnerMismatch);
        }

        [Test]
        public void ClosureExpressionGraph_VerifierRejectsFanOutNestedShapeAndBudget()
        {
            var values = new MaterialValueIR();
            MaterialValue baseColor = values.Parameter(MaterialParameter.BaseColor);
            MaterialValue topBaseColor = values.Parameter(MaterialParameter.TopBaseColor);
            MaterialValue roughness = values.Parameter(MaterialParameter.Roughness);
            MaterialValue metallic = values.Parameter(MaterialParameter.Metallic);
            MaterialValue normal =
                values.ExternalInput(MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent =
                values.ExternalInput(MaterialExternalInput.GeometryTangentWS);
            MaterialValue weight = values.Parameter(MaterialParameter.LayerWeight);
            var graph = new ClosureExpressionGraph(values);
            MaterialClosure background = graph.Slab(
                baseColor,
                roughness,
                metallic,
                normal,
                tangent,
                ClosureFeatureMask.None);
            MaterialClosure foreground = graph.Slab(
                topBaseColor,
                roughness,
                metallic,
                normal,
                tangent,
                ClosureFeatureMask.None);
            MaterialClosure mixed = graph.HorizontalMix(
                background,
                foreground,
                weight);
            MaterialClosure nested = graph.VerticalLayer(
                mixed,
                foreground,
                weight);

            MaterialIRVerificationResult result =
                MaterialIRVerifier.VerifyClosureGraph(
                    graph,
                    nested,
                    ClosureTopologyBudget.Prototype);

            Assert.That(result.IsValid, Is.False);
            AssertDiagnostic(
                result.Diagnostics,
                MaterialIRDiagnosticCodes.ClosureGraphFanOut,
                nodeIndex: foreground.Index);
            AssertDiagnostic(
                result.Diagnostics,
                MaterialIRDiagnosticCodes.InvalidClosureGraphShape,
                nodeIndex: nested.Index);
            AssertDiagnostic(
                result.Diagnostics,
                MaterialIRDiagnosticCodes.ClosureGraphBudgetExceeded);
        }

        [Test]
        public void MaterialIRModule_CanonicalizesClosureAllocationAndPrunesDeadClosures()
        {
            MaterialIRModule baseline = BuildCanonicalClosureGraphModule(
                reverseClosureAllocation: false,
                includeDeadClosure: false);
            MaterialIRModule reordered = BuildCanonicalClosureGraphModule(
                reverseClosureAllocation: true,
                includeDeadClosure: true);

            Assert.That(baseline.ClosureGraph.NodeCount, Is.EqualTo(3));
            Assert.That(reordered.ClosureGraph.NodeCount, Is.EqualTo(3));
            ClosureExpressionNode baselineRoot =
                baseline.ClosureGraph.GetNode(baseline.SurfaceClosure);
            ClosureExpressionNode reorderedRoot =
                reordered.ClosureGraph.GetNode(reordered.SurfaceClosure);
            Assert.That(baseline.SurfaceClosure.Index, Is.EqualTo(2));
            Assert.That(reordered.SurfaceClosure.Index, Is.EqualTo(2));
            Assert.That(baselineRoot.Opcode, Is.EqualTo(ClosureExpressionOpcode.HorizontalMix));
            Assert.That(reorderedRoot.Opcode, Is.EqualTo(ClosureExpressionOpcode.HorizontalMix));
            Assert.That(baselineRoot.Operand0, Is.Zero);
            Assert.That(baselineRoot.Operand1, Is.EqualTo(1));
            Assert.That(reorderedRoot.Operand0, Is.Zero);
            Assert.That(reorderedRoot.Operand1, Is.EqualTo(1));
            CollectionAssert.AreEqual(baseline.Values.Nodes, reordered.Values.Nodes);
            CollectionAssert.AreEqual(
                baseline.CanonicalIR.Payload,
                reordered.CanonicalIR.Payload);
            Assert.That(baseline.CanonicalIR.PayloadEquals(reordered.CanonicalIR), Is.True);
            Assert.That(baseline.SemanticHash, Is.EqualTo(reordered.SemanticHash));
            Assert.That(baseline.GetDebugDump(), Is.EqualTo(reordered.GetDebugDump()));
            Assert.That(
                reordered.Values.Nodes.Any(node =>
                    node.Opcode == MaterialValueOpcode.Constant
                    && node.Type == MaterialValueType.Float4
                    && node.Constant.x == 0.125f),
                Is.False,
                "Canonical IR must prune values referenced only by an unreachable closure.");
        }

        [Test]
        public void MaterialIRModule_PreservesClosureOperandOrderAndOperatorKind()
        {
            MaterialIRModule horizontal = BuildCanonicalClosureGraphModule(
                reverseClosureAllocation: false,
                includeDeadClosure: false);
            MaterialIRModule swapped = BuildCanonicalClosureGraphModule(
                reverseClosureAllocation: false,
                includeDeadClosure: false,
                swapRootOperands: true);
            MaterialIRModule vertical = BuildCanonicalClosureGraphModule(
                reverseClosureAllocation: false,
                includeDeadClosure: false,
                rootOpcode: ClosureExpressionOpcode.VerticalLayer);

            Assert.That(horizontal.CanonicalIR.PayloadEquals(swapped.CanonicalIR), Is.False);
            Assert.That(horizontal.CanonicalIR.PayloadEquals(vertical.CanonicalIR), Is.False);
            Assert.That(horizontal.SemanticHash, Is.Not.EqualTo(swapped.SemanticHash));
            Assert.That(horizontal.SemanticHash, Is.Not.EqualTo(vertical.SemanticHash));
        }

        [Test]
        public void ClosureExpressionGraph_LowersPrototypeTopAndBottomRoles()
        {
            CompiledMaterialProgram single =
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

            Assert.That(single.Module.ClosureGraph.IsFrozen, Is.True);
            Assert.That(single.Module.Topology.Slabs[0].IsTop, Is.True);
            Assert.That(single.Module.Topology.Slabs[0].IsBottom, Is.True);
            Assert.That(horizontal.Module.Topology.Slabs[0].IsTop, Is.True);
            Assert.That(horizontal.Module.Topology.Slabs[0].IsBottom, Is.True);
            Assert.That(horizontal.Module.Topology.Slabs[1].IsTop, Is.True);
            Assert.That(horizontal.Module.Topology.Slabs[1].IsBottom, Is.True);
            Assert.That(vertical.Module.Topology.Slabs[0].IsTop, Is.False);
            Assert.That(vertical.Module.Topology.Slabs[0].IsBottom, Is.True);
            Assert.That(vertical.Module.Topology.Slabs[1].IsTop, Is.True);
            Assert.That(vertical.Module.Topology.Slabs[1].IsBottom, Is.False);
        }

        [Test]
        public void ClosureExpressionGraph_FromTopologyPreservesDualSlabOrderAndKind()
        {
            var operatorKinds = new[]
            {
                VividDualSlabOperator.HorizontalMix,
                VividDualSlabOperator.VerticalLayer,
            };
            foreach (VividDualSlabOperator operatorKind in operatorKinds)
            {
                CompiledMaterialProgram program =
                    MaterialProgramPrototypeBuilder.BuildDualSlab(
                        GPUDrivenMaterialCompiler.ProgramVersion,
                        operatorKind);
                ClosureTopology source = program.Module.Topology;
                ClosureExpressionGraph graph =
                    ClosureExpressionGraph.FromTopology(
                        source,
                        out MaterialClosure root);
                ClosureExpressionNode rootNode = graph.GetNode(root);
                ClosureExpressionOpcode expectedOpcode =
                    operatorKind == VividDualSlabOperator.HorizontalMix
                        ? ClosureExpressionOpcode.HorizontalMix
                        : ClosureExpressionOpcode.VerticalLayer;

                Assert.That(rootNode.Opcode, Is.EqualTo(expectedOpcode));
                Assert.That(rootNode.Operand0, Is.Zero);
                Assert.That(rootNode.Operand1, Is.EqualTo(1));
                Assert.That(rootNode.Weight, Is.EqualTo(source.Operators[0].Weight));
                Assert.That(
                    graph.Nodes[rootNode.Operand0].Slab.BaseColor,
                    Is.EqualTo(source.Slabs[0].BaseColor));
                Assert.That(
                    graph.Nodes[rootNode.Operand1].Slab.BaseColor,
                    Is.EqualTo(source.Slabs[1].BaseColor));

                ClosureTopology lowered = ClosureTopologyLowerer.Lower(
                    graph,
                    root,
                    source.Budget);
                Assert.That(
                    lowered.Operators[0].Kind,
                    Is.EqualTo(source.Operators[0].Kind));
                Assert.That(
                    lowered.Slabs[0].IsTop,
                    Is.EqualTo(source.Slabs[0].IsTop));
                Assert.That(
                    lowered.Slabs[0].IsBottom,
                    Is.EqualTo(source.Slabs[0].IsBottom));
                Assert.That(
                    lowered.Slabs[1].IsTop,
                    Is.EqualTo(source.Slabs[1].IsTop));
                Assert.That(
                    lowered.Slabs[1].IsBottom,
                    Is.EqualTo(source.Slabs[1].IsBottom));
            }
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
            Assert.That(module.Values.Owns(module.Outputs.CoverageValue), Is.True);
            Assert.That(module.Outputs.CoverageValue.Type, Is.EqualTo(MaterialValueType.Float));
            Assert.That(module.Outputs.Emission.Type, Is.EqualTo(MaterialValueType.Float3));
            Assert.That(
                module.MaterialFeatures,
                Is.EqualTo(MaterialFeatureMask.AlphaClip));
            Assert.That(
                module.ShadingModels,
                Is.EqualTo(
                    MaterialShadingModelMask.StandardLit
                    | MaterialShadingModelMask.Unlit));
            Assert.That(module.Verification.IsValid, Is.True);
            Assert.That(module.Verification.Diagnostics, Is.Empty);
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
                Does.Contain("material_features=AlphaClip"));
            Assert.That(
                module.GetDebugDump(),
                Does.Contain("shading_models=StandardLit, Unlit"));
            var noMaterialFeatures = new MaterialIRModule(
                module.Values,
                module.Outputs,
                module.ClosureGraph,
                module.SurfaceClosure,
                module.Topology.Budget,
                MaterialFeatureMask.None,
                module.ShadingModels);
            Assert.That(noMaterialFeatures.StructuralHash, Is.Not.EqualTo(module.StructuralHash));
            MaterialIRVerificationException unknownFeatureException =
                Assert.Throws<MaterialIRVerificationException>(() => new MaterialIRModule(
                    module.Values,
                    module.Outputs,
                    module.ClosureGraph,
                    module.SurfaceClosure,
                    module.Topology.Budget,
                    (MaterialFeatureMask) (1 << 1),
                    module.ShadingModels));
            AssertDiagnostic(
                unknownFeatureException.Diagnostics,
                MaterialIRDiagnosticCodes.UnknownMaterialFeature);
            Assert.Throws<InvalidOperationException>(() =>
                module.Values.Parameter(MaterialParameter.Roughness));

            var foreignValues = new MaterialValueIR();
            MaterialValue foreignCoverage =
                foreignValues.Parameter(MaterialParameter.BaseColor);
            MaterialIRVerificationException outputException =
                Assert.Throws<MaterialIRVerificationException>(() => new MaterialIRModule(
                    module.Values,
                    new MaterialOutputRoots(
                        foreignCoverage,
                        module.Outputs.AlphaClipThreshold,
                        module.Outputs.Emission),
                    module.ClosureGraph,
                    module.SurfaceClosure,
                    module.Topology.Budget,
                    module.MaterialFeatures,
                    module.ShadingModels));
            AssertDiagnostic(
                outputException.Diagnostics,
                MaterialIRDiagnosticCodes.OutputNotOwned);
        }

        [Test]
        public void MaterialIRModule_CanonicalizesAcrossValueAllocationAndDeclarationOrder()
        {
            MaterialIRModule first = BuildCanonicalHashModule(useAlternateValueOrder: false);
            MaterialIRModule reordered = BuildCanonicalHashModule(useAlternateValueOrder: true);

            Assert.That(first.Values.NodeCount, Is.EqualTo(reordered.Values.NodeCount));
            CollectionAssert.AreEqual(first.Values.Nodes, reordered.Values.Nodes);
            CollectionAssert.AreEqual(
                first.Values.ParameterDeclarations,
                reordered.Values.ParameterDeclarations);
            CollectionAssert.AreEqual(
                first.Values.ResourceDeclarations,
                reordered.Values.ResourceDeclarations);
            CollectionAssert.AreEqual(
                first.CanonicalIR.Payload,
                reordered.CanonicalIR.Payload);
            Assert.That(first.CanonicalIR.PayloadEquals(reordered.CanonicalIR), Is.True);
            Assert.That(first.GetDebugDump(), Is.EqualTo(reordered.GetDebugDump()));
            Assert.That(first.StructuralHash, Is.EqualTo(reordered.StructuralHash));
            Assert.That(
                reordered.Values.Nodes.Any(node =>
                    node.Opcode == MaterialValueOpcode.Constant
                    && node.Type == MaterialValueType.Float
                    && node.Constant.x == 123.0f),
                Is.False,
                "Canonical IR must not retain the deliberately unreachable constant.");
        }

        [Test]
        public void MaterialIRModule_CanonicalizesCommutativeAddAndMergesEquivalentSubgraphs()
        {
            MaterialIRModule reused = BuildBinaryCanonicalModule(
                MaterialValueOpcode.Add,
                reversePrimaryOperands: false,
                emitOppositeOrderForAlpha: false);
            MaterialIRModule swappedDuplicate = BuildBinaryCanonicalModule(
                MaterialValueOpcode.Add,
                reversePrimaryOperands: false,
                emitOppositeOrderForAlpha: true);
            MaterialIRModule swappedOnly = BuildBinaryCanonicalModule(
                MaterialValueOpcode.Add,
                reversePrimaryOperands: true,
                emitOppositeOrderForAlpha: false);

            Assert.That(reused.CanonicalIR.PayloadEquals(swappedDuplicate.CanonicalIR), Is.True);
            Assert.That(reused.CanonicalIR.PayloadEquals(swappedOnly.CanonicalIR), Is.True);
            CollectionAssert.AreEqual(
                reused.CanonicalIR.Payload,
                swappedDuplicate.CanonicalIR.Payload);
            CollectionAssert.AreEqual(
                reused.CanonicalIR.Payload,
                swappedOnly.CanonicalIR.Payload);
            CollectionAssert.AreEqual(reused.Values.Nodes, swappedDuplicate.Values.Nodes);
            CollectionAssert.AreEqual(reused.Values.Nodes, swappedOnly.Values.Nodes);
            Assert.That(reused.StructuralHash, Is.EqualTo(swappedDuplicate.StructuralHash));
            Assert.That(
                swappedDuplicate.Values.Nodes.Count(node =>
                    node.Opcode == MaterialValueOpcode.Add),
                Is.EqualTo(1));
            Assert.That(
                swappedDuplicate.Outputs.CoverageValue.Index,
                Is.EqualTo(swappedDuplicate.Outputs.AlphaClipThreshold.Index));
        }

        [Test]
        public void MaterialIRModule_PreservesOrderedSubtractOperands()
        {
            MaterialIRModule forward = BuildBinaryCanonicalModule(
                MaterialValueOpcode.Subtract,
                reversePrimaryOperands: false,
                emitOppositeOrderForAlpha: false);
            MaterialIRModule reversed = BuildBinaryCanonicalModule(
                MaterialValueOpcode.Subtract,
                reversePrimaryOperands: true,
                emitOppositeOrderForAlpha: false);

            Assert.That(forward.CanonicalIR.PayloadEquals(reversed.CanonicalIR), Is.False);
            CollectionAssert.AreNotEqual(
                forward.CanonicalIR.Payload,
                reversed.CanonicalIR.Payload);
            Assert.That(forward.StructuralHash, Is.Not.EqualTo(reversed.StructuralHash));
        }

        [Test]
        public void MaterialIRModule_PrunesUnreferencedNormalBasisWithoutChangingIdentity()
        {
            MaterialIRModule baseline = BuildCanonicalHashModule(
                useAlternateValueOrder: false);
            MaterialIRModule withUnusedNormalBasis = BuildCanonicalHashModule(
                useAlternateValueOrder: false,
                includeUnusedNormalBasis: true);

            Assert.That(withUnusedNormalBasis.Topology.NormalBases, Has.Count.EqualTo(1));
            Assert.That(
                withUnusedNormalBasis.Values.Nodes.Any(node =>
                    node.Opcode == MaterialValueOpcode.Normalize),
                Is.False);
            Assert.That(
                baseline.CanonicalIR.PayloadEquals(withUnusedNormalBasis.CanonicalIR),
                Is.True);
            CollectionAssert.AreEqual(
                baseline.CanonicalIR.Payload,
                withUnusedNormalBasis.CanonicalIR.Payload);
            CollectionAssert.AreEqual(
                baseline.Values.Nodes,
                withUnusedNormalBasis.Values.Nodes);
            Assert.That(
                baseline.StructuralHash,
                Is.EqualTo(withUnusedNormalBasis.StructuralHash));
            Assert.That(
                baseline.GetDebugDump(),
                Is.EqualTo(withUnusedNormalBasis.GetDebugDump()));
        }

        [Test]
        public void MaterialIRModule_DoesNotApplyAssociativeRewrites()
        {
            MaterialIRModule leftAssociated = BuildAssociativeCanonicalModule(
                leftAssociated: true);
            MaterialIRModule rightAssociated = BuildAssociativeCanonicalModule(
                leftAssociated: false);

            Assert.That(
                leftAssociated.CanonicalIR.PayloadEquals(rightAssociated.CanonicalIR),
                Is.False);
            CollectionAssert.AreNotEqual(
                leftAssociated.CanonicalIR.Payload,
                rightAssociated.CanonicalIR.Payload);
            Assert.That(
                leftAssociated.StructuralHash,
                Is.Not.EqualTo(rightAssociated.StructuralHash));
        }

        [Test]
        public void MaterialIRModule_CanonicalPayloadIsDefensivelyCopied()
        {
            MaterialIRModule module = BuildCanonicalHashModule(
                useAlternateValueOrder: false);
            byte[] originalPayload = module.CanonicalIR.Payload;
            byte[] exposedPayload = module.CanonicalIR.Payload;
            ulong originalHash = module.CanonicalIR.PayloadHash;

            Assert.That(exposedPayload, Is.Not.SameAs(originalPayload));
            exposedPayload[0] ^= byte.MaxValue;

            CollectionAssert.AreEqual(originalPayload, module.CanonicalIR.Payload);
            Assert.That(module.CanonicalIR.PayloadHash, Is.EqualTo(originalHash));
            Assert.That(module.CanonicalIR.PayloadEquals(originalPayload), Is.True);
        }

        [Test]
        public void MaterialIRModule_CanonicalizationIsIdempotent()
        {
            MaterialIRModule first = BuildCanonicalHashModule(
                useAlternateValueOrder: true);
            var second = new MaterialIRModule(
                first.Values,
                first.Outputs,
                first.ClosureGraph,
                first.SurfaceClosure,
                first.Topology.Budget,
                first.MaterialFeatures,
                first.ShadingModels);

            CollectionAssert.AreEqual(first.Values.Nodes, second.Values.Nodes);
            CollectionAssert.AreEqual(
                first.CanonicalIR.Payload,
                second.CanonicalIR.Payload);
            Assert.That(first.CanonicalIR.PayloadEquals(second.CanonicalIR), Is.True);
            Assert.That(first.SemanticHash, Is.EqualTo(second.SemanticHash));
            Assert.That(first.GetDebugDump(), Is.EqualTo(second.GetDebugDump()));
        }

        [Test]
        public void MaterialIRModule_TopologyBudgetIsNotMaterialSemantics()
        {
            MaterialIRModule baseline = BuildCanonicalHashModule(
                useAlternateValueOrder: false);
            var roomyBudget = new ClosureTopologyBudget(
                maxClosureCount: 4,
                maxOperatorCount: 3);
            var roomy = new MaterialIRModule(
                baseline.Values,
                baseline.Outputs,
                baseline.ClosureGraph,
                baseline.SurfaceClosure,
                roomyBudget,
                baseline.MaterialFeatures,
                baseline.ShadingModels);

            Assert.That(
                baseline.Topology.Budget.MaxClosureCount,
                Is.Not.EqualTo(roomy.Topology.Budget.MaxClosureCount));
            Assert.That(baseline.CanonicalIR.PayloadEquals(roomy.CanonicalIR), Is.True);
            Assert.That(baseline.SemanticHash, Is.EqualTo(roomy.SemanticHash));
            Assert.That(baseline.GetDebugDump(), Is.EqualTo(roomy.GetDebugDump()));
        }

        [Test]
        public void MaterialValueIR_CanonicalCsePreservesRawConstantBits()
        {
            var valueIR = new MaterialValueIR();
            MaterialValue positiveZero = valueIR.Constant(math.asfloat(0x00000000u));
            MaterialValue negativeZero = valueIR.Constant(math.asfloat(0x80000000u));
            MaterialValue firstNaN = valueIR.Constant(math.asfloat(0x7FC00001u));
            MaterialValue duplicateNaN = valueIR.Constant(math.asfloat(0x7FC00001u));
            MaterialValue distinctNaN = valueIR.Constant(math.asfloat(0x7FC00002u));

            Assert.That(positiveZero, Is.Not.EqualTo(negativeZero));
            Assert.That(firstNaN, Is.EqualTo(duplicateNaN));
            Assert.That(firstNaN, Is.Not.EqualTo(distinctNaN));
            Assert.That(valueIR.NodeCount, Is.EqualTo(4));

            MaterialIRModule positiveZeroModule = BuildConstantCoverageModule(
                0x00000000u);
            MaterialIRModule negativeZeroModule = BuildConstantCoverageModule(
                0x80000000u);
            Assert.That(
                positiveZeroModule.CanonicalIR.PayloadEquals(
                    negativeZeroModule.CanonicalIR),
                Is.False);
            Assert.That(
                positiveZeroModule.SemanticHash,
                Is.Not.EqualTo(negativeZeroModule.SemanticHash));
        }

        [Test]
        public void CompilationContract_BuiltinCatalogHasFrozenAbi()
        {
            Assert.That(MaterialProgramContract.IRSchemaVersion, Is.EqualTo(3u));
            Assert.That(MaterialProgramContract.CanonicalIRVersion, Is.EqualTo(2u));
            Assert.That(MaterialProgramContract.ClosureExpressionVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.StageLIRVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.DerivativeLegalizationVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.ProgramLoweringVersion, Is.EqualTo(4u));
            Assert.That(MaterialProgramContract.GenericLayoutVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.LayoutFingerprintVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.DeferredExportContractVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.DeferredExportFingerprintVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.ProgramCatalogVersion, Is.EqualTo(3u));
            Assert.That(MaterialProgramContract.ProgramCatalogManifestVersion, Is.EqualTo(2u));
            Assert.That(MaterialProgramContract.SemanticHashVersion, Is.EqualTo(4u));
            Assert.That(MaterialProgramContract.CompiledHashVersion, Is.EqualTo(6u));
            Assert.That(MaterialProgramContract.CompilerVersion, Is.EqualTo(11u));
            Assert.That(MaterialProgramContract.NativeTemplateBackendVersion, Is.EqualTo(6u));
            Assert.That(MaterialProgramContract.CoverageHlslArtifactVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.CoverageHlslBackendVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.SurfaceHlslArtifactVersion, Is.EqualTo(3u));
            Assert.That(MaterialProgramContract.SurfaceHlslBackendVersion, Is.EqualTo(4u));
            Assert.That(MaterialProgramContract.VerifierVersion, Is.EqualTo(3u));
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
                0xF934E6AEDE283181ul,
                0x7B58B734ED0EDE45ul,
                0x2E8FA4336811E656ul,
            };
            var compiledHashes = new List<ulong>();

            for (int programIndex = 0; programIndex < runtimePrograms.Length; programIndex++)
            {
                var programID = (VividMaterialProgramID) (uint) programIndex;
                CompiledMaterialProgram program =
                    GPUDrivenMaterialCompiler.GetMaterialProgram(programID);
                Assert.That(
                    GPUDrivenMaterialCompiler.GetCatalogedMaterialProgram(programID)
                        .ProgramID,
                    Is.EqualTo(programID));
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
                Assert.That(
                    program.Module.CanonicalIR.PayloadHash,
                    Is.EqualTo(expectedSemanticHashes[programIndex]));
                Assert.That(program.Module.StructuralHash, Is.EqualTo(program.SemanticHash.Value));
                Assert.That(
                    program.CompiledHash.Version,
                    Is.EqualTo(MaterialProgramContract.CompiledHashVersion));
                Assert.That(program.CompiledHash.Value, Is.Not.Zero);
                compiledHashes.Add(program.CompiledHash.Value);

                if (programIndex == 0)
                    AssertStandardMaterialLayout(program);
                else
                    AssertDualSlabMaterialLayout(program);
            }

            CollectionAssert.AllItemsAreUnique(expectedSemanticHashes);
            CollectionAssert.AllItemsAreUnique(compiledHashes);
        }

        [Test]
        public void CompilationContract_BuiltinProgramsDeclareExactDeferredExports()
        {
            CompiledMaterialProgram standard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    MaterialProgramContract.RuntimeAbiVersion);
            CompiledMaterialProgram rebuiltStandard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    MaterialProgramContract.RuntimeAbiVersion);
            CompiledMaterialProgram horizontal =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    MaterialProgramContract.RuntimeAbiVersion,
                    VividDualSlabOperator.HorizontalMix);
            CompiledMaterialProgram vertical =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    MaterialProgramContract.RuntimeAbiVersion,
                    VividDualSlabOperator.VerticalLayer);

            const MaterialShadingModelMask builtinShadingModels =
                MaterialShadingModelMask.StandardLit
                | MaterialShadingModelMask.Unlit;
            const MaterialDeferredExportPayloadFlags corePayload =
                MaterialDeferredExportPayloadFlags.SurfaceSummary
                | MaterialDeferredExportPayloadFlags.DiffuseIrradiance;
            const MaterialDeferredExportPolicyFlags corePolicy =
                MaterialDeferredExportPolicyFlags.DynamicDiffuseIrradiance
                | MaterialDeferredExportPolicyFlags.ReceiveSsrOnFastSlab
                | MaterialDeferredExportPolicyFlags.ReceiveDecals;

            AssertDeferredExportContract(
                standard,
                MaterialDeferredExportSidecarAbi.None,
                builtinShadingModels,
                MaterialDeferredExportLitClass.FastSlab,
                1u,
                MaterialDeferredExportTopology.None,
                corePayload,
                corePolicy);
            AssertDeferredExportContract(
                horizontal,
                MaterialDeferredExportSidecarAbi.DualSlabV1,
                builtinShadingModels,
                MaterialDeferredExportLitClass.DualSlab,
                2u,
                MaterialDeferredExportTopology.HorizontalMix,
                corePayload
                    | MaterialDeferredExportPayloadFlags.DualSlabSidecar
                    | MaterialDeferredExportPayloadFlags.SharedNormalAndAmbientOcclusion,
                corePolicy
                    | MaterialDeferredExportPolicyFlags.FastSlabWhenSidecarEmpty);
            AssertDeferredExportContract(
                vertical,
                MaterialDeferredExportSidecarAbi.DualSlabV1,
                builtinShadingModels,
                MaterialDeferredExportLitClass.DualSlab,
                2u,
                MaterialDeferredExportTopology.VerticalLayer,
                corePayload
                    | MaterialDeferredExportPayloadFlags.DualSlabSidecar
                    | MaterialDeferredExportPayloadFlags.SharedNormalAndAmbientOcclusion,
                corePolicy
                    | MaterialDeferredExportPolicyFlags.FastSlabWhenSidecarEmpty);

            Assert.That(
                rebuiltStandard.DeferredExportContract.Fingerprint,
                Is.EqualTo(standard.DeferredExportContract.Fingerprint));
            Assert.That(
                horizontal.DeferredExportContract.Fingerprint,
                Is.Not.EqualTo(standard.DeferredExportContract.Fingerprint));
            Assert.That(
                vertical.DeferredExportContract.Fingerprint,
                Is.Not.EqualTo(horizontal.DeferredExportContract.Fingerprint));
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

            Assert.That(
                first.Module.CanonicalIR.PayloadEquals(reordered.Module.CanonicalIR),
                Is.True);
            CollectionAssert.AreEqual(
                first.Module.CanonicalIR.Payload,
                reordered.Module.CanonicalIR.Payload);
            Assert.That(first.SemanticHash, Is.EqualTo(reordered.SemanticHash));
            Assert.That(first.CompiledHash, Is.EqualTo(reordered.CompiledHash));
            CollectionAssert.AreEqual(
                GetRuntimeProgramDataWords(first.RuntimeData),
                GetRuntimeProgramDataWords(reordered.RuntimeData));
            AssertStandardMaterialLayout(first);
            AssertStandardMaterialLayout(reordered);
            CollectionAssert.AreEqual(
                GetValueSliceSignature(first.CoverageProgram.ValueSlice),
                GetValueSliceSignature(reordered.CoverageProgram.ValueSlice));
            CollectionAssert.AreEqual(
                GetValueSliceSignature(first.SurfaceProgram.ValueSlice),
                GetValueSliceSignature(reordered.SurfaceProgram.ValueSlice));
            Assert.That(
                first.Diagnostics.GetDebugDump(),
                Is.EqualTo(reordered.Diagnostics.GetDebugDump()));
        }

        [Test]
        public void CompilationContract_ProgramIdIsNotCompiledIdentity()
        {
            CompiledMaterialProgram prototype =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    MaterialProgramContract.RuntimeAbiVersion);
            MaterialIRModule prototypeModule = prototype.Module;
            var unlitOnly = new MaterialIRModule(
                prototypeModule.Values,
                prototypeModule.Outputs,
                prototypeModule.ClosureGraph,
                prototypeModule.SurfaceClosure,
                prototypeModule.Topology.Budget,
                MaterialFeatureMask.None,
                MaterialShadingModelMask.Unlit);
            CompiledMaterialProgram compiledUnlitOnly =
                CompiledMaterialProgram.Compile(
                    unlitOnly,
                    MaterialProgramContract.RuntimeAbiVersion);

            AssertRuntimeProgramData(
                compiledUnlitOnly.RuntimeData,
                new uint[] { 1u, 0u, 0u, 0u, 0u, 0u, 7u, 0u });
            Assert.That(
                compiledUnlitOnly.DeferredExportContract.ShadingModels,
                Is.EqualTo(MaterialShadingModelMask.Unlit));
            Assert.That(
                compiledUnlitOnly.DeferredExportContract.LitClass,
                Is.EqualTo(MaterialDeferredExportLitClass.None));
            Assert.That(compiledUnlitOnly.SemanticHash, Is.Not.EqualTo(prototype.SemanticHash));
            Assert.That(compiledUnlitOnly.CompiledHash, Is.Not.EqualTo(prototype.CompiledHash));
            MaterialProgramCatalog catalog = MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.ForProgram("Lit", prototype),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "UnlitOnly",
                    compiledUnlitOnly));
            Assert.That(
                catalog.GetEntry((VividMaterialProgramID) 0u).ProgramID,
                Is.Not.EqualTo(catalog.GetEntry((VividMaterialProgramID) 1u).ProgramID));
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
        public void StageLIR_ProjectsCanonicalValuesPerStageAndEliminatesAbstractDerivatives()
        {
            CompiledMaterialProgram standard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            MaterialStageLIR coverage = standard.CoverageProgram.StageLIR;
            MaterialStageLIR surface = standard.SurfaceProgram.StageLIR;

            Assert.That(coverage.Stage, Is.EqualTo(MaterialEvaluationStage.Coverage));
            Assert.That(
                coverage.ExecutionModel,
                Is.EqualTo(MaterialStageExecutionModel.RasterFragment));
            Assert.That(
                coverage.DerivativeProvider,
                Is.EqualTo(MaterialStageDerivativeProvider.NativeQuad));
            Assert.That(surface.Stage, Is.EqualTo(MaterialEvaluationStage.Surface));
            Assert.That(
                surface.ExecutionModel,
                Is.EqualTo(MaterialStageExecutionModel.VisibilityResolve));
            Assert.That(
                surface.DerivativeProvider,
                Is.EqualTo(MaterialStageDerivativeProvider.VisibilityBuffer));
            Assert.That(coverage.IsFrozen, Is.True);
            Assert.That(surface.IsFrozen, Is.True);
            Assert.That(coverage.Roots, Has.Count.EqualTo(2));
            Assert.That(surface.Roots, Has.Count.EqualTo(6));
            Assert.That(
                coverage.Roots.All(coverage.Owns),
                Is.True);
            Assert.That(
                surface.Roots.All(surface.Owns),
                Is.True);

            AssertLegalizedGradientInputs(coverage);
            AssertLegalizedGradientInputs(surface);
            Assert.That(
                coverage.Nodes.Count(node =>
                    node.Opcode == MaterialStageLIROpcode.StageInput
                    && (node.Semantic == (int) MaterialStageInput.GeometryNormalWS
                        || node.Semantic == (int) MaterialStageInput.GeometryTangentWS)),
                Is.Zero);
            Assert.That(
                surface.Nodes.Count(node =>
                    node.Opcode == MaterialStageLIROpcode.StageInput
                    && (node.Semantic == (int) MaterialStageInput.GeometryNormalWS
                        || node.Semantic == (int) MaterialStageInput.GeometryTangentWS)),
                Is.EqualTo(2));
            Assert.That(
                coverage.GetDebugDump(),
                Does.Contain("derivative_provider=NativeQuad"));
            Assert.That(
                surface.GetDebugDump(),
                Does.Contain("derivative_provider=VisibilityBuffer"));
        }

        [Test]
        public void StageLIR_LegalizesAffineUVAndUniformDerivatives()
        {
            var values = new MaterialValueIR();
            MaterialValue uv = values.ExternalInput(MaterialExternalInput.UV0);
            MaterialValue scale = values.Parameter(new MaterialParameterDeclaration(
                "UVScale",
                MaterialValueType.Float2));
            MaterialValue offset = values.Parameter(new MaterialParameterDeclaration(
                "UVOffset",
                MaterialValueType.Float2));
            MaterialValue scalarOffset = values.Parameter(new MaterialParameterDeclaration(
                "ScalarOffset",
                MaterialValueType.Float));
            MaterialValue blendFactor = values.Parameter(new MaterialParameterDeclaration(
                "BlendFactor",
                MaterialValueType.Float));
            MaterialValue saturatedScale = values.Saturate(scale);
            MaterialValue transformedUV = values.Add(
                values.Multiply(uv, saturatedScale),
                offset);
            MaterialValue transformedDdx = values.Ddx(transformedUV);
            MaterialValue directUVDdx = values.Ddx(uv);
            MaterialValue transformedDdy = values.Ddy(transformedUV);
            MaterialValue uniformDdx = values.Ddx(saturatedScale);
            MaterialValue dividedDdx = values.Ddx(values.Divide(uv, saturatedScale));
            MaterialValue uniformWeightLerpDdx = values.Ddx(
                values.Lerp(uv, offset, blendFactor));
            MaterialValue uvX = values.Swizzle(uv, MaterialSwizzleMask.X);
            MaterialValue varyingWeightLerpDdx = values.Ddx(
                values.Lerp(offset, scale, uvX));
            MaterialValue uniformCondition = values.Compare(
                scalarOffset,
                blendFactor,
                MaterialComparison.Less);
            MaterialValue selectDdx = values.Ddx(
                values.Select(uniformCondition, uv, offset));
            MaterialValue composedDdx = values.Ddx(values.Compose(uvX, scalarOffset));
            MaterialValue dottedDdx = values.Ddx(values.Dot(uv, saturatedScale));
            MaterialValue texture = values.TextureResource(MaterialTextureResource.BaseColor);
            MaterialValue sample = values.TextureSampleGrad(
                texture,
                transformedUV,
                transformedDdx,
                transformedDdy);
            values.Freeze();

            var slice = new MaterialValueSlice(
                values,
                sample,
                uniformDdx,
                directUVDdx,
                dividedDdx,
                uniformWeightLerpDdx,
                varyingWeightLerpDdx,
                selectDdx,
                composedDdx,
                dottedDdx);
            MaterialStageLIR coverage = MaterialStageLIRLowerer.Lower(
                slice,
                MaterialEvaluationStage.Coverage);
            MaterialStageLIR surface = MaterialStageLIRLowerer.Lower(
                slice,
                MaterialEvaluationStage.Surface);

            foreach (MaterialStageLIR stageLIR in new[] { coverage, surface })
            {
                MaterialStageLIRNode ddxNode =
                    stageLIR.GetNode(stageLIR.GetValue(transformedDdx));
                MaterialStageLIRNode ddyNode =
                    stageLIR.GetNode(stageLIR.GetValue(transformedDdy));
                MaterialStageLIRNode uniformDdxNode =
                    stageLIR.GetNode(stageLIR.GetValue(uniformDdx));
                Assert.That(ddxNode.Opcode, Is.EqualTo(MaterialStageLIROpcode.Multiply));
                Assert.That(ddyNode.Opcode, Is.EqualTo(MaterialStageLIROpcode.Multiply));
                Assert.That(uniformDdxNode.Opcode, Is.EqualTo(MaterialStageLIROpcode.Constant));
                Assert.That(
                    stageLIR.GetNode(stageLIR.GetValue(directUVDdx)).Opcode,
                    Is.EqualTo(MaterialStageLIROpcode.StageInput));
                Assert.That(
                    stageLIR.GetNode(stageLIR.GetValue(dividedDdx)).Opcode,
                    Is.EqualTo(MaterialStageLIROpcode.Divide));
                Assert.That(
                    stageLIR.GetNode(stageLIR.GetValue(uniformWeightLerpDdx)).Opcode,
                    Is.EqualTo(MaterialStageLIROpcode.Lerp));
                Assert.That(
                    stageLIR.GetNode(stageLIR.GetValue(varyingWeightLerpDdx)).Opcode,
                    Is.EqualTo(MaterialStageLIROpcode.Lerp));
                Assert.That(
                    stageLIR.GetNode(stageLIR.GetValue(selectDdx)).Opcode,
                    Is.EqualTo(MaterialStageLIROpcode.Select));
                Assert.That(
                    stageLIR.GetNode(stageLIR.GetValue(composedDdx)).Opcode,
                    Is.EqualTo(MaterialStageLIROpcode.Compose));
                Assert.That(
                    stageLIR.GetNode(stageLIR.GetValue(dottedDdx)).Opcode,
                    Is.EqualTo(MaterialStageLIROpcode.Dot));
                Assert.That(
                    stageLIR.Nodes.Any(node =>
                        node.Opcode == MaterialStageLIROpcode.StageInput
                        && node.Semantic == (int) MaterialStageInput.UV0Ddx),
                    Is.True);
                Assert.That(
                    stageLIR.Nodes.Any(node =>
                        node.Opcode == MaterialStageLIROpcode.StageInput
                        && node.Semantic == (int) MaterialStageInput.UV0Ddy),
                    Is.True);
                Assert.That(MaterialIRVerifier.VerifyStageLIR(stageLIR).IsValid, Is.True);
            }

            MaterialStageLIR uniformOnly = MaterialStageLIRLowerer.Lower(
                new MaterialValueSlice(values, uniformDdx),
                MaterialEvaluationStage.Surface);
            MaterialValueRequirements uniformRequirements =
                MaterialValueRequirements.Collect(uniformOnly);
            Assert.That(uniformOnly.NodeCount, Is.EqualTo(1));
            Assert.That(
                uniformOnly.GetNode(uniformOnly.Roots[0]).Opcode,
                Is.EqualTo(MaterialStageLIROpcode.Constant));
            Assert.That(uniformOnly.TryGetValue(scale, out _), Is.False);
            Assert.That(uniformRequirements.ParameterDeclarations, Is.Empty);
            Assert.That(uniformRequirements.StageInputs, Is.Empty);
        }

        [Test]
        public void StageLIR_RejectsUnavailableInputsAndUndefinedDerivatives()
        {
            var coverageValues = new MaterialValueIR();
            MaterialValue normal = coverageValues.ExternalInput(
                MaterialExternalInput.GeometryNormalWS);
            coverageValues.Freeze();
            MaterialIRVerificationException inputException =
                Assert.Throws<MaterialIRVerificationException>(() =>
                    MaterialStageLIRLowerer.Lower(
                        new MaterialValueSlice(coverageValues, normal),
                        MaterialEvaluationStage.Coverage));
            AssertDiagnostic(
                inputException.Diagnostics,
                MaterialIRDiagnosticCodes.StageInputUnavailable,
                normal.Index);

            var derivativeValues = new MaterialValueIR();
            MaterialValue uv = derivativeValues.ExternalInput(MaterialExternalInput.UV0);
            MaterialValue texture = derivativeValues.TextureResource(
                MaterialTextureResource.BaseColor);
            MaterialValue sample = derivativeValues.TextureSampleGrad(
                texture,
                uv,
                derivativeValues.Ddx(uv),
                derivativeValues.Ddy(uv));
            MaterialValue sampleDdx = derivativeValues.Ddx(sample);
            MaterialValue varyingProductDdx = derivativeValues.Ddx(
                derivativeValues.Multiply(uv, uv));
            derivativeValues.Freeze();
            MaterialIRVerificationException derivativeException =
                Assert.Throws<MaterialIRVerificationException>(() =>
                    MaterialStageLIRLowerer.Lower(
                        new MaterialValueSlice(derivativeValues, sampleDdx),
                        MaterialEvaluationStage.Surface));
            AssertDiagnostic(
                derivativeException.Diagnostics,
                MaterialIRDiagnosticCodes.DerivativeSourceCannotBeLegalized,
                sampleDdx.Index);
            foreach (MaterialEvaluationStage stage in new[]
                     {
                         MaterialEvaluationStage.Coverage,
                         MaterialEvaluationStage.Surface,
                     })
            {
                MaterialIRVerificationException varyingProductException =
                    Assert.Throws<MaterialIRVerificationException>(() =>
                        MaterialStageLIRLowerer.Lower(
                            new MaterialValueSlice(
                                derivativeValues,
                                varyingProductDdx),
                            stage));
                AssertDiagnostic(
                    varyingProductException.Diagnostics,
                    MaterialIRDiagnosticCodes.DerivativeSourceCannotBeLegalized,
                    varyingProductDdx.Index);
            }
        }

        [Test]
        public void StageLIR_PreservesAlreadyExplicitConstantGradients()
        {
            var values = new MaterialValueIR();
            MaterialValue texture = values.TextureResource(MaterialTextureResource.BaseColor);
            MaterialValue uv = values.ExternalInput(MaterialExternalInput.UV0);
            MaterialValue gradient = values.Constant(new float2(0.25f, 0.5f));
            MaterialValue sample = values.TextureSampleGrad(texture, uv, gradient, gradient);
            values.Freeze();

            MaterialStageLIR stageLIR = MaterialStageLIRLowerer.Lower(
                new MaterialValueSlice(values, sample),
                MaterialEvaluationStage.Surface);
            MaterialStageLIRNode sampleNode =
                stageLIR.GetNode(stageLIR.GetValue(sample));

            Assert.That(sampleNode.Opcode, Is.EqualTo(MaterialStageLIROpcode.TextureSampleGrad));
            Assert.That(
                stageLIR.Nodes[sampleNode.Operand2].Opcode,
                Is.EqualTo(MaterialStageLIROpcode.Constant));
            Assert.That(sampleNode.Operand3, Is.EqualTo(sampleNode.Operand2));
        }

        [Test]
        public void StageLIRVerifier_RejectsVectorCompareOperands()
        {
            var values = new MaterialValueIR();
            MaterialValue left = values.Constant(new float2(1.0f, 2.0f));
            MaterialValue right = values.Constant(new float2(3.0f, 4.0f));
            MaterialValue comparisonResult = values.Constant(true);
            values.Freeze();
            var slice = new MaterialValueSlice(values, left, right, comparisonResult);
            int[] sourceValueMap = Enumerable.Repeat(-1, values.NodeCount).ToArray();
            sourceValueMap[left.Index] = 0;
            sourceValueMap[right.Index] = 1;
            sourceValueMap[comparisonResult.Index] = 2;
            var stageLIR = new MaterialStageLIR(
                MaterialEvaluationStage.Coverage,
                MaterialStageExecutionModel.RasterFragment,
                MaterialStageDerivativeProvider.NativeQuad,
                slice,
                new[]
                {
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.Constant,
                        MaterialValueType.Float2,
                        semantic: 0,
                        constant: new float4(1.0f, 2.0f, 0.0f, 0.0f),
                        sourceNodeIndex: left.Index,
                        operandCount: 0,
                        operand0: -1,
                        operand1: -1,
                        operand2: -1,
                        operand3: -1),
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.Constant,
                        MaterialValueType.Float2,
                        semantic: 0,
                        constant: new float4(3.0f, 4.0f, 0.0f, 0.0f),
                        sourceNodeIndex: right.Index,
                        operandCount: 0,
                        operand0: -1,
                        operand1: -1,
                        operand2: -1,
                        operand3: -1),
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.Compare,
                        MaterialValueType.Bool,
                        semantic: (int) MaterialComparison.Less,
                        constant: default,
                        sourceNodeIndex: comparisonResult.Index,
                        operandCount: 2,
                        operand0: 0,
                        operand1: 1,
                        operand2: -1,
                        operand3: -1),
                },
                new[] { 0, 1, 2 },
                sourceValueMap);

            MaterialIRVerificationResult result =
                MaterialIRVerifier.VerifyStageLIRStructure(stageLIR);
            MaterialIRDiagnostic diagnostic = result.Diagnostics.First(entry =>
                entry.Code == MaterialIRDiagnosticCodes.InvalidStageLIR
                && entry.Message.Contains(
                    "compare operands must both be scalar Float"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(diagnostic.NodeIndex, Is.EqualTo(comparisonResult.Index));
        }

        [Test]
        public void StageLIRVerifier_RejectsMalformedTypesPayloadProfilesAndDeadNodes()
        {
            var values = new MaterialValueIR();
            MaterialValue parameter = values.Parameter(new MaterialParameterDeclaration(
                "VerifierParameter",
                MaterialValueType.Float));
            values.Freeze();
            var slice = new MaterialValueSlice(values, parameter);

            int[] malformedMap = Enumerable.Repeat(-1, values.NodeCount).ToArray();
            malformedMap[parameter.Index] = 0;
            var malformedParameter = new MaterialStageLIR(
                MaterialEvaluationStage.Coverage,
                MaterialStageExecutionModel.RasterFragment,
                MaterialStageDerivativeProvider.NativeQuad,
                slice,
                new[]
                {
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.Parameter,
                        MaterialValueType.Float,
                        semantic: 999,
                        constant: default,
                        sourceNodeIndex: parameter.Index,
                        operandCount: 0,
                        operand0: -1,
                        operand1: -1,
                        operand2: -1,
                        operand3: -1),
                },
                new[] { 0 },
                malformedMap);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyStageLIR(malformedParameter).Diagnostics,
                MaterialIRDiagnosticCodes.InvalidStageLIR,
                parameter.Index);

            var semanticMismatch = new MaterialStageLIR(
                MaterialEvaluationStage.Coverage,
                MaterialStageExecutionModel.RasterFragment,
                MaterialStageDerivativeProvider.NativeQuad,
                slice,
                new[]
                {
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.Constant,
                        MaterialValueType.Float,
                        semantic: 0,
                        constant: default,
                        sourceNodeIndex: parameter.Index,
                        operandCount: 0,
                        operand0: -1,
                        operand1: -1,
                        operand2: -1,
                        operand3: -1),
                },
                new[] { 0 },
                malformedMap);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyStageLIR(semanticMismatch).Diagnostics,
                MaterialIRDiagnosticCodes.InvalidStageLIR,
                parameter.Index);

            var derivativeValues = new MaterialValueIR();
            MaterialValue uv = derivativeValues.ExternalInput(MaterialExternalInput.UV0);
            MaterialValue uvDdx = derivativeValues.Ddx(uv);
            derivativeValues.Freeze();
            int[] derivativeMap = Enumerable.Repeat(
                -1,
                derivativeValues.NodeCount).ToArray();
            derivativeMap[uvDdx.Index] = 0;
            var axisMismatch = new MaterialStageLIR(
                MaterialEvaluationStage.Coverage,
                MaterialStageExecutionModel.RasterFragment,
                MaterialStageDerivativeProvider.NativeQuad,
                new MaterialValueSlice(derivativeValues, uvDdx),
                new[]
                {
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.StageInput,
                        MaterialValueType.Float2,
                        semantic: (int) MaterialStageInput.UV0Ddy,
                        constant: default,
                        sourceNodeIndex: uvDdx.Index,
                        operandCount: 0,
                        operand0: -1,
                        operand1: -1,
                        operand2: -1,
                        operand3: -1),
                },
                new[] { 0 },
                derivativeMap);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyStageLIR(axisMismatch).Diagnostics,
                MaterialIRDiagnosticCodes.InvalidStageLIR,
                uvDdx.Index);

            var affineValues = new MaterialValueIR();
            MaterialValue affineUV = affineValues.ExternalInput(
                MaterialExternalInput.UV0);
            MaterialValue affineScale = affineValues.Parameter(
                new MaterialParameterDeclaration(
                    "AffineScale",
                    MaterialValueType.Float2));
            MaterialValue affineDdx = affineValues.Ddx(
                affineValues.Multiply(affineUV, affineScale));
            affineValues.Freeze();
            int[] affineMap = Enumerable.Repeat(
                -1,
                affineValues.NodeCount).ToArray();
            affineMap[affineDdx.Index] = 0;
            var incompleteAffineRecipe = new MaterialStageLIR(
                MaterialEvaluationStage.Coverage,
                MaterialStageExecutionModel.RasterFragment,
                MaterialStageDerivativeProvider.NativeQuad,
                new MaterialValueSlice(affineValues, affineDdx),
                new[]
                {
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.StageInput,
                        MaterialValueType.Float2,
                        semantic: (int) MaterialStageInput.UV0Ddx,
                        constant: default,
                        sourceNodeIndex: affineDdx.Index,
                        operandCount: 0,
                        operand0: -1,
                        operand1: -1,
                        operand2: -1,
                        operand3: -1),
                },
                new[] { 0 },
                affineMap);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyStageLIR(incompleteAffineRecipe).Diagnostics,
                MaterialIRDiagnosticCodes.InvalidStageLIR);

            var nonlinearValues = new MaterialValueIR();
            MaterialValue nonlinearUV = nonlinearValues.ExternalInput(
                MaterialExternalInput.UV0);
            MaterialValue nonlinearDdx = nonlinearValues.Ddx(
                nonlinearValues.Multiply(nonlinearUV, nonlinearUV));
            nonlinearValues.Freeze();
            int[] nonlinearMap = Enumerable.Repeat(
                -1,
                nonlinearValues.NodeCount).ToArray();
            nonlinearMap[nonlinearDdx.Index] = 0;
            var illegalSourceRecipe = new MaterialStageLIR(
                MaterialEvaluationStage.Coverage,
                MaterialStageExecutionModel.RasterFragment,
                MaterialStageDerivativeProvider.NativeQuad,
                new MaterialValueSlice(nonlinearValues, nonlinearDdx),
                new[]
                {
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.StageInput,
                        MaterialValueType.Float2,
                        semantic: (int) MaterialStageInput.UV0Ddx,
                        constant: default,
                        sourceNodeIndex: nonlinearDdx.Index,
                        operandCount: 0,
                        operand0: -1,
                        operand1: -1,
                        operand2: -1,
                        operand3: -1),
                },
                new[] { 0 },
                nonlinearMap);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyStageLIR(illegalSourceRecipe).Diagnostics,
                MaterialIRDiagnosticCodes.DerivativeSourceCannotBeLegalized,
                nonlinearDdx.Index);

            var malformedTypes = new MaterialStageLIR(
                MaterialEvaluationStage.Coverage,
                MaterialStageExecutionModel.RasterFragment,
                MaterialStageDerivativeProvider.NativeQuad,
                slice,
                new[]
                {
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.Parameter,
                        MaterialValueType.Float,
                        semantic: 0,
                        constant: default,
                        sourceNodeIndex: parameter.Index,
                        operandCount: 0,
                        operand0: -1,
                        operand1: -1,
                        operand2: -1,
                        operand3: -1),
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.Constant,
                        MaterialValueType.Float2,
                        semantic: 0,
                        constant: new float4(1.0f, 1.0f, 0.0f, 0.0f),
                        sourceNodeIndex: parameter.Index,
                        operandCount: 0,
                        operand0: -1,
                        operand1: -1,
                        operand2: -1,
                        operand3: -1),
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.Add,
                        MaterialValueType.Float,
                        semantic: 0,
                        constant: default,
                        sourceNodeIndex: parameter.Index,
                        operandCount: 2,
                        operand0: 0,
                        operand1: 1,
                        operand2: -1,
                        operand3: -1),
                },
                new[] { 2 },
                new[] { 2 });
            AssertDiagnostic(
                MaterialIRVerifier.VerifyStageLIR(malformedTypes).Diagnostics,
                MaterialIRDiagnosticCodes.InvalidStageLIR,
                parameter.Index);

            MaterialStageLIR valid = MaterialStageLIRLowerer.Lower(
                slice,
                MaterialEvaluationStage.Coverage);
            int[] validRoots = valid.Roots.Select(root => root.Index).ToArray();
            int[] validMap = Enumerable.Range(0, valid.SourceValueMapCount)
                .Select(valid.GetMappedNodeIndex)
                .ToArray();
            MaterialStageLIRNode[] deadNodes = valid.Nodes.Concat(new[]
            {
                new MaterialStageLIRNode(
                    MaterialStageLIROpcode.Constant,
                    MaterialValueType.Float,
                    semantic: 0,
                    constant: new float4(2.0f, 0.0f, 0.0f, 0.0f),
                    sourceNodeIndex: parameter.Index,
                    operandCount: 0,
                    operand0: -1,
                    operand1: -1,
                    operand2: -1,
                    operand3: -1),
            }).ToArray();
            var deadLIR = new MaterialStageLIR(
                valid.Stage,
                valid.ExecutionModel,
                valid.DerivativeProvider,
                slice,
                deadNodes,
                validRoots,
                validMap);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyStageLIR(deadLIR).Diagnostics,
                MaterialIRDiagnosticCodes.InvalidStageLIR,
                parameter.Index);

            var unknownStage = new MaterialStageLIR(
                (MaterialEvaluationStage) 99,
                valid.ExecutionModel,
                valid.DerivativeProvider,
                slice,
                valid.Nodes.ToArray(),
                validRoots,
                validMap);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyStageLIR(unknownStage).Diagnostics,
                MaterialIRDiagnosticCodes.InvalidStageLIR);

            var invalidRoot = new MaterialStageLIR(
                valid.Stage,
                valid.ExecutionModel,
                valid.DerivativeProvider,
                slice,
                valid.Nodes.ToArray(),
                new[] { valid.NodeCount },
                validMap);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyStageLIR(invalidRoot).Diagnostics,
                MaterialIRDiagnosticCodes.InvalidStageLIR);

            var invalidOperandCount = new MaterialStageLIR(
                valid.Stage,
                valid.ExecutionModel,
                valid.DerivativeProvider,
                slice,
                new[]
                {
                    new MaterialStageLIRNode(
                        MaterialStageLIROpcode.Parameter,
                        MaterialValueType.Float,
                        semantic: 0,
                        constant: default,
                        sourceNodeIndex: parameter.Index,
                        operandCount: 5,
                        operand0: -1,
                        operand1: -1,
                        operand2: -1,
                        operand3: -1),
                },
                new[] { 0 },
                malformedMap);
            AssertDiagnostic(
                MaterialIRVerifier.VerifyStageLIR(invalidOperandCount).Diagnostics,
                MaterialIRDiagnosticCodes.InvalidStageLIR,
                parameter.Index);
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
        public void CoverageLowering_AcceptsGeneralVerifiedCoverageValueIR()
        {
            MaterialIRModule module = BuildUnsupportedCoverageModule();

            CompiledCoverageProgram coverage = CoverageProgramLowerer.Compile(module);

            Assert.That(
                coverage.ProgramID,
                Is.EqualTo(VividMaterialCoverageProgramID.BaseColorAlpha));
            Assert.That(coverage.StageLIR.Stage, Is.EqualTo(MaterialEvaluationStage.Coverage));
            Assert.That(coverage.StageLIR.Roots, Has.Count.EqualTo(2));
            Assert.That(
                coverage.ValueSlice.Contains(module.Outputs.CoverageValue),
                Is.True);
            Assert.That(
                coverage.ValueSlice.Contains(module.Outputs.AlphaClipThreshold),
                Is.True);
        }

        [Test]
        public void SurfaceMatcherAndFrozenCatalog_SeparateEvaluatorFromRuntimeProgramID()
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
            Assert.That(
                standard.SurfaceProgram.ProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.StandardSingleSlab));
            Assert.That(
                horizontal.SurfaceProgram.ProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.DualSlab));
            Assert.That(
                vertical.SurfaceProgram.ProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.DualSlab));
            Assert.That(
                standard.Lowering.SelectionKey.Topology,
                Is.EqualTo(MaterialProgramTopologySpecialization.SingleSlab));
            Assert.That(
                horizontal.Lowering.SelectionKey.Topology,
                Is.EqualTo(MaterialProgramTopologySpecialization.HorizontalMix));
            Assert.That(
                vertical.Lowering.SelectionKey.Topology,
                Is.EqualTo(MaterialProgramTopologySpecialization.VerticalLayer));
            MaterialProgramCatalog catalog = MaterialProgramCatalog.Bake(
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
            Assert.That(
                catalog.GetEntry(VividMaterialProgramID.DualSlabHorizontalMix)
                    .ProgramID,
                Is.EqualTo(VividMaterialProgramID.DualSlabHorizontalMix));
            Assert.That(
                catalog.GetEntry(VividMaterialProgramID.DualSlabVerticalLayer)
                    .ProgramID,
                Is.EqualTo(VividMaterialProgramID.DualSlabVerticalLayer));
            Assert.That(
                standard.Module.ClosureGraph.GetNode(
                    standard.Module.SurfaceClosure).Opcode,
                Is.EqualTo(ClosureExpressionOpcode.Slab));
            Assert.That(
                horizontal.Module.ClosureGraph.GetNode(
                    horizontal.Module.SurfaceClosure).Opcode,
                Is.EqualTo(ClosureExpressionOpcode.HorizontalMix));
            Assert.That(
                vertical.Module.ClosureGraph.GetNode(
                    vertical.Module.SurfaceClosure).Opcode,
                Is.EqualTo(ClosureExpressionOpcode.VerticalLayer));
            CollectionAssert.AreEqual(
                GetValueSliceSignature(horizontal.SurfaceProgram.ValueSlice),
                GetValueSliceSignature(vertical.SurfaceProgram.ValueSlice));
        }

        [Test]
        public void SurfaceMatcher_AcceptsGeneralVerifiedSlabValueIR()
        {
            MaterialIRModule module = BuildUnsupportedSurfaceModule();

            CompiledSurfaceProgram surface = SurfaceProgramMatcher.Compile(module);

            Assert.That(
                surface.ProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.StandardSingleSlab));
            Assert.That(
                surface.StageLIR.Nodes.Any(node =>
                    node.Opcode == MaterialStageLIROpcode.Constant
                    && node.Type == MaterialValueType.Float),
                Is.True);
        }

        [Test]
        public void TransportLowering_UsesExplicitNoneProgram()
        {
            CompiledMaterialProgram program =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);

            Assert.That(program.TransportProgram, Is.SameAs(CompiledTransportProgram.None));
            Assert.That(
                program.TransportProgram.ProgramID,
                Is.EqualTo(VividMaterialTransportProgramID.None));
            Assert.That(program.TransportProgram.Requirements.ParameterDeclarations, Is.Empty);
            Assert.That(program.TransportProgram.Requirements.ResourceDeclarations, Is.Empty);
            Assert.That(
                program.Lowering.SelectionKey.TransportProgramID,
                Is.EqualTo(VividMaterialTransportProgramID.None));
            Assert.That(
                program.RuntimeData.TransportProgramID,
                Is.EqualTo(VividMaterialTransportProgramID.None));
        }

        [Test]
        public void ProgramCatalog_IsClosedAndPreservesExplicitIDHoles()
        {
            CompiledMaterialProgram builtin =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            var customProgramID = (VividMaterialProgramID) 4u;
            CompiledMaterialProgram compiled = CompiledMaterialProgram.Compile(
                builtin.Module,
                MaterialProgramContract.RuntimeAbiVersion,
                MaterialProgramBuiltinCatalog.Templates);
            MaterialProgramCatalog catalog = MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.Reserved("P0.Reserved"),
                MaterialProgramCatalogBakeSlot.Reserved("P1.Reserved"),
                MaterialProgramCatalogBakeSlot.Reserved("P2.Reserved"),
                MaterialProgramCatalogBakeSlot.Reserved("P3.Reserved"),
                MaterialProgramCatalogBakeSlot.ForProgram("P4.Custom", compiled));

            Assert.That(catalog.GetEntry(customProgramID).ProgramID, Is.EqualTo(customProgramID));
            Assert.That(compiled.CompiledHash, Is.EqualTo(builtin.CompiledHash));
            Assert.That(
                compiled.CoverageHlsl.PayloadEquals(builtin.CoverageHlsl),
                Is.True);
            Assert.That(
                compiled.CoverageHlsl.EntryPoint,
                Is.EqualTo(builtin.CoverageHlsl.EntryPoint));
            VividMaterialProgramData[] runtimeTable =
                catalog.CreateRuntimeProgramTable();
            Assert.That(runtimeTable, Has.Length.EqualTo(5));
            for (int holeIndex = 0; holeIndex < 4; holeIndex++)
                Assert.That(runtimeTable[holeIndex].Version, Is.Zero);
            AssertRuntimeProgramData(
                runtimeTable[4],
                new uint[] { 1u, 0u, 0u, 0u, 0u, 0u, 7u, 0u });
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                catalog.GetMaterialProgram(VividMaterialProgramID.StandardSingleSlab));
            Assert.Throws<InvalidOperationException>(() =>
                new MaterialProgramCatalog.ManifestEntry(
                    catalog,
                    customProgramID,
                    "P4.ForgedOutsideBake",
                    compiled));
        }

        [Test]
        public void ClosureTopology_RejectsUnknownClosureOperatorWithStableDiagnostic()
        {
            CompiledMaterialProgram prototype =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.HorizontalMix);
            MaterialIRModule prototypeModule = prototype.Module;
            ClosureTopology prototypeTopology = prototypeModule.Topology;
            MaterialIRVerificationException exception =
                Assert.Throws<MaterialIRVerificationException>(() => new ClosureTopology(
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
                    ClosureTopologyBudget.Prototype));
            AssertDiagnostic(
                exception.Diagnostics,
                MaterialIRDiagnosticCodes.InvalidTopologySemantic);
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
        public void GenericLayout_IsCanonicalAndDoesNotCrossFourWordLanes()
        {
            var boolParameter = new MaterialParameterDeclaration(
                "A_Bool",
                MaterialValueType.Bool);
            var float2Parameter = new MaterialParameterDeclaration(
                "B_Float2",
                MaterialValueType.Float2);
            var float3Parameter = new MaterialParameterDeclaration(
                "C_CustomFloat3",
                MaterialValueType.Float3);
            var floatParameter = new MaterialParameterDeclaration(
                "D_Float",
                MaterialValueType.Float);
            var float4Parameter = new MaterialParameterDeclaration(
                "E_Float4",
                MaterialValueType.Float4);
            var firstResource = new MaterialResourceDeclaration(
                "A_CustomTexture",
                MaterialValueType.Texture2D);
            var secondResource = new MaterialResourceDeclaration(
                "Z_CustomTexture",
                MaterialValueType.Texture2D);
            var first = new MaterialGenericLayout(
                new[]
                {
                    float4Parameter,
                    float3Parameter,
                    boolParameter,
                    floatParameter,
                    float2Parameter,
                },
                new[] { secondResource, firstResource });
            var reordered = new MaterialGenericLayout(
                new[]
                {
                    float2Parameter,
                    floatParameter,
                    boolParameter,
                    float3Parameter,
                    float4Parameter,
                },
                new[] { firstResource, secondResource });

            Assert.That(first.Version, Is.EqualTo(MaterialProgramContract.GenericLayoutVersion));
            Assert.That(first.PayloadEquals(reordered), Is.True);
            Assert.That(first.Fingerprint, Is.EqualTo(reordered.Fingerprint));
            Assert.That(first.ParameterStrideInWords, Is.EqualTo(12));
            AssertGenericParameterBinding(first, boolParameter, wordOffset: 0, wordCount: 1);
            AssertGenericParameterBinding(first, float2Parameter, wordOffset: 1, wordCount: 2);
            AssertGenericParameterBinding(first, float3Parameter, wordOffset: 4, wordCount: 3);
            AssertGenericParameterBinding(first, floatParameter, wordOffset: 7, wordCount: 1);
            AssertGenericParameterBinding(first, float4Parameter, wordOffset: 8, wordCount: 4);
            Assert.That(
                first.TryGetResourceBinding(
                    firstResource,
                    out MaterialGenericResourceBinding firstBinding),
                Is.True);
            Assert.That(firstBinding.Slot, Is.Zero);
            Assert.That(
                first.TryGetResourceBinding(
                    secondResource,
                    out MaterialGenericResourceBinding secondBinding),
                Is.True);
            Assert.That(secondBinding.Slot, Is.EqualTo(1));
            Assert.That(
                MaterialNativeTemplateDeclarationAdapter.TryGetParameter(
                    float3Parameter,
                    out _),
                Is.False);
            Assert.That(
                MaterialNativeTemplateDeclarationAdapter.TryGetTexture(
                    firstResource,
                    out _),
                Is.False);
        }

        [Test]
        public void ValueRequirements_PreserveCustomDeclarationsForGenericLowering()
        {
            var values = new MaterialValueIR();
            var declaration = new MaterialParameterDeclaration(
                "CustomFloat2",
                MaterialValueType.Float2);
            MaterialValue value = values.Parameter(declaration);
            values.Freeze();
            MaterialStageLIR stageLIR = MaterialStageLIRLowerer.Lower(
                new MaterialValueSlice(values, value),
                MaterialEvaluationStage.Surface);

            MaterialValueRequirements requirements =
                MaterialValueRequirements.Collect(stageLIR);
            var layout = new MaterialGenericLayout(requirements);

            Assert.That(requirements.IsNativeTemplateCompatible, Is.False);
            CollectionAssert.AreEqual(
                new[] { declaration },
                requirements.ParameterDeclarations);
            Assert.That(requirements.Parameters, Is.Empty);
            AssertGenericParameterBinding(
                layout,
                declaration,
                wordOffset: 0,
                wordCount: 2);
        }

        [Test]
        public void NativeLayoutSchema_RequiresExactLiveDeclarationsAndConvertsEmission()
        {
            CompiledMaterialProgram standard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            CompiledMaterialProgram dualSlab =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    VividDualSlabOperator.HorizontalMix);
            MaterialNativeTemplateLayoutSchema schema =
                MaterialLayoutLowerer.CreateLegacyLayoutSchema();

            Assert.That(schema.Matches(standard.Lowering.Requirements), Is.True);
            Assert.That(schema.Matches(dualSlab.Lowering.Requirements), Is.False);
            Assert.That(
                schema.TryGetParameterBinding(
                    MaterialNativeTemplateDeclarationAdapter.GetParameter(
                        MaterialParameter.Emission),
                    out MaterialNativeParameterBinding emissionBinding),
                Is.True);
            Assert.That(
                emissionBinding.Target,
                Is.EqualTo(MaterialRuntimeParameter.Emission));
            Assert.That(
                emissionBinding.Conversion,
                Is.EqualTo(MaterialParameterStorageConversion.Float3ToFloat4));

            Assert.Throws<NotSupportedException>(() =>
                MaterialLayoutLowerer.Compile(
                    dualSlab.Lowering.Requirements,
                    dualSlab.Lowering.GenericLayout,
                    schema));
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
                nodes: 9,
                textureSamples: 1,
                derivatives: 2,
                nativeDerivatives: 2,
                importedGradients: 0,
                arithmeticNodes: 2,
                parameters: 2,
                textureResources: 1,
                externalInputs: 1);
            AssertStageCost(
                standardCost.Surface,
                nodes: 12,
                textureSamples: 1,
                derivatives: 2,
                nativeDerivatives: 0,
                importedGradients: 2,
                arithmeticNodes: 1,
                parameters: 4,
                textureResources: 1,
                externalInputs: 3);
            AssertStageCost(
                standardCost.Combined,
                nodes: 14,
                textureSamples: 1,
                derivatives: 2,
                nativeDerivatives: 0,
                importedGradients: 0,
                arithmeticNodes: 2,
                parameters: 5,
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
                nodes: 9,
                textureSamples: 1,
                derivatives: 2,
                nativeDerivatives: 2,
                importedGradients: 0,
                arithmeticNodes: 2,
                parameters: 2,
                textureResources: 1,
                externalInputs: 1);
            AssertStageCost(
                dualCost.Surface,
                nodes: 19,
                textureSamples: 2,
                derivatives: 2,
                nativeDerivatives: 0,
                importedGradients: 2,
                arithmeticNodes: 2,
                parameters: 8,
                textureResources: 2,
                externalInputs: 3);
            AssertStageCost(
                dualCost.Combined,
                nodes: 21,
                textureSamples: 2,
                derivatives: 2,
                nativeDerivatives: 0,
                importedGradients: 0,
                arithmeticNodes: 3,
                parameters: 9,
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
            Assert.That(standardDump, Does.Contain("cost_model=lowered_program_worst_case_v3"));
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
                maxCombinedValueNodes: 13,
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
                Does.Contain("combined value nodes cost 14 exceeds budget 13"));
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
                Assert.That(first.ProgramID, Is.EqualTo(VividMaterialProgramID.StandardSingleSlab));
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
                    compiled.ProgramID,
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
                Assert.That(compiled.RuntimeHeader.ProgramID, Is.EqualTo(compiled.ProgramID));
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
            MaterialIRVerificationException exception =
                Assert.Throws<MaterialIRVerificationException>(() => new ClosureTopology(
                    prototype.Module.Values,
                    new[] { prototype.Module.Topology.NormalBases[0] },
                    new[] { prototype.Module.Topology.Slabs[0], prototype.Module.Topology.Slabs[1] },
                    new[] { prototype.Module.Topology.Operators[0] },
                    budget));
            AssertDiagnostic(
                exception.Diagnostics,
                MaterialIRDiagnosticCodes.TopologyBudgetExceeded);
        }

        private static void AssertCoverageRequirements(CompiledMaterialProgram program)
        {
            CompiledCoverageProgram coverage = program.CoverageProgram;
            Assert.That(
                coverage.ProgramID,
                Is.EqualTo(VividMaterialCoverageProgramID.BaseColorAlpha));
            Assert.That(program.RuntimeData.CoverageProgramID, Is.EqualTo(coverage.ProgramID));
            Assert.That(coverage.ValueSlice.NodeCount, Is.EqualTo(9));
            Assert.That(
                coverage.ValueSlice.Contains(program.Module.Outputs.CoverageValue),
                Is.True);
            Assert.That(
                coverage.ValueSlice.Contains(program.Module.Outputs.AlphaClipThreshold),
                Is.True);
            Assert.That(
                coverage.ValueSlice.Contains(program.Module.Outputs.Emission),
                Is.False);
            CollectionAssert.AreEqual(
                new[] { MaterialParameter.BaseColor, MaterialParameter.AlphaClipThreshold },
                coverage.RequiredParameters);
            CollectionAssert.DoesNotContain(
                coverage.Requirements.ParameterDeclarations,
                MaterialNativeTemplateDeclarationAdapter.GetParameter(
                    MaterialParameter.Emission));
            CollectionAssert.AreEqual(
                new[] { MaterialTextureResource.BaseColor },
                coverage.RequiredTextureResources);
            CollectionAssert.AreEqual(
                new[] { MaterialExternalInput.UV0 },
                coverage.RequiredExternalInputs);
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialStageInput.UV0,
                    MaterialStageInput.UV0Ddx,
                    MaterialStageInput.UV0Ddy,
                },
                coverage.RequiredStageInputs);
        }

        private static void AssertStandardSurfaceRequirements(
            CompiledMaterialProgram program)
        {
            CompiledSurfaceProgram surface = program.SurfaceProgram;
            Assert.That(
                surface.ProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.StandardSingleSlab));
            Assert.That(program.RuntimeData.SurfaceProgramID, Is.EqualTo(surface.ProgramID));
            Assert.That(surface.ValueSlice.NodeCount, Is.EqualTo(12));
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Topology.Slabs[0].Roughness),
                Is.True);
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Topology.NormalBases[0].Normal),
                Is.True);
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Outputs.AlphaClipThreshold),
                Is.False);
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Outputs.Emission),
                Is.True);
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialParameter.BaseColor,
                    MaterialParameter.Roughness,
                    MaterialParameter.Metallic,
                    MaterialParameter.Emission,
                },
                surface.RequiredParameters);
            CollectionAssert.Contains(
                surface.Requirements.ParameterDeclarations,
                MaterialNativeTemplateDeclarationAdapter.GetParameter(
                    MaterialParameter.Emission));
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
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialStageInput.UV0,
                    MaterialStageInput.UV0Ddx,
                    MaterialStageInput.UV0Ddy,
                    MaterialStageInput.GeometryNormalWS,
                    MaterialStageInput.GeometryTangentWS,
                },
                surface.RequiredStageInputs);
        }

        private static void AssertDualSlabSurfaceRequirements(
            CompiledMaterialProgram program)
        {
            CompiledSurfaceProgram surface = program.SurfaceProgram;
            Assert.That(
                surface.ProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.DualSlab));
            Assert.That(program.RuntimeData.SurfaceProgramID, Is.EqualTo(surface.ProgramID));
            Assert.That(surface.ValueSlice.NodeCount, Is.EqualTo(19));
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Topology.Slabs[1].BaseColor),
                Is.True);
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Topology.Operators[0].Weight),
                Is.True);
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Outputs.AlphaClipThreshold),
                Is.False);
            Assert.That(
                surface.ValueSlice.Contains(program.Module.Outputs.Emission),
                Is.True);
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
                    MaterialParameter.Emission,
                },
                surface.RequiredParameters);
            CollectionAssert.Contains(
                surface.Requirements.ParameterDeclarations,
                MaterialNativeTemplateDeclarationAdapter.GetParameter(
                    MaterialParameter.Emission));
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
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialStageInput.UV0,
                    MaterialStageInput.UV0Ddx,
                    MaterialStageInput.UV0Ddy,
                    MaterialStageInput.GeometryNormalWS,
                    MaterialStageInput.GeometryTangentWS,
                },
                surface.RequiredStageInputs);
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
                    MaterialParameter.Emission,
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
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialStageInput.UV0,
                    MaterialStageInput.UV0Ddx,
                    MaterialStageInput.UV0Ddy,
                    MaterialStageInput.GeometryNormalWS,
                    MaterialStageInput.GeometryTangentWS,
                },
                layout.Requirements.StageInputs);
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
                    MaterialParameter.Emission,
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
            CollectionAssert.AreEqual(
                new[]
                {
                    MaterialStageInput.UV0,
                    MaterialStageInput.UV0Ddx,
                    MaterialStageInput.UV0Ddy,
                    MaterialStageInput.GeometryNormalWS,
                    MaterialStageInput.GeometryTangentWS,
                },
                layout.Requirements.StageInputs);
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

        private static void AssertGenericParameterBinding(
            MaterialGenericLayout layout,
            in MaterialParameterDeclaration declaration,
            int wordOffset,
            int wordCount)
        {
            Assert.That(
                layout.TryGetParameterBinding(
                    declaration,
                    out MaterialGenericParameterBinding binding),
                Is.True);
            Assert.That(binding.WordOffset, Is.EqualTo(wordOffset));
            Assert.That(binding.WordCount, Is.EqualTo(wordCount));
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

        private static void AssertCandidateDiagnostic(
            MaterialValueIR values,
            in MaterialValueNode candidate,
            string expectedCode)
        {
            int expectedNodeIndex = values.NodeCount;
            MaterialIRVerificationResult result =
                MaterialIRVerifier.VerifyCandidateNode(values, candidate);

            Assert.That(result.IsValid, Is.False);
            AssertDiagnostic(result.Diagnostics, expectedCode, expectedNodeIndex);
        }

        private static void AssertDiagnostic(
            System.Collections.Generic.IReadOnlyList<MaterialIRDiagnostic> diagnostics,
            string expectedCode,
            int nodeIndex = -1)
        {
            MaterialIRDiagnostic diagnostic = diagnostics.First(entry =>
                string.Equals(entry.Code, expectedCode, StringComparison.Ordinal));
            Assert.That(diagnostic.Code, Is.EqualTo(expectedCode));
            Assert.That(diagnostic.NodeIndex, Is.EqualTo(nodeIndex));
        }

        private static void AssertLegalizedGradientInputs(MaterialStageLIR stageLIR)
        {
            Assert.That(
                stageLIR.Nodes.Count(node =>
                    node.Opcode == MaterialStageLIROpcode.StageInput
                    && node.Semantic == (int) MaterialStageInput.UV0Ddx),
                Is.EqualTo(1));
            Assert.That(
                stageLIR.Nodes.Count(node =>
                    node.Opcode == MaterialStageLIROpcode.StageInput
                    && node.Semantic == (int) MaterialStageInput.UV0Ddy),
                Is.EqualTo(1));
            foreach (MaterialStageLIRNode sample in stageLIR.Nodes.Where(node =>
                         node.Opcode == MaterialStageLIROpcode.TextureSampleGrad))
            {
                Assert.That(
                    stageLIR.Nodes[sample.Operand2].Semantic,
                    Is.EqualTo((int) MaterialStageInput.UV0Ddx));
                Assert.That(
                    stageLIR.Nodes[sample.Operand3].Semantic,
                    Is.EqualTo((int) MaterialStageInput.UV0Ddy));
            }
            for (int nodeIndex = 0; nodeIndex < stageLIR.Nodes.Count; nodeIndex++)
            {
                MaterialStageLIRNode node = stageLIR.Nodes[nodeIndex];
                for (int operandIndex = 0; operandIndex < node.OperandCount; operandIndex++)
                    Assert.That(node.GetOperand(operandIndex), Is.LessThan(nodeIndex));
            }
        }

        private static void AssertStageCost(
            in MaterialStageCost cost,
            int nodes,
            int textureSamples,
            int derivatives,
            int nativeDerivatives,
            int importedGradients,
            int arithmeticNodes,
            int parameters,
            int textureResources,
            int externalInputs)
        {
            Assert.That(cost.ValueNodeCount, Is.EqualTo(nodes));
            Assert.That(cost.TextureSampleCount, Is.EqualTo(textureSamples));
            Assert.That(cost.DerivativeCount, Is.EqualTo(derivatives));
            Assert.That(cost.NativeDerivativeCount, Is.EqualTo(nativeDerivatives));
            Assert.That(cost.ImportedGradientCount, Is.EqualTo(importedGradients));
            Assert.That(cost.SurvivingDerivativeOpCount, Is.Zero);
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
                GetRuntimeProgramDataWords(runtimeData));
        }

        private static void AssertDeferredExportContract(
            CompiledMaterialProgram program,
            MaterialDeferredExportSidecarAbi sidecarAbi,
            MaterialShadingModelMask shadingModels,
            MaterialDeferredExportLitClass litClass,
            uint expectedClosureCount,
            MaterialDeferredExportTopology topology,
            MaterialDeferredExportPayloadFlags payloadFlags,
            MaterialDeferredExportPolicyFlags policyFlags)
        {
            MaterialDeferredExportContract contract = program.DeferredExportContract;
            Assert.That(contract, Is.Not.Null);
            Assert.That(
                contract.Version,
                Is.EqualTo(MaterialProgramContract.DeferredExportContractVersion));
            Assert.That(
                contract.SurfaceSummaryAbi,
                Is.EqualTo(MaterialDeferredExportSurfaceSummaryAbi.SurfaceSummaryV1));
            Assert.That(contract.DualSlabSidecarAbi, Is.EqualTo(sidecarAbi));
            Assert.That(contract.ShadingModels, Is.EqualTo(shadingModels));
            Assert.That(contract.LitClass, Is.EqualTo(litClass));
            Assert.That(contract.ExpectedClosureCount, Is.EqualTo(expectedClosureCount));
            Assert.That(contract.Topology, Is.EqualTo(topology));
            Assert.That(contract.PayloadFlags, Is.EqualTo(payloadFlags));
            Assert.That(contract.PolicyFlags, Is.EqualTo(policyFlags));
            Assert.That(
                contract.Fingerprint.Version,
                Is.EqualTo(MaterialProgramContract.DeferredExportFingerprintVersion));
            Assert.That(contract.Fingerprint.Value, Is.Not.Zero);
        }

        private static uint[] GetRuntimeProgramDataWords(
            in VividMaterialProgramData runtimeData)
        {
            return new[]
            {
                runtimeData.Version,
                (uint) runtimeData.CoverageProgramID,
                (uint) runtimeData.SurfaceProgramID,
                (uint) runtimeData.TransportProgramID,
                (uint) runtimeData.ParameterLayoutID,
                (uint) runtimeData.ResourceLayoutID,
                (uint) runtimeData.CapabilityFlags,
                (uint) runtimeData.ExecutionClass,
            };
        }

        private static string[] GetValueSliceSignature(MaterialValueSlice valueSlice)
        {
            return valueSlice.NodeIndices.Select(index =>
            {
                MaterialValueNode node = valueSlice.Values.Nodes[index];
                string semantic;
                if (node.Opcode == MaterialValueOpcode.Parameter)
                {
                    MaterialParameterDeclaration declaration =
                        valueSlice.Values.ParameterDeclarations[node.Semantic];
                    semantic = $"{declaration.Symbol}:{declaration.Type}";
                }
                else if (node.Opcode == MaterialValueOpcode.TextureResource)
                {
                    MaterialResourceDeclaration declaration =
                        valueSlice.Values.ResourceDeclarations[node.Semantic];
                    semantic = $"{declaration.Symbol}:{declaration.Type}";
                }
                else
                {
                    semantic = node.Semantic.ToString();
                }
                return $"{node.Opcode}:{node.Type}:{semantic}";
            }).ToArray();
        }

        private static MaterialIRModule BuildBinaryCanonicalModule(
            MaterialValueOpcode opcode,
            bool reversePrimaryOperands,
            bool emitOppositeOrderForAlpha)
        {
            var valueIR = new MaterialValueIR();
            MaterialValue left = valueIR.Parameter(MaterialParameter.Roughness);
            MaterialValue right = valueIR.Parameter(MaterialParameter.Metallic);
            MaterialValue primary = EmitBinary(
                valueIR,
                opcode,
                reversePrimaryOperands ? right : left,
                reversePrimaryOperands ? left : right);
            MaterialValue alphaClipThreshold = emitOppositeOrderForAlpha
                ? EmitBinary(valueIR, opcode, right, left)
                : primary;
            MaterialValue baseColor = valueIR.Parameter(MaterialParameter.BaseColor);
            MaterialValue emission = valueIR.Parameter(MaterialParameter.Emission);
            MaterialValue normal =
                valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent =
                valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS);
            var topology = new ClosureTopology(
                valueIR,
                new[] { new ClosureNormalBasis(normal, tangent) },
                new[]
                {
                    new ClosureSlab(
                        baseColor,
                        left,
                        right,
                        normalBasisIndex: 0,
                        features: ClosureFeatureMask.None,
                        isTop: true,
                        isBottom: true),
                },
                Array.Empty<ClosureOperator>(),
                ClosureTopologyBudget.Prototype);
            return CreateModuleFromTopology(
                valueIR,
                new MaterialOutputRoots(primary, alphaClipThreshold, emission),
                topology,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit);
        }

        private static MaterialIRModule BuildAssociativeCanonicalModule(
            bool leftAssociated)
        {
            var valueIR = new MaterialValueIR();
            MaterialValue a = valueIR.Parameter(MaterialParameter.Roughness);
            MaterialValue b = valueIR.Parameter(MaterialParameter.Metallic);
            MaterialValue c = valueIR.Parameter(MaterialParameter.LayerWeight);
            MaterialValue sum = leftAssociated
                ? valueIR.Add(valueIR.Add(a, b), c)
                : valueIR.Add(a, valueIR.Add(b, c));
            MaterialValue baseColor = valueIR.Parameter(MaterialParameter.BaseColor);
            MaterialValue emission = valueIR.Parameter(MaterialParameter.Emission);
            MaterialValue normal =
                valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent =
                valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS);
            var topology = new ClosureTopology(
                valueIR,
                new[] { new ClosureNormalBasis(normal, tangent) },
                new[]
                {
                    new ClosureSlab(
                        baseColor,
                        a,
                        b,
                        normalBasisIndex: 0,
                        features: ClosureFeatureMask.None,
                        isTop: true,
                        isBottom: true),
                },
                Array.Empty<ClosureOperator>(),
                ClosureTopologyBudget.Prototype);
            return CreateModuleFromTopology(
                valueIR,
                new MaterialOutputRoots(sum, sum, emission),
                topology,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit);
        }

        private static MaterialIRModule BuildConstantCoverageModule(
            uint coverageBits)
        {
            var valueIR = new MaterialValueIR();
            MaterialValue coverage = valueIR.Constant(math.asfloat(coverageBits));
            MaterialValue alphaClipThreshold =
                valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
            MaterialValue emission = valueIR.Parameter(MaterialParameter.Emission);
            MaterialValue baseColor = valueIR.Parameter(MaterialParameter.BaseColor);
            MaterialValue roughness = valueIR.Parameter(MaterialParameter.Roughness);
            MaterialValue metallic = valueIR.Parameter(MaterialParameter.Metallic);
            MaterialValue normal =
                valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent =
                valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS);
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
                        features: ClosureFeatureMask.None,
                        isTop: true,
                        isBottom: true),
                },
                Array.Empty<ClosureOperator>(),
                ClosureTopologyBudget.Prototype);
            return CreateModuleFromTopology(
                valueIR,
                new MaterialOutputRoots(coverage, alphaClipThreshold, emission),
                topology,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit);
        }

        private static MaterialValue EmitBinary(
            MaterialValueIR valueIR,
            MaterialValueOpcode opcode,
            MaterialValue left,
            MaterialValue right)
        {
            switch (opcode)
            {
                case MaterialValueOpcode.Add:
                    return valueIR.Add(left, right);
                case MaterialValueOpcode.Subtract:
                    return valueIR.Subtract(left, right);
                default:
                    throw new ArgumentOutOfRangeException(nameof(opcode), opcode, null);
            }
        }

        private static MaterialIRModule BuildUnsupportedCoverageModule()
        {
            var valueIR = new MaterialValueIR();
            MaterialValue baseColor = valueIR.Parameter(MaterialParameter.BaseColor);
            MaterialValue roughness = valueIR.Parameter(MaterialParameter.Roughness);
            MaterialValue metallic = valueIR.Parameter(MaterialParameter.Metallic);
            MaterialValue alphaClipThreshold =
                valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
            MaterialValue emission = valueIR.Parameter(MaterialParameter.Emission);
            MaterialValue coverageValue = valueIR.Constant(1.0f);
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
            return CreateModuleFromTopology(
                valueIR,
                new MaterialOutputRoots(coverageValue, alphaClipThreshold, emission),
                topology,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit);
        }

        private static MaterialIRModule BuildCanonicalHashModule(
            bool useAlternateValueOrder,
            bool includeUnusedNormalBasis = false)
        {
            var valueIR = new MaterialValueIR();
            MaterialValue baseColor;
            MaterialValue roughness;
            MaterialValue metallic;
            MaterialValue alphaClipThreshold;
            MaterialValue emission;
            MaterialValue coverage;
            MaterialValue normal;
            MaterialValue tangent;

            if (useAlternateValueOrder)
            {
                roughness = valueIR.Parameter(MaterialParameter.Roughness);
                metallic = valueIR.Parameter(MaterialParameter.Metallic);
                alphaClipThreshold = valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
                emission = valueIR.Parameter(MaterialParameter.Emission);
                normal = valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS);
                tangent = valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS);
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
                normal = valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS);
                tangent = valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS);
                roughness = valueIR.Parameter(MaterialParameter.Roughness);
                metallic = valueIR.Parameter(MaterialParameter.Metallic);
                alphaClipThreshold = valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
                emission = valueIR.Parameter(MaterialParameter.Emission);
            }
            coverage = valueIR.Swizzle(baseColor, MaterialSwizzleMask.W);

            var normalBases = new System.Collections.Generic.List<ClosureNormalBasis>
            {
                new ClosureNormalBasis(normal, tangent),
            };
            if (includeUnusedNormalBasis)
            {
                normalBases.Add(new ClosureNormalBasis(
                    valueIR.Normalize(normal),
                    valueIR.Normalize(tangent)));
            }

            var topology = new ClosureTopology(
                valueIR,
                normalBases.ToArray(),
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
            return CreateModuleFromTopology(
                valueIR,
                new MaterialOutputRoots(coverage, alphaClipThreshold, emission),
                topology,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit
                | MaterialShadingModelMask.Unlit);
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
            MaterialValue emission = valueIR.Parameter(MaterialParameter.Emission);
            MaterialValue coverage = valueIR.Swizzle(baseColor, MaterialSwizzleMask.W);
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
            return CreateModuleFromTopology(
                valueIR,
                new MaterialOutputRoots(coverage, alphaClipThreshold, emission),
                topology,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit);
        }

        private static MaterialIRModule BuildCanonicalClosureGraphModule(
            bool reverseClosureAllocation,
            bool includeDeadClosure,
            bool swapRootOperands = false,
            ClosureExpressionOpcode rootOpcode =
                ClosureExpressionOpcode.HorizontalMix)
        {
            var values = new MaterialValueIR();
            MaterialValue baseColor;
            MaterialValue roughness;
            MaterialValue metallic;
            MaterialValue topBaseColor;
            MaterialValue topRoughness;
            MaterialValue topMetallic;
            if (reverseClosureAllocation)
            {
                topBaseColor = values.Parameter(MaterialParameter.TopBaseColor);
                topRoughness = values.Parameter(MaterialParameter.TopRoughness);
                topMetallic = values.Parameter(MaterialParameter.TopMetallic);
                baseColor = values.Parameter(MaterialParameter.BaseColor);
                roughness = values.Parameter(MaterialParameter.Roughness);
                metallic = values.Parameter(MaterialParameter.Metallic);
            }
            else
            {
                baseColor = values.Parameter(MaterialParameter.BaseColor);
                roughness = values.Parameter(MaterialParameter.Roughness);
                metallic = values.Parameter(MaterialParameter.Metallic);
                topBaseColor = values.Parameter(MaterialParameter.TopBaseColor);
                topRoughness = values.Parameter(MaterialParameter.TopRoughness);
                topMetallic = values.Parameter(MaterialParameter.TopMetallic);
            }

            MaterialValue normal =
                values.ExternalInput(MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent =
                values.ExternalInput(MaterialExternalInput.GeometryTangentWS);
            MaterialValue weight = values.Parameter(MaterialParameter.LayerWeight);
            MaterialValue coverage = values.Constant(1.0f);
            MaterialValue alphaClipThreshold =
                values.Parameter(MaterialParameter.AlphaClipThreshold);
            MaterialValue emission = values.Parameter(MaterialParameter.Emission);
            var graph = new ClosureExpressionGraph(values);

            if (includeDeadClosure)
            {
                graph.Slab(
                    values.Constant(new float4(0.125f)),
                    values.Constant(0.25f),
                    values.Constant(0.375f),
                    normal,
                    tangent,
                    ClosureFeatureMask.None);
            }

            MaterialClosure background;
            MaterialClosure foreground;
            if (reverseClosureAllocation)
            {
                foreground = graph.Slab(
                    topBaseColor,
                    topRoughness,
                    topMetallic,
                    normal,
                    tangent,
                    ClosureFeatureMask.None);
                background = graph.Slab(
                    baseColor,
                    roughness,
                    metallic,
                    normal,
                    tangent,
                    ClosureFeatureMask.None);
            }
            else
            {
                background = graph.Slab(
                    baseColor,
                    roughness,
                    metallic,
                    normal,
                    tangent,
                    ClosureFeatureMask.None);
                foreground = graph.Slab(
                    topBaseColor,
                    topRoughness,
                    topMetallic,
                    normal,
                    tangent,
                    ClosureFeatureMask.None);
            }

            MaterialClosure operand0 = swapRootOperands
                ? foreground
                : background;
            MaterialClosure operand1 = swapRootOperands
                ? background
                : foreground;
            MaterialClosure root;
            switch (rootOpcode)
            {
                case ClosureExpressionOpcode.HorizontalMix:
                    root = graph.HorizontalMix(operand0, operand1, weight);
                    break;
                case ClosureExpressionOpcode.VerticalLayer:
                    root = graph.VerticalLayer(operand0, operand1, weight);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(rootOpcode),
                        rootOpcode,
                        null);
            }

            return new MaterialIRModule(
                values,
                new MaterialOutputRoots(
                    coverage,
                    alphaClipThreshold,
                    emission),
                graph,
                root,
                ClosureTopologyBudget.Prototype,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit);
        }

        private static MaterialIRModule CreateModuleFromTopology(
            MaterialValueIR values,
            in MaterialOutputRoots outputs,
            ClosureTopology topology,
            MaterialFeatureMask materialFeatures,
            MaterialShadingModelMask shadingModels)
        {
            ClosureExpressionGraph closureGraph =
                ClosureExpressionGraph.FromTopology(
                    topology,
                    out MaterialClosure surfaceClosure);
            return new MaterialIRModule(
                values,
                outputs,
                closureGraph,
                surfaceClosure,
                topology.Budget,
                materialFeatures,
                shadingModels);
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
