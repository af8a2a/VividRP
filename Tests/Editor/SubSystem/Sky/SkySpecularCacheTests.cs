using NUnit.Framework;

namespace VividRP.Editor.Tests
{
    public class SkySpecularCacheTests
    {

        [Test]
        public void SkyTextureContentHash_IsStableAndTracksTextureShape()
        {
            var first = new UnityEngine.Cubemap(
                4,
                UnityEngine.TextureFormat.RGBAHalf,
                true);
            var second = new UnityEngine.Cubemap(
                8,
                UnityEngine.TextureFormat.RGBAHalf,
                true);

            try
            {
                var firstHash = VividRP.Runtime.SkyManager.GetSkyTextureContentHash(first);
                var unchangedHash =
                    VividRP.Runtime.SkyManager.GetSkyTextureContentHash(first);
                var secondHash =
                    VividRP.Runtime.SkyManager.GetSkyTextureContentHash(second);

                Assert.That(unchangedHash, Is.EqualTo(firstHash));
                Assert.That(secondHash, Is.Not.EqualTo(firstHash));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(first);
            }
        }
    }
}
