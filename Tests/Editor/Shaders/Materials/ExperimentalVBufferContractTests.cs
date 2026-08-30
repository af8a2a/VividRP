using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine.Experimental.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ExperimentalVBufferContractTests
    {
        private const string ResolveAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosureBufferResolve.shader";
        private const string SurfaceProgramAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialSurface.hlsl";
        private const string SurfaceAotAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialSurfaceAOT.generated.hlsl";

        [Test]
        public void ClosureBackend_UsesSharedVisibilityBufferAbi()
        {
            var pass = new VisibilityBufferPass();
            var resources = ((IRenderPass)pass).Initialize();
            Assert.That(
                resources.Textures.Single(entry => entry.Name == "VisibilityBuffer")
                    .Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32_UInt));
            Assert.That(
                resources.Textures.Single(entry => entry.Name == "VisibilityBufferAttributes0")
                    .Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(
                resources.Textures.Single(entry => entry.Name == "VisibilityBufferAttributes1")
                    .Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(
                resources.Textures.Single(entry => entry.Name == "VisibilityBufferBarycentrics")
                    .Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16_SFloat));
        }

        [Test]
        public void ClosureResolve_ConsumesGeneratedSurfaceProgramAndFailsKnownMissClosed()
        {
            string source = File.ReadAllText(ResolveAssetPath);
            string surfaceProgramSource = File.ReadAllText(SurfaceProgramAssetPath);
            string surfaceAotSource = File.ReadAllText(SurfaceAotAssetPath);
            string compactSource = string.Concat(
                source.Where(character => !char.IsWhiteSpace(character)));

            StringAssert.Contains("UnpackVisibilityBufferValue", source);
            StringAssert.Contains("PullInstanceData(visibility.InstanceID)", source);
            StringAssert.Contains("VividGetMaterialProgramStatus", source);
            StringAssert.Contains("VIVID_MATERIAL_PROGRAM_KNOWN", source);
            StringAssert.Contains("VIVID_MATERIAL_PROGRAM_KNOWN_FAILURE", source);
            StringAssert.Contains("VIVID_MATERIAL_PROGRAM_LEGACY_FALLBACK", source);
            StringAssert.DoesNotContain("VividTryLoadStandardSingleSlabSurfaceProgram", source);
            StringAssert.DoesNotContain("VividTryLoadDualSlabSurfaceProgram", source);
            StringAssert.Contains("VividMaterialSurfaceAOT.generated.hlsl", surfaceProgramSource);
            StringAssert.Contains("VividTryEvaluateAOTSurfaceProgram", source);
            StringAssert.Contains("VividLoadMaterialFloat", surfaceAotSource);
            StringAssert.Contains("PullMaterialResourceData", surfaceAotSource);
            StringAssert.Contains("VividAOTSurfaceContext", source);
            StringAssert.Contains("aotSurfaceOutput.BaseSlab", source);
            StringAssert.Contains("aotSurfaceOutput.TopSlab", source);
            StringAssert.Contains("aotSurfaceOutput.Emission", source);
            StringAssert.Contains("aotSurfaceOutput.ClosureCount", source);
            StringAssert.Contains("aotSurfaceOutput.LayerOperator", source);
            StringAssert.Contains("aotSurfaceOutput.LayerWeight", source);
            StringAssert.Contains("evaluatedAOTSingleSurface", source);
            StringAssert.Contains("evaluatedAOTDualSurface", source);
            StringAssert.Contains("failedAOTSurface", source);
            StringAssert.Contains("float3(1.0f, 0.0f, 1.0f)", source);
            StringAssert.Contains("VividMaterialData materialData", source);
            StringAssert.Contains("VividSurfaceBindingData surfaceBindingData", source);
            StringAssert.Contains("VividEvaluateSlabSurfaceGrad", source);
            StringAssert.Contains(
                "VividCreateSurfaceSampleContextGrad",
                surfaceProgramSource);
            StringAssert.Contains("VividSampleBaseColorGrad", surfaceProgramSource);
            StringAssert.Contains("VividSampleNormalGrad", surfaceProgramSource);
            StringAssert.Contains("VividSampleMaskGrad", surfaceProgramSource);
            StringAssert.Contains("ComputeWorldSpacePosition", source);
            StringAssert.Contains("ReconstructTangentToWorld", source);
            StringAssert.Contains("VividCompileExperimentalStandardSurface", source);
            StringAssert.Contains("VividCompileExperimentalLayeredSurface", source);
            StringAssert.Contains("topSurfaceBindingData", source);
            StringAssert.DoesNotContain("dualSlabMaterialData.LayerOperator", source);
            StringAssert.DoesNotContain("dualSlabMaterialData.LayerWeight", source);
            StringAssert.DoesNotContain("dualSlabMaterialData.Emission.rgb", source);
            StringAssert.DoesNotContain(
                "if(!loadedMaterialProgram){materialData=PullMaterialData(",
                compactSource);
            StringAssert.Contains(
                "elseif(usesLegacyMaterial){"
                + "if(instanceData.MaterialIndex>=_MaterialDataCount)discard;"
                + "materialData=PullMaterialData(instanceData.MaterialIndex);",
                compactSource);
            StringAssert.Contains(
                "materialProgramID!=VIVIDMATERIALPROGRAMID_INVALID"
                + "&&!evaluatedAOTSurface",
                compactSource);
            StringAssert.Contains("VividPackExperimentalClosureBuffer", source);
            StringAssert.DoesNotContain("VividExperimentalVBufferMaterialData", source);
            StringAssert.DoesNotContain("_VividExperimentalVBufferMaterials", source);
            StringAssert.DoesNotContain("VIVID_EXPERIMENTAL_FEATURE", source);
            StringAssert.DoesNotContain("RendererList", source);
        }
    }
}
