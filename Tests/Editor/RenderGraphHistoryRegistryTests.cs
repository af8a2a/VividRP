using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderGraphHistoryRegistryTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RTHandles.Initialize(1, 1);
        }

        [SetUp]
        public void SetUp()
        {
            RenderGraphHistoryRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            RenderGraphHistoryRegistry.Clear();
        }

        [Test]
        public void GetOrCreateHistoryTarget_ReusesHandle_WhenDescriptorMatches()
        {
            using var cameraScope = new CameraScope();
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();

            try
            {
                var descriptor = new RenderGraphTextureDesc
                {
                    Width = 64,
                    Height = 32,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    Name = "HistoryA",
                };

                var first = RenderGraphHistoryRegistry.GetOrCreateHistoryTarget(cameraScope.Camera, graphAsset, 0, descriptor);
                var second = RenderGraphHistoryRegistry.GetOrCreateHistoryTarget(cameraScope.Camera, graphAsset, 0, descriptor);

                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.SameAs(first));
                Assert.That(RenderGraphHistoryRegistry.TryGetHistoryTarget(cameraScope.Camera, graphAsset, 0, out var handle, out var hasValidData), Is.True);
                Assert.That(handle, Is.SameAs(first));
                Assert.That(hasValidData, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void GetOrCreateHistoryTarget_RecreatesHandle_WhenDescriptorChanges()
        {
            using var cameraScope = new CameraScope();
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();

            try
            {
                var firstDescriptor = new RenderGraphTextureDesc
                {
                    Width = 64,
                    Height = 32,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    Name = "HistoryA",
                };
                var secondDescriptor = new RenderGraphTextureDesc
                {
                    Width = 128,
                    Height = 64,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    Name = "HistoryA",
                };

                var first = RenderGraphHistoryRegistry.GetOrCreateHistoryTarget(cameraScope.Camera, graphAsset, 0, firstDescriptor);
                var second = RenderGraphHistoryRegistry.GetOrCreateHistoryTarget(cameraScope.Camera, graphAsset, 0, secondDescriptor);

                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.Not.Null);
                Assert.That(second, Is.Not.SameAs(first));
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void MarkHistoryValid_UpdatesValidityState()
        {
            using var cameraScope = new CameraScope();
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();

            try
            {
                var descriptor = new RenderGraphTextureDesc
                {
                    Width = 32,
                    Height = 32,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    Name = "HistoryA",
                };

                RenderGraphHistoryRegistry.GetOrCreateHistoryTarget(cameraScope.Camera, graphAsset, 0, descriptor);
                RenderGraphHistoryRegistry.MarkHistoryValid(cameraScope.Camera, graphAsset, 0);

                var found = RenderGraphHistoryRegistry.TryGetHistoryTarget(cameraScope.Camera, graphAsset, 0, out _, out var hasValidData);

                Assert.That(found, Is.True);
                Assert.That(hasValidData, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void BuildKey_UsesCameraAndGraphAssetEntityIds()
        {
            using var cameraScope = new CameraScope();
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();

            try
            {
                var buildKeyMethod = typeof(RenderGraphHistoryRegistry).GetMethod("BuildKey", BindingFlags.Static | BindingFlags.NonPublic);

                Assert.That(buildKeyMethod, Is.Not.Null);

                var key = (string)buildKeyMethod.Invoke(null, new object[] { cameraScope.Camera, graphAsset, 3 });

                Assert.That(key, Is.EqualTo($"{cameraScope.Camera.GetEntityId()}|{graphAsset.GetEntityId()}|3"));
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        private sealed class CameraScope : System.IDisposable
        {
            private readonly GameObject m_GameObject;

            internal CameraScope()
            {
                m_GameObject = new GameObject("HistoryRegistryCamera");
                Camera = m_GameObject.AddComponent<Camera>();
            }

            internal Camera Camera { get; }

            public void Dispose()
            {
                if (m_GameObject != null)
                    Object.DestroyImmediate(m_GameObject);
            }
        }
    }
}
