using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven.Bindless;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class DDGISceneCacheBuilderTests
    {
        [Test]
        public void Build_CollectsOnlySupportedOpaqueRenderersInsideExpandedBounds()
        {
            var volumeObject = new GameObject("DDGI Scene Cache Volume");
            var volume = volumeObject.AddComponent<DDGIVolume>();
            var createdObjects = new List<Object>();

            try
            {
                volume.SetBoundProxyShape(new BoundProxyShape
                {
                    shape = BoundProxyShapeType.Box,
                    size = new Vector3(8.0f, 8.0f, 8.0f),
                });
                SetProbeMaxRayDistance(volume, 1.0f);

                Texture2D baseTexture = new Texture2D(2, 2);
                createdObjects.Add(baseTexture);

                Material supportedMaterial = CreateSupportedMaterial(baseTexture);
                Material transparentMaterial = CreateSupportedMaterial(null);
                transparentMaterial.renderQueue = (int)RenderQueue.Transparent;
                Material alphaClipMaterial = CreateSupportedMaterial(null);
                if (alphaClipMaterial.HasProperty("_Cutoff"))
                {
                    alphaClipMaterial.SetFloat("_Cutoff", 0.5f);
                }
                else
                {
                    alphaClipMaterial.EnableKeyword("_ALPHATEST_ON");
                }

                createdObjects.Add(supportedMaterial);
                createdObjects.Add(transparentMaterial);
                createdObjects.Add(alphaClipMaterial);

                MeshRenderer supportedRenderer = CreateRenderer("Supported", Vector3.zero, supportedMaterial, createdObjects);
                CreateRenderer("Transparent", new Vector3(1.0f, 0.0f, 0.0f), transparentMaterial, createdObjects);
                CreateRenderer("AlphaClip", new Vector3(-1.0f, 0.0f, 0.0f), alphaClipMaterial, createdObjects);
                CreateRenderer("Outside", new Vector3(20.0f, 0.0f, 0.0f), supportedMaterial, createdObjects);

                using var bindlessTextures = new BindlessTextureContainer(new FakeBindlessTextureDescriptorAllocator(16));
                var builder = new DDGISceneCacheBuilder();
                var sceneCache = new DDGISceneCache();

                bool changed = builder.Build(volume, bindlessTextures, sceneCache);

                Assert.That(changed, Is.True);
                Assert.That(sceneCache.Renderers, Has.Count.EqualTo(1));
                Assert.That(sceneCache.Renderers[0], Is.SameAs(supportedRenderer));
                Assert.That(sceneCache.Instances, Has.Count.EqualTo(1));
                Assert.That(sceneCache.SubMeshes, Has.Count.EqualTo(1));
                Assert.That(sceneCache.Materials, Has.Count.EqualTo(1));
                Assert.That(sceneCache.Vertices.Count, Is.EqualTo(3));
                Assert.That(sceneCache.Indices.Count, Is.EqualTo(3));
                Assert.That(sceneCache.Materials[0].BaseMapIndex, Is.Not.EqualTo(DDGIMaterialData.InvalidTextureIndex));
            }
            finally
            {
                for (int index = createdObjects.Count - 1; index >= 0; index--)
                {
                    if (createdObjects[index] != null)
                    {
                        Object.DestroyImmediate(createdObjects[index]);
                    }
                }

                Object.DestroyImmediate(volumeObject);
            }
        }

        [Test]
        public void Build_UsesStableRendererOrderingAcrossUnchangedFrames()
        {
            var volumeObject = new GameObject("DDGI Scene Cache Stable Order Volume");
            var volume = volumeObject.AddComponent<DDGIVolume>();
            var createdObjects = new List<Object>();

            try
            {
                volume.SetBoundProxyShape(new BoundProxyShape
                {
                    shape = BoundProxyShapeType.Box,
                    size = new Vector3(12.0f, 12.0f, 12.0f),
                });

                Material material = CreateSupportedMaterial(null);
                createdObjects.Add(material);

                MeshRenderer firstRenderer = CreateRenderer("First", new Vector3(-2.0f, 0.0f, 0.0f), material, createdObjects);
                MeshRenderer secondRenderer = CreateRenderer("Second", new Vector3(2.0f, 0.0f, 0.0f), material, createdObjects);

                using var bindlessTextures = new BindlessTextureContainer(new FakeBindlessTextureDescriptorAllocator(16));
                var builder = new DDGISceneCacheBuilder();
                var sceneCache = new DDGISceneCache();

                bool firstBuildChanged = builder.Build(volume, bindlessTextures, sceneCache);
                EntityId[] firstOrder =
                {
                    sceneCache.Renderers[0].GetEntityId(),
                    sceneCache.Renderers[1].GetEntityId(),
                };
                int firstHash = sceneCache.SceneHash;

                bool secondBuildChanged = builder.Build(volume, bindlessTextures, sceneCache);
                EntityId[] secondOrder =
                {
                    sceneCache.Renderers[0].GetEntityId(),
                    sceneCache.Renderers[1].GetEntityId(),
                };

                Assert.That(firstBuildChanged, Is.True);
                Assert.That(secondBuildChanged, Is.False);
                Assert.That(sceneCache.SceneHash, Is.EqualTo(firstHash));
                CollectionAssert.AreEqual(firstOrder, secondOrder);
                CollectionAssert.AreEquivalent(
                    new[] { firstRenderer.GetEntityId(), secondRenderer.GetEntityId() },
                    secondOrder);
            }
            finally
            {
                for (int index = createdObjects.Count - 1; index >= 0; index--)
                {
                    if (createdObjects[index] != null)
                    {
                        Object.DestroyImmediate(createdObjects[index]);
                    }
                }

                Object.DestroyImmediate(volumeObject);
            }
        }

        private static void SetProbeMaxRayDistance(DDGIVolume volume, float value)
        {
            var serializedObject = new SerializedObject(volume);
            serializedObject.Update();
            serializedObject.FindProperty("m_ProbeMaxRayDistance").floatValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            volume.SendMessage("OnValidate");
        }

        private static MeshRenderer CreateRenderer(string name, Vector3 position, Material material, List<Object> createdObjects)
        {
            var gameObject = new GameObject(name);
            createdObjects.Add(gameObject);
            gameObject.transform.position = position;

            var meshFilter = gameObject.AddComponent<MeshFilter>();
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            Mesh mesh = CreateTriangleMesh();
            createdObjects.Add(mesh);
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = material;
            return meshRenderer;
        }

        private static Mesh CreateTriangleMesh()
        {
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, 0.0f, 0.0f),
                    new Vector3(0.5f, 0.0f, 0.0f),
                    new Vector3(0.0f, 1.0f, 0.0f),
                },
                normals = new[]
                {
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward,
                },
                uv = new[]
                {
                    new Vector2(0.0f, 0.0f),
                    new Vector2(1.0f, 0.0f),
                    new Vector2(0.5f, 1.0f),
                },
            };
            mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateSupportedMaterial(Texture texture)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null, "Expected a built-in lit shader for DDGI scene-cache tests.");

            var material = new Material(shader);
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", new Color(0.7f, 0.8f, 0.9f, 1.0f));
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", new Color(0.7f, 0.8f, 0.9f, 1.0f));
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0.15f);
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", new Color(0.1f, 0.05f, 0.02f, 1.0f));
            }

            if (texture != null)
            {
                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", texture);
                }

                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", texture);
                }
            }

            return material;
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

            public string UnavailableReason { get; set; } = string.Empty;

            public uint CreateSRVDescriptorCallCountThisFrame { get; private set; }

            public bool TryCreateTextureDescriptor(Texture texture, uint index)
            {
                CreateSRVDescriptorCallCountThisFrame++;
                return true;
            }

            public void ResetPerFrameStats()
            {
                CreateSRVDescriptorCallCountThisFrame = 0;
            }
        }
    }
}
