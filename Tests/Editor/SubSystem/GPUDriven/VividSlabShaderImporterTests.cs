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
        private const string ClearCoatDualNormalSampleRelativePath =
            "Shaders/Material/Samples/ClearCoatDualNormal.slab.shader";
        private const string FabricSlabRelativePath =
            "Shaders/Material/Fabric/FabricSlab.slab.shader";
        private const string FurSlabRelativePath =
            "Shaders/Material/Fur/FurSlab.slab.shader";
        private const string BaseTemplateRelativePath =
            "Shaders/Material/Templates/SubstrateOpaque.slab.template";
        private const string StandardLitRelativePath =
            "Shaders/Material/StandardLit/StandardLit.shader";
        private const string StandardLitSlabRelativePath =
            "Shaders/Material/StandardLit/StandardLitSlab.slab.shader";
        private const string StandardLitTemplateRelativePath =
            "Shaders/Material/Templates/StandardLit.slab.template";

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
        public void Assemble_ClearCoatDualNormalSample_EstimatesDualNormalBudgetAndEmitsBothTargets()
        {
            string samplePath = VividPackagePathUtility.GetPreferredAssetPath(ClearCoatDualNormalSampleRelativePath);
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
            Assert.That(assembled.ShaderName, Is.EqualTo("VividRP/Material/Samples/ClearCoatDualNormal"));
            Assert.That(assembled.HasLinkedGraphSurface, Is.True);
            Assert.That(assembled.HasCustomSurfaceHook, Is.True);
            Assert.That(assembled.IsMultiClosure, Is.False);
            Assert.That(
                assembled.EstimatedSubstrateBytesPerPixel,
                Is.EqualTo(
                    VividSubstrateContract.ComputeSingleClosureTotalSlices(
                        VividSubstrateFeatureMask.OpenPBROpaque
                        | VividSubstrateFeatureMask.Coat
                        | VividSubstrateFeatureMask.DualNormal
                        | VividSubstrateFeatureMask.Anisotropy)
                    * VividSubstrateContract.BytesPerSlice));

            StringAssert.Contains("_CoatWeight", assembled.RasterShaderSource);
            StringAssert.Contains("_CoatRoughness", assembled.RasterShaderSource);
            StringAssert.Contains("_CoatIor", assembled.RasterShaderSource);
            StringAssert.Contains("_CoatDarkening", assembled.RasterShaderSource);
            StringAssert.Contains("v2.hasIndependentCoatNormal", assembled.RasterShaderSource);
            StringAssert.Contains("v2.coatNormalWS", assembled.RasterShaderSource);
            StringAssert.Contains("v2.hasIndependentCoatNormal", assembled.ComputeResolveSource);
            StringAssert.Contains("v2.coatNormalWS", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch (", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch(", assembled.ComputeResolveSource);
        }

        [Test]
        public void Assemble_FabricSlab_SupportsCottonWoolSilkThreadMapDoubleSidedAndZeroSwitchCompute()
        {
            string fabricPath = VividPackagePathUtility.GetPreferredAssetPath(FabricSlabRelativePath);
            string templatePath = VividPackagePathUtility.GetPreferredAssetPath(BaseTemplateRelativePath);

            VividSlabShaderAssemblyResult assembled = VividSlabShaderAssembler.Assemble(
                fabricPath,
                File.ReadAllText(fabricPath),
                templatePath,
                File.ReadAllText(templatePath),
                resolvedLinkedGraphPath: string.Empty,
                compiledGraphProgram: null);

            Assert.That(assembled.Succeeded, Is.True);
            Assert.That(assembled.ShaderName, Is.EqualTo("VividRP/Material/FabricSlab"));
            Assert.That(assembled.HasCustomSurfaceHook, Is.True);
            Assert.That(assembled.HasCustomCoverage, Is.False);
            Assert.That(assembled.IsMultiClosure, Is.False);
            Assert.That(
                assembled.EstimatedSubstrateBytesPerPixel,
                Is.GreaterThanOrEqualTo(
                    VividSubstrateContract.ComputeSingleClosureTotalSlices(
                        VividSubstrateFeatureMask.OpenPBROpaque
                        | VividSubstrateFeatureMask.Fuzz
                        | VividSubstrateFeatureMask.Anisotropy)
                    * VividSubstrateContract.BytesPerSlice));

            StringAssert.Contains("Cull [_Cull]", assembled.RasterShaderSource);
            StringAssert.Contains("FRONT_FACE_SEMANTIC", assembled.RasterShaderSource);
            StringAssert.Contains("[earlydepthstencil]", assembled.RasterShaderSource);
            StringAssert.Contains("_FabricType", assembled.RasterShaderSource);
            StringAssert.Contains("_ThreadMap", assembled.RasterShaderSource);
            StringAssert.Contains("_FuzzMap", assembled.RasterShaderSource);
            StringAssert.Contains("_SheenColor", assembled.RasterShaderSource);
            StringAssert.Contains("_Anisotropy", assembled.RasterShaderSource);
            StringAssert.Contains("CustomEditor \"VividRP.Editor.FabricShaderGUI\"", assembled.RasterShaderSource);
            StringAssert.Contains("VividFabricParamsToOpenPBRV2Inputs(", assembled.RasterShaderSource);
            StringAssert.Contains("VividFabricParamsToOpenPBRV2Inputs(", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch (", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch(", assembled.ComputeResolveSource);
        }

        [Test]
        public void Assemble_SubstrateOpaqueTemplate_WiresFragmentInstancingAndMotionVectorsWithWpoAndCoverage()
        {
            string templatePath = VividPackagePathUtility.GetPreferredAssetPath(BaseTemplateRelativePath);
            const string instancedWpoSource =
                "// @vivid-base \"Packages/com.vivid.render-pipelines/Shaders/Material/Templates/SubstrateOpaque.slab.template\"\n"
                + "Shader \"VividRP/Material/TestInstancedMotion\"\n"
                + "{\n"
                + "    Properties { _FurLength(\"Fur Length\", Float) = 0.05 }\n"
                + "    // @vivid-cbuffer-begin\n"
                + "    float _FurLength;\n"
                + "    // @vivid-cbuffer-end\n"
                + "    // @vivid-instancing-begin\n"
                + "    UNITY_INSTANCING_BUFFER_START(Props)\n"
                + "        UNITY_DEFINE_INSTANCED_PROP(float, _ShellHeight)\n"
                + "    UNITY_INSTANCING_BUFFER_END(Props)\n"
                + "    // @vivid-instancing-end\n"
                + "    // @vivid-vertex-begin\n"
                + "    #define VIVID_SLAB_HAS_VERTEX_OFFSET 1\n"
                + "    float3 VividSlabEvaluateVertexOffset(const VividSlabVertexContext ctx)\n"
                + "    {\n"
                + "        #if defined(VIVID_SLAB_TARGET_RASTER)\n"
                + "            float h = UNITY_ACCESS_INSTANCED_PROP(Props, _ShellHeight);\n"
                + "            return ctx.normalWS * (h * _FurLength);\n"
                + "        #else\n"
                + "            return 0.0f.xxx;\n"
                + "        #endif\n"
                + "    }\n"
                + "    // @vivid-vertex-end\n"
                + "    // @vivid-coverage-begin\n"
                + "    #define VIVID_SLAB_HAS_CUSTOM_COVERAGE 1\n"
                + "    void VividSlabEvaluateCoverage(const VividSlabEvalContext ctx, out float alpha, out float cutoff)\n"
                + "    {\n"
                + "        #if defined(VIVID_SLAB_TARGET_RASTER)\n"
                + "            float h = UNITY_ACCESS_INSTANCED_PROP(Props, _ShellHeight);\n"
                + "            alpha = 1.0f - h;\n"
                + "            cutoff = 0.5f;\n"
                + "        #else\n"
                + "            alpha = 1.0f;\n"
                + "            cutoff = 0.0f;\n"
                + "        #endif\n"
                + "    }\n"
                + "    // @vivid-coverage-end\n"
                + "    VIVID_SLAB_USE_BASE_SUBSHADER\n"
                + "}\n";

            VividSlabShaderAssemblyResult assembled = VividSlabShaderAssembler.Assemble(
                "TestInstancedMotion.slab.shader",
                instancedWpoSource,
                templatePath,
                File.ReadAllText(templatePath),
                resolvedLinkedGraphPath: string.Empty,
                compiledGraphProgram: null);

            Assert.That(assembled.Succeeded, Is.True);
            Assert.That(assembled.HasVertexOffset, Is.True);
            Assert.That(assembled.HasCustomCoverage, Is.True);

            // Fragment instancing + user instancing buffer wired across Raster passes.
            StringAssert.Contains("UNITY_INSTANCING_BUFFER_START(Props)", assembled.RasterShaderSource);
            StringAssert.Contains("UNITY_TRANSFER_INSTANCE_ID(input, o);", assembled.RasterShaderSource);
            StringAssert.Contains("UNITY_SETUP_INSTANCE_ID(input);", assembled.RasterShaderSource);
            StringAssert.DoesNotContain(VividSlabShaderAssembler.UserInstancingToken, assembled.RasterShaderSource);

            // MotionVectors pass present with WPO, previous-WPO hook, coverage clip, and VividRP velocity encoding.
            StringAssert.Contains("Name \"MotionVectors\"", assembled.RasterShaderSource);
            StringAssert.Contains("Tags { \"LightMode\" = \"MotionVectors\" }", assembled.RasterShaderSource);
            StringAssert.Contains("VertSlabMotion", assembled.RasterShaderSource);
            StringAssert.Contains("FragSlabMotion", assembled.RasterShaderSource);
            StringAssert.Contains("VIVID_SLAB_HAS_PREV_VERTEX_OFFSET", assembled.RasterShaderSource);
            StringAssert.Contains("EncodeMotionVectorFromCsPositions(", assembled.RasterShaderSource);
            StringAssert.Contains("EncodeCameraMotionVectorFromCsPositions(", assembled.RasterShaderSource);

            // Standalone GPUDriven Compute must not receive Raster-only UNITY_INSTANCING_BUFFER declarations.
            StringAssert.DoesNotContain("UNITY_INSTANCING_BUFFER_START", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch (", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch(", assembled.ComputeResolveSource);
        }

        [Test]
        public void Assemble_FurSlab_WiresInstancingWpoPrevWpoPreDepthEarlyZAndZeroSwitchCompute()
        {
            string furPath = VividPackagePathUtility.GetPreferredAssetPath(FurSlabRelativePath);
            string templatePath = VividPackagePathUtility.GetPreferredAssetPath(BaseTemplateRelativePath);

            VividSlabShaderAssemblyResult assembled = VividSlabShaderAssembler.Assemble(
                furPath,
                File.ReadAllText(furPath),
                templatePath,
                File.ReadAllText(templatePath),
                resolvedLinkedGraphPath: string.Empty,
                compiledGraphProgram: null);

            Assert.That(assembled.Succeeded, Is.True);
            Assert.That(assembled.ShaderName, Is.EqualTo("VividRP/Material/FurSlab"));
            Assert.That(assembled.HasVertexOffset, Is.True);
            Assert.That(assembled.HasCustomCoverage, Is.True);
            Assert.That(assembled.HasCustomSurfaceHook, Is.True);
            Assert.That(assembled.IsMultiClosure, Is.False);
            Assert.That(
                assembled.EstimatedSubstrateBytesPerPixel,
                Is.EqualTo(
                    VividSubstrateContract.ComputeSingleClosureTotalSlices(
                        VividSubstrateFeatureMask.OpenPBROpaque
                        | VividSubstrateFeatureMask.Fuzz
                        | VividSubstrateFeatureMask.Anisotropy)
                    * VividSubstrateContract.BytesPerSlice));

            // Raster: instanced _ShellHeight, WPO + Prev-WPO MotionVectors, PreDepth Early-Z GBuffer.
            StringAssert.Contains("UNITY_INSTANCING_BUFFER_START(VividFurInstancedProps)", assembled.RasterShaderSource);
            StringAssert.Contains("UNITY_DEFINE_INSTANCED_PROP(float, _ShellHeight)", assembled.RasterShaderSource);
            StringAssert.Contains("VividSlabEvaluateVertexOffset(", assembled.RasterShaderSource);
            StringAssert.Contains("VividSlabEvaluatePreviousVertexOffset(", assembled.RasterShaderSource);
            StringAssert.Contains("VIVID_SLAB_GBUFFER_PREDEPTH_EARLY_Z", assembled.RasterShaderSource);
            StringAssert.Contains("[earlydepthstencil]", assembled.RasterShaderSource);
            StringAssert.Contains("Name \"MotionVectors\"", assembled.RasterShaderSource);
            StringAssert.Contains("CustomEditor \"VividRP.Editor.FurShaderGUI\"", assembled.RasterShaderSource);

            // Compute: compiles cleanly with zero switch and no Raster-only instancing buffer.
            StringAssert.Contains("VividFurParamsToOpenPBRV2Inputs(", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("UNITY_INSTANCING_BUFFER_START", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch (", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch(", assembled.ComputeResolveSource);
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
            // VividGBuffer runs with ZWrite Off + ZTest Equal after VividPreDepth coverage clip and
            // writes _OutSubstrateClosureBuffer UAV, so [earlydepthstencil] must stay enabled.
            StringAssert.Contains("[earlydepthstencil]", assembled.RasterShaderSource);
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

        [Test]
        public void StandardLitSlab_Assemble_PreservesAllPassesPropertiesAndZeroSwitchCompute()
        {
            string slabPath = VividPackagePathUtility.GetPreferredAssetPath(StandardLitSlabRelativePath);
            string templatePath = VividPackagePathUtility.GetPreferredAssetPath(StandardLitTemplateRelativePath);
            string legacyPath = VividPackagePathUtility.GetPreferredAssetPath(StandardLitRelativePath);

            string slabSource = File.ReadAllText(slabPath);
            string templateSource = File.ReadAllText(templatePath);
            string legacySource = File.ReadAllText(legacyPath);

            VividSlabShaderAssemblyResult assembled = VividSlabShaderAssembler.Assemble(
                slabPath,
                slabSource,
                templatePath,
                templateSource,
                resolvedLinkedGraphPath: string.Empty,
                compiledGraphProgram: null);

            Assert.That(assembled.Succeeded, Is.True);
            Assert.That(assembled.ShaderName, Is.EqualTo("VividRP/Material/StandardLitSlab"));
            Assert.That(assembled.HasLinkedGraphSurface, Is.False);
            Assert.That(assembled.IsMultiClosure, Is.False);
            Assert.That(assembled.EstimatedSubstrateBytesPerPixel, Is.EqualTo(32));

            int propsStart = assembled.RasterShaderSource.IndexOf("Properties", StringComparison.Ordinal);
            int subShaderStart = assembled.RasterShaderSource.IndexOf("SubShader", StringComparison.Ordinal);
            StringAssert.DoesNotContain(
                "#line",
                assembled.RasterShaderSource.Substring(propsStart, subShaderStart - propsStart));

            string[] expectedPasses =
            {
                "Name \"VividPreDepth\"",
                "Name \"ShadowCaster\"",
                "Name \"VividGBuffer\"",
                "Name \"VividGBufferGPUDrivenDecal\"",
                "Name \"Meta\"",
                "Name \"MotionVectors\"",
                "Name \"IndirectDXR\"",
                "Name \"ReferencedPathtracingDXR\"",
                "Name \"RaytracingGBufferDXR\"",
                "Name \"VividVisibilityRaster\"",
            };
            for (int i = 0; i < expectedPasses.Length; i++)
                StringAssert.Contains(expectedPasses[i], assembled.RasterShaderSource);

            StringAssert.Contains("CustomEditor \"VividRP.Editor.StandardLitShaderGUI\"", assembled.RasterShaderSource);
            StringAssert.Contains("VividSlabEvaluateStandardLitFromMaterialData(", assembled.ComputeResolveSource);
            StringAssert.Contains("VIVID_SLAB_NO_AOT_SURFACE_DISPATCH", assembled.ComputeResolveSource);
            StringAssert.Contains("VividWriteSubstrateOpenPBRV2Single(", assembled.ComputeResolveSource);
            StringAssert.Contains("VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch(", assembled.ComputeResolveSource);
            StringAssert.DoesNotContain("switch (", assembled.ComputeResolveSource);

            // Verify 100% property parity with StandardLit.shader.
            var propRegex = new System.Text.RegularExpressions.Regex(@"(_[A-Za-z0-9_]+)\s*\(");
            int legacyPropsEnd = legacySource.IndexOf("SubShader", StringComparison.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in propRegex.Matches(legacySource.Substring(0, legacyPropsEnd)))
            {
                string propName = m.Groups[1].Value;
                StringAssert.Contains(
                    propName + "(",
                    slabSource,
                    $"StandardLitSlab.slab.shader is missing StandardLit property '{propName}'.");
            }

            // Verify custom @vivid-surface-begin..end post-hook injects into BOTH Raster and Compute.
            string withHook = slabSource.Replace(
                "VIVID_SLAB_USE_BASE_SUBSHADER",
                "// @vivid-surface-begin\n"
                + "#define VIVID_SLAB_HAS_CUSTOM_POST_SURFACE 1\n"
                + "void VividSlabPostEvaluateSurface(const VividSlabEvalContext ctx, inout VividOpenPBRV2Inputs v2, inout float ambientOcclusion) { v2.coatWeight = max(v2.coatWeight, 0.25f); }\n"
                + "// @vivid-surface-end\n\n    VIVID_SLAB_USE_BASE_SUBSHADER");
            VividSlabShaderAssemblyResult hooked = VividSlabShaderAssembler.Assemble(
                slabPath,
                withHook,
                templatePath,
                templateSource,
                resolvedLinkedGraphPath: string.Empty,
                compiledGraphProgram: null);
            Assert.That(hooked.Succeeded, Is.True);
            Assert.That(hooked.HasCustomSurfaceHook, Is.True);
            StringAssert.Contains("VividSlabPostEvaluateSurface(", hooked.RasterShaderSource);
            StringAssert.Contains("VividSlabPostEvaluateSurface(", hooked.ComputeResolveSource);
        }

        [Test]
        public void SceneData_ActiveMaterialBins_TracksDirectBitmaskOverflowTerrainAndZeroAllocation()
        {
            VividSlabProgramDescriptor descriptorA = ScriptableObject.CreateInstance<VividSlabProgramDescriptor>();
            VividSlabProgramDescriptor descriptorB = ScriptableObject.CreateInstance<VividSlabProgramDescriptor>();
            try
            {
                descriptorA.Apply(
                    rasterShader: null,
                    resolveComputeShader: null,
                    resolveKernelName: VividSlabProgramDescriptor.DefaultResolveKernelName,
                    shaderName: "VividRP/Material/TestActiveBinA",
                    baseTemplateAssetPath: string.Empty,
                    linkedGraphAssetPath: string.Empty,
                    estimatedSubstrateBytesPerPixel: 32,
                    parameterStrideInWords: 0,
                    parameters: Array.Empty<VividSlabParameterSlot>(),
                    resources: Array.Empty<VividSlabResourceSlot>(),
                    hasLinkedGraphSurface: false,
                    hasCustomSurfaceHook: false,
                    sourceContentHash: 0x11112222u,
                    hasImportErrors: false,
                    diagnostics: Array.Empty<string>());
                descriptorB.Apply(
                    rasterShader: null,
                    resolveComputeShader: null,
                    resolveKernelName: VividSlabProgramDescriptor.DefaultResolveKernelName,
                    shaderName: "VividRP/Material/TestActiveBinB",
                    baseTemplateAssetPath: string.Empty,
                    linkedGraphAssetPath: string.Empty,
                    estimatedSubstrateBytesPerPixel: 32,
                    parameterStrideInWords: 0,
                    parameters: Array.Empty<VividSlabParameterSlot>(),
                    resources: Array.Empty<VividSlabResourceSlot>(),
                    hasLinkedGraphSurface: false,
                    hasCustomSurfaceHook: false,
                    sourceContentHash: 0x33334444u,
                    hasImportErrors: false,
                    diagnostics: Array.Empty<string>());

                var scene = new VividGPUDrivenSceneData();
                Assert.That(scene.ActiveDirectProgramBinMask, Is.EqualTo(0ul));
                Assert.That(scene.HasActiveOverflowProgramBin, Is.False);
                Assert.That(scene.ActiveBinnedDispatchCount, Is.EqualTo(1));

                VividMaterialProgramID programA = scene.RegisterDynamicSlabProgram(descriptorA);
                VividMaterialProgramID programB = scene.RegisterDynamicSlabProgram(descriptorB);
                ulong expectedBitA = VividMaterialBinningContract.ComputeDirectProgramBinBit((int)(uint)programA);
                ulong expectedBitB = VividMaterialBinningContract.ComputeDirectProgramBinBit((int)(uint)programB);
                Assert.That(expectedBitA, Is.Not.EqualTo(0ul));
                Assert.That(expectedBitB, Is.Not.EqualTo(0ul));
                Assert.That(expectedBitA, Is.Not.EqualTo(expectedBitB));

                // Registering programs without referencing materials keeps them inactive.
                Assert.That(scene.ActiveDirectProgramBinMask, Is.EqualTo(0ul));
                Assert.That(scene.HasActiveOverflowProgramBin, Is.False);
                Assert.That(scene.ActiveBinnedDispatchCount, Is.EqualTo(1));

                scene.AddMaterial(
                    default(VividMaterialData),
                    new VividMaterialRuntimeHeader
                    {
                        ProgramID = programA,
                        ParameterAddress = 0u,
                        ResourceBindingAddress = 0u,
                    });
                Assert.That(scene.ActiveDirectProgramBinMask, Is.EqualTo(expectedBitA));
                Assert.That(scene.HasActiveOverflowProgramBin, Is.False);
                Assert.That(scene.ActiveBinnedDispatchCount, Is.EqualTo(2));

                scene.AddMaterial(
                    default(VividMaterialData),
                    new VividMaterialRuntimeHeader
                    {
                        ProgramID = programB,
                        ParameterAddress = 0u,
                        ResourceBindingAddress = 0u,
                    });
                Assert.That(scene.ActiveDirectProgramBinMask, Is.EqualTo(expectedBitA | expectedBitB));
                Assert.That(scene.HasActiveOverflowProgramBin, Is.False);
                Assert.That(scene.ActiveBinnedDispatchCount, Is.EqualTo(3));

                // Legacy fallback (ProgramID == Invalid) activates the overflow bin.
                scene.AddLegacyMaterial(default(VividMaterialData));
                Assert.That(scene.ActiveDirectProgramBinMask, Is.EqualTo(expectedBitA | expectedBitB));
                Assert.That(scene.HasActiveOverflowProgramBin, Is.True);
                Assert.That(scene.ActiveBinnedDispatchCount, Is.EqualTo(4));

                // Warm up and verify stable active-bin queries and tzcnt iteration allocate zero managed memory.
                int sink = IterateActiveBins(scene, 64);

                long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
                sink += IterateActiveBins(scene, 1024);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
                Assert.That(sink, Is.GreaterThan(0));
                Assert.That(allocated, Is.Zero, "Active bin bitmask queries and tzcnt iteration must allocate zero bytes.");

                // ClearMaterials resets material bins cleanly.
                scene.ClearMaterials();
                Assert.That(scene.ActiveDirectProgramBinMask, Is.EqualTo(0ul));
                Assert.That(scene.HasActiveOverflowProgramBin, Is.False);
                Assert.That(scene.ActiveBinnedDispatchCount, Is.EqualTo(1));

                // StandardSingleSlab (ProgramID == 0) maps to OverflowBinIndex in both C# and HLSL.
                Assert.That(
                    VividMaterialBinningContract.MapProgramIdToBinIndex((uint)VividMaterialProgramID.StandardSingleSlab, true),
                    Is.EqualTo(VividMaterialBinningContract.OverflowBinIndex));
                scene.MutableMaterialRuntimeHeaders.Add(new VividMaterialRuntimeHeader
                {
                    ProgramID = VividMaterialProgramID.StandardSingleSlab,
                });
                Assert.That(scene.ActiveDirectProgramBinMask, Is.EqualTo(0ul));
                Assert.That(scene.HasActiveOverflowProgramBin, Is.True);
                Assert.That(scene.ActiveBinnedDispatchCount, Is.EqualTo(2));

                // Terrain materials and out-of-range instance MaterialIndex also keep OverflowBin active.
                scene.ClearMaterials();
                scene.MutableTerrainMaterials.Add(default(VividTerrainMaterialData));
                Assert.That(scene.HasActiveOverflowProgramBin, Is.True);
                scene.ClearTerrainMaterials();
                Assert.That(scene.HasActiveOverflowProgramBin, Is.False);

                scene.AddInstance(
                    new VividInstanceData
                    {
                        MaterialIndex = 99u,
                        Flags = VividInstanceFlags.None,
                    },
                    1);
                Assert.That(scene.HasActiveOverflowProgramBin, Is.True);
                scene.ClearInstances();
                Assert.That(scene.HasActiveOverflowProgramBin, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(descriptorA);
                UnityEngine.Object.DestroyImmediate(descriptorB);
            }
        }

        private static int IterateActiveBins(VividGPUDrivenSceneData scene, int iterations)
        {
            int sink = 0;
            for (int i = 0; i < iterations; i++)
            {
                ulong mask = scene.ActiveDirectProgramBinMask;
                while (mask != 0ul)
                {
                    sink += VividMaterialBinningContract.FirstDirectProgramBinIndex + math.tzcnt(mask);
                    mask &= mask - 1ul;
                }

                if (scene.HasActiveOverflowProgramBin)
                    sink += VividMaterialBinningContract.OverflowBinIndex;
                sink += scene.ActiveBinnedDispatchCount;
            }

            return sink;
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

        [Test]
        public void Assemble_HandwrittenFabricAndClearCoatSlabs_ReflectParametersResourcesAndLinearColors()
        {
            string templatePath = VividPackagePathUtility.GetPreferredAssetPath(BaseTemplateRelativePath);
            string templateText = File.ReadAllText(templatePath);

            // 1. Standalone handwritten FabricSlab (no linked MaterialGraph): reflects CBuffer + 6 Textures.
            string fabricPath = VividPackagePathUtility.GetPreferredAssetPath(FabricSlabRelativePath);
            string fabricSource = File.ReadAllText(fabricPath);
            VividSlabShaderAssemblyResult fabricResult = VividSlabShaderAssembler.Assemble(
                fabricPath,
                fabricSource,
                templatePath,
                templateText,
                resolvedLinkedGraphPath: string.Empty,
                compiledGraphProgram: null);

            Assert.That(fabricResult.Succeeded, Is.True);
            Assert.That(fabricResult.ParameterSlots.Length, Is.GreaterThanOrEqualTo(25));
            Assert.That(fabricResult.ParameterStrideInWords % 4, Is.Zero);
            Assert.That(fabricResult.ResourceSlots.Length, Is.EqualTo(6));

            VividSlabParameterSlot baseColorSlot = FindParamSlot(fabricResult.ParameterSlots, "_BaseColor");
            VividSlabParameterSlot sheenColorSlot = FindParamSlot(fabricResult.ParameterSlots, "_SheenColor");
            VividSlabParameterSlot emissionSlot = FindParamSlot(fabricResult.ParameterSlots, "_EmissionColor");
            VividSlabParameterSlot fabricTypeSlot = FindParamSlot(fabricResult.ParameterSlots, "_FabricType");
            Assert.That(baseColorSlot.IsColor, Is.True, "Standard Color property _BaseColor must convert sRGB->Linear.");
            Assert.That(sheenColorSlot.IsColor, Is.True, "Standard Color property _SheenColor must convert sRGB->Linear.");
            Assert.That(emissionSlot.IsColor, Is.False, "[HDR] Color property _EmissionColor must stay linear without double conversion.");
            Assert.That(fabricTypeSlot.Kind, Is.EqualTo(VividSlabParameterKind.Float));

            VividSlabResourceSlot baseMapSlot = FindResourceSlot(fabricResult.ResourceSlots, "_BaseMap");
            VividSlabResourceSlot bumpMapSlot = FindResourceSlot(fabricResult.ResourceSlots, "_BumpMap");
            VividSlabResourceSlot emissionMapSlot = FindResourceSlot(fabricResult.ResourceSlots, "_EmissionMap");
            VividSlabResourceSlot threadMapSlot = FindResourceSlot(fabricResult.ResourceSlots, "_ThreadMap");
            VividSlabResourceSlot fuzzMapSlot = FindResourceSlot(fabricResult.ResourceSlots, "_FuzzMap");
            Assert.That(baseMapSlot.DefaultTexture, Is.EqualTo(VividSlabDefaultTextureKind.White));
            Assert.That(bumpMapSlot.SampleKind, Is.EqualTo(VividSlabTextureSampleKind.Normal));
            Assert.That(bumpMapSlot.DefaultTexture, Is.EqualTo(VividSlabDefaultTextureKind.Bump));
            Assert.That(emissionMapSlot.DefaultTexture, Is.EqualTo(VividSlabDefaultTextureKind.Black));
            Assert.That(threadMapSlot.SampleKind, Is.EqualTo(VividSlabTextureSampleKind.Mask));
            Assert.That(fuzzMapSlot.DefaultTexture, Is.EqualTo(VividSlabDefaultTextureKind.Gray));

            StringAssert.Contains("VividLoadSlabParam__SheenColor(", fabricResult.ComputeResolveSource);
            StringAssert.Contains("VividSampleSlabTex__BaseMap(", fabricResult.ComputeResolveSource);
            StringAssert.Contains("return float4(1.0f, 0.5f, 1.0f, 0.5f);", fabricResult.ComputeResolveSource);
            StringAssert.DoesNotContain("_OutGBuffer4", fabricResult.ComputeResolveSource);
            StringAssert.Contains("#define VIVID_SLAB_PARAM(", fabricResult.RasterShaderSource);

            // 2. Hybrid Graph + handwritten CBuffer ClearCoatDualNormal: merges without duplicate PropertyName.
            string ccPath = VividPackagePathUtility.GetPreferredAssetPath(ClearCoatDualNormalSampleRelativePath);
            string ccSource = File.ReadAllText(ccPath);
            CompiledMaterialProgram openPbrProgram = BuildOpenPBROpaqueTestProgram();
            VividSlabShaderAssemblyResult ccResult = VividSlabShaderAssembler.Assemble(
                ccPath,
                ccSource,
                templatePath,
                templateText,
                resolvedLinkedGraphPath: "OpenPBROpaque.vmatg",
                compiledGraphProgram: openPbrProgram);

            Assert.That(ccResult.Succeeded, Is.True);
            var seenProps = new HashSet<string>(StringComparer.Ordinal);
            foreach (VividSlabParameterSlot s in ccResult.ParameterSlots)
                Assert.That(seenProps.Add(s.PropertyName), Is.True, $"Duplicate parameter slot '{s.PropertyName}'.");
            Assert.That(FindParamSlot(ccResult.ParameterSlots, "_CoatWeight").DefaultValue.x, Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(FindParamSlot(ccResult.ParameterSlots, "_CoatIor").DefaultValue.x, Is.EqualTo(1.65f).Within(1e-5f));
            StringAssert.Contains("VividLoadSlabParam__CoatWeight(", ccResult.ComputeResolveSource);

            // 3. Linear color packing & zero-allocation PackParameterLanes check.
            var descriptor = ScriptableObject.CreateInstance<VividSlabProgramDescriptor>();
            try
            {
                descriptor.Apply(
                    rasterShader: null,
                    resolveComputeShader: null,
                    resolveKernelName: VividSlabProgramDescriptor.DefaultResolveKernelName,
                    shaderName: fabricResult.ShaderName,
                    baseTemplateAssetPath: templatePath,
                    linkedGraphAssetPath: string.Empty,
                    estimatedSubstrateBytesPerPixel: fabricResult.EstimatedSubstrateBytesPerPixel,
                    parameterStrideInWords: fabricResult.ParameterStrideInWords,
                    parameters: fabricResult.ParameterSlots,
                    resources: fabricResult.ResourceSlots,
                    hasLinkedGraphSurface: false,
                    hasCustomSurfaceHook: true,
                    sourceContentHash: fabricResult.SourceContentHash,
                    hasImportErrors: false,
                    diagnostics: Array.Empty<string>());

                var lanes = new List<uint4>(16);
                for (int w = 0; w < 8; w++)
                {
                    lanes.Clear();
                    descriptor.PackParameterLanes(null, null, lanes);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 64; i++)
                {
                    lanes.Clear();
                    descriptor.PackParameterLanes(null, null, lanes);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero, "Reflected handwritten Slab parameter packing must allocate 0 managed bytes.");

                int baseWord = baseColorSlot.WordOffset;
                float packedR = math.asfloat(lanes[baseWord / 4][baseWord % 4]);
                float expectedR = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? new Color(baseColorSlot.DefaultValue.x, baseColorSlot.DefaultValue.y, baseColorSlot.DefaultValue.z, baseColorSlot.DefaultValue.w).linear.r
                    : baseColorSlot.DefaultValue.x;
                Assert.That(packedR, Is.EqualTo(expectedR).Within(1e-4f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(descriptor);
            }
        }

        [Test]
        public void Assemble_WpoPaddingDirectives_ParseConstantAndPropertyExpressionsWithZeroAllocResolution()
        {
            string templatePath = VividPackagePathUtility.GetPreferredAssetPath(BaseTemplateRelativePath);
            string templateText = File.ReadAllText(templatePath);

            // 1. Constant @vivid-wpo-padding directive on SubstrateMultiClosurePoC.slab.shader
            string multiPath = VividPackagePathUtility.GetPreferredAssetPath(
                "Shaders/Material/Samples/SubstrateMultiClosurePoC.slab.shader");
            VividSlabShaderAssemblyResult multiResult = VividSlabShaderAssembler.Assemble(
                multiPath,
                File.ReadAllText(multiPath),
                templatePath,
                templateText,
                resolvedLinkedGraphPath: string.Empty,
                compiledGraphProgram: null);
            Assert.That(multiResult.Succeeded, Is.True);
            Assert.That(multiResult.HasVertexOffset, Is.True);
            Assert.That(multiResult.WorldPositionOffsetPadding, Is.EqualTo(0.025f).Within(1e-5f));
            Assert.That(multiResult.WorldPositionOffsetPaddingProperties, Is.Empty);

            // 2. Mixed property + constant @vivid-wpo-padding directive on FurSlab.slab.shader
            string furPath = VividPackagePathUtility.GetPreferredAssetPath(FurSlabRelativePath);
            VividSlabShaderAssemblyResult furResult = VividSlabShaderAssembler.Assemble(
                furPath,
                File.ReadAllText(furPath),
                templatePath,
                templateText,
                resolvedLinkedGraphPath: string.Empty,
                compiledGraphProgram: null);
            Assert.That(furResult.Succeeded, Is.True);
            Assert.That(furResult.HasVertexOffset, Is.True);
            Assert.That(furResult.WorldPositionOffsetPadding, Is.EqualTo(0.02f).Within(1e-5f));
            Assert.That(furResult.WorldPositionOffsetPaddingProperties, Is.EqualTo(new[]
            {
                "_FurLength",
                "_GravityStrength",
                "_WindStrength",
            }));

            var descriptor = ScriptableObject.CreateInstance<VividSlabProgramDescriptor>();
            try
            {
                descriptor.Apply(
                    rasterShader: null,
                    resolveComputeShader: null,
                    resolveKernelName: VividSlabProgramDescriptor.DefaultResolveKernelName,
                    shaderName: furResult.ShaderName,
                    baseTemplateAssetPath: templatePath,
                    linkedGraphAssetPath: string.Empty,
                    estimatedSubstrateBytesPerPixel: furResult.EstimatedSubstrateBytesPerPixel,
                    parameterStrideInWords: furResult.ParameterStrideInWords,
                    parameters: furResult.ParameterSlots,
                    resources: furResult.ResourceSlots,
                    hasLinkedGraphSurface: false,
                    hasCustomSurfaceHook: true,
                    sourceContentHash: furResult.SourceContentHash,
                    hasImportErrors: false,
                    diagnostics: Array.Empty<string>(),
                    hasCustomCoverage: furResult.HasCustomCoverage,
                    hasVertexOffset: furResult.HasVertexOffset,
                    worldPositionOffsetPadding: furResult.WorldPositionOffsetPadding,
                    worldPositionOffsetPaddingProperties: furResult.WorldPositionOffsetPaddingProperties);

                float expectedDefaultPad = 0.02f
                    + Mathf.Abs(FindParamSlot(furResult.ParameterSlots, "_FurLength").DefaultValue.x)
                    + Mathf.Abs(FindParamSlot(furResult.ParameterSlots, "_GravityStrength").DefaultValue.x)
                    + Mathf.Abs(FindParamSlot(furResult.ParameterSlots, "_WindStrength").DefaultValue.x);

                // Warm up property ID cache before measuring managed allocations.
                float resolvedPad = 0.0f;
                for (int w = 0; w < 8; w++)
                    resolvedPad = descriptor.ResolveWorldPositionOffsetPadding(null);
                Assert.That(resolvedPad, Is.EqualTo(expectedDefaultPad).Within(1e-5f));

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 64; i++)
                    resolvedPad = descriptor.ResolveWorldPositionOffsetPadding(null);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero, "ResolveWorldPositionOffsetPadding must allocate 0 managed bytes.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(descriptor);
            }
        }

        [Test]
        public void Assemble_ShellLayersDirectiveAndFurSlab_WiresDynamicGPUDrivenShellAmplification()
        {
            string templatePath = VividPackagePathUtility.GetPreferredAssetPath(BaseTemplateRelativePath);
            string templateText = File.ReadAllText(templatePath);

            // 1. FurSlab declares @vivid-shell-layers 8 and wires both VisibilityRaster and Compute Resolve shells.
            string furPath = VividPackagePathUtility.GetPreferredAssetPath(FurSlabRelativePath);
            VividSlabShaderAssemblyResult furResult = VividSlabShaderAssembler.Assemble(
                furPath,
                File.ReadAllText(furPath),
                templatePath,
                templateText,
                resolvedLinkedGraphPath: string.Empty,
                compiledGraphProgram: null);

            Assert.That(furResult.Succeeded, Is.True);
            Assert.That(furResult.ShellLayerCount, Is.EqualTo(8));
            Assert.That(furResult.HasVertexOffset, Is.True);
            Assert.That(furResult.HasCustomCoverage, Is.True);

            // Reflected GPUDriven shell controls
            VividSlabParameterSlot shellCountSlot = FindParamSlot(furResult.ParameterSlots, "_GPUDrivenShellCount");
            VividSlabParameterSlot shellPowerSlot = FindParamSlot(furResult.ParameterSlots, "_ShellHeightPower");
            Assert.That(shellCountSlot.DefaultValue.x, Is.EqualTo(8.0f).Within(1e-5f));
            Assert.That(shellPowerSlot.DefaultValue.x, Is.EqualTo(1.0f).Within(1e-5f));

            // VisibilityRaster encodes ShellIndex and evaluates custom shell params + hybrid coverage.
            Assert.That(furResult.RasterShaderSource, Does.Contain("float4 _VividSlabShellParams;"));
            Assert.That(furResult.RasterShaderSource, Does.Contain("PackVisibilityBufferValueWithShell(visibilityBufferValue, shellIndex)"));
            Assert.That(furResult.RasterShaderSource, Does.Contain("VividSlabResolveShellLayer(vctx, shellHeight, shellStep)"));
            Assert.That(furResult.RasterShaderSource, Does.Contain("VividEvaluateFurHybridSlabWithDerivatives("));

            // Standalone Compute Resolve unpacks ShellIndex, replays shell WPO, computes analytic world-pos derivatives,
            // and runs Hybrid Fur raymarching without ddx/ddy or >8 UAVs.
            Assert.That(furResult.ComputeResolveSource, Does.Contain("#define VIVID_SLAB_MAX_SHELL_LAYERS 8u"));
            Assert.That(furResult.ComputeResolveSource, Does.Contain("UnpackVisibilityBufferShellIndex(packedVis)"));
            Assert.That(furResult.ComputeResolveSource, Does.Contain("VividSlabResolveShellLayer(shellCtx, shellHeight, shellStep)"));
            Assert.That(furResult.ComputeResolveSource, Does.Contain("InterpolateWithBarycentric(bary, posWS0, posWS1, posWS2)"));
            Assert.That(furResult.ComputeResolveSource, Does.Contain("evalCtx.positionWSDdx = posWSInterp.ddx;"));
            Assert.That(furResult.ComputeResolveSource, Does.Contain("evalCtx.positionWSDdy = posWSInterp.ddy;"));
            Assert.That(furResult.ComputeResolveSource, Does.Contain("evalCtx.shellHeight = shellHeight;"));
            Assert.That(furResult.ComputeResolveSource, Does.Contain("evalCtx.shellStep = shellStep;"));
            Assert.That(furResult.ComputeResolveSource, Does.Not.Contain("_OutGBuffer4"));
            Assert.That(furResult.ComputeResolveSource, Does.Not.Contain("GPUDriven single-shell skin fallback"));

            // 2. Non-shell shaders default to ShellLayerCount == 1 and clamp out-of-range directives to [1, 16].
            string fabricPath = VividPackagePathUtility.GetPreferredAssetPath(FabricSlabRelativePath);
            VividSlabShaderAssemblyResult fabricResult = VividSlabShaderAssembler.Assemble(
                fabricPath,
                File.ReadAllText(fabricPath),
                templatePath,
                templateText,
                resolvedLinkedGraphPath: string.Empty,
                compiledGraphProgram: null);
            Assert.That(fabricResult.Succeeded, Is.True);
            Assert.That(fabricResult.ShellLayerCount, Is.EqualTo(1));
            Assert.That(fabricResult.ComputeResolveSource, Does.Contain("#define VIVID_SLAB_MAX_SHELL_LAYERS 1u"));

            VividSlabShaderDirectives clampedDirectives = VividSlabShaderAssembler.ParseDirectives(
                "// @vivid-shell-layers 64\nShader \"Test/ClampedShells\" {}");
            Assert.That(clampedDirectives.ShellLayerCount, Is.EqualTo(VividSlabProgramDescriptor.MaxSupportedShellLayerCount));

            var descriptor = ScriptableObject.CreateInstance<VividSlabProgramDescriptor>();
            try
            {
                descriptor.Apply(
                    rasterShader: null,
                    resolveComputeShader: null,
                    resolveKernelName: VividSlabProgramDescriptor.DefaultResolveKernelName,
                    shaderName: furResult.ShaderName,
                    baseTemplateAssetPath: templatePath,
                    linkedGraphAssetPath: string.Empty,
                    estimatedSubstrateBytesPerPixel: furResult.EstimatedSubstrateBytesPerPixel,
                    parameterStrideInWords: furResult.ParameterStrideInWords,
                    parameters: furResult.ParameterSlots,
                    resources: furResult.ResourceSlots,
                    hasLinkedGraphSurface: false,
                    hasCustomSurfaceHook: true,
                    sourceContentHash: furResult.SourceContentHash,
                    hasImportErrors: false,
                    diagnostics: Array.Empty<string>(),
                    hasCustomCoverage: false,
                    hasVertexOffset: false,
                    worldPositionOffsetPadding: 0.0f,
                    worldPositionOffsetPaddingProperties: null,
                    shellLayerCount: furResult.ShellLayerCount);

                Assert.That(descriptor.ShellLayerCount, Is.EqualTo(8));
                Assert.That(
                    descriptor.RequiresProgrammableVisibilityRaster,
                    Is.True,
                    "Multi-shell descriptors must route through programmable VisibilityBuffer rasterization.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(descriptor);
            }
        }

        private static VividSlabParameterSlot FindParamSlot(VividSlabParameterSlot[] slots, string propName)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (string.Equals(slots[i].PropertyName, propName, StringComparison.Ordinal))
                    return slots[i];
            }
            Assert.Fail($"Expected parameter slot '{propName}' was not reflected.");
            return default;
        }

        private static VividSlabResourceSlot FindResourceSlot(VividSlabResourceSlot[] slots, string propName)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (string.Equals(slots[i].PropertyName, propName, StringComparison.Ordinal))
                    return slots[i];
            }
            Assert.Fail($"Expected resource slot '{propName}' was not reflected.");
            return default;
        }
    }
}
