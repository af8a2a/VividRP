using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class ShaderVariablesGlobalSHTests
    {
        [Test]
        public void Source_RemovesCpuSkyDiffuseSHCompatibilityFields()
        {
            var skyDataSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividSkyData.cs"));
            var globalsSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "ShaderVariablesGlobal.cs"));

            Assert.That(skyDataSource, Does.Not.Contain("hasDiffuseSH"));
            Assert.That(skyDataSource, Does.Not.Contain("diffuseSH"));
            Assert.That(globalsSource, Does.Contain("var ambientProbe = skyData == null"));
            Assert.That(globalsSource, Does.Not.Contain("skyData.hasDiffuseSH"));
            Assert.That(globalsSource, Does.Not.Contain("skyData.diffuseSH"));
        }

        [Test]
        public void Create_FallsBackToRenderSettingsAmbientProbe_WhenSkyDataIsMissing()
        {
            var originalAmbientProbe = RenderSettings.ambientProbe;
            var sh = new SphericalHarmonicsL2();
            PopulateChannel(ref sh, 0, 2.0f);
            PopulateChannel(ref sh, 1, 12.0f);
            PopulateChannel(ref sh, 2, 22.0f);

            try
            {
                RenderSettings.ambientProbe = sh;
                var globals = ShaderVariablesGlobal.Create(default, null, null);

                Assert.That(globals._VividSHAr, Is.EqualTo(new Vector4(5.0f, 3.0f, 4.0f, -6.0f)));
                Assert.That(globals._VividSHC, Is.EqualTo(new Vector4(10.0f, 20.0f, 30.0f, 1.0f)));
            }
            finally
            {
                RenderSettings.ambientProbe = originalAmbientProbe;
            }
        }

        [Test]
        public void Create_UsesZeroAmbientProbe_WhenSkyDataExists()
        {
            var originalAmbientProbe = RenderSettings.ambientProbe;
            var sh = new SphericalHarmonicsL2();
            PopulateChannel(ref sh, 0, 2.0f);
            PopulateChannel(ref sh, 1, 12.0f);
            PopulateChannel(ref sh, 2, 22.0f);

            try
            {
                RenderSettings.ambientProbe = sh;
                var globals = ShaderVariablesGlobal.Create(default, null, new VividSkyData());

                Assert.That(globals._VividSHAr, Is.EqualTo(Vector4.zero));
                Assert.That(globals._VividSHAg, Is.EqualTo(Vector4.zero));
                Assert.That(globals._VividSHAb, Is.EqualTo(Vector4.zero));
                Assert.That(globals._VividSHBr, Is.EqualTo(Vector4.zero));
                Assert.That(globals._VividSHBg, Is.EqualTo(Vector4.zero));
                Assert.That(globals._VividSHBb, Is.EqualTo(Vector4.zero));
                Assert.That(globals._VividSHC, Is.EqualTo(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)));
            }
            finally
            {
                RenderSettings.ambientProbe = originalAmbientProbe;
            }
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
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

        private static void PopulateChannel(ref SphericalHarmonicsL2 sh, int channel, float startValue)
        {
            for (var coefficient = 0; coefficient < 9; coefficient++)
                sh[channel, coefficient] = startValue + coefficient;
        }
    }
}
