using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class NrdNormalRoughnessPackingTests
    {
        [Test]
        public void GBufferLayout_PacksMaterialFeatureIdIntoGBuffer0()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "GBuffer.hlsl"));

            Assert.That(source, Does.Contain("RT0 (RGBA8_UNORM)              : BaseColor.rgb + MaterialFeatureId.a"));
            Assert.That(source, Does.Contain("uint materialFeatures;"));
            Assert.That(source, Does.Contain("#define VIVID_MATERIALFEATURE_ID_MASK 31u"));
            Assert.That(source, Does.Contain("float EncodeVividMaterialFeatureId(uint materialFeatures)"));
            Assert.That(source, Does.Contain("uint DecodeVividMaterialFeatureId(float encodedMaterialFeatureId)"));
            Assert.That(source, Does.Contain("uint DecodeVividMaterialFeatures(float encodedMaterialFeatureId)"));
            Assert.That(source, Does.Contain("uint LegacyVividMaterialIdToFeatures(uint materialId)"));
            Assert.That(source, Does.Contain("output.rt0 = float4(surfaceData.baseColor, EncodeVividMaterialFeatureId(surfaceData.materialFeatures));"));
            Assert.That(source, Does.Contain("surfaceData.materialFeatures = DecodeVividMaterialFeatures(rt0.a);"));
        }

        [Test]
        public void GBufferLayout_PacksLinearRoughnessAndNrdMaterialIdIntoGBuffer1()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "GBuffer.hlsl"));

            Assert.That(source, Does.Contain("RT1 (A2B10G10R10_UNORM)"));
            Assert.That(source, Does.Contain("output.rt1 = float4("));
            Assert.That(source, Does.Contain("surfaceData.linearRoughness,"));
            Assert.That(source, Does.Contain("EncodeVividNrdMaterialId(GetVividNrdMaterialIdFromFeatures(surfaceData.materialFeatures))"));
        }

        [Test]
        public void GBufferLayout_PacksMetallicAoAndTwoMaterialDataChannelsIntoGBuffer2()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "GBuffer.hlsl"));

            Assert.That(source, Does.Contain("RT2 (RGBA8_UNORM)              : Metallic.r + AO.g + MaterialData0.b + MaterialData1.a"));
            Assert.That(source, Does.Contain("surfaceData.metallic,"));
            Assert.That(source, Does.Contain("surfaceData.ambientOcclusion,"));
            Assert.That(source, Does.Contain("surfaceData.customData,"));
            Assert.That(source, Does.Contain("surfaceData.customData1);"));
        }

        [Test]
        public void GBufferLayout_PacksBakedGiAndValidityIntoGBuffer4()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "GBuffer.hlsl"));

            Assert.That(source, Does.Contain("RT4 (RGBA16_SFLOAT)            : BuiltinData.bakeDiffuseLighting.rgb + BuiltinData.hasBakedGI.a"));
            Assert.That(source, Does.Contain("VividBuiltinData builtinData;"));
            Assert.That(source, Does.Contain("output.rt4 = float4(surfaceData.builtinData.bakeDiffuseLighting, surfaceData.builtinData.hasBakedGI);"));
            Assert.That(source, Does.Contain("surfaceData.builtinData.bakeDiffuseLighting = max(rt4.rgb, 0.0);"));
            Assert.That(source, Does.Contain("surfaceData.builtinData.hasBakedGI = saturate(rt4.a);"));
        }

        [Test]
        public void BuiltinData_StoresBakeLightingAndShadowMaskMaterialData()
        {
            var builtinDataSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "BuiltinData.hlsl"));
            var bakedGiSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "BakedGI.hlsl"));

            Assert.That(builtinDataSource, Does.Contain("struct VividBuiltinData"));
            Assert.That(builtinDataSource, Does.Contain("float3 bakeDiffuseLighting;"));
            Assert.That(builtinDataSource, Does.Contain("float3 backBakeDiffuseLighting;"));
            Assert.That(builtinDataSource, Does.Contain("float4 shadowMask;"));
            Assert.That(builtinDataSource, Does.Contain("float hasBakedGI;"));
            Assert.That(builtinDataSource, Does.Contain("float isLightmap;"));
            Assert.That(bakedGiSource, Does.Contain("float4 SampleVividShadowMask(float2 lightmapUV, float3 positionWS)"));
            Assert.That(bakedGiSource, Does.Contain("SampleVividShadowMask(lightmapUV, positionWS)"));
        }

        [Test]
        public void NrdCustomEncoding_DecodesPackedOctNormalRoughnessAndMaterialId()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "NRD", "NRD.hlsl"));

            Assert.That(source, Does.Contain("#elif( NRD_NORMAL_ENCODING == 5 )"));
            Assert.That(source, Does.Contain("half2 octNormalWS = p.xy * 2.0 - 1.0;"));
            Assert.That(source, Does.Contain("r.w = p.z;"));
            Assert.That(source, Does.Contain("materialID = round( saturate( p.w ) * 3.0 );"));
        }

        [Test]
        public void NrdCustomEncoding_PacksPackedOctNormalRoughnessAndMaterialId()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "NRD", "NRD.hlsl"));

            Assert.That(source, Does.Contain("float2 octNormalWS = PackNormalOctQuadEncode( _NRD_SafeNormalize( N ) );"));
            Assert.That(source, Does.Contain("p = float4( octNormalWS * 0.5 + 0.5, roughness, saturate( materialID / 3.0 ) );"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
