using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass.Core
{
    // CPU-only producer. Kept across ContextItem.Reset so the depth interval has
    // hysteresis, but ownership changes never inherit another camera/light's interval.
    internal sealed class VirtualShadowMapClipmapLayout
    {
        internal const int MaxLevels = 16;
        internal const int MinResolution = 512;
        internal readonly Matrix4x4[] Views = new Matrix4x4[MaxLevels];
        internal readonly Matrix4x4[] Projections = new Matrix4x4[MaxLevels];
        internal readonly ShadowSplitData[] Splits = new ShadowSplitData[MaxLevels];
        internal readonly long[] OriginX = new long[MaxLevels];
        internal readonly long[] OriginY = new long[MaxLevels];
        internal readonly float[] Radii = new float[MaxLevels];
        internal readonly Vector3[] Centers = new Vector3[MaxLevels];
        internal readonly Matrix4x4[] CandidateViews = new Matrix4x4[VividShadowData.MaxCascadeCount + 1];
        internal readonly Matrix4x4[] CandidateProjections = new Matrix4x4[VividShadowData.MaxCascadeCount + 1];
        private readonly Plane[] m_Planes = new Plane[6];
        private bool m_HasDepth;
        internal int Count { get; private set; }
        internal int Resolution { get; private set; }
        internal int FirstLevel { get; private set; }
        internal ulong CameraId { get; private set; }
        internal ulong LightId { get; private set; }
        internal Quaternion Rotation { get; private set; }
        internal float DepthMin { get; private set; }
        internal float DepthMax { get; private set; }
        internal float MaxDistance { get; private set; }
        internal float NormalBias { get; private set; }
        internal float BlendBorder { get; private set; }
        internal Vector3 CameraPosition { get; private set; }

        internal void Reset() => Count = 0;

        internal void Update(Vector3 cameraPosition, Quaternion rotation, Bounds casterBounds,
            float maxDistance, int resolution, int firstLevel, float normalBias,
            ulong cameraId, ulong lightId, float transitionFraction = 0.2f)
        {
            bool sameOwner = m_HasDepth && CameraId == cameraId && LightId == lightId
                && Rotation.Equals(rotation);
            CameraId = cameraId;
            LightId = lightId;
            Rotation = rotation;
            Resolution = Mathf.Max(MinResolution, resolution);
            MaxDistance = maxDistance;
            NormalBias = normalBias;
            BlendBorder = 0.5f * Mathf.Clamp(transitionFraction, 0.0f, 0.5f);
            CameraPosition = cameraPosition;
            int lastLevel = Mathf.Max(firstLevel, Mathf.CeilToInt(Mathf.Log(Mathf.Max(maxDistance * 2, 1), 2)));
            FirstLevel = Mathf.Max(firstLevel, lastLevel - MaxLevels + 1);
            Count = lastLevel - FirstLevel + 1;

            Matrix4x4 worldToLight = Matrix4x4.Rotate(Quaternion.Inverse(rotation));
            Vector3 cameraLS = worldToLight.MultiplyPoint3x4(cameraPosition);
            Vector3 casterCenter = worldToLight.MultiplyPoint3x4(casterBounds.center);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 extents = casterBounds.extents;
            float casterExtentZ = Mathf.Abs(forward.x) * extents.x
                + Mathf.Abs(forward.y) * extents.y + Mathf.Abs(forward.z) * extents.z;
            float requiredMin = Mathf.Min(casterCenter.z - casterExtentZ, cameraLS.z - maxDistance);
            float requiredMax = Mathf.Max(casterCenter.z + casterExtentZ, cameraLS.z + maxDistance);
            if (!sameOwner || requiredMin < DepthMin || requiredMax > DepthMax)
            {
                FitDepthInterval(requiredMin, requiredMax, out float min, out float max);
                DepthMin = min;
                DepthMax = max;
                m_HasDepth = true;
            }

            int pagesPerAxis = Resolution / VirtualShadowMapPrototypeRuntime.PageSize;
            for (int i = 0; i < Count; i++)
            {
                float radius = Mathf.Pow(2, FirstLevel + i);
                float pageWorldSize = 2 * radius / pagesPerAxis;
                long x = (long)Math.Floor((double)cameraLS.x / pageWorldSize) - pagesPerAxis / 2;
                long y = (long)Math.Floor((double)cameraLS.y / pageWorldSize) - pagesPerAxis / 2;
                float centerX = (float)((double)x * pageWorldSize + radius);
                float centerY = (float)((double)y * pageWorldSize + radius);
                Matrix4x4 view = worldToLight;
                view.m03 = -centerX;
                view.m13 = -centerY;
                view.m20 = -view.m20;
                view.m21 = -view.m21;
                view.m22 = -view.m22;
                view.m23 = DepthMin;
                Matrix4x4 projection = Matrix4x4.Ortho(-radius, radius, -radius, radius, 0, DepthMax - DepthMin);
                Vector3 centerWS = rotation * new Vector3(centerX, centerY, (DepthMin + DepthMax) * 0.5f);
                var split = new ShadowSplitData
                {
                    cullingMatrix = projection * view,
                    cullingSphere = new Vector4(centerWS.x, centerWS.y, centerWS.z,
                        Mathf.Sqrt(2 * radius * radius + (DepthMax - DepthMin) * (DepthMax - DepthMin) * 0.25f)),
                    cullingNearPlane = 0,
                    shadowCascadeBlendCullingFactor = 1,
                    cullingPlaneCount = 6
                };
                GeometryUtility.CalculateFrustumPlanes(split.cullingMatrix, m_Planes);
                for (int p = 0; p < 6; p++)
                    split.SetCullingPlane(p, m_Planes[p]);
                Views[i] = view;
                Projections[i] = projection;
                Splits[i] = split;
                OriginX[i] = x;
                OriginY[i] = y;
                Radii[i] = radius;
                Centers[i] = centerWS;
            }
        }

        internal static void FitDepthInterval(float requiredMin, float requiredMax, out float min, out float max)
        {
            float span = Mathf.Pow(2, Mathf.Ceil(Mathf.Log(Mathf.Max(32, (requiredMax - requiredMin) * 2), 2)));
            float center = Mathf.Round((requiredMin + requiredMax) * 0.5f / (span * 0.25f)) * (span * 0.25f);
            min = center - span * 0.5f;
            max = center + span * 0.5f;
        }

        internal int BuildCandidateUnion(VividShadowData csm)
        {
            for (int i = 0; i < csm.cascadeCount; i++)
            {
                CandidateViews[i] = csm.viewMatrices[i];
                CandidateProjections[i] = csm.projMatrices[i];
            }
            CandidateViews[csm.cascadeCount] = Views[Count - 1];
            CandidateProjections[csm.cascadeCount] = Projections[Count - 1];
            return csm.cascadeCount + 1;
        }
    }
}
