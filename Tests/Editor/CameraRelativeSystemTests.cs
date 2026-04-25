using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class CameraRelativeSystemTests
    {
        private sealed class TestCameraState : CameraRelativeState
        {
            public override void Dispose()
            {
            }
        }

        [Test]
        public void PurgeDestroyedCameras_DoesNotAllocate_WhenCleaningDestroyedCamera()
        {
            var system = new CameraRelativeSystem<TestCameraState>();
            system.PurgeDestroyedCameras();

            var cameraObject = new GameObject("CameraRelativeSystemTestCamera");
            var camera = cameraObject.AddComponent<Camera>();
            system.GetOrCreateBase(camera);

            Object.DestroyImmediate(cameraObject);

            var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            system.PurgeDestroyedCameras();
            var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(allocatedBytes, Is.Zero);
        }
    }
}
