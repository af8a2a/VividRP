using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class TextureResolveUtilityTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RTHandles.Initialize(1, 1);
        }

        [Test]
        public void ResolveTexture_ReturnsNull_WhenHandleIsNull()
        {
            Assert.That(TextureResolveUtility.ResolveTexture((RTHandle)null), Is.Null);
        }

        [Test]
        public void ResolveTexture_ReturnsOwnedRenderTexture_WhenHandleHasRenderTexture()
        {
            RTHandle handle = null;

            try
            {
                handle = RTHandles.Alloc(
                    4,
                    4,
                    colorFormat: GraphicsFormat.R8G8B8A8_UNorm,
                    name: "ResolveTextureTestHandle");

                Assert.That(TextureResolveUtility.ResolveTexture(handle), Is.SameAs(handle.rt));
            }
            finally
            {
                handle?.Release();
            }
        }

        [Test]
        public void ResolveTexture_ReturnsExternalTexture_WhenHandleWrapsExternalTexture()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                name = "ResolveTextureTestExternal"
            };
            RTHandle handle = null;

            try
            {
                handle = RTHandles.Alloc(texture);

                Assert.That(TextureResolveUtility.ResolveTexture(handle), Is.SameAs(texture));
            }
            finally
            {
                handle?.Release();
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ResolveTexture_ReturnsNull_WhenRenderGraphTextureIsNull()
        {
            Assert.That(TextureResolveUtility.ResolveTexture((RenderGraphTexture)null), Is.Null);
        }
    }
}
