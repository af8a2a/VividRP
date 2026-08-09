using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class HdrpLightingShaderPortTests
    {
        [Test]
        public void LightingFolder_ContainsHdrpClusteredLightBuildShaders()
        {
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "LightLoop.cs.hlsl")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "ShaderBase.hlsl")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "ShaderConfig.cs.hlsl")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "ShaderVariablesGlobalLightLoop.hlsl")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "scrbound.compute")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "lightlistbuild-bigtile.compute")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "lightlistbuild-clustered.compute")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "ClearLightLists.compute")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "lightlistbuild-clearatomic.compute")), Is.True);
        }

        [Test]
        public void ShaderVariablesLightList_CSharpLayout_MatchesHdrpCBufferPacking()
        {
            var assembly = typeof(LightGridPass).Assembly;
            var dimensionsType = assembly.GetType("VividRP.Runtime.ShaderVariablesLightListInt2");
            var lightListType = assembly.GetType("VividRP.Runtime.ShaderVariablesLightList");

            Assert.That(dimensionsType, Is.Not.Null);
            Assert.That(lightListType, Is.Not.Null);
            Assert.That(Marshal.SizeOf(dimensionsType), Is.EqualTo(8));
            Assert.That(Marshal.SizeOf(lightListType), Is.EqualTo(564));
        }

        private static string GetPackagePath(params string[] parts)
        {
            var vividPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "VividRP"));
            if (Directory.Exists(vividPath))
                return Path.Combine(vividPath, Path.Combine(parts));

            var legacyPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "com.af8a2a.vividrp"));
            return Path.Combine(legacyPath, Path.Combine(parts));
        }
    }
}
