using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class NrdNormalRoughnessPackingTests
    {
        [Test]
        public void GBufferLayout_PacksLinearRoughnessAndNrdMaterialIdIntoGBuffer1()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "GBuffer.hlsl"));

            Assert.That(source, Does.Contain("RT1 (A2B10G10R10_UNORM)"));
            Assert.That(source, Does.Contain("output.rt1 = float4("));
            Assert.That(source, Does.Contain("surfaceData.linearRoughness,"));
            Assert.That(source, Does.Contain("EncodeVividNrdMaterialId(surfaceData.materialId)"));
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
