using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class ShaderVariablesGlobalSHTests
    {

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

        [Test]
        public void Create_PopulatesPlanetGlobals_ForPhysicallyBasedSkyCompatibility()
        {
            var globals = ShaderVariablesGlobal.Create(
                new VividCameraData.ShaderVariables
                {
                    worldSpaceCameraPos = new Vector4(0.0f, 10.0f, 0.0f, 1.0f)
                },
                null,
                null);

            Assert.That(globals._VividPlanetCenterRadius.w, Is.GreaterThanOrEqualTo(1000.0f));
            Assert.That(globals._VividPlanetUpAltitude.y, Is.GreaterThan(0.0f));
            Assert.That(globals._VividPlanetUpAltitude.w, Is.GreaterThanOrEqualTo(1.0f));
        }

        [Test]
        public void Create_UsesManualPlanetCenterFromActiveSkySettings()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var cameraObject = new GameObject("Shader Globals Planet Camera");
            var camera = cameraObject.AddComponent<Camera>();

            try
            {
                var settings = profile.Add<SkySettingsVolume>(false);
                settings.renderingSpace.value = RenderingSpace.World;
                settings.centerMode.value = PlanetMode.Manual;
                settings.planetCenter.value = new Vector3(100.0f, -900.0f, 50.0f);

                var skyVolume = profile.Add<PhysicallyBasedSkyVolume>(false);
                skyVolume.planetRadius.value = 1000.0f;

                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                camera.transform.position = new Vector3(100.0f, 120.0f, 50.0f);
                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var globals = ShaderVariablesGlobal.Create(
                    new VividCameraData.ShaderVariables
                    {
                        worldSpaceCameraPos = new Vector4(100.0f, 120.0f, 50.0f, 1.0f)
                    },
                    null,
                    null);

                Assert.That(globals._VividPlanetCenterRadius, Is.EqualTo(new Vector4(100.0f, -900.0f, 50.0f, 1000.0f)));
                Assert.That(globals._VividPlanetUpAltitude, Is.EqualTo(new Vector4(0.0f, 1.0f, 0.0f, 20.0f)));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(profile);
            }
        }

        private static void PopulateChannel(ref SphericalHarmonicsL2 sh, int channel, float startValue)
        {
            for (var coefficient = 0; coefficient < 9; coefficient++)
                sh[channel, coefficient] = startValue + coefficient;
        }
    }
}
