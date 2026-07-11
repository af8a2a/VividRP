using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.Particle
{
    public enum VividParticleSystemSimulationSpace
    {
        Local,
        World,
    }

    public enum VividParticleForceSpace
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

    public enum VividParticleTextureSheetAnimationType
    {
        WholeSheet,
        SingleRow,
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
    public sealed class VividParticleForceOverLifetimeModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private Vector3 m_Force;

        [SerializeField]
        private VividParticleForceSpace m_Space = VividParticleForceSpace.Local;

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

        public Vector3 force
        {
            get => m_Force;
            set
            {
                if (m_Force == value)
                    return;

                m_Force = value;
                NotifyChanged();
            }
        }

        public VividParticleForceSpace space
        {
            get => m_Space;
            set
            {
                if (m_Space == value)
                    return;

                m_Space = value;
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleForceOverLifetimeModule CreateDefault()
        {
            return new VividParticleForceOverLifetimeModule();
        }

        internal VividParticleForceOverLifetimeModule Clone()
        {
            var clone = new VividParticleForceOverLifetimeModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleForceOverLifetimeModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_Force = source.m_Force;
            m_Space = source.m_Space;
            Validate();
        }

        internal void Validate()
        {
            if (!float.IsFinite(m_Force.x))
                m_Force.x = 0.0f;
            if (!float.IsFinite(m_Force.y))
                m_Force.y = 0.0f;
            if (!float.IsFinite(m_Force.z))
                m_Force.z = 0.0f;
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleVelocityOverLifetimeModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private AnimationCurve m_X = AnimationCurve.Constant(0.0f, 1.0f, 0.0f);

        [SerializeField]
        private AnimationCurve m_Y = AnimationCurve.Constant(0.0f, 1.0f, 0.0f);

        [SerializeField]
        private AnimationCurve m_Z = AnimationCurve.Constant(0.0f, 1.0f, 0.0f);

        [SerializeField]
        private VividParticleForceSpace m_Space = VividParticleForceSpace.Local;

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

        public AnimationCurve x
        {
            get => m_X ??= CreateDefaultCurve();
            set
            {
                m_X = CloneCurve(value);
                NotifyChanged();
            }
        }

        public AnimationCurve y
        {
            get => m_Y ??= CreateDefaultCurve();
            set
            {
                m_Y = CloneCurve(value);
                NotifyChanged();
            }
        }

        public AnimationCurve z
        {
            get => m_Z ??= CreateDefaultCurve();
            set
            {
                m_Z = CloneCurve(value);
                NotifyChanged();
            }
        }

        public VividParticleForceSpace space
        {
            get => m_Space;
            set
            {
                if (m_Space == value)
                    return;

                m_Space = value;
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleVelocityOverLifetimeModule CreateDefault()
        {
            return new VividParticleVelocityOverLifetimeModule();
        }

        internal void CopyFrom(VividParticleVelocityOverLifetimeModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_X = CloneCurve(source.m_X);
            m_Y = CloneCurve(source.m_Y);
            m_Z = CloneCurve(source.m_Z);
            m_Space = source.m_Space;
            Validate();
        }

        internal void Validate()
        {
            m_X ??= CreateDefaultCurve();
            m_Y ??= CreateDefaultCurve();
            m_Z ??= CreateDefaultCurve();
        }

        internal Vector3 Evaluate(float normalizedLifetime)
        {
            float time = Mathf.Clamp01(normalizedLifetime);
            return new Vector3(x.Evaluate(time), y.Evaluate(time), z.Evaluate(time));
        }

        private static AnimationCurve CreateDefaultCurve()
        {
            return AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null)
                return CreateDefaultCurve();

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleLimitVelocityOverLifetimeModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private bool m_SeparateAxes;

        [SerializeField]
        private AnimationCurve m_Limit = CreateLimitCurve();

        [SerializeField]
        private AnimationCurve m_LimitX = CreateLimitCurve();

        [SerializeField]
        private AnimationCurve m_LimitY = CreateLimitCurve();

        [SerializeField]
        private AnimationCurve m_LimitZ = CreateLimitCurve();

        [SerializeField]
        private float m_Dampen = 1.0f;

        [SerializeField]
        private VividParticleForceSpace m_Space = VividParticleForceSpace.Local;

        [SerializeField]
        private AnimationCurve m_Drag = CreateDragCurve();

        [SerializeField]
        private bool m_MultiplyDragByParticleSize;

        [SerializeField]
        private bool m_MultiplyDragByParticleVelocity;

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

        public bool separateAxes
        {
            get => m_SeparateAxes;
            set
            {
                if (m_SeparateAxes == value)
                    return;

                m_SeparateAxes = value;
                NotifyChanged();
            }
        }

        public AnimationCurve limit
        {
            get => m_Limit ??= CreateLimitCurve();
            set
            {
                m_Limit = CloneCurve(value, CreateLimitCurve);
                NotifyChanged();
            }
        }

        public AnimationCurve limitX
        {
            get => m_LimitX ??= CreateLimitCurve();
            set
            {
                m_LimitX = CloneCurve(value, CreateLimitCurve);
                NotifyChanged();
            }
        }

        public AnimationCurve limitY
        {
            get => m_LimitY ??= CreateLimitCurve();
            set
            {
                m_LimitY = CloneCurve(value, CreateLimitCurve);
                NotifyChanged();
            }
        }

        public AnimationCurve limitZ
        {
            get => m_LimitZ ??= CreateLimitCurve();
            set
            {
                m_LimitZ = CloneCurve(value, CreateLimitCurve);
                NotifyChanged();
            }
        }

        public float dampen
        {
            get => m_Dampen;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (m_Dampen == clamped)
                    return;

                m_Dampen = clamped;
                NotifyChanged();
            }
        }

        public VividParticleForceSpace space
        {
            get => m_Space;
            set
            {
                if (m_Space == value)
                    return;

                m_Space = value;
                NotifyChanged();
            }
        }

        public AnimationCurve drag
        {
            get => m_Drag ??= CreateDragCurve();
            set
            {
                m_Drag = CloneCurve(value, CreateDragCurve);
                NotifyChanged();
            }
        }

        public bool multiplyDragByParticleSize
        {
            get => m_MultiplyDragByParticleSize;
            set
            {
                if (m_MultiplyDragByParticleSize == value)
                    return;

                m_MultiplyDragByParticleSize = value;
                NotifyChanged();
            }
        }

        public bool multiplyDragByParticleVelocity
        {
            get => m_MultiplyDragByParticleVelocity;
            set
            {
                if (m_MultiplyDragByParticleVelocity == value)
                    return;

                m_MultiplyDragByParticleVelocity = value;
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleLimitVelocityOverLifetimeModule CreateDefault()
        {
            return new VividParticleLimitVelocityOverLifetimeModule();
        }

        internal VividParticleLimitVelocityOverLifetimeModule Clone()
        {
            var clone = new VividParticleLimitVelocityOverLifetimeModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleLimitVelocityOverLifetimeModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_SeparateAxes = source.m_SeparateAxes;
            m_Limit = CloneCurve(source.m_Limit, CreateLimitCurve);
            m_LimitX = CloneCurve(source.m_LimitX, CreateLimitCurve);
            m_LimitY = CloneCurve(source.m_LimitY, CreateLimitCurve);
            m_LimitZ = CloneCurve(source.m_LimitZ, CreateLimitCurve);
            m_Dampen = source.m_Dampen;
            m_Space = source.m_Space;
            m_Drag = CloneCurve(source.m_Drag, CreateDragCurve);
            m_MultiplyDragByParticleSize = source.m_MultiplyDragByParticleSize;
            m_MultiplyDragByParticleVelocity = source.m_MultiplyDragByParticleVelocity;
            Validate();
        }

        internal void Validate()
        {
            m_Limit ??= CreateLimitCurve();
            m_LimitX ??= CreateLimitCurve();
            m_LimitY ??= CreateLimitCurve();
            m_LimitZ ??= CreateLimitCurve();
            m_Drag ??= CreateDragCurve();
            m_Dampen = Mathf.Clamp01(m_Dampen);
        }

        internal Vector3 EvaluateLimit(float normalizedLifetime)
        {
            float time = Mathf.Clamp01(normalizedLifetime);
            if (!m_SeparateAxes)
            {
                float scalarLimit = Mathf.Max(0.0f, limit.Evaluate(time));
                return Vector3.one * scalarLimit;
            }

            return new Vector3(
                Mathf.Max(0.0f, limitX.Evaluate(time)),
                Mathf.Max(0.0f, limitY.Evaluate(time)),
                Mathf.Max(0.0f, limitZ.Evaluate(time)));
        }

        internal float EvaluateDrag(float normalizedLifetime)
        {
            return Mathf.Max(0.0f, drag.Evaluate(Mathf.Clamp01(normalizedLifetime)));
        }

        private static AnimationCurve CreateLimitCurve()
        {
            return AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
        }

        private static AnimationCurve CreateDragCurve()
        {
            return AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
        }

        private static AnimationCurve CloneCurve(
            AnimationCurve source,
            Func<AnimationCurve> defaultFactory)
        {
            if (source == null)
                return defaultFactory();

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleColorOverLifetimeModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private Gradient m_Color = CreateDefaultGradient();

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

        public Gradient color
        {
            get => m_Color ??= CreateDefaultGradient();
            set
            {
                m_Color = CloneGradient(value);
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleColorOverLifetimeModule CreateDefault()
        {
            return new VividParticleColorOverLifetimeModule();
        }

        internal VividParticleColorOverLifetimeModule Clone()
        {
            var clone = new VividParticleColorOverLifetimeModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleColorOverLifetimeModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_Color = CloneGradient(source.m_Color);
            Validate();
        }

        internal void Validate()
        {
            m_Color ??= CreateDefaultGradient();
        }

        internal Color Evaluate(float normalizedLifetime)
        {
            return color.Evaluate(Mathf.Clamp01(normalizedLifetime));
        }

        private static Gradient CreateDefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0.0f),
                    new GradientColorKey(Color.white, 1.0f),
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(1.0f, 1.0f),
                });
            return gradient;
        }

        private static Gradient CloneGradient(Gradient source)
        {
            if (source == null)
                return CreateDefaultGradient();

            var clone = new Gradient
            {
                mode = source.mode,
                colorSpace = source.colorSpace,
            };
            clone.SetKeys(source.colorKeys, source.alphaKeys);
            return clone;
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleColorBySpeedModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private Gradient m_Color = CreateDefaultGradient();

        [SerializeField]
        private Vector2 m_Range = new(0.0f, 1.0f);

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

        public Gradient color
        {
            get => m_Color ??= CreateDefaultGradient();
            set
            {
                m_Color = CloneGradient(value);
                NotifyChanged();
            }
        }

        public Vector2 range
        {
            get => m_Range;
            set
            {
                Vector2 clamped = NormalizeRange(value);
                if (m_Range == clamped)
                    return;

                m_Range = clamped;
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleColorBySpeedModule CreateDefault()
        {
            return new VividParticleColorBySpeedModule();
        }

        internal VividParticleColorBySpeedModule Clone()
        {
            var clone = new VividParticleColorBySpeedModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleColorBySpeedModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_Color = CloneGradient(source.m_Color);
            m_Range = source.m_Range;
            Validate();
        }

        internal void Validate()
        {
            m_Color ??= CreateDefaultGradient();
            m_Range = NormalizeRange(m_Range);
        }

        internal Color Evaluate(float speed)
        {
            float denominator = Mathf.Max(0.000001f, m_Range.y - m_Range.x);
            float normalizedSpeed = Mathf.Clamp01((speed - m_Range.x) / denominator);
            return color.Evaluate(normalizedSpeed);
        }

        private static Vector2 NormalizeRange(Vector2 value)
        {
            float minimum = float.IsFinite(value.x) ? Mathf.Max(0.0f, value.x) : 0.0f;
            float maximum = float.IsFinite(value.y) ? Mathf.Max(0.0f, value.y) : minimum;
            return maximum >= minimum
                ? new Vector2(minimum, maximum)
                : new Vector2(maximum, minimum);
        }

        private static Gradient CreateDefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0.0f),
                    new GradientColorKey(Color.white, 1.0f),
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(1.0f, 1.0f),
                });
            return gradient;
        }

        private static Gradient CloneGradient(Gradient source)
        {
            if (source == null)
                return CreateDefaultGradient();

            var clone = new Gradient
            {
                mode = source.mode,
                colorSpace = source.colorSpace,
            };
            clone.SetKeys(source.colorKeys, source.alphaKeys);
            return clone;
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleSizeOverLifetimeModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private AnimationCurve m_Size = CreateDefaultCurve();

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

        public AnimationCurve size
        {
            get => m_Size ??= CreateDefaultCurve();
            set
            {
                m_Size = CloneCurve(value);
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleSizeOverLifetimeModule CreateDefault()
        {
            return new VividParticleSizeOverLifetimeModule();
        }

        internal VividParticleSizeOverLifetimeModule Clone()
        {
            var clone = new VividParticleSizeOverLifetimeModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleSizeOverLifetimeModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_Size = CloneCurve(source.m_Size);
            Validate();
        }

        internal void Validate()
        {
            m_Size ??= CreateDefaultCurve();
        }

        internal float Evaluate(float normalizedLifetime)
        {
            return Mathf.Max(0.0f, size.Evaluate(Mathf.Clamp01(normalizedLifetime)));
        }

        private static AnimationCurve CreateDefaultCurve()
        {
            return AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 1.0f);
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null)
                return CreateDefaultCurve();

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleSizeBySpeedModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private AnimationCurve m_Size = CreateDefaultCurve();

        [SerializeField]
        private Vector2 m_Range = new(0.0f, 1.0f);

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

        public AnimationCurve size
        {
            get => m_Size ??= CreateDefaultCurve();
            set
            {
                m_Size = CloneCurve(value);
                NotifyChanged();
            }
        }

        public Vector2 range
        {
            get => m_Range;
            set
            {
                Vector2 clamped = NormalizeRange(value);
                if (m_Range == clamped)
                    return;

                m_Range = clamped;
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleSizeBySpeedModule CreateDefault()
        {
            return new VividParticleSizeBySpeedModule();
        }

        internal VividParticleSizeBySpeedModule Clone()
        {
            var clone = new VividParticleSizeBySpeedModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleSizeBySpeedModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_Size = CloneCurve(source.m_Size);
            m_Range = source.m_Range;
            Validate();
        }

        internal void Validate()
        {
            m_Size ??= CreateDefaultCurve();
            m_Range = NormalizeRange(m_Range);
        }

        internal float Evaluate(float speed)
        {
            float denominator = Mathf.Max(0.000001f, m_Range.y - m_Range.x);
            float normalizedSpeed = Mathf.Clamp01((speed - m_Range.x) / denominator);
            return Mathf.Max(0.0f, size.Evaluate(normalizedSpeed));
        }

        private static Vector2 NormalizeRange(Vector2 value)
        {
            float minimum = float.IsFinite(value.x) ? Mathf.Max(0.0f, value.x) : 0.0f;
            float maximum = float.IsFinite(value.y) ? Mathf.Max(0.0f, value.y) : minimum;
            return maximum >= minimum
                ? new Vector2(minimum, maximum)
                : new Vector2(maximum, minimum);
        }

        private static AnimationCurve CreateDefaultCurve()
        {
            return AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 1.0f);
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null)
                return CreateDefaultCurve();

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleRotationOverLifetimeModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private AnimationCurve m_AngularVelocity = AnimationCurve.Constant(0.0f, 1.0f, 0.0f);

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

        public AnimationCurve angularVelocity
        {
            get => m_AngularVelocity ??= AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
            set
            {
                m_AngularVelocity = CloneCurve(value);
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleRotationOverLifetimeModule CreateDefault()
        {
            return new VividParticleRotationOverLifetimeModule();
        }

        internal VividParticleRotationOverLifetimeModule Clone()
        {
            var clone = new VividParticleRotationOverLifetimeModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleRotationOverLifetimeModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_AngularVelocity = CloneCurve(source.m_AngularVelocity);
            Validate();
        }

        internal void Validate()
        {
            m_AngularVelocity ??= AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
        }

        internal float EvaluateAngularVelocity(float normalizedLifetime)
        {
            return angularVelocity.Evaluate(Mathf.Clamp01(normalizedLifetime));
        }

        internal float EvaluateIntegratedRadians(float normalizedLifetime, float startLifetime)
        {
            const int integrationSteps = 32;
            float end = Mathf.Clamp01(normalizedLifetime);
            if (end <= 0.0f || startLifetime <= 0.0f)
                return 0.0f;

            float step = end / integrationSteps;
            float previous = EvaluateAngularVelocity(0.0f) * Mathf.Deg2Rad;
            float integral = 0.0f;
            for (int index = 1; index <= integrationSteps; index++)
            {
                float current = EvaluateAngularVelocity(index * step) * Mathf.Deg2Rad;
                integral += (previous + current) * 0.5f * step;
                previous = current;
            }

            return integral * startLifetime;
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null)
                return AnimationCurve.Constant(0.0f, 1.0f, 0.0f);

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleRotationBySpeedModule
    {
        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private bool m_SeparateAxes;

        [SerializeField]
        private AnimationCurve m_X = CreateZeroCurve();

        [SerializeField]
        private AnimationCurve m_Y = CreateZeroCurve();

        [SerializeField]
        private AnimationCurve m_Z = AnimationCurve.Constant(0.0f, 1.0f, 45.0f);

        [SerializeField]
        private Vector2 m_Range = new(0.0f, 1.0f);

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

        public bool separateAxes
        {
            get => m_SeparateAxes;
            set
            {
                if (m_SeparateAxes == value)
                    return;

                m_SeparateAxes = value;
                NotifyChanged();
            }
        }

        public AnimationCurve x
        {
            get => m_X ??= CreateZeroCurve();
            set
            {
                m_X = CloneCurve(value, CreateZeroCurve());
                NotifyChanged();
            }
        }

        public AnimationCurve y
        {
            get => m_Y ??= CreateZeroCurve();
            set
            {
                m_Y = CloneCurve(value, CreateZeroCurve());
                NotifyChanged();
            }
        }

        public AnimationCurve z
        {
            get => m_Z ??= AnimationCurve.Constant(0.0f, 1.0f, 45.0f);
            set
            {
                m_Z = CloneCurve(value, AnimationCurve.Constant(0.0f, 1.0f, 45.0f));
                NotifyChanged();
            }
        }

        public Vector2 range
        {
            get => m_Range;
            set
            {
                Vector2 normalized = NormalizeRange(value);
                if (m_Range == normalized)
                    return;

                m_Range = normalized;
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleRotationBySpeedModule CreateDefault()
        {
            return new VividParticleRotationBySpeedModule();
        }

        internal VividParticleRotationBySpeedModule Clone()
        {
            var clone = new VividParticleRotationBySpeedModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleRotationBySpeedModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_SeparateAxes = source.m_SeparateAxes;
            m_X = CloneCurve(source.m_X, CreateZeroCurve());
            m_Y = CloneCurve(source.m_Y, CreateZeroCurve());
            m_Z = CloneCurve(source.m_Z, AnimationCurve.Constant(0.0f, 1.0f, 45.0f));
            m_Range = source.m_Range;
            Validate();
        }

        internal void Validate()
        {
            m_X ??= CreateZeroCurve();
            m_Y ??= CreateZeroCurve();
            m_Z ??= AnimationCurve.Constant(0.0f, 1.0f, 45.0f);
            m_Range = NormalizeRange(m_Range);
        }

        internal Vector3 EvaluateAngularVelocity(float speed)
        {
            float denominator = Mathf.Max(0.000001f, m_Range.y - m_Range.x);
            float normalizedSpeed = Mathf.Clamp01((speed - m_Range.x) / denominator);
            return m_SeparateAxes
                ? new Vector3(
                    x.Evaluate(normalizedSpeed),
                    y.Evaluate(normalizedSpeed),
                    z.Evaluate(normalizedSpeed))
                : new Vector3(0.0f, 0.0f, z.Evaluate(normalizedSpeed));
        }

        private static Vector2 NormalizeRange(Vector2 value)
        {
            float minimum = float.IsFinite(value.x) ? Mathf.Max(0.0f, value.x) : 0.0f;
            float maximum = float.IsFinite(value.y) ? Mathf.Max(0.0f, value.y) : minimum;
            return maximum >= minimum
                ? new Vector2(minimum, maximum)
                : new Vector2(maximum, minimum);
        }

        private static AnimationCurve CreateZeroCurve()
        {
            return AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
        }

        private static AnimationCurve CloneCurve(AnimationCurve source, AnimationCurve fallback)
        {
            AnimationCurve curve = source ?? fallback;
            return new AnimationCurve(curve.keys)
            {
                preWrapMode = curve.preWrapMode,
                postWrapMode = curve.postWrapMode,
            };
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }

    [Serializable]
    public sealed class VividParticleNoiseModule
    {
        internal const int MinimumOctaveCount = 1;
        internal const int MaximumOctaveCount = 4;

        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private bool m_SeparateAxes;

        [SerializeField]
        private AnimationCurve m_Strength = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);

        [SerializeField]
        private AnimationCurve m_StrengthX = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);

        [SerializeField]
        private AnimationCurve m_StrengthY = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);

        [SerializeField]
        private AnimationCurve m_StrengthZ = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);

        [SerializeField]
        private float m_Frequency = 0.5f;

        [SerializeField]
        private bool m_Damping = true;

        [SerializeField]
        private int m_OctaveCount = 1;

        [SerializeField]
        private float m_OctaveMultiplier = 0.5f;

        [SerializeField]
        private float m_OctaveScale = 2.0f;

        [SerializeField]
        private AnimationCurve m_ScrollSpeed = AnimationCurve.Constant(0.0f, 1.0f, 0.0f);

        [SerializeField]
        private AnimationCurve m_PositionAmount = AnimationCurve.Constant(0.0f, 1.0f, 1.0f);

        [SerializeField]
        private AnimationCurve m_RotationAmount = AnimationCurve.Constant(0.0f, 1.0f, 0.0f);

        [SerializeField]
        private AnimationCurve m_SizeAmount = AnimationCurve.Constant(0.0f, 1.0f, 0.0f);

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

        public bool separateAxes
        {
            get => m_SeparateAxes;
            set
            {
                if (m_SeparateAxes == value)
                    return;

                m_SeparateAxes = value;
                NotifyChanged();
            }
        }

        public AnimationCurve strength
        {
            get => m_Strength ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            set { m_Strength = CloneCurve(value, 1.0f); NotifyChanged(); }
        }

        public AnimationCurve strengthX
        {
            get => m_StrengthX ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            set { m_StrengthX = CloneCurve(value, 1.0f); NotifyChanged(); }
        }

        public AnimationCurve strengthY
        {
            get => m_StrengthY ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            set { m_StrengthY = CloneCurve(value, 1.0f); NotifyChanged(); }
        }

        public AnimationCurve strengthZ
        {
            get => m_StrengthZ ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            set { m_StrengthZ = CloneCurve(value, 1.0f); NotifyChanged(); }
        }

        public float frequency
        {
            get => m_Frequency;
            set
            {
                float clamped = float.IsFinite(value) ? Mathf.Max(0.0f, value) : 0.0f;
                if (m_Frequency == clamped)
                    return;
                m_Frequency = clamped;
                NotifyChanged();
            }
        }

        public bool damping
        {
            get => m_Damping;
            set { if (m_Damping != value) { m_Damping = value; NotifyChanged(); } }
        }

        public int octaveCount
        {
            get => m_OctaveCount;
            set
            {
                int clamped = Mathf.Clamp(value, MinimumOctaveCount, MaximumOctaveCount);
                if (m_OctaveCount == clamped)
                    return;
                m_OctaveCount = clamped;
                NotifyChanged();
            }
        }

        public float octaveMultiplier
        {
            get => m_OctaveMultiplier;
            set
            {
                float clamped = float.IsFinite(value) ? Mathf.Clamp01(value) : 0.0f;
                if (m_OctaveMultiplier == clamped)
                    return;
                m_OctaveMultiplier = clamped;
                NotifyChanged();
            }
        }

        public float octaveScale
        {
            get => m_OctaveScale;
            set
            {
                float clamped = float.IsFinite(value) ? Mathf.Max(1.0f, value) : 1.0f;
                if (m_OctaveScale == clamped)
                    return;
                m_OctaveScale = clamped;
                NotifyChanged();
            }
        }

        public AnimationCurve scrollSpeed
        {
            get => m_ScrollSpeed ??= AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
            set { m_ScrollSpeed = CloneCurve(value, 0.0f); NotifyChanged(); }
        }

        public AnimationCurve positionAmount
        {
            get => m_PositionAmount ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            set { m_PositionAmount = CloneCurve(value, 1.0f); NotifyChanged(); }
        }

        public AnimationCurve rotationAmount
        {
            get => m_RotationAmount ??= AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
            set { m_RotationAmount = CloneCurve(value, 0.0f); NotifyChanged(); }
        }

        public AnimationCurve sizeAmount
        {
            get => m_SizeAmount ??= AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
            set { m_SizeAmount = CloneCurve(value, 0.0f); NotifyChanged(); }
        }

        internal void SetChangeCallback(Action onChanged) => m_OnChanged = onChanged;

        internal static VividParticleNoiseModule CreateDefault() => new();

        internal VividParticleNoiseModule Clone()
        {
            var clone = new VividParticleNoiseModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleNoiseModule source)
        {
            if (source == null)
                return;
            m_Enabled = source.m_Enabled;
            m_SeparateAxes = source.m_SeparateAxes;
            m_Strength = CloneCurve(source.m_Strength, 1.0f);
            m_StrengthX = CloneCurve(source.m_StrengthX, 1.0f);
            m_StrengthY = CloneCurve(source.m_StrengthY, 1.0f);
            m_StrengthZ = CloneCurve(source.m_StrengthZ, 1.0f);
            m_Frequency = source.m_Frequency;
            m_Damping = source.m_Damping;
            m_OctaveCount = source.m_OctaveCount;
            m_OctaveMultiplier = source.m_OctaveMultiplier;
            m_OctaveScale = source.m_OctaveScale;
            m_ScrollSpeed = CloneCurve(source.m_ScrollSpeed, 0.0f);
            m_PositionAmount = CloneCurve(source.m_PositionAmount, 1.0f);
            m_RotationAmount = CloneCurve(source.m_RotationAmount, 0.0f);
            m_SizeAmount = CloneCurve(source.m_SizeAmount, 0.0f);
            Validate();
        }

        internal void Validate()
        {
            m_Strength ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            m_StrengthX ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            m_StrengthY ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            m_StrengthZ ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            m_ScrollSpeed ??= AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
            m_PositionAmount ??= AnimationCurve.Constant(0.0f, 1.0f, 1.0f);
            m_RotationAmount ??= AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
            m_SizeAmount ??= AnimationCurve.Constant(0.0f, 1.0f, 0.0f);
            m_Frequency = float.IsFinite(m_Frequency) ? Mathf.Max(0.0f, m_Frequency) : 0.0f;
            m_OctaveCount = Mathf.Clamp(m_OctaveCount, MinimumOctaveCount, MaximumOctaveCount);
            m_OctaveMultiplier = float.IsFinite(m_OctaveMultiplier) ? Mathf.Clamp01(m_OctaveMultiplier) : 0.0f;
            m_OctaveScale = float.IsFinite(m_OctaveScale) ? Mathf.Max(1.0f, m_OctaveScale) : 1.0f;
        }

        internal Vector3 EvaluateStrength(float normalizedLifetime)
        {
            float time = Mathf.Clamp01(normalizedLifetime);
            if (!m_SeparateAxes)
            {
                float value = strength.Evaluate(time);
                return Vector3.one * value;
            }
            return new Vector3(
                strengthX.Evaluate(time),
                strengthY.Evaluate(time),
                strengthZ.Evaluate(time));
        }

        internal float EvaluateScrollSpeed(float normalizedLifetime)
        {
            return scrollSpeed.Evaluate(Mathf.Clamp01(normalizedLifetime));
        }

        internal float EvaluatePositionAmount(float normalizedLifetime)
        {
            return positionAmount.Evaluate(Mathf.Clamp01(normalizedLifetime));
        }

        internal float EvaluateRotationAmount(float normalizedLifetime)
        {
            return rotationAmount.Evaluate(Mathf.Clamp01(normalizedLifetime));
        }

        internal float EvaluateSizeAmount(float normalizedLifetime)
        {
            return sizeAmount.Evaluate(Mathf.Clamp01(normalizedLifetime));
        }

        internal bool hasPositionEffect => HasNonZeroSample(positionAmount);

        internal bool hasRotationEffect => HasNonZeroSample(rotationAmount);

        internal bool hasSizeEffect => HasNonZeroSample(sizeAmount);

        private static bool HasNonZeroSample(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
                return false;

            const int sampleCount = 16;
            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)(sampleCount - 1);
                if (Mathf.Abs(curve.Evaluate(time)) > 0.000001f)
                    return true;
            }

            return false;
        }

        private static AnimationCurve CloneCurve(AnimationCurve source, float defaultValue)
        {
            AnimationCurve curve = source ?? AnimationCurve.Constant(0.0f, 1.0f, defaultValue);
            return new AnimationCurve(curve.keys)
            {
                preWrapMode = curve.preWrapMode,
                postWrapMode = curve.postWrapMode,
            };
        }

        private void NotifyChanged() => m_OnChanged?.Invoke();
    }

    [Serializable]
    public sealed class VividParticleTextureSheetAnimationModule
    {
        internal const int MinimumTileCount = 1;
        internal const int MaximumTileCount = 64;
        internal const float MinimumCycleCount = 0.0f;

        [NonSerialized]
        private Action m_OnChanged;

        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private int m_NumTilesX = 1;

        [SerializeField]
        private int m_NumTilesY = 1;

        [SerializeField]
        private VividParticleTextureSheetAnimationType m_Animation =
            VividParticleTextureSheetAnimationType.WholeSheet;

        [SerializeField]
        private AnimationCurve m_FrameOverTime = CreateDefaultCurve();

        [SerializeField]
        private float m_StartFrame;

        [SerializeField]
        private float m_CycleCount = 1.0f;

        [SerializeField]
        private int m_RowIndex;

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

        public int numTilesX
        {
            get => m_NumTilesX;
            set
            {
                int clamped = Mathf.Clamp(value, MinimumTileCount, MaximumTileCount);
                if (m_NumTilesX == clamped)
                    return;

                m_NumTilesX = clamped;
                m_RowIndex = Mathf.Clamp(m_RowIndex, 0, Mathf.Max(0, m_NumTilesY - 1));
                NotifyChanged();
            }
        }

        public int numTilesY
        {
            get => m_NumTilesY;
            set
            {
                int clamped = Mathf.Clamp(value, MinimumTileCount, MaximumTileCount);
                if (m_NumTilesY == clamped)
                    return;

                m_NumTilesY = clamped;
                m_RowIndex = Mathf.Clamp(m_RowIndex, 0, m_NumTilesY - 1);
                NotifyChanged();
            }
        }

        public VividParticleTextureSheetAnimationType animation
        {
            get => m_Animation;
            set
            {
                if (m_Animation == value)
                    return;

                m_Animation = value;
                NotifyChanged();
            }
        }

        public AnimationCurve frameOverTime
        {
            get => m_FrameOverTime ??= CreateDefaultCurve();
            set
            {
                m_FrameOverTime = CloneCurve(value);
                NotifyChanged();
            }
        }

        public float startFrame
        {
            get => m_StartFrame;
            set
            {
                if (m_StartFrame == value)
                    return;

                m_StartFrame = value;
                NotifyChanged();
            }
        }

        public float cycleCount
        {
            get => m_CycleCount;
            set
            {
                float clamped = Mathf.Max(MinimumCycleCount, value);
                if (m_CycleCount == clamped)
                    return;

                m_CycleCount = clamped;
                NotifyChanged();
            }
        }

        public int rowIndex
        {
            get => m_RowIndex;
            set
            {
                int clamped = Mathf.Clamp(value, 0, Mathf.Max(0, m_NumTilesY - 1));
                if (m_RowIndex == clamped)
                    return;

                m_RowIndex = clamped;
                NotifyChanged();
            }
        }

        internal void SetChangeCallback(Action onChanged)
        {
            m_OnChanged = onChanged;
        }

        internal static VividParticleTextureSheetAnimationModule CreateDefault()
        {
            return new VividParticleTextureSheetAnimationModule();
        }

        internal VividParticleTextureSheetAnimationModule Clone()
        {
            var clone = new VividParticleTextureSheetAnimationModule();
            clone.CopyFrom(this);
            return clone;
        }

        internal void CopyFrom(VividParticleTextureSheetAnimationModule source)
        {
            if (source == null)
                return;

            m_Enabled = source.m_Enabled;
            m_NumTilesX = source.m_NumTilesX;
            m_NumTilesY = source.m_NumTilesY;
            m_Animation = source.m_Animation;
            m_FrameOverTime = CloneCurve(source.m_FrameOverTime);
            m_StartFrame = source.m_StartFrame;
            m_CycleCount = source.m_CycleCount;
            m_RowIndex = source.m_RowIndex;
            Validate();
        }

        internal void Validate()
        {
            m_NumTilesX = Mathf.Clamp(m_NumTilesX, MinimumTileCount, MaximumTileCount);
            m_NumTilesY = Mathf.Clamp(m_NumTilesY, MinimumTileCount, MaximumTileCount);
            m_CycleCount = Mathf.Max(MinimumCycleCount, m_CycleCount);
            m_RowIndex = Mathf.Clamp(m_RowIndex, 0, m_NumTilesY - 1);
            m_FrameOverTime ??= CreateDefaultCurve();
        }

        internal float EvaluateFrame(float normalizedLifetime)
        {
            return frameOverTime.Evaluate(Mathf.Clamp01(normalizedLifetime));
        }

        private static AnimationCurve CreateDefaultCurve()
        {
            return AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null)
                return CreateDefaultCurve();

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
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
        internal const int MinimumBatchLayer = 0;
        internal const int MaximumBatchLayer = 31;

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
        private int m_SortingPriority;

        [SerializeField]
        private int m_BatchLayer;

        [SerializeField]
        private ShadowCastingMode m_ShadowCastingMode = ShadowCastingMode.Off;

        [SerializeField]
        private MotionVectorGenerationMode m_MotionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        [SerializeField]
        private bool m_StaticShadowCaster;

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

        public int sortingPriority
        {
            get => m_SortingPriority;
            set
            {
                if (m_SortingPriority == value)
                    return;

                m_SortingPriority = value;
                NotifyChanged();
            }
        }

        public int batchLayer
        {
            get => m_BatchLayer;
            set
            {
                int clamped = Mathf.Clamp(value, MinimumBatchLayer, MaximumBatchLayer);
                if (m_BatchLayer == clamped)
                    return;

                m_BatchLayer = clamped;
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

        public MotionVectorGenerationMode motionVectorGenerationMode
        {
            get => m_MotionVectorGenerationMode;
            set
            {
                MotionVectorGenerationMode resolved = ValidateMotionVectorGenerationMode(value);
                if (m_MotionVectorGenerationMode == resolved)
                    return;

                m_MotionVectorGenerationMode = resolved;
                NotifyChanged();
            }
        }

        public bool staticShadowCaster
        {
            get => m_StaticShadowCaster;
            set
            {
                if (m_StaticShadowCaster == value)
                    return;

                m_StaticShadowCaster = value;
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
            m_SortingPriority = source.m_SortingPriority;
            m_BatchLayer = source.m_BatchLayer;
            m_ShadowCastingMode = source.m_ShadowCastingMode;
            m_MotionVectorGenerationMode = ValidateMotionVectorGenerationMode(source.m_MotionVectorGenerationMode);
            m_StaticShadowCaster = source.m_StaticShadowCaster;
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
            m_BatchLayer = Mathf.Clamp(m_BatchLayer, MinimumBatchLayer, MaximumBatchLayer);
            m_Meshes ??= Array.Empty<Mesh>();
            m_MotionVectorGenerationMode = ValidateMotionVectorGenerationMode(m_MotionVectorGenerationMode);
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
            return mesh != null ? RuntimeHelpers.GetHashCode(mesh) : 0;
        }

        private static MotionVectorGenerationMode ValidateMotionVectorGenerationMode(MotionVectorGenerationMode mode)
        {
            return mode switch
            {
                MotionVectorGenerationMode.Camera => mode,
                MotionVectorGenerationMode.Object => mode,
                MotionVectorGenerationMode.ForceNoMotion => mode,
                _ => MotionVectorGenerationMode.ForceNoMotion,
            };
        }

        private void NotifyChanged()
        {
            m_OnChanged?.Invoke();
        }
    }
}
