using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class SkyAmbientProbeConvolutionTests
    {

        [Test]
        public void SkyManager_HasValidSkyTexture_RequiresCreatedCubemapRenderTexture()
        {
            var method = typeof(SkyManager).GetMethod("HasValidSkyTexture", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var texture2D = new Texture2D(4, 4);
            var cubemap = new RenderTexture(16, 16, 0)
            {
                dimension = TextureDimension.Cube,
                volumeDepth = 6
            };

            try
            {
                Assert.That(InvokeHasValidSkyTexture(method, null), Is.False);
                Assert.That(InvokeHasValidSkyTexture(method, texture2D), Is.False);
                Assert.That(InvokeHasValidSkyTexture(method, cubemap), Is.False);

                cubemap.Create();

                Assert.That(InvokeHasValidSkyTexture(method, cubemap), Is.True);
            }
            finally
            {
                cubemap.Release();
                Object.DestroyImmediate(cubemap);
                Object.DestroyImmediate(texture2D);
            }
        }

        private static bool InvokeHasValidSkyTexture(MethodInfo method, Texture texture)
        {
            return (bool)method.Invoke(null, new object[] { texture });
        }
    }
}
