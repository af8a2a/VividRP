using System;
using NUnit.Framework;
using Unity.Mathematics;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class OpenPBROpaqueSurfaceExportTests
    {
        [Test]
        public void NativeSurface_ExportsAllElevenDeclaredInputsWithoutLegacyMapping()
        {
            CompiledMaterialProgram program = BuildNative(namedParameters: true);
            Assert.That(program.RuntimeData.SurfaceProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.OpenPBROpaque));
            Assert.That(program.RuntimeData.CapabilityFlags
                    & VividMaterialProgramCapabilities.LegacyGBufferExport,
                Is.EqualTo(VividMaterialProgramCapabilities.None));
            Assert.That(program.Lowering.GenericLayout.ParameterBindings.Count,
                Is.EqualTo((int) OpenPBROpaqueContract.FieldCount));
            for (int fieldIndex = 0; fieldIndex < OpenPBROpaqueContract.FieldCount; fieldIndex++)
            {
                string field = ((OpenPBROpaqueFieldSemantic) fieldIndex).ToString();
                var declaration = new MaterialParameterDeclaration(
                    field, ClosureOpenPBROpaqueExpression.GetFieldType(fieldIndex));
                Assert.That(program.Lowering.GenericLayout.TryGetParameterBinding(
                        declaration, out MaterialGenericParameterBinding binding),
                    Is.True, field);
                string loader = declaration.Type == MaterialValueType.Float3
                    ? "VividLoadMaterialFloat3" : "VividLoadMaterialFloat";
                Assert.That(program.SurfaceHlsl.Source,
                    Does.Contain($"{loader}(parameterAddress, {binding.WordOffset}u)"), field);
                string hlslField = char.ToLowerInvariant(field[0]) + field.Substring(1);
                Assert.That(program.SurfaceHlsl.Source,
                    Does.Contain("output.OpenPBROpaque." + hlslField + " = "), field);
            }
            Assert.That(program.SurfaceHlsl.Source, Does.Not.Contain("output.BaseSlab."));
            Assert.That(program.SurfaceHlsl.Source, Does.Not.Contain("output.Emission ="));
            Assert.That(program.SurfaceHlsl.Source, Does.Not.Contain("VividEvaluateAOTSlabSurfaceDetail"));
            Assert.That(program.SurfaceHlsl.Source, Does.Not.Contain("saturate("));
        }

        [Test]
        public void NativeExport_ContainsProfileIdentityAndNoLegacyDeferredPayload()
        {
            MaterialDeferredExportContract contract = BuildNative(false).DeferredExportContract;
            Assert.That(contract.NativePayloadAbi,
                Is.EqualTo(MaterialDeferredExportNativePayloadAbi.OpenPBROpaqueV1));
            Assert.That(contract.NativeProfileVersion, Is.EqualTo(OpenPBROpaqueContract.Version));
            Assert.That(contract.NativeProfileFingerprint, Is.EqualTo(OpenPBROpaqueContract.Fingerprint));
            Assert.That(contract.SurfaceSummaryAbi, Is.EqualTo(MaterialDeferredExportSurfaceSummaryAbi.None));
            Assert.That(contract.DualSlabSidecarAbi, Is.EqualTo(MaterialDeferredExportSidecarAbi.None));
            Assert.That(contract.ShadingModels, Is.EqualTo(MaterialShadingModelMask.OpenPBROpaque));
            Assert.That(contract.LitClass, Is.EqualTo(MaterialDeferredExportLitClass.OpenPBROpaque));
            Assert.That(contract.ExpectedClosureCount, Is.EqualTo(1u));
            Assert.That(contract.Topology, Is.EqualTo(MaterialDeferredExportTopology.None));
            Assert.That(contract.PayloadFlags, Is.EqualTo(MaterialDeferredExportPayloadFlags.NativeOpenPBROpaque));
            Assert.That(contract.PolicyFlags, Is.EqualTo(MaterialDeferredExportPolicyFlags.None));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void NativeExport_RejectsLegacyPayloadAndUnknownNativeAbi(int invalidField)
        {
            Assert.Catch<ArgumentException>(() => new MaterialDeferredExportContract(
                invalidField == 0 ? MaterialDeferredExportSurfaceSummaryAbi.SurfaceSummaryV1
                    : MaterialDeferredExportSurfaceSummaryAbi.None,
                invalidField == 1 ? MaterialDeferredExportSidecarAbi.DualSlabV1
                    : MaterialDeferredExportSidecarAbi.None,
                invalidField == 2 ? MaterialShadingModelMask.StandardLit
                    : MaterialShadingModelMask.OpenPBROpaque,
                MaterialDeferredExportLitClass.OpenPBROpaque,
                invalidField == 3 ? 2u : 1u,
                MaterialDeferredExportTopology.None,
                invalidField == 4 ? MaterialDeferredExportPayloadFlags.SurfaceSummary
                    : MaterialDeferredExportPayloadFlags.NativeOpenPBROpaque,
                invalidField == 5 ? MaterialDeferredExportPolicyFlags.ReceiveDecals
                    : MaterialDeferredExportPolicyFlags.None,
                invalidField == 6 ? (MaterialDeferredExportNativePayloadAbi) 999u
                    : MaterialDeferredExportNativePayloadAbi.OpenPBROpaqueV1));
        }

        [Test]
        public void NativeCatalog_PreservesSlabAndUsesTypedDispatcherIdentity()
        {
            CompiledMaterialProgram legacy = MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                MaterialProgramContract.RuntimeAbiVersion);
            CompiledMaterialProgram native = BuildNative(namedParameters: true);
            MaterialProgramCatalog catalog = Bake(legacy, native);
            Assert.That(catalog.CreateRuntimeProgramTable()[0].SurfaceProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.StandardSingleSlab));
            Assert.That(catalog.CreateRuntimeProgramTable()[1].SurfaceProgramID,
                Is.EqualTo(VividMaterialSurfaceProgramID.OpenPBROpaque));
            Assert.That(catalog.TryGetCatalogedProgram(BuildNative(true), out _), Is.True);
            Assert.That(legacy.DeferredExportContract.NativePayloadAbi,
                Is.EqualTo(MaterialDeferredExportNativePayloadAbi.None));
            Assert.That(legacy.DeferredExportContract.NativeProfileVersion, Is.Zero);
            Assert.That(legacy.DeferredExportContract.NativeProfileFingerprint, Is.Zero);

            string source = MaterialSurfaceHlslSourceBuilder.BuildSource(catalog);
            Assert.That(source, Does.Contain("#include \"../VividOpenPBROpaqueContract.hlsl\""));
            Assert.That(source, Does.Not.Contain("/OpenPBR/OpenPBR.hlsl"));
            Assert.That(source, Does.Contain("VividOpenPBROpaqueInputs OpenPBROpaque;"));
            Assert.That(source, Does.Contain("deferredExportContract.NativePayloadAbi = 1u;"));
            Assert.That(source, Does.Contain($"deferredExportContract.NativeProfileVersion = {OpenPBROpaqueContract.Version}u;"));
            Assert.That(source, Does.Contain($"deferredExportContract.NativeProfileFingerprintLo = {(uint) OpenPBROpaqueContract.Fingerprint}u;"));
            Assert.That(source, Does.Contain($"deferredExportContract.NativeProfileFingerprintHi = {(uint) (OpenPBROpaqueContract.Fingerprint >> 32)}u;"));
            Assert.That(source, Does.Contain("VividValidateOpenPBROpaqueInputs(output.OpenPBROpaque, 0u) != 0u"));
            Assert.That(source, Is.EqualTo(MaterialSurfaceHlslSourceBuilder.BuildSource(
                Bake(MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    MaterialProgramContract.RuntimeAbiVersion), BuildNative(true)))));
        }

        [Test]
        public void ProductionCompiler_InitializesFourTemplatesWithoutChangingLegacySlots()
        {
            // Access the actual static production entry point. Do not use the
            // Resources-backed frozen asset table or a fixture-created catalog.
            MaterialProgramCatalog catalog = GPUDrivenMaterialCompiler.ProgramCatalog;
            Assert.That(MaterialProgramBuiltinCatalog.Templates.Count, Is.EqualTo(4));
            Assert.That(MaterialProgramContract.BuiltinNativeTemplateCount, Is.EqualTo(4));
            Assert.That(MaterialProgramContract.BuiltinProgramCount, Is.EqualTo(3));
            Assert.That(MaterialProgramContract.ProductionCatalogProgramCount, Is.EqualTo(4));
            Assert.That(catalog.Count, Is.EqualTo(4));
            Assert.That(catalog.RuntimeTableLength, Is.EqualTo(4));
            string[] names =
            {
                "P0.StandardSingleSlab", "P1.DualSlabHorizontalMix",
                "P2.DualSlabVerticalLayer", "P3.GenericSingleSlabProof",
            };
            VividMaterialSurfaceProgramID[] surfaces =
            {
                VividMaterialSurfaceProgramID.StandardSingleSlab,
                VividMaterialSurfaceProgramID.DualSlab,
                VividMaterialSurfaceProgramID.DualSlab,
                VividMaterialSurfaceProgramID.StandardSingleSlab,
            };
            VividMaterialProgramData[] table = catalog.CreateRuntimeProgramTable();
            Assert.That(table.Length, Is.EqualTo(4));
            for (int slot = 0; slot < table.Length; slot++)
            {
                Assert.That(catalog.Slots[slot].StableName, Is.EqualTo(names[slot]));
                Assert.That((uint) catalog.Slots[slot].ProgramID, Is.EqualTo((uint) slot));
                Assert.That(table[slot].SurfaceProgramID, Is.EqualTo(surfaces[slot]));
                Assert.That(catalog.Slots[slot].Program.DeferredExportContract.NativePayloadAbi,
                    Is.EqualTo(MaterialDeferredExportNativePayloadAbi.None));
            }
        }

        [Test]
        public void NativeEmissionChange_ChangesCompiledAndCatalogIdentity()
        {
            CompiledMaterialProgram first = BuildNative(false, 0.0f);
            CompiledMaterialProgram second = BuildNative(false, 2.0f);
            Assert.That(first.CompiledHash, Is.Not.EqualTo(second.CompiledHash));
            Assert.That(first.SurfaceHlsl.PayloadHash, Is.Not.EqualTo(second.SurfaceHlsl.PayloadHash));
            Assert.That(Bake(first).ManifestHash, Is.Not.EqualTo(Bake(second).ManifestHash));
            Assert.That(Bake(first).TryGetCatalogedProgram(second, out _), Is.False);
        }

        private static MaterialProgramCatalog Bake(params CompiledMaterialProgram[] programs)
        {
            var slots = new MaterialProgramCatalogBakeSlot[programs.Length];
            for (int index = 0; index < programs.Length; index++)
                slots[index] = MaterialProgramCatalogBakeSlot.ForProgram("Test.Program." + index, programs[index]);
            return MaterialProgramCatalog.Bake(MaterialProgramBuiltinCatalog.Templates, slots);
        }

        private static CompiledMaterialProgram BuildNative(bool namedParameters, float emissionLuminance = 0.0f)
        {
            var values = new MaterialValueIR();
            OpenPBROpaqueInputs defaults = OpenPBROpaqueContract.CreateDefault();
            var inputs = new MaterialValue[(int) OpenPBROpaqueContract.FieldCount];
            for (int index = 0; index < inputs.Length; index++)
            {
                var semantic = (OpenPBROpaqueFieldSemantic) index;
                if (namedParameters)
                {
                    inputs[index] = values.Parameter(new MaterialParameterDeclaration(
                        semantic.ToString(), ClosureOpenPBROpaqueExpression.GetFieldType(index)));
                    continue;
                }
                switch (semantic)
                {
                    case OpenPBROpaqueFieldSemantic.BaseWeight: inputs[index] = values.Constant(defaults.BaseWeight); break;
                    case OpenPBROpaqueFieldSemantic.BaseColor: inputs[index] = values.Constant(defaults.BaseColor); break;
                    case OpenPBROpaqueFieldSemantic.BaseDiffuseRoughness: inputs[index] = values.Constant(defaults.BaseDiffuseRoughness); break;
                    case OpenPBROpaqueFieldSemantic.BaseMetalness: inputs[index] = values.Constant(defaults.BaseMetalness); break;
                    case OpenPBROpaqueFieldSemantic.SpecularWeight: inputs[index] = values.Constant(defaults.SpecularWeight); break;
                    case OpenPBROpaqueFieldSemantic.SpecularColor: inputs[index] = values.Constant(defaults.SpecularColor); break;
                    case OpenPBROpaqueFieldSemantic.SpecularRoughness: inputs[index] = values.Constant(defaults.SpecularRoughness); break;
                    case OpenPBROpaqueFieldSemantic.SpecularIor: inputs[index] = values.Constant(defaults.SpecularIor); break;
                    case OpenPBROpaqueFieldSemantic.NormalWS: inputs[index] = values.Constant(defaults.NormalWS); break;
                    case OpenPBROpaqueFieldSemantic.EmissionLuminance: inputs[index] = values.Constant(emissionLuminance); break;
                    case OpenPBROpaqueFieldSemantic.EmissionColor: inputs[index] = values.Constant(defaults.EmissionColor); break;
                }
            }
            var closures = new ClosureExpressionGraph(values);
            MaterialClosure surface = closures.OpenPBROpaque(new ClosureOpenPBROpaqueExpression(
                inputs[0], inputs[1], inputs[2], inputs[3], inputs[4], inputs[5],
                inputs[6], inputs[7], inputs[8], inputs[9], inputs[10]));
            var module = new MaterialIRModule(values,
                new MaterialOutputRoots(values.Constant(1.0f), values.Constant(0.0f), values.Constant(new float3(0.0f))),
                closures, surface, ClosureTopologyBudget.Prototype,
                MaterialFeatureMask.None, MaterialShadingModelMask.OpenPBROpaque);
            return CompiledMaterialProgram.Compile(module, MaterialProgramContract.RuntimeAbiVersion);
        }
    }
}
