using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum VividReflectionProbeProxyVolumeMode
    {
        Infinite = 0,
        InfluenceVolume = 1,
        Box = 2,
    }

    public static class VividReflectionProbeExtensions
    {
        public static VividAdditionalReflectionData GetVividAdditionalReflectionData(this ReflectionProbe reflectionProbe)
        {
            if (reflectionProbe == null)
                throw new ArgumentNullException(nameof(reflectionProbe));

            var gameObject = reflectionProbe.gameObject;
            if (!gameObject.TryGetComponent<VividAdditionalReflectionData>(out var reflectionData))
                reflectionData = gameObject.AddComponent<VividAdditionalReflectionData>();

            return reflectionData;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(ReflectionProbe))]
    [ExecuteAlways]
    public sealed class VividAdditionalReflectionData : MonoBehaviour, IAdditionalData
    {
        internal const float MinBoxSize = 0.0001f;
        internal const int MaxImportance = 32767;

        private static readonly HashSet<VividAdditionalReflectionData> s_AllInstances = new();
        private static readonly Dictionary<EntityId, VividAdditionalReflectionData> s_DataByReflectionProbeId = new(new EntityIdComparer());

        [SerializeField, Min(0.0f)]
        private float m_Multiplier = 1.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_Weight = 1.0f;

        [SerializeField, Min(0)]
        private int m_Importance;

        [SerializeField, Min(0.0f)]
        private float m_FadeDistance = 10000.0f;

        [SerializeField, Min(0.0f)]
        private float m_RangeCompressionFactor = 1.0f;

        [SerializeField]
        private Vector3 m_CapturePositionOffset = Vector3.zero;

        [SerializeField]
        private Vector3 m_InfluenceBoxSize = Vector3.one * 10.0f;

        [SerializeField]
        private Vector3 m_InfluenceBoxOffset = Vector3.zero;

        [SerializeField]
        private Vector3 m_BoxBlendDistancePositive = Vector3.zero;

        [SerializeField]
        private Vector3 m_BoxBlendDistanceNegative = Vector3.zero;

        [SerializeField]
        private Vector3 m_BoxBlendNormalDistancePositive = Vector3.zero;

        [SerializeField]
        private Vector3 m_BoxBlendNormalDistanceNegative = Vector3.zero;

        [SerializeField]
        private bool m_BoxPerAxisControl;

        [SerializeField]
        private Vector3 m_BoxSideFadePositive = Vector3.one;

        [SerializeField]
        private Vector3 m_BoxSideFadeNegative = Vector3.one;

        [SerializeField]
        private VividReflectionProbeProxyVolumeMode m_ProxyVolumeMode = VividReflectionProbeProxyVolumeMode.InfluenceVolume;

        [SerializeField]
        private Vector3 m_ProxyBoxSize = Vector3.one * 10.0f;

        [SerializeField]
        private Vector3 m_ProxyBoxOffset = Vector3.zero;

        private ReflectionProbe m_ReflectionProbe;
        private EntityId m_ReflectionProbeEntityId = EntityId.None;
        private bool m_ReflectionProbeSyncDirty = true;

        public ReflectionProbe reflectionProbe
        {
            get
            {
                if (m_ReflectionProbe == null)
                    m_ReflectionProbe = GetComponent<ReflectionProbe>();
                return m_ReflectionProbe;
            }
        }

        public float multiplier
        {
            get => m_Multiplier;
            set => m_Multiplier = Mathf.Max(0.0f, value);
        }

        public float weight
        {
            get => m_Weight;
            set => m_Weight = Mathf.Clamp01(value);
        }

        public int importance
        {
            get => m_Importance;
            set
            {
                var clampedValue = Mathf.Clamp(value, 0, MaxImportance);
                if (m_Importance == clampedValue)
                    return;

                m_Importance = clampedValue;
                MarkReflectionProbeSyncDirty();
            }
        }

        public float fadeDistance
        {
            get => m_FadeDistance;
            set => m_FadeDistance = Mathf.Max(0.0f, value);
        }

        public float rangeCompressionFactor
        {
            get => m_RangeCompressionFactor;
            set => m_RangeCompressionFactor = Mathf.Max(0.0001f, value);
        }

        public Vector3 capturePositionOffset
        {
            get => m_CapturePositionOffset;
            set => m_CapturePositionOffset = value;
        }

        public Vector3 influenceBoxSize
        {
            get => m_InfluenceBoxSize;
            set
            {
                var sanitizedValue = SanitizeSize(value);
                if (m_InfluenceBoxSize == sanitizedValue)
                    return;

                m_InfluenceBoxSize = sanitizedValue;
                MarkReflectionProbeSyncDirty();
            }
        }

        public Vector3 influenceBoxOffset
        {
            get => m_InfluenceBoxOffset;
            set
            {
                if (m_InfluenceBoxOffset == value)
                    return;

                m_InfluenceBoxOffset = value;
                MarkReflectionProbeSyncDirty();
            }
        }

        public Vector3 boxBlendDistancePositive
        {
            get => m_BoxBlendDistancePositive;
            set
            {
                var clampedValue = ClampBlendDistance(value, influenceBoxSize);
                if (m_BoxBlendDistancePositive == clampedValue)
                    return;

                m_BoxBlendDistancePositive = clampedValue;
                MarkReflectionProbeSyncDirty();
            }
        }

        public Vector3 boxBlendDistanceNegative
        {
            get => m_BoxBlendDistanceNegative;
            set
            {
                var clampedValue = ClampBlendDistance(value, influenceBoxSize);
                if (m_BoxBlendDistanceNegative == clampedValue)
                    return;

                m_BoxBlendDistanceNegative = clampedValue;
                MarkReflectionProbeSyncDirty();
            }
        }

        public Vector3 boxBlendNormalDistancePositive
        {
            get => m_BoxBlendNormalDistancePositive;
            set => m_BoxBlendNormalDistancePositive = ClampBlendDistance(value, influenceBoxSize);
        }

        public Vector3 boxBlendNormalDistanceNegative
        {
            get => m_BoxBlendNormalDistanceNegative;
            set => m_BoxBlendNormalDistanceNegative = ClampBlendDistance(value, influenceBoxSize);
        }

        public bool boxPerAxisControl
        {
            get => m_BoxPerAxisControl;
            set => m_BoxPerAxisControl = value;
        }

        public Vector3 boxSideFadePositive
        {
            get => m_BoxSideFadePositive;
            set => m_BoxSideFadePositive = Clamp01(value);
        }

        public Vector3 boxSideFadeNegative
        {
            get => m_BoxSideFadeNegative;
            set => m_BoxSideFadeNegative = Clamp01(value);
        }

        public VividReflectionProbeProxyVolumeMode proxyVolumeMode
        {
            get => m_ProxyVolumeMode;
            set
            {
                if (m_ProxyVolumeMode == value)
                    return;

                m_ProxyVolumeMode = value;
                MarkReflectionProbeSyncDirty();
            }
        }

        public Vector3 proxyBoxSize
        {
            get => m_ProxyBoxSize;
            set => m_ProxyBoxSize = SanitizeSize(value);
        }

        public Vector3 proxyBoxOffset
        {
            get => m_ProxyBoxOffset;
            set => m_ProxyBoxOffset = value;
        }

        public bool isProjectionInfinite => m_ProxyVolumeMode == VividReflectionProbeProxyVolumeMode.Infinite;

        public static VividAdditionalReflectionData[] GetAllInstances()
        {
            var reflectionDatas = new VividAdditionalReflectionData[s_AllInstances.Count];
            s_AllInstances.CopyTo(reflectionDatas);
            return reflectionDatas;
        }

        internal static bool hasRegisteredData => s_DataByReflectionProbeId.Count > 0;

        internal static bool TryGetAdditionalData(
            ReflectionProbe reflectionProbe,
            out VividAdditionalReflectionData reflectionData)
        {
            reflectionData = null;

            if (reflectionProbe == null)
                return false;

            return TryGetAdditionalData(reflectionProbe.GetEntityId(), out reflectionData);
        }

        internal static bool TryGetAdditionalData(
            EntityId reflectionProbeEntityId,
            out VividAdditionalReflectionData reflectionData)
        {
            reflectionData = null;

            if (IsEntityIdNone(reflectionProbeEntityId))
                return false;

            return s_DataByReflectionProbeId.TryGetValue(reflectionProbeEntityId, out reflectionData)
                   && reflectionData != null
                   && reflectionData.isActiveAndEnabled;
        }

        public Bounds GetInfluenceBounds()
        {
            return new Bounds(transform.TransformPoint(m_InfluenceBoxOffset), ScaleSize(m_InfluenceBoxSize, transform.lossyScale));
        }

        public Vector3 GetProxyBoxSize()
        {
            return m_ProxyVolumeMode == VividReflectionProbeProxyVolumeMode.Box
                ? m_ProxyBoxSize
                : m_InfluenceBoxSize;
        }

        public Vector3 GetProxyBoxOffset()
        {
            return m_ProxyVolumeMode == VividReflectionProbeProxyVolumeMode.Box
                ? m_ProxyBoxOffset
                : m_InfluenceBoxOffset;
        }

        public void SyncReflectionProbe()
        {
            Sanitize();

            var probe = reflectionProbe;
            if (probe == null)
                return;

            probe.size = m_InfluenceBoxSize;
            probe.center = m_InfluenceBoxOffset;
            probe.blendDistance = GetMaxBlendDistance();
            probe.importance = m_Importance;
            probe.boxProjection = !isProjectionInfinite;
            m_ReflectionProbeSyncDirty = false;
        }

        internal void SyncReflectionProbeIfDirty()
        {
            if (!m_ReflectionProbeSyncDirty)
                return;

            SyncReflectionProbe();
        }

        private void Reset()
        {
            PullFromReflectionProbe();
            SyncReflectionProbe();
        }

        private void OnEnable()
        {
            s_AllInstances.Add(this);
            RegisterReflectionProbe();
            PullFromReflectionProbeIfDefault();
            SyncReflectionProbe();
        }

        private void OnDisable()
        {
            s_AllInstances.Remove(this);
            UnregisterReflectionProbe();
        }

        private void OnDestroy()
        {
            s_AllInstances.Remove(this);
            UnregisterReflectionProbe();
        }

        private void OnValidate()
        {
            MarkReflectionProbeSyncDirty();
            SyncReflectionProbe();
        }

        private void RegisterReflectionProbe()
        {
            var probe = reflectionProbe;
            if (probe == null)
                return;

            m_ReflectionProbeEntityId = probe.GetEntityId();
            if (IsEntityIdNone(m_ReflectionProbeEntityId))
                return;

            s_DataByReflectionProbeId[m_ReflectionProbeEntityId] = this;
        }

        private void UnregisterReflectionProbe()
        {
            if (IsEntityIdNone(m_ReflectionProbeEntityId))
                return;

            if (s_DataByReflectionProbeId.TryGetValue(m_ReflectionProbeEntityId, out var reflectionData)
                && ReferenceEquals(reflectionData, this))
            {
                s_DataByReflectionProbeId.Remove(m_ReflectionProbeEntityId);
            }

            m_ReflectionProbeEntityId = EntityId.None;
        }

        private static bool IsEntityIdNone(EntityId entityId)
        {
            return EntityId.ToULong(entityId) == EntityId.ToULong(EntityId.None);
        }

        private void MarkReflectionProbeSyncDirty()
        {
            m_ReflectionProbeSyncDirty = true;
        }

        private sealed class EntityIdComparer : IEqualityComparer<EntityId>
        {
            public bool Equals(EntityId x, EntityId y)
            {
                return EntityId.ToULong(x) == EntityId.ToULong(y);
            }

            public int GetHashCode(EntityId obj)
            {
                return EntityId.ToULong(obj).GetHashCode();
            }
        }

        private void PullFromReflectionProbeIfDefault()
        {
            if (m_ReflectionProbe == null)
                m_ReflectionProbe = GetComponent<ReflectionProbe>();

            if (m_ReflectionProbe == null)
                return;

            if (m_InfluenceBoxSize == Vector3.one * 10.0f
                && m_InfluenceBoxOffset == Vector3.zero
                && m_Importance == 0
                && Mathf.Approximately(GetMaxBlendDistance(), 0.0f))
            {
                PullFromReflectionProbe();
            }
        }

        private void PullFromReflectionProbe()
        {
            var probe = reflectionProbe;
            if (probe == null)
                return;

            m_InfluenceBoxSize = SanitizeSize(probe.size);
            m_InfluenceBoxOffset = probe.center;
            m_CapturePositionOffset = Vector3.zero;
            m_ProxyBoxSize = m_InfluenceBoxSize;
            m_ProxyBoxOffset = m_InfluenceBoxOffset;
            m_BoxBlendDistancePositive = Vector3.one * Mathf.Max(0.0f, probe.blendDistance);
            m_BoxBlendDistanceNegative = m_BoxBlendDistancePositive;
            m_Importance = Mathf.Clamp(probe.importance, 0, MaxImportance);
            m_ProxyVolumeMode = probe.boxProjection
                ? VividReflectionProbeProxyVolumeMode.InfluenceVolume
                : VividReflectionProbeProxyVolumeMode.Infinite;
        }

        private void Sanitize()
        {
            m_Multiplier = Mathf.Max(0.0f, m_Multiplier);
            m_Weight = Mathf.Clamp01(m_Weight);
            m_Importance = Mathf.Clamp(m_Importance, 0, MaxImportance);
            m_FadeDistance = Mathf.Max(0.0f, m_FadeDistance);
            m_RangeCompressionFactor = Mathf.Max(0.0001f, m_RangeCompressionFactor);
            m_InfluenceBoxSize = SanitizeSize(m_InfluenceBoxSize);
            m_ProxyBoxSize = SanitizeSize(m_ProxyBoxSize);
            m_BoxBlendDistancePositive = ClampBlendDistance(m_BoxBlendDistancePositive, m_InfluenceBoxSize);
            m_BoxBlendDistanceNegative = ClampBlendDistance(m_BoxBlendDistanceNegative, m_InfluenceBoxSize);
            m_BoxBlendNormalDistancePositive = ClampBlendDistance(m_BoxBlendNormalDistancePositive, m_InfluenceBoxSize);
            m_BoxBlendNormalDistanceNegative = ClampBlendDistance(m_BoxBlendNormalDistanceNegative, m_InfluenceBoxSize);
            m_BoxSideFadePositive = Clamp01(m_BoxSideFadePositive);
            m_BoxSideFadeNegative = Clamp01(m_BoxSideFadeNegative);
        }

        private float GetMaxBlendDistance()
        {
            return Mathf.Max(
                m_BoxBlendDistancePositive.x,
                m_BoxBlendDistancePositive.y,
                m_BoxBlendDistancePositive.z,
                m_BoxBlendDistanceNegative.x,
                m_BoxBlendDistanceNegative.y,
                m_BoxBlendDistanceNegative.z);
        }

        internal static Vector3 SanitizeSize(Vector3 value)
        {
            return new Vector3(
                Mathf.Max(Mathf.Abs(value.x), MinBoxSize),
                Mathf.Max(Mathf.Abs(value.y), MinBoxSize),
                Mathf.Max(Mathf.Abs(value.z), MinBoxSize));
        }

        internal static Vector3 ClampBlendDistance(Vector3 value, Vector3 boxSize)
        {
            var halfSize = SanitizeSize(boxSize) * 0.5f;
            return new Vector3(
                Mathf.Clamp(value.x, 0.0f, halfSize.x),
                Mathf.Clamp(value.y, 0.0f, halfSize.y),
                Mathf.Clamp(value.z, 0.0f, halfSize.z));
        }

        internal static Vector3 Clamp01(Vector3 value)
        {
            return new Vector3(
                Mathf.Clamp01(value.x),
                Mathf.Clamp01(value.y),
                Mathf.Clamp01(value.z));
        }

        internal static Vector3 ScaleSize(Vector3 size, Vector3 scale)
        {
            return new Vector3(
                Mathf.Abs(size.x * scale.x),
                Mathf.Abs(size.y * scale.y),
                Mathf.Abs(size.z * scale.z));
        }
    }
}
