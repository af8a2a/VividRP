using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.VirtualShadowMap
{
    // CPU-only producer. Kept across ContextItem.Reset so the depth interval has
    // hysteresis, but ownership changes never inherit another camera/light's interval.
    internal sealed class VirtualShadowMapClipmapLayout
    {
        internal const int MaxLevels = 24;
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
        private int m_DepthLevelCount;
        private readonly float[] m_DepthCenters = new float[MaxLevels];
        private readonly float[] m_DepthRadii = new float[MaxLevels];
        internal readonly float[] DepthMins = new float[MaxLevels];
        internal readonly float[] DepthMaxs = new float[MaxLevels];
        internal int Count { get; private set; }
        internal int Resolution { get; private set; }
        internal int FirstLevel { get; private set; }
        internal ulong CameraId { get; private set; }
        internal ulong LightId { get; private set; }
        internal Quaternion Rotation { get; private set; }
        internal float DepthMin => DepthMins[0];
        internal float DepthMax => DepthMaxs[0];
        internal float MaxDistance { get; private set; }
        internal float NormalBias { get; private set; }
        internal float BlendBorder { get; private set; }
        internal Vector3 CameraPosition { get; private set; }

        internal void Reset() => Count = 0;

        // UE GetLevelRadius uses centimetres; Unity world units are metres.
        internal static float GetLevelRadius(int level) => Mathf.Pow(2, level + 1) * 0.01f;

        // UE rounds ties toward +infinity, unlike Mathf.Round's ties-to-even.
        internal static long SnapCenter(double coordinate, double radius)
            => (long)Math.Floor(coordinate / radius + 0.5);

        internal void Update(Vector3 cameraPosition, Quaternion rotation, Bounds casterBounds,
            float maxDistance, int resolution, int firstLevel, float normalBias,
            ulong cameraId, ulong lightId, float transitionFraction = 0.2f, VividCameraData coverageView = null,
            int lastLevel = 22, float zRangeScale = 1000)
        {
            // UE builds its light view from direction, not the light actor's roll.
            rotation = Quaternion.LookRotation(rotation * Vector3.forward, Vector3.up);
            firstLevel = Mathf.Clamp(firstLevel, -1, 22);
            if (coverageView?.camera != null && coverageView.camera.orthographic)
            {
                float halfWidthCm = Mathf.Abs(coverageView.nonJitteredProjectionMatrix.inverse.m00) * 100;
                firstLevel = Mathf.Clamp(Mathf.Max(firstLevel,
                    Mathf.FloorToInt(Mathf.Log(Mathf.Max(halfWidthCm, 1e-8f), 2))), 0, 22);
            }
            lastLevel = Mathf.Clamp(lastLevel, firstLevel, 22);
            bool sameOwner = m_HasDepth && CameraId == cameraId && LightId == lightId
                && Rotation.Equals(rotation) && FirstLevel == firstLevel;
            CameraId = cameraId;
            LightId = lightId;
            Rotation = rotation;
            Resolution = VirtualShadowMapProjectionSet.ResolveResolution(resolution, MinResolution);
            MaxDistance = maxDistance;
            NormalBias = normalBias;
            BlendBorder = 0.5f * Mathf.Clamp(transitionFraction, 0.0f, 0.5f);
            CameraPosition = cameraPosition;
            FirstLevel = firstLevel;
            Count = lastLevel - firstLevel + 1;

            Matrix4x4 worldToLight = Matrix4x4.Rotate(Quaternion.Inverse(rotation));
            Vector3 cameraLS = worldToLight.MultiplyPoint3x4(cameraPosition);
            int pages = Resolution / VirtualShadowMapPrototypeRuntime.PageSize;
            for (int i = 0; i < Count; i++)
            {
                float rawRadius = GetLevelRadius(FirstLevel + i);
                float radius = 2 * rawRadius;
                // Four raw radii span a projection. Moving one snap unit scrolls
                // exactly one quarter of its pages, independently at each level.
                long snapX = SnapCenter(cameraLS.x, rawRadius), snapY = SnapCenter(cameraLS.y, rawRadius);
                OriginX[i] = (snapX - 2) * (pages / 4);
                OriginY[i] = (snapY - 2) * (pages / 4);
                float centerX = (float)(snapX * (double)rawRadius);
                float centerY = (float)(snapY * (double)rawRadius);
                float depthRadius = rawRadius * Mathf.Max(1.2f, zRangeScale);
                // UE UpdateClipmapLevel: do not refit to scene bounds. Cache each
                // level's depth independently until its 90 percent guard is crossed.
                if (!sameOwner || i >= m_DepthLevelCount || m_DepthRadii[i] != depthRadius
                    || Math.Abs((double)cameraLS.z - m_DepthCenters[i]) + rawRadius > 0.9 * depthRadius)
                {
                    m_DepthCenters[i] = cameraLS.z;
                    m_DepthRadii[i] = depthRadius;
                }
                DepthMins[i] = m_DepthCenters[i] - depthRadius;
                DepthMaxs[i] = m_DepthCenters[i] + depthRadius;
                Matrix4x4 view = worldToLight;
                view.m03 = -centerX;
                view.m13 = -centerY;
                view.m20 = -view.m20;
                view.m21 = -view.m21;
                view.m22 = -view.m22;
                view.m23 = DepthMins[i];
                Matrix4x4 projection = Matrix4x4.Ortho(-radius, radius, -radius, radius, 0, 2 * depthRadius);
                Vector3 centerWS = rotation * new Vector3(centerX, centerY, m_DepthCenters[i]);
                var split = new ShadowSplitData
                {
                    cullingMatrix = projection * view,
                    cullingSphere = new Vector4(centerWS.x, centerWS.y, centerWS.z,
                        Mathf.Sqrt(2 * radius * radius + depthRadius * depthRadius)),
                    cullingNearPlane = 0,
                    shadowCascadeBlendCullingFactor = 1,
                    cullingPlaneCount = 6
                };
                GeometryUtility.CalculateFrustumPlanes(split.cullingMatrix, m_Planes);
                for (int p = 0; p < 6; p++) split.SetCullingPlane(p, m_Planes[p]);
                Views[i] = view;
                Projections[i] = projection;
                Splits[i] = split;
                Radii[i] = radius;
                Centers[i] = centerWS;
            }
            m_HasDepth = true;
            m_DepthLevelCount = Count;
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
