using System.Reflection;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools.Utils;
using Object = UnityEngine.Object;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.Tests
{
    public class MeshletRendererGizmoUtilityTests
    {
        [Test]
        public void TryGetLocalSelectionBounds_UsesSourceMeshBounds_WhenSourceMeshExists()
        {
            var gameObject = new GameObject("MeshletRenderer_GizmoBounds");
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            Mesh mesh = CreateOffsetMesh("MeshletRenderer_GizmoBoundsMesh");
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();

            try
            {
                meshFilter.sharedMesh = mesh;
                meshletRenderer.CaptureSourceFromRenderer(meshRenderer);

                bool hasBounds = MeshletRendererGizmoUtility.TryGetLocalSelectionBounds(meshletRenderer, out Bounds bounds);

                Assert.That(hasBounds, Is.True);
                Assert.That(bounds.center, Is.EqualTo(mesh.bounds.center).Using(Vector3EqualityComparer.Instance));
                Assert.That(bounds.size, Is.EqualTo(mesh.bounds.size).Using(Vector3EqualityComparer.Instance));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TryGetLocalSelectionBounds_FallsBackToMeshletCollectionBounds_WhenSourceMeshIsMissing()
        {
            var gameObject = new GameObject("MeshletRenderer_GizmoBoundsFallback");
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            var collectionA = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            var collectionB = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();

            try
            {
                collectionA.Bounds = new Bounds(new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(2.0f, 2.0f, 2.0f));
                collectionB.Bounds = new Bounds(new Vector3(3.0f, 1.0f, 0.0f), new Vector3(4.0f, 2.0f, 2.0f));

                typeof(MeshletRenderer)
                    .GetField("m_MeshletCollections", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(meshletRenderer, new[] { collectionA, collectionB });

                bool hasBounds = MeshletRendererGizmoUtility.TryGetLocalSelectionBounds(meshletRenderer, out Bounds bounds);

                Assert.That(hasBounds, Is.True);
                Assert.That(bounds.center, Is.EqualTo(new Vector3(1.5f, 0.5f, 0.0f)).Using(Vector3EqualityComparer.Instance));
                Assert.That(bounds.size, Is.EqualTo(new Vector3(7.0f, 3.0f, 2.0f)).Using(Vector3EqualityComparer.Instance));
            }
            finally
            {
                Object.DestroyImmediate(collectionA);
                Object.DestroyImmediate(collectionB);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void GetLocalMeshletBounds_UsesBoundingSphereCenterAndRadius()
        {
            var meshlet = new VividMeshlet
            {
                BoundingSphere = new float4(1.0f, 2.0f, 3.0f, 4.0f),
            };

            Bounds bounds = MeshletRendererGizmoUtility.GetLocalMeshletBounds(meshlet);

            Assert.That(bounds.center, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)).Using(Vector3EqualityComparer.Instance));
            Assert.That(bounds.size, Is.EqualTo(new Vector3(8.0f, 8.0f, 8.0f)).Using(Vector3EqualityComparer.Instance));
        }

        private static Mesh CreateOffsetMesh(string meshName)
        {
            var mesh = new Mesh
            {
                name = meshName,
            };

            mesh.vertices = new[]
            {
                new Vector3(2.0f, 1.0f, -1.0f),
                new Vector3(4.0f, 1.0f, -1.0f),
                new Vector3(2.0f, 5.0f, -1.0f),
                new Vector3(4.0f, 5.0f, -1.0f),
            };
            mesh.normals = new[]
            {
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
            };
            mesh.uv = new[]
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 1.0f),
            };
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
