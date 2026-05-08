using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
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
        public void BuildLODSelectionContext_UsesCameraProjectionAndPixelSize()
        {
            GameObject cameraObject = null;

            try
            {
                cameraObject = new GameObject("GPUDrivenLODCamera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 250.0f;
                camera.fieldOfView = 45.0f;
                camera.aspect = 16.0f / 9.0f;
                cameraObject.transform.SetPositionAndRotation(
                    new Vector3(4.0f, 5.0f, 6.0f),
                    Quaternion.Euler(15.0f, 35.0f, 5.0f)
                );

                VividGPUDrivenCullingContextUtility.BuildLODSelectionContext(
                    camera,
                    out VividGPULODSelectionContext lodSelectionContext
                );

                Matrix4x4 expectedViewProjection =
                    GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;

                AssertFloat4x4Approximately(lodSelectionContext.ViewProjectionMatrix, expectedViewProjection);
                Assert.That(lodSelectionContext.CameraPosition.x, Is.EqualTo(4.0f).Within(0.0001f));
                Assert.That(lodSelectionContext.CameraPosition.y, Is.EqualTo(5.0f).Within(0.0001f));
                Assert.That(lodSelectionContext.CameraPosition.z, Is.EqualTo(6.0f).Within(0.0001f));
                Assert.That(lodSelectionContext.CameraUp.y, Is.Not.EqualTo(0.0f));
                Assert.That(lodSelectionContext.CameraRight.x, Is.Not.EqualTo(0.0f));
                Assert.That(lodSelectionContext.ScreenSizePixels.x, Is.GreaterThan(0.0f));
                Assert.That(lodSelectionContext.ScreenSizePixels.y, Is.GreaterThan(0.0f));
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
        public void Build_TransformsCullingSphereIntoViewSpace_WhenProvided()
        {
            var viewMatrix = Matrix4x4.TRS(
                new Vector3(2.0f, 3.0f, 4.0f),
                Quaternion.Euler(0.0f, 90.0f, 0.0f),
                Vector3.one
            ).inverse;
            var projectionMatrix = Matrix4x4.Ortho(-10.0f, 10.0f, -10.0f, 10.0f, 0.1f, 50.0f);
            var cullingSphereWS = new Vector4(5.0f, 7.0f, 11.0f, 13.0f);

            VividGPUDrivenCullingContextUtility.Build(
                viewMatrix,
                projectionMatrix,
                cameraPositionWS: Vector3.zero,
                cameraRightWS: Vector3.right,
                cameraUpWS: Vector3.up,
                pixelSize: new Vector2(512.0f, 512.0f),
                isPerspective: false,
                passMask: VividInstancePassMask.Shadows,
                cullingSphereWS,
                out VividGPUCullingContext cullingContext,
                out _);

            Vector3 expectedCenterLS = viewMatrix.MultiplyPoint3x4(cullingSphereWS);
            Assert.That(cullingContext.CullingSphereLS.x, Is.EqualTo(expectedCenterLS.x).Within(0.0001f));
            Assert.That(cullingContext.CullingSphereLS.y, Is.EqualTo(expectedCenterLS.y).Within(0.0001f));
            Assert.That(cullingContext.CullingSphereLS.z, Is.EqualTo(expectedCenterLS.z).Within(0.0001f));
            Assert.That(cullingContext.CullingSphereLS.w, Is.EqualTo(cullingSphereWS.w).Within(0.0001f));
        }

        [Test]
        public void GPUDrivenCommon_UsesProvidedViewMatrixForward_ForOrthographicConeCulling()
        {
            string source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividGPUDrivenCommon.hlsl"));

            Assert.That(source, Does.Contain("float3 GetViewForwardDir(const float4x4 viewMatrix)"));
            Assert.That(source, Does.Contain("return normalize(-float3(viewMatrix._m20, viewMatrix._m21, viewMatrix._m22));"));
            Assert.That(source, Does.Not.Contain("UNITY_MATRIX_I_V"));
            Assert.That(source, Does.Contain(": GetViewForwardDir(cullingContext.ViewMatrix);"));
        }

        [Test]
        public void GPUDrivenCommon_TransformsConeAxisAsNormal_ForConeCulling()
        {
            string source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividGPUDrivenCommon.hlsl"));

            Assert.That(
                source,
                Does.Contain("mul(meshlet.ConeAxis.xyz, (float3x3) instanceData.WorldToObjectMatrix)"));
            Assert.That(
                source,
                Does.Not.Contain("mul((float3x3) instanceData.ObjectToWorldMatrix, meshlet.ConeAxis.xyz)"));
        }

        [Test]
        public void GPUDrivenShaders_CullAgainstLightSpaceReceiverSphere_WhenProvided()
        {
            string commonSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividGPUDrivenCommon.hlsl"));
            string instanceCullingSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "GPUInstanceCulling.compute"));
            string meshletCullingSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "GPUMeshletCulling.compute"));

            Assert.That(commonSource, Does.Contain("bool DoLightSphereCulling("));
            Assert.That(commonSource, Does.Contain("bool LightSphereCulling("));
            Assert.That(commonSource, Does.Contain("cullingContext.CullingSphereLS.w <= 0.0f"));
            Assert.That(commonSource, Does.Contain("DoLightSphereCulling(cullingContext.CullingSphereLS, casterBoundingSphereWS, worldToLightSpaceRotation)"));
            Assert.That(instanceCullingSource, Does.Contain("!LightSphereCulling(cullingContext, boundingSphereWS)"));
            Assert.That(meshletCullingSource, Does.Contain("!LightSphereCulling(cullingContext, boundingSphereWS)"));
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

        private static void AssertFloat4x4Approximately(float4x4 actual, Matrix4x4 expected)
        {
            Assert.That(actual.c0.x, Is.EqualTo(expected.m00).Within(0.0001f));
            Assert.That(actual.c0.y, Is.EqualTo(expected.m10).Within(0.0001f));
            Assert.That(actual.c0.z, Is.EqualTo(expected.m20).Within(0.0001f));
            Assert.That(actual.c0.w, Is.EqualTo(expected.m30).Within(0.0001f));
            Assert.That(actual.c1.x, Is.EqualTo(expected.m01).Within(0.0001f));
            Assert.That(actual.c1.y, Is.EqualTo(expected.m11).Within(0.0001f));
            Assert.That(actual.c1.z, Is.EqualTo(expected.m21).Within(0.0001f));
            Assert.That(actual.c1.w, Is.EqualTo(expected.m31).Within(0.0001f));
            Assert.That(actual.c2.x, Is.EqualTo(expected.m02).Within(0.0001f));
            Assert.That(actual.c2.y, Is.EqualTo(expected.m12).Within(0.0001f));
            Assert.That(actual.c2.z, Is.EqualTo(expected.m22).Within(0.0001f));
            Assert.That(actual.c2.w, Is.EqualTo(expected.m32).Within(0.0001f));
            Assert.That(actual.c3.x, Is.EqualTo(expected.m03).Within(0.0001f));
            Assert.That(actual.c3.y, Is.EqualTo(expected.m13).Within(0.0001f));
            Assert.That(actual.c3.z, Is.EqualTo(expected.m23).Within(0.0001f));
            Assert.That(actual.c3.w, Is.EqualTo(expected.m33).Within(0.0001f));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "Custom_URP"),
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

            public ulong CompletedFrameFenceValue => 0ul;

            public ulong PendingFrameFenceValue => 1ul;

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
