using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class TextureScaleBiasUtilityTests
    {
        [Test]
        public void GetScaleBias_ReturnsIdentityScaleBias_WhenHandleIsNull()
        {
            Assert.That(
                TextureScaleBiasUtility.GetScaleBias((RTHandle)null),
                Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
        }

        [Test]
        public void GetScaleBias_ReturnsNonFlippedScaleBias_WhenUvOriginsMatch()
        {
            Assert.That(
                TextureScaleBiasUtility.GetScaleBias(
                    new Vector2(0.5f, 0.25f),
                    TextureUVOrigin.TopLeft,
                    TextureUVOrigin.TopLeft),
                Is.EqualTo(new Vector4(0.5f, 0.25f, 0f, 0f)));
        }

        [Test]
        public void GetScaleBias_ReturnsFlippedScaleBias_WhenUvOriginsDiffer()
        {
            Assert.That(
                TextureScaleBiasUtility.GetScaleBias(
                    new Vector2(0.5f, 0.25f),
                    TextureUVOrigin.TopLeft,
                    TextureUVOrigin.BottomLeft),
                Is.EqualTo(new Vector4(0.5f, -0.25f, 0f, 0.25f)));
        }

        [Test]
        public void GetScaleBias_ReturnsFlippedIdentityScaleBias_WhenHandleIsNullAndUvOriginsDiffer()
        {
            Assert.That(
                TextureScaleBiasUtility.GetScaleBias(
                    (RTHandle)null,
                    TextureUVOrigin.TopLeft,
                    TextureUVOrigin.BottomLeft),
                Is.EqualTo(new Vector4(1f, -1f, 0f, 1f)));
        }
    }
}
