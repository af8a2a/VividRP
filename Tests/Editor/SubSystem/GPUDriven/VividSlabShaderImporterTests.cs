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
