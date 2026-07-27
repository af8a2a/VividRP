using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingAtmosphereTransportTests
    {
        private const double BottomRadius = 6371000.0;
        private const double TopRadius = BottomRadius + 80000.0;
        private const double RayleighScaleHeight = 8000.0;
        private const double MieScaleHeight = 1200.0;
        private const double OzoneLayerStart = BottomRadius + 10000.0;
        private const double OzoneLayerWidth = 50000.0;
        private const int ZenithResolution = 128;
        private const int RadialResolution = 64;
        private const int LutSampleCount = 256;
        private const int ReferenceSampleCount = 4096;

        private static readonly DensityDepth RayleighExtinction =
            new(5.8e-6, 13.5e-6, 33.1e-6);
        private static readonly DensityDepth MieExtinction =
            new(4.0e-6, 4.0e-6, 4.0e-6);
        private static readonly DensityDepth OzoneExtinction =
            new(0.65e-6, 1.88e-6, 0.085e-6);

        [Test]
        public void StableSphereIntersection_ResolvesMeterScaleGroundHit()
        {
            var cameraRadius = BottomRadius + 2.0;

            Assert.That(
                TryIntersectSphere(
                    cameraRadius,
                    -1.0,
                    BottomRadius,
                    out var groundIntersection),
                Is.True);
            Assert.That(
                groundIntersection.near,
                Is.EqualTo(2.0).Within(1e-6));
            Assert.That(
                groundIntersection.far,
                Is.GreaterThan(BottomRadius * 2.0));

            Assert.That(
                TryIntersectSphere(
                    cameraRadius,
                    0.0,
                    TopRadius,
                    out var atmosphereIntersection),
                Is.True);
            var expectedExit = Math.Sqrt(
                TopRadius * TopRadius
                - cameraRadius * cameraRadius);
            Assert.That(
                atmosphereIntersection.far,
                Is.EqualTo(expectedExit).Within(1e-6));
        }

        [Test]
        public void OpticalDepthLut_AgreesWithHighSampleReferenceMatrix()
        {
            var texelCache = new Dictionary<long, DensityDepth>();
            var heights = new[]
            {
                0.0,
                100.0,
                1000.0,
                5000.0,
                10000.0,
                30000.0,
                60000.0,
                79000.0
            };
            var horizonRelativeCosines = new[]
            {
                -1.0,
                -0.25,
                -0.02,
                0.00001,
                0.02,
                0.25,
                1.0
            };

            var maximumAbsoluteError = 0.0;
            foreach (var height in heights)
            {
                var radius = BottomRadius + height;
                var horizonCosine = HorizonCosine(radius);
                foreach (var relativeCosine in horizonRelativeCosines)
                {
                    var cosineZenith = relativeCosine < 0.0
                        ? horizonCosine
                            + relativeCosine
                            * (1.0 + horizonCosine)
                        : horizonCosine
                            + relativeCosine
                            * (1.0 - horizonCosine);
                    var lutDepth = SampleLut(
                        radius,
                        cosineZenith,
                        texelCache);
                    var referenceDepth = IntegrateDensity(
                        radius,
                        cosineZenith,
                        ReferenceSampleCount);
                    var lutTransmittance =
                        EvaluateTransmittance(lutDepth);
                    var referenceTransmittance =
                        EvaluateTransmittance(referenceDepth);

                    maximumAbsoluteError = Math.Max(
                        maximumAbsoluteError,
                        MaxComponent(
                            Abs(
                                lutTransmittance
                                - referenceTransmittance)));
                }
            }

            Assert.That(
                maximumAbsoluteError,
                Is.LessThan(0.02),
                "A1 optical-depth LUT exceeded the two-percent absolute " +
                "transmittance error gate across the height/zenith matrix.");
        }

        private static DensityDepth SampleLut(
            double radius,
            double cosineZenith,
            IDictionary<long, DensityDepth> texelCache)
        {
            if (BoundaryDistance(radius, cosineZenith) <= 0.01)
                return default;

            MapLut(radius, cosineZenith, out var u, out var v);
            var texelX = u * ZenithResolution - 0.5;
            var texelY = v * RadialResolution - 0.5;
            var lowerX = Clamp(
                (int)Math.Floor(texelX),
                0,
                ZenithResolution - 1);
            var lowerY = Clamp(
                (int)Math.Floor(texelY),
                0,
                RadialResolution - 1);
            var upperX = Math.Min(lowerX + 1, ZenithResolution - 1);
            var upperY = Math.Min(lowerY + 1, RadialResolution - 1);
            var fractionX = texelX - Math.Floor(texelX);
            var fractionY = texelY - Math.Floor(texelY);

            var lowerRow = Lerp(
                GetLutTexel(lowerX, lowerY, texelCache),
                GetLutTexel(upperX, lowerY, texelCache),
                fractionX);
            var upperRow = Lerp(
                GetLutTexel(lowerX, upperY, texelCache),
                GetLutTexel(upperX, upperY, texelCache),
                fractionX);
            return Lerp(lowerRow, upperRow, fractionY);
        }

        private static DensityDepth GetLutTexel(
            int x,
            int y,
            IDictionary<long, DensityDepth> texelCache)
        {
            var key = ((long)y << 32) | (uint)x;
            if (texelCache.TryGetValue(key, out var value))
                return value;

            var u = (x + 0.5) / ZenithResolution;
            var v = (y + 0.5) / RadialResolution;
            UnmapLut(u, v, out var radius, out var cosineZenith);
            value = IntegrateDensity(
                radius,
                cosineZenith,
                LutSampleCount);
            texelCache.Add(key, value);
            return value;
        }

        private static void MapLut(
            double radius,
            double cosineZenith,
            out double u,
            out double v)
        {
            var horizonCosine = HorizonCosine(radius);
            var aboveHorizon = cosineZenith >= horizonCosine;
            var denominator = aboveHorizon
                ? Math.Max(1.0 - horizonCosine, 1e-6)
                : Math.Max(1.0 + horizonCosine, 1e-6);
            var horizonDistance = aboveHorizon
                ? Math.Max(cosineZenith - horizonCosine, 0.0)
                : Math.Max(horizonCosine - cosineZenith, 0.0);
            var mappedCosine = Math.Sqrt(
                Clamp(horizonDistance / denominator, 0.0, 1.0));
            u = aboveHorizon
                ? 0.5 + 0.5 * mappedCosine
                : 0.5 - 0.5 * mappedCosine;

            var halfTexelU = 0.5 / ZenithResolution;
            u = aboveHorizon
                ? Clamp(
                    u,
                    0.5 + halfTexelU,
                    1.0 - halfTexelU)
                : Clamp(
                    u,
                    halfTexelU,
                    0.5 - halfTexelU);
            v = Math.Sqrt(
                Clamp(
                    (radius - BottomRadius)
                    / (TopRadius - BottomRadius),
                    0.0,
                    1.0));
            v = Clamp(
                v,
                0.5 / RadialResolution,
                1.0 - 0.5 / RadialResolution);
        }

        private static void UnmapLut(
            double u,
            double v,
            out double radius,
            out double cosineZenith)
        {
            radius =
                BottomRadius
                + v * v * (TopRadius - BottomRadius);
            var horizonCosine = HorizonCosine(radius);
            var mappedCosine = u * 2.0 - 1.0;
            var hemisphereSign = mappedCosine >= 0.0 ? 1.0 : -1.0;
            cosineZenith =
                horizonCosine
                + hemisphereSign
                * mappedCosine
                * mappedCosine
                * (1.0 - hemisphereSign * horizonCosine);
            cosineZenith = Clamp(cosineZenith, -1.0, 1.0);
        }

        private static DensityDepth IntegrateDensity(
            double radius,
            double cosineZenith,
            int sampleCount)
        {
            var segmentLength =
                BoundaryDistance(radius, cosineZenith);
            if (segmentLength <= 0.01)
                return default;

            var stepLength = segmentLength / sampleCount;
            var densityDepth = default(DensityDepth);
            for (var sampleIndex = 0;
                sampleIndex < sampleCount;
                sampleIndex++)
            {
                var distance =
                    (sampleIndex + 0.5) * stepLength;
                var sampleRadius = Math.Sqrt(
                    radius * radius
                    + distance
                    * (2.0 * radius * cosineZenith + distance));
                densityDepth += EvaluateDensity(sampleRadius);
            }

            return densityDepth * stepLength;
        }

        private static double BoundaryDistance(
            double radius,
            double cosineZenith)
        {
            if (!TryIntersectSphere(
                    radius,
                    cosineZenith,
                    TopRadius,
                    out var atmosphereIntersection)
                || atmosphereIntersection.far < 0.0)
            {
                return 0.0;
            }

            var boundaryTolerance =
                Math.Max(BottomRadius * 1e-7, 0.01);
            if (radius <= BottomRadius + boundaryTolerance
                && cosineZenith < 0.0)
            {
                return 0.0;
            }

            var distance = atmosphereIntersection.far;
            if (TryIntersectSphere(
                    radius,
                    cosineZenith,
                    BottomRadius,
                    out var groundIntersection))
            {
                var groundDistance = -1.0;
                if (groundIntersection.near >= 0.0)
                    groundDistance = groundIntersection.near;
                else if (groundIntersection.far >= 0.0
                    && radius > BottomRadius + boundaryTolerance)
                {
                    groundDistance = groundIntersection.far;
                }

                if (groundDistance >= 0.0
                    && groundDistance <= distance)
                {
                    distance = groundDistance;
                }
            }

            return Math.Max(distance, 0.0);
        }

        private static bool TryIntersectSphere(
            double radialDistance,
            double cosineZenith,
            double sphereRadius,
            out SphereIntersection intersection)
        {
            var b = radialDistance * cosineZenith;
            var c =
                (radialDistance - sphereRadius)
                * (radialDistance + sphereRadius);
            var discriminant = b * b - c;
            if (discriminant < 0.0)
            {
                intersection = default;
                return false;
            }

            var rootDiscriminant = Math.Sqrt(discriminant);
            var q = -b
                - (b >= 0.0
                    ? rootDiscriminant
                    : -rootDiscriminant);
            double first;
            double second;
            if (Math.Abs(q) > 1e-12)
            {
                first = q;
                second = c / q;
            }
            else
            {
                first = -b - rootDiscriminant;
                second = -b + rootDiscriminant;
            }

            intersection = first <= second
                ? new SphereIntersection(first, second)
                : new SphereIntersection(second, first);
            return true;
        }

        private static double HorizonCosine(double radius)
        {
            var ratio =
                BottomRadius / Math.Max(radius, BottomRadius);
            return -Math.Sqrt(
                Math.Max(1.0 - ratio * ratio, 0.0));
        }

        private static DensityDepth EvaluateDensity(double radius)
        {
            var height = Math.Max(radius - BottomRadius, 0.0);
            var ozoneCoordinate =
                (radius - OzoneLayerStart) / OzoneLayerWidth;
            return new DensityDepth(
                Math.Exp(-height / RayleighScaleHeight),
                Math.Exp(-height / MieScaleHeight),
                Clamp(
                    1.0 - Math.Abs(
                        ozoneCoordinate * 2.0 - 1.0),
                    0.0,
                    1.0));
        }

        private static DensityDepth EvaluateTransmittance(
            DensityDepth densityDepth)
        {
            var opticalDepth =
                densityDepth.x * RayleighExtinction
                + densityDepth.y * MieExtinction
                + densityDepth.z * OzoneExtinction;
            return new DensityDepth(
                Math.Exp(-Math.Min(Math.Max(opticalDepth.x, 0.0), 80.0)),
                Math.Exp(-Math.Min(Math.Max(opticalDepth.y, 0.0), 80.0)),
                Math.Exp(-Math.Min(Math.Max(opticalDepth.z, 0.0), 80.0)));
        }

        private static DensityDepth Lerp(
            DensityDepth first,
            DensityDepth second,
            double interpolation)
        {
            return first
                + (second - first) * interpolation;
        }

        private static DensityDepth Abs(DensityDepth value)
        {
            return new DensityDepth(
                Math.Abs(value.x),
                Math.Abs(value.y),
                Math.Abs(value.z));
        }

        private static double MaxComponent(DensityDepth value)
        {
            return Math.Max(value.x, Math.Max(value.y, value.z));
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static double Clamp(
            double value,
            double minimum,
            double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private readonly struct SphereIntersection
        {
            internal SphereIntersection(double near, double far)
            {
                this.near = near;
                this.far = far;
            }

            internal double near { get; }
            internal double far { get; }
        }

        private readonly struct DensityDepth
        {
            internal DensityDepth(double x, double y, double z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            internal double x { get; }
            internal double y { get; }
            internal double z { get; }

            public static DensityDepth operator +(
                DensityDepth first,
                DensityDepth second)
            {
                return new DensityDepth(
                    first.x + second.x,
                    first.y + second.y,
                    first.z + second.z);
            }

            public static DensityDepth operator -(
                DensityDepth first,
                DensityDepth second)
            {
                return new DensityDepth(
                    first.x - second.x,
                    first.y - second.y,
                    first.z - second.z);
            }

            public static DensityDepth operator *(
                DensityDepth value,
                double scalar)
            {
                return new DensityDepth(
                    value.x * scalar,
                    value.y * scalar,
                    value.z * scalar);
            }

            public static DensityDepth operator *(
                double scalar,
                DensityDepth value)
            {
                return value * scalar;
            }
        }
    }
}
