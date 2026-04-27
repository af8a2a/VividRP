using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven.Bindless;
using VividRP.Runtime.SubSystem.Decal;

namespace VividRP.Editor.Tests
{
    public sealed class DecalProjectorTests
    {
        [Test]
        public void TryCreateBoundProxyWorldData_UsesTransformRotationForDecalBounds()
        {
            var owner = new GameObject("Decal Projector Test");

            try
            {
                owner.transform.position = new Vector3(3.0f, 4.0f, 5.0f);
                owner.transform.rotation = Quaternion.Euler(10.0f, 35.0f, 20.0f);

                var projector = owner.AddComponent<DecalProjector>();

                Assert.That(projector.TryCreateBoundProxyWorldData(out BoundProxyWorldData worldData), Is.True);
                Assert.That(worldData.entityId, Is.EqualTo(owner.transform.GetEntityId()));
                Assert.That(worldData.feature, Is.EqualTo(BoundProxyFeature.Decal));
                AssertQuaternion(worldData.worldRotation, owner.transform.rotation);
                AssertVector3(worldData.worldCenter, owner.transform.position);
                Assert.That(worldData.worldAabb.size.x, Is.GreaterThan(0.0f));
                Assert.That(worldData.worldAabb.size.y, Is.GreaterThan(0.0f));
                Assert.That(worldData.worldAabb.size.z, Is.GreaterThan(0.0f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void Register_InitializesDecalSystem_WhenProjectorAppearsAfterDeinitialize()
        {
            var owner = new GameObject("Decal Register Test");

            try
            {
                DecalSystem.Deinitialize();

                var initializedField = typeof(DecalSystem).GetField(
                    "s_Initialized",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(initializedField, Is.Not.Null);

                var projector = owner.AddComponent<DecalProjector>();

                Assert.That(initializedField.GetValue(null), Is.EqualTo(true));

                DecalSystem.Unregister(projector);
            }
            finally
            {
                DecalSystem.Deinitialize();
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void CreateDecalClusterData_RegistersMaterialTextures_WhenBindlessIsAvailable()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(8);
            using var container = new BindlessTextureContainer(allocator);
            var baseColorTexture = new Texture2D(1, 1);
            var normalTexture = new Texture2D(1, 1);
            var metallicTexture = new Texture2D(1, 1);
            var roughnessTexture = new Texture2D(1, 1);

            try
            {
                var decal = new DecalData
                {
                    worldToDecal = Matrix4x4.identity,
                    baseColorTexture = baseColorTexture,
                    normalTexture = normalTexture,
                    metallicTexture = metallicTexture,
                    roughnessTexture = roughnessTexture,
                    baseColor = Color.white,
                    blendDistance = 0.25f,
                    metallic = 0.75f,
                    roughness = 0.35f,
                };

                var clusterData = DecalSystem.CreateDecalClusterData(decal, true, container);

                Assert.That(clusterData.baseColorTextureIndex, Is.EqualTo(7u));
                Assert.That(clusterData.normalTextureIndex, Is.EqualTo(6u));
                Assert.That(clusterData.metallicTextureIndex, Is.EqualTo(5u));
                Assert.That(clusterData.roughnessTextureIndex, Is.EqualTo(4u));
                Assert.That(allocator.DescriptorWrites, Has.Count.EqualTo(4));
                Assert.That(allocator.DescriptorWrites[0].Texture, Is.SameAs(baseColorTexture));
                Assert.That(allocator.DescriptorWrites[1].Texture, Is.SameAs(normalTexture));
                Assert.That(allocator.DescriptorWrites[2].Texture, Is.SameAs(metallicTexture));
                Assert.That(allocator.DescriptorWrites[3].Texture, Is.SameAs(roughnessTexture));
                Assert.That(clusterData.blendDistance, Is.EqualTo(0.25f));
                Assert.That(clusterData.metallic, Is.EqualTo(0.75f));
                Assert.That(clusterData.roughness, Is.EqualTo(0.35f));
            }
            finally
            {
                Object.DestroyImmediate(baseColorTexture);
                Object.DestroyImmediate(normalTexture);
                Object.DestroyImmediate(metallicTexture);
                Object.DestroyImmediate(roughnessTexture);
            }
        }

        [Test]
        public void CreateDecalClusterData_WritesInvalidTextureIndices_WhenBindlessIsUnavailable()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(8)
            {
                IsAvailable = false,
            };
            using var container = new BindlessTextureContainer(allocator);
            var baseColorTexture = new Texture2D(1, 1);
            var normalTexture = new Texture2D(1, 1);
            var metallicTexture = new Texture2D(1, 1);
            var roughnessTexture = new Texture2D(1, 1);

            try
            {
                var decal = new DecalData
                {
                    worldToDecal = Matrix4x4.identity,
                    baseColorTexture = baseColorTexture,
                    normalTexture = normalTexture,
                    metallicTexture = metallicTexture,
                    roughnessTexture = roughnessTexture,
                    baseColor = Color.white,
                    blendDistance = 0.25f,
                    metallic = 0.75f,
                    roughness = 0.35f,
                };

                var clusterData = DecalSystem.CreateDecalClusterData(decal, true, container);

                Assert.That(clusterData.baseColorTextureIndex, Is.EqualTo(BindlessTextureContainer.InvalidTextureIndex));
                Assert.That(clusterData.normalTextureIndex, Is.EqualTo(BindlessTextureContainer.InvalidTextureIndex));
                Assert.That(clusterData.metallicTextureIndex, Is.EqualTo(BindlessTextureContainer.InvalidTextureIndex));
                Assert.That(clusterData.roughnessTextureIndex, Is.EqualTo(BindlessTextureContainer.InvalidTextureIndex));
                Assert.That(clusterData.metallic, Is.EqualTo(0.75f));
                Assert.That(clusterData.roughness, Is.EqualTo(0.35f));
                Assert.That(allocator.DescriptorWrites, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(baseColorTexture);
                Object.DestroyImmediate(normalTexture);
                Object.DestroyImmediate(metallicTexture);
                Object.DestroyImmediate(roughnessTexture);
            }
        }

        [Test]
        public void CreateDecalClusterData_WritesInvalidTextureIndices_WhenTexturesAreMissing()
        {
            var allocator = new FakeBindlessTextureDescriptorAllocator(8);
            using var container = new BindlessTextureContainer(allocator);

            var decal = new DecalData
            {
                worldToDecal = Matrix4x4.identity,
                baseColor = Color.white,
                blendDistance = 0.25f,
                metallic = 0.75f,
                roughness = 0.35f,
            };

            var clusterData = DecalSystem.CreateDecalClusterData(decal, true, container);

            Assert.That(clusterData.baseColorTextureIndex, Is.EqualTo(BindlessTextureContainer.InvalidTextureIndex));
            Assert.That(clusterData.normalTextureIndex, Is.EqualTo(BindlessTextureContainer.InvalidTextureIndex));
            Assert.That(clusterData.metallicTextureIndex, Is.EqualTo(BindlessTextureContainer.InvalidTextureIndex));
            Assert.That(clusterData.roughnessTextureIndex, Is.EqualTo(BindlessTextureContainer.InvalidTextureIndex));
            Assert.That(allocator.DescriptorWrites, Is.Empty);
        }

        [Test]
        public void DecalProjector_MetallicAndRoughnessPropertiesClampToUnitRange()
        {
            var owner = new GameObject("Decal Material Scalar Test");

            try
            {
                var projector = owner.AddComponent<DecalProjector>();

                projector.Metallic = 2.0f;
                projector.Roughness = -1.0f;

                Assert.That(projector.Metallic, Is.EqualTo(1.0f));
                Assert.That(projector.Roughness, Is.EqualTo(0.0f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void CreateWorldToDecalMatrix_UsesHdrpProjectedPlaneConvention()
        {
            var worldData = new BoundProxyWorldData
            {
                worldCenter = Vector3.zero,
                worldRotation = Quaternion.identity,
                boxSize = new Vector3(2.0f, 4.0f, 6.0f),
            };

            Matrix4x4 worldToDecal = DecalSystem.CreateWorldToDecalMatrix(worldData);

            AssertVector3(worldToDecal.MultiplyPoint3x4(new Vector3(1.0f, 0.0f, 0.0f)), new Vector3(0.5f, 0.0f, 0.0f));
            AssertVector3(worldToDecal.MultiplyPoint3x4(new Vector3(0.0f, 2.0f, 0.0f)), new Vector3(0.0f, 0.0f, 0.5f));
            AssertVector3(worldToDecal.MultiplyPoint3x4(new Vector3(0.0f, 0.0f, 3.0f)), new Vector3(0.0f, -0.5f, 0.0f));
        }

        [Test]
        public void NormalizeBlendDistance_ReturnsProjectedPlaneRelativeDistanceClampedToHalfExtent()
        {
            Assert.That(DecalSystem.NormalizeBlendDistance(0.5f, new Vector3(4.0f, 2.0f, 8.0f)), Is.EqualTo(0.25f));
            Assert.That(DecalSystem.NormalizeBlendDistance(0.5f, new Vector3(4.0f, 2.0f, 0.1f)), Is.EqualTo(0.25f));
            Assert.That(DecalSystem.NormalizeBlendDistance(0.5f, new Vector3(4.0f, 0.1f, 8.0f)), Is.EqualTo(0.5f));
            Assert.That(DecalSystem.NormalizeBlendDistance(10.0f, new Vector3(4.0f, 2.0f, 8.0f)), Is.EqualTo(0.5f));
            Assert.That(DecalSystem.NormalizeBlendDistance(0.5f, Vector3.zero), Is.EqualTo(0.0f));
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }

        private static void AssertQuaternion(Quaternion actual, Quaternion expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(tolerance));
        }

        private sealed class FakeBindlessTextureDescriptorAllocator : IBindlessTextureDescriptorAllocator
        {
            public FakeBindlessTextureDescriptorAllocator(uint descriptorHeapCount)
            {
                DescriptorHeapCount = descriptorHeapCount;
                DescriptorCapacity = descriptorHeapCount;
            }

            public bool IsAvailable { get; set; } = true;

            public uint DescriptorHeapCount { get; }

            public uint DescriptorStartIndex { get; }

            public uint DescriptorCapacity { get; }

            public ulong CompletedFrameFenceValue { get; }

            public ulong PendingFrameFenceValue { get; } = 1ul;

            public string UnavailableReason { get; set; } = string.Empty;

            public uint CreateSRVDescriptorCallCountThisFrame { get; private set; }

            public List<DescriptorWrite> DescriptorWrites { get; } = new();

            public void ResetPerFrameStats()
            {
                CreateSRVDescriptorCallCountThisFrame = 0;
            }

            public bool TryCreateTextureDescriptor(Texture texture, uint index)
            {
                CreateSRVDescriptorCallCountThisFrame++;
                DescriptorWrites.Add(new DescriptorWrite(index, texture));
                return true;
            }
        }

        private readonly struct DescriptorWrite
        {
            public DescriptorWrite(uint index, Texture texture)
            {
                Index = index;
                Texture = texture;
            }

            public uint Index { get; }

            public Texture Texture { get; }
        }
    }
}
