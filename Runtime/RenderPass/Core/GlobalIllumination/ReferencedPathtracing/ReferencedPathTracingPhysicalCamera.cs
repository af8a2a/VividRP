using System;
using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core
{
    internal readonly struct ReferencedPathTracingPhysicalCameraState
        : IEquatable<ReferencedPathTracingPhysicalCameraState>
    {
        internal const int Version = 1;
        private const float MillimetersToMeters = 0.001f;
        private const float MinimumTransportDistance = 0.0001f;

        private ReferencedPathTracingPhysicalCameraState(
            bool enabled,
            float focusDistance,
            float focalLength,
            float aperture,
            Vector2 lensRadius)
        {
            this.enabled = enabled;
            this.focusDistance = focusDistance;
            this.focalLength = focalLength;
            this.aperture = aperture;
            this.lensRadius = lensRadius;

            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            ReferencedPathTracingStableHash.Add(ref hash, Version);
            ReferencedPathTracingStableHash.Add(ref hash, enabled);
            if (enabled)
            {
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    focusDistance);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    lensRadius.x);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    lensRadius.y);
            }

            signature = hash;
        }

        internal bool enabled { get; }
        internal float focusDistance { get; }
        internal float focalLength { get; }
        internal float aperture { get; }
        internal Vector2 lensRadius { get; }
        internal ulong signature { get; }

        internal Vector4 shaderParameters => new(
            lensRadius.x,
            lensRadius.y,
            focusDistance,
            enabled ? 1.0f : 0.0f);

        internal static ReferencedPathTracingPhysicalCameraState Resolve(
            Camera camera,
            DepthOfFieldSettingsData depthOfFieldSettings)
        {
            if (camera == null
                || camera.orthographic
                || camera.cameraType == CameraType.SceneView
                || !depthOfFieldSettings.enabled
                || depthOfFieldSettings.focusMode
                    != DepthOfFieldMode.UsePhysicalCamera)
            {
                return Disabled;
            }

            var focalLength = Mathf.Max(
                camera.focalLength,
                MinimumTransportDistance);
            var aperture = Mathf.Max(
                camera.aperture,
                Camera.kMinAperture);
            var focusDistance = Mathf.Max(
                depthOfFieldSettings.focusDistanceMode
                    == FocusDistanceMode.Camera
                    ? camera.focusDistance
                    : depthOfFieldSettings.focusDistance,
                MinimumTransportDistance);

            // Unity focal length is millimetres while scene/world distance is
            // metres by convention. This is the same thin-lens radius used by
            // HDRP: radius = 0.5 * focalLength / f-number.
            var apertureRadius =
                0.5f
                * focalLength
                * MillimetersToMeters
                / aperture;
            var anamorphism = Mathf.Clamp(
                camera.anamorphism / 4.0f,
                -0.99f,
                0.99f);
            var lensRadius = new Vector2(
                apertureRadius * (1.0f - anamorphism),
                apertureRadius * (1.0f + anamorphism));
            var enabled =
                lensRadius.x > 0.0f
                && lensRadius.y > 0.0f
                && focusDistance > 0.0f;

            return new ReferencedPathTracingPhysicalCameraState(
                enabled,
                focusDistance,
                focalLength,
                aperture,
                lensRadius);
        }

        public bool Equals(
            ReferencedPathTracingPhysicalCameraState other)
        {
            return enabled == other.enabled
                && focusDistance.Equals(other.focusDistance)
                && focalLength.Equals(other.focalLength)
                && aperture.Equals(other.aperture)
                && lensRadius.Equals(other.lensRadius)
                && signature == other.signature;
        }

        public override bool Equals(object obj)
        {
            return obj
                is ReferencedPathTracingPhysicalCameraState other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            return signature.GetHashCode();
        }

        internal static ReferencedPathTracingPhysicalCameraState Disabled =>
            new(
                false,
                0.0f,
                0.0f,
                0.0f,
                Vector2.zero);
    }
}
