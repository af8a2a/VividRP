using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class SkyDiffuseSHUtilityTests
    {
        [Test]
        public void ProjectCubemapToSH_ReturnsDominantL0_WhenCubemapIsUniform()
        {
            var cubemap = CreateSolidCubemap(new Color(0.5f, 0.25f, 0.125f, 1.0f));

            try
            {
                var projected = SkyDiffuseSHUtility.TryProjectCubemapToSH(cubemap, Color.white, 1.0f, 0.0f, out var sh);

                Assert.That(projected, Is.True);
                Assert.That(sh[0, 0], Is.GreaterThan(0.0f));
                Assert.That(Mathf.Abs(sh[0, 1]), Is.LessThan(1e-3f));
                Assert.That(Mathf.Abs(sh[0, 2]), Is.LessThan(1e-3f));
                Assert.That(Mathf.Abs(sh[0, 3]), Is.LessThan(1e-3f));
                Assert.That(Mathf.Abs(sh[0, 4]), Is.LessThan(1e-3f));
                Assert.That(Mathf.Abs(sh[0, 5]), Is.LessThan(1e-3f));
                Assert.That(Mathf.Abs(sh[0, 6]), Is.LessThan(1e-3f));
                Assert.That(Mathf.Abs(sh[0, 7]), Is.LessThan(1e-3f));
                Assert.That(Mathf.Abs(sh[0, 8]), Is.LessThan(1e-3f));
            }
            finally
            {
                Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void ProjectCubemapToSH_ScalesL0_WhenExposureChanges()
        {
            var cubemap = CreateSolidCubemap(Color.white);

            try
            {
                SkyDiffuseSHUtility.TryProjectCubemapToSH(cubemap, Color.white, 1.0f, 0.0f, out var lowExposureSh);
                SkyDiffuseSHUtility.TryProjectCubemapToSH(cubemap, Color.white, 2.0f, 0.0f, out var highExposureSh);

                Assert.That(highExposureSh[0, 0], Is.EqualTo(lowExposureSh[0, 0] * 2.0f).Within(1e-3f));
                Assert.That(highExposureSh[1, 0], Is.EqualTo(lowExposureSh[1, 0] * 2.0f).Within(1e-3f));
                Assert.That(highExposureSh[2, 0], Is.EqualTo(lowExposureSh[2, 0] * 2.0f).Within(1e-3f));
            }
            finally
            {
                Object.DestroyImmediate(cubemap);
            }
        }

        private static Cubemap CreateSolidCubemap(Color color)
        {
            var cubemap = new Cubemap(4, TextureFormat.RGBA32, false);
            var colors = new Color[16];
            for (var index = 0; index < colors.Length; index++)
                colors[index] = color;
            cubemap.SetPixels(colors, CubemapFace.PositiveX);
            cubemap.SetPixels(colors, CubemapFace.NegativeX);
            cubemap.SetPixels(colors, CubemapFace.PositiveY);
            cubemap.SetPixels(colors, CubemapFace.NegativeY);
            cubemap.SetPixels(colors, CubemapFace.PositiveZ);
            cubemap.SetPixels(colors, CubemapFace.NegativeZ);
            cubemap.Apply(false, false);
            return cubemap;
        }
    }
}
