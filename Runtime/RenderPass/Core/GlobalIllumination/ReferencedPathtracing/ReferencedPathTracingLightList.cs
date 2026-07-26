using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass.Core
{
    internal enum ReferencedPathTracingLightType : uint
    {
        Invalid = 0,
        Directional = 1,
        Point = 2,
        Spot = 3,
        Rectangle = 4,
        Tube = 5,
        Disc = 6,
        Environment = 7,
        EmissiveTriangle = 8,
    }

    [Flags]
    internal enum ReferencedPathTracingLightFlags : uint
    {
        None = 0,
        Singular = 1u << 0,
        Infinite = 1u << 1,
        BsdfReachable = 1u << 2,
        OneSided = 1u << 3,
        CastsShadows = 1u << 4,
        HasStableId = 1u << 5,
        UsesAreaMeasure = 1u << 6,
        UsesLineMeasure = 1u << 7,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ReferencedPathTracingLightRecord
    {
        internal const int Stride = 144;

        internal Vector3 positionWS;
        internal float range;

        internal Vector3 forwardWS;
        internal float angularDiameter;

        internal Vector3 rightWS;
        internal float shapeRadius;

        internal Vector3 upWS;
        internal float barnDoorCosAngle;

        internal Vector3 radiometricColor;
        internal float selectionWeight;

        internal Vector2 areaSize;
        internal Vector2 spotAngleParameters;

        internal Vector2 rangeAttenuation;
        internal float barnDoorLength;
        internal float shadowStrength;

        internal float selectionPdf;
        internal float cdf;
        internal uint renderingLayerMask;
        internal uint shadowRenderingLayerMask;

        internal uint stableIdLow;
        internal uint stableIdHigh;
        internal uint lightType;
        internal uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ReferencedPathTracingLightListParameters
    {
        internal const int Stride = 48;
        internal const uint Version = 1;
        internal const uint DistributionModeCdf = 1;

        internal uint lightCount;
        internal uint activeLightCount;
        internal uint unsupportedLightCount;
        internal uint unstableLightCount;

        internal float totalSelectionWeight;
        internal float inverseTotalSelectionWeight;
        internal uint signatureLow;
        internal uint signatureHigh;

        internal uint version;
        internal uint distributionMode;
        internal uint reserved0;
        internal uint reserved1;

        internal static ReferencedPathTracingLightListParameters CreateEmpty()
        {
            return new ReferencedPathTracingLightListParameters
            {
                version = Version,
                distributionMode = DistributionModeCdf,
            };
        }
    }

    internal readonly struct ReferencedPathTracingLightListBuildResult
    {
        internal ReferencedPathTracingLightListBuildResult(
            ReferencedPathTracingLightRecord[] records,
            ReferencedPathTracingLightListParameters parameters)
        {
            this.records = records ?? Array.Empty<ReferencedPathTracingLightRecord>();
            this.parameters = parameters;
        }

        internal ReferencedPathTracingLightRecord[] records { get; }

        internal ReferencedPathTracingLightListParameters parameters { get; }
    }

    internal static class ReferencedPathTracingLightListBuilder
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        private const float DirectionEpsilon = 1e-8f;
        private const float FiniteDirectionalThreshold = 1e-6f;

        private readonly struct Candidate
        {
            internal Candidate(
                ulong stableId,
                ReferencedPathTracingLightRecord record)
            {
                this.stableId = stableId;
                this.record = record;
            }

            internal ulong stableId { get; }

            internal ReferencedPathTracingLightRecord record { get; }
        }

        internal static ReferencedPathTracingLightListBuildResult Build(
            IReadOnlyList<VividLightRenderData> sceneLights)
        {
            var candidates = new List<Candidate>(sceneLights?.Count ?? 0);
            uint unsupportedLightCount = 0;
            uint unstableLightCount = 0;

            if (sceneLights != null)
            {
                for (var lightIndex = 0; lightIndex < sceneLights.Count; lightIndex++)
                {
                    var source = sceneLights[lightIndex];
                    if (!IsEnabledAndActive(source))
                        continue;

                    var stableId = EntityId.ToULong(source.lightEntityId);
                    if (stableId == EntityId.ToULong(EntityId.None))
                    {
                        unstableLightCount++;
                        continue;
                    }

                    if (!TryCreateRecord(source, stableId, out var record))
                    {
                        unsupportedLightCount++;
                        continue;
                    }

                    candidates.Add(new Candidate(stableId, record));
                }
            }

            candidates.Sort(CompareCandidates);
            var uniqueRecords =
                new List<ReferencedPathTracingLightRecord>(candidates.Count);
            for (var candidateIndex = 0;
                 candidateIndex < candidates.Count;)
            {
                var nextCandidateIndex = candidateIndex + 1;
                while (nextCandidateIndex < candidates.Count
                    && candidates[nextCandidateIndex].stableId
                        == candidates[candidateIndex].stableId)
                {
                    nextCandidateIndex++;
                }

                var duplicateCount =
                    nextCandidateIndex - candidateIndex;
                if (duplicateCount == 1)
                    uniqueRecords.Add(candidates[candidateIndex].record);
                else
                    unstableLightCount += (uint)duplicateCount;

                candidateIndex = nextCandidateIndex;
            }

            var records = uniqueRecords.ToArray();
            double totalSelectionWeight = 0.0;
            uint activeLightCount = 0;

            for (var lightIndex = 0; lightIndex < records.Length; lightIndex++)
            {
                var record = records[lightIndex];
                if (record.selectionWeight <= 0.0f)
                    continue;

                totalSelectionWeight += record.selectionWeight;
                activeLightCount++;
            }

            var parameters =
                ReferencedPathTracingLightListParameters.CreateEmpty();
            parameters.lightCount = (uint)records.Length;
            parameters.activeLightCount = activeLightCount;
            parameters.unsupportedLightCount = unsupportedLightCount;
            parameters.unstableLightCount = unstableLightCount;

            if (totalSelectionWeight > 0.0
                && totalSelectionWeight <= float.MaxValue)
            {
                parameters.totalSelectionWeight = (float)totalSelectionWeight;
                parameters.inverseTotalSelectionWeight =
                    1.0f / parameters.totalSelectionWeight;
                AssignSelectionDistribution(records, totalSelectionWeight);
            }

            var signature = ComputeSignature(records, parameters);
            parameters.signatureLow = (uint)signature;
            parameters.signatureHigh = (uint)(signature >> 32);
            return new ReferencedPathTracingLightListBuildResult(
                records,
                parameters);
        }

        private static bool TryCreateRecord(
            VividLightRenderData source,
            ulong stableId,
            out ReferencedPathTracingLightRecord record)
        {
            record = default;
            if (!TryResolveLightType(source, out var lightType)
                || !HasFiniteColor(source.color))
            {
                return false;
            }

            var isDirectional =
                lightType == ReferencedPathTracingLightType.Directional;
            if (!isDirectional
                && (!HasFiniteVector(source.positionWS)
                    || !IsFinite(source.range)
                    || source.range <= 0.0f))
            {
                return false;
            }

            var width = SanitizeNonNegative(source.areaSize.x);
            var height = SanitizeNonNegative(source.areaSize.y);
            var shapeRadius = SanitizeNonNegative(source.shapeRadius);
            if ((lightType == ReferencedPathTracingLightType.Rectangle
                    && (width <= 0.0f || height <= 0.0f))
                || (lightType == ReferencedPathTracingLightType.Tube
                    && width <= 0.0f)
                || (lightType == ReferencedPathTracingLightType.Disc
                    && shapeRadius <= 0.0f))
            {
                return false;
            }

            if (lightType == ReferencedPathTracingLightType.Tube)
                height = 0.0f;
            else if (lightType == ReferencedPathTracingLightType.Disc)
                width = height = 2.0f * shapeRadius;

            var angularDiameter = isDirectional
                ? Mathf.Clamp(
                    SanitizeNonNegative(source.angularDiameter),
                    0.0f,
                    90.0f) * Mathf.Deg2Rad
                : 0.0f;
            var radiometricColor = new Vector3(
                Mathf.Max(source.color.x, 0.0f),
                Mathf.Max(source.color.y, 0.0f),
                Mathf.Max(source.color.z, 0.0f));
            ResolveSpotAngleParameters(
                source.lightType,
                source.innerSpotAngle,
                source.spotAngle,
                out var angleScale,
                out var angleOffset);
            ResolveRangeAttenuation(
                source,
                isDirectional,
                out var rangeAttenuationScale,
                out var rangeAttenuationBias);

            var flags = ResolveFlags(source, lightType, angularDiameter);
            record = new ReferencedPathTracingLightRecord
            {
                positionWS = isDirectional ? Vector3.zero : source.positionWS,
                range = isDirectional ? 0.0f : Mathf.Max(source.range, 0.001f),
                forwardWS = NormalizeDirection(source.forwardWS, Vector3.forward),
                angularDiameter = angularDiameter,
                rightWS = NormalizeDirection(source.rightWS, Vector3.right),
                shapeRadius = shapeRadius,
                upWS = NormalizeDirection(source.upWS, Vector3.up),
                barnDoorCosAngle = IsAreaLight(lightType)
                    ? Mathf.Cos(
                        Mathf.Clamp(
                            SanitizeNonNegative(source.barnDoorAngle),
                            0.0f,
                            90.0f) * Mathf.Deg2Rad)
                    : 0.0f,
                radiometricColor = radiometricColor,
                selectionWeight = ComputeSelectionWeight(
                    lightType,
                    radiometricColor,
                    width,
                    height,
                    shapeRadius),
                areaSize = new Vector2(width, height),
                spotAngleParameters = new Vector2(angleScale, angleOffset),
                rangeAttenuation = new Vector2(
                    rangeAttenuationScale,
                    rangeAttenuationBias),
                barnDoorLength = IsAreaLight(lightType)
                    ? SanitizeNonNegative(source.barnDoorLength)
                    : 0.0f,
                shadowStrength = Mathf.Clamp01(
                    SanitizeNonNegative(source.shadowStrength)),
                renderingLayerMask = source.renderingLayerMask,
                shadowRenderingLayerMask = source.shadowRenderingLayerMask,
                stableIdLow = (uint)stableId,
                stableIdHigh = (uint)(stableId >> 32),
                lightType = (uint)lightType,
                flags = (uint)flags,
            };
            return true;
        }

        private static bool TryResolveLightType(
            VividLightRenderData source,
            out ReferencedPathTracingLightType lightType)
        {
            lightType = source.lightType switch
            {
                LightType.Directional =>
                    ReferencedPathTracingLightType.Directional,
                LightType.Point => ReferencedPathTracingLightType.Point,
                LightType.Spot => ReferencedPathTracingLightType.Spot,
                LightType.Rectangle =>
                    ReferencedPathTracingLightType.Rectangle,
                LightType.Tube => ReferencedPathTracingLightType.Tube,
                LightType.Disc => ReferencedPathTracingLightType.Disc,
                _ => ReferencedPathTracingLightType.Invalid,
            };
            return lightType != ReferencedPathTracingLightType.Invalid;
        }

        private static ReferencedPathTracingLightFlags ResolveFlags(
            VividLightRenderData source,
            ReferencedPathTracingLightType lightType,
            float angularDiameter)
        {
            var flags = ReferencedPathTracingLightFlags.HasStableId;
            if ((source.flags & VividLightRenderDataFlags.CastShadows) != 0
                && source.shadowStrength > 0.0f)
            {
                flags |= ReferencedPathTracingLightFlags.CastsShadows;
            }

            switch (lightType)
            {
                case ReferencedPathTracingLightType.Directional:
                    flags |= ReferencedPathTracingLightFlags.Infinite;
                    flags |= angularDiameter > FiniteDirectionalThreshold
                        ? ReferencedPathTracingLightFlags.BsdfReachable
                        : ReferencedPathTracingLightFlags.Singular;
                    break;
                case ReferencedPathTracingLightType.Point:
                case ReferencedPathTracingLightType.Spot:
                    flags |= ReferencedPathTracingLightFlags.Singular;
                    break;
                case ReferencedPathTracingLightType.Rectangle:
                case ReferencedPathTracingLightType.Disc:
                    flags |= ReferencedPathTracingLightFlags.OneSided
                        | ReferencedPathTracingLightFlags.UsesAreaMeasure;
                    break;
                case ReferencedPathTracingLightType.Tube:
                    flags |= ReferencedPathTracingLightFlags.Singular
                        | ReferencedPathTracingLightFlags.OneSided
                        | ReferencedPathTracingLightFlags.UsesLineMeasure;
                    break;
            }

            return flags;
        }

        private static float ComputeSelectionWeight(
            ReferencedPathTracingLightType lightType,
            Vector3 color,
            float width,
            float height,
            float shapeRadius)
        {
            var basePower = Mathf.Max(color.x, Mathf.Max(color.y, color.z));
            if (basePower <= 0.0f)
                return 0.0f;

            var weight = lightType switch
            {
                ReferencedPathTracingLightType.Rectangle =>
                    basePower * width * height,
                ReferencedPathTracingLightType.Tube => basePower * width,
                ReferencedPathTracingLightType.Disc =>
                    basePower * Mathf.PI * shapeRadius * shapeRadius,
                _ => basePower,
            };
            return IsFinite(weight) ? Mathf.Max(weight, 0.0f) : 0.0f;
        }

        private static void AssignSelectionDistribution(
            ReferencedPathTracingLightRecord[] records,
            double totalSelectionWeight)
        {
            double accumulatedWeight = 0.0;
            var lastActiveIndex = -1;
            for (var lightIndex = 0; lightIndex < records.Length; lightIndex++)
            {
                var record = records[lightIndex];
                if (record.selectionWeight > 0.0f)
                {
                    accumulatedWeight += record.selectionWeight;
                    lastActiveIndex = lightIndex;
                }

                record.selectionPdf =
                    (float)(record.selectionWeight / totalSelectionWeight);
                record.cdf =
                    (float)(accumulatedWeight / totalSelectionWeight);
                records[lightIndex] = record;
            }

            if (lastActiveIndex < 0)
                return;

            var lastRecord = records[lastActiveIndex];
            lastRecord.cdf = 1.0f;
            records[lastActiveIndex] = lastRecord;
            for (var lightIndex = lastActiveIndex + 1;
                 lightIndex < records.Length;
                 lightIndex++)
            {
                var record = records[lightIndex];
                record.cdf = 1.0f;
                records[lightIndex] = record;
            }
        }

        private static int CompareCandidates(Candidate lhs, Candidate rhs)
        {
            var stableIdOrder = lhs.stableId.CompareTo(rhs.stableId);
            return stableIdOrder != 0
                ? stableIdOrder
                : lhs.record.lightType.CompareTo(rhs.record.lightType);
        }

        private static bool IsEnabledAndActive(VividLightRenderData source)
        {
            const VividLightRenderDataFlags requiredFlags =
                VividLightRenderDataFlags.Enabled
                | VividLightRenderDataFlags.ActiveInHierarchy;
            return (source.flags & requiredFlags) == requiredFlags;
        }

        private static bool IsAreaLight(
            ReferencedPathTracingLightType lightType)
        {
            return lightType == ReferencedPathTracingLightType.Rectangle
                || lightType == ReferencedPathTracingLightType.Disc;
        }

        private static void ResolveSpotAngleParameters(
            LightType lightType,
            float innerSpotAngle,
            float outerSpotAngle,
            out float angleScale,
            out float angleOffset)
        {
            if (lightType != LightType.Spot)
            {
                angleScale = 0.0f;
                angleOffset = 1.0f;
                return;
            }

            var innerHalfAngleDegrees = Mathf.Clamp(
                SanitizeNonNegative(innerSpotAngle) * 0.5f,
                0.0f,
                89.0f);
            var minimumOuterHalfAngle =
                Mathf.Min(innerHalfAngleDegrees + 0.001f, 89.0f);
            var outerHalfAngleDegrees = Mathf.Clamp(
                SanitizeNonNegative(outerSpotAngle) * 0.5f,
                minimumOuterHalfAngle,
                89.0f);
            var cosInner = Mathf.Cos(innerHalfAngleDegrees * Mathf.Deg2Rad);
            var cosOuter = Mathf.Cos(outerHalfAngleDegrees * Mathf.Deg2Rad);
            var angleRange = Mathf.Max(cosInner - cosOuter, 0.001f);
            angleScale = 1.0f / angleRange;
            angleOffset = -cosOuter * angleScale;
        }

        private static void ResolveRangeAttenuation(
            VividLightRenderData source,
            bool isDirectional,
            out float scale,
            out float bias)
        {
            if (isDirectional)
            {
                scale = 0.0f;
                bias = 0.0f;
                return;
            }

            var range = Mathf.Max(source.range, 0.001f);
            scale = IsFinite(source.rangeAttenuationScale)
                && source.rangeAttenuationScale > 0.0f
                    ? source.rangeAttenuationScale
                    : 1.0f / Mathf.Max(range * range, 1e-6f);
            bias = IsFinite(source.rangeAttenuationBias)
                && source.rangeAttenuationBias > 0.0f
                    ? source.rangeAttenuationBias
                    : 1.0f;
        }

        private static Vector3 NormalizeDirection(
            Vector3 direction,
            Vector3 fallback)
        {
            if (!HasFiniteVector(direction))
                return fallback;

            var lengthSquared = direction.sqrMagnitude;
            return lengthSquared > DirectionEpsilon
                ? direction / Mathf.Sqrt(lengthSquared)
                : fallback;
        }

        private static bool HasFiniteColor(Vector3 value)
        {
            return HasFiniteVector(value);
        }

        private static bool HasFiniteVector(Vector3 value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z);
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) ? Mathf.Max(value, 0.0f) : 0.0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static ulong ComputeSignature(
            ReferencedPathTracingLightRecord[] records,
            ReferencedPathTracingLightListParameters parameters)
        {
            var hash = FnvOffsetBasis;
            Hash(ref hash, parameters.version);
            Hash(ref hash, parameters.distributionMode);
            Hash(ref hash, parameters.lightCount);
            Hash(ref hash, parameters.activeLightCount);
            Hash(ref hash, parameters.unsupportedLightCount);
            Hash(ref hash, parameters.unstableLightCount);
            Hash(ref hash, parameters.totalSelectionWeight);
            Hash(ref hash, parameters.inverseTotalSelectionWeight);

            for (var lightIndex = 0; lightIndex < records.Length; lightIndex++)
                Hash(ref hash, records[lightIndex]);

            return hash;
        }

        private static void Hash(
            ref ulong hash,
            ReferencedPathTracingLightRecord record)
        {
            Hash(ref hash, record.positionWS);
            Hash(ref hash, record.range);
            Hash(ref hash, record.forwardWS);
            Hash(ref hash, record.angularDiameter);
            Hash(ref hash, record.rightWS);
            Hash(ref hash, record.shapeRadius);
            Hash(ref hash, record.upWS);
            Hash(ref hash, record.barnDoorCosAngle);
            Hash(ref hash, record.radiometricColor);
            Hash(ref hash, record.selectionWeight);
            Hash(ref hash, record.areaSize.x);
            Hash(ref hash, record.areaSize.y);
            Hash(ref hash, record.spotAngleParameters.x);
            Hash(ref hash, record.spotAngleParameters.y);
            Hash(ref hash, record.rangeAttenuation.x);
            Hash(ref hash, record.rangeAttenuation.y);
            Hash(ref hash, record.barnDoorLength);
            Hash(ref hash, record.shadowStrength);
            Hash(ref hash, record.selectionPdf);
            Hash(ref hash, record.cdf);
            Hash(ref hash, record.renderingLayerMask);
            Hash(ref hash, record.shadowRenderingLayerMask);
            Hash(ref hash, record.stableIdLow);
            Hash(ref hash, record.stableIdHigh);
            Hash(ref hash, record.lightType);
            Hash(ref hash, record.flags);
        }

        private static void Hash(ref ulong hash, Vector3 value)
        {
            Hash(ref hash, value.x);
            Hash(ref hash, value.y);
            Hash(ref hash, value.z);
        }

        private static void Hash(ref ulong hash, float value)
        {
            Hash(
                ref hash,
                unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        private static void Hash(ref ulong hash, uint value)
        {
            hash ^= value & 0xffu;
            hash *= FnvPrime;
            hash ^= (value >> 8) & 0xffu;
            hash *= FnvPrime;
            hash ^= (value >> 16) & 0xffu;
            hash *= FnvPrime;
            hash ^= (value >> 24) & 0xffu;
            hash *= FnvPrime;
        }
    }
}
