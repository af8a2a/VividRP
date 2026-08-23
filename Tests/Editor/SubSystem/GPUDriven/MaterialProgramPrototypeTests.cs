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
                module.Topology,
                MaterialFeatureMask.None,
                module.ShadingModels);
            Assert.That(noMaterialFeatures.StructuralHash, Is.Not.EqualTo(module.StructuralHash));
            MaterialIRVerificationException unknownFeatureException =
                Assert.Throws<MaterialIRVerificationException>(() => new MaterialIRModule(
                    module.Values,
                    module.Outputs,
                    module.Topology,
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
                    module.Topology,
                    module.MaterialFeatures,
                    module.ShadingModels));
            AssertDiagnostic(
                outputException.Diagnostics,
                MaterialIRDiagnosticCodes.OutputNotOwned);
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
            Assert.That(MaterialProgramContract.IRSchemaVersion, Is.EqualTo(2u));
            Assert.That(MaterialProgramContract.SemanticHashVersion, Is.EqualTo(2u));
            Assert.That(MaterialProgramContract.CompiledHashVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.CompilerVersion, Is.EqualTo(2u));
            Assert.That(MaterialProgramContract.NativeTemplateBackendVersion, Is.EqualTo(2u));
            Assert.That(MaterialProgramContract.VerifierVersion, Is.EqualTo(1u));
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
                0x64F1CA45107C27F8ul,
                0x19543940D7603740ul,
                0x055478DD3B3B45ABul,
            };
            var expectedCompiledHashes = new[]
            {
                0x04A59854D0819128ul,
                0x43C3B1B4311A2A48ul,
                0x44A606DB9A862400ul,
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
            var unlitOnly = new MaterialIRModule(
                prototypeModule.Values,
                prototypeModule.Outputs,
                prototypeModule.Topology,
                prototypeModule.MaterialFeatures,
                MaterialShadingModelMask.Unlit);
            CompiledMaterialProgram compiledUnlitOnly =
                CompiledMaterialProgram.Compile(
                    unlitOnly,
                    MaterialProgramContract.RuntimeAbiVersion);

            Assert.That(compiledUnlitOnly.ProgramID, Is.EqualTo(prototype.ProgramID));
            AssertRuntimeProgramData(
                compiledUnlitOnly.RuntimeData,
                new uint[] { 1u, 0u, 0u, 0u, 0u, 0u, 7u, 0u });
            Assert.That(compiledUnlitOnly.SemanticHash, Is.Not.EqualTo(prototype.SemanticHash));
            Assert.That(compiledUnlitOnly.CompiledHash, Is.Not.EqualTo(prototype.CompiledHash));
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
                nodes: 9,
                textureSamples: 1,
                derivatives: 2,
                arithmeticNodes: 2,
                parameters: 2,
                textureResources: 1,
                externalInputs: 1);
            AssertStageCost(
                standardCost.Surface,
                nodes: 12,
                textureSamples: 1,
                derivatives: 2,
                arithmeticNodes: 1,
                parameters: 4,
                textureResources: 1,
                externalInputs: 3);
            AssertStageCost(
                standardCost.Combined,
                nodes: 14,
                textureSamples: 1,
                derivatives: 2,
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
                arithmeticNodes: 2,
                parameters: 2,
                textureResources: 1,
                externalInputs: 1);
            AssertStageCost(
                dualCost.Surface,
                nodes: 19,
                textureSamples: 2,
                derivatives: 2,
                arithmeticNodes: 2,
                parameters: 8,
                textureResources: 2,
                externalInputs: 3);
            AssertStageCost(
                dualCost.Combined,
                nodes: 21,
                textureSamples: 2,
                derivatives: 2,
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
            return new MaterialIRModule(
                valueIR,
                new MaterialOutputRoots(coverageValue, alphaClipThreshold, emission),
                topology,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit);
        }

        private static MaterialIRModule BuildCanonicalHashModule(bool useAlternateValueOrder)
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
            return new MaterialIRModule(
                valueIR,
                new MaterialOutputRoots(coverage, alphaClipThreshold, emission),
                topology,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit);
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
