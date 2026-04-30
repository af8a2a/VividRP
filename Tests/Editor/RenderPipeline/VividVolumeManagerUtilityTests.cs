using System.IO;
using NUnit.Framework;
using UnityEngine;
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
        public void VividVolumeManagerUtility_UsesHdrpSceneViewVolumeMaskFallback()
        {
            var source = File.ReadAllText(GetVolumeManagerUtilitySourcePath());

            Assert.That(source, Does.Contain("camera.cameraType == CameraType.SceneView"));
            Assert.That(source, Does.Contain("var mainCamera = Camera.main"));
            Assert.That(source, Does.Contain("mainCamera.TryGetComponent<VividAdditionalCameraData>"));
            Assert.That(source, Does.Contain("return mainCameraData.volumeLayerMask"));
            Assert.That(source, Does.Contain("return s_DefaultVolumeLayerMask"));
        }

        private static string GetVolumeManagerUtilitySourcePath()
        {
            var sourcePath = GetPackageFilePath("Runtime", "RenderPipeline", "VividVolumeManagerUtility.cs");

            Assert.That(File.Exists(sourcePath), Is.True, $"Expected volume manager utility source at '{sourcePath}'.");
            return sourcePath;
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
