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
    [Flags]
    public enum VividLightRenderDataFlags : uint
    {
        None = 0,
        UsePipelineSettings = 1u << 0,
        CustomShadowLayers = 1u << 1,
        Enabled = 1u << 2,
        ActiveInHierarchy = 1u << 3,
        CastShadows = 1u << 4,
        AffectVolumetric = 1u << 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VividLightRenderData
    {
        public EntityId lightEntityId;
        public LightType lightType;
        public Vector3 positionWS;
        public float range;
        public Vector3 forwardWS;
        public Vector3 rightWS;
        public Vector3 upWS;
        public Vector2 areaSize;
        public float shapeRadius;
        public float barnDoorAngle;
        public float barnDoorLength;
        public float volumetricDimmer;
        public float volumetricFadeDistance;
        public float volumetricShadowDimmer;
        public float intensity;
        public Vector3 color;
        public float shadowStrength;
        public float spotAngle;
        public float innerSpotAngle;
        public float rangeAttenuationScale;
        public float rangeAttenuationBias;
        public uint renderingLayerMask;
        public uint shadowRenderingLayerMask;
        public VividLightRenderDataFlags flags;
    }

    public sealed class VividLightRenderDatabase
    {
        private const int k_InlinePrepareLightThreshold = 64;

        private readonly List<VividAdditionalLightData> m_RegisteredAdditionalLightData = new();
        private readonly List<VividLightRenderData> m_LightData = new();
        private readonly List<VividLightRenderData> m_PreparedSceneLightData = new();
        private readonly Dictionary<EntityId, int> m_EntityIdToDataIndex = new();
        private NativeArray<VividLightRenderData> m_SceneLightSources;
        private NativeList<VividLightRenderData> m_PreparedSceneLightNativeData;
        private JobHandle m_PrepareSceneLightJobHandle;
        private bool m_PrepareSceneLightScheduled;
        private bool m_PreparedSceneLightPending;
        private bool m_SceneLightKickRan;

        public static VividLightRenderDatabase instance => Singleton<VividLightRenderDatabase>.instance;

        public int lightCount => m_LightData.Count;

        public IReadOnlyList<VividLightRenderData> lightData => m_LightData;

        internal IReadOnlyList<VividLightRenderData> sceneLightData => m_PreparedSceneLightData;

        internal VividLightRenderData RegisterLight(VividAdditionalLightData additionalLightData)
        {
            if (additionalLightData == null)
                return default;

            RegisterAdditionalLightData(additionalLightData);
            return UpdateLightData(additionalLightData.light, additionalLightData);
        }

        internal VividLightRenderData RegisterLight(Light light)
        {
            return UpdateLightData(light);
        }

        internal VividLightRenderData UpdateLightData(VividAdditionalLightData additionalLightData)
        {
            if (additionalLightData == null)
                return default;

            return UpdateLightData(additionalLightData.light, additionalLightData);
        }

        internal VividLightRenderData UpdateLightData(Light light, VividAdditionalLightData additionalLightData = null)
        {
            if (!light)
                return default;

            if (!additionalLightData && !light.TryGetComponent(out additionalLightData))
                additionalLightData = null;

            if (additionalLightData && additionalLightData.isActiveAndEnabled)
                RegisterAdditionalLightData(additionalLightData);

            var trackedLightData = CreateLightRenderData(light, additionalLightData);
            StoreLightData(trackedLightData);
            return trackedLightData;
        }

        internal bool TryGetLightData(Light light, out VividLightRenderData trackedLightData)
        {
            trackedLightData = default;

            if (light == null)
                return false;

            return TryGetLightData(light.GetEntityId(), out trackedLightData);
        }

        internal bool TryGetLightData(EntityId lightEntityId, out VividLightRenderData trackedLightData)
        {
            trackedLightData = default;

            if (lightEntityId.Equals(EntityId.None))
                return false;

            return m_EntityIdToDataIndex.TryGetValue(lightEntityId, out var dataIndex)
                   && TryGetLightData(dataIndex, out trackedLightData);
        }

        internal void UnregisterLight(VividAdditionalLightData additionalLightData)
        {
            if (additionalLightData == null)
                return;

            m_RegisteredAdditionalLightData.Remove(additionalLightData);
            UnregisterLight(additionalLightData.light);
        }

        internal void UnregisterLight(Light light)
        {
            if (light == null)
                return;

            RemoveRegisteredLight(light);

            var lightEntityId = light.GetEntityId();
            if (lightEntityId.Equals(EntityId.None)
                || !m_EntityIdToDataIndex.TryGetValue(lightEntityId, out var removedIndex))
                return;

            var removedLightData = m_LightData[removedIndex];
            RemoveLookups(removedLightData);

            var lastIndex = m_LightData.Count - 1;
            if (removedIndex != lastIndex)
            {
                var lastLightData = m_LightData[lastIndex];
                m_LightData[removedIndex] = lastLightData;
                m_EntityIdToDataIndex[lastLightData.lightEntityId] = removedIndex;
            }

            m_LightData.RemoveAt(lastIndex);
        }

        internal void Clear()
        {
            CompleteScheduledSceneLightPrepare(false);
            m_RegisteredAdditionalLightData.Clear();
            m_LightData.Clear();
            m_PreparedSceneLightData.Clear();
            m_EntityIdToDataIndex.Clear();
            m_PreparedSceneLightPending = false;
            m_SceneLightKickRan = false;
            if (m_PreparedSceneLightNativeData.IsCreated)
                m_PreparedSceneLightNativeData.Clear();
        }

        internal void BuildSceneLightSnapshotAndSchedulePrepare(bool allowAsyncSchedule)
        {
            CompleteScheduledSceneLightPrepare(true);

            var registeredCount = m_RegisteredAdditionalLightData.Count;
            EnsureSceneLightPrepareCapacity(registeredCount);

            var sourceCount = 0;
            for (var lightIndex = 0; lightIndex < registeredCount; lightIndex++)
            {
                var additionalLightData = m_RegisteredAdditionalLightData[lightIndex];
                if (additionalLightData == null)
                    continue;

                var light = additionalLightData.light;
                if (light == null)
                    continue;

                var trackedLightData = CreateLightRenderData(light, additionalLightData);
                StoreLightData(trackedLightData);
                m_SceneLightSources[sourceCount++] = trackedLightData;
            }

            m_SceneLightKickRan = true;

            if (m_PreparedSceneLightNativeData.IsCreated)
                m_PreparedSceneLightNativeData.Clear();

            if (sourceCount == 0)
            {
                m_PreparedSceneLightData.Clear();
                m_PreparedSceneLightPending = false;
                return;
            }

            var job = new PrepareSceneLightsJob
            {
                SourceLights = m_SceneLightSources.GetSubArray(0, sourceCount),
                PreparedLights = m_PreparedSceneLightNativeData,
            };

            m_PreparedSceneLightPending = true;
            if (!allowAsyncSchedule || ShouldRunSceneLightPrepareInline(sourceCount))
            {
                job.Run();
                return;
            }

            m_PrepareSceneLightJobHandle = job.Schedule();
            m_PrepareSceneLightScheduled = true;
            JobHandle.ScheduleBatchedJobs();
        }

        internal void CompleteSceneLightPrepare()
        {
            if (!m_SceneLightKickRan)
                BuildSceneLightSnapshotAndSchedulePrepare(false);

            CompleteScheduledSceneLightPrepare(true);
        }

        internal void ReleaseSceneLightPrepareResources()
        {
            CompleteScheduledSceneLightPrepare(false);

            if (m_SceneLightSources.IsCreated)
                m_SceneLightSources.Dispose();

            if (m_PreparedSceneLightNativeData.IsCreated)
                m_PreparedSceneLightNativeData.Dispose();

            m_SceneLightSources = default;
            m_PreparedSceneLightNativeData = default;
            m_PreparedSceneLightData.Clear();
            m_PreparedSceneLightPending = false;
            m_SceneLightKickRan = false;
        }

        private bool TryGetLightData(int dataIndex, out VividLightRenderData trackedLightData)
        {
            trackedLightData = default;

            if (dataIndex < 0 || dataIndex >= m_LightData.Count)
                return false;

            trackedLightData = m_LightData[dataIndex];
            return true;
        }

        private void RegisterAdditionalLightData(VividAdditionalLightData additionalLightData)
        {
            if (additionalLightData == null)
                return;

            VividSceneLightSystem.EnsureInitialized();

            if (!m_RegisteredAdditionalLightData.Contains(additionalLightData))
                m_RegisteredAdditionalLightData.Add(additionalLightData);
        }

        private void RemoveRegisteredLight(Light light)
        {
            for (var lightIndex = m_RegisteredAdditionalLightData.Count - 1; lightIndex >= 0; lightIndex--)
            {
                var additionalLightData = m_RegisteredAdditionalLightData[lightIndex];
                if (additionalLightData == null || additionalLightData.light == light)
                    m_RegisteredAdditionalLightData.RemoveAt(lightIndex);
            }
        }

        private static bool ShouldRunSceneLightPrepareInline(int lightCount)
        {
            return lightCount <= k_InlinePrepareLightThreshold;
        }

        private void EnsureSceneLightPrepareCapacity(int requiredCapacity)
        {
            requiredCapacity = Mathf.Max(requiredCapacity, 1);
            var targetCapacity = Mathf.Max(8, Mathf.NextPowerOfTwo(requiredCapacity));

            if (!m_SceneLightSources.IsCreated || m_SceneLightSources.Length < targetCapacity)
            {
                if (m_SceneLightSources.IsCreated)
                    m_SceneLightSources.Dispose();

                m_SceneLightSources = new NativeArray<VividLightRenderData>(
                    targetCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }

            if (!m_PreparedSceneLightNativeData.IsCreated)
            {
                m_PreparedSceneLightNativeData = new NativeList<VividLightRenderData>(targetCapacity, Allocator.Persistent);
                return;
            }

            if (m_PreparedSceneLightNativeData.Capacity < targetCapacity)
                m_PreparedSceneLightNativeData.Capacity = targetCapacity;
        }

        private void CompleteScheduledSceneLightPrepare(bool applyResult)
        {
            if (m_PrepareSceneLightScheduled)
                m_PrepareSceneLightJobHandle.Complete();

            m_PrepareSceneLightJobHandle = default;
            m_PrepareSceneLightScheduled = false;

            if (!m_PreparedSceneLightPending)
                return;

            if (applyResult)
                ApplyPreparedSceneLightData();

            m_PreparedSceneLightPending = false;
        }

        private void ApplyPreparedSceneLightData()
        {
            m_PreparedSceneLightData.Clear();

            if (!m_PreparedSceneLightNativeData.IsCreated)
                return;

            for (var lightIndex = 0; lightIndex < m_PreparedSceneLightNativeData.Length; lightIndex++)
                m_PreparedSceneLightData.Add(m_PreparedSceneLightNativeData[lightIndex]);
        }

        private void StoreLightData(VividLightRenderData trackedLightData)
        {
            if (trackedLightData.lightEntityId.Equals(EntityId.None))
                return;

            if (m_EntityIdToDataIndex.TryGetValue(trackedLightData.lightEntityId, out var dataIndex))
            {
                m_LightData[dataIndex] = trackedLightData;
            }
            else
            {
                dataIndex = m_LightData.Count;
                m_EntityIdToDataIndex.Add(trackedLightData.lightEntityId, dataIndex);
                m_LightData.Add(trackedLightData);
            }

            m_EntityIdToDataIndex[trackedLightData.lightEntityId] = dataIndex;
        }

        private void RemoveLookups(VividLightRenderData trackedLightData)
        {
            if (!trackedLightData.lightEntityId.Equals(EntityId.None))
                m_EntityIdToDataIndex.Remove(trackedLightData.lightEntityId);
        }

        [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private struct PrepareSceneLightsJob : IJob
        {
            [ReadOnly]
            public NativeArray<VividLightRenderData> SourceLights;

            public NativeList<VividLightRenderData> PreparedLights;

            public void Execute()
            {
                const VividLightRenderDataFlags requiredFlags =
                    VividLightRenderDataFlags.Enabled | VividLightRenderDataFlags.ActiveInHierarchy;

                for (var lightIndex = 0; lightIndex < SourceLights.Length; lightIndex++)
                {
                    var lightData = SourceLights[lightIndex];
                    if ((lightData.flags & requiredFlags) != requiredFlags)
                        continue;

                    PreparedLights.AddNoResize(lightData);
                }
            }
        }

        private static VividLightRenderData CreateLightRenderData(Light light, VividAdditionalLightData additionalLightData)
        {
            var nativeIntensity = Mathf.Max(light.intensity, 0.0f);
            var finalColor = EvaluateLightColor(light);

            var range = Mathf.Max(light.range, 0.0f);
            var rangeAttenuationScale = range > 0.0f ? 1.0f / Mathf.Max(range * range, 1e-6f) : 0.0f;
            var shadowRenderingLayerMask = additionalLightData
                ? additionalLightData.effectiveShadowRenderingLayers
                : (RenderingLayerMask)light.renderingLayerMask;

            return new VividLightRenderData
            {
                lightEntityId = light.GetEntityId(),
                lightType = light.type,
                positionWS = light.transform.position,
                range = range,
                forwardWS = light.transform.forward,
                rightWS = light.transform.right,
                upWS = light.transform.up,
                areaSize = ResolveAreaSize(light),
                shapeRadius = Mathf.Max(light.shapeRadius, 0.0f),
                barnDoorAngle = additionalLightData != null
                    ? additionalLightData.barnDoorAngle
                    : VividAdditionalLightData.DefaultBarnDoorAngle,
                barnDoorLength = additionalLightData != null
                    ? additionalLightData.barnDoorLength
                    : VividAdditionalLightData.DefaultBarnDoorLength,
                volumetricDimmer = additionalLightData != null
                    ? additionalLightData.volumetricDimmer
                    : VividAdditionalLightData.DefaultVolumetricDimmer,
                volumetricFadeDistance = additionalLightData != null
                    ? additionalLightData.volumetricFadeDistance
                    : VividAdditionalLightData.DefaultVolumetricFadeDistance,
                volumetricShadowDimmer = additionalLightData != null
                    ? additionalLightData.volumetricShadowDimmer
                    : VividAdditionalLightData.DefaultVolumetricShadowDimmer,
                intensity = nativeIntensity,
                color = new Vector3(finalColor.r, finalColor.g, finalColor.b),
                shadowStrength = light.shadows != LightShadows.None ? light.shadowStrength : 0.0f,
                spotAngle = light.spotAngle,
                innerSpotAngle = light.innerSpotAngle,
                rangeAttenuationScale = rangeAttenuationScale,
                rangeAttenuationBias = 1.0f,
                renderingLayerMask = (uint)light.renderingLayerMask,
                shadowRenderingLayerMask = (uint)shadowRenderingLayerMask,
                flags = BuildFlags(light, additionalLightData),
            };
        }

        internal static Color EvaluateLightColor(Light light)
        {
            if (!light)
                return Color.black;

            var finalColor = light.useColorTemperature ? Color.white : light.color.linear;
            finalColor *= Mathf.Max(light.intensity, 0.0f);
            if (light.useColorTemperature)
                finalColor *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);

            return finalColor;
        }

        private static VividLightRenderDataFlags BuildFlags(Light light, VividAdditionalLightData additionalLightData)
        {
            var flags = VividLightRenderDataFlags.None;

            if (additionalLightData == null || additionalLightData.usePipelineSettings)
                flags |= VividLightRenderDataFlags.UsePipelineSettings;

            if (additionalLightData != null && additionalLightData.customShadowLayers)
                flags |= VividLightRenderDataFlags.CustomShadowLayers;

            if (light.enabled)
                flags |= VividLightRenderDataFlags.Enabled;

            if (light.gameObject.activeInHierarchy)
                flags |= VividLightRenderDataFlags.ActiveInHierarchy;

            if (light.shadows != LightShadows.None)
                flags |= VividLightRenderDataFlags.CastShadows;

            if (additionalLightData == null || additionalLightData.affectsVolumetric)
                flags |= VividLightRenderDataFlags.AffectVolumetric;

            return flags;
        }

        internal static bool IsLightDataChanged(Light light, VividAdditionalLightData additionalLightData, in VividLightRenderData trackedLightData)
        {
            if (light == null)
                return false;

            var currentLightData = CreateLightRenderData(light, additionalLightData);
            return !LightDataEquals(in currentLightData, in trackedLightData);
        }

        private static bool LightDataEquals(in VividLightRenderData lhs, in VividLightRenderData rhs)
        {
            return lhs.lightEntityId.Equals(rhs.lightEntityId)
                   && lhs.lightType == rhs.lightType
                   && Approximately(lhs.positionWS, rhs.positionWS)
                   && Mathf.Approximately(lhs.range, rhs.range)
                   && Approximately(lhs.forwardWS, rhs.forwardWS)
                   && Approximately(lhs.rightWS, rhs.rightWS)
                   && Approximately(lhs.upWS, rhs.upWS)
                   && Approximately(lhs.areaSize, rhs.areaSize)
                   && Mathf.Approximately(lhs.shapeRadius, rhs.shapeRadius)
                   && Mathf.Approximately(lhs.barnDoorAngle, rhs.barnDoorAngle)
                   && Mathf.Approximately(lhs.barnDoorLength, rhs.barnDoorLength)
                   && Mathf.Approximately(lhs.volumetricDimmer, rhs.volumetricDimmer)
                   && Mathf.Approximately(lhs.volumetricFadeDistance, rhs.volumetricFadeDistance)
                   && Mathf.Approximately(lhs.volumetricShadowDimmer, rhs.volumetricShadowDimmer)
                   && Mathf.Approximately(lhs.intensity, rhs.intensity)
                   && Approximately(lhs.color, rhs.color)
                   && Mathf.Approximately(lhs.shadowStrength, rhs.shadowStrength)
                   && Mathf.Approximately(lhs.spotAngle, rhs.spotAngle)
                   && Mathf.Approximately(lhs.innerSpotAngle, rhs.innerSpotAngle)
                   && Mathf.Approximately(lhs.rangeAttenuationScale, rhs.rangeAttenuationScale)
                   && Mathf.Approximately(lhs.rangeAttenuationBias, rhs.rangeAttenuationBias)
                   && lhs.renderingLayerMask == rhs.renderingLayerMask
                   && lhs.shadowRenderingLayerMask == rhs.shadowRenderingLayerMask
                   && lhs.flags == rhs.flags;
        }

        private static bool Approximately(Vector3 lhs, Vector3 rhs)
        {
            return Mathf.Approximately(lhs.x, rhs.x)
                   && Mathf.Approximately(lhs.y, rhs.y)
                   && Mathf.Approximately(lhs.z, rhs.z);
        }

        private static bool Approximately(Vector2 lhs, Vector2 rhs)
        {
            return Mathf.Approximately(lhs.x, rhs.x)
                   && Mathf.Approximately(lhs.y, rhs.y);
        }

        private static Vector2 ResolveAreaSize(Light light)
        {
            if (light == null)
                return Vector2.zero;

            return light.type switch
            {
                LightType.Rectangle => new Vector2(
                    Mathf.Max(light.areaSize.x, 0.0f),
                    Mathf.Max(light.areaSize.y, 0.0f)),
                LightType.Tube => new Vector2(
                    Mathf.Max(light.areaSize.x, 0.0f),
                    0.0f),
                LightType.Disc => Vector2.one * Mathf.Max(light.shapeRadius * 2.0f, 0.0f),
                _ => Vector2.zero,
            };
        }
    }

    public static class VividLightExtensions
    {
        public static VividAdditionalLightData GetVividAdditionalLightData(this Light light)
        {
            if (light == null)
                throw new ArgumentNullException(nameof(light));

            var gameObject = light.gameObject;
            if (!gameObject.TryGetComponent<VividAdditionalLightData>(out var lightData))
                lightData = gameObject.AddComponent<VividAdditionalLightData>();

            return lightData;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [ExecuteAlways]
    public class VividAdditionalLightData : MonoBehaviour, IAdditionalData
    {
        public enum CSMScreenSpaceShadowQuality
        {
            Low = 0,
            Medium = 1,
            High = 2,
            VeryHigh = 3,
        }

        public enum CSMShadowAtlasResolution
        {
            Resolution1024 = 1024,
            Resolution2048 = 2048,
            Resolution4096 = 4096,
            Resolution8192 = 8192,
        }

        public enum CelestialBodyShadingSource
        {
            Emission = 0,
            ReflectSunLight = 1,
            Manual = 2,
        }

        internal readonly struct TimeOfDaySunState
        {
            public TimeOfDaySunState(Vector3 directionToSun, float elevationDegrees, float azimuthDegrees, float lux)
            {
                this.directionToSun = directionToSun;
                this.elevationDegrees = elevationDegrees;
                this.azimuthDegrees = azimuthDegrees;
                this.lux = lux;
            }

            public Vector3 directionToSun { get; }

            public float elevationDegrees { get; }

            public float azimuthDegrees { get; }

            public float lux { get; }
        }

        internal const float DefaultRayTracedShadowRayLength = 1000f;
        internal const float DefaultRayTracedShadowRayBias = 0.001f;
        internal const float DefaultRayTracedShadowDistantRayBias = 0.001f;
        internal const float DefaultRayTracedShadowSunAngularDiameter = 0.533f;
        internal const int DefaultShadowAtlasResolution = 4096;
        internal const CSMScreenSpaceShadowQuality DefaultScreenSpaceShadowQuality = CSMScreenSpaceShadowQuality.Low;
        internal const float DefaultShadowDepthBias = 1.0f;
        internal const float DefaultShadowNormalBias = 1.0f;
        internal const float DefaultShadowSlopeBias = 2.5f;
        internal const float MaxShadowDepthBias = 10.0f;
        internal const float MaxShadowNormalBias = 10.0f;
        internal const float MaxShadowSlopeBias = 5.0f;
        internal const int MinPCSSSampleCount = 1;
        internal const int MaxPCSSSampleCount = 64;
        internal const int DefaultDirLightPCSSBlockerSampleCount = 24;
        internal const int DefaultDirLightPCSSFilterSampleCount = 32;
        internal const float DefaultDirLightPCSSMaxPenumbraSize = 0.56f;
        internal const float DefaultDirLightPCSSMaxSamplingDistance = 0.5f;
        internal const float DefaultDirLightPCSSMinFilterSizeTexels = 1.5f;
        internal const float DefaultDirLightPCSSMinFilterMaxAngularDiameter = 10.0f;
        internal const float DefaultDirLightPCSSBlockerSearchAngularDiameter = 12.0f;
        internal const float MinDirLightPCSSBlockerSamplingClumpExponent = 1.0f;
        internal const float MaxDirLightPCSSBlockerSamplingClumpExponent = 6.0f;
        internal const float DefaultDirLightPCSSBlockerSamplingClumpExponent = 2.0f;
        internal const float DefaultBarnDoorAngle = 90.0f;
        internal const float DefaultBarnDoorLength = 0.05f;
        internal const float DefaultVolumetricDimmer = 1.0f;
        internal const float MaxVolumetricDimmer = 16.0f;
        internal const float DefaultVolumetricFadeDistance = 10000.0f;
        internal const float DefaultVolumetricShadowDimmer = 1.0f;
        internal const float DefaultCelestialBodyAngularDiameter = 0.5f;
        internal const float DefaultCelestialBodyDistance = 149597870700.0f;
        internal const float DefaultManualSunIntensity = 130000.0f;
        internal const float DefaultTimeOfDay = 12.0f;
        internal const float DefaultTimeOfDayMaximumLux = DefaultManualSunIntensity;
        private const float TimeOfDayHours = 24.0f;
        private const float TimeOfDaySunrise = 6.0f;
        private const float TimeOfDayDaylightDuration = 12.0f;
        private const float TimeOfDayHorizonFadeDegrees = 5.0f;
        private const float TimeOfDayAtmosphereExtinction = 0.14f;

        [SerializeField]
        private bool m_UsePipelineSettings = true;

        [SerializeField]
        private bool m_CustomShadowLayers;

        [SerializeField]
        private RenderingLayerMask m_ShadowRenderingLayersMask = RenderingLayerMask.defaultRenderingLayerMask;

        [SerializeField]
        private bool m_EnableRayTracedShadow;

        [SerializeField]
        private float m_RayTracedShadowRayLength = DefaultRayTracedShadowRayLength;

        [SerializeField]
        private float m_RayTracedShadowRayBias = DefaultRayTracedShadowRayBias;

        [SerializeField]
        private float m_RayTracedShadowDistantRayBias = DefaultRayTracedShadowDistantRayBias;

        [SerializeField]
        private float m_RayTracedShadowSunAngularDiameter = DefaultRayTracedShadowSunAngularDiameter;

        [SerializeField]
        private CSMScreenSpaceShadowQuality m_ScreenSpaceShadowQuality = DefaultScreenSpaceShadowQuality;

        [SerializeField]
        private CSMShadowAtlasResolution m_ShadowAtlasResolution = CSMShadowAtlasResolution.Resolution4096;

        [SerializeField]
        private float m_DepthBias = DefaultShadowDepthBias;

        [SerializeField]
        private float m_NormalBias = DefaultShadowNormalBias;

        [SerializeField]
        private float m_SlopeBias = DefaultShadowSlopeBias;

        [SerializeField, Range(MinPCSSSampleCount, MaxPCSSSampleCount)]
        private int m_DirLightPCSSBlockerSampleCount = DefaultDirLightPCSSBlockerSampleCount;

        [SerializeField, Range(MinPCSSSampleCount, MaxPCSSSampleCount)]
        private int m_DirLightPCSSFilterSampleCount = DefaultDirLightPCSSFilterSampleCount;

        [SerializeField]
        private float m_DirLightPCSSMaxPenumbraSize = DefaultDirLightPCSSMaxPenumbraSize;

        [SerializeField]
        private float m_DirLightPCSSMaxSamplingDistance = DefaultDirLightPCSSMaxSamplingDistance;

        [SerializeField]
        private float m_DirLightPCSSMinFilterSizeTexels = DefaultDirLightPCSSMinFilterSizeTexels;

        [SerializeField]
        private float m_DirLightPCSSMinFilterMaxAngularDiameter = DefaultDirLightPCSSMinFilterMaxAngularDiameter;

        [SerializeField]
        private float m_DirLightPCSSBlockerSearchAngularDiameter = DefaultDirLightPCSSBlockerSearchAngularDiameter;

        [SerializeField, Range(MinDirLightPCSSBlockerSamplingClumpExponent, MaxDirLightPCSSBlockerSamplingClumpExponent)]
        private float m_DirLightPCSSBlockerSamplingClumpExponent = DefaultDirLightPCSSBlockerSamplingClumpExponent;

        [SerializeField, Range(0.0f, 90.0f)]
        private float m_BarnDoorAngle = DefaultBarnDoorAngle;

        [SerializeField, Min(0.0f)]
        private float m_BarnDoorLength = DefaultBarnDoorLength;

        [SerializeField]
        private bool m_AffectsVolumetric = true;

        [SerializeField, Range(0.0f, MaxVolumetricDimmer)]
        private float m_VolumetricDimmer = DefaultVolumetricDimmer;

        [SerializeField, Min(0.0f)]
        private float m_VolumetricFadeDistance = DefaultVolumetricFadeDistance;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_VolumetricShadowDimmer = DefaultVolumetricShadowDimmer;

        [SerializeField]
        private bool m_InteractsWithSky = true;

        [SerializeField]
        private bool m_EnableTimeOfDay;

        [SerializeField, Range(0.0f, TimeOfDayHours)]
        private float m_TimeOfDay = DefaultTimeOfDay;

        [SerializeField, Min(0.0f)]
        private float m_TimeOfDayMaximumLux = DefaultTimeOfDayMaximumLux;

        [SerializeField, Range(0.0f, 90.0f)]
        private float m_AngularDiameter = DefaultCelestialBodyAngularDiameter;

        [SerializeField]
        private bool m_DiameterMultiplierMode;

        [SerializeField, Min(0.0f)]
        private float m_DiameterMultiplier = 1.0f;

        [SerializeField, Min(0.0f)]
        private float m_DiameterOverride = DefaultCelestialBodyAngularDiameter;

        [SerializeField]
        private CelestialBodyShadingSource m_CelestialBodyShadingSource = CelestialBodyShadingSource.Emission;

        [SerializeField]
        private Light m_SunLightOverride;

        [SerializeField]
        private Color m_SunColor = Color.white;

        [SerializeField, Min(0.0f)]
        private float m_SunIntensity = DefaultManualSunIntensity;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_MoonPhase = 0.2f;

        [SerializeField, Range(0.0f, 360.0f)]
        private float m_MoonPhaseRotation;

        [SerializeField, Min(0.0f)]
        private float m_Earthshine = 1.0f;

        [SerializeField, Range(0.0f, 90.0f)]
        private float m_FlareSize = 2.0f;

        [SerializeField]
        private Color m_FlareTint = Color.white;

        [SerializeField, Min(0.0f)]
        private float m_FlareFalloff = 4.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_FlareMultiplier = 1.0f;

        [SerializeField]
        private Texture m_SurfaceTexture;

        [SerializeField]
        private Color m_SurfaceTint = Color.white;

        [SerializeField, Min(0.0f)]
        private float m_Distance = DefaultCelestialBodyDistance;

        [NonSerialized]
        private bool m_Animated;

        private Light m_Light;

        internal Light light
        {
            get
            {
                if (m_Light == null)
                    TryGetComponent(out m_Light);

                return m_Light;
            }
        }

        public bool usePipelineSettings
        {
            get => m_UsePipelineSettings;
            set
            {
                if (m_UsePipelineSettings == value)
                    return;

                m_UsePipelineSettings = value;
                NotifyLightDataChanged();
            }
        }

        public bool customShadowLayers
        {
            get => m_CustomShadowLayers;
            set
            {
                if (m_CustomShadowLayers == value)
                    return;

                m_CustomShadowLayers = value;
                NotifyLightDataChanged();
            }
        }

        public RenderingLayerMask shadowRenderingLayers
        {
            get => m_ShadowRenderingLayersMask;
            set
            {
                if (m_ShadowRenderingLayersMask == value)
                    return;

                m_ShadowRenderingLayersMask = value;
                NotifyLightDataChanged();
            }
        }

        public RenderingLayerMask effectiveShadowRenderingLayers
        {
            get
            {
                if (m_CustomShadowLayers)
                    return m_ShadowRenderingLayersMask;

                return light != null ? (RenderingLayerMask)light.renderingLayerMask : RenderingLayerMask.defaultRenderingLayerMask;
            }
        }

        public bool enableRayTracedShadow
        {
            get => m_EnableRayTracedShadow;
            set
            {
                if (m_EnableRayTracedShadow == value)
                    return;

                m_EnableRayTracedShadow = value;
                NotifyLightDataChanged();
            }
        }

        public float rayTracedShadowRayLength
        {
            get => m_RayTracedShadowRayLength;
            set => SetRayTracedShadowFloat(
                ref m_RayTracedShadowRayLength,
                value,
                DefaultRayTracedShadowRayLength);
        }

        public float rayTracedShadowRayBias
        {
            get => m_RayTracedShadowRayBias;
            set => SetRayTracedShadowFloat(
                ref m_RayTracedShadowRayBias,
                value,
                DefaultRayTracedShadowRayBias);
        }

        public float rayTracedShadowDistantRayBias
        {
            get => m_RayTracedShadowDistantRayBias;
            set => SetRayTracedShadowFloat(
                ref m_RayTracedShadowDistantRayBias,
                value,
                DefaultRayTracedShadowDistantRayBias);
        }

        public float rayTracedShadowSunAngularDiameter
        {
            get => m_RayTracedShadowSunAngularDiameter;
            set => SetRayTracedShadowFloat(
                ref m_RayTracedShadowSunAngularDiameter,
                value,
                DefaultRayTracedShadowSunAngularDiameter);
        }

        public CSMScreenSpaceShadowQuality screenSpaceShadowQuality
        {
            get => SanitizeScreenSpaceShadowQuality(m_ScreenSpaceShadowQuality);
            set
            {
                var sanitizedValue = SanitizeScreenSpaceShadowQuality(value);
                if (m_ScreenSpaceShadowQuality == sanitizedValue)
                    return;

                m_ScreenSpaceShadowQuality = sanitizedValue;
                NotifyLightDataChanged();
            }
        }

        public CSMShadowAtlasResolution shadowAtlasResolution
        {
            get => SanitizeShadowAtlasResolution(m_ShadowAtlasResolution);
            set
            {
                var sanitizedValue = SanitizeShadowAtlasResolution(value);
                if (m_ShadowAtlasResolution == sanitizedValue)
                    return;

                m_ShadowAtlasResolution = sanitizedValue;
                NotifyLightDataChanged();
            }
        }

        internal int resolvedShadowAtlasResolution => (int)SanitizeShadowAtlasResolution(m_ShadowAtlasResolution);

        public float depthBias
        {
            get => m_DepthBias;
            set => SetClampedFloat(
                ref m_DepthBias,
                value,
                0.0f,
                MaxShadowDepthBias,
                DefaultShadowDepthBias);
        }

        public float normalBias
        {
            get => m_NormalBias;
            set => SetClampedFloat(
                ref m_NormalBias,
                value,
                0.0f,
                MaxShadowNormalBias,
                DefaultShadowNormalBias);
        }

        public float slopeBias
        {
            get => m_SlopeBias;
            set => SetClampedFloat(
                ref m_SlopeBias,
                value,
                0.0f,
                MaxShadowSlopeBias,
                DefaultShadowSlopeBias);
        }

        public int dirLightPCSSBlockerSampleCount
        {
            get => m_DirLightPCSSBlockerSampleCount;
            set => SetClampedInt(
                ref m_DirLightPCSSBlockerSampleCount,
                value,
                MinPCSSSampleCount,
                MaxPCSSSampleCount,
                DefaultDirLightPCSSBlockerSampleCount);
        }

        public int dirLightPCSSFilterSampleCount
        {
            get => m_DirLightPCSSFilterSampleCount;
            set => SetClampedInt(
                ref m_DirLightPCSSFilterSampleCount,
                value,
                MinPCSSSampleCount,
                MaxPCSSSampleCount,
                DefaultDirLightPCSSFilterSampleCount);
        }

        public float dirLightPCSSMaxPenumbraSize
        {
            get => m_DirLightPCSSMaxPenumbraSize;
            set => SetNonNegativeFloat(
                ref m_DirLightPCSSMaxPenumbraSize,
                value,
                DefaultDirLightPCSSMaxPenumbraSize);
        }

        public float dirLightPCSSMaxSamplingDistance
        {
            get => m_DirLightPCSSMaxSamplingDistance;
            set => SetNonNegativeFloat(
                ref m_DirLightPCSSMaxSamplingDistance,
                value,
                DefaultDirLightPCSSMaxSamplingDistance);
        }

        public float dirLightPCSSMinFilterSizeTexels
        {
            get => m_DirLightPCSSMinFilterSizeTexels;
            set => SetNonNegativeFloat(
                ref m_DirLightPCSSMinFilterSizeTexels,
                value,
                DefaultDirLightPCSSMinFilterSizeTexels);
        }

        public float dirLightPCSSMinFilterMaxAngularDiameter
        {
            get => m_DirLightPCSSMinFilterMaxAngularDiameter;
            set => SetNonNegativeFloat(
                ref m_DirLightPCSSMinFilterMaxAngularDiameter,
                value,
                DefaultDirLightPCSSMinFilterMaxAngularDiameter);
        }

        public float dirLightPCSSBlockerSearchAngularDiameter
        {
            get => m_DirLightPCSSBlockerSearchAngularDiameter;
            set => SetNonNegativeFloat(
                ref m_DirLightPCSSBlockerSearchAngularDiameter,
                value,
                DefaultDirLightPCSSBlockerSearchAngularDiameter);
        }

        public float dirLightPCSSBlockerSamplingClumpExponent
        {
            get => m_DirLightPCSSBlockerSamplingClumpExponent;
            set => SetClampedFloat(
                ref m_DirLightPCSSBlockerSamplingClumpExponent,
                value,
                MinDirLightPCSSBlockerSamplingClumpExponent,
                MaxDirLightPCSSBlockerSamplingClumpExponent,
                DefaultDirLightPCSSBlockerSamplingClumpExponent);
        }

        public bool interactsWithSky
        {
            get => m_InteractsWithSky && light != null && light.type == LightType.Directional;
            set
            {
                if (m_InteractsWithSky == value)
                    return;

                m_InteractsWithSky = value;
                NotifyLightDataChanged();
            }
        }

        public bool enableTimeOfDay
        {
            get => m_EnableTimeOfDay && supportsTimeOfDay;
            set
            {
                if (m_EnableTimeOfDay == value)
                    return;

                m_EnableTimeOfDay = value;
                ApplyTimeOfDayToLight();
                NotifyLightDataChanged();
            }
        }

        public float timeOfDay
        {
            get => m_TimeOfDay;
            set
            {
                var sanitizedValue = SanitizeTimeOfDay(value);
                if (Mathf.Approximately(m_TimeOfDay, sanitizedValue))
                    return;

                m_TimeOfDay = sanitizedValue;
                ApplyTimeOfDayToLight();
                NotifyLightDataChanged();
            }
        }

        public float timeOfDayMaximumLux
        {
            get => m_TimeOfDayMaximumLux;
            set
            {
                var sanitizedValue = SanitizeNonNegativeFloat(value, DefaultTimeOfDayMaximumLux);
                if (Mathf.Approximately(m_TimeOfDayMaximumLux, sanitizedValue))
                    return;

                m_TimeOfDayMaximumLux = sanitizedValue;
                ApplyTimeOfDayToLight();
                NotifyLightDataChanged();
            }
        }

        public float barnDoorAngle
        {
            get => m_BarnDoorAngle;
            set => SetClampedFloat(ref m_BarnDoorAngle, value, 0.0f, 90.0f, DefaultBarnDoorAngle);
        }

        public float barnDoorLength
        {
            get => m_BarnDoorLength;
            set => SetNonNegativeFloat(ref m_BarnDoorLength, value, DefaultBarnDoorLength);
        }

        public bool affectsVolumetric
        {
            get => m_AffectsVolumetric;
            set
            {
                if (m_AffectsVolumetric == value)
                    return;

                m_AffectsVolumetric = value;
                NotifyLightDataChanged();
            }
        }

        public float volumetricDimmer
        {
            get => m_AffectsVolumetric ? m_VolumetricDimmer : 0.0f;
            set => SetClampedFloat(
                ref m_VolumetricDimmer,
                value,
                0.0f,
                MaxVolumetricDimmer,
                DefaultVolumetricDimmer);
        }

        public float volumetricFadeDistance
        {
            get => m_VolumetricFadeDistance;
            set => SetNonNegativeFloat(ref m_VolumetricFadeDistance, value, DefaultVolumetricFadeDistance);
        }

        public float volumetricShadowDimmer
        {
            get => m_AffectsVolumetric ? m_VolumetricShadowDimmer : 0.0f;
            set => SetClampedFloat(
                ref m_VolumetricShadowDimmer,
                value,
                0.0f,
                1.0f,
                DefaultVolumetricShadowDimmer);
        }

        public float angularDiameter
        {
            get => m_AngularDiameter;
            set => SetClampedFloat(ref m_AngularDiameter, value, 0.0f, 90.0f, DefaultCelestialBodyAngularDiameter);
        }

        public bool diameterMultiplierMode
        {
            get => m_DiameterMultiplierMode;
            set
            {
                if (m_DiameterMultiplierMode == value)
                    return;

                m_DiameterMultiplierMode = value;
                NotifyLightDataChanged();
            }
        }

        public float diameterMultiplier
        {
            get => m_DiameterMultiplier;
            set => SetNonNegativeFloat(ref m_DiameterMultiplier, value, 1.0f);
        }

        public float diameterOverride
        {
            get => m_DiameterOverride;
            set => SetNonNegativeFloat(ref m_DiameterOverride, value, DefaultCelestialBodyAngularDiameter);
        }

        public CelestialBodyShadingSource celestialBodyShadingSource
        {
            get => m_CelestialBodyShadingSource;
            set
            {
                if (m_CelestialBodyShadingSource == value)
                    return;

                m_CelestialBodyShadingSource = value;
                NotifyLightDataChanged();
            }
        }

        public Light sunLightOverride
        {
            get => m_SunLightOverride;
            set
            {
                if (m_SunLightOverride == value)
                    return;

                m_SunLightOverride = value;
                NotifyLightDataChanged();
            }
        }

        public Color sunColor
        {
            get => m_SunColor;
            set
            {
                if (m_SunColor == value)
                    return;

                m_SunColor = value;
                NotifyLightDataChanged();
            }
        }

        public float sunIntensity
        {
            get => m_SunIntensity;
            set => SetNonNegativeFloat(ref m_SunIntensity, value, DefaultManualSunIntensity);
        }

        public float moonPhase
        {
            get => m_MoonPhase;
            set => SetClampedFloat(ref m_MoonPhase, value, 0.0f, 1.0f, 0.2f);
        }

        public float moonPhaseRotation
        {
            get => m_MoonPhaseRotation;
            set => SetWrappedAngle(ref m_MoonPhaseRotation, value);
        }

        public float earthshine
        {
            get => m_Earthshine;
            set => SetNonNegativeFloat(ref m_Earthshine, value, 1.0f);
        }

        public float flareSize
        {
            get => m_FlareSize;
            set => SetClampedFloat(ref m_FlareSize, value, 0.0f, 90.0f, 2.0f);
        }

        public Color flareTint
        {
            get => m_FlareTint;
            set
            {
                if (m_FlareTint == value)
                    return;

                m_FlareTint = value;
                NotifyLightDataChanged();
            }
        }

        public float flareFalloff
        {
            get => m_FlareFalloff;
            set => SetNonNegativeFloat(ref m_FlareFalloff, value, 4.0f);
        }

        public float flareMultiplier
        {
            get => m_FlareMultiplier;
            set => SetClampedFloat(ref m_FlareMultiplier, value, 0.0f, 1.0f, 1.0f);
        }

        public Texture surfaceTexture
        {
            get => m_SurfaceTexture;
            set
            {
                if (m_SurfaceTexture == value)
                    return;

                m_SurfaceTexture = value;
                NotifyLightDataChanged();
            }
        }

        public Color surfaceTint
        {
            get => m_SurfaceTint;
            set
            {
                if (m_SurfaceTint == value)
                    return;

                m_SurfaceTint = value;
                NotifyLightDataChanged();
            }
        }

        public float distance
        {
            get => m_Distance;
            set => SetNonNegativeFloat(ref m_Distance, value, DefaultCelestialBodyDistance);
        }

        internal float resolvedAngularDiameter => m_DiameterMultiplierMode
            ? m_DiameterMultiplier * m_AngularDiameter
            : m_DiameterOverride;

        internal bool supportsRayTracedShadow => light != null && light.type == LightType.Directional;

        internal bool isRayTracedShadowActive => isActiveAndEnabled && supportsRayTracedShadow && m_EnableRayTracedShadow;

        internal bool supportsTimeOfDay => light != null && light.type == LightType.Directional;

        internal TimeOfDaySunState currentTimeOfDaySunState => EvaluateTimeOfDaySun(m_TimeOfDay, m_TimeOfDayMaximumLux);

        internal void NotifyLightDataChanged()
        {
            UpdateLightBoundsOverride(light);
            VividLightRenderDatabase.instance.UpdateLightData(light, this);
        }

        internal void ApplyTimeOfDayToLight()
        {
            ApplyTimeOfDayToLight(light);
        }

        private void Start()
        {
            RefreshAnimatedState();
        }

        private void OnEnable()
        {
            m_Light = light;
            RefreshAnimatedState();
            ConstrainTimeOfDaySettings();
            ApplyTimeOfDayToLight(m_Light);
            UpdateLightBoundsOverride(m_Light);
            VividLightRenderDatabase.instance.RegisterLight(this);
        }

        private void LateUpdate()
        {
            if (!isActiveAndEnabled)
                return;

            var currentLight = m_Light != null ? m_Light : light;
            if (currentLight == null)
                return;

            UpdateLightBoundsOverride(currentLight);
            ApplyTimeOfDayToLight(currentLight);

            if (!m_Animated && !m_EnableTimeOfDay)
                return;

            if (VividLightRenderDatabase.instance.TryGetLightData(currentLight, out var trackedLightData)
                && !VividLightRenderDatabase.IsLightDataChanged(currentLight, this, trackedLightData))
            {
                return;
            }

            VividLightRenderDatabase.instance.UpdateLightData(currentLight, this);
        }

        private void OnDisable()
        {
            ClearLightBoundsOverride(m_Light != null ? m_Light : light);
            VividLightRenderDatabase.instance.UnregisterLight(m_Light != null ? m_Light : light);
        }

        private void OnDestroy()
        {
            ClearLightBoundsOverride(m_Light);
            VividLightRenderDatabase.instance.UnregisterLight(m_Light);
        }

        private void OnValidate()
        {
            m_Light = light;
            ConstrainRayTracedShadowSettings();
            ConstrainShadowBiasSettings();
            ConstrainAreaLightSettings();
            ConstrainVolumetricSettings();
            ConstrainTimeOfDaySettings();
            ConstrainCelestialBodySettings();
            RefreshAnimatedState();
            ApplyTimeOfDayToLight(m_Light);
            UpdateLightBoundsOverride(m_Light);
            VividLightRenderDatabase.instance.UpdateLightData(m_Light, this);
        }

        private void RefreshAnimatedState()
        {
            m_Animated = GetComponent<Animator>() != null;
        }

        private static void UpdateLightBoundsOverride(Light targetLight)
        {
            if (targetLight == null)
                return;

            switch (targetLight.type)
            {
                case LightType.Rectangle:
                    UpdateRectangleLightBounds(targetLight);
                    break;
                case LightType.Tube:
                    UpdateTubeLightBounds(targetLight);
                    break;
                case LightType.Disc:
                    UpdateDiscLightBounds(targetLight);
                    break;
                default:
                    ClearLightBoundsOverride(targetLight);
                    break;
            }
        }

        private static void UpdateRectangleLightBounds(Light targetLight)
        {
            targetLight.useBoundingSphereOverride = true;
            targetLight.boundingSphereOverride = new Vector4(
                0.0f,
                0.0f,
                0.0f,
                GetRectangleLightBoundsRadius(targetLight));
        }

        // Matches HDRP: barn door only crops the shaded source footprint and should not shrink
        // the conservative visible-light bounds used to keep rectangle lights in visibleLights.
        private static float GetRectangleLightBoundsRadius(Light targetLight)
        {
            var diagonal = 0.5f * targetLight.areaSize.magnitude;
            return Mathf.Max(targetLight.range, 0.0f) + diagonal;
        }

        private static void UpdateDiscLightBounds(Light targetLight)
        {
            targetLight.useBoundingSphereOverride = true;
            targetLight.boundingSphereOverride = new Vector4(
                0.0f,
                0.0f,
                0.0f,
                Mathf.Max(targetLight.range, 0.0f) + Mathf.Max(targetLight.areaSize.x, 0.0f));
        }

        private static void UpdateTubeLightBounds(Light targetLight)
        {
            targetLight.useBoundingSphereOverride = true;
            targetLight.boundingSphereOverride = new Vector4(
                0.0f,
                0.0f,
                0.0f,
                Mathf.Max(targetLight.range, 0.0f) + Mathf.Max(targetLight.areaSize.x, 0.0f) * 0.5f);
        }

        private static void ClearLightBoundsOverride(Light targetLight)
        {
            if (targetLight == null)
                return;

            targetLight.useBoundingSphereOverride = false;
        }

        private void ApplyTimeOfDayToLight(Light targetLight)
        {
            if (!m_EnableTimeOfDay || targetLight == null || targetLight.type != LightType.Directional)
                return;

            var state = EvaluateTimeOfDaySun(m_TimeOfDay, m_TimeOfDayMaximumLux);
            targetLight.transform.rotation = GetTimeOfDayLightRotation(state.directionToSun);
            targetLight.lightUnit = LightUnit.Lux;
            targetLight.luxAtDistance = 1.0f;
            targetLight.intensity = state.lux;
        }

        private void SetRayTracedShadowFloat(ref float field, float value, float defaultValue)
        {
            var sanitizedValue = SanitizeRayTracedShadowFloat(value, defaultValue);
            if (Mathf.Approximately(field, sanitizedValue))
                return;

            field = sanitizedValue;
            NotifyLightDataChanged();
        }

        private void SetNonNegativeFloat(ref float field, float value, float defaultValue)
        {
            var sanitizedValue = SanitizeNonNegativeFloat(value, defaultValue);
            if (Mathf.Approximately(field, sanitizedValue))
                return;

            field = sanitizedValue;
            NotifyLightDataChanged();
        }

        private void SetClampedFloat(ref float field, float value, float min, float max, float defaultValue)
        {
            var sanitizedValue = SanitizeClampedFloat(value, min, max, defaultValue);
            if (Mathf.Approximately(field, sanitizedValue))
                return;

            field = sanitizedValue;
            NotifyLightDataChanged();
        }

        private void SetClampedInt(ref int field, int value, int min, int max, int defaultValue)
        {
            var sanitizedValue = SanitizeClampedInt(value, min, max, defaultValue);
            if (field == sanitizedValue)
                return;

            field = sanitizedValue;
            NotifyLightDataChanged();
        }

        private void SetWrappedAngle(ref float field, float value)
        {
            var sanitizedValue = SanitizeWrappedAngle(value);
            if (Mathf.Approximately(field, sanitizedValue))
                return;

            field = sanitizedValue;
            NotifyLightDataChanged();
        }

        private void ConstrainRayTracedShadowSettings()
        {
            m_RayTracedShadowRayLength = SanitizeRayTracedShadowFloat(
                m_RayTracedShadowRayLength,
                DefaultRayTracedShadowRayLength);
            m_RayTracedShadowRayBias = SanitizeRayTracedShadowFloat(
                m_RayTracedShadowRayBias,
                DefaultRayTracedShadowRayBias);
            m_RayTracedShadowDistantRayBias = SanitizeRayTracedShadowFloat(
                m_RayTracedShadowDistantRayBias,
                DefaultRayTracedShadowDistantRayBias);
            m_RayTracedShadowSunAngularDiameter = SanitizeRayTracedShadowFloat(
                m_RayTracedShadowSunAngularDiameter,
                DefaultRayTracedShadowSunAngularDiameter);
        }

        private void ConstrainShadowBiasSettings()
        {
            m_ScreenSpaceShadowQuality = SanitizeScreenSpaceShadowQuality(m_ScreenSpaceShadowQuality);
            m_ShadowAtlasResolution = SanitizeShadowAtlasResolution(m_ShadowAtlasResolution);
            m_DepthBias = SanitizeClampedFloat(
                m_DepthBias,
                0.0f,
                MaxShadowDepthBias,
                DefaultShadowDepthBias);
            m_NormalBias = SanitizeClampedFloat(
                m_NormalBias,
                0.0f,
                MaxShadowNormalBias,
                DefaultShadowNormalBias);
            m_SlopeBias = SanitizeClampedFloat(
                m_SlopeBias,
                0.0f,
                MaxShadowSlopeBias,
                DefaultShadowSlopeBias);
            m_DirLightPCSSBlockerSampleCount = SanitizeClampedInt(
                m_DirLightPCSSBlockerSampleCount,
                MinPCSSSampleCount,
                MaxPCSSSampleCount,
                DefaultDirLightPCSSBlockerSampleCount);
            m_DirLightPCSSFilterSampleCount = SanitizeClampedInt(
                m_DirLightPCSSFilterSampleCount,
                MinPCSSSampleCount,
                MaxPCSSSampleCount,
                DefaultDirLightPCSSFilterSampleCount);
            m_DirLightPCSSMaxPenumbraSize = SanitizeNonNegativeFloat(
                m_DirLightPCSSMaxPenumbraSize,
                DefaultDirLightPCSSMaxPenumbraSize);
            m_DirLightPCSSMaxSamplingDistance = SanitizeNonNegativeFloat(
                m_DirLightPCSSMaxSamplingDistance,
                DefaultDirLightPCSSMaxSamplingDistance);
            m_DirLightPCSSMinFilterSizeTexels = SanitizeNonNegativeFloat(
                m_DirLightPCSSMinFilterSizeTexels,
                DefaultDirLightPCSSMinFilterSizeTexels);
            m_DirLightPCSSMinFilterMaxAngularDiameter = SanitizeNonNegativeFloat(
                m_DirLightPCSSMinFilterMaxAngularDiameter,
                DefaultDirLightPCSSMinFilterMaxAngularDiameter);
            m_DirLightPCSSBlockerSearchAngularDiameter = SanitizeNonNegativeFloat(
                m_DirLightPCSSBlockerSearchAngularDiameter,
                DefaultDirLightPCSSBlockerSearchAngularDiameter);
            m_DirLightPCSSBlockerSamplingClumpExponent = SanitizeClampedFloat(
                m_DirLightPCSSBlockerSamplingClumpExponent,
                MinDirLightPCSSBlockerSamplingClumpExponent,
                MaxDirLightPCSSBlockerSamplingClumpExponent,
                DefaultDirLightPCSSBlockerSamplingClumpExponent);
        }

        private void ConstrainAreaLightSettings()
        {
            m_BarnDoorAngle = SanitizeClampedFloat(m_BarnDoorAngle, 0.0f, 90.0f, DefaultBarnDoorAngle);
            m_BarnDoorLength = SanitizeNonNegativeFloat(m_BarnDoorLength, DefaultBarnDoorLength);
        }

        private void ConstrainVolumetricSettings()
        {
            m_VolumetricDimmer = SanitizeClampedFloat(
                m_VolumetricDimmer,
                0.0f,
                MaxVolumetricDimmer,
                DefaultVolumetricDimmer);
            m_VolumetricFadeDistance = SanitizeNonNegativeFloat(
                m_VolumetricFadeDistance,
                DefaultVolumetricFadeDistance);
            m_VolumetricShadowDimmer = SanitizeClampedFloat(
                m_VolumetricShadowDimmer,
                0.0f,
                1.0f,
                DefaultVolumetricShadowDimmer);
        }

        private void ConstrainTimeOfDaySettings()
        {
            m_TimeOfDay = SanitizeTimeOfDay(m_TimeOfDay);
            m_TimeOfDayMaximumLux = SanitizeNonNegativeFloat(m_TimeOfDayMaximumLux, DefaultTimeOfDayMaximumLux);
        }

        private static CSMScreenSpaceShadowQuality SanitizeScreenSpaceShadowQuality(CSMScreenSpaceShadowQuality value)
        {
            return value switch
            {
                CSMScreenSpaceShadowQuality.Low => CSMScreenSpaceShadowQuality.Low,
                CSMScreenSpaceShadowQuality.Medium => CSMScreenSpaceShadowQuality.Medium,
                CSMScreenSpaceShadowQuality.High => CSMScreenSpaceShadowQuality.High,
                CSMScreenSpaceShadowQuality.VeryHigh => CSMScreenSpaceShadowQuality.VeryHigh,
                _ => DefaultScreenSpaceShadowQuality
            };
        }

        private static CSMShadowAtlasResolution SanitizeShadowAtlasResolution(CSMShadowAtlasResolution value)
        {
            return value switch
            {
                CSMShadowAtlasResolution.Resolution1024 => CSMShadowAtlasResolution.Resolution1024,
                CSMShadowAtlasResolution.Resolution2048 => CSMShadowAtlasResolution.Resolution2048,
                CSMShadowAtlasResolution.Resolution4096 => CSMShadowAtlasResolution.Resolution4096,
                CSMShadowAtlasResolution.Resolution8192 => CSMShadowAtlasResolution.Resolution8192,
                _ => (CSMShadowAtlasResolution)DefaultShadowAtlasResolution
            };
        }

        private void ConstrainCelestialBodySettings()
        {
            m_AngularDiameter = SanitizeClampedFloat(m_AngularDiameter, 0.0f, 90.0f, DefaultCelestialBodyAngularDiameter);
            m_DiameterMultiplier = SanitizeNonNegativeFloat(m_DiameterMultiplier, 1.0f);
            m_DiameterOverride = SanitizeNonNegativeFloat(m_DiameterOverride, DefaultCelestialBodyAngularDiameter);
            m_SunIntensity = SanitizeNonNegativeFloat(m_SunIntensity, DefaultManualSunIntensity);
            m_MoonPhase = SanitizeClampedFloat(m_MoonPhase, 0.0f, 1.0f, 0.2f);
            m_MoonPhaseRotation = SanitizeWrappedAngle(m_MoonPhaseRotation);
            m_Earthshine = SanitizeNonNegativeFloat(m_Earthshine, 1.0f);
            m_FlareSize = SanitizeClampedFloat(m_FlareSize, 0.0f, 90.0f, 2.0f);
            m_FlareFalloff = SanitizeNonNegativeFloat(m_FlareFalloff, 4.0f);
            m_FlareMultiplier = SanitizeClampedFloat(m_FlareMultiplier, 0.0f, 1.0f, 1.0f);
            m_Distance = SanitizeNonNegativeFloat(m_Distance, DefaultCelestialBodyDistance);
        }

        internal static TimeOfDaySunState EvaluateTimeOfDaySun(float timeOfDay, float maximumLux)
        {
            var sanitizedTime = SanitizeTimeOfDay(timeOfDay);
            var elevationDegrees = Mathf.Sin((sanitizedTime - TimeOfDaySunrise) / TimeOfDayDaylightDuration * Mathf.PI) * 90.0f;
            var azimuthDegrees = Mathf.Repeat(sanitizedTime / TimeOfDayHours * 360.0f, 360.0f);
            var directionToSun = CalculateTimeOfDaySunDirection(elevationDegrees, azimuthDegrees);
            var lux = EvaluateTimeOfDayLux(elevationDegrees, maximumLux);
            return new TimeOfDaySunState(directionToSun, elevationDegrees, azimuthDegrees, lux);
        }

        internal static float EvaluateTimeOfDayLux(float elevationDegrees, float maximumLux)
        {
            var maxLux = Mathf.Max(SanitizeNonNegativeFloat(maximumLux, DefaultTimeOfDayMaximumLux), 0.0f);
            if (maxLux <= 0.0f || elevationDegrees <= 0.0f)
                return 0.0f;

            var horizonFade = Mathf.SmoothStep(
                0.0f,
                1.0f,
                Mathf.InverseLerp(0.0f, TimeOfDayHorizonFadeDegrees, elevationDegrees));
            var airMass = CalculateRelativeAirMass(elevationDegrees);
            var zenithAirMass = CalculateRelativeAirMass(90.0f);
            var atmosphericScale = Mathf.Exp(-TimeOfDayAtmosphereExtinction * Mathf.Max(airMass - zenithAirMass, 0.0f));
            return maxLux * horizonFade * atmosphericScale;
        }

        private static Vector3 CalculateTimeOfDaySunDirection(float elevationDegrees, float azimuthDegrees)
        {
            var elevationRadians = elevationDegrees * Mathf.Deg2Rad;
            var azimuthRadians = azimuthDegrees * Mathf.Deg2Rad;
            var cosElevation = Mathf.Cos(elevationRadians);
            var direction = new Vector3(
                Mathf.Sin(azimuthRadians) * cosElevation,
                Mathf.Sin(elevationRadians),
                Mathf.Cos(azimuthRadians) * cosElevation);

            return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.up;
        }

        private static Quaternion GetTimeOfDayLightRotation(Vector3 directionToSun)
        {
            var forward = directionToSun.sqrMagnitude > 1e-6f ? -directionToSun.normalized : Vector3.down;
            var up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(forward, up);
        }

        private static float CalculateRelativeAirMass(float elevationDegrees)
        {
            var elevation = Mathf.Clamp(elevationDegrees, 0.001f, 90.0f);
            var sinElevation = Mathf.Sin(elevation * Mathf.Deg2Rad);
            return 1.0f / (sinElevation + 0.50572f * Mathf.Pow(elevation + 6.07995f, -1.6364f));
        }

        private static float SanitizeRayTracedShadowFloat(float value, float defaultValue)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return defaultValue;

            return Mathf.Max(0f, value);
        }

        private static float SanitizeNonNegativeFloat(float value, float defaultValue)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return defaultValue;

            return Mathf.Max(0.0f, value);
        }

        private static float SanitizeClampedFloat(float value, float min, float max, float defaultValue)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return defaultValue;

            return Mathf.Clamp(value, min, max);
        }

        private static float SanitizeTimeOfDay(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return DefaultTimeOfDay;

            return Mathf.Clamp(value, 0.0f, TimeOfDayHours);
        }

        private static int SanitizeClampedInt(int value, int min, int max, int defaultValue)
        {
            if (value == int.MinValue || value == int.MaxValue)
                return defaultValue;

            return Mathf.Clamp(value, min, max);
        }

        private static float SanitizeWrappedAngle(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0.0f;

            return Mathf.Repeat(value, 360.0f);
        }
    }
}
