using System.IO;
using NUnit.Framework;

namespace VividRP.Editor.Tests
{
    public sealed class DLSSRRResourcePrepContractTests
    {
        private const string ShaderAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Private/DLSS/DLSSRRResourcePrep.compute";

        [Test]
        public void ResourcePrep_ConsumesSurfaceSummaryNormalRoughnessAndSpecularF0()
        {
            string source = File.ReadAllText(ShaderAssetPath);

            StringAssert.Contains("SurfaceSummaryGBuffer.hlsl", source);
            StringAssert.Contains("VividDecodeSurfaceSummaryNormal(gbuffer1.xy)", source);
            StringAssert.Contains("float perceptualRoughness = saturate(gbuffer1.z)", source);
            StringAssert.Contains(
                "float alphaRoughness = perceptualRoughness * perceptualRoughness",
                source);
            StringAssert.Contains(
                "VividDecodeSurfaceSummarySpecularF0(gbuffer2.rgb)",
                source);
            StringAssert.Contains(
                "float4(normalWS, perceptualRoughness)",
                source);
            StringAssert.DoesNotContain("UnpackNormalOctQuadEncode", source);
            StringAssert.DoesNotContain("PerceptualSmoothnessTo", source);
            StringAssert.DoesNotContain("metallic", source.ToLowerInvariant());
        }
    }
}
