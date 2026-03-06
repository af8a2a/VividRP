using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderGraphPreviewRegistryTests
    {
        private sealed class PreviewTestPass : RasterPass
        {
            public override void Create()
            {
            }

            public override void Prepare(ContextContainer frameData)
            {
            }

            public override void Record(RasterGraphContext context)
            {
            }

            public override void Dispose()
            {
            }
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RTHandles.Initialize(1, 1);
        }

        [SetUp]
        public void SetUp()
        {
            RenderGraphPreviewRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            RenderGraphPreviewRegistry.Clear();
        }

        [Test]
        public void SetPreview_RegistersPreviewTexture_ForPassField()
        {
            var texture = new Texture2D(4, 4);

            try
            {
                RenderGraphPreviewRegistry.SetPreview(typeof(PreviewTestPass), "Color", texture);

                var found = RenderGraphPreviewRegistry.TryGetPreview(typeof(PreviewTestPass), "Color", out var previewTexture);

                Assert.That(found, Is.True);
                Assert.That(previewTexture, Is.SameAs(texture));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void Clear_RemovesRegisteredPreviewTextures()
        {
            var texture = new Texture2D(4, 4);

            try
            {
                RenderGraphPreviewRegistry.SetPreview(typeof(PreviewTestPass), "Color", texture);
                RenderGraphPreviewRegistry.Clear();

                var found = RenderGraphPreviewRegistry.TryGetPreview(typeof(PreviewTestPass), "Color", out _);

                Assert.That(found, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void GetOrCreatePreviewTarget_ReusesHandle_WhenSourceShapeMatches()
        {
            var sourceInfo = new RenderTargetInfo
            {
                width = 64,
                height = 32,
                volumeDepth = 1,
                msaaSamples = 1,
                format = GraphicsFormat.R8G8B8A8_UNorm,
                bindMS = false,
            };
            var sourceDesc = new RenderGraphTextureDesc
            {
                Width = 64,
                Height = 32,
                ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                FilterMode = FilterMode.Point,
                WrapMode = TextureWrapMode.Clamp,
            };

            var first = RenderGraphPreviewRegistry.GetOrCreatePreviewTarget(typeof(PreviewTestPass), "Color", sourceInfo, sourceDesc);
            var second = RenderGraphPreviewRegistry.GetOrCreatePreviewTarget(typeof(PreviewTestPass), "Color", sourceInfo, sourceDesc);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first));
            Assert.That(RenderGraphPreviewRegistry.TryGetPreview(typeof(PreviewTestPass), "Color", out var previewTexture), Is.True);
            Assert.That(previewTexture, Is.SameAs(first.rt));
        }

        [Test]
        public void GetOrCreatePreviewTarget_RecreatesHandle_WhenSourceShapeChanges()
        {
            var sourceDesc = new RenderGraphTextureDesc
            {
                Width = 64,
                Height = 32,
                ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
            };
            var firstInfo = new RenderTargetInfo
            {
                width = 64,
                height = 32,
                volumeDepth = 1,
                msaaSamples = 1,
                format = GraphicsFormat.R8G8B8A8_UNorm,
                bindMS = false,
            };
            var secondInfo = new RenderTargetInfo
            {
                width = 128,
                height = 64,
                volumeDepth = 1,
                msaaSamples = 1,
                format = GraphicsFormat.R8G8B8A8_UNorm,
                bindMS = false,
            };

            var first = RenderGraphPreviewRegistry.GetOrCreatePreviewTarget(typeof(PreviewTestPass), "Color", firstInfo, sourceDesc);
            var second = RenderGraphPreviewRegistry.GetOrCreatePreviewTarget(typeof(PreviewTestPass), "Color", secondInfo, sourceDesc);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(RenderGraphPreviewRegistry.TryGetPreview(typeof(PreviewTestPass), "Color", out var previewTexture), Is.True);
            Assert.That(previewTexture, Is.SameAs(second.rt));
        }
    }
}

