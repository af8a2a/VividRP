using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class GPUDrivenMaterialCompilerTests
    {
        [Test]
        public void MaterialProgramHlsl_DeclaresGeneratedAbiAndRuntimeBuffers()
        {
            string generatedContract = File.ReadAllText(
                "Packages/com.vivid.render-pipelines/Runtime/SubSystem/GPUDriven/VividGPUDrivenStructs.cs.hlsl");
            string runtimeContract = File.ReadAllText(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl");

            Assert.That(generatedContract, Does.Contain("struct VividMaterialRuntimeHeader"));
            Assert.That(generatedContract, Does.Contain("struct VividMaterialProgramData"));
            Assert.That(generatedContract, Does.Contain("struct VividDualSlabMaterialData"));
            Assert.That(
                generatedContract,
                Does.Contain("#define VIVIDMATERIALPROGRAMID_STANDARD_SINGLE_SLAB (0)"));
            Assert.That(
                generatedContract,
                Does.Contain("#define VIVIDMATERIALPROGRAMID_DUAL_SLAB_HORIZONTAL_MIX (1)"));
            Assert.That(
                generatedContract,
                Does.Contain("#define VIVIDMATERIALPROGRAMID_DUAL_SLAB_VERTICAL_LAYER (2)"));
            Assert.That(
                generatedContract,
                Does.Contain("#define VIVIDDUALSLABOPERATOR_VERTICAL_LAYER (1)"));
            Assert.That(
                generatedContract,
                Does.Contain("#define VIVIDMATERIALPROGRAMCAPABILITIES_UNLIT (4)"));
            Assert.That(
                generatedContract,
                Does.Contain("#define VIVIDMATERIALRUNTIMEFLAGS_UNLIT (2)"));
            Assert.That(runtimeContract, Does.Contain("struct VividMaterialRuntimeHeader"));
            Assert.That(runtimeContract, Does.Contain("struct VividMaterialProgramData"));
            Assert.That(
                runtimeContract,
                Does.Contain(
                    $"#define VIVID_MATERIAL_PROGRAM_VERSION {GPUDrivenMaterialCompiler.ProgramVersion}u"));
            Assert.That(
                runtimeContract,
                Does.Contain(
                    $"#define VIVIDMATERIALPROGRAMID_DUAL_SLAB_HORIZONTAL_MIX {(uint)VividMaterialProgramID.DualSlabHorizontalMix}u"));
            Assert.That(
                runtimeContract,
                Does.Contain(
                    $"#define VIVIDMATERIALPROGRAMID_DUAL_SLAB_VERTICAL_LAYER {(uint)VividMaterialProgramID.DualSlabVerticalLayer}u"));
            Assert.That(
                runtimeContract,
                Does.Contain(
                    $"#define VIVIDMATERIALRUNTIMEFLAGS_ALPHA_CLIP {(uint)VividMaterialRuntimeFlags.AlphaClip}u"));
            Assert.That(
                runtimeContract,
                Does.Contain(
                    $"#define VIVIDMATERIALPROGRAMCAPABILITIES_ALPHA_CLIP {(uint)VividMaterialProgramCapabilities.AlphaClip}u"));
            Assert.That(
                runtimeContract,
                Does.Contain(
                    $"#define VIVIDMATERIALPROGRAMCAPABILITIES_UNLIT {(uint)VividMaterialProgramCapabilities.Unlit}u"));
            Assert.That(
                runtimeContract,
                Does.Contain(
                    $"#define VIVIDMATERIALRUNTIMEFLAGS_UNLIT {(uint)VividMaterialRuntimeFlags.Unlit}u"));
            Assert.That(
                runtimeContract,
                Does.Contain("StructuredBuffer<VividMaterialRuntimeHeader> _MaterialRuntimeHeaders;"));
            Assert.That(
                runtimeContract,
                Does.Contain("StructuredBuffer<VividMaterialProgramData> _MaterialPrograms;"));
            Assert.That(
                runtimeContract,
                Does.Contain("StructuredBuffer<VividDualSlabMaterialData> _DualSlabMaterialData;"));

            string[] sharedStructs =
            {
                "VividMaterialRuntimeHeader",
                "VividMaterialProgramData",
                "VividMaterialData",
                "VividDualSlabMaterialData",
                "VividSurfaceBindingData",
            };
            foreach (string structName in sharedStructs)
            {
                Assert.That(
                    GetHlslStructSignature(runtimeContract, structName),
                    Is.EqualTo(GetHlslStructSignature(generatedContract, structName)),
                    $"C#-generated and runtime HLSL contracts differ for {structName}.");
            }
        }

        [Test]
        public void ProductionCatalog_CoexistsSameTopologyWithDistinctCompiledPayload()
        {
            MaterialProgramCatalog catalog =
                GPUDrivenMaterialCompiler.ProgramCatalog;
            MaterialProgramCatalog.ManifestEntry standard =
                catalog.GetEntry(VividMaterialProgramID.StandardSingleSlab);
            MaterialProgramCatalog.ManifestEntry generic = catalog.GetEntry(
                (VividMaterialProgramID)
                    MaterialProgramContract.BuiltinProgramCount);

            Assert.That(
                catalog.RuntimeTableLength,
                Is.EqualTo(
                    MaterialProgramContract.ProductionCatalogProgramCount));
            Assert.That(generic.StableName, Is.EqualTo("P3.GenericSingleSlabProof"));
            Assert.That((uint) generic.ProgramID, Is.EqualTo(3u));
            Assert.That(
                generic.Program.Lowering.SelectionKey,
                Is.EqualTo(standard.Program.Lowering.SelectionKey));
            Assert.That(
                generic.LayoutFingerprint,
                Is.EqualTo(standard.LayoutFingerprint));
            Assert.That(
                generic.Program.CompiledHash,
                Is.Not.EqualTo(standard.Program.CompiledHash));
            Assert.That(
                generic.Program.Module.SemanticHash,
                Is.Not.EqualTo(standard.Program.Module.SemanticHash));
            Assert.That(
                generic.Program.CoverageHlsl.EntryPoint,
                Is.Not.EqualTo(standard.Program.CoverageHlsl.EntryPoint));
            Assert.That(
                generic.Program.SurfaceHlsl.EntryPoint,
                Is.Not.EqualTo(standard.Program.SurfaceHlsl.EntryPoint));
        }

        [Test]
        public void CompileStandardSingleSlab_ProducesProgram0AndLegacyCompatibleData()
        {
            var proxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            try
            {
                proxy.BaseColor = new Color(0.8f, 0.6f, 0.4f, 0.75f);
                proxy.TextureTilingOffset = new Vector4(2.0f, 3.0f, 0.25f, 0.5f);
                proxy.EmissionColor = new Color(0.1f, 0.2f, 0.3f, 0.5f);
                proxy.BumpScale = 0.4f;
                proxy.Metallic = 0.75f;
                proxy.Roughness = 0.35f;
                proxy.MetallicRemap = new Vector2(0.1f, 0.8f);
                proxy.SmoothnessRemap = new Vector2(0.2f, 0.9f);
                proxy.AmbientOcclusionRemap = new Vector2(0.3f, 0.7f);
                proxy.MaskMode = GPUDrivenMaterialMaskMode.RoughnessMetallicOcclusion;
                proxy.AlphaClip = true;
                proxy.Cutoff = 0.42f;
                proxy.CullMode = CullMode.Off;
                proxy.DisableLighting = true;

                GPUDrivenCompiledMaterialInstance compiled =
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(
                        proxy,
                        parameterAddress: 7u,
                        surfaceBindingIndex: 11u);

                Assert.That(
                    compiled.RuntimeHeader.ProgramID,
                    Is.EqualTo(VividMaterialProgramID.StandardSingleSlab));
                Assert.That(compiled.RuntimeHeader.ParameterAddress, Is.EqualTo(7u));
                Assert.That(compiled.RuntimeHeader.ResourceBindingAddress, Is.EqualTo(11u));
                Assert.That(
                    compiled.RuntimeHeader.Flags,
                    Is.EqualTo(
                        VividMaterialRuntimeFlags.AlphaClip
                        | VividMaterialRuntimeFlags.Unlit));

                VividMaterialData materialData = compiled.LegacyMaterialData;
                float4 expectedBaseColor =
                    GPUDrivenMaterialCompiler.ConvertMaterialColorForGPU(proxy.BaseColor);
                float4 expectedEmission =
                    GPUDrivenMaterialCompiler.ConvertMaterialColorForGPU(proxy.EmissionColor);
                Assert.That(materialData.AlbedoColor, Is.EqualTo(expectedBaseColor));
                Assert.That(materialData.TextureTilingOffset, Is.EqualTo(new float4(2.0f, 3.0f, 0.25f, 0.5f)));
                Assert.That(materialData.Emission, Is.EqualTo(expectedEmission));
                Assert.That(materialData.MetallicSmoothnessRemap, Is.EqualTo(new float4(0.1f, 0.8f, 0.2f, 0.9f)));
                Assert.That(materialData.AmbientOcclusionRemap, Is.EqualTo(new float4(0.3f, 0.7f, 0.0f, 0.0f)));
                Assert.That(materialData.SurfaceBindingIndex, Is.EqualTo(11u));
                Assert.That(materialData.NormalsStrength, Is.EqualTo(0.4f));
                Assert.That(materialData.Roughness, Is.EqualTo(0.35f));
                Assert.That(materialData.Metallic, Is.EqualTo(0.75f));
                Assert.That(materialData.MaterialFlags, Is.EqualTo(VividMaterialFlags.Unlit));
                Assert.That(
                    materialData.RendererListID,
                    Is.EqualTo(VividRendererListID.CullOff | VividRendererListID.AlphaTest));
                Assert.That(materialData.AlphaClipThreshold, Is.EqualTo(0.42f));
                Assert.That(materialData.Padding0, Is.EqualTo((uint)proxy.MaskMode));
                Assert.That(materialData.Padding1, Is.Zero);
                Assert.That(compiled.ParameterLanes.Count, Is.EqualTo(4));
                Assert.That(
                    compiled.ParameterLanes[1],
                    Is.EqualTo(math.asuint(expectedBaseColor)));
                Assert.That(
                    math.asfloat(compiled.ParameterLanes[2].w),
                    Is.EqualTo(proxy.Metallic));
                Assert.That(
                    math.asfloat(compiled.ParameterLanes[3].x),
                    Is.EqualTo(proxy.Roughness));
            }
            finally
            {
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void CompileStandardSingleSlab_UsesCatalogedMaterialGraphProgram()
        {
            var proxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var graph = ScriptableObject.CreateInstance<MaterialGraphImportAsset>();
            try
            {
                VividMaterialProgramID genericProgramID =
                    (VividMaterialProgramID)
                        MaterialProgramContract.BuiltinProgramCount;
                CompiledMaterialProgram genericProgram =
                    GPUDrivenMaterialCompiler.GetMaterialProgram(genericProgramID);
                graph.Apply(
                    CreateCompilationResult(genericProgram),
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    GPUDrivenMaterialCompiler.ProgramCatalog);
                proxy.MaterialGraph = graph;

                Assert.That(
                    GPUDrivenMaterialCompiler.TryValidateMaterialProxy(
                        proxy,
                        out string validationMessage),
                    Is.True,
                    validationMessage);
                GPUDrivenCompiledMaterialInstance compiled =
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(
                        proxy,
                        parameterAddress: 5u,
                        surfaceBindingIndex: 7u);

                Assert.That(compiled.ProgramID, Is.EqualTo(genericProgramID));
                Assert.That(
                    compiled.CatalogProgram,
                    Is.SameAs(GPUDrivenMaterialCompiler.ProgramCatalog.GetEntry(
                        genericProgramID)));
                Assert.That(
                    compiled.MaterialProgram.CompiledHash,
                    Is.EqualTo(genericProgram.CompiledHash));
                Assert.That(compiled.RuntimeHeader.ParameterAddress, Is.EqualTo(5u));
                Assert.That(
                    compiled.RuntimeHeader.ResourceBindingAddress,
                    Is.EqualTo(7u));
                Assert.That(compiled.ParameterLanes.Count, Is.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(graph);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void CompileStandardSingleSlab_PacksNamedParameterOverrideByDeclaration()
        {
            var proxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var graphAsset = ScriptableObject.CreateInstance<MaterialGraphImportAsset>();
            var catalogAsset = ScriptableObject.CreateInstance<MaterialProgramCatalogAsset>();
            try
            {
                MaterialGraphCompilationResult compilation =
                    CompileNamedParameterProgram();
                MaterialProgramCatalog catalog = CreateExtendedCatalog(
                    "P4.NamedParameter",
                    compilation.Program);
                catalogAsset.Apply(catalog);
                graphAsset.Apply(
                    compilation,
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    catalog);
                proxy.MaterialGraph = graphAsset;
                var value = new Vector4(0.125f, 0.375f, 0.625f, 0.875f);
                proxy.SetParameterOverride(
                    "UserTint",
                    GPUDrivenMaterialParameterType.Float4,
                    value);

                Assert.That(
                    GPUDrivenMaterialCompiler.TryValidateMaterialProxy(
                        proxy,
                        catalogAsset,
                        out string validationMessage),
                    Is.True,
                    validationMessage);
                GPUDrivenCompiledMaterialInstance compiled =
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(
                        proxy,
                        parameterAddress: 3u,
                        resourceBindingAddress: 7u,
                        legacySurfaceBindingIndex: 11u,
                        frozenCatalog: catalogAsset);
                var declaration = new MaterialParameterDeclaration(
                    "UserTint",
                    MaterialValueType.Float4);
                Assert.That(
                    MaterialNativeTemplateDeclarationAdapter.TryGetParameter(
                        declaration,
                        out _),
                    Is.False);
                Assert.That(
                    compilation.Program.Lowering.GenericLayout
                        .TryGetParameterBinding(
                            declaration,
                            out MaterialGenericParameterBinding binding),
                    Is.True);

                AssertParameterValue(compiled, binding, value);
                Assert.That(
                    (uint) compiled.ProgramID,
                    Is.EqualTo((uint) MaterialProgramContract.ProductionCatalogProgramCount));
            }
            finally
            {
                Object.DestroyImmediate(catalogAsset);
                Object.DestroyImmediate(graphAsset);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void CompileStandardSingleSlab_RejectsNamedParameterOverrideTypeMismatch()
        {
            var proxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var graphAsset = ScriptableObject.CreateInstance<MaterialGraphImportAsset>();
            var catalogAsset = ScriptableObject.CreateInstance<MaterialProgramCatalogAsset>();
            try
            {
                MaterialGraphCompilationResult compilation =
                    CompileNamedParameterProgram();
                MaterialProgramCatalog catalog = CreateExtendedCatalog(
                    "P4.NamedParameter",
                    compilation.Program);
                catalogAsset.Apply(catalog);
                graphAsset.Apply(
                    compilation,
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    catalog);
                proxy.MaterialGraph = graphAsset;
                proxy.SetParameterOverride(
                    "UserTint",
                    GPUDrivenMaterialParameterType.Float3,
                    Vector4.one);

                Assert.That(
                    GPUDrivenMaterialCompiler.TryValidateMaterialProxy(
                        proxy,
                        catalogAsset,
                        out string validationMessage),
                    Is.False);
                Assert.That(validationMessage, Does.Contain("UserTint"));
                Assert.That(validationMessage, Does.Contain("Float3"));
                Assert.That(validationMessage, Does.Contain("Float4"));
                System.InvalidOperationException exception = Assert.Throws<
                    System.InvalidOperationException>(() =>
                        GPUDrivenMaterialCompiler.CompileStandardSingleSlab(
                            proxy,
                            parameterAddress: 0u,
                            resourceBindingAddress: 0u,
                            legacySurfaceBindingIndex: 0u,
                            frozenCatalog: catalogAsset));
                Assert.That(exception.Message, Is.EqualTo(validationMessage));
            }
            finally
            {
                Object.DestroyImmediate(catalogAsset);
                Object.DestroyImmediate(graphAsset);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void CompileStandardSingleSlab_RejectsMissingNamedParameterOverride()
        {
            var proxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var graphAsset = ScriptableObject.CreateInstance<MaterialGraphImportAsset>();
            var catalogAsset = ScriptableObject.CreateInstance<MaterialProgramCatalogAsset>();
            try
            {
                MaterialGraphCompilationResult compilation =
                    CompileNamedParameterProgram();
                MaterialProgramCatalog catalog = CreateExtendedCatalog(
                    "P4.NamedParameter",
                    compilation.Program);
                catalogAsset.Apply(catalog);
                graphAsset.Apply(
                    compilation,
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    catalog);
                proxy.MaterialGraph = graphAsset;

                Assert.That(
                    GPUDrivenMaterialCompiler.TryValidateMaterialProxy(
                        proxy,
                        catalogAsset,
                        out string validationMessage),
                    Is.False);
                Assert.That(validationMessage, Does.Contain("UserTint"));
                Assert.That(validationMessage, Does.Contain("no value"));
                System.InvalidOperationException exception = Assert.Throws<
                    System.InvalidOperationException>(() =>
                        GPUDrivenMaterialCompiler.CompileStandardSingleSlab(
                            proxy,
                            parameterAddress: 0u,
                            resourceBindingAddress: 0u,
                            legacySurfaceBindingIndex: 0u,
                            frozenCatalog: catalogAsset));
                Assert.That(exception.Message, Is.EqualTo(validationMessage));
            }
            finally
            {
                Object.DestroyImmediate(catalogAsset);
                Object.DestroyImmediate(graphAsset);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void TryValidateMaterialProxy_RejectsStaleMaterialGraphCatalogBinding()
        {
            var proxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var graph = ScriptableObject.CreateInstance<MaterialGraphImportAsset>();
            try
            {
                MaterialProgramCatalog production =
                    GPUDrivenMaterialCompiler.ProgramCatalog;
                MaterialProgramCatalog staleCatalog = MaterialProgramCatalog.Bake(
                    production.Templates,
                    MaterialProgramCatalogBakeSlot.ForProgram(
                        "P0.StandardSingleSlab",
                        production.GetMaterialProgram(
                            VividMaterialProgramID.StandardSingleSlab)),
                    MaterialProgramCatalogBakeSlot.ForProgram(
                        "P1.DualSlabHorizontalMix",
                        production.GetMaterialProgram(
                            VividMaterialProgramID.DualSlabHorizontalMix)),
                    MaterialProgramCatalogBakeSlot.ForProgram(
                        "P2.DualSlabVerticalLayer",
                        production.GetMaterialProgram(
                            VividMaterialProgramID.DualSlabVerticalLayer)),
                    MaterialProgramCatalogBakeSlot.ForProgram(
                        "P3.GenericSingleSlabProof",
                        production.GetMaterialProgram(
                            (VividMaterialProgramID)
                                MaterialProgramContract.BuiltinProgramCount)),
                    MaterialProgramCatalogBakeSlot.Reserved("P4.Reserved"));
                graph.Apply(
                    CreateCompilationResult(production.GetMaterialProgram(
                        VividMaterialProgramID.StandardSingleSlab)),
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    staleCatalog);
                proxy.MaterialGraph = graph;

                bool valid = GPUDrivenMaterialCompiler.TryValidateMaterialProxy(
                    proxy,
                    out string validationMessage);

                Assert.That(valid, Is.False);
                Assert.That(validationMessage, Does.Contain("stale"));
            }
            finally
            {
                Object.DestroyImmediate(graph);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void TryValidateMaterialProxy_RejectsGraphTopologyIncompatibleWithProxy()
        {
            var proxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var graph = ScriptableObject.CreateInstance<MaterialGraphImportAsset>();
            try
            {
                CompiledMaterialProgram dualProgram =
                    GPUDrivenMaterialCompiler.GetMaterialProgram(
                        VividMaterialProgramID.DualSlabHorizontalMix);
                graph.Apply(
                    CreateCompilationResult(dualProgram),
                    GPUDrivenMaterialCompiler.ProgramVersion,
                    GPUDrivenMaterialCompiler.ProgramCatalog);
                proxy.MaterialGraph = graph;

                bool valid = GPUDrivenMaterialCompiler.TryValidateMaterialProxy(
                    proxy,
                    out string validationMessage);

                Assert.That(valid, Is.False);
                Assert.That(validationMessage, Does.Contain("not compatible"));
            }
            finally
            {
                Object.DestroyImmediate(graph);
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void CompileStandardSingleSlab_FromMaterialData_UsesCatalogProgramAndRuntimeFlags()
        {
            var materialData = new VividMaterialData
            {
                AlbedoColor = new float4(0.8f, 0.6f, 0.4f, 0.75f),
                SurfaceBindingIndex = 11u,
                RendererListID =
                    VividRendererListID.CullOff | VividRendererListID.AlphaTest,
                AlphaClipThreshold = 0.42f,
            };

            GPUDrivenCompiledMaterialInstance compiled =
                GPUDrivenMaterialCompiler.CompileStandardSingleSlab(
                    in materialData,
                    parameterAddress: 7u,
                    surfaceBindingIndex: 11u);

            Assert.That(
                compiled.CatalogProgram,
                Is.SameAs(GPUDrivenMaterialCompiler.GetCatalogedMaterialProgram(
                    VividMaterialProgramID.StandardSingleSlab)));
            Assert.That(
                compiled.ProgramID,
                Is.EqualTo(VividMaterialProgramID.StandardSingleSlab));
            Assert.That(compiled.RuntimeHeader.ParameterAddress, Is.EqualTo(7u));
            Assert.That(
                compiled.RuntimeHeader.ResourceBindingAddress,
                Is.EqualTo(11u));
            Assert.That(
                compiled.RuntimeHeader.Flags,
                Is.EqualTo(VividMaterialRuntimeFlags.AlphaClip));
            Assert.That(
                compiled.LegacyMaterialData.AlbedoColor,
                Is.EqualTo(materialData.AlbedoColor));
            Assert.That(
                compiled.LegacyMaterialData.SurfaceBindingIndex,
                Is.EqualTo(11u));
        }

        [Test]
        public void CompileStandardSingleSlab_DeduplicatesTopologyAcrossInstances()
        {
            var first = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var second = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            try
            {
                first.Metallic = 0.1f;
                second.Metallic = 0.9f;

                GPUDrivenCompiledMaterialInstance firstCompiled =
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(first, 0u, 4u);
                GPUDrivenCompiledMaterialInstance secondCompiled =
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(second, 1u, 9u);

                Assert.That(firstCompiled.RuntimeHeader.ProgramID, Is.EqualTo(secondCompiled.RuntimeHeader.ProgramID));
                Assert.That(firstCompiled.RuntimeHeader.ParameterAddress, Is.Not.EqualTo(secondCompiled.RuntimeHeader.ParameterAddress));
                Assert.That(firstCompiled.RuntimeHeader.ResourceBindingAddress, Is.Not.EqualTo(secondCompiled.RuntimeHeader.ResourceBindingAddress));
                Assert.That(firstCompiled.LegacyMaterialData.Metallic, Is.Not.EqualTo(secondCompiled.LegacyMaterialData.Metallic));

                var sceneData = new VividGPUDrivenSceneData();
                Assert.That(
                    sceneData.MaterialProgramCount,
                    Is.EqualTo(
                        MaterialProgramContract.ProductionCatalogProgramCount));
                VividMaterialProgramData program = sceneData.MaterialPrograms[0];
                Assert.That(program.Version, Is.EqualTo(GPUDrivenMaterialCompiler.ProgramVersion));
                Assert.That(
                    program.CoverageProgramID,
                    Is.EqualTo(VividMaterialCoverageProgramID.BaseColorAlpha));
                Assert.That(program.SurfaceProgramID, Is.EqualTo(VividMaterialSurfaceProgramID.StandardSingleSlab));
                Assert.That(program.ParameterLayoutID, Is.EqualTo(VividMaterialParameterLayoutID.GenericParameterLanes));
                Assert.That(program.ResourceLayoutID, Is.EqualTo(VividMaterialResourceLayoutID.GenericResourceRecords));
                Assert.That(
                    program.CapabilityFlags & VividMaterialProgramCapabilities.AlphaClip,
                    Is.EqualTo(VividMaterialProgramCapabilities.AlphaClip));

                VividMaterialProgramData dualSlabProgram = sceneData.MaterialPrograms[
                    (int) VividMaterialProgramID.DualSlabHorizontalMix];
                Assert.That(
                    dualSlabProgram.SurfaceProgramID,
                    Is.EqualTo(VividMaterialSurfaceProgramID.DualSlab));
                Assert.That(
                    dualSlabProgram.ParameterLayoutID,
                    Is.EqualTo(VividMaterialParameterLayoutID.GenericParameterLanes));
                Assert.That(
                    dualSlabProgram.ResourceLayoutID,
                    Is.EqualTo(VividMaterialResourceLayoutID.GenericResourceRecords));
                Assert.That(
                    sceneData.MaterialPrograms[
                        (int) VividMaterialProgramID.DualSlabVerticalLayer].SurfaceProgramID,
                    Is.EqualTo(VividMaterialSurfaceProgramID.DualSlab));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void CompileDualSlab_ProducesVerticalLayerProgramWithTwoFixedSlabs()
        {
            var baseProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var topProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var definition =
                ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();
            try
            {
                baseProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                baseProxy.BaseColor = Color.red;
                baseProxy.Metallic = 0.2f;
                baseProxy.Roughness = 0.3f;
                baseProxy.LayerWeight = 0.65f;
                baseProxy.AlphaClip = true;
                baseProxy.Cutoff = 0.4f;
                topProxy.BaseColor = Color.blue;
                topProxy.Metallic = 0.8f;
                topProxy.Roughness = 0.7f;
                definition.TopSlab = topProxy;
                definition.Operator = VividDualSlabOperator.VerticalLayer;
                baseProxy.DualSlabDefinition = definition;

                GPUDrivenCompiledMaterialInstance compiled =
                    GPUDrivenMaterialCompiler.CompileDualSlab(
                        baseProxy,
                        parameterAddress: 3u,
                        baseSurfaceBindingIndex: 7u);

                Assert.That(
                    compiled.RuntimeHeader.ProgramID,
                    Is.EqualTo(VividMaterialProgramID.DualSlabVerticalLayer));
                Assert.That(compiled.RuntimeHeader.ParameterAddress, Is.EqualTo(3u));
                Assert.That(compiled.RuntimeHeader.ResourceBindingAddress, Is.EqualTo(7u));
                Assert.That(
                    compiled.RuntimeHeader.Flags & VividMaterialRuntimeFlags.AlphaClip,
                    Is.EqualTo(VividMaterialRuntimeFlags.AlphaClip));
                Assert.That(
                    compiled.DualSlabMaterialData.BaseMetallic,
                    Is.EqualTo(0.2f));
                Assert.That(
                    compiled.DualSlabMaterialData.TopMetallic,
                    Is.EqualTo(0.8f));
                Assert.That(
                    compiled.DualSlabMaterialData.LayerOperator,
                    Is.EqualTo(VividDualSlabOperator.VerticalLayer));
                Assert.That(compiled.DualSlabMaterialData.LayerWeight, Is.EqualTo(0.65f));
                Assert.That(compiled.DualSlabMaterialData.AlphaClipThreshold, Is.EqualTo(0.4f));
                Assert.That(compiled.LegacyMaterialData.SurfaceBindingIndex, Is.EqualTo(7u));
                Assert.That(compiled.ParameterLanes.Count, Is.EqualTo(6));
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(topProxy);
                Object.DestroyImmediate(baseProxy);
            }
        }

        [Test]
        public void TryValidateMaterialProxy_AcceptsStandardAndBothDualSlabOperators()
        {
            var standardProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var baseProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var topProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var definition =
                ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();
            try
            {
                Assert.That(
                    GPUDrivenMaterialCompiler.TryValidateMaterialProxy(
                        standardProxy,
                        out string standardValidationMessage),
                    Is.True);
                Assert.That(standardValidationMessage, Is.Empty);

                baseProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                definition.TopSlab = topProxy;
                baseProxy.DualSlabDefinition = definition;

                VividDualSlabOperator[] operators =
                {
                    VividDualSlabOperator.HorizontalMix,
                    VividDualSlabOperator.VerticalLayer,
                };
                foreach (VividDualSlabOperator layerOperator in operators)
                {
                    definition.Operator = layerOperator;

                    Assert.That(
                        GPUDrivenMaterialCompiler.TryValidateMaterialProxy(
                            baseProxy,
                            out string validationMessage),
                        Is.True,
                        layerOperator.ToString());
                    Assert.That(validationMessage, Is.Empty);
                }
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(topProxy);
                Object.DestroyImmediate(baseProxy);
                Object.DestroyImmediate(standardProxy);
            }
        }

        [Test]
        public void TryValidateMaterialProxy_RejectsDualSlabWithoutDefinition()
        {
            var baseProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            try
            {
                baseProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;

                AssertInvalidDualSlabProxy(baseProxy, "require a definition");
            }
            finally
            {
                Object.DestroyImmediate(baseProxy);
            }
        }

        [Test]
        public void TryValidateMaterialProxy_RejectsDualSlabWithoutTopSlab()
        {
            var baseProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var definition =
                ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();
            try
            {
                baseProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                baseProxy.DualSlabDefinition = definition;

                AssertInvalidDualSlabProxy(
                    baseProxy,
                    "require a StandardLit top-slab proxy");
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(baseProxy);
            }
        }

        [Test]
        public void TryValidateMaterialProxy_RejectsSelfReferentialDualSlab()
        {
            var baseProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var definition =
                ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();
            try
            {
                baseProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                definition.TopSlab = baseProxy;
                baseProxy.DualSlabDefinition = definition;

                AssertInvalidDualSlabProxy(
                    baseProxy,
                    "cannot use the base proxy as their top Slab");
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(baseProxy);
            }
        }

        [Test]
        public void TryValidateMaterialProxy_RejectsNonStandardTopSlab()
        {
            var baseProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var topProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var definition =
                ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();
            try
            {
                baseProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                topProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                definition.TopSlab = topProxy;
                baseProxy.DualSlabDefinition = definition;

                AssertInvalidDualSlabProxy(
                    baseProxy,
                    "nested Dual Slab topology is not supported");
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(topProxy);
                Object.DestroyImmediate(baseProxy);
            }
        }

        [Test]
        public void TryValidateMaterialProxy_RejectsUnsupportedDualSlabOperator()
        {
            var baseProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var topProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var definition =
                ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();
            try
            {
                baseProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                definition.TopSlab = topProxy;
                definition.Operator = (VividDualSlabOperator) 99;
                baseProxy.DualSlabDefinition = definition;

                AssertInvalidDualSlabProxy(baseProxy, "operator '99' is not supported");
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(topProxy);
                Object.DestroyImmediate(baseProxy);
            }
        }

        [Test]
        public void SceneData_AddMaterial_MaintainsIndexAlignedRuntimeHeaders()
        {
            var proxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            try
            {
                var sceneData = new VividGPUDrivenSceneData();
                GPUDrivenCompiledMaterialInstance first =
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(
                        proxy,
                        parameterAddress: 0u,
                        resourceBindingAddress: 0u,
                        legacySurfaceBindingIndex: 3u);
                GPUDrivenCompiledMaterialInstance second =
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(
                        proxy,
                        parameterAddress: 4u,
                        resourceBindingAddress: 1u,
                        legacySurfaceBindingIndex: 5u);
                for (int bindingIndex = 0; bindingIndex < 6; bindingIndex++)
                    sceneData.MutableSurfaceBindings.Add(default);
                sceneData.MutableMaterialParameterLanes.AddRange(first.ParameterLanes);
                sceneData.MutableMaterialParameterLanes.AddRange(second.ParameterLanes);
                sceneData.MutableMaterialResources.Add(default);
                sceneData.MutableMaterialResources.Add(default);

                Assert.That(
                    sceneData.AddMaterial(first.LegacyMaterialData, first.RuntimeHeader),
                    Is.Zero);
                Assert.That(
                    sceneData.AddMaterial(second.LegacyMaterialData, second.RuntimeHeader),
                    Is.EqualTo(1));
                Assert.That(sceneData.MaterialCount, Is.EqualTo(2));
                Assert.That(sceneData.MaterialRuntimeHeaderCount, Is.EqualTo(2));
                Assert.That(sceneData.MaterialRuntimeHeaders[0].ParameterAddress, Is.Zero);
                Assert.That(sceneData.MaterialRuntimeHeaders[1].ParameterAddress, Is.EqualTo(4u));

                VividMaterialRuntimeHeader mismatchedHeader = second.RuntimeHeader;
                mismatchedHeader.ParameterAddress = 5u;
                Assert.Throws<System.ArgumentException>(() =>
                    sceneData.AddMaterial(second.LegacyMaterialData, mismatchedHeader));
                Assert.That(sceneData.MaterialCount, Is.EqualTo(2));
                Assert.That(sceneData.MaterialRuntimeHeaderCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void AddLegacyMaterial_UsesInvalidProgramWithoutBreakingIndexAlignment()
        {
            var sceneData = new VividGPUDrivenSceneData();
            int index = sceneData.AddLegacyMaterial(new VividMaterialData
            {
                SurfaceBindingIndex = 6u,
            });

            Assert.That(index, Is.Zero);
            Assert.That(sceneData.MaterialRuntimeHeaderCount, Is.EqualTo(1));
            VividMaterialRuntimeHeader header = sceneData.MaterialRuntimeHeaders[0];
            Assert.That(header.ProgramID, Is.EqualTo(VividMaterialProgramID.Invalid));
            Assert.That(header.ParameterAddress, Is.Zero);
            Assert.That(header.ResourceBindingAddress, Is.EqualTo(6u));
        }

        [Test]
        public void SceneData_AddMaterial_UsesCompiledResourceLayoutRecordCount()
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
                    GPUDrivenMaterialCompiler.CompileDualSlab(
                        baseProxy,
                        parameterAddress: 0u,
                        baseSurfaceBindingIndex: 0u);

                var sceneData = new VividGPUDrivenSceneData();
                sceneData.MutableDualSlabMaterials.Add(compiled.DualSlabMaterialData);
                sceneData.MutableMaterialParameterLanes.AddRange(compiled.ParameterLanes);
                sceneData.MutableMaterialResources.Add(default);
                Assert.Throws<System.ArgumentException>(() => sceneData.AddMaterial(
                    compiled.LegacyMaterialData,
                    compiled.RuntimeHeader));

                sceneData.MutableMaterialResources.Add(default);
                Assert.That(
                    sceneData.AddMaterial(
                        compiled.LegacyMaterialData,
                        compiled.RuntimeHeader),
                    Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(topProxy);
                Object.DestroyImmediate(baseProxy);
            }
        }

        private static MaterialGraphCompilationResult CreateCompilationResult(
            CompiledMaterialProgram program)
        {
            return new MaterialGraphCompilationResult(
                program,
                program.Module,
                new MaterialGraphProvenance(
                    new Dictionary<string, HashSet<int>>(),
                    new Dictionary<string, HashSet<int>>()),
                System.Array.Empty<MaterialGraphDiagnostic>());
        }

        private static MaterialGraphCompilationResult CompileNamedParameterProgram()
        {
            var graph = new MaterialGraph();
            MaterialGraphValue tint = graph.Parameter(
                "UserTint",
                "UserTint",
                MaterialValueType.Float4);
            MaterialGraphValue normal = graph.ExternalInput(
                "GeometryNormal",
                MaterialExternalInput.GeometryNormalWS);
            MaterialGraphValue tangent = graph.ExternalInput(
                "GeometryTangent",
                MaterialExternalInput.GeometryTangentWS);
            MaterialGraphClosure slab = graph.Slab(
                "Slab",
                tint,
                graph.Constant("Roughness", 0.5f),
                graph.Constant("Metallic", 0.0f),
                normal,
                tangent);
            graph.Output(
                "Output",
                slab,
                graph.Swizzle("Coverage", tint, MaterialSwizzleMask.W),
                graph.Constant("AlphaClipThreshold", 0.0f),
                graph.Constant("Emission", new float3(0.0f)));

            MaterialGraphCompilationResult compilation =
                MaterialGraphCompiler.Compile(
                    graph,
                    GPUDrivenMaterialCompiler.ProgramVersion);
            Assert.That(
                compilation.Succeeded,
                Is.True,
                string.Join("\n", compilation.Diagnostics));
            return compilation;
        }

        private static MaterialProgramCatalog CreateExtendedCatalog(
            string customStableName,
            CompiledMaterialProgram customProgram)
        {
            MaterialProgramCatalog production =
                GPUDrivenMaterialCompiler.ProgramCatalog;
            var slots = new List<MaterialProgramCatalogBakeSlot>(
                production.RuntimeTableLength + 1);
            for (int slotIndex = 0;
                 slotIndex < production.RuntimeTableLength;
                 slotIndex++)
            {
                MaterialProgramCatalog.ManifestEntry entry =
                    production.Slots[slotIndex];
                slots.Add(entry == null
                    ? MaterialProgramCatalogBakeSlot.Reserved(
                        $"P{slotIndex}.Reserved")
                    : MaterialProgramCatalogBakeSlot.ForProgram(
                        entry.StableName,
                        entry.Program));
            }
            slots.Add(MaterialProgramCatalogBakeSlot.ForProgram(
                customStableName,
                customProgram));
            return MaterialProgramCatalog.Bake(
                production.Templates,
                slots.ToArray());
        }

        private static void AssertParameterValue(
            GPUDrivenCompiledMaterialInstance compiled,
            in MaterialGenericParameterBinding binding,
            Vector4 expected)
        {
            uint4 expectedBits = math.asuint(new float4(
                expected.x,
                expected.y,
                expected.z,
                expected.w));
            for (int wordIndex = 0; wordIndex < binding.WordCount; wordIndex++)
            {
                int absoluteWord = binding.WordOffset + wordIndex;
                uint4 lane = compiled.ParameterLanes[absoluteWord / 4];
                Assert.That(
                    lane[absoluteWord % 4],
                    Is.EqualTo(expectedBits[wordIndex]),
                    $"Parameter word {wordIndex}");
            }
        }

        private static string GetHlslStructSignature(string source, string structName)
        {
            string declaration = $"struct {structName}";
            int start = source.IndexOf(declaration, System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing {declaration}.");
            int end = source.IndexOf("};", start, System.StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start), $"Incomplete {declaration}.");
            string body = source.Substring(start, end - start + 2);
            string signature = string.Join(
                " ",
                body.Split((char[]) null, System.StringSplitOptions.RemoveEmptyEntries));
            // GenerateHLSL emits default-underlying C# enums as int, while the runtime
            // contract exposes the same 32-bit bitfields as uint.
            return signature.Replace(" int ", " uint ");
        }

        private static void AssertInvalidDualSlabProxy(
            GPUDrivenMaterialProxy materialProxy,
            string expectedMessageFragment)
        {
            Assert.That(
                GPUDrivenMaterialCompiler.TryValidateMaterialProxy(
                    materialProxy,
                    out string validationMessage),
                Is.False);
            Assert.That(validationMessage, Does.Contain(expectedMessageFragment));

            System.InvalidOperationException exception =
                Assert.Throws<System.InvalidOperationException>(() =>
                    GPUDrivenMaterialCompiler.CompileDualSlab(
                        materialProxy,
                        parameterAddress: 0u,
                        baseSurfaceBindingIndex: 0u));
            Assert.That(exception.Message, Is.EqualTo(validationMessage));
        }
    }
}
