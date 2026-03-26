using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Bindless;

namespace VividRP.Editor.Tests
{
    public class VividGPUDrivenCullingContextUtilityTests
    {
        [Test]
        public void Build_PopulatesCameraDerivedData_WhenCameraIsPerspective()
        {
            GameObject cameraObject = null;

            try
            {
                cameraObject = new GameObject("GPUDrivenPerspectiveCamera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 500.0f;
                camera.fieldOfView = 50.0f;
                camera.orthographic = false;
                camera.aspect = 16.0f / 9.0f;
                cameraObject.transform.SetPositionAndRotation(
                    new Vector3(1.0f, 2.0f, 3.0f),
                    Quaternion.Euler(10.0f, 20.0f, 0.0f)
                );

                VividGPUDrivenCullingContextUtility.Build(
                    camera,
                    VividInstancePassMask.Main | VividInstancePassMask.Shadows,
                    out VividGPUCullingContext cullingContext,
                    out VividGPULODSelectionContext lodSelectionContext
                );

                Assert.That(cullingContext.PassMask, Is.EqualTo((int) (VividInstancePassMask.Main | VividInstancePassMask.Shadows)));
                Assert.That(cullingContext.CameraIsPerspective, Is.EqualTo(1));
                Assert.That(cullingContext.CameraPosition.x, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(cullingContext.CameraPosition.y, Is.EqualTo(2.0f).Within(0.0001f));
                Assert.That(cullingContext.CameraPosition.z, Is.EqualTo(3.0f).Within(0.0001f));
                Assert.That(lodSelectionContext.CameraPosition.x, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(lodSelectionContext.CameraUp.y, Is.Not.EqualTo(0.0f));
                Assert.That(lodSelectionContext.CameraRight.x, Is.Not.EqualTo(0.0f));
                Assert.That(lodSelectionContext.ScreenSizePixels.x, Is.GreaterThan(0.0f));
                Assert.That(lodSelectionContext.ScreenSizePixels.y, Is.GreaterThan(0.0f));

                Vector4 firstPlane = VividGPUDrivenCullingContextUtility.GetFrustumPlane(cullingContext, 0);
                Assert.That(firstPlane.sqrMagnitude, Is.GreaterThan(0.0f));
            }
            finally
            {
                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }
            }
        }

        [Test]
        public void Cull_CreatesZeroedTransientBuffers_WhenShadersAreUnavailable()
        {
            GameObject cameraObject = null;
            CommandBuffer cmd = null;

            try
            {
                cameraObject = new GameObject("GPUDrivenCullCamera");
                Camera camera = cameraObject.AddComponent<Camera>();
                using var system = new VividGPUDrivenSystem(new FakeBindlessTextureDescriptorAllocator(8));
                system.PrepareFrame();

                cmd = CommandBufferPool.Get("VividGPUDrivenCullTest");

                Assert.DoesNotThrow(() => system.Cull(camera, cmd, null, null, null));
                Assert.That(system.CullingBufferSet.CandidateMeshletRenderRequestsBuffer, Is.Not.Null);
                Assert.That(system.CullingBufferSet.GPUMeshletCullingIndirectDispatchArgsBuffer, Is.Not.Null);
                Assert.That(system.CullingBufferSet.VisibleMeshletRenderRequestsBuffer, Is.Not.Null);
                Assert.That(system.CullingBufferSet.VisibleMeshletRenderRequestCounterBuffer, Is.Not.Null);
                Assert.That(system.CullingBufferSet.VisibleMeshletIndirectDrawArgsBuffer, Is.Not.Null);
                Assert.That(system.CullingBufferSet.MeshletListBuildJobsBuffer, Is.Not.Null);
                Assert.That(system.VisibleMeshletIndirectDrawArgsBuffer, Is.Not.Null);
                Assert.DoesNotThrow(() => system.BindGlobals(cmd));
            }
            finally
            {
                if (cmd != null)
                {
                    cmd.Clear();
                    CommandBufferPool.Release(cmd);
                }

                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }
            }
        }

        private sealed class FakeBindlessTextureDescriptorAllocator : IBindlessTextureDescriptorAllocator
        {
            public FakeBindlessTextureDescriptorAllocator(uint descriptorHeapCount)
            {
                DescriptorHeapCount = descriptorHeapCount;
                DescriptorCapacity = descriptorHeapCount;
            }

            public bool IsAvailable => true;

            public uint DescriptorHeapCount { get; }
            public uint DescriptorStartIndex { get; }
            public uint DescriptorCapacity { get; }

            public string UnavailableReason => string.Empty;

            public uint CreateSRVDescriptorCallCountThisFrame { get; private set; }

            public void ResetPerFrameStats()
            {
                CreateSRVDescriptorCallCountThisFrame = 0;
            }

            public bool TryCreateTextureDescriptor(Texture texture, uint index)
            {
                CreateSRVDescriptorCallCountThisFrame++;
                return true;
            }
        }
    }
}
