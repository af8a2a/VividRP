using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class ShaderVariablesGlobalSHTests
    {
        [Test]
        public void Create_PacksSkyDiffuseSHIntoExplicitGlobalVectors_WhenSkyDataIsProvided()
        {
            var sh = new SphericalHarmonicsL2();
            PopulateChannel(ref sh, 0, 1.0f);
            PopulateChannel(ref sh, 1, 11.0f);
            PopulateChannel(ref sh, 2, 21.0f);

            var skyData = new VividSkyData
            {
                hasDiffuseSH = true,
                diffuseSH = sh
            };

            var globals = ShaderVariablesGlobal.Create(default, null, skyData);

            Assert.That(globals._VividSHAr, Is.EqualTo(new Vector4(4.0f, 2.0f, 3.0f, -6.0f)));
            Assert.That(globals._VividSHAg, Is.EqualTo(new Vector4(14.0f, 12.0f, 13.0f, -6.0f)));
            Assert.That(globals._VividSHAb, Is.EqualTo(new Vector4(24.0f, 22.0f, 23.0f, -6.0f)));
            Assert.That(globals._VividSHBr, Is.EqualTo(new Vector4(5.0f, 6.0f, 21.0f, 8.0f)));
            Assert.That(globals._VividSHBg, Is.EqualTo(new Vector4(15.0f, 16.0f, 51.0f, 18.0f)));
            Assert.That(globals._VividSHBb, Is.EqualTo(new Vector4(25.0f, 26.0f, 81.0f, 28.0f)));
            Assert.That(globals._VividSHC, Is.EqualTo(new Vector4(9.0f, 19.0f, 29.0f, 1.0f)));
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

        private static void PopulateChannel(ref SphericalHarmonicsL2 sh, int channel, float startValue)
        {
            for (var coefficient = 0; coefficient < 9; coefficient++)
                sh[channel, coefficient] = startValue + coefficient;
        }
    }
}
