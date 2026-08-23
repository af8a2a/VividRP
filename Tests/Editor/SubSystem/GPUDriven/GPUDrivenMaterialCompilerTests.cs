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
            Assert.That(
                generatedContract,
                Does.Contain("#define VIVIDMATERIALPROGRAMID_STANDARD_SINGLE_SLAB (0)"));
            Assert.That(runtimeContract, Does.Contain("struct VividMaterialRuntimeHeader"));
            Assert.That(runtimeContract, Does.Contain("struct VividMaterialProgramData"));
            Assert.That(
                runtimeContract,
                Does.Contain(
                    $"#define VIVID_MATERIAL_PROGRAM_VERSION {GPUDrivenMaterialCompiler.ProgramVersion}u"));
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
                Does.Contain("StructuredBuffer<VividMaterialRuntimeHeader> _MaterialRuntimeHeaders;"));
            Assert.That(
                runtimeContract,
                Does.Contain("StructuredBuffer<VividMaterialProgramData> _MaterialPrograms;"));
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
            }
            finally
            {
                Object.DestroyImmediate(proxy);
            }
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
                Assert.That(sceneData.MaterialProgramCount, Is.EqualTo(1));
                VividMaterialProgramData program = sceneData.MaterialPrograms[0];
                Assert.That(program.Version, Is.EqualTo(GPUDrivenMaterialCompiler.ProgramVersion));
                Assert.That(
                    program.CoverageProgramID,
                    Is.EqualTo(VividMaterialCoverageProgramID.BaseColorAlpha));
                Assert.That(program.SurfaceProgramID, Is.EqualTo(VividMaterialSurfaceProgramID.StandardSingleSlab));
                Assert.That(program.ParameterLayoutID, Is.EqualTo(VividMaterialParameterLayoutID.LegacyMaterialData));
                Assert.That(program.ResourceLayoutID, Is.EqualTo(VividMaterialResourceLayoutID.LegacySurfaceBinding));
                Assert.That(
                    program.CapabilityFlags & VividMaterialProgramCapabilities.AlphaClip,
                    Is.EqualTo(VividMaterialProgramCapabilities.AlphaClip));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
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
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(proxy, 0u, 3u);
                GPUDrivenCompiledMaterialInstance second =
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(proxy, 1u, 5u);

                Assert.That(
                    sceneData.AddMaterial(first.LegacyMaterialData, first.RuntimeHeader),
                    Is.Zero);
                Assert.That(
                    sceneData.AddMaterial(second.LegacyMaterialData, second.RuntimeHeader),
                    Is.EqualTo(1));
                Assert.That(sceneData.MaterialCount, Is.EqualTo(2));
                Assert.That(sceneData.MaterialRuntimeHeaderCount, Is.EqualTo(2));
                Assert.That(sceneData.MaterialRuntimeHeaders[0].ParameterAddress, Is.Zero);
                Assert.That(sceneData.MaterialRuntimeHeaders[1].ParameterAddress, Is.EqualTo(1u));

                VividMaterialRuntimeHeader mismatchedHeader = second.RuntimeHeader;
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
    }
}
