using NUnit.Framework;
using UnityEngine;
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
    }
}
