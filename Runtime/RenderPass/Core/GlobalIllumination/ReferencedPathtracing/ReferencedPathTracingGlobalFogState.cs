using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core
{
    internal readonly struct ReferencedPathTracingGlobalFogState
        : System.IEquatable<ReferencedPathTracingGlobalFogState>
    {
        internal const int ContractVersion = 1;

        internal ReferencedPathTracingGlobalFogState(
            bool enabled,
            Vector3 scatteringAlbedo,
            float extinction,
            float baseHeight,
            float reciprocalScaleHeight,
            float maxDistance,
            float anisotropy,
            bool directionalLightsOnly,
            float globalLightProbeDimmer)
        {
            this.enabled = enabled;
            this.scatteringAlbedo = scatteringAlbedo;
            this.extinction = extinction;
            this.baseHeight = baseHeight;
            this.reciprocalScaleHeight = reciprocalScaleHeight;
            this.maxDistance = maxDistance;
            this.anisotropy = anisotropy;
            this.directionalLightsOnly = directionalLightsOnly;
            this.globalLightProbeDimmer = globalLightProbeDimmer;
            signature = 0ul;
            signature = ComputeSignature(this);
        }

        internal bool enabled { get; }
        internal Vector3 scatteringAlbedo { get; }
        internal float extinction { get; }
        internal float baseHeight { get; }
        internal float reciprocalScaleHeight { get; }
        internal float maxDistance { get; }
        internal float anisotropy { get; }
        internal bool directionalLightsOnly { get; }
        internal float globalLightProbeDimmer { get; }
        internal ulong signature { get; }

        internal static ReferencedPathTracingGlobalFogState Disabled =>
            new(
                false,
                Vector3.zero,
                0.0f,
                0.0f,
                1.0f,
                0.0f,
                0.0f,
                false,
                0.0f);

        internal static ReferencedPathTracingGlobalFogState Resolve(
            VividVolumetricFogVolume volume = null)
        {
            volume ??=
                VividVolumeManagerUtility.GetVolumetricFogVolume();
            if (volume == null || !volume.IsActive())
                return Disabled;

            var extinction = SanitizeNonNegative(volume.GetExtinction());
            var maxDistance =
                SanitizeNonNegative(volume.maxFogDistance.value);
            var enabled = extinction > 0.0f && maxDistance > 0.0f;
            if (!enabled)
                return Disabled;

            var albedo = volume.albedo.value;
            var scatteringAlbedo = new Vector3(
                SanitizeUnit(albedo.r),
                SanitizeUnit(albedo.g),
                SanitizeUnit(albedo.b));
            var baseHeight = SanitizeFinite(volume.baseHeight.value);
            var maximumHeight = Mathf.Max(
                SanitizeFinite(volume.maximumHeight.value),
                baseHeight + 0.01f);
            var scaleHeight =
                VividVolumetricUtility.ComputeHeightFogScaleHeight(
                    baseHeight,
                    maximumHeight);

            return new ReferencedPathTracingGlobalFogState(
                true,
                scatteringAlbedo,
                extinction,
                baseHeight,
                1.0f / Mathf.Max(scaleHeight, 0.0001f),
                maxDistance,
                Mathf.Clamp(
                    SanitizeFinite(volume.anisotropy.value),
                    -0.95f,
                    0.95f),
                volume.directionalLightsOnly.value,
                Mathf.Clamp01(
                    SanitizeFinite(
                        volume.globalLightProbeDimmer.value)));
        }

        public bool Equals(ReferencedPathTracingGlobalFogState other)
        {
            return signature == other.signature;
        }

        public override bool Equals(object obj)
        {
            return obj is ReferencedPathTracingGlobalFogState other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            return signature.GetHashCode();
        }

        private static ulong ComputeSignature(
            ReferencedPathTracingGlobalFogState state)
        {
            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ContractVersion);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                state.enabled);
            if (!state.enabled)
                return hash;

            AddVector(ref hash, state.scatteringAlbedo);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                state.extinction);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                state.baseHeight);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                state.reciprocalScaleHeight);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                state.maxDistance);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                state.anisotropy);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                state.directionalLightsOnly);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                state.globalLightProbeDimmer);
            return hash;
        }

        private static void AddVector(ref ulong hash, Vector3 value)
        {
            ReferencedPathTracingStableHash.Add(ref hash, value.x);
            ReferencedPathTracingStableHash.Add(ref hash, value.y);
            ReferencedPathTracingStableHash.Add(ref hash, value.z);
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0.0f
                : value;
        }

        private static float SanitizeNonNegative(float value)
        {
            return Mathf.Max(SanitizeFinite(value), 0.0f);
        }

        private static float SanitizeUnit(float value)
        {
            return Mathf.Clamp01(SanitizeFinite(value));
        }
    }
}
