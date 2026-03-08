using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividLightData : ContextItem
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DirectionalLightData
        {
            public Vector3 directionWS;
            public float shadowStrength;
            public Vector3 color;
            public uint renderingLayerMask;

            internal static int Stride => Marshal.SizeOf<DirectionalLightData>();
        }

        internal readonly struct VisibleLightDescriptor
        {
            public VisibleLightDescriptor(EntityId lightEntityId, LightType lightType, Color finalColor)
            {
                this.lightEntityId = lightEntityId;
                this.lightType = lightType;
                this.finalColor = finalColor;
            }

            public EntityId lightEntityId { get; }

            public LightType lightType { get; }

            public Color finalColor { get; }
        }

        public NativeArray<VisibleLight> visibleLights;
        public NativeArray<VisibleReflectionProbe> visibleReflectionProbes;
        public DirectionalLightData[] directionalLights = Array.Empty<DirectionalLightData>();
        public int mainLightIndex;
        public EntityId mainLightEntityId;
        public int directionalLightCount;
        public int mainDirectionalLightIndex;
        public EntityId mainDirectionalLightEntityId;

        public bool hasVisibleLights => visibleLights.IsCreated && visibleLights.Length > 0;

        public bool hasVisibleReflectionProbes => visibleReflectionProbes.IsCreated && visibleReflectionProbes.Length > 0;

        public bool hasMainLight => IsValidLightIndex(mainLightIndex);

        public bool hasDirectionalLights => directionalLightCount > 0;

        public bool hasMainDirectionalLight => IsValidDirectionalLightIndex(mainDirectionalLightIndex);

        public int visibleLightCount => hasVisibleLights ? visibleLights.Length : 0;

        public int additionalLightsCount => hasVisibleLights ? visibleLights.Length - (hasMainLight ? 1 : 0) : 0;

        public int visibleReflectionProbeCount => hasVisibleReflectionProbes ? visibleReflectionProbes.Length : 0;

        public int additionalDirectionalLightsCount => hasDirectionalLights ? directionalLightCount - (hasMainDirectionalLight ? 1 : 0) : 0;

        public VisibleLight mainVisibleLight => hasMainLight ? visibleLights[mainLightIndex] : default;

        public Light mainLight => hasMainLight ? visibleLights[mainLightIndex].light : null;

        public DirectionalLightData mainDirectionalLight => hasMainDirectionalLight ? directionalLights[mainDirectionalLightIndex] : default;

        internal void Update(CullingResults cullingResults)
        {
            visibleLights = cullingResults.visibleLights;
            visibleReflectionProbes = cullingResults.visibleReflectionProbes;
            mainLightIndex = FindMainLightIndex(visibleLights, RenderSettings.sun);
            mainLightEntityId = hasMainLight && visibleLights[mainLightIndex].light != null
                ? visibleLights[mainLightIndex].light.GetEntityId()
                : EntityId.None;
            UpdateDirectionalLights(visibleLights, RenderSettings.sun);
        }

        internal void UpdateDirectionalLights(NativeArray<VisibleLight> visibleLights, Light sunLight)
        {
            EnsureDirectionalLightCapacity(CountDirectionalLights(visibleLights));

            directionalLightCount = 0;
            mainDirectionalLightIndex = -1;
            mainDirectionalLightEntityId = EntityId.None;

            if (!visibleLights.IsCreated || visibleLights.Length == 0)
                return;

            var sunLightEntityId = sunLight != null ? sunLight.GetEntityId() : EntityId.None;
            var brightestDirectionalIndex = -1;
            var brightestDirectionalEntityId = EntityId.None;
            var brightestDirectionalIntensity = float.NegativeInfinity;

            for (var lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
            {
                var visibleLight = visibleLights[lightIndex];
                if (visibleLight.lightType != LightType.Directional)
                    continue;

                directionalLights[directionalLightCount] = CreateDirectionalLightData(visibleLight);

                var lightEntityId = visibleLight.light != null ? visibleLight.light.GetEntityId() : EntityId.None;
                if (!sunLightEntityId.Equals(EntityId.None) && lightEntityId.Equals(sunLightEntityId))
                {
                    mainDirectionalLightIndex = directionalLightCount;
                    mainDirectionalLightEntityId = lightEntityId;
                }

                var lightIntensity = GetLightIntensity(directionalLights[directionalLightCount].color);
                if (lightIntensity > brightestDirectionalIntensity)
                {
                    brightestDirectionalIntensity = lightIntensity;
                    brightestDirectionalIndex = directionalLightCount;
                    brightestDirectionalEntityId = lightEntityId;
                }

                directionalLightCount++;
            }

            if (mainDirectionalLightIndex >= 0)
                return;

            mainDirectionalLightIndex = brightestDirectionalIndex;
            mainDirectionalLightEntityId = brightestDirectionalEntityId;
        }

        internal void UpdateDirectionalLights(IReadOnlyList<Light> lights, Light sunLight)
        {
            EnsureDirectionalLightCapacity(CountDirectionalLights(lights));

            directionalLightCount = 0;
            mainDirectionalLightIndex = -1;
            mainDirectionalLightEntityId = EntityId.None;

            if (lights == null || lights.Count == 0)
                return;

            var sunLightEntityId = sunLight != null ? sunLight.GetEntityId() : EntityId.None;
            var brightestDirectionalIndex = -1;
            var brightestDirectionalEntityId = EntityId.None;
            var brightestDirectionalIntensity = float.NegativeInfinity;

            for (var lightIndex = 0; lightIndex < lights.Count; lightIndex++)
            {
                var light = lights[lightIndex];
                if (!IsDirectionalLightSupported(light))
                    continue;

                directionalLights[directionalLightCount] = CreateDirectionalLightData(light);

                var lightEntityId = light.GetEntityId();
                if (!sunLightEntityId.Equals(EntityId.None) && lightEntityId.Equals(sunLightEntityId))
                {
                    mainDirectionalLightIndex = directionalLightCount;
                    mainDirectionalLightEntityId = lightEntityId;
                }

                var lightIntensity = GetLightIntensity(directionalLights[directionalLightCount].color);
                if (lightIntensity > brightestDirectionalIntensity)
                {
                    brightestDirectionalIntensity = lightIntensity;
                    brightestDirectionalIndex = directionalLightCount;
                    brightestDirectionalEntityId = lightEntityId;
                }

                directionalLightCount++;
            }

            if (mainDirectionalLightIndex >= 0)
                return;

            mainDirectionalLightIndex = brightestDirectionalIndex;
            mainDirectionalLightEntityId = brightestDirectionalEntityId;
        }

        public override void Reset()
        {
            visibleLights = default;
            visibleReflectionProbes = default;
            mainLightIndex = -1;
            mainLightEntityId = EntityId.None;
            directionalLightCount = 0;
            mainDirectionalLightIndex = -1;
            mainDirectionalLightEntityId = EntityId.None;
        }

        internal static int FindMainLightIndex(NativeArray<VisibleLight> visibleLights, Light sunLight)
        {
            if (!visibleLights.IsCreated || visibleLights.Length == 0)
                return -1;

            var sunLightEntityId = sunLight != null ? sunLight.GetEntityId() : EntityId.None;
            var brightestDirectionalIndex = -1;
            var brightestDirectionalIntensity = float.NegativeInfinity;

            for (var lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
            {
                var visibleLight = visibleLights[lightIndex];
                if (visibleLight.lightType != LightType.Directional)
                    continue;

                if (!sunLightEntityId.Equals(EntityId.None)
                    && visibleLight.light != null
                    && visibleLight.light.GetEntityId().Equals(sunLightEntityId))
                    return lightIndex;

                var lightIntensity = GetLightIntensity(visibleLight.finalColor);
                if (lightIntensity <= brightestDirectionalIntensity)
                    continue;

                brightestDirectionalIntensity = lightIntensity;
                brightestDirectionalIndex = lightIndex;
            }

            return brightestDirectionalIndex;
        }

        internal static int FindMainLightIndex(IReadOnlyList<VisibleLightDescriptor> visibleLights, EntityId sunLightEntityId)
        {
            if (visibleLights == null || visibleLights.Count == 0)
                return -1;

            var brightestDirectionalIndex = -1;
            var brightestDirectionalIntensity = float.NegativeInfinity;

            for (var lightIndex = 0; lightIndex < visibleLights.Count; lightIndex++)
            {
                var visibleLight = visibleLights[lightIndex];
                if (visibleLight.lightType != LightType.Directional)
                    continue;

                if (!sunLightEntityId.Equals(EntityId.None) && visibleLight.lightEntityId.Equals(sunLightEntityId))
                    return lightIndex;

                var lightIntensity = GetLightIntensity(visibleLight.finalColor);
                if (lightIntensity <= brightestDirectionalIntensity)
                    continue;

                brightestDirectionalIntensity = lightIntensity;
                brightestDirectionalIndex = lightIndex;
            }

            return brightestDirectionalIndex;
        }

        private bool IsValidLightIndex(int lightIndex)
        {
            return hasVisibleLights && lightIndex >= 0 && lightIndex < visibleLights.Length;
        }

        private bool IsValidDirectionalLightIndex(int lightIndex)
        {
            return hasDirectionalLights && directionalLights != null && lightIndex >= 0 && lightIndex < directionalLightCount;
        }

        private void EnsureDirectionalLightCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= directionalLights.Length)
                return;

            directionalLights = new DirectionalLightData[requiredCapacity];
        }

        private static int CountDirectionalLights(IReadOnlyList<Light> lights)
        {
            if (lights == null || lights.Count == 0)
                return 0;

            var directionalLightCount = 0;
            for (var lightIndex = 0; lightIndex < lights.Count; lightIndex++)
            {
                if (IsDirectionalLightSupported(lights[lightIndex]))
                    directionalLightCount++;
            }

            return directionalLightCount;
        }

        private static int CountDirectionalLights(NativeArray<VisibleLight> visibleLights)
        {
            if (!visibleLights.IsCreated || visibleLights.Length == 0)
                return 0;

            var directionalLightCount = 0;
            for (var lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
            {
                if (visibleLights[lightIndex].lightType == LightType.Directional)
                    directionalLightCount++;
            }

            return directionalLightCount;
        }

        private static DirectionalLightData CreateDirectionalLightData(Light light)
        {
            var finalColor = light.color.linear * light.intensity;
            return new DirectionalLightData
            {
                directionWS = -light.transform.forward,
                shadowStrength = light.shadows != LightShadows.None ? light.shadowStrength : 0f,
                color = new Vector3(finalColor.r, finalColor.g, finalColor.b),
                renderingLayerMask = (uint)light.renderingLayerMask,
            };
        }

        private static DirectionalLightData CreateDirectionalLightData(VisibleLight visibleLight)
        {
            var forward = visibleLight.localToWorldMatrix.GetColumn(2);
            var directionWS = new Vector3(-forward.x, -forward.y, -forward.z);
            var shadowStrength = visibleLight.light != null && visibleLight.light.shadows != LightShadows.None
                ? visibleLight.light.shadowStrength
                : 0f;
            var renderingLayerMask = visibleLight.light != null ? (uint)visibleLight.light.renderingLayerMask : 0u;

            return new DirectionalLightData
            {
                directionWS = directionWS,
                shadowStrength = shadowStrength,
                color = new Vector3(visibleLight.finalColor.r, visibleLight.finalColor.g, visibleLight.finalColor.b),
                renderingLayerMask = renderingLayerMask,
            };
        }

        private static bool IsDirectionalLightSupported(Light light)
        {
            return light != null
                   && light.type == LightType.Directional
                   && light.enabled
                   && light.gameObject.activeInHierarchy;
        }

        private static float GetLightIntensity(Color finalColor)
        {
            return Mathf.Max(finalColor.r, finalColor.g, finalColor.b);
        }

        private static float GetLightIntensity(Vector3 finalColor)
        {
            return Mathf.Max(finalColor.x, finalColor.y, finalColor.z);
        }
    }
}
