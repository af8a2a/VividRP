using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.Particle
{
    public enum VividParticleSystemSimulationSpace
    {
        Local,
        World,
    }

    public enum VividParticleShapeType
    {
        Point,
        Sphere,
        Box,
        Cone,
    }

    public enum VividParticleSystemStopBehavior
    {
        StopEmitting,
        StopEmittingAndClear,
    }

    public enum VividParticleRenderMode
    {
        Billboard,
        Stretch,
        HorizontalBillboard,
        VerticalBillboard,
        Mesh,
        None,
    }

    public enum VividParticleGpuDataMode
    {
        Shared,
        PerParticle,
    }

    public enum VividParticleSortMode
    {
        None,
        ByDistance,
    }

    [Serializable]
    public struct VividParticleBurst
    {
        [SerializeField]
        private float m_Time;

        [SerializeField]
        private int m_Count;

        public VividParticleBurst(float time, int count)
        {
            m_Time = Mathf.Max(0.0f, time);
            m_Count = Mathf.Max(0, count);
        }

        public float time
        {
            get => m_Time;
            set => m_Time = Mathf.Max(0.0f, value);
        }

        public int count
        {
            get => m_Count;
            set => m_Count = Mathf.Max(0, value);
        }

        internal void Validate()
        {
            m_Time = Mathf.Max(0.0f, m_Time);
            m_Count = Mathf.Max(0, m_Count);
        }
    }

    [Serializable]
    public sealed class VividParticleMainModule
    {
        internal const float MinimumDuration = 0.001f;
        internal const float MinimumStartLifetime = 0.001f;
        internal const float MinimumStartSize = 0.001f;
        internal const int MinimumMaxParticles = 1;

        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private float m_Duration = 5.0f;

        [SerializeField]
        private bool m_Loop = true;

        [SerializeField]
        private bool m_PlayOnAwake = true;

        [SerializeField]
        private float m_StartLifetime = 5.0f;

        [SerializeField]
        private float m_StartSpeed = 1.0f;

        [SerializeField]
        private float m_StartSize = 1.0f;

        [SerializeField]
        private Color m_StartColor = Color.white;

        [SerializeField]
        private float m_GravityModifier;

        [SerializeField]
        private VividParticleSystemSimulationSpace m_SimulationSpace = VividParticleSystemSimulationSpace.Local;

        [SerializeField]
        private int m_MaxParticles = 1000;

        [SerializeField]
        private uint m_RandomSeed = 1u;

        [SerializeField]
        private bool m_UseAutoRandomSeed = true;

        public float duration
        {
            get => m_Duration;
            set
            {
                float clamped = Mathf.Max(MinimumDuration, value);
                if (m_Duration == clamped)
                    return;

                m_Duration = clamped;
                NotifyChanged();
            }
        }

        public bool loop
        {
            get => m_Loop;
            set
            {
                if (m_Loop == value)
                    return;

                m_Loop = value;
                NotifyChanged();
            }
        }

        public bool playOnAwake
        {
            get => m_PlayOnAwake;
            set
            {
                if (m_PlayOnAwake == value)
                    return;

                m_PlayOnAwake = value;
                NotifyChanged();
            }
        }

        public float startLifetime
        {
            get => m_StartLifetime;
            set
            {
                float clamped = Mathf.Max(MinimumStartLifetime, value);
                if (m_StartLifetime == clamped)
                    return;

                m_StartLifetime = clamped;
                NotifyChanged();
            }
        }

        public float startSpeed
        {
            get => m_StartSpeed;
            set
            {
                if (m_StartSpeed == value)
                    return;

                m_StartSpeed = value;
                NotifyChanged();
            }
        }

        public float startSize
        {
            get => m_StartSize;
            set
            {
                float clamped = Mathf.Max(MinimumStartSize, value);
                if (m_StartSize == clamped)
                    return;

                m_StartSize = clamped;
                NotifyChanged();
            }
        }

        public Color startColor
        {
            get => m_StartColor;
            set
            {
                if (m_StartColor == value)
                    return;

                m_StartColor = value;
                NotifyChanged();
            }
        }

        public float gravityModifier
        {
            get => m_GravityModifier;
            set
            {
                if (m_GravityModifier == value)
                    return;

                m_GravityModifier = value;
                NotifyChanged();
            }
        }

        public VividParticleSystemSimulationSpace simulationSpace
        {
            get => m_SimulationSpace;
            set
            {
                if (m_SimulationSpace == value)
                    return;

                m_SimulationSpace = value;
                NotifyChanged();
            }
        }

        public int maxParticles
        {
            get => m_MaxParticles;
            set
            {
                int clamped = Mathf.Max(MinimumMaxParticles, value);
                if (m_MaxParticles == clamped)
                    return;

                m_MaxParticles = clamped;
                NotifyChanged();
            }
        }

        public uint randomSeed
        {
            get => m_RandomSeed;
            set
            {
                if (m_RandomSeed == value)
                    return;

                m_RandomSeed = value;
                NotifyChanged();
            }
        }

        public bool useAutoRandomSeed
        {
            get => m_UseAutoRandomSeed;
            set
            {
                if (m_UseAutoRandomSeed == value)
                    return;

                m_UseAutoRandomSeed = value;
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleMainModule CreateDefault()
        {
            return new VividParticleMainModule();
        }

        internal VividParticleMainModule Clone()
        {
            var clone = new VividParticleMainModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleMainModule source)
        {
            if (source == null)
                return;

            m_Duration = source.m_Duration;
            m_Loop = source.m_Loop;
            m_PlayOnAwake = source.m_PlayOnAwake;
            m_StartLifetime = source.m_StartLifetime;
            m_StartSpeed = source.m_StartSpeed;
            m_StartSize = source.m_StartSize;
            m_StartColor = source.m_StartColor;
            m_GravityModifier = source.m_GravityModifier;
            m_SimulationSpace = source.m_SimulationSpace;
            m_MaxParticles = source.m_MaxParticles;
            m_RandomSeed = source.m_RandomSeed;
            m_UseAutoRandomSeed = source.m_UseAutoRandomSeed;
            Validate();
        }

        internal void Validate()
        {
            m_Duration = Mathf.Max(MinimumDuration, m_Duration);
            m_StartLifetime = Mathf.Max(MinimumStartLifetime, m_StartLifetime);
            m_StartSize = Mathf.Max(MinimumStartSize, m_StartSize);
            m_MaxParticles = Mathf.Max(MinimumMaxParticles, m_MaxParticles);
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleEmissionModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled = true;

        [SerializeField]
        private float m_RateOverTime = 10.0f;

        [SerializeField]
        private VividParticleBurst[] m_Bursts = Array.Empty<VividParticleBurst>();

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                if (m_Enabled == value)
                    return;

                m_Enabled = value;
                NotifyChanged();
            }
        }

        public float rateOverTime
        {
            get => m_RateOverTime;
            set
            {
                float clamped = Mathf.Max(0.0f, value);
                if (m_RateOverTime == clamped)
                    return;

                m_RateOverTime = clamped;
                NotifyChanged();
            }
        }

        public VividParticleBurst[] bursts
        {
            get => m_Bursts;
            set
            {
                VividParticleBurst[] next = value ?? Array.Empty<VividParticleBurst>();
                if (ReferenceEquals(m_Bursts, next))
                    return;

                m_Bursts = next;
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleEmissionModule CreateDefault()
        {
            return new VividParticleEmissionModule();
        }

        internal VividParticleEmissionModule Clone()
        {
            var clone = new VividParticleEmissionModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleEmissionModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_RateOverTime = source.m_RateOverTime;
            m_Bursts = source.m_Bursts != null
                ? (VividParticleBurst[])source.m_Bursts.Clone()
                : Array.Empty<VividParticleBurst>();
            Validate();
        }

        internal void Validate()
        {
            m_RateOverTime = Mathf.Max(0.0f, m_RateOverTime);
            m_Bursts ??= Array.Empty<VividParticleBurst>();
            for (int index = 0; index < m_Bursts.Length; index++)
                m_Bursts[index].Validate();
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleShapeModule
    {
        internal const float MinimumRadius = 0.0f;
        internal const float MinimumBoxExtent = 0.0f;

        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled = true;

        [SerializeField]
        private VividParticleShapeType m_ShapeType = VividParticleShapeType.Point;

        [SerializeField]
        private float m_Radius = 1.0f;

        [SerializeField]
        private Vector3 m_BoxSize = Vector3.one;

        [SerializeField]
        private float m_Angle = 25.0f;

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                if (m_Enabled == value)
                    return;

                m_Enabled = value;
                NotifyChanged();
            }
        }

        public VividParticleShapeType shapeType
        {
            get => m_ShapeType;
            set
            {
                if (m_ShapeType == value)
                    return;

                m_ShapeType = value;
                NotifyChanged();
            }
        }

        public float radius
        {
            get => m_Radius;
            set
            {
                float clamped = Mathf.Max(MinimumRadius, value);
                if (m_Radius == clamped)
                    return;

                m_Radius = clamped;
                NotifyChanged();
            }
        }

        public Vector3 boxSize
        {
            get => m_BoxSize;
            set
            {
                Vector3 clamped = Max(value, Vector3.zero);
                if (m_BoxSize == clamped)
                    return;

                m_BoxSize = clamped;
                NotifyChanged();
            }
        }

        public float angle
        {
            get => m_Angle;
            set
            {
                float clamped = Mathf.Clamp(value, 0.0f, 89.0f);
                if (m_Angle == clamped)
                    return;

                m_Angle = clamped;
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleShapeModule CreateDefault()
        {
            return new VividParticleShapeModule();
        }

        internal VividParticleShapeModule Clone()
        {
            var clone = new VividParticleShapeModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleShapeModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_ShapeType = source.m_ShapeType;
            m_Radius = source.m_Radius;
            m_BoxSize = source.m_BoxSize;
            m_Angle = source.m_Angle;
            Validate();
        }

        internal void Validate()
        {
            m_Radius = Mathf.Max(MinimumRadius, m_Radius);
            m_BoxSize = Max(m_BoxSize, Vector3.zero);
            m_Angle = Mathf.Clamp(m_Angle, 0.0f, 89.0f);
        }

        private static Vector3 Max(Vector3 value, Vector3 minimum)
        {
            return new Vector3(
                Mathf.Max(value.x, minimum.x),
                Mathf.Max(value.y, minimum.y),
                Mathf.Max(value.z, minimum.z));
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleRendererModule
    {
        internal const float MinimumSizeScale = 0.001f;
        internal const float MinimumStretchLengthScale = 0.0f;
        internal const float MinimumStretchSpeedScale = 0.0f;

        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled = true;

        [SerializeField]
        private VividParticleRenderMode m_RenderMode = VividParticleRenderMode.Billboard;

        [SerializeField]
        private Material m_Material;

        [SerializeField]
        private Mesh m_Mesh;

        [SerializeField]
        private Mesh[] m_Meshes = Array.Empty<Mesh>();

        [SerializeField]
        private Color m_Color = Color.white;

        [SerializeField]
        private float m_SizeScale = 1.0f;

        [SerializeField]
        private float m_StretchLengthScale = 2.0f;

        [SerializeField]
        private float m_StretchSpeedScale;

        [SerializeField]
        private Vector3 m_Pivot;

        [SerializeField]
        private float m_MinParticleSize;

        [SerializeField]
        private float m_MaxParticleSize;

        [SerializeField]
        private Vector3 m_Flip;

        [SerializeField]
        private int m_RenderQueueOffset;

        [SerializeField]
        private ShadowCastingMode m_ShadowCastingMode = ShadowCastingMode.Off;

        [SerializeField]
        private bool m_ReceiveShadows;

        [SerializeField]
        private uint m_RenderingLayerMask = uint.MaxValue;

        [SerializeField]
        private VividParticleGpuDataMode m_ColorDataMode = VividParticleGpuDataMode.Shared;

        [SerializeField]
        private VividParticleGpuDataMode m_RotationDataMode = VividParticleGpuDataMode.Shared;

        [SerializeField]
        private VividParticleGpuDataMode m_VelocityDataMode = VividParticleGpuDataMode.Shared;

        [SerializeField]
        private VividParticleGpuDataMode m_SizeDataMode = VividParticleGpuDataMode.Shared;

        [SerializeField]
        private bool m_UVDataEnabled;

        [SerializeField]
        private bool m_CustomData1Enabled;

        [SerializeField]
        private bool m_CustomData2Enabled;

        [SerializeField]
        private bool m_MeshIndexDataEnabled;

        [SerializeField]
        private VividParticleSortMode m_SortMode;

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                if (m_Enabled == value)
                    return;

                m_Enabled = value;
                NotifyChanged();
            }
        }

        public VividParticleRenderMode renderMode
        {
            get => m_RenderMode;
            set
            {
                if (m_RenderMode == value)
                    return;

                m_RenderMode = value;
                NotifyChanged();
            }
        }

        public Material material
        {
            get => m_Material;
            set
            {
                if (m_Material == value)
                    return;

                m_Material = value;
                NotifyChanged();
            }
        }

        public Mesh mesh
        {
            get => m_Mesh;
            set
            {
                if (m_Mesh == value)
                    return;

                m_Mesh = value;
                NotifyChanged();
            }
        }

        public Mesh[] meshes
        {
            get => CopyMeshes(m_Meshes);
            set
            {
                Mesh[] copy = CopyMeshes(value);
                if (MeshArraysEqual(m_Meshes, copy))
                    return;

                m_Meshes = copy;
                NotifyChanged();
            }
        }

        public int meshCount => GetMeshCount();

        public Color color
        {
            get => m_Color;
            set
            {
                if (m_Color == value)
                    return;

                m_Color = value;
                NotifyChanged();
            }
        }

        public float sizeScale
        {
            get => m_SizeScale;
            set
            {
                float clamped = Mathf.Max(MinimumSizeScale, value);
                if (m_SizeScale == clamped)
                    return;

                m_SizeScale = clamped;
                NotifyChanged();
            }
        }

        public float stretchLengthScale
        {
            get => m_StretchLengthScale;
            set
            {
                float clamped = Mathf.Max(MinimumStretchLengthScale, value);
                if (m_StretchLengthScale == clamped)
                    return;

                m_StretchLengthScale = clamped;
                NotifyChanged();
            }
        }

        public float stretchSpeedScale
        {
            get => m_StretchSpeedScale;
            set
            {
                float clamped = Mathf.Max(MinimumStretchSpeedScale, value);
                if (m_StretchSpeedScale == clamped)
                    return;

                m_StretchSpeedScale = clamped;
                NotifyChanged();
            }
        }

        public Vector3 pivot
        {
            get => m_Pivot;
            set
            {
                if (m_Pivot == value)
                    return;

                m_Pivot = value;
                NotifyChanged();
            }
        }

        public float minParticleSize
        {
            get => m_MinParticleSize;
            set
            {
                float clamped = Mathf.Max(0.0f, value);
                if (m_MinParticleSize == clamped)
                    return;

                m_MinParticleSize = clamped;
                NotifyChanged();
            }
        }

        public float maxParticleSize
        {
            get => m_MaxParticleSize;
            set
            {
                float clamped = Mathf.Max(0.0f, value);
                if (m_MaxParticleSize == clamped)
                    return;

                m_MaxParticleSize = clamped;
                NotifyChanged();
            }
        }

        public Vector3 flip
        {
            get => m_Flip;
            set
            {
                Vector3 clamped = new(
                    Mathf.Clamp01(value.x),
                    Mathf.Clamp01(value.y),
                    Mathf.Clamp01(value.z));
                if (m_Flip == clamped)
                    return;

                m_Flip = clamped;
                NotifyChanged();
            }
        }

        public int renderQueueOffset
        {
            get => m_RenderQueueOffset;
            set
            {
                if (m_RenderQueueOffset == value)
                    return;

                m_RenderQueueOffset = value;
                NotifyChanged();
            }
        }

        public ShadowCastingMode shadowCastingMode
        {
            get => m_ShadowCastingMode;
            set
            {
                if (m_ShadowCastingMode == value)
                    return;

                m_ShadowCastingMode = value;
                NotifyChanged();
            }
        }

        public bool receiveShadows
        {
            get => m_ReceiveShadows;
            set
            {
                if (m_ReceiveShadows == value)
                    return;

                m_ReceiveShadows = value;
                NotifyChanged();
            }
        }

        public uint renderingLayerMask
        {
            get => m_RenderingLayerMask;
            set
            {
                if (m_RenderingLayerMask == value)
                    return;

                m_RenderingLayerMask = value;
                NotifyChanged();
            }
        }

        public VividParticleGpuDataMode colorDataMode
        {
            get => m_ColorDataMode;
            set
            {
                if (m_ColorDataMode == value)
                    return;

                m_ColorDataMode = value;
                NotifyChanged();
            }
        }

        public VividParticleGpuDataMode rotationDataMode
        {
            get => m_RotationDataMode;
            set
            {
                if (m_RotationDataMode == value)
                    return;

                m_RotationDataMode = value;
                NotifyChanged();
            }
        }

        public VividParticleGpuDataMode velocityDataMode
        {
            get => m_VelocityDataMode;
            set
            {
                if (m_VelocityDataMode == value)
                    return;

                m_VelocityDataMode = value;
                NotifyChanged();
            }
        }

        public VividParticleGpuDataMode sizeDataMode
        {
            get => m_SizeDataMode;
            set
            {
                if (m_SizeDataMode == value)
                    return;

                m_SizeDataMode = value;
                NotifyChanged();
            }
        }

        public bool uvDataEnabled
        {
            get => m_UVDataEnabled;
            set
            {
                if (m_UVDataEnabled == value)
                    return;

                m_UVDataEnabled = value;
                NotifyChanged();
            }
        }

        public bool customData1Enabled
        {
            get => m_CustomData1Enabled;
            set
            {
                if (m_CustomData1Enabled == value)
                    return;

                m_CustomData1Enabled = value;
                NotifyChanged();
            }
        }

        public bool customData2Enabled
        {
            get => m_CustomData2Enabled;
            set
            {
                if (m_CustomData2Enabled == value)
                    return;

                m_CustomData2Enabled = value;
                NotifyChanged();
            }
        }

        public bool meshIndexDataEnabled
        {
            get => m_MeshIndexDataEnabled;
            set
            {
                if (m_MeshIndexDataEnabled == value)
                    return;

                m_MeshIndexDataEnabled = value;
                NotifyChanged();
            }
        }

        public VividParticleSortMode sortMode
        {
            get => m_SortMode;
            set
            {
                if (m_SortMode == value)
                    return;

                m_SortMode = value;
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleRendererModule CreateDefault()
        {
            return new VividParticleRendererModule();
        }

        internal VividParticleRendererModule Clone()
        {
            var clone = new VividParticleRendererModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleRendererModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_RenderMode = source.m_RenderMode;
            m_Material = source.m_Material;
            m_Mesh = source.m_Mesh;
            m_Meshes = CopyMeshes(source.m_Meshes);
            m_Color = source.m_Color;
            m_SizeScale = source.m_SizeScale;
            m_StretchLengthScale = source.m_StretchLengthScale;
            m_StretchSpeedScale = source.m_StretchSpeedScale;
            m_Pivot = source.m_Pivot;
            m_MinParticleSize = source.m_MinParticleSize;
            m_MaxParticleSize = source.m_MaxParticleSize;
            m_Flip = source.m_Flip;
            m_RenderQueueOffset = source.m_RenderQueueOffset;
            m_ShadowCastingMode = source.m_ShadowCastingMode;
            m_ReceiveShadows = source.m_ReceiveShadows;
            m_RenderingLayerMask = source.m_RenderingLayerMask;
            m_ColorDataMode = source.m_ColorDataMode;
            m_RotationDataMode = source.m_RotationDataMode;
            m_VelocityDataMode = source.m_VelocityDataMode;
            m_SizeDataMode = source.m_SizeDataMode;
            m_UVDataEnabled = source.m_UVDataEnabled;
            m_CustomData1Enabled = source.m_CustomData1Enabled;
            m_CustomData2Enabled = source.m_CustomData2Enabled;
            m_MeshIndexDataEnabled = source.m_MeshIndexDataEnabled;
            m_SortMode = source.m_SortMode;
            Validate();
        }

        internal void Validate()
        {
            m_SizeScale = Mathf.Max(MinimumSizeScale, m_SizeScale);
            m_StretchLengthScale = Mathf.Max(MinimumStretchLengthScale, m_StretchLengthScale);
            m_StretchSpeedScale = Mathf.Max(MinimumStretchSpeedScale, m_StretchSpeedScale);
            m_MinParticleSize = Mathf.Max(0.0f, m_MinParticleSize);
            m_MaxParticleSize = Mathf.Max(0.0f, m_MaxParticleSize);
            m_Meshes ??= Array.Empty<Mesh>();
            m_Flip = new Vector3(
                Mathf.Clamp01(m_Flip.x),
                Mathf.Clamp01(m_Flip.y),
                Mathf.Clamp01(m_Flip.z));
        }

        internal Mesh renderMesh => ResolveRenderMesh();

        internal bool hasRenderMesh => renderMesh != null;

        internal int meshSetHash
        {
            get
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 397) ^ GetMeshHash(m_Mesh);
                    Mesh[] meshes = m_Meshes;
                    int count = meshes?.Length ?? 0;
                    hash = (hash * 397) ^ count;
                    for (int index = 0; index < count; index++)
                        hash = (hash * 397) ^ GetMeshHash(meshes[index]);

                    return hash;
                }
            }
        }

        public int GetMeshes(Mesh[] meshes)
        {
            int count = 0;
            if (m_Mesh != null)
            {
                if (meshes != null && count < meshes.Length)
                    meshes[count] = m_Mesh;

                count++;
            }

            Mesh[] source = m_Meshes;
            int sourceCount = source?.Length ?? 0;
            for (int index = 0; index < sourceCount; index++)
            {
                Mesh mesh = source[index];
                if (mesh == null)
                    continue;

                if (meshes != null && count < meshes.Length)
                    meshes[count] = mesh;

                count++;
            }

            return count;
        }

        public void SetMeshes(Mesh[] meshes)
        {
            SetMeshes(meshes, meshes?.Length ?? 0);
        }

        public void SetMeshes(Mesh[] meshes, int size)
        {
            int count = Mathf.Clamp(size, 0, meshes?.Length ?? 0);
            Mesh primary = null;
            var extraMeshes = new Mesh[Mathf.Max(0, count - 1)];
            for (int index = 0; index < count; index++)
            {
                Mesh mesh = meshes[index];
                if (index == 0)
                    primary = mesh;
                else
                    extraMeshes[index - 1] = mesh;
            }

            if (m_Mesh == primary && MeshArraysEqual(m_Meshes, extraMeshes))
                return;

            m_Mesh = primary;
            m_Meshes = extraMeshes;
            NotifyChanged();
        }

        private Mesh ResolveRenderMesh()
        {
            if (m_Mesh != null)
                return m_Mesh;

            Mesh[] meshes = m_Meshes;
            int count = meshes?.Length ?? 0;
            for (int index = 0; index < count; index++)
            {
                if (meshes[index] != null)
                    return meshes[index];
            }

            return null;
        }

        private int GetMeshCount()
        {
            int count = m_Mesh != null ? 1 : 0;
            Mesh[] meshes = m_Meshes;
            int sourceCount = meshes?.Length ?? 0;
            for (int index = 0; index < sourceCount; index++)
            {
                if (meshes[index] != null)
                    count++;
            }

            return count;
        }

        private static Mesh[] CopyMeshes(Mesh[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<Mesh>();

            var copy = new Mesh[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static bool MeshArraysEqual(Mesh[] left, Mesh[] right)
        {
            int leftCount = left?.Length ?? 0;
            int rightCount = right?.Length ?? 0;
            if (leftCount != rightCount)
                return false;

            for (int index = 0; index < leftCount; index++)
            {
                if (left[index] != right[index])
                    return false;
            }

            return true;
        }

        private static int GetMeshHash(Mesh mesh)
        {
            return mesh != null ? mesh.GetEntityId().GetHashCode() : 0;
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }
}
