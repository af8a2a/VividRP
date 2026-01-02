using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// World-space light data structure for CPU-side operations.
    /// </summary>
    [Serializable]
    public struct WorldLightData
    {
        /// <summary>
        /// World-space position of the light.
        /// </summary>
        public float3 positionWS;

        /// <summary>
        /// World-space direction of the light (normalized).
        /// </summary>
        public float3 directionWS;

        /// <summary>
        /// Light color (linear space, already multiplied by intensity).
        /// </summary>
        public Color color;

        /// <summary>
        /// Light range in world units.
        /// </summary>
        public float range;

        /// <summary>
        /// Type of the light.
        /// </summary>
        public LightType lightType;

        /// <summary>
        /// Spot angle in degrees (for spot lights).
        /// </summary>
        public float spotAngle;

        /// <summary>
        /// Inner spot angle in degrees (for spot lights).
        /// </summary>
        public float innerSpotAngle;

        /// <summary>
        /// Area light size (for rectangle/disc lights).
        /// </summary>
        public float2 areaSize;

        /// <summary>
        /// Shadow strength (0-1).
        /// </summary>
        public float shadowStrength;

        /// <summary>
        /// Whether the light has a cookie (1.0 = yes, 0.0 = no).
        /// </summary>
        public float cookie;

        /// <summary>
        /// Light layer mask.
        /// </summary>
        public uint lightLayerMask;

        /// <summary>
        /// Whether the light is enabled (1 = enabled, 0 = disabled).
        /// </summary>
        public uint enabled;

        /// <summary>
        /// Bounding sphere for culling.
        /// </summary>
        public BoundingSphere boundingSphere;
    }

    /// <summary>
    /// GPU-friendly world light data structure (must match shader definition).
    /// </summary>
    [GenerateHLSL(PackingRules.Exact, false)]
    public struct WorldLightDataGPU
    {
        /// <summary>
        /// World-space position of the light.
        /// </summary>
        public float3 positionWS;
        public float _padding0;

        /// <summary>
        /// World-space direction of the light (normalized).
        /// </summary>
        public float3 directionWS;
        public float _padding1;

        /// <summary>
        /// Light color (linear space, already multiplied by intensity).
        /// </summary>
        public float3 color;
        public float range;

        /// <summary>
        /// Range squared (for distance calculations).
        /// </summary>
        public float rangeSquared;
        public float spotAngleCos;
        public float spotAngleSin;

        /// <summary>
        /// Inner spot angle cosine.
        /// </summary>
        public float innerSpotAngleCos;
        public float _padding2;

        /// <summary>
        /// Area light size (for rectangle/disc lights).
        /// </summary>
        public float2 areaSize;
        public float _padding3;
        public float _padding4;

        /// <summary>
        /// Bounding sphere center.
        /// </summary>
        public float3 boundingSphereCenter;
        public float boundingSphereRadius;

        /// <summary>
        /// Light type (as uint to match shader enum).
        /// </summary>
        public uint lightType;

        /// <summary>
        /// Shadow strength (0-1).
        /// </summary>
        public float shadowStrength;

        /// <summary>
        /// Whether the light has a cookie (1.0 = yes, 0.0 = no).
        /// </summary>
        public float cookie;

        /// <summary>
        /// Light layer mask.
        /// </summary>
        public uint lightLayerMask;

        /// <summary>
        /// Whether the light is enabled (1 = enabled, 0 = disabled).
        /// </summary>
        public uint enabled;
        public uint _padding5;
        public uint _padding6;
        public uint _padding7;
    }
}

