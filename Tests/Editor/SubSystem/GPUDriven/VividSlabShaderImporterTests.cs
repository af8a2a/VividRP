using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class VividSlabShaderImporterTests
    {
        private const string SampleSlabShaderRelativePath =
            "Shaders/Material/Samples/SubstratePoC.slab.shader";
        private const string MultiClosureSampleSlabShaderRelativePath =
            "Shaders/Material/Samples/SubstrateMultiClosurePoC.slab.shader";
        private const string BaseTemplateRelativePath =
            "Shaders/Material/Templates/SubstrateOpaque.slab.template";
        private const string StandardLitRelativePath =
            "Shaders/Material/StandardLit/StandardLit.shader";

        [Test]
        public void ParseDirectives_ExtractsBaseGraphAndShaderName()
        {
            const string sample =
                "// @vivid-base \"Packages/com.vivid.render-pipelines/Shaders/Material/Templates/SubstrateOpaque.slab.template\"\n"
                + "// @vivid-graph \"Packages/com.vivid.render-pipelines/Runtime/Resources/MaterialGraphs/OpenPBROpaque.vmatg\"\n"
                + "Shader \"VividRP/Material/TestSlab\"\n"
                + "{\n"
                + "}\n";

            VividSlabShaderDirectives directives = VividSlabShaderAssembler.ParseDirectives(sample);
            Assert.That(
                directives.BaseTemplatePath,
                Is.EqualTo("Packages/com.vivid.render-pipelines/Shaders/Material/Templates/SubstrateOpaque.slab.template"));
            Assert.That(
                directives.LinkedGraphPath,
                Is.EqualTo("Packages/com.vivid.render-pipelines/Runtime/Resources/MaterialGraphs/OpenPBROpaque.vmatg"));
            Assert.That(directives.ShaderName, Is.EqualTo("VividRP/Material/TestSlab"));
        }

        [Test]
        public void DualTargetStageLIRBackend_LowersOpenPBROpaqueAndSingleSlabToRasterAndCompute()
        {
            CompiledMaterialProgram openPbrProgram = BuildOpenPBROpaqueTestProgram();
            MaterialSlabHlslPayload openPbrPayload =
                MaterialSlabHlslBackend.Compile(openPbrProgram);

            Assert.That(openPbrPayload.ParameterSlots.Length, Is.GreaterThanOrEqualTo(11));
            Assert.That(openPbrPayload.ParameterStrideInWords % 4, Is.Zero);
            StringAssert.Contains("_BaseColor", openPbrPayload.PropertiesBlock);
            StringAssert.Contains("_SpecularIor", openPbrPayload.PropertiesBlock);
            StringAssert.Contains("VividSlabEvaluateGraphSurface(", openPbrPayload.RasterEvaluateFunction);
            StringAssert.Contains("VividSlabEvaluateGraphSurface(", openPbrPayload.ComputeEvaluateFunction);
            StringAssert.Contains("v2Inputs.core.specularIor =", openPbrPayload.RasterEvaluateFunction);
            StringAssert.Contains("VividLoadMaterialFloat(", openPbrPayload.ComputeEvaluateFunction);
            StringAssert.DoesNotContain("VividLoadMaterialFloat(", openPbrPayload.RasterEvaluateFunction);

            CompiledMaterialProgram singleSlabProgram =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    GPUDrivenMaterialCompiler.ProgramVersion);
            MaterialSlabHlslPayload singleSlabPayload =
                MaterialSlabHlslBackend.Compile(singleSlabProgram);

            Assert.That(singleSlabPayload.ResourceSlots.Length, Is.GreaterThanOrEqualTo(1));
            StringAssert.Contains("SAMPLE_TEXTURE2D(", singleSlabPayload.RasterEvaluateFunction);
            StringAssert.Contains("PullMaterialResourceData(", singleSlabPayload.ComputeEvaluateFunction);
            StringAssert.Contains("VividCreateSurfaceSampleContextGrad(", singleSlabPayload.ComputeEvaluateFunction);
            StringAssert.Contains("VividStandardLitToOpenPBRV2Inputs(", singleSlabPayload.ComputeEvaluateFunction);
        }

        [Test]
        public void Assemble_WithBaseTemplateGraphAndCustomPostHook_EmitsBothTargetsWithoutSwitch()
        {
            string samplePath = VividPackagePathUtility.GetPreferredAssetPath(SampleSlabShaderRelativePath);
            string templatePath = VividPackagePathUtility.GetPreferredAssetPath(BaseTemplateRelativePath);
            string sourceText = File.ReadAllText(samplePath);
            string templateText = File.ReadAllText(templatePath);
            CompiledMaterialProgram openPbrProgram = BuildOpenPBROpaqueTestProgram();

            VividSlabShaderAssemblyResult first = VividSlabShaderAssembler.Assemble(
                samplePath,
                sourceText,
                templatePath,
                templateText,
                resolvedLinkedGraphPath: "OpenPBROpaque.vmatg",
                compiledGraphProgram: openPbrProgram);
            VividSlabShaderAssemblyResult second = VividSlabShaderAssembler.Assemble(
                samplePath,
                sourceText,
                templatePath,
                templateText,
                resolvedLinkedGraphPath: "OpenPBROpaque.vmatg",
                compiledGraphProgram: openPbrProgram);

            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.HasLinkedGraphSurface, Is.True);
            Assert.That(first.HasCustomSurfaceHook, Is.True);
            Assert.That(first.EstimatedSubstrateBytesPerPixel, Is.GreaterThanOrEqualTo(32));
            Assert.That(first.SourceContentHash, Is.EqualTo(second.SourceContentHash));
            Assert.That(first.ParameterSlots.Length, Is.GreaterThanOrEqualTo(11));

            // Raster output expands Base SubShader + Graph + Custom PostHook without #line inside Properties.
            int propsStart = first.RasterShaderSource.IndexOf("Properties", StringComparison.Ordinal);
            int subShaderStart = first.RasterShaderSource.IndexOf("SubShader", StringComparison.Ordinal);
            string propsSlice = first.RasterShaderSource.Substring(propsStart, subShaderStart - propsStart);
            StringAssert.DoesNotContain("#line", propsSlice);
            StringAssert.Contains("VividSlabPostEvaluateSurface(", first.RasterShaderSource);
            StringAssert.Contains("VividWriteSubstrateOpenPBRV2Single(", first.RasterShaderSource);
            StringAssert.Contains("register(u5)", first.RasterShaderSource);

            // Standalone GPUDriven ComputeShader: VT-first multi_compile, StageLIR + PostHook, zero switch.
            StringAssert.Contains(
                "#pragma kernel " + VividSlabProgramDescriptor.DefaultResolveKernelName,
                first.ComputeResolveSource);
            StringAssert.Contains(
                "VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE",
                first.ComputeResolveSource);
            StringAssert.Contains("VividSlabEvaluateGraphSurface(", first.ComputeResolveSource);
            StringAssert.Contains("VividSlabPostEvaluateSurface(", first.ComputeResolveSource);
            StringAssert.Contains("VividWriteSubstrateOpenPBRV2Single(", first.ComputeResolveSource);
            StringAssert.Contains("runtimeHeader.ProgramID != _TargetMaterialBin", first.ComputeResolveSource);
            StringAssert.DoesNotContain("switch (", first.ComputeResolveSource);
            StringAssert.DoesNotContain("switch(", first.ComputeResolveSource);
        }

        [Test]
        public void DynamicSlabProgramRegistration_PacksLanesAndRequiresSubstrateBuffer_ZeroAllocation()
        {
            VividSlabProgramDescriptor descriptor = ScriptableObject.CreateInstance<VividSlabProgramDescriptor>();
            try
            {
                var paramSlots = new[]
                {
                    new VividSlabParameterSlot
                    {
                        Symbol = "SpecularIor",
                        PropertyName = "_SpecularIor",
                        Kind = VividSlabParameterKind.Float,
                        WordOffset = 0,
                        WordCount = 1,
                        DefaultValue = new Vector4(1.5f, 0f, 0f, 0f),
                    },
                    new VividSlabParameterSlot
                    {
                        Symbol = "BaseColor",
                        PropertyName = "_BaseColor",
                        Kind = VividSlabParameterKind.Float3,
                        WordOffset = 1,
                        WordCount = 3,
                        DefaultValue = new Vector4(0.2f, 0.4f, 0.6f, 1.0f),
                    },
                };
                descriptor.Apply(
                    rasterShader: null,
                    resolveComputeShader: null,
                    resolveKernelName: VividSlabProgramDescriptor.DefaultResolveKernelName,
                    shaderName: "VividRP/Material/TestDynamic",
                    baseTemplateAssetPath: string.Empty,
                    linkedGraphAssetPath: string.Empty,
                    estimatedSubstrateBytesPerPixel: 32,
                    parameterStrideInWords: 4,
                    parameters: paramSlots,
                    resources: Array.Empty<VividSlabResourceSlot>(),
                    hasLinkedGraphSurface: true,
                    hasCustomSurfaceHook: true,
                    sourceContentHash: 0x12345678u,
                    hasImportErrors: false,
                    diagnostics: Array.Empty<string>());

                var scene = new VividGPUDrivenSceneData();
                VividMaterialProgramID firstId = scene.RegisterDynamicSlabProgram(descriptor);
                VividMaterialProgramID duplicateId = scene.RegisterDynamicSlabProgram(descriptor);
                Assert.That(duplicateId, Is.EqualTo(firstId));
                Assert.That((uint)firstId, Is.GreaterThanOrEqualTo(1u));
                Assert.That(scene.DynamicSlabPrograms.Count, Is.EqualTo(1));

                descriptor.PackParameterLanes(null, null, scene.MutableMaterialParameterLanes);
                Assert.That(scene.MaterialParameterLaneCount, Is.EqualTo(1));
                uint4 lane = scene.MaterialParameterLanes[0];
                Assert.That(math.asfloat(lane.x), Is.EqualTo(1.5f).Within(1e-5f));
                Assert.That(math.asfloat(lane.y), Is.EqualTo(0.2f).Within(1e-5f));
                Assert.That(math.asfloat(lane.z), Is.EqualTo(0.4f).Within(1e-5f));
                Assert.That(math.asfloat(lane.w), Is.EqualTo(0.6f).Within(1e-5f));

                int matIdx = scene.AddMaterial(
                    new VividMaterialData { SpecularIOR = 1.5f },
                    new VividMaterialRuntimeHeader
                    {
                        ProgramID = firstId,
                        ParameterAddress = 0u,
                        ResourceBindingAddress = 0u,
                        Flags = VividMaterialRuntimeFlags.None,
                    });
                Assert.That(matIdx, Is.EqualTo(0));

                // Warmup + zero-allocation check on RequiresSubstrateBuffer and PackParameterLanes
                var scratchLanes = new List<uint4>(4);
                for (int i = 0; i < 32; ++i)
                {
                    scene.RequiresSubstrateBuffer(0u);
                    scratchLanes.Clear();
                    descriptor.PackParameterLanes(null, null, scratchLanes);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                bool req = true;
                for (int i = 0; i < 128; ++i)
                {
                    req &= scene.RequiresSubstrateBuffer(0u);
                    scratchLanes.Clear();
                    descriptor.PackParameterLanes(null, null, scratchLanes);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(req, Is.True);
                Assert.That(allocated, Is.Zero, "Steady-state dynamic Slab queries and parameter packing must allocate zero managed memory.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(descriptor);
            }
        }

        [Test]
        public void CompositeExtension_ClaimsSlabShaderAndLeavesRegularShaderUntouched()
        {
            string slabPath = VividPackagePathUtility.GetPreferredAssetPath(SampleSlabShaderRelativePath);
            AssetImporter slabImporter = AssetImporter.GetAtPath(slabPath);
            if (slabImporter == null)
            {
                Assert.Ignore("AssetDatabase has not imported SubstratePoC.slab.shader yet.");
            }

            Assert.That(
                slabImporter,
                Is.TypeOf<VividSlabShaderImporter>(),
                "Composite extension .slab.shader must be claimed by VividSlabShaderImporter.");

            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(slabPath);
            Assert.That(mainAsset, Is.InstanceOf<Shader>(), "Main asset of .slab.shader must be a Unity Shader.");

            UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(slabPath);
            ComputeShader resolveCompute = null;
            VividSlabProgramDescriptor descriptor = null;
            for (int i = 0; i < allAssets.Length; i++)
            {
                if (allAssets[i] is ComputeShader cs)
                    resolveCompute = cs;
                else if (allAssets[i] is VividSlabProgramDescriptor desc)
                    descriptor = desc;
            }

            Assert.That(resolveCompute, Is.Not.Null, "SubAsset GPUDriven ComputeShader must be created.");
            Assert.That(descriptor, Is.Not.Null, "SubAsset VividSlabProgramDescriptor must be created.");
            Assert.That(descriptor.HasImportErrors, Is.False);
            Assert.That(descriptor.IsGPUDrivenResolveReady, Is.True);
            Assert.That(descriptor.HasLinkedGraphSurface, Is.True);
            Assert.That(descriptor.HasCustomSurfaceHook, Is.True);
            Assert.That(descriptor.RasterShader, Is.SameAs(mainAsset));
            Assert.That(descriptor.ResolveComputeShader, Is.SameAs(resolveCompute));
            Assert.That(
                resolveCompute.HasKernel(descriptor.ResolveKernelName),
                Is.True,
                $"ComputeShader SubAsset must contain kernel '{descriptor.ResolveKernelName}'.");

            Assert.That(
                VividSlabProgramRegistry.TryGetDescriptor((Shader)mainAsset, out VividSlabProgramDescriptor lookedUp),
                Is.True);
            Assert.That(lookedUp, Is.SameAs(descriptor));

            string stdLitPath = VividPackagePathUtility.GetPreferredAssetPath(StandardLitRelativePath);
            AssetImporter stdLitImporter = AssetImporter.GetAtPath(stdLitPath);
            Assert.That(stdLitImporter, Is.Not.Null);
            Assert.That(
                stdLitImporter,
                Is.Not.TypeOf<VividSlabShaderImporter>(),
                "Regular .shader files must continue using Unity's built-in ShaderImporter.");
        }

        [Test]
        public void DualTargetStageLIRBackend_LowersMultiClosureTreesToTier3RasterAndCompute()
        {
            CompiledMaterialProgram dualMix = BuildMultiClosureTestProgram(closureCount: 2);
            MaterialSlabHlslPayload dualPayload = MaterialSlabHlslBackend.Compile(dualMix);
            Assert.That(dualPayload.IsMultiClosure, Is.True);
            Assert.That(dualPayload.ClosureCount, Is.EqualTo(2));
            Assert.That(
                dualPayload.EstimatedSubstrateBytesPerPixel,
                Is.EqualTo(VividSubstrateContract.ComputeMultiClosureTotalSlices(2) * VividSubstrateContract.BytesPerSlice));
            StringAssert.Contains("#define VIVID_SLAB_MULTI_CLOSURE_OUTPUT 1", dualPayload.RasterEvaluateFunction);
            StringAssert.Contains("#define VIVID_SLAB_MULTI_CLOSURE_OUTPUT 1", dualPayload.ComputeEvaluateFunction);
            StringAssert.Contains("VividSlabEvaluateMultiClosureGraphSurface(", dualPayload.RasterEvaluateFunction);
            StringAssert.Contains("VividSlabEvaluateMultiClosureGraphSurface(", dualPayload.ComputeEvaluateFunction);
            StringAssert.Contains("VIVID_SUBSTRATE_TOPOLOGY_HORIZONTAL_MIX", dualPayload.ComputeEvaluateFunction);

            CompiledMaterialProgram triTree = BuildMultiClosureTestProgram(closureCount: 3);
            MaterialSlabHlslPayload triPayload = MaterialSlabHlslBackend.Compile(triTree);
            Assert.That(triPayload.IsMultiClosure, Is.True);
            Assert.That(triPayload.ClosureCount, Is.EqualTo(3));
            Assert.That(
                triPayload.EstimatedSubstrateBytesPerPixel,
                Is.EqualTo(VividSubstrateContract.ComputeMultiClosureTotalSlices(3) * VividSubstrateContract.BytesPerSlice));
            StringAssert.Contains("mcOutput.closureCount = 3u;", triPayload.RasterEvaluateFunction);
            StringAssert.Contains("mcOutput.closureCount = 3u;", triPayload.ComputeEvaluateFunction);
            StringAssert.Contains("VIVID_SUBSTRATE_TOPOLOGY_HORIZONTAL_MIX", triPayload.ComputeEvaluateFunction);
            StringAssert.Contains("VIVID_SUBSTRATE_TOPOLOGY_VERTICAL_LAYER", triPayload.ComputeEvaluateFunction);

            string samplePath = VividPackagePathUtility.GetPreferredAssetPath(SampleSlabShaderRelativePath);
            string templatePath = VividPackagePathUtility.GetPreferredAssetPath(BaseTemplateRelativePath);
            VividSlabShaderAssemblyResult assembled = VividSlabShaderAssembler.Assemble(
                samplePath,
                File.ReadAllText(samplePath),
                templatePath,
                File.ReadAllText(templatePath),
                resolvedLinkedGraphPath: "TriClosure.vmatg",
                compiledGraphProgram: triTree);

            Assert.That(assembled.Succeeded, Is.True);
            Assert.That(assembled.IsMultiClosure, Is.True);
            Assert.That(assembled.ClosureCount, Is.EqualTo(3));
            StringAssert.Contains("VividWriteSubstrateMultiClosure(", assembled.RasterShaderSource);
            StringAssert.Contains("VividWriteSubstrateMultiClosure(", assembled.ComputeResolveSource);
            StringAssert.Contains("VividBuildMultiClosureTopLayerSummary(", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch(", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch (", assembled.ComputeResolveSource);
        }

        [Test]
        public void Assemble_WithWpoAndCustomCoverage_DisablesEarlyDepthAndEnablesProgrammableVisibilityRaster()
        {
            string samplePath = VividPackagePathUtility.GetPreferredAssetPath(MultiClosureSampleSlabShaderRelativePath);
            string templatePath = VividPackagePathUtility.GetPreferredAssetPath(BaseTemplateRelativePath);
            CompiledMaterialProgram openPbrProgram = BuildOpenPBROpaqueTestProgram();

            VividSlabShaderAssemblyResult assembled = VividSlabShaderAssembler.Assemble(
                samplePath,
                File.ReadAllText(samplePath),
                templatePath,
                File.ReadAllText(templatePath),
                resolvedLinkedGraphPath: "OpenPBROpaque.vmatg",
                compiledGraphProgram: openPbrProgram);

            Assert.That(assembled.Succeeded, Is.True);
            Assert.That(assembled.HasVertexOffset, Is.True);
            Assert.That(assembled.HasCustomCoverage, Is.True);
            Assert.That(assembled.HasCustomSurfaceHook, Is.True);
            StringAssert.Contains("VividGBufferGPUDrivenDecal", assembled.RasterShaderSource);
            StringAssert.Contains("VividVisibilityRaster", assembled.RasterShaderSource);
            StringAssert.Contains("ApplyVividGPUDrivenDecalsToOpenPBRV2Inputs(", assembled.RasterShaderSource);
            StringAssert.Contains("VividSlabEvaluateVertexOffset(", assembled.RasterShaderSource);
            StringAssert.Contains("VividSlabEvaluateCoverage(", assembled.RasterShaderSource);
            StringAssert.DoesNotContain("[earlydepthstencil]", assembled.RasterShaderSource);
            StringAssert.Contains("VividSlabEvaluateVertexOffset(", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch(", assembled.ComputeResolveSource);
        }

        [Test]
        public void RegisterDynamicSlabProgram_WithProgrammableVisibilityRaster_SetsCapabilityFlags_ZeroAllocation()
        {
            VividSlabProgramDescriptor descriptor = ScriptableObject.CreateInstance<VividSlabProgramDescriptor>();
            try
            {
                descriptor.Apply(
                    rasterShader: null,
                    resolveComputeShader: null,
                    resolveKernelName: VividSlabProgramDescriptor.DefaultResolveKernelName,
                    shaderName: "VividRP/Material/TestProgrammable",
                    baseTemplateAssetPath: string.Empty,
                    linkedGraphAssetPath: string.Empty,
                    estimatedSubstrateBytesPerPixel: 72,
                    parameterStrideInWords: 4,
                    parameters: Array.Empty<VividSlabParameterSlot>(),
                    resources: Array.Empty<VividSlabResourceSlot>(),
                    hasLinkedGraphSurface: true,
                    hasCustomSurfaceHook: true,
                    sourceContentHash: 0xABCDEF01u,
                    hasImportErrors: false,
                    diagnostics: Array.Empty<string>(),
                    isMultiClosure: true,
                    closureCount: 3,
                    hasCustomCoverage: true,
                    hasVertexOffset: true);

                Assert.That(descriptor.IsMultiClosure, Is.True);
                Assert.That(descriptor.ClosureCount, Is.EqualTo(3));
                Assert.That(descriptor.HasCustomCoverage, Is.True);
                Assert.That(descriptor.HasVertexOffset, Is.True);
                Assert.That(descriptor.RequiresProgrammableVisibilityRaster, Is.True);

                var scene = new VividGPUDrivenSceneData();
                VividMaterialProgramID programId = scene.RegisterDynamicSlabProgram(descriptor);
                VividMaterialProgramData progData = scene.MaterialPrograms[(int)(uint)programId];
                Assert.That(
                    (progData.CapabilityFlags & VividMaterialProgramCapabilities.ProgrammableRaster) != 0,
                    Is.True);
                Assert.That(
                    (progData.CapabilityFlags & VividMaterialProgramCapabilities.AlphaClip) != 0,
                    Is.True);
                Assert.That(scene.HasProgrammableVisibilityRasterPrograms(), Is.True);

                for (int i = 0; i < 32; ++i)
                {
                    scene.HasProgrammableVisibilityRasterPrograms();
                    scene.TryGetDynamicSlabProgram(programId, out _);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                bool hasProg = true;
                for (int i = 0; i < 128; ++i)
                {
                    hasProg &= scene.HasProgrammableVisibilityRasterPrograms();
                    hasProg &= scene.TryGetDynamicSlabProgram(programId, out _);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(hasProg, Is.True);
                Assert.That(allocated, Is.Zero, "Programmable VisibilityRaster queries must allocate zero managed memory.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(descriptor);
            }
        }

        private static CompiledMaterialProgram BuildMultiClosureTestProgram(int closureCount)
        {
            var values = new MaterialValueIR();
            var closures = new ClosureExpressionGraph(values);
            MaterialClosure slabA = closures.Slab(new ClosureSlabExpression(
                values.Parameter(new MaterialParameterDeclaration("BaseColorA", MaterialValueType.Float3)),
                values.Parameter(new MaterialParameterDeclaration("RoughnessA", MaterialValueType.Float)),
                values.Parameter(new MaterialParameterDeclaration("MetallicA", MaterialValueType.Float)),
                values.Constant(new float3(0.0f, 0.0f, 1.0f)),
                values.Constant(new float3(1.0f, 0.0f, 0.0f)),
                ClosureFeatureMask.None));
            MaterialClosure slabB = closures.Slab(new ClosureSlabExpression(
                values.Parameter(new MaterialParameterDeclaration("BaseColorB", MaterialValueType.Float3)),
                values.Parameter(new MaterialParameterDeclaration("RoughnessB", MaterialValueType.Float)),
                values.Parameter(new MaterialParameterDeclaration("MetallicB", MaterialValueType.Float)),
                values.Constant(new float3(0.0f, 0.0f, 1.0f)),
                values.Constant(new float3(1.0f, 0.0f, 0.0f)),
                ClosureFeatureMask.None));
            MaterialValue mixWeight = values.Parameter(new MaterialParameterDeclaration("MixWeight", MaterialValueType.Float));
            MaterialClosure root = closures.HorizontalMix(slabA, slabB, mixWeight);

            if (closureCount >= 3)
            {
                MaterialClosure slabC = closures.Slab(new ClosureSlabExpression(
                    values.Parameter(new MaterialParameterDeclaration("BaseColorC", MaterialValueType.Float3)),
                    values.Parameter(new MaterialParameterDeclaration("RoughnessC", MaterialValueType.Float)),
                    values.Constant(0.0f),
                    values.Constant(new float3(0.0f, 0.0f, 1.0f)),
                    values.Constant(new float3(1.0f, 0.0f, 0.0f)),
                    ClosureFeatureMask.None));
                MaterialValue layerWeight = values.Parameter(new MaterialParameterDeclaration("LayerWeight", MaterialValueType.Float));
                root = closures.VerticalLayer(root, slabC, layerWeight);
            }

            var module = new MaterialIRModule(
                values,
                new MaterialOutputRoots(
                    values.Constant(1.0f),
                    values.Constant(0.0f),
                    values.Constant(new float3(0.0f))),
                closures,
                root,
                ClosureTopologyBudget.SubstrateMultiClosure,
                MaterialFeatureMask.None,
                MaterialShadingModelMask.StandardLit);
            return CompiledMaterialProgram.Compile(module, GPUDrivenMaterialCompiler.ProgramVersion);
        }

        private static CompiledMaterialProgram BuildOpenPBROpaqueTestProgram()
        {
            var values = new MaterialValueIR();
            var inputs = new MaterialValue[(int)OpenPBROpaqueContract.FieldCount];
            for (int i = 0; i < inputs.Length; i++)
            {
                var semantic = (OpenPBROpaqueFieldSemantic)i;
                inputs[i] = values.Parameter(new MaterialParameterDeclaration(
                    semantic.ToString(),
                    ClosureOpenPBROpaqueExpression.GetFieldType(i)));
            }
            var closures = new ClosureExpressionGraph(values);
            MaterialClosure surface = closures.OpenPBROpaque(new ClosureOpenPBROpaqueExpression(
                inputs[0], inputs[1], inputs[2], inputs[3], inputs[4], inputs[5],
                inputs[6], inputs[7], inputs[8], inputs[9], inputs[10]));
            var module = new MaterialIRModule(
                values,
                new MaterialOutputRoots(
                    values.Constant(1.0f),
                    values.Constant(0.0f),
                    values.Constant(new float3(0.0f))),
                closures,
                surface,
                ClosureTopologyBudget.Prototype,
                MaterialFeatureMask.None,
                MaterialShadingModelMask.OpenPBROpaque);
            return CompiledMaterialProgram.Compile(module, GPUDrivenMaterialCompiler.ProgramVersion);
        }
    }
}
