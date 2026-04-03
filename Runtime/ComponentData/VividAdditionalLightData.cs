using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VividLightRenderData
    {
        public EntityId lightEntityId;
        public LightType lightType;
        public Vector3 positionWS;
        public float range;
        public Vector3 forwardWS;
        public float intensity;
        public Vector3 color;
        public float shadowStrength;
        public float spotAngle;
        public float innerSpotAngle;
        public float inverseRangeSquared;
        public uint renderingLayerMask;
        public uint shadowRenderingLayerMask;
        public VividLightRenderDataFlags flags;
    }

    public sealed class VividLightRenderDatabase
    {
        private readonly List<VividLightRenderData> m_LightData = new();
        private readonly Dictionary<EntityId, int> m_EntityIdToDataIndex = new();

        public static VividLightRenderDatabase instance => Singleton<VividLightRenderDatabase>.instance;

        public int lightCount => m_LightData.Count;

        public IReadOnlyList<VividLightRenderData> lightData => m_LightData;

        internal VividLightRenderData RegisterLight(VividAdditionalLightData additionalLightData)
        {
            return UpdateLightData(additionalLightData);
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
            if (light == null)
                return default;

            if (additionalLightData == null && !light.TryGetComponent(out additionalLightData))
                additionalLightData = null;

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

            UnregisterLight(additionalLightData.light);
        }

        internal void UnregisterLight(Light light)
        {
            if (light == null)
                return;

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
            m_LightData.Clear();
            m_EntityIdToDataIndex.Clear();
        }

        private bool TryGetLightData(int dataIndex, out VividLightRenderData trackedLightData)
        {
            trackedLightData = default;

            if (dataIndex < 0 || dataIndex >= m_LightData.Count)
                return false;

            trackedLightData = m_LightData[dataIndex];
            return true;
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

        private static VividLightRenderData CreateLightRenderData(Light light, VividAdditionalLightData additionalLightData)
        {
            var nativeIntensity = ResolveNativeLightIntensity(light);
            var finalColor = light.color.linear * nativeIntensity;
            var range = Mathf.Max(light.range, 0.0f);
            var inverseRangeSquared = range > 0.0f ? 1.0f / Mathf.Max(range * range, 1e-6f) : 0.0f;
            var shadowRenderingLayerMask = additionalLightData != null
                ? additionalLightData.effectiveShadowRenderingLayers
                : (RenderingLayerMask)light.renderingLayerMask;

            return new VividLightRenderData
            {
                lightEntityId = light.GetEntityId(),
                lightType = light.type,
                positionWS = light.transform.position,
                range = range,
                forwardWS = light.transform.forward,
                intensity = Mathf.Max(light.intensity, 0.0f),
                color = new Vector3(finalColor.r, finalColor.g, finalColor.b),
                shadowStrength = light.shadows != LightShadows.None ? light.shadowStrength : 0.0f,
                spotAngle = light.spotAngle,
                innerSpotAngle = light.innerSpotAngle,
                inverseRangeSquared = inverseRangeSquared,
                renderingLayerMask = (uint)light.renderingLayerMask,
                shadowRenderingLayerMask = (uint)shadowRenderingLayerMask,
                flags = BuildFlags(light, additionalLightData),
            };
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

            return flags;
        }

        private static float ResolveNativeLightIntensity(Light light)
        {
            if (light == null)
                return 0.0f;

            var intensity = Mathf.Max(light.intensity, 0.0f);
            if (!LightUnitUtils.IsLightUnitSupported(light.type, light.lightUnit))
                return intensity;

            return Mathf.Max(
                LightUnitUtils.ConvertIntensity(
                    light,
                    intensity,
                    light.lightUnit,
                    LightUnitUtils.GetNativeLightUnit(light.type)),
                0.0f);
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
                   && Mathf.Approximately(lhs.intensity, rhs.intensity)
                   && Approximately(lhs.color, rhs.color)
                   && Mathf.Approximately(lhs.shadowStrength, rhs.shadowStrength)
                   && Mathf.Approximately(lhs.spotAngle, rhs.spotAngle)
                   && Mathf.Approximately(lhs.innerSpotAngle, rhs.innerSpotAngle)
                   && Mathf.Approximately(lhs.inverseRangeSquared, rhs.inverseRangeSquared)
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
        internal const float DefaultRayTracedShadowRayLength = 1000f;
        internal const float DefaultRayTracedShadowRayBias = 0.001f;
        internal const float DefaultRayTracedShadowDistantRayBias = 0.001f;
        internal const float DefaultRayTracedShadowSunAngularDiameter = 0.533f;

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

        internal bool supportsRayTracedShadow => light != null && light.type == LightType.Directional;

        internal bool isRayTracedShadowActive => isActiveAndEnabled && supportsRayTracedShadow && m_EnableRayTracedShadow;

        internal void NotifyLightDataChanged()
        {
            VividLightRenderDatabase.instance.UpdateLightData(light, this);
        }

        private void Start()
        {
            RefreshAnimatedState();
        }

        private void OnEnable()
        {
            m_Light = light;
            RefreshAnimatedState();
            VividLightRenderDatabase.instance.RegisterLight(this);
        }

        private void LateUpdate()
        {
            if (!isActiveAndEnabled || !m_Animated)
                return;

            var currentLight = m_Light != null ? m_Light : light;
            if (currentLight == null)
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
            VividLightRenderDatabase.instance.UnregisterLight(m_Light != null ? m_Light : light);
        }

        private void OnDestroy()
        {
            VividLightRenderDatabase.instance.UnregisterLight(m_Light);
        }

        private void OnValidate()
        {
            m_Light = light;
            ConstrainRayTracedShadowSettings();
            RefreshAnimatedState();
            VividLightRenderDatabase.instance.UpdateLightData(m_Light, this);
        }

        private void RefreshAnimatedState()
        {
            m_Animated = GetComponent<Animator>() != null;
        }

        private void SetRayTracedShadowFloat(ref float field, float value, float defaultValue)
        {
            var sanitizedValue = SanitizeRayTracedShadowFloat(value, defaultValue);
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

        private static float SanitizeRayTracedShadowFloat(float value, float defaultValue)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return defaultValue;

            return Mathf.Max(0f, value);
        }
    }
}
