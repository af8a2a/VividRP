using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct FilmGrainSettingsData
    {
        public bool enabled;
        public Texture texture;
        public float intensity;
        public float response;

        public static FilmGrainSettingsData CreateDefault()
        {
            return new FilmGrainSettingsData
            {
                enabled = false,
                texture = null,
                intensity = 0f,
                response = 0f
            };
        }
    }

    internal static class FilmGrainSettingsResolver
    {
        internal static FilmGrainSettingsData Resolve()
        {
            var settings = FilmGrainSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var filmGrain = stack.GetComponent<FilmGrain>();
            if (filmGrain == null || !filmGrain.IsActive())
                return settings;

            var texture = ResolveTexture(filmGrain);
            if (texture == null)
                return settings;

            settings.enabled = true;
            settings.texture = texture;
            settings.intensity = filmGrain.intensity.value;
            settings.response = filmGrain.response.value;
            return settings;
        }

        internal static Texture ResolveTexture(FilmGrain filmGrain)
        {
            if (filmGrain == null)
                return null;

            if (filmGrain.IsCustomTextureMode())
                return filmGrain.texture.value;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (resources == null)
                return null;

            return filmGrain.type.value switch
            {
                FilmGrainLookup.Thin1 => resources.FilmGrainThin1,
                FilmGrainLookup.Thin2 => resources.FilmGrainThin2,
                FilmGrainLookup.Medium1 => resources.FilmGrainMedium1,
                FilmGrainLookup.Medium2 => resources.FilmGrainMedium2,
                FilmGrainLookup.Medium3 => resources.FilmGrainMedium3,
                FilmGrainLookup.Medium4 => resources.FilmGrainMedium4,
                FilmGrainLookup.Medium5 => resources.FilmGrainMedium5,
                FilmGrainLookup.Medium6 => resources.FilmGrainMedium6,
                FilmGrainLookup.Large01 => resources.FilmGrainLarge01,
                FilmGrainLookup.Large02 => resources.FilmGrainLarge02,
                _ => null
            };
        }
    }
}
