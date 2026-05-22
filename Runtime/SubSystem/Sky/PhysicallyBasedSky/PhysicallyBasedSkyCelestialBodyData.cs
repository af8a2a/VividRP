using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Bindless;
using Object = UnityEngine.Object;
using ShadingSource = VividRP.Runtime.VividAdditionalLightData.CelestialBodyShadingSource;

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

        public uint surfaceTextureIndex;
        public Vector2 padding;
        public int shadowIndex;

        internal static int Stride => Marshal.SizeOf<PhysicallyBasedSkyCelestialBodyData>();
    }

    internal static class PhysicallyBasedSkyCelestialBodyUtility
    {
        internal const int MaxCelestialBodies = 16;

        private const float DefaultCelestialDistance = VividAdditionalLightData.DefaultCelestialBodyDistance;
        private const int CelestialBodyTypeStar = 0;
        private const int CelestialBodyTypeMoon = 1;
        private const int DefaultHash = 13;
        private const float MinimumFlareSizeRadians = 5.960464478e-8f;

        internal static int ResolveCelestialLightCount(in SkyRendererContext context)
        {
            BuildCelestialData(context, null, out var lightCount, out _, out _, out _);
            return lightCount;
        }

        internal static int ResolveCelestialBodyCount(in SkyRendererContext context)
        {
            BuildCelestialData(context, null, out _, out var bodyCount, out _, out _);
            return bodyCount;
        }

        internal static float ResolveCelestialLightExposure(in SkyRendererContext context)
        {
            BuildCelestialData(context, null, out _, out _, out var exposure, out _);
            return exposure;
        }

        internal static int ComputeCelestialLightHash(in SkyRendererContext context)
        {
            return BuildCelestialData(context, null, out _, out _, out _, out _);
        }

        internal static int ComputeCelestialBodyHash(in SkyRendererContext context)
        {
            BuildCelestialData(context, null, out _, out _, out _, out var celestialBodyHash);
            return celestialBodyHash;
        }

        internal static int BuildCelestialBodyData(
            in SkyRendererContext context,
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            out int celestialLightCount,
            out int celestialBodyCount,
            out float celestialLightExposure)
        {
            return BuildCelestialBodyData(
                context,
                celestialBodies,
                out celestialLightCount,
                out celestialBodyCount,
                out celestialLightExposure,
                out _);
        }

        internal static int BuildCelestialBodyData(
            in SkyRendererContext context,
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            out int celestialLightCount,
            out int celestialBodyCount,
            out float celestialLightExposure,
            out int celestialBodyHash)
        {
            if (celestialBodies != null && celestialBodies.Length < MaxCelestialBodies)
            {
                throw new ArgumentException(
                    $"Celestial body array must provide at least {MaxCelestialBodies} elements.",
                    nameof(celestialBodies));
            }

            return BuildCelestialData(
                context,
                celestialBodies,
                out celestialLightCount,
                out celestialBodyCount,
                out celestialLightExposure,
                out celestialBodyHash);
        }

        internal static int ComputeCelestialLightHash(
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            int celestialLightCount)
        {
            unchecked
            {
                var hash = DefaultHash;
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

        internal static int ComputeCelestialBodyHash(
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            int celestialBodyCount)
        {
            unchecked
            {
                var hash = DefaultHash;
                var count = Mathf.Clamp(celestialBodyCount, 0, celestialBodies?.Length ?? 0);
                for (var bodyIndex = 0; bodyIndex < count; bodyIndex++)
                {
                    hash = AppendCelestialBodyHash(hash, celestialBodies[bodyIndex], 0);
                }

                return hash;
            }
        }

        private static int BuildCelestialData(
            in SkyRendererContext context,
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            out int celestialLightCount,
            out int celestialBodyCount,
            out float celestialLightExposure,
            out int celestialBodyHash)
        {
            celestialLightCount = 0;
            celestialBodyCount = 0;
            celestialLightExposure = 1.0f;
            celestialBodyHash = DefaultHash;
            var celestialLightHash = DefaultHash;

            if (TryBuildFromActualLights(
                    context,
                    celestialBodies,
                    ref celestialLightCount,
                    ref celestialBodyCount,
                    ref celestialLightExposure,
                    ref celestialLightHash,
                    ref celestialBodyHash)
                && celestialLightCount > 0)
            {
                return celestialLightHash;
            }

            return BuildFallbackApproximateCelestialBodies(
                context,
                celestialBodies,
                ref celestialLightCount,
                ref celestialBodyCount,
                ref celestialLightExposure,
                ref celestialLightHash,
                ref celestialBodyHash);
        }
        private static bool TryBuildFromActualLights(
            in SkyRendererContext context,
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            ref int celestialLightCount,
            ref int celestialBodyCount,
            ref float celestialLightExposure,
            ref int celestialLightHash,
            ref int celestialBodyHash)
        {
            return TryBuildFromVisibleLights(
                       context,
                       celestialBodies,
                       ref celestialLightCount,
                       ref celestialBodyCount,
                       ref celestialLightExposure,
                       ref celestialLightHash,
                       ref celestialBodyHash)
                   || TryBuildFromSceneLights(
                       context,
                       celestialBodies,
                       ref celestialLightCount,
                       ref celestialBodyCount,
                       ref celestialLightExposure,
                       ref celestialLightHash,
                       ref celestialBodyHash);
        }

        private static bool TryBuildFromVisibleLights(
            in SkyRendererContext context,
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            ref int celestialLightCount,
            ref int celestialBodyCount,
            ref float celestialLightExposure,
            ref int celestialLightHash,
            ref int celestialBodyHash)
        {
            if (context.lightData == null || !context.lightData.hasVisibleLights)
                return false;

            var visibleLights = context.lightData.visibleLights;
            if (!visibleLights.IsCreated || visibleLights.Length == 0)
                return false;

            var initialCelestialLightCount = celestialLightCount;
            var initialCelestialBodyCount = celestialBodyCount;

            for (var lightIndex = 0; lightIndex < visibleLights.Length && celestialBodyCount < MaxCelestialBodies; lightIndex++)
            {
                var light = visibleLights[lightIndex].light;
                if (!light || light.type != LightType.Directional)
                    continue;

                TryAppendDirectionalLight(
                    light,
                    includeEmissiveLights: true,
                    context,
                    celestialBodies,
                    ref celestialLightCount,
                    ref celestialBodyCount,
                    ref celestialLightExposure,
                    ref celestialLightHash,
                    ref celestialBodyHash);
            }

            for (var lightIndex = 0; lightIndex < visibleLights.Length && celestialBodyCount < MaxCelestialBodies; lightIndex++)
            {
                var light = visibleLights[lightIndex].light;
                if (light == null || light.type != LightType.Directional)
                    continue;

                TryAppendDirectionalLight(
                    light,
                    includeEmissiveLights: false,
                    context,
                    celestialBodies,
                    ref celestialLightCount,
                    ref celestialBodyCount,
                    ref celestialLightExposure,
                    ref celestialLightHash,
                    ref celestialBodyHash);
            }

            return celestialLightCount > initialCelestialLightCount
                   || celestialBodyCount > initialCelestialBodyCount;
        }

        private static bool TryBuildFromSceneLights(
            in SkyRendererContext context,
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            ref int celestialLightCount,
            ref int celestialBodyCount,
            ref float celestialLightExposure,
            ref int celestialLightHash,
            ref int celestialBodyHash)
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (lights == null || lights.Length == 0)
                return false;

            Array.Sort(lights, static (lhs, rhs) =>
            {
                if (ReferenceEquals(lhs, rhs))
                    return 0;

                if (lhs == null)
                    return -1;

                if (rhs == null)
                    return 1;

                return lhs.GetEntityId().CompareTo(rhs.GetEntityId());
            });

            var initialCelestialLightCount = celestialLightCount;
            var initialCelestialBodyCount = celestialBodyCount;

            for (var lightIndex = 0; lightIndex < lights.Length && celestialBodyCount < MaxCelestialBodies; lightIndex++)
            {
                var light = lights[lightIndex];
                if (light == null || light.type != LightType.Directional)
                    continue;

                TryAppendDirectionalLight(
                    light,
                    includeEmissiveLights: true,
                    context,
                    celestialBodies,
                    ref celestialLightCount,
                    ref celestialBodyCount,
                    ref celestialLightExposure,
                    ref celestialLightHash,
                    ref celestialBodyHash);
            }

            for (var lightIndex = 0; lightIndex < lights.Length && celestialBodyCount < MaxCelestialBodies; lightIndex++)
            {
                var light = lights[lightIndex];
                if (light == null || light.type != LightType.Directional)
                    continue;

                TryAppendDirectionalLight(
                    light,
                    includeEmissiveLights: false,
                    context,
                    celestialBodies,
                    ref celestialLightCount,
                    ref celestialBodyCount,
                    ref celestialLightExposure,
                    ref celestialLightHash,
                    ref celestialBodyHash);
            }

            return celestialLightCount > initialCelestialLightCount
                   || celestialBodyCount > initialCelestialBodyCount;
        }

        private static int BuildFallbackApproximateCelestialBodies(
            in SkyRendererContext context,
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            ref int celestialLightCount,
            ref int celestialBodyCount,
            ref float celestialLightExposure,
            ref int celestialLightHash,
            ref int celestialBodyHash)
        {
            if (context.lightData != null
                && context.lightData.hasDirectionalLights
                && context.lightData.directionalLights != null)
            {
                var initialCelestialLightCount = celestialLightCount;
                var directionalLightCount = Mathf.Min(
                    context.lightData.directionalLightCount,
                    Mathf.Min(context.lightData.directionalLights.Length, MaxCelestialBodies));

                for (var lightIndex = 0; lightIndex < directionalLightCount; lightIndex++)
                {
                    ref readonly var light = ref context.lightData.directionalLights[lightIndex];
                    if (GetMaxColorChannel(light.color) <= 0.0f)
                        continue;

                    var celestialBody = CreateApproximateCelestialBody(light.directionWS, light.color);
                    if (celestialBodies != null)
                        celestialBodies[celestialBodyCount] = celestialBody;

                    celestialLightExposure = Mathf.Max(celestialLightExposure, ComputeExposure(celestialBody));
                    celestialLightHash = AppendCelestialLightHash(celestialLightHash, celestialBody);
                    celestialBodyHash = AppendCelestialBodyHash(celestialBodyHash, celestialBody, 0);
                    celestialLightCount++;
                    celestialBodyCount++;
                }

                if (celestialLightCount > initialCelestialLightCount)
                    return celestialLightHash;
            }

            if (!PhysicallyBasedSkyRenderer.TryResolveFallbackSunLight(out var sunLight))
                return DefaultHash;

            var sunColor = VividLightRenderDatabase.EvaluateLightColor(sunLight);
            var sunColorVector = new Vector3(sunColor.r, sunColor.g, sunColor.b);
            if (GetMaxColorChannel(sunColorVector) <= 0.0f)
                return DefaultHash;

            var fallbackCelestialBody = CreateApproximateCelestialBody(
                (-sunLight.transform.forward).normalized,
                sunColorVector);

            if (celestialBodies != null)
                celestialBodies[0] = fallbackCelestialBody;

            celestialLightCount = 1;
            celestialBodyCount = 1;
            celestialLightExposure = Mathf.Max(celestialLightExposure, ComputeExposure(fallbackCelestialBody));
            celestialLightHash = AppendCelestialLightHash(celestialLightHash, fallbackCelestialBody);
            celestialBodyHash = AppendCelestialBodyHash(celestialBodyHash, fallbackCelestialBody, 0);
            return celestialLightHash;
        }
        private static void TryAppendDirectionalLight(
            Light light,
            bool includeEmissiveLights,
            in SkyRendererContext context,
            PhysicallyBasedSkyCelestialBodyData[] celestialBodies,
            ref int celestialLightCount,
            ref int celestialBodyCount,
            ref float celestialLightExposure,
            ref int celestialLightHash,
            ref int celestialBodyHash)
        {
            if (!light
                || !light.enabled
                || !light.gameObject.activeInHierarchy
                || light.type != LightType.Directional
                || celestialBodyCount >= MaxCelestialBodies)
            {
                return;
            }

            light.TryGetComponent(out VividAdditionalLightData additionalData);
            if (!InteractsWithSky(light, additionalData))
                return;

            var lightColor = ResolveLightColor(light, additionalData);
            var isEmissiveLight = GetMaxColorChannel(lightColor) > 0.0f;
            if (isEmissiveLight != includeEmissiveLights)
                return;

            var celestialBody = CreateCelestialBody(light, additionalData, context, lightColor);
            if (celestialBodies != null)
                celestialBodies[celestialBodyCount] = celestialBody;

            if (isEmissiveLight)
            {
                celestialLightExposure = Mathf.Max(celestialLightExposure, ComputeExposure(celestialBody));
                celestialLightHash = AppendCelestialLightHash(celestialLightHash, celestialBody);
                celestialLightCount++;
            }

            celestialBodyHash = AppendCelestialBodyHash(
                celestialBodyHash,
                celestialBody,
                additionalData?.surfaceTexture != null ? additionalData.surfaceTexture.GetEntityId().GetHashCode() : 0);
            celestialBodyCount++;
        }

        private static PhysicallyBasedSkyCelestialBodyData CreateApproximateCelestialBody(Vector3 lightDirection, Vector3 lightColor)
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
                surfaceTextureIndex = BindlessTextureContainer.InvalidTextureIndex,
                padding = Vector2.zero,
                shadowIndex = -1,
            };
        }

        private static PhysicallyBasedSkyCelestialBodyData CreateCelestialBody(
            Light light,
            VividAdditionalLightData additionalData,
            in SkyRendererContext context,
            Vector3 lightColor)
        {
            var transform = light.transform;
            var forward = Normalize(transform.forward, Vector3.forward);
            var right = Normalize(transform.right, Vector3.right);
            var up = Normalize(transform.up, Vector3.up);

            var angularDiameter = additionalData != null
                ? additionalData.angularDiameter
                : VividAdditionalLightData.DefaultCelestialBodyAngularDiameter;
            var angularRadius = Mathf.Max(angularDiameter, 0.0f) * 0.5f * Mathf.Deg2Rad;
            var distanceFromCamera = additionalData != null
                ? Mathf.Max(additionalData.distance, 0.0f)
                : DefaultCelestialDistance;
            var celestialBody = new PhysicallyBasedSkyCelestialBodyData
            {
                color = lightColor,
                radius = Mathf.Tan(angularRadius) * distanceFromCamera,
                forward = forward,
                distanceFromCamera = distanceFromCamera,
                right = right,
                angularRadius = angularRadius,
                up = up,
                type = CelestialBodyTypeStar,
                surfaceColor = ToLinearVector(additionalData != null ? additionalData.surfaceTint : Color.white),
                earthshine = Mathf.Max(additionalData?.earthshine ?? 0.0f, 0.0f) * 0.01f,
                surfaceTextureScaleOffset = ResolveSurfaceTextureScaleOffset(additionalData),
                sunDirection = Vector3.zero,
                flareCosInner = Mathf.Cos(angularRadius),
                phaseAngleSinCos = new Vector2(0.0f, 1.0f),
                flareSize = Mathf.Max(
                    Mathf.Max(additionalData?.flareSize ?? 0.0f, 0.0f) * Mathf.Deg2Rad,
                    MinimumFlareSizeRadians),
                flareColor = ToLinearVector(additionalData != null ? additionalData.flareTint : Color.white)
                    * Mathf.Clamp01(additionalData?.flareMultiplier ?? 0.0f),
                flareFalloff = Mathf.Max(additionalData?.flareFalloff ?? 0.0f, 0.0f),
                surfaceTextureIndex = ResolveSurfaceTextureIndex(additionalData),
                padding = Vector2.zero,
                shadowIndex = ResolveShadowIndex(light, context),
            };

            celestialBody.flareCosOuter = Mathf.Cos(celestialBody.angularRadius + celestialBody.flareSize);

            var shadingSource = additionalData?.celestialBodyShadingSource ?? ShadingSource.Emission;
            if (shadingSource == ShadingSource.Emission)
            {
                var rcpSolidAngle = 1.0f / (Mathf.PI * 2.0f * Mathf.Max(1.0f - celestialBody.flareCosInner, 1e-6f));
                celestialBody.type = CelestialBodyTypeStar;
                celestialBody.surfaceColor *= rcpSolidAngle;
                celestialBody.flareColor *= rcpSolidAngle;
                celestialBody.surfaceColor = Vector3.Scale(celestialBody.color, celestialBody.surfaceColor);
                celestialBody.flareColor = Vector3.Scale(celestialBody.color, celestialBody.flareColor);
                return celestialBody;
            }

            Vector3 sunColor;
            if (shadingSource == ShadingSource.Manual && additionalData != null)
            {
                var phase = additionalData.moonPhase * Mathf.PI * 2.0f;
                var rotation = Quaternion.AngleAxis(additionalData.moonPhaseRotation, celestialBody.forward);
                var remap = Quaternion.FromToRotation(Vector3.right, celestialBody.forward);
                celestialBody.phaseAngleSinCos = new Vector2(Mathf.Sin(phase), Mathf.Cos(phase));
                celestialBody.sunDirection = Normalize(
                    rotation * remap * new Vector3(celestialBody.phaseAngleSinCos.y, 0.0f, celestialBody.phaseAngleSinCos.x),
                    Vector3.forward);
                sunColor = ToLinearVector(additionalData.sunColor) * Mathf.Max(additionalData.sunIntensity, 0.0f);
            }
            else
            {
                var lightSource = ResolveSunLight(light, additionalData);
                celestialBody.sunDirection = lightSource != null
                    ? Normalize(lightSource.transform.forward, Vector3.forward)
                    : Vector3.forward;
                sunColor = lightSource != null
                    ? ResolveLightColor(lightSource, GetAdditionalLightData(lightSource))
                    : Vector3.zero;
            }

            celestialBody.type = CelestialBodyTypeMoon;
            celestialBody.surfaceColor = Vector3.Scale(sunColor, celestialBody.surfaceColor);
            celestialBody.flareColor = Vector3.Scale(sunColor, celestialBody.flareColor);
            return celestialBody;
        }
        private static bool InteractsWithSky(Light light, VividAdditionalLightData additionalData)
        {
            if (light == null || light.type != LightType.Directional)
                return false;

            return additionalData == null || additionalData.interactsWithSky;
        }

        private static VividAdditionalLightData GetAdditionalLightData(Light light)
        {
            if (light == null)
                return null;

            light.TryGetComponent(out VividAdditionalLightData additionalData);
            return additionalData;
        }

        private static Vector3 ResolveLightColor(Light light, VividAdditionalLightData additionalData)
        {
            if (!light)
                return Vector3.zero;

            var trackedLightData = VividLightRenderDatabase.instance.UpdateLightData(light, additionalData);
            return trackedLightData.color;
        }

        private static Light ResolveSunLight(Light excludedLight, VividAdditionalLightData additionalData)
        {
            var overrideLight = additionalData?.sunLightOverride;
            if (overrideLight != null
                && overrideLight != excludedLight
                && overrideLight.type == LightType.Directional
                && overrideLight.enabled
                && overrideLight.gameObject.activeInHierarchy)
            {
                return overrideLight;
            }

            Light result = null;
            var brightestIntensity = 0.0f;
            var sceneLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var lightIndex = 0; lightIndex < sceneLights.Length; lightIndex++)
            {
                var candidate = sceneLights[lightIndex];
                if (candidate == null
                    || candidate == excludedLight
                    || candidate.type != LightType.Directional
                    || !candidate.enabled
                    || !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var candidateColor = ResolveLightColor(candidate, GetAdditionalLightData(candidate));
                var candidateIntensity = GetMaxColorChannel(candidateColor);
                if (candidateIntensity <= brightestIntensity)
                    continue;

                brightestIntensity = candidateIntensity;
                result = candidate;
            }

            return result;
        }

        private static int ResolveShadowIndex(Light light, in SkyRendererContext context)
        {
            if (light == null
                || light.shadows == LightShadows.None
                || !light.enabled
                || !light.gameObject.activeInHierarchy)
            {
                return -1;
            }

            if (context.lightData != null
                && context.lightData.hasMainDirectionalLight)
            {
                return context.lightData.mainDirectionalLightEntityId.Equals(light.GetEntityId()) ? 0 : -1;
            }

            return RenderSettings.sun == light ? 0 : -1;
        }

        private static Vector4 ResolveSurfaceTextureScaleOffset(VividAdditionalLightData additionalData)
        {
            return additionalData?.surfaceTexture != null
                ? new Vector4(1.0f, 1.0f, 0.0f, 0.0f)
                : Vector4.zero;
        }

        private static uint ResolveSurfaceTextureIndex(VividAdditionalLightData additionalData)
        {
            var surfaceTexture = additionalData?.surfaceTexture;
            if (surfaceTexture == null)
                return BindlessTextureContainer.InvalidTextureIndex;

            return VividGPUDrivenSystem.instance.BindlessTextureContainer.TryGetOrCreateIndex(surfaceTexture, out var surfaceTextureIndex)
                ? surfaceTextureIndex
                : BindlessTextureContainer.InvalidTextureIndex;
        }

        private static int AppendCelestialLightHash(int hash, in PhysicallyBasedSkyCelestialBodyData celestialBody)
        {
            unchecked
            {
                hash = hash * 23 + celestialBody.forward.GetHashCode();
                hash = hash * 23 + celestialBody.color.GetHashCode();
                return hash;
            }
        }

        private static int AppendCelestialBodyHash(
            int hash,
            in PhysicallyBasedSkyCelestialBodyData celestialBody,
            int surfaceTextureInstanceId)
        {
            unchecked
            {
                hash = hash * 23 + celestialBody.color.GetHashCode();
                hash = hash * 23 + celestialBody.forward.GetHashCode();
                hash = hash * 23 + celestialBody.right.GetHashCode();
                hash = hash * 23 + celestialBody.up.GetHashCode();
                hash = hash * 23 + celestialBody.radius.GetHashCode();
                hash = hash * 23 + celestialBody.distanceFromCamera.GetHashCode();
                hash = hash * 23 + celestialBody.angularRadius.GetHashCode();
                hash = hash * 23 + celestialBody.type;
                hash = hash * 23 + celestialBody.surfaceColor.GetHashCode();
                hash = hash * 23 + celestialBody.earthshine.GetHashCode();
                hash = hash * 23 + celestialBody.surfaceTextureScaleOffset.GetHashCode();
                hash = hash * 23 + surfaceTextureInstanceId;
                hash = hash * 23 + celestialBody.sunDirection.GetHashCode();
                hash = hash * 23 + celestialBody.flareCosInner.GetHashCode();
                hash = hash * 23 + celestialBody.phaseAngleSinCos.GetHashCode();
                hash = hash * 23 + celestialBody.flareCosOuter.GetHashCode();
                hash = hash * 23 + celestialBody.flareSize.GetHashCode();
                hash = hash * 23 + celestialBody.flareColor.GetHashCode();
                hash = hash * 23 + celestialBody.flareFalloff.GetHashCode();
                hash = hash * 23 + celestialBody.shadowIndex;
                return hash;
            }
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

        private static Vector3 ToLinearVector(Color color)
        {
            var linear = color.linear;
            return new Vector3(linear.r, linear.g, linear.b);
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
        private const int DefaultHash = 13;

        private GraphicsBuffer m_Buffer;
        private PhysicallyBasedSkyCelestialBodyData[] m_CelestialBodies;

        internal GraphicsBuffer Buffer => m_Buffer;

        internal int CelestialLightCount { get; private set; }

        internal int CelestialBodyCount { get; private set; }

        internal float CelestialLightExposure { get; private set; } = 1.0f;

        internal int CelestialLightHash { get; private set; } = DefaultHash;

        internal int CelestialBodyHash { get; private set; } = DefaultHash;

        internal void Update(in SkyRendererContext context)
        {
            EnsureResources();

            CelestialLightHash = PhysicallyBasedSkyCelestialBodyUtility.BuildCelestialBodyData(
                context,
                m_CelestialBodies,
                out var celestialLightCount,
                out var celestialBodyCount,
                out var celestialLightExposure,
                out var celestialBodyHash);

            if (celestialBodyCount < m_CelestialBodies.Length)
                Array.Clear(m_CelestialBodies, celestialBodyCount, m_CelestialBodies.Length - celestialBodyCount);

            m_Buffer.SetData(m_CelestialBodies);

            CelestialLightCount = celestialLightCount;
            CelestialBodyCount = celestialBodyCount;
            CelestialLightExposure = celestialLightExposure;
            CelestialBodyHash = celestialBodyHash;
        }

        public void Dispose()
        {
            m_Buffer?.Dispose();
            m_Buffer = null;
            m_CelestialBodies = null;
            CelestialLightCount = 0;
            CelestialBodyCount = 0;
            CelestialLightExposure = 1.0f;
            CelestialLightHash = DefaultHash;
            CelestialBodyHash = DefaultHash;
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
