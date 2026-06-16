using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VividHDROutputUtilityTests
    {
        [TestCase(CameraType.Game, true)]
        [TestCase(CameraType.SceneView, false)]
        [TestCase(CameraType.Preview, false)]
        [TestCase(CameraType.Reflection, false)]
        public void HDROutputActiveForCameraType_OnlyAllowsGameCameras(
            CameraType cameraType,
            bool canUseMainDisplayHDR)
        {
            var expected = canUseMainDisplayHDR && VividHDROutputUtility.HDROutputForMainDisplayIsActive();

            Assert.That(VividHDROutputUtility.HDROutputActiveForCameraType(cameraType), Is.EqualTo(expected));
        }

        [Test]
        public void HDRDisplayInformationForCamera_UsesMainHDROutputSettings()
        {
            var displaySettings = HDROutputSettings.main;
            var displayInformation = VividHDROutputUtility.HDRDisplayInformationForCamera(null);

            Assert.That(
                displayInformation.maxFullFrameToneMapLuminance,
                Is.EqualTo(displaySettings.maxFullFrameToneMapLuminance));
            Assert.That(displayInformation.maxToneMapLuminance, Is.EqualTo(displaySettings.maxToneMapLuminance));
            Assert.That(displayInformation.minToneMapLuminance, Is.EqualTo(displaySettings.minToneMapLuminance));
            Assert.That(displayInformation.paperWhiteNits, Is.EqualTo(displaySettings.paperWhiteNits));
        }

        [Test]
        public void HDRDisplayColorGamutForCamera_UsesMainHDROutputSettings()
        {
            Assert.That(
                VividHDROutputUtility.HDRDisplayColorGamutForCamera(null),
                Is.EqualTo(HDROutputSettings.main.displayColorGamut));
        }

        [Test]
        public void SetHDRState_DoesNothing_WhenCameraIsNull()
        {
            var enableHdrOnce = true;

            VividHDROutputUtility.SetHDRState(null, ref enableHdrOnce);

            Assert.That(enableHdrOnce, Is.True);
        }

        [Test]
        public void HDROutputActiveForCamera_ReturnsFalse_WhenCameraDisallowsHDR()
        {
            var gameObject = new GameObject("HDR Output Test Camera");
            try
            {
                var camera = gameObject.AddComponent<Camera>();
                camera.cameraType = CameraType.Game;
                camera.allowHDR = false;

                Assert.That(VividHDROutputUtility.HDROutputAllowedForCamera(camera), Is.False);
                Assert.That(VividHDROutputUtility.HDROutputActiveForCamera(camera), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
