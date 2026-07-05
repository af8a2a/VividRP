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
            set => m_Duration = Mathf.Max(MinimumDuration, value);
        }

        public bool loop
        {
            get => m_Loop;
            set => m_Loop = value;
        }

        public bool playOnAwake
        {
            get => m_PlayOnAwake;
            set => m_PlayOnAwake = value;
        }

        public float startLifetime
        {
            get => m_StartLifetime;
            set => m_StartLifetime = Mathf.Max(MinimumStartLifetime, value);
        }

        public float startSpeed
        {
            get => m_StartSpeed;
            set => m_StartSpeed = value;
        }

        public float startSize
        {
            get => m_StartSize;
            set => m_StartSize = Mathf.Max(MinimumStartSize, value);
        }

        public Color startColor
        {
            get => m_StartColor;
            set => m_StartColor = value;
        }

        public float gravityModifier
        {
            get => m_GravityModifier;
            set => m_GravityModifier = value;
        }

        public VividParticleSystemSimulationSpace simulationSpace
        {
            get => m_SimulationSpace;
            set => m_SimulationSpace = value;
        }

        public int maxParticles
        {
            get => m_MaxParticles;
            set => m_MaxParticles = Mathf.Max(MinimumMaxParticles, value);
        }

        public uint randomSeed
        {
            get => m_RandomSeed;
            set => m_RandomSeed = value;
        }

        public bool useAutoRandomSeed
        {
            get => m_UseAutoRandomSeed;
            set => m_UseAutoRandomSeed = value;
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
    }

    [Serializable]
    public sealed class VividParticleEmissionModule
    {
        [SerializeField]
        private bool m_Enabled = true;

        [SerializeField]
        private float m_RateOverTime = 10.0f;

        [SerializeField]
        private VividParticleBurst[] m_Bursts = Array.Empty<VividParticleBurst>();

        public bool enabled
        {
            get => m_Enabled;
            set => m_Enabled = value;
        }

        public float rateOverTime
        {
            get => m_RateOverTime;
            set => m_RateOverTime = Mathf.Max(0.0f, value);
        }

        public VividParticleBurst[] bursts
        {
            get => m_Bursts;
            set => m_Bursts = value ?? Array.Empty<VividParticleBurst>();
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
    }

    [Serializable]
    public sealed class VividParticleShapeModule
    {
        internal const float MinimumRadius = 0.0f;
        internal const float MinimumBoxExtent = 0.0f;

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
            set => m_Enabled = value;
        }

        public VividParticleShapeType shapeType
        {
            get => m_ShapeType;
            set => m_ShapeType = value;
        }

        public float radius
        {
            get => m_Radius;
            set => m_Radius = Mathf.Max(MinimumRadius, value);
        }

        public Vector3 boxSize
        {
            get => m_BoxSize;
            set => m_BoxSize = Max(value, Vector3.zero);
        }

        public float angle
        {
            get => m_Angle;
            set => m_Angle = Mathf.Clamp(value, 0.0f, 89.0f);
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
    }

    [Serializable]
    public sealed class VividParticleRendererModule
    {
        internal const float MinimumSizeScale = 0.001f;
        internal const float MinimumStretchLengthScale = 0.0f;
        internal const float MinimumStretchSpeedScale = 0.0f;

        [SerializeField]
        private bool m_Enabled = true;

        [SerializeField]
        private VividParticleRenderMode m_RenderMode = VividParticleRenderMode.Billboard;

        [SerializeField]
        private Material m_Material;

        [SerializeField]
        private Mesh m_Mesh;

        [SerializeField]
        private Color m_Color = Color.white;

        [SerializeField]
        private float m_SizeScale = 1.0f;

        [SerializeField]
        private float m_StretchLengthScale = 2.0f;

        [SerializeField]
        private float m_StretchSpeedScale;

        [SerializeField]
        private int m_RenderQueueOffset;

        [SerializeField]
        private ShadowCastingMode m_ShadowCastingMode = ShadowCastingMode.Off;

        [SerializeField]
        private bool m_ReceiveShadows;

        [SerializeField]
        private VividParticleGpuDataMode m_ColorDataMode = VividParticleGpuDataMode.PerParticle;

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
            set => m_Enabled = value;
        }

        public VividParticleRenderMode renderMode
        {
            get => m_RenderMode;
            set => m_RenderMode = value;
        }

        public Material material
        {
            get => m_Material;
            set => m_Material = value;
        }

        public Mesh mesh
        {
            get => m_Mesh;
            set => m_Mesh = value;
        }

        public Color color
        {
            get => m_Color;
            set => m_Color = value;
        }

        public float sizeScale
        {
            get => m_SizeScale;
            set => m_SizeScale = Mathf.Max(MinimumSizeScale, value);
        }

        public float stretchLengthScale
        {
            get => m_StretchLengthScale;
            set => m_StretchLengthScale = Mathf.Max(MinimumStretchLengthScale, value);
        }

        public float stretchSpeedScale
        {
            get => m_StretchSpeedScale;
            set => m_StretchSpeedScale = Mathf.Max(MinimumStretchSpeedScale, value);
        }

        public int renderQueueOffset
        {
            get => m_RenderQueueOffset;
            set => m_RenderQueueOffset = value;
        }

        public ShadowCastingMode shadowCastingMode
        {
            get => m_ShadowCastingMode;
            set => m_ShadowCastingMode = value;
        }

        public bool receiveShadows
        {
            get => m_ReceiveShadows;
            set => m_ReceiveShadows = value;
        }

        public VividParticleGpuDataMode colorDataMode
        {
            get => m_ColorDataMode;
            set => m_ColorDataMode = value;
        }

        public VividParticleGpuDataMode rotationDataMode
        {
            get => m_RotationDataMode;
            set => m_RotationDataMode = value;
        }

        public VividParticleGpuDataMode velocityDataMode
        {
            get => m_VelocityDataMode;
            set => m_VelocityDataMode = value;
        }

        public VividParticleGpuDataMode sizeDataMode
        {
            get => m_SizeDataMode;
            set => m_SizeDataMode = value;
        }

        public bool uvDataEnabled
        {
            get => m_UVDataEnabled;
            set => m_UVDataEnabled = value;
        }

        public bool customData1Enabled
        {
            get => m_CustomData1Enabled;
            set => m_CustomData1Enabled = value;
        }

        public bool customData2Enabled
        {
            get => m_CustomData2Enabled;
            set => m_CustomData2Enabled = value;
        }

        public bool meshIndexDataEnabled
        {
            get => m_MeshIndexDataEnabled;
            set => m_MeshIndexDataEnabled = value;
        }

        public VividParticleSortMode sortMode
        {
            get => m_SortMode;
            set => m_SortMode = value;
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
            m_Color = source.m_Color;
            m_SizeScale = source.m_SizeScale;
            m_StretchLengthScale = source.m_StretchLengthScale;
            m_StretchSpeedScale = source.m_StretchSpeedScale;
            m_RenderQueueOffset = source.m_RenderQueueOffset;
            m_ShadowCastingMode = source.m_ShadowCastingMode;
            m_ReceiveShadows = source.m_ReceiveShadows;
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
        }
    }
}
