using UnityEngine;

namespace VividRP.Runtime
{
    internal readonly struct DDGIProfile
    {
        public DDGIProfile(
            DDGIProfileId id,
            int raysPerProbe,
            int irradianceTexelCount,
            int irradianceInteriorTexelCount,
            int distanceTexelCount,
            int distanceInteriorTexelCount,
            float hysteresis,
            float distanceExponent,
            float irradianceEncodingGamma,
            float irradianceThreshold,
            float brightnessThreshold,
            float randomBackfaceThreshold,
            float fixedBackfaceThreshold,
            float minFrontfaceDistance)
        {
            Id = id;
            RaysPerProbe = raysPerProbe;
            IrradianceTexelCount = irradianceTexelCount;
            IrradianceInteriorTexelCount = irradianceInteriorTexelCount;
            DistanceTexelCount = distanceTexelCount;
            DistanceInteriorTexelCount = distanceInteriorTexelCount;
            Hysteresis = hysteresis;
            DistanceExponent = distanceExponent;
            IrradianceEncodingGamma = irradianceEncodingGamma;
            IrradianceThreshold = irradianceThreshold;
            BrightnessThreshold = brightnessThreshold;
            RandomBackfaceThreshold = randomBackfaceThreshold;
            FixedBackfaceThreshold = fixedBackfaceThreshold;
            MinFrontfaceDistance = minFrontfaceDistance;
        }

        public DDGIProfileId Id { get; }

        public int RaysPerProbe { get; }

        public int IrradianceTexelCount { get; }

        public int IrradianceInteriorTexelCount { get; }

        public int DistanceTexelCount { get; }

        public int DistanceInteriorTexelCount { get; }

        public float Hysteresis { get; }

        public float DistanceExponent { get; }

        public float IrradianceEncodingGamma { get; }

        public float IrradianceThreshold { get; }

        public float BrightnessThreshold { get; }

        public float RandomBackfaceThreshold { get; }

        public float FixedBackfaceThreshold { get; }

        public float MinFrontfaceDistance { get; }
    }

    internal static class DDGIProfileTable
    {
        private static readonly DDGIProfile s_Balanced = new(
            DDGIProfileId.Balanced,
            raysPerProbe: 144,
            irradianceTexelCount: 8,
            irradianceInteriorTexelCount: 6,
            distanceTexelCount: 16,
            distanceInteriorTexelCount: 14,
            hysteresis: 0.97f,
            distanceExponent: 50.0f,
            irradianceEncodingGamma: 5.0f,
            irradianceThreshold: 0.2f,
            brightnessThreshold: 1.0f,
            randomBackfaceThreshold: 0.1f,
            fixedBackfaceThreshold: 0.25f,
            minFrontfaceDistance: 1.0f);

        public static DDGIProfile GetProfile(DDGIProfileId profileId)
        {
            return profileId switch
            {
                DDGIProfileId.Balanced => s_Balanced,
                _ => s_Balanced,
            };
        }

        public static Vector3 SanitizeProbeSpacing(Vector3 spacing)
        {
            const float minSpacing = 0.01f;
            return new Vector3(
                Mathf.Max(spacing.x, minSpacing),
                Mathf.Max(spacing.y, minSpacing),
                Mathf.Max(spacing.z, minSpacing));
        }
    }
}
