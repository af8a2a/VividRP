using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
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

        [StructLayout(LayoutKind.Sequential)]
        public struct PunctualLightData
        {
            public Vector3 positionWS;
            public float range;
            public Vector3 color;
            public uint lightType;
            public Vector3 directionWS;
            public float angleScale;
            public float angleOffset;
            public float inverseRangeSquared;
            public float shadowStrength;
            public uint renderingLayerMask;

            internal static int Stride => Marshal.SizeOf<PunctualLightData>();
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

        [Flags]
        private enum VisibleLightCollectionMask
        {
            None = 0,
            Directional = 1 << 0,
            Punctual = 1 << 1,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DirectionalLightCandidate
        {
            public int visibleLightIndex;
            public DirectionalLightData lightData;
            public float intensity;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PunctualLightCandidate
        {
            public int visibleLightIndex;
            public PunctualLightData lightData;
        }

        [BurstCompile]
        private struct BuildVisibleLightCandidatesJob : IJob
        {
            [ReadOnly]
            public NativeArray<VisibleLight> visibleLights;

            public bool collectDirectionalLights;
            public bool collectPunctualLights;
            public NativeList<DirectionalLightCandidate> directionalLights;
            public NativeList<PunctualLightCandidate> punctualLights;

            public void Execute()
            {
                for (var lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
                {
                    var visibleLight = visibleLights[lightIndex];

                    if (collectDirectionalLights && visibleLight.lightType == LightType.Directional)
                        directionalLights.AddNoResize(CreateDirectionalLightCandidate(lightIndex, visibleLight));

                    if (collectPunctualLights && IsPunctualLightSupported(visibleLight))
                        punctualLights.AddNoResize(CreatePunctualLightCandidate(lightIndex, visibleLight));
                }
            }
        }

        public NativeArray<VisibleLight> visibleLights;
        public NativeArray<VisibleReflectionProbe> visibleReflectionProbes;
        public DirectionalLightData[] directionalLights = Array.Empty<DirectionalLightData>();
        public PunctualLightData[] punctualLights = Array.Empty<PunctualLightData>();
        public int mainLightIndex;
        public EntityId mainLightEntityId;
        public int directionalLightCount;
        public int punctualLightCount;
        public int mainDirectionalLightIndex;
        public EntityId mainDirectionalLightEntityId;

        public bool hasVisibleLights => visibleLights.IsCreated && visibleLights.Length > 0;

        public bool hasVisibleReflectionProbes => visibleReflectionProbes.IsCreated && visibleReflectionProbes.Length > 0;

        public bool hasMainLight => IsValidLightIndex(mainLightIndex);

        public bool hasDirectionalLights => directionalLightCount > 0;

        public bool hasPunctualLights => punctualLightCount > 0;

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
            UpdateVisibleLightData(visibleLights, RenderSettings.sun, VisibleLightCollectionMask.Directional | VisibleLightCollectionMask.Punctual);
        }

        internal void UpdateDirectionalLights(NativeArray<VisibleLight> visibleLights, Light sunLight)
        {
            UpdateVisibleLightData(visibleLights, sunLight, VisibleLightCollectionMask.Directional);
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

        internal void UpdatePunctualLights(NativeArray<VisibleLight> visibleLights)
        {
            UpdateVisibleLightData(visibleLights, null, VisibleLightCollectionMask.Punctual);
        }

        internal void UpdatePunctualLights(IReadOnlyList<Light> lights)
        {
            EnsurePunctualLightCapacity(CountPunctualLights(lights));

            punctualLightCount = 0;

            if (lights == null || lights.Count == 0)
                return;

            for (var lightIndex = 0; lightIndex < lights.Count; lightIndex++)
            {
                var light = lights[lightIndex];
                if (!IsPunctualLightSupported(light))
                    continue;

                punctualLights[punctualLightCount] = CreatePunctualLightData(light);
                punctualLightCount++;
            }
        }

        public override void Reset()
        {
            visibleLights = default;
            visibleReflectionProbes = default;
            mainLightIndex = -1;
            mainLightEntityId = EntityId.None;
            directionalLightCount = 0;
            punctualLightCount = 0;
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

        private void EnsurePunctualLightCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= punctualLights.Length)
                return;

            punctualLights = new PunctualLightData[requiredCapacity];
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

        private static int CountPunctualLights(IReadOnlyList<Light> lights)
        {
            if (lights == null || lights.Count == 0)
                return 0;

            var count = 0;
            for (var lightIndex = 0; lightIndex < lights.Count; lightIndex++)
            {
                if (IsPunctualLightSupported(lights[lightIndex]))
                    count++;
            }

            return count;
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

        private static int CountPunctualLights(NativeArray<VisibleLight> visibleLights)
        {
            if (!visibleLights.IsCreated || visibleLights.Length == 0)
                return 0;

            var count = 0;
            for (var lightIndex = 0; lightIndex < visibleLights.Length; lightIndex++)
            {
                if (IsPunctualLightSupported(visibleLights[lightIndex]))
                    count++;
            }

            return count;
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
            var localToWorld = visibleLight.localToWorldMatrix;
            var directionWS = new Vector3(-localToWorld.m02, -localToWorld.m12, -localToWorld.m22);

            return new DirectionalLightData
            {
                directionWS = directionWS,
                shadowStrength = 0f,
                color = new Vector3(visibleLight.finalColor.r, visibleLight.finalColor.g, visibleLight.finalColor.b),
                renderingLayerMask = 0u,
            };
        }

        private static PunctualLightData CreatePunctualLightData(Light light)
        {
            var finalColor = light.color.linear * light.intensity;
            var lightType = GetPunctualLightType(light.type);
            var directionWS = light.transform.forward;
            var range = Mathf.Max(light.range, 0.001f);
            var inverseRangeSquared = 1.0f / Mathf.Max(range * range, 1e-6f);
            GetSpotAngleParameters(light.type, light.innerSpotAngle, light.spotAngle, out var angleScale, out var angleOffset);

            return new PunctualLightData
            {
                positionWS = light.transform.position,
                range = range,
                color = new Vector3(finalColor.r, finalColor.g, finalColor.b),
                lightType = lightType,
                directionWS = directionWS,
                angleScale = angleScale,
                angleOffset = angleOffset,
                inverseRangeSquared = inverseRangeSquared,
                shadowStrength = light.shadows != LightShadows.None ? light.shadowStrength : 0f,
                renderingLayerMask = (uint)light.renderingLayerMask,
            };
        }

        private static PunctualLightData CreatePunctualLightData(VisibleLight visibleLight)
        {
            var localToWorld = visibleLight.localToWorldMatrix;
            var range = Mathf.Max(visibleLight.range, 0.001f);
            var inverseRangeSquared = 1.0f / Mathf.Max(range * range, 1e-6f);
            GetSpotAngleParameters(visibleLight.lightType, visibleLight.innerSpotAngle, visibleLight.spotAngle, out var angleScale, out var angleOffset);

            return new PunctualLightData
            {
                positionWS = new Vector3(localToWorld.m03, localToWorld.m13, localToWorld.m23),
                range = range,
                color = new Vector3(visibleLight.finalColor.r, visibleLight.finalColor.g, visibleLight.finalColor.b),
                lightType = GetPunctualLightType(visibleLight.lightType),
                directionWS = new Vector3(localToWorld.m02, localToWorld.m12, localToWorld.m22),
                angleScale = angleScale,
                angleOffset = angleOffset,
                inverseRangeSquared = inverseRangeSquared,
                shadowStrength = 0f,
                renderingLayerMask = 0u,
            };
        }

        private void UpdateVisibleLightData(NativeArray<VisibleLight> visibleLights, Light sunLight, VisibleLightCollectionMask collectionMask)
        {
            var collectDirectionalLights = (collectionMask & VisibleLightCollectionMask.Directional) != 0;
            var collectPunctualLights = (collectionMask & VisibleLightCollectionMask.Punctual) != 0;

            if (collectDirectionalLights)
            {
                directionalLightCount = 0;
                mainLightIndex = -1;
                mainLightEntityId = EntityId.None;
                mainDirectionalLightIndex = -1;
                mainDirectionalLightEntityId = EntityId.None;
            }

            if (collectPunctualLights)
                punctualLightCount = 0;

            if (!visibleLights.IsCreated || visibleLights.Length == 0)
                return;

            var lightCapacity = Mathf.Max(visibleLights.Length, 1);
            using var directionalCandidates = new NativeList<DirectionalLightCandidate>(lightCapacity, Allocator.TempJob);
            using var punctualCandidates = new NativeList<PunctualLightCandidate>(lightCapacity, Allocator.TempJob);

            var buildCandidatesJob = new BuildVisibleLightCandidatesJob
            {
                visibleLights = visibleLights,
                collectDirectionalLights = collectDirectionalLights,
                collectPunctualLights = collectPunctualLights,
                directionalLights = directionalCandidates,
                punctualLights = punctualCandidates,
            };

            buildCandidatesJob.Schedule().Complete();

            if (collectDirectionalLights)
                ApplyDirectionalLightCandidates(visibleLights, directionalCandidates, sunLight);

            if (collectPunctualLights)
                ApplyPunctualLightCandidates(visibleLights, punctualCandidates);
        }

        private void ApplyDirectionalLightCandidates(
            NativeArray<VisibleLight> visibleLights,
            NativeList<DirectionalLightCandidate> directionalCandidates,
            Light sunLight)
        {
            EnsureDirectionalLightCapacity(directionalCandidates.Length);

            directionalLightCount = directionalCandidates.Length;
            mainLightIndex = -1;
            mainLightEntityId = EntityId.None;
            mainDirectionalLightIndex = -1;
            mainDirectionalLightEntityId = EntityId.None;

            var sunLightEntityId = sunLight != null ? sunLight.GetEntityId() : EntityId.None;
            var brightestDirectionalIntensity = float.NegativeInfinity;
            var brightestVisibleLightIndex = -1;
            var brightestDirectionalIndex = -1;
            var brightestDirectionalEntityId = EntityId.None;

            for (var directionalIndex = 0; directionalIndex < directionalLightCount; directionalIndex++)
            {
                var candidate = directionalCandidates[directionalIndex];
                var lightData = candidate.lightData;
                var visibleLight = visibleLights[candidate.visibleLightIndex];
                var light = visibleLight.light;
                var lightEntityId = EntityId.None;

                if (light != null)
                {
                    lightEntityId = light.GetEntityId();
                    lightData.shadowStrength = light.shadows != LightShadows.None ? light.shadowStrength : 0f;
                    lightData.renderingLayerMask = (uint)light.renderingLayerMask;

                    if (!sunLightEntityId.Equals(EntityId.None) && lightEntityId.Equals(sunLightEntityId))
                    {
                        mainLightIndex = candidate.visibleLightIndex;
                        mainLightEntityId = lightEntityId;
                        mainDirectionalLightIndex = directionalIndex;
                        mainDirectionalLightEntityId = lightEntityId;
                    }
                }

                directionalLights[directionalIndex] = lightData;

                if (candidate.intensity <= brightestDirectionalIntensity)
                    continue;

                brightestDirectionalIntensity = candidate.intensity;
                brightestVisibleLightIndex = candidate.visibleLightIndex;
                brightestDirectionalIndex = directionalIndex;
                brightestDirectionalEntityId = lightEntityId;
            }

            if (mainDirectionalLightIndex >= 0)
                return;

            mainLightIndex = brightestVisibleLightIndex;
            mainLightEntityId = brightestDirectionalEntityId;
            mainDirectionalLightIndex = brightestDirectionalIndex;
            mainDirectionalLightEntityId = brightestDirectionalEntityId;
        }

        private void ApplyPunctualLightCandidates(
            NativeArray<VisibleLight> visibleLights,
            NativeList<PunctualLightCandidate> punctualCandidates)
        {
            EnsurePunctualLightCapacity(punctualCandidates.Length);

            punctualLightCount = punctualCandidates.Length;

            for (var punctualIndex = 0; punctualIndex < punctualLightCount; punctualIndex++)
            {
                var candidate = punctualCandidates[punctualIndex];
                var lightData = candidate.lightData;
                var light = visibleLights[candidate.visibleLightIndex].light;

                if (light != null)
                {
                    lightData.shadowStrength = light.shadows != LightShadows.None ? light.shadowStrength : 0f;
                    lightData.renderingLayerMask = (uint)light.renderingLayerMask;
                }

                punctualLights[punctualIndex] = lightData;
            }
        }

        private static DirectionalLightCandidate CreateDirectionalLightCandidate(int visibleLightIndex, VisibleLight visibleLight)
        {
            return new DirectionalLightCandidate
            {
                visibleLightIndex = visibleLightIndex,
                lightData = CreateDirectionalLightData(visibleLight),
                intensity = GetLightIntensity(visibleLight.finalColor),
            };
        }

        private static PunctualLightCandidate CreatePunctualLightCandidate(int visibleLightIndex, VisibleLight visibleLight)
        {
            return new PunctualLightCandidate
            {
                visibleLightIndex = visibleLightIndex,
                lightData = CreatePunctualLightData(visibleLight),
            };
        }

        private static bool IsDirectionalLightSupported(Light light)
        {
            return light != null
                   && light.type == LightType.Directional
                   && light.enabled
                   && light.gameObject.activeInHierarchy;
        }

        private static bool IsPunctualLightSupported(Light light)
        {
            return light != null
                   && (light.type == LightType.Point || light.type == LightType.Spot)
                   && light.enabled
                   && light.gameObject.activeInHierarchy
                   && light.range > 0.0f;
        }

        private static bool IsPunctualLightSupported(VisibleLight visibleLight)
        {
            return (visibleLight.lightType == LightType.Point || visibleLight.lightType == LightType.Spot)
                   && visibleLight.range > 0.0f;
        }

        private static uint GetPunctualLightType(LightType lightType)
        {
            return lightType == LightType.Spot ? 1u : 0u;
        }

        private static void GetSpotAngleParameters(LightType lightType, float innerSpotAngle, float outerSpotAngle, out float angleScale, out float angleOffset)
        {
            if (lightType != LightType.Spot)
            {
                angleScale = 0.0f;
                angleOffset = 1.0f;
                return;
            }

            var innerHalfAngle = Mathf.Clamp(innerSpotAngle * 0.5f, 0.0f, 89.0f) * Mathf.Deg2Rad;
            var outerHalfAngle = Mathf.Clamp(outerSpotAngle * 0.5f, innerSpotAngle * 0.5f + 0.001f, 89.0f) * Mathf.Deg2Rad;
            var cosInner = Mathf.Cos(innerHalfAngle);
            var cosOuter = Mathf.Cos(outerHalfAngle);
            var angleRange = Mathf.Max(cosInner - cosOuter, 0.001f);

            angleScale = 1.0f / angleRange;
            angleOffset = -cosOuter * angleScale;
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
