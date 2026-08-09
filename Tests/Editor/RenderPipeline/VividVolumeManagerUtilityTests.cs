using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VividVolumeManagerUtilityTests
    {
        [Test]
        public void ResolveVolumeLayerMask_ReturnsAdditionalCameraMask_WhenProvided()
        {
            var gameObject = new GameObject("Volume Mask Camera");
            var camera = gameObject.AddComponent<Camera>();
            var additionalCameraData = gameObject.AddComponent<VividAdditionalCameraData>();
            additionalCameraData.volumeLayerMask = 1 << 7;

            try
            {
                var layerMask = VividVolumeManagerUtility.ResolveVolumeLayerMask(camera, additionalCameraData);

                Assert.That(layerMask.value, Is.EqualTo(1 << 7));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ResolveVolumeLayerMask_ReturnsCameraCullingMask_WhenNoAdditionalDataIsAvailable()
        {
            var gameObject = new GameObject("Volume Mask Camera");
            var camera = gameObject.AddComponent<Camera>();
            camera.cullingMask = 1 << 5;

            try
            {
                var layerMask = VividVolumeManagerUtility.ResolveVolumeLayerMask(camera);

                Assert.That(layerMask.value, Is.EqualTo(1 << 5));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void UpgradeDefaultVolumeProfileValues_ReplacesKnownLegacyUnrealDefaultsOnce()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var autoExposure = profile.Add<AutoExposure>(true);

            try
            {
                autoExposure.percent.value = new Vector2(1f, 99f);
                autoExposure.minEV100.value = 5f;
                autoExposure.maxEV100.value = 13f;
                autoExposure.histogramLogRange.value = new Vector2(-10f, 6f);

                Assert.That(
                    VividVolumeManagerUtility.UpgradeDefaultVolumeProfileValues(profile),
                    Is.True);
                Assert.That(autoExposure.percent.value, Is.EqualTo(new Vector2(10f, 90f)));
                Assert.That(autoExposure.minEV100.value, Is.EqualTo(-10f).Within(1e-5f));
                Assert.That(autoExposure.maxEV100.value, Is.EqualTo(20f).Within(1e-5f));
                Assert.That(
                    autoExposure.histogramLogRange.value,
                    Is.EqualTo(new Vector2(-10f, 20f)));
                Assert.That(
                    autoExposure.ConsumeUnrealDefaultProfileUpgradePendingSave(),
                    Is.True);
                Assert.That(
                    autoExposure.ConsumeUnrealDefaultProfileUpgradePendingSave(),
                    Is.False);

                autoExposure.histogramLogRange.value = new Vector2(-8f, 12f);

                Assert.That(
                    VividVolumeManagerUtility.UpgradeDefaultVolumeProfileValues(profile),
                    Is.False);
                Assert.That(
                    autoExposure.histogramLogRange.value,
                    Is.EqualTo(new Vector2(-8f, 12f)));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
