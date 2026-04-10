using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PhysicallyBasedSkyCelestialBodyData
    {
        public Vector3 color;
        public float radius;

        public Vector3 forward;
        public float distanceFromCamera;

        public Vector3 right;
        public float angularRadius;

        public Vector3 up;
        public int type;

        public Vector3 surfaceColor;
        public float earthshine;

        public Vector4 surfaceTextureScaleOffset;

        public Vector3 sunDirection;
        public float flareCosInner;

        public Vector2 phaseAngleSinCos;
        public float flareCosOuter;
        public float flareSize;

        public Vector3 flareColor;
        public float flareFalloff;

        public Vector3 padding;
        public int shadowIndex;

        internal static int Stride => Marshal.SizeOf<PhysicallyBasedSkyCelestialBodyData>();
    }

    internal static class PhysicallyBasedSkyCelestialBodyUtility
    {
        internal const int MaxCelestialBodies = 16;

        private const float DefaultCelestialDistance = 149597870700.0f;
        private const int CelestialBodyTypeStar = 0;

        internal static int ResolveCelestialLightCount(in SkyRendererContext context)
        {
            BuildLightHash(context, null, out var lightCount, out _, out _);
            return lightCount;
        }

        internal static int ResolveCelestialBodyCount(in SkyRendererContext context)
        {
            BuildLightHash(context, null, out _, out var bodyCount, out _);
            return bodyCount;
        }

        internal static float ResolveCelestialLightExposure(in SkyRendererContext context)
        {
            BuildLightHash(context, null, out _, out _, out var exposure);
            return exposure;
        }

        internal static int ComputeCelestialLightHash(in SkyRendererContext context)
        {
            return BuildLightHash(context, null, out _, out _, out _);
        }

        internal static int BuildCelestialBodyData(
            in SkyRendererContext context,
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            out int celestialLightCount,
            out int celestialBodyCount,
            out float celestialLightExposure)
        {
            if (celestialBodies == null)
                throw new ArgumentNullException(nameof(celestialBodies));

            if (celestialBodies.Length < MaxCelestialBodies)
                throw new ArgumentException($"Celestial body array must provide at least {MaxCelestialBodies} elements.", nameof(celestialBodies));

            return BuildLightHash(
                context,
                celestialBodies,
                out celestialLightCount,
                out celestialBodyCount,
                out celestialLightExposure);
        }

        internal static int ComputeCelestialLightHash(
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            int celestialLightCount)
        {
            unchecked
            {
                var hash = 13;
                var count = Mathf.Clamp(celestialLightCount, 0, celestialBodies?.Length ?? 0);
                for (var lightIndex = 0; lightIndex < count; lightIndex++)
                {
                    ref readonly var celestialBody = ref celestialBodies[lightIndex];
                    hash = hash * 23 + celestialBody.forward.GetHashCode();
                    hash = hash * 23 + celestialBody.color.GetHashCode();
                }

                return hash;
            }
        }

        private static int BuildLightHash(
            in SkyRendererContext context,
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            out int celestialLightCount,
            out int celestialBodyCount,
            out float celestialLightExposure)
        {
            celestialLightCount = 0;
            celestialBodyCount = 0;
            celestialLightExposure = 1.0f;
            var hash = 13;

            if (context.lightData != null
                && context.lightData.hasDirectionalLights
                && context.lightData.directionalLights != null)
            {
                var directionalLightCount = Mathf.Min(
                    context.lightData.directionalLightCount,
                    Mathf.Min(context.lightData.directionalLights.Length, MaxCelestialBodies));

                for (var lightIndex = 0; lightIndex < directionalLightCount; lightIndex++)
                {
                    ref readonly var light = ref context.lightData.directionalLights[lightIndex];
                    if (GetMaxColorChannel(light.color) <= 0.0f)
                        continue;

                    var celestialBody = CreateCelestialBody(light.directionWS, light.color);
                    if (celestialBodies != null)
                        celestialBodies[celestialBodyCount] = celestialBody;

                    celestialLightExposure = Mathf.Max(celestialLightExposure, ComputeExposure(celestialBody));
                    hash = hash * 23 + celestialBody.forward.GetHashCode();
                    hash = hash * 23 + celestialBody.color.GetHashCode();
                    celestialLightCount++;
                    celestialBodyCount++;
                }

                return hash;
            }

            if (RenderSettings.sun == null)
                return 13;

            var sunColor = PhysicallyBasedSkyRenderer.ResolveSunColor(context);
            if (GetMaxColorChannel(new Vector3(sunColor.r, sunColor.g, sunColor.b)) <= 0.0f)
                return 13;

            var fallbackCelestialBody = CreateCelestialBody(
                PhysicallyBasedSkyRenderer.ResolveSunDirection(context),
                new Vector3(sunColor.r, sunColor.g, sunColor.b));

            if (celestialBodies != null)
                celestialBodies[0] = fallbackCelestialBody;

            celestialLightCount = 1;
            celestialBodyCount = 1;
            celestialLightExposure = Mathf.Max(celestialLightExposure, ComputeExposure(fallbackCelestialBody));
            hash = hash * 23 + fallbackCelestialBody.forward.GetHashCode();
            hash = hash * 23 + fallbackCelestialBody.color.GetHashCode();
            return hash;
        }

        private static PhysicallyBasedSkyCelestialBodyData CreateCelestialBody(Vector3 lightDirection, Vector3 lightColor)
        {
            var directionToLight = Normalize(lightDirection, Vector3.up);
            var forward = -directionToLight;
            BuildBasis(forward, out var right, out var up);

            var angularRadius = Mathf.Deg2Rad * PhysicallyBasedSkyRenderer.SunAngularDiameterDegrees * 0.5f;
            var flareCosInner = Mathf.Cos(angularRadius);
            var solidAngle = Mathf.PI * 2.0f * Mathf.Max(1.0f - flareCosInner, 1e-6f);
            var radianceScale = 1.0f / solidAngle;

            return new PhysicallyBasedSkyCelestialBodyData
            {
                color = lightColor,
                radius = Mathf.Tan(angularRadius) * DefaultCelestialDistance,
                forward = forward,
                distanceFromCamera = DefaultCelestialDistance,
                right = right,
                angularRadius = angularRadius,
                up = up,
                type = CelestialBodyTypeStar,
                surfaceColor = lightColor * radianceScale,
                earthshine = 0.0f,
                surfaceTextureScaleOffset = Vector4.zero,
                sunDirection = Vector3.zero,
                flareCosInner = flareCosInner,
                phaseAngleSinCos = new Vector2(0.0f, 1.0f),
                flareCosOuter = flareCosInner,
                flareSize = 0.0f,
                flareColor = lightColor * radianceScale,
                flareFalloff = 0.0f,
                padding = Vector3.zero,
                shadowIndex = -1,
            };
        }

        private static float ComputeExposure(in PhysicallyBasedSkyCelestialBodyData celestialBody)
        {
            return GetMaxColorChannel(celestialBody.color) * Mathf.Max(-celestialBody.forward.y, 0.0f);
        }

        private static float GetMaxColorChannel(Vector3 color)
        {
            return Mathf.Max(color.x, Mathf.Max(color.y, color.z));
        }

        private static Vector3 Normalize(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > 1e-6f ? value.normalized : fallback.normalized;
        }

        private static void BuildBasis(Vector3 forward, out Vector3 right, out Vector3 up)
        {
            var referenceUp = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.999f
                ? Vector3.right
                : Vector3.up;

            right = Normalize(Vector3.Cross(referenceUp, forward), Vector3.right);
            up = Normalize(Vector3.Cross(forward, right), Vector3.up);
        }
    }

    internal sealed class PhysicallyBasedSkyCelestialBodyBuffer : IDisposable
    {
        private GraphicsBuffer m_Buffer;
        private PhysicallyBasedSkyCelestialBodyData[] m_CelestialBodies;

        internal GraphicsBuffer Buffer => m_Buffer;

        internal int CelestialLightCount { get; private set; }

        internal int CelestialBodyCount { get; private set; }

        internal float CelestialLightExposure { get; private set; } = 1.0f;

        internal int CelestialLightHash { get; private set; } = 13;

        internal void Update(in SkyRendererContext context)
        {
            EnsureResources();

            CelestialLightHash = PhysicallyBasedSkyCelestialBodyUtility.BuildCelestialBodyData(
                context,
                m_CelestialBodies,
                out var celestialLightCount,
                out var celestialBodyCount,
                out var celestialLightExposure);

            if (celestialBodyCount < m_CelestialBodies.Length)
                Array.Clear(m_CelestialBodies, celestialBodyCount, m_CelestialBodies.Length - celestialBodyCount);

            m_Buffer.SetData(m_CelestialBodies);

            CelestialLightCount = celestialLightCount;
            CelestialBodyCount = celestialBodyCount;
            CelestialLightExposure = celestialLightExposure;
        }

        public void Dispose()
        {
            m_Buffer?.Dispose();
            m_Buffer = null;
            m_CelestialBodies = null;
            CelestialLightCount = 0;
            CelestialBodyCount = 0;
            CelestialLightExposure = 1.0f;
            CelestialLightHash = 13;
        }

        private void EnsureResources()
        {
            if (m_CelestialBodies == null || m_CelestialBodies.Length != PhysicallyBasedSkyCelestialBodyUtility.MaxCelestialBodies)
                m_CelestialBodies = new PhysicallyBasedSkyCelestialBodyData[PhysicallyBasedSkyCelestialBodyUtility.MaxCelestialBodies];

            if (m_Buffer != null
                && m_Buffer.count == PhysicallyBasedSkyCelestialBodyUtility.MaxCelestialBodies
                && m_Buffer.stride == PhysicallyBasedSkyCelestialBodyData.Stride
                && m_Buffer.target == GraphicsBuffer.Target.Structured)
            {
                return;
            }

            m_Buffer?.Dispose();
            m_Buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                PhysicallyBasedSkyCelestialBodyUtility.MaxCelestialBodies,
                PhysicallyBasedSkyCelestialBodyData.Stride);
        }
    }
}
