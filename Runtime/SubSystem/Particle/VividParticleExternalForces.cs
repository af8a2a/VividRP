using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime.Particle
{
    public enum VividParticleForceFieldShape
    {
        Sphere,
        Hemisphere,
        Cylinder,
        Box,
    }

    public enum VividParticleGameObjectFilter
    {
        LayerMask,
        List,
        LayerMaskAndList,
    }

    [Serializable]
    public sealed class VividParticleExternalForcesModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private AnimationCurve m_Multiplier = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);

        [SerializeField]
        private VividParticleGameObjectFilter m_InfluenceFilter = VividParticleGameObjectFilter.LayerMask;

        [SerializeField]
        private LayerMask m_InfluenceMask = ~0;

        [SerializeField]
        private VividParticleForceField[] m_Influences = Array.Empty<VividParticleForceField>();

        public bool enabled
        {
            get => m_Enabled;
            set { if (m_Enabled != value) { m_Enabled = value; NotifyChanged(); } }
        }

        public AnimationCurve multiplier
        {
            get => m_Multiplier ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            set { m_Multiplier = CloneCurve(value, 1.0f); NotifyChanged(); }
        }

        public VividParticleGameObjectFilter influenceFilter
        {
            get => m_InfluenceFilter;
            set
            {
                VividParticleGameObjectFilter validated = Enum.IsDefined(
                    typeof(VividParticleGameObjectFilter), value)
                        ? value
                        : VividParticleGameObjectFilter.LayerMask;
                if (m_InfluenceFilter == validated)
                    return;
                m_InfluenceFilter = validated;
                NotifyChanged();
            }
        }

        public LayerMask influenceMask
        {
            get => m_InfluenceMask;
            set { if (m_InfluenceMask != value) { m_InfluenceMask = value; NotifyChanged(); } }
        }

        public int influenceCount => m_Influences?.Length ?? 0;

        public VividParticleForceField GetInfluence(int index)
        {
            return (uint)index < (uint)influenceCount ? m_Influences[index] : null;
        }

        public bool IsAffectedBy(VividParticleForceField forceField)
        {
            if (forceField == null || m_Influences == null)
                return false;
            for (int index = 0; index < m_Influences.Length; index++)
            {
                if (m_Influences[index] == forceField)
                    return true;
            }
            return false;
        }

        public void AddInfluence(VividParticleForceField forceField)
        {
            if (forceField == null || IsAffectedBy(forceField))
                return;
            int count = influenceCount;
            Array.Resize(ref m_Influences, count + 1);
            m_Influences[count] = forceField;
            NotifyChanged();
        }

        public void RemoveInfluence(int index)
        {
            int count = influenceCount;
            if ((uint)index >= (uint)count)
                return;
            for (int source = index + 1; source < count; source++)
                m_Influences[source - 1] = m_Influences[source];
            Array.Resize(ref m_Influences, count - 1);
            NotifyChanged();
        }

        public void RemoveAllInfluences()
        {
            if (influenceCount == 0)
                return;
            m_Influences = Array.Empty<VividParticleForceField>();
            NotifyChanged();
        }

        internal float EvaluateMultiplier(float normalizedLifetime)
        {
            return multiplier.Evaluate(Mathf.Clamp01(normalizedLifetime));
        }

        internal int CopyInfluenceEntityIds(NativeList<ulong> destination)
        {
            destination.Clear();
            if (m_Influences == null)
                return 0;
            for (int index = 0; index < m_Influences.Length; index++)
            {
                VividParticleForceField field = m_Influences[index];
                if (field != null)
                    destination.Add(UnityEngine.EntityId.ToULong(field.GetEntityId()));
            }
            return destination.Length;
        }

        internal bool ContainsInfluenceEntityId(ulong entityId)
        {
            if (m_Influences == null)
                return false;
            for (int index = 0; index < m_Influences.Length; index++)
            {
                VividParticleForceField field = m_Influences[index];
                if (field != null
                    && UnityEngine.EntityId.ToULong(field.GetEntityId()) == entityId)
                {
                    return true;
                }
            }
            return false;
        }

        internal void SetChangeCallback(Action onChanged) => m_OnChanged = onChanged;

        internal static VividParticleExternalForcesModule CreateDefault() => new();

        internal void CopyFrom(VividParticleExternalForcesModule source)
        {
            if (source == null)
                return;
            m_Enabled = source.m_Enabled;
            m_Multiplier = CloneCurve(source.m_Multiplier, 1.0f);
            m_InfluenceFilter = source.m_InfluenceFilter;
            m_InfluenceMask = source.m_InfluenceMask;
            m_Influences = source.m_Influences != null
                ? (VividParticleForceField[])source.m_Influences.Clone()
                : Array.Empty<VividParticleForceField>();
            Validate();
        }

        internal void Validate()
        {
            m_Multiplier ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            if (!Enum.IsDefined(typeof(VividParticleGameObjectFilter), m_InfluenceFilter))
                m_InfluenceFilter = VividParticleGameObjectFilter.LayerMask;
            m_Influences ??= Array.Empty<VividParticleForceField>();
        }

        private static AnimationCurve CloneCurve(AnimationCurve source, float fallback)
        {
            source ??= AnimationCurve.Constant(0.0f, 1.0f, fallback);
            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }

        private void NotifyChanged() => m_OnChanged?.Invoke();
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Vivid Particle Force Field")]
    public sealed class VividParticleForceField : MonoBehaviour
    {
        [SerializeField]
        private VividParticleForceFieldShape m_Shape;

        [SerializeField, Min(0.0f)]
        private float m_StartRange;

        [SerializeField, Min(0.0f)]
        private float m_EndRange = 1.0f;

        [SerializeField, Min(0.0f)]
        private float m_Length = 1.0f;

        [SerializeField]
        private AnimationCurve m_DirectionX = CreateCurve(0.0f);

        [SerializeField]
        private AnimationCurve m_DirectionY = CreateCurve(0.0f);

        [SerializeField]
        private AnimationCurve m_DirectionZ = CreateCurve(0.0f);

        [SerializeField]
        private AnimationCurve m_Gravity = CreateCurve(0.0f);

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_GravityFocus;

        [SerializeField]
        private AnimationCurve m_RotationSpeed = CreateCurve(0.0f);

        [SerializeField]
        private AnimationCurve m_RotationAttraction = CreateCurve(0.0f);

        [SerializeField]
        private Vector2 m_RotationRandomness;

        [SerializeField]
        private AnimationCurve m_Drag = CreateCurve(0.0f);

        [SerializeField]
        private bool m_MultiplyDragByParticleSize;

        [SerializeField]
        private bool m_MultiplyDragByParticleVelocity;

        [SerializeField]
        private Texture3D m_VectorField;

        [SerializeField]
        private AnimationCurve m_VectorFieldSpeed = CreateCurve(1.0f);

        [SerializeField]
        private AnimationCurve m_VectorFieldAttraction = CreateCurve(0.0f);

        private Matrix4x4 m_LastLocalToWorld;
        private int m_SettingsVersion;
        private int m_LastPreparedSettingsVersion = -1;

        public VividParticleForceFieldShape shape { get => m_Shape; set { m_Shape = value; MarkDirty(); } }
        public float startRange { get => m_StartRange; set { m_StartRange = Mathf.Max(0.0f, value); ValidateRanges(); MarkDirty(); } }
        public float endRange { get => m_EndRange; set { m_EndRange = Mathf.Max(0.0f, value); ValidateRanges(); MarkDirty(); } }
        public float length { get => m_Length; set { m_Length = Mathf.Max(0.0f, value); MarkDirty(); } }
        public AnimationCurve directionX { get => m_DirectionX; set { m_DirectionX = value ?? CreateCurve(0.0f); MarkDirty(); } }
        public AnimationCurve directionY { get => m_DirectionY; set { m_DirectionY = value ?? CreateCurve(0.0f); MarkDirty(); } }
        public AnimationCurve directionZ { get => m_DirectionZ; set { m_DirectionZ = value ?? CreateCurve(0.0f); MarkDirty(); } }
        public AnimationCurve gravity { get => m_Gravity; set { m_Gravity = value ?? CreateCurve(0.0f); MarkDirty(); } }
        public float gravityFocus { get => m_GravityFocus; set { m_GravityFocus = Mathf.Clamp01(value); MarkDirty(); } }
        public AnimationCurve rotationSpeed { get => m_RotationSpeed; set { m_RotationSpeed = value ?? CreateCurve(0.0f); MarkDirty(); } }
        public AnimationCurve rotationAttraction { get => m_RotationAttraction; set { m_RotationAttraction = value ?? CreateCurve(0.0f); MarkDirty(); } }
        public Vector2 rotationRandomness { get => m_RotationRandomness; set { m_RotationRandomness = Vector2.Max(Vector2.zero, value); MarkDirty(); } }
        public AnimationCurve drag { get => m_Drag; set { m_Drag = value ?? CreateCurve(0.0f); MarkDirty(); } }
        public bool multiplyDragByParticleSize { get => m_MultiplyDragByParticleSize; set { m_MultiplyDragByParticleSize = value; MarkDirty(); } }
        public bool multiplyDragByParticleVelocity { get => m_MultiplyDragByParticleVelocity; set { m_MultiplyDragByParticleVelocity = value; MarkDirty(); } }
        public Texture3D vectorField { get => m_VectorField; set { m_VectorField = value; MarkDirty(); } }
        public AnimationCurve vectorFieldSpeed { get => m_VectorFieldSpeed; set { m_VectorFieldSpeed = value ?? CreateCurve(1.0f); MarkDirty(); } }
        public AnimationCurve vectorFieldAttraction { get => m_VectorFieldAttraction; set { m_VectorFieldAttraction = value ?? CreateCurve(0.0f); MarkDirty(); } }

        private void OnEnable()
        {
            Validate();
            m_LastLocalToWorld = transform.localToWorldMatrix;
            VividParticleForceFieldRegistry.Register(this);
        }

        private void OnDisable() => VividParticleForceFieldRegistry.Unregister(this);

        private void OnDestroy() => VividParticleForceFieldRegistry.Unregister(this);

        private void OnValidate()
        {
            Validate();
            MarkDirty();
        }

        internal bool ConsumeRuntimeDirty()
        {
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            bool dirty = m_LastPreparedSettingsVersion != m_SettingsVersion
                || m_LastLocalToWorld != localToWorld;
            m_LastPreparedSettingsVersion = m_SettingsVersion;
            m_LastLocalToWorld = localToWorld;
            return dirty;
        }

        internal unsafe VividParticleNativeForceField CreateNative(int vectorFieldOffset)
        {
            var data = new VividParticleNativeForceField
            {
                EntityId = UnityEngine.EntityId.ToULong(GetEntityId()),
                Layer = gameObject.layer,
                Shape = (int)m_Shape,
                Active = isActiveAndEnabled ? 1 : 0,
                StartRange = m_StartRange,
                EndRange = m_EndRange,
                Length = m_Length,
                GravityFocus = m_GravityFocus,
                RotationRandomness = new float2(m_RotationRandomness.x, m_RotationRandomness.y),
                MultiplyDragByParticleSize = m_MultiplyDragByParticleSize ? 1 : 0,
                MultiplyDragByParticleVelocity = m_MultiplyDragByParticleVelocity ? 1 : 0,
                LocalToWorld = ToFloat4x4(transform.localToWorldMatrix),
                WorldToLocal = ToFloat4x4(transform.worldToLocalMatrix),
                VectorFieldOffset = vectorFieldOffset,
                VectorFieldWidth = m_VectorField != null ? m_VectorField.width : 0,
                VectorFieldHeight = m_VectorField != null ? m_VectorField.height : 0,
                VectorFieldDepth = m_VectorField != null ? m_VectorField.depth : 0,
            };
            for (int index = 0; index < VividParticleNativeForceField.LutResolution; index++)
            {
                float t = index / (float)(VividParticleNativeForceField.LutResolution - 1);
                data.DirectionXLut[index] = m_DirectionX.Evaluate(t);
                data.DirectionYLut[index] = m_DirectionY.Evaluate(t);
                data.DirectionZLut[index] = m_DirectionZ.Evaluate(t);
                data.GravityLut[index] = m_Gravity.Evaluate(t);
                data.RotationSpeedLut[index] = m_RotationSpeed.Evaluate(t);
                data.RotationAttractionLut[index] = m_RotationAttraction.Evaluate(t);
                data.DragLut[index] = m_Drag.Evaluate(t);
                data.VectorFieldSpeedLut[index] = m_VectorFieldSpeed.Evaluate(t);
                data.VectorFieldAttractionLut[index] = m_VectorFieldAttraction.Evaluate(t);
            }
            return data;
        }

        internal int AppendVectorFieldData(NativeList<float4> destination)
        {
            if (m_VectorField == null || !m_VectorField.isReadable)
                return -1;
            NativeArray<Color> pixels = m_VectorField.GetPixelData<Color>(0);
            if (!pixels.IsCreated || pixels.Length == 0)
                return -1;
            int offset = destination.Length;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color value = pixels[index];
                destination.Add(new float4(value.r, value.g, value.b, value.a));
            }
            return offset;
        }

        private void Validate()
        {
            if (!Enum.IsDefined(typeof(VividParticleForceFieldShape), m_Shape))
                m_Shape = VividParticleForceFieldShape.Sphere;
            m_StartRange = Mathf.Max(0.0f, m_StartRange);
            m_EndRange = Mathf.Max(m_StartRange, m_EndRange);
            m_Length = Mathf.Max(0.0f, m_Length);
            m_GravityFocus = Mathf.Clamp01(m_GravityFocus);
            m_RotationRandomness = Vector2.Max(Vector2.zero, m_RotationRandomness);
            m_DirectionX ??= CreateCurve(0.0f);
            m_DirectionY ??= CreateCurve(0.0f);
            m_DirectionZ ??= CreateCurve(0.0f);
            m_Gravity ??= CreateCurve(0.0f);
            m_RotationSpeed ??= CreateCurve(0.0f);
            m_RotationAttraction ??= CreateCurve(0.0f);
            m_Drag ??= CreateCurve(0.0f);
            m_VectorFieldSpeed ??= CreateCurve(1.0f);
            m_VectorFieldAttraction ??= CreateCurve(0.0f);
        }

        private void ValidateRanges()
        {
            if (m_EndRange < m_StartRange)
                m_EndRange = m_StartRange;
        }

        private void MarkDirty()
        {
            m_SettingsVersion++;
            VividParticleForceFieldRegistry.MarkDirty();
        }

        private static AnimationCurve CreateCurve(float value) =>
            AnimationCurve.Constant(0.0f, 1.0f, value);

        private static float4x4 ToFloat4x4(Matrix4x4 value)
        {
            return new float4x4(
                new float4(value.m00, value.m10, value.m20, value.m30),
                new float4(value.m01, value.m11, value.m21, value.m31),
                new float4(value.m02, value.m12, value.m22, value.m32),
                new float4(value.m03, value.m13, value.m23, value.m33));
        }
    }

    internal unsafe struct VividParticleNativeForceField
    {
        public const int LutResolution = 16;

        public ulong EntityId;
        public int Layer;
        public int Shape;
        public int Active;
        public float StartRange;
        public float EndRange;
        public float Length;
        public float GravityFocus;
        public float2 RotationRandomness;
        public int MultiplyDragByParticleSize;
        public int MultiplyDragByParticleVelocity;
        public int VectorFieldOffset;
        public int VectorFieldWidth;
        public int VectorFieldHeight;
        public int VectorFieldDepth;
        public float4x4 LocalToWorld;
        public float4x4 WorldToLocal;
        public fixed float DirectionXLut[LutResolution];
        public fixed float DirectionYLut[LutResolution];
        public fixed float DirectionZLut[LutResolution];
        public fixed float GravityLut[LutResolution];
        public fixed float RotationSpeedLut[LutResolution];
        public fixed float RotationAttractionLut[LutResolution];
        public fixed float DragLut[LutResolution];
        public fixed float VectorFieldSpeedLut[LutResolution];
        public fixed float VectorFieldAttractionLut[LutResolution];
    }

    internal static unsafe class VividParticleForceFieldRegistry
    {
        private static readonly List<VividParticleForceField> s_Fields = new();
        private static NativeList<VividParticleNativeForceField> s_NativeFields;
        private static NativeList<float4> s_VectorFieldData;
        private static bool s_Dirty = true;
        private static int s_Version;

        public static int version => s_Version;
        public static int count => s_NativeFields.IsCreated ? s_NativeFields.Length : 0;
        public static int vectorFieldValueCount => s_VectorFieldData.IsCreated ? s_VectorFieldData.Length : 0;
        public static VividParticleNativeForceField* fields => s_NativeFields.IsCreated
            ? (VividParticleNativeForceField*)s_NativeFields.GetUnsafeReadOnlyPtr()
            : null;
        public static float4* vectorFieldData => s_VectorFieldData.IsCreated
            ? (float4*)s_VectorFieldData.GetUnsafeReadOnlyPtr()
            : null;

        public static void Register(VividParticleForceField field)
        {
            if (field == null || s_Fields.Contains(field))
                return;
            s_Fields.Add(field);
            s_Dirty = true;
        }

        public static void Unregister(VividParticleForceField field)
        {
            if (field != null && s_Fields.Remove(field))
                s_Dirty = true;
        }

        public static void MarkDirty() => s_Dirty = true;

        public static bool Prepare()
        {
            for (int index = s_Fields.Count - 1; index >= 0; index--)
            {
                VividParticleForceField field = s_Fields[index];
                if (field == null)
                {
                    s_Fields.RemoveAt(index);
                    s_Dirty = true;
                }
                else if (field.ConsumeRuntimeDirty())
                {
                    s_Dirty = true;
                }
            }

            if (!s_Dirty)
                return false;

            EnsureCreated();
            s_NativeFields.Clear();
            s_VectorFieldData.Clear();
            for (int index = 0; index < s_Fields.Count; index++)
            {
                VividParticleForceField field = s_Fields[index];
                if (field == null)
                    continue;
                int vectorOffset = field.AppendVectorFieldData(s_VectorFieldData);
                s_NativeFields.Add(field.CreateNative(vectorOffset));
            }
            s_Dirty = false;
            s_Version++;
            return true;
        }

        public static void ResolveInfluences(
            VividParticleExternalForcesModule module,
            NativeList<int> destination)
        {
            destination.Clear();
            if (module == null || !module.enabled || !s_NativeFields.IsCreated)
                return;

            int layerMask = module.influenceMask.value;
            VividParticleGameObjectFilter filter = module.influenceFilter;
            for (int index = 0; index < s_NativeFields.Length; index++)
            {
                VividParticleNativeForceField field = s_NativeFields[index];
                bool matchesLayer = (layerMask & (1 << math.clamp(field.Layer, 0, 31))) != 0;
                bool matchesList = module.ContainsInfluenceEntityId(field.EntityId);
                bool include = filter switch
                {
                    VividParticleGameObjectFilter.List => matchesList,
                    VividParticleGameObjectFilter.LayerMaskAndList => matchesLayer || matchesList,
                    _ => matchesLayer,
                };
                if (include && field.Active != 0)
                    destination.Add(index);
            }
        }

        public static void ClearForTests()
        {
            s_Fields.Clear();
            if (s_NativeFields.IsCreated)
                s_NativeFields.Dispose();
            if (s_VectorFieldData.IsCreated)
                s_VectorFieldData.Dispose();
            s_NativeFields = default;
            s_VectorFieldData = default;
            s_Dirty = true;
            s_Version = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ClearForTests();

        private static void EnsureCreated()
        {
            if (!s_NativeFields.IsCreated)
                s_NativeFields = new NativeList<VividParticleNativeForceField>(16, Allocator.Persistent);
            if (!s_VectorFieldData.IsCreated)
                s_VectorFieldData = new NativeList<float4>(64, Allocator.Persistent);
        }
    }
}
