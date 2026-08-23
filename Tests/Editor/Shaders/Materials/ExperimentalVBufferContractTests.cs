using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.Experimental.Material;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.RenderPass.Experimental.Material;

namespace VividRP.Editor.Tests
{
    public sealed class ExperimentalVBufferContractTests
    {
        private const string ResolveAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosureBufferResolve.shader";

        [Test]
        public void VBufferAbi_UsesVersionedTwentyFourBytePayload()
        {
            Assert.That(ExperimentalVBufferContract.Version, Is.EqualTo(2u));
            Assert.That(ExperimentalVBufferContract.InvalidMaterialValue, Is.Zero);
            Assert.That(ExperimentalVBufferContract.MaterialValueOffset, Is.EqualTo(1u));
            Assert.That(ExperimentalVBufferContract.BytesPerPixel, Is.EqualTo(8 + 8 + 8));
            Assert.That(
                (uint)ExperimentalVBufferMaterialFeatureFlags.RMOMap,
                Is.EqualTo(1u << 9));
            Assert.That(
                Marshal.SizeOf<ExperimentalVBufferMaterialData>(),
                Is.EqualTo(ExperimentalVBufferContract.MaterialRecordStride));

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
        public void ClosureResolve_ConsumesVBufferVtAndCompilesSingleStandardSurface()
        {
            string source = File.ReadAllText(ResolveAssetPath);

            StringAssert.Contains("#define VIVID_EXPERIMENTAL_VBUFFER_VERSION 2u", source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_VBUFFER_MATERIAL_STRIDE 192u",
                source);
            StringAssert.Contains("VividExperimentalVBufferMaterialData", source);
            StringAssert.Contains("VividCreateSurfaceSampleContextGrad", source);
            StringAssert.Contains("VividSampleBaseColorGrad", source);
            StringAssert.Contains("VividSampleNormalGrad", source);
            StringAssert.Contains("VividSampleMaskGrad", source);
            StringAssert.Contains("VIVID_EXPERIMENTAL_FEATURE_RMO_MAP", source);
            StringAssert.Contains("ComputeWorldSpacePosition", source);
            StringAssert.Contains("ReconstructTangentToWorld", source);
            StringAssert.Contains("VividCompileExperimentalStandardSurface", source);
            StringAssert.DoesNotContain("VividCompileExperimentalLayeredSurface", source);
            StringAssert.DoesNotContain("TopBinding", source);
            StringAssert.Contains("VividPackExperimentalClosureBuffer", source);
            StringAssert.DoesNotContain("RendererList", source);
        }
    }
}
