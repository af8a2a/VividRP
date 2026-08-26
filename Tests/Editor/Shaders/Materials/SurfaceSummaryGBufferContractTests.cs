using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VividRP.Editor.Tests
{
    public sealed class SurfaceSummaryGBufferContractTests
    {
        private const string SurfaceSummaryAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Public/SurfaceSummaryGBuffer.hlsl";

        [Test]
        public void SurfaceSummaryGBuffer_DeclaresFrozenAbiAndCentralizedPacking()
        {
            string source = File.ReadAllText(SurfaceSummaryAssetPath);
            string compactSource = string.Concat(
                source.Where(character => !char.IsWhiteSpace(character)));

            StringAssert.Contains(
                "#define VIVID_SURFACE_SUMMARY_GBUFFER_ABI_VERSION 1u",
                source);
            StringAssert.Contains(
                "#define VIVID_SURFACE_SUMMARY_GBUFFER_ABI_TAG 3u",
                source);
            StringAssert.Contains("RT0 (R8G8B8A8_SRGB)", source);
            StringAssert.Contains("RT1 (A2B10G10R10_UNORM)", source);
            StringAssert.Contains("RT2 (R8G8B8A8_UNORM)", source);
            StringAssert.Contains("RT3 (B10G11R11_UFLOAT)", source);
            StringAssert.Contains("RT4 / DiffuseIrradiance", source);
            StringAssert.Contains("VividSurfaceSummaryData", source);
            StringAssert.Contains("VividPackSurfaceSummaryGBuffer(", source);
            StringAssert.Contains("VividUnpackSurfaceSummaryGBuffer(", source);
            StringAssert.Contains("VividSanitizeSurfaceSummaryData(", source);
            StringAssert.Contains("VividEncodeSurfaceSummarySpecularF0(", source);
            StringAssert.Contains("VividDecodeSurfaceSummarySpecularF0(", source);
            StringAssert.Contains("sqrt(saturate(specularF0))", source);
            StringAssert.Contains("encodedSpecularF0 * encodedSpecularF0", source);
            StringAssert.Contains("surfaceData.perceptualRoughness", source);
            StringAssert.Contains("surfaceData.ambientOcclusion", source);
            StringAssert.Contains("surfaceData.diffuseIrradiance", source);
            StringAssert.Contains(
                "output.rt0=float4(surfaceData.diffuseAlbedo,"
                + "VividEncodeDeferredExportHeader(surfaceData.deferredExportHeader));",
                compactSource);
            StringAssert.Contains(
                "output.rt1=float4(VividEncodeSurfaceSummaryNormal(surfaceData.normalWS),"
                + "surfaceData.perceptualRoughness,VividEncodeSurfaceSummaryGBufferABITag());",
                compactSource);
            StringAssert.Contains(
                "output.rt2=float4(VividEncodeSurfaceSummarySpecularF0(surfaceData.specularF0),"
                + "surfaceData.ambientOcclusion);",
                compactSource);
            StringAssert.Contains(
                "output.rt3=float4(surfaceData.emissive,0.0);",
                compactSource);
            StringAssert.Contains(
                "output.rt4=VividPackSurfaceSummaryDiffuseIrradiance("
                + "surfaceData.diffuseIrradiance);",
                compactSource);
            StringAssert.Contains(
                "boolisValidABI=VividIsSurfaceSummaryGBufferABIValid(rt1.a);",
                compactSource);
            StringAssert.Contains(
                "surfaceData.deferredExportHeader=isValidABI?"
                + "VividDecodeDeferredExportHeader(rt0.a):"
                + "VIVID_DEFERRED_EXPORT_CLASS_ERROR;",
                compactSource);
            StringAssert.Contains(
                "surfaceData.perceptualRoughness=saturate(rt1.z);",
                compactSource);
            StringAssert.Contains(
                "surfaceData.specularF0=VividDecodeSurfaceSummarySpecularF0(rt2.rgb);",
                compactSource);
            StringAssert.Contains(
                "surfaceData.ambientOcclusion=saturate(rt2.a);",
                compactSource);
            StringAssert.DoesNotContain("linearRoughness", source);
            StringAssert.DoesNotContain("metallic", source);
        }

        [Test]
        public void DeferredExportHeader_DeclaresFrozenClassesFlagsAndUnormEncoding()
        {
            string source = File.ReadAllText(SurfaceSummaryAssetPath);

            AssertDefine(source, "VIVID_DEFERRED_EXPORT_CLASS_EMPTY", "0u");
            AssertDefine(source, "VIVID_DEFERRED_EXPORT_CLASS_UNLIT", "1u");
            AssertDefine(source, "VIVID_DEFERRED_EXPORT_CLASS_FAST_SLAB", "2u");
            AssertDefine(source, "VIVID_DEFERRED_EXPORT_CLASS_GENERAL_SLAB", "3u");
            AssertDefine(source, "VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB", "4u");
            AssertDefine(source, "VIVID_DEFERRED_EXPORT_CLASS_SUBSURFACE", "5u");
            AssertDefine(source, "VIVID_DEFERRED_EXPORT_CLASS_CATCH_ALL", "14u");
            AssertDefine(source, "VIVID_DEFERRED_EXPORT_CLASS_ERROR", "15u");
            AssertDefine(source, "VIVID_DEFERRED_EXPORT_CLASS_MASK", "0x0Fu");
            AssertDefine(
                source,
                "VIVID_DEFERRED_EXPORT_FLAG_VERTICAL_LAYER",
                "(1u << 4)");
            AssertDefine(
                source,
                "VIVID_DEFERRED_EXPORT_FLAG_HAS_DIFFUSE_IRRADIANCE",
                "(1u << 5)");
            AssertDefine(
                source,
                "VIVID_DEFERRED_EXPORT_FLAG_RECEIVE_SSR",
                "(1u << 6)");
            AssertDefine(
                source,
                "VIVID_DEFERRED_EXPORT_FLAG_RECEIVE_DECALS",
                "(1u << 7)");
            StringAssert.Contains("VividBuildDeferredExportHeader(", source);
            StringAssert.Contains("VividSanitizeDeferredExportHeader(", source);
            StringAssert.Contains("VividEncodeDeferredExportHeader(", source);
            StringAssert.Contains("VividDecodeDeferredExportHeader(", source);
            StringAssert.Contains("VividGetDeferredExportClass(", source);
            StringAssert.Contains("VividHasDeferredExportFlag(", source);
            StringAssert.Contains("VividEncodeSurfaceSummaryGBufferABITag(", source);
            StringAssert.Contains("VividDecodeSurfaceSummaryGBufferABITag(", source);
            StringAssert.Contains("VividIsSurfaceSummaryGBufferABIValid(", source);
            StringAssert.Contains("* (1.0 / 255.0)", source);
            StringAssert.Contains("round(saturate(encodedHeader) * 255.0)", source);
        }

        [Test]
        public void DualSlabLayerSidecar_DeclaresFrozenEightByteExtension()
        {
            string source = File.ReadAllText(SurfaceSummaryAssetPath);
            string compactSource = string.Concat(
                source.Where(character => !char.IsWhiteSpace(character)));

            StringAssert.Contains(
                "#define VIVID_DUAL_SLAB_LAYER_SIDECAR_ABI_VERSION 1u",
                source);
            StringAssert.Contains(
                "#define VIVID_DUAL_SLAB_LAYER_SIDECAR_MIN_WEIGHT (0.5f / 255.0f)",
                source);
            StringAssert.Contains("RT5 / LayerAux0 (R8G8B8A8_SRGB)", source);
            StringAssert.Contains("RT6 / LayerAux1 (R8G8B8A8_UNORM)", source);
            StringAssert.Contains("VividDualSlabLayerData", source);
            StringAssert.Contains("VividPackDualSlabLayerAux0(", source);
            StringAssert.Contains("VividPackDualSlabLayerAux1(", source);
            StringAssert.Contains("VividUnpackDualSlabLayerSidecar(", source);
            StringAssert.Contains("VividDualSlabLayerSidecarOutput", source);
            StringAssert.Contains("VividPackDualSlabLayerSidecar(", source);
            StringAssert.Contains("VividIsDualSlabLayerSidecarValid(", source);
            StringAssert.Contains("float4 rt0 : SV_Target0;", source);
            StringAssert.Contains("float4 rt1 : SV_Target1;", source);
            StringAssert.Contains(
                "returnfloat4(layerData.diffuseAlbedo,layerData.layerWeight);",
                compactSource);
            StringAssert.Contains(
                "returnfloat4(VividEncodeSurfaceSummarySpecularF0(" +
                "layerData.specularF0),layerData.perceptualRoughness);",
                compactSource);
            StringAssert.Contains(
                "layerData.specularF0=VividDecodeSurfaceSummarySpecularF0(" +
                "layerAux1.rgb);",
                compactSource);
        }

        private static void AssertDefine(
            string source,
            string identifier,
            string value)
        {
            StringAssert.Contains($"#define {identifier} {value}", source);
        }
    }
}
