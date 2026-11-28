using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime
{
    public enum HairGroomAbsorptionMode
    {
        Color = 0,
        Melanin = 1,
        NormalizedMelanin = 2,
    }

    /// <summary>
    /// Unified shading parameters shared between realtime Strand Visibility Buffer
    /// (VividHairFurBSDF.hlsl) and Reference Path Tracing (VividRP/Material/Hair).
    /// </summary>
    [Serializable]
    public struct HairGroomShadingParameters
    {
        public HairGroomAbsorptionMode AbsorptionMode;
        public Color BaseColor;
        [Range(0.0f, 1.0f)] public float Melanin;
        [Range(0.0f, 1.0f)] public float MelaninRedness;
        [Range(0.05f, 1.0f)] public float LongitudinalRoughness;
        [Range(0.05f, 1.0f)] public float AzimuthalRoughness;
        [Range(1.2f, 2.0f)] public float Ior;
        [Range(0.0f, 10.0f)] public float CuticleAngleDegrees;
        [Range(0.0f, 1.0f)] public float MedullaWeight;
        [Range(0.1f, 1.0f)] public float MedullaRoughness;
        [Range(0.0f, 2.0f)] public float DualScatterGain;

        public static HairGroomShadingParameters CreateDefault()
        {
            return new HairGroomShadingParameters
            {
                AbsorptionMode = HairGroomAbsorptionMode.Melanin,
                BaseColor = new Color(0.28f, 0.16f, 0.08f, 1.0f),
                Melanin = 0.55f,
                MelaninRedness = 0.25f,
                LongitudinalRoughness = 0.28f,
                AzimuthalRoughness = 0.42f,
                Ior = 1.55f,
                CuticleAngleDegrees = 3.0f,
                MedullaWeight = 0.0f,
                MedullaRoughness = 0.65f,
                DualScatterGain = 1.0f,
            };
        }
    }

    /// <summary>
    /// Zero-allocation snapshot of one active strand groom submitted to the render graph.
    /// </summary>
    public readonly struct HairGroomRenderEntry
    {
        public readonly HairGroomRenderer Renderer;
        public readonly HairGroomAsset Asset;
        public readonly Matrix4x4 LocalToWorld;
        public readonly Bounds WorldBounds;
        public readonly float RadiusScale;
        public readonly int ActiveStrandLimit;
        public readonly HairGroomShadingParameters Shading;

        public HairGroomRenderEntry(
            HairGroomRenderer renderer,
            HairGroomAsset asset,
            Matrix4x4 localToWorld,
            Bounds worldBounds,
            float radiusScale,
            int activeStrandLimit,
            in HairGroomShadingParameters shading)
        {
            Renderer = renderer;
            Asset = asset;
            LocalToWorld = localToWorld;
            WorldBounds = worldBounds;
            RadiusScale = radiusScale;
            ActiveStrandLimit = activeStrandLimit;
            Shading = shading;
        }
    }

    /// <summary>
    /// Global zero-allocation registry of active HairGroomRenderer instances.
    /// Consumed by Strand Hair RenderGraph passes without FindObjectsOfType or per-frame GC.
    /// </summary>
    public static class HairGroomSystem
    {
        private static readonly List<HairGroomRenderer> s_ActiveRenderers = new List<HairGroomRenderer>(16);

        public static int ActiveCount => s_ActiveRenderers.Count;

        public static void Register(HairGroomRenderer renderer)
        {
            if (renderer == null)
                return;

            for (int i = 0; i < s_ActiveRenderers.Count; i++)
            {
                if (ReferenceEquals(s_ActiveRenderers[i], renderer))
                    return;
            }

            s_ActiveRenderers.Add(renderer);
        }

        public static void Unregister(HairGroomRenderer renderer)
        {
            if (renderer == null)
                return;

            int count = s_ActiveRenderers.Count;
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(s_ActiveRenderers[i], renderer))
                {
                    int last = count - 1;
                    s_ActiveRenderers[i] = s_ActiveRenderers[last];
                    s_ActiveRenderers.RemoveAt(last);
                    return;
                }
            }
        }

        /// <summary>
        /// Populates caller-owned list with valid, renderable hair grooms.
        /// Allocates zero managed memory once destination capacity is warmed up.
        /// </summary>
        public static int CollectActiveEntries(
            List<HairGroomRenderEntry> destination,
            int layerMask = ~0)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();
            int count = s_ActiveRenderers.Count;
            for (int i = 0; i < count; i++)
            {
                var renderer = s_ActiveRenderers[i];
                if (renderer == null || !renderer.isActiveAndEnabled)
                    continue;

                var asset = renderer.GroomAsset;
                if (asset == null || !asset.HasBinaryData || asset.SegmentCount <= 0)
                    continue;

                int layerBit = 1 << renderer.gameObject.layer;
                if ((layerMask & layerBit) == 0)
                    continue;

                var localToWorld = renderer.transform.localToWorldMatrix;
                var worldBounds = TransformBounds(asset.Bounds, localToWorld);
                destination.Add(new HairGroomRenderEntry(
                    renderer,
                    asset,
                    localToWorld,
                    worldBounds,
                    renderer.RadiusScale,
                    renderer.ActiveStrandLimit,
                    renderer.Shading));
            }

            return destination.Count;
        }

        public static Bounds TransformBounds(Bounds localBounds, Matrix4x4 localToWorld)
        {
            Vector3 center = localToWorld.MultiplyPoint3x4(localBounds.center);
            Vector3 ext = localBounds.extents;
            Vector3 axisX = localToWorld.MultiplyVector(new Vector3(ext.x, 0.0f, 0.0f));
            Vector3 axisY = localToWorld.MultiplyVector(new Vector3(0.0f, ext.y, 0.0f));
            Vector3 axisZ = localToWorld.MultiplyVector(new Vector3(0.0f, 0.0f, ext.z));
            Vector3 worldExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, worldExtents * 2.0f);
        }
    }
}
