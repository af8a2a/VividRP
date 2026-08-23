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
        public void ClosureResolve_ConsumesMaterialProgramWithoutExperimentalRegistry()
        {
            string source = File.ReadAllText(ResolveAssetPath);
            string surfaceProgramSource = File.ReadAllText(SurfaceProgramAssetPath);
            string compactSource = string.Concat(
                source.Where(character => !char.IsWhiteSpace(character)));

            StringAssert.Contains("UnpackVisibilityBufferValue", source);
            StringAssert.Contains("PullInstanceData(visibility.InstanceID)", source);
            StringAssert.Contains("VividTryLoadStandardSingleSlabSurfaceProgram", source);
            StringAssert.Contains("VividTryLoadDualSlabSurfaceProgram", source);
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
            StringAssert.Contains("dualSlabMaterialData.LayerOperator", source);
            StringAssert.Contains("dualSlabMaterialData.LayerWeight", source);
            StringAssert.Contains("dualSlabMaterialData.Emission.rgb", source);
            StringAssert.DoesNotContain(
                "if(isDualSlab){materialData=PullMaterialData(",
                compactSource);
            StringAssert.Contains("VividPackExperimentalClosureBuffer", source);
            StringAssert.DoesNotContain("VividExperimentalVBufferMaterialData", source);
            StringAssert.DoesNotContain("_VividExperimentalVBufferMaterials", source);
            StringAssert.DoesNotContain("VIVID_EXPERIMENTAL_FEATURE", source);
            StringAssert.DoesNotContain("RendererList", source);
        }
    }
}
