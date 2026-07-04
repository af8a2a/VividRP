using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using VividRP.Runtime.Particle.Debug;

namespace VividRP.Editor.Tests
{
    public sealed class VividBRGSmokeTestTests
    {
        [Test]
        public void CreatePerInstanceMetadata_UsesAlignedOffsetAndPerInstanceBit()
        {
            MetadataValue metadata = VividBRGSmokeTest.CreatePerInstanceMetadata(
                123,
                VividBRGSmokeTest.ObjectToWorldByteAddress);

            Assert.That(metadata.NameID, Is.EqualTo(123));
            Assert.That(metadata.Value & VividBRGSmokeTest.PerInstanceMetadataMask, Is.Not.Zero);
            Assert.That((metadata.Value & ~VividBRGSmokeTest.PerInstanceMetadataMask) % 16u, Is.Zero);
            Assert.That(
                metadata.Value & ~VividBRGSmokeTest.PerInstanceMetadataMask,
                Is.EqualTo((uint)VividBRGSmokeTest.ObjectToWorldByteAddress));
        }

        [Test]
        public void PackMatrix_StoresUnityPackedMatrixColumns()
        {
            Matrix4x4 matrix = Matrix4x4.TRS(
                new Vector3(1.0f, 2.0f, 3.0f),
                Quaternion.Euler(15.0f, 30.0f, 45.0f),
                new Vector3(2.0f, 3.0f, 4.0f));

            VividBRGSmokeTest.PackedMatrix packed = VividBRGSmokeTest.PackMatrix(matrix);

            Assert.That(packed.c0x, Is.EqualTo(matrix.m00).Within(0.0001f));
            Assert.That(packed.c0y, Is.EqualTo(matrix.m10).Within(0.0001f));
            Assert.That(packed.c0z, Is.EqualTo(matrix.m20).Within(0.0001f));
            Assert.That(packed.c1x, Is.EqualTo(matrix.m01).Within(0.0001f));
            Assert.That(packed.c1y, Is.EqualTo(matrix.m11).Within(0.0001f));
            Assert.That(packed.c1z, Is.EqualTo(matrix.m21).Within(0.0001f));
            Assert.That(packed.c2x, Is.EqualTo(matrix.m02).Within(0.0001f));
            Assert.That(packed.c2y, Is.EqualTo(matrix.m12).Within(0.0001f));
            Assert.That(packed.c2z, Is.EqualTo(matrix.m22).Within(0.0001f));
            Assert.That(packed.c3x, Is.EqualTo(matrix.m03).Within(0.0001f));
            Assert.That(packed.c3y, Is.EqualTo(matrix.m13).Within(0.0001f));
            Assert.That(packed.c3z, Is.EqualTo(matrix.m23).Within(0.0001f));
        }

        [Test]
        public void CalculateWorldBounds_FollowsTransformAndSize()
        {
            Matrix4x4 localToWorld = Matrix4x4.TRS(
                new Vector3(1.0f, 2.0f, 3.0f),
                Quaternion.identity,
                new Vector3(2.0f, 3.0f, 4.0f));

            Bounds bounds = VividBRGSmokeTest.CalculateWorldBounds(localToWorld, 2.0f);

            Assert.That(bounds.center.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(bounds.center.y, Is.EqualTo(2.0f).Within(0.0001f));
            Assert.That(bounds.center.z, Is.EqualTo(3.0f).Within(0.0001f));
            Assert.That(bounds.size.x, Is.EqualTo(4.0f).Within(0.0001f));
            Assert.That(bounds.size.y, Is.EqualTo(6.0f).Within(0.0001f));
            Assert.That(bounds.size.z, Is.EqualTo(0.16f).Within(0.0001f));
        }

        [Test]
        public void IntersectsCullingPlanes_ReturnsTrue_WhenBoundsIntersectAllPlanes()
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
            Plane[] planes = CreateUnitCubePlanes();

            Assert.That(VividBRGSmokeTest.IntersectsCullingPlanes(bounds, planes), Is.True);
        }

        [Test]
        public void IntersectsCullingPlanes_ReturnsFalse_WhenBoundsAreOutsideAPlane()
        {
            Bounds bounds = new Bounds(new Vector3(2.0f, 0.0f, 0.0f), Vector3.one);
            Plane[] planes = CreateUnitCubePlanes();

            Assert.That(VividBRGSmokeTest.IntersectsCullingPlanes(bounds, planes), Is.False);
        }

        [Test]
        public void Shader_CanBeFoundByName()
        {
            Assert.That(Shader.Find(VividBRGSmokeTest.ShaderName), Is.Not.Null);
        }

        [Test]
        public void InitializeForTests_WithMissingShader_LogsWarningAndStaysUninitialized()
        {
            var gameObject = new GameObject(nameof(VividBRGSmokeTestTests));
            gameObject.SetActive(false);

            try
            {
                var component = gameObject.AddComponent<VividBRGSmokeTest>();
                LogAssert.Expect(
                    LogType.Warning,
                    new Regex(@"\[VividRP\] Could not find shader '.+' for VividBRGSmokeTest\."));

                Assert.That(component.InitializeForTests(null), Is.False);
                Assert.That(component.IsInitialized, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void EnableDisable_CanRepeatWithoutThrowing()
        {
            Shader shader = Shader.Find(VividBRGSmokeTest.ShaderName);
            Assert.That(shader, Is.Not.Null);

            var gameObject = new GameObject(nameof(VividBRGSmokeTestTests));

            try
            {
                var component = gameObject.AddComponent<VividBRGSmokeTest>();

                Assert.DoesNotThrow(() =>
                {
                    component.enabled = false;
                    component.enabled = true;
                    component.enabled = false;
                });
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static Plane[] CreateUnitCubePlanes()
        {
            return new[]
            {
                new Plane(Vector3.right, new Vector3(-1.0f, 0.0f, 0.0f)),
                new Plane(Vector3.left, new Vector3(1.0f, 0.0f, 0.0f)),
                new Plane(Vector3.up, new Vector3(0.0f, -1.0f, 0.0f)),
                new Plane(Vector3.down, new Vector3(0.0f, 1.0f, 0.0f)),
                new Plane(Vector3.forward, new Vector3(0.0f, 0.0f, -1.0f)),
                new Plane(Vector3.back, new Vector3(0.0f, 0.0f, 1.0f)),
            };
        }
    }
}
