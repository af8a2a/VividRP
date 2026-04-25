using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.PackageManager;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class FilmGrainTests
    {
        [Test]
        public void FilmGrain_IsInactive_WhenIntensityIsZero()
        {
            var filmGrain = new FilmGrain();
            filmGrain.intensity.value = 0f;

            Assert.That(filmGrain.IsActive(), Is.False);
        }

        [Test]
        public void FilmGrain_IsActive_WhenIntensityPositiveAndPresetType()
        {
            var filmGrain = new FilmGrain();
            filmGrain.intensity.value = 0.5f;
            filmGrain.type.value = FilmGrainLookup.Thin1;

            Assert.That(filmGrain.IsActive(), Is.True);
        }

        [Test]
        public void FilmGrain_IsInactive_WhenCustomTypeAndNoTexture()
        {
            var filmGrain = new FilmGrain();
            filmGrain.intensity.value = 0.5f;
            filmGrain.type.value = FilmGrainLookup.Custom;
            filmGrain.texture.value = null;

            Assert.That(filmGrain.IsActive(), Is.False);
        }

        [Test]
        public void FilmGrain_IsActive_WhenCustomTypeAndHasTexture()
        {
            var filmGrain = new FilmGrain();
            filmGrain.intensity.value = 0.5f;
            filmGrain.type.value = FilmGrainLookup.Custom;
            filmGrain.texture.value = Texture2D.whiteTexture;

            Assert.That(filmGrain.IsActive(), Is.True);
        }

        [Test]
        public void FilmGrain_IsCustomTextureMode_ReturnsTrueOnlyForCustom()
        {
            var filmGrain = new FilmGrain();

            filmGrain.type.value = FilmGrainLookup.Custom;
            Assert.That(filmGrain.IsCustomTextureMode(), Is.True);

            filmGrain.type.value = FilmGrainLookup.Medium3;
            Assert.That(filmGrain.IsCustomTextureMode(), Is.False);
        }

        [Test]
        public void FilmGrainSettingsData_CreateDefault_ReturnsDisabledState()
        {
            var data = FilmGrainSettingsData.CreateDefault();

            Assert.That(data.enabled, Is.False);
            Assert.That(data.texture, Is.Null);
            Assert.That(data.intensity, Is.EqualTo(0f));
            Assert.That(data.response, Is.EqualTo(0f));
        }

        [Test]
        public void FilmGrainRuntimeUtility_CreateMaterialParams_MatchesHdrpIntensityScale()
        {
            var data = new FilmGrainSettingsData
            {
                intensity = 0.25f,
                response = 0.75f
            };

            var materialParams = FilmGrainRuntimeUtility.CreateMaterialParams(data);

            Assert.That(materialParams.x, Is.EqualTo(1f));
            Assert.That(materialParams.y, Is.EqualTo(0.75f));
            Assert.That(materialParams.z, Is.EqualTo(0f));
            Assert.That(materialParams.w, Is.EqualTo(0f));
        }

        [Test]
        public void FinalBlitShader_UsesHdrpFilmGrainSamplingAndResponse()
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(FilmGrain).Assembly);
            Assert.That(packageInfo, Is.Not.Null);

            var shaderPath = Path.Combine(
                packageInfo.resolvedPath,
                "Shaders",
                "Core",
                "Private",
                "FinalBlit.shader");
            var shaderSource = File.ReadAllText(shaderPath);

            StringAssert.Contains(
                "SAMPLE_TEXTURE2D(_VividFilmGrainTexture, s_linear_repeat_sampler, grainUV).w",
                shaderSource);
            StringAssert.Contains("postProcessed = saturate(postProcessed);", shaderSource);
            StringAssert.Contains("lum = 1.0 - sqrt(lum);", shaderSource);
            StringAssert.Contains("lum = lerp(1.0, lum, _VividFilmGrainParams.y);", shaderSource);

            var bloomIndex = shaderSource.IndexOf("postProcessed += bloom;", System.StringComparison.Ordinal);
            var grainIndex = shaderSource.IndexOf(
                "SAMPLE_TEXTURE2D(_VividFilmGrainTexture, s_linear_repeat_sampler, grainUV).w",
                System.StringComparison.Ordinal);

            Assert.That(bloomIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(grainIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(bloomIndex, Is.LessThan(grainIndex));
        }
    }
}
