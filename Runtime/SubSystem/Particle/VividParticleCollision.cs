using System;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime.Particle.ECS;

namespace VividRP.Runtime.Particle
{
    public enum VividParticleCollisionType
    {
        Planes,
        World,
    }

    public enum VividParticleCollisionMode
    {
        Collision3D,
        Collision2D,
    }

    public enum VividParticleCollisionQuality
    {
        High,
        Medium,
        Low,
    }

    public readonly struct VividParticleCollisionEvent
    {
        internal VividParticleCollisionEvent(
            int particleIndex,
            Component colliderComponent,
            Vector3 intersection,
            Vector3 normal,
            Vector3 velocity)
        {
            this.particleIndex = particleIndex;
            this.colliderComponent = colliderComponent;
            this.intersection = intersection;
            this.normal = normal;
            this.velocity = velocity;
        }

        public int particleIndex { get; }
        public Component colliderComponent { get; }
        public Vector3 intersection { get; }
        public Vector3 normal { get; }
        public Vector3 velocity { get; }
    }

    [Serializable]
    public sealed class VividParticleCollisionModule
    {
        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private VividParticleCollisionType m_Type;

        [SerializeField]
        private VividParticleCollisionMode m_Mode;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_Dampen;

        [SerializeField, Min(0.0f)]
        private float m_Bounce = 1.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_LifetimeLoss;

        [SerializeField, Min(0.0f)]
        private float m_MinKillSpeed;

        [SerializeField, Min(0.0f)]
        private float m_MaxKillSpeed = 10000.0f;

        [SerializeField, Min(0.0f)]
        private float m_RadiusScale = 1.0f;

        [SerializeField]
        private VividParticleCollisionQuality m_Quality;

        [SerializeField]
        private LayerMask m_CollidesWith = ~0;

        [SerializeField, Min(1)]
        private int m_MaxCollisionShapes = 256;

        [SerializeField]
        private bool m_EnableDynamicColliders;

        [SerializeField]
        private bool m_SendCollisionMessages;

        [SerializeField]
        private Transform[] m_Planes = Array.Empty<Transform>();

        [NonSerialized]
        private Action m_OnChanged;

        public bool enabled { get => m_Enabled; set { m_Enabled = value; NotifyChanged(); } }
        public VividParticleCollisionType type { get => m_Type; set { m_Type = value; NotifyChanged(); } }
        public VividParticleCollisionMode mode { get => m_Mode; set { m_Mode = value; NotifyChanged(); } }
        public float dampen { get => m_Dampen; set { m_Dampen = Mathf.Clamp01(value); NotifyChanged(); } }
        public float bounce { get => m_Bounce; set { m_Bounce = Mathf.Max(0.0f, value); NotifyChanged(); } }
        public float lifetimeLoss { get => m_LifetimeLoss; set { m_LifetimeLoss = Mathf.Clamp01(value); NotifyChanged(); } }
        public float minKillSpeed { get => m_MinKillSpeed; set { m_MinKillSpeed = Mathf.Max(0.0f, value); ValidateKillSpeeds(); NotifyChanged(); } }
        public float maxKillSpeed { get => m_MaxKillSpeed; set { m_MaxKillSpeed = Mathf.Max(0.0f, value); ValidateKillSpeeds(); NotifyChanged(); } }
        public float radiusScale { get => m_RadiusScale; set { m_RadiusScale = Mathf.Max(0.0f, value); NotifyChanged(); } }
        public VividParticleCollisionQuality quality { get => m_Quality; set { m_Quality = value; NotifyChanged(); } }
        public LayerMask collidesWith { get => m_CollidesWith; set { m_CollidesWith = value; NotifyChanged(); } }
        public int maxCollisionShapes { get => m_MaxCollisionShapes; set { m_MaxCollisionShapes = Mathf.Max(1, value); NotifyChanged(); } }
        public bool enableDynamicColliders { get => m_EnableDynamicColliders; set { m_EnableDynamicColliders = value; NotifyChanged(); } }
        public bool sendCollisionMessages { get => m_SendCollisionMessages; set { m_SendCollisionMessages = value; NotifyChanged(); } }
        public int planeCount => m_Planes?.Length ?? 0;

        public Transform GetPlane(int index)
        {
            if ((uint)index >= (uint)planeCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return m_Planes[index];
        }

        public Transform[] GetPlanes()
        {
            return m_Planes != null ? (Transform[])m_Planes.Clone() : Array.Empty<Transform>();
        }

        public void AddPlane(Transform plane)
        {
            int count = planeCount;
            Array.Resize(ref m_Planes, count + 1);
            m_Planes[count] = plane;
            NotifyChanged();
        }

        public void SetPlane(int index, Transform plane)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (index >= planeCount)
                Array.Resize(ref m_Planes, index + 1);
            m_Planes[index] = plane;
            NotifyChanged();
        }

        public void RemovePlane(int index)
        {
            int count = planeCount;
            if ((uint)index >= (uint)count)
                throw new ArgumentOutOfRangeException(nameof(index));
            for (int source = index + 1; source < count; source++)
                m_Planes[source - 1] = m_Planes[source];
            Array.Resize(ref m_Planes, count - 1);
            NotifyChanged();
        }

        public void RemovePlane(Transform plane)
        {
            if (m_Planes == null)
                return;
            int index = Array.IndexOf(m_Planes, plane);
            if (index >= 0)
                RemovePlane(index);
        }

        public void RemoveAllPlanes()
        {
            if (planeCount == 0)
                return;
            m_Planes = Array.Empty<Transform>();
            NotifyChanged();
        }

        internal static VividParticleCollisionModule CreateDefault() => new();

        internal void CopyFrom(VividParticleCollisionModule source)
        {
            if (source == null)
                return;
            m_Enabled = source.m_Enabled;
            m_Type = source.m_Type;
            m_Mode = source.m_Mode;
            m_Dampen = source.m_Dampen;
            m_Bounce = source.m_Bounce;
            m_LifetimeLoss = source.m_LifetimeLoss;
            m_MinKillSpeed = source.m_MinKillSpeed;
            m_MaxKillSpeed = source.m_MaxKillSpeed;
            m_RadiusScale = source.m_RadiusScale;
            m_Quality = source.m_Quality;
            m_CollidesWith = source.m_CollidesWith;
            m_MaxCollisionShapes = source.m_MaxCollisionShapes;
            m_EnableDynamicColliders = source.m_EnableDynamicColliders;
            m_SendCollisionMessages = source.m_SendCollisionMessages;
            m_Planes = source.m_Planes != null
                ? (Transform[])source.m_Planes.Clone()
                : Array.Empty<Transform>();
            Validate();
        }

        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(VividParticleCollisionType), m_Type))
                m_Type = VividParticleCollisionType.Planes;
            if (!Enum.IsDefined(typeof(VividParticleCollisionMode), m_Mode))
                m_Mode = VividParticleCollisionMode.Collision3D;
            if (!Enum.IsDefined(typeof(VividParticleCollisionQuality), m_Quality))
                m_Quality = VividParticleCollisionQuality.High;
            m_Dampen = Mathf.Clamp01(m_Dampen);
            m_Bounce = Mathf.Max(0.0f, m_Bounce);
            m_LifetimeLoss = Mathf.Clamp01(m_LifetimeLoss);
            m_MinKillSpeed = Mathf.Max(0.0f, m_MinKillSpeed);
            m_MaxKillSpeed = Mathf.Max(0.0f, m_MaxKillSpeed);
            ValidateKillSpeeds();
            m_RadiusScale = Mathf.Max(0.0f, m_RadiusScale);
            m_MaxCollisionShapes = Mathf.Max(1, m_MaxCollisionShapes);
            m_Planes ??= Array.Empty<Transform>();
        }

        internal void SetChangeCallback(Action onChanged) => m_OnChanged = onChanged;

        private void ValidateKillSpeeds()
        {
            if (m_MaxKillSpeed < m_MinKillSpeed)
                m_MaxKillSpeed = m_MinKillSpeed;
        }

        private void NotifyChanged() => m_OnChanged?.Invoke();
    }

    internal struct VividParticleNativeCollisionPlane
    {
        public ulong EntityId;
        public float3 Position;
        public float3 Normal;
    }

    internal struct VividParticleNativeCollisionEvent
    {
        public ulong ColliderEntityId;
        public int ParticleIndex;
        public float3 Intersection;
        public float3 Normal;
        public float3 Velocity;
        public int IsUserEvent;
        public VividParticleSubEmitterParticleData Particle;
    }

    public enum VividParticleOverlapAction
    {
        Ignore,
        Kill,
        Callback,
    }

    public enum VividParticleColliderQueryMode
    {
        Disabled,
        One,
        All,
    }

    public enum VividParticleTriggerEventType
    {
        Inside,
        Outside,
        Enter,
        Exit,
    }

    public readonly struct VividParticleTriggerEvent
    {
        internal VividParticleTriggerEvent(
            int particleIndex,
            Collider collider,
            VividParticleTriggerEventType eventType)
        {
            this.particleIndex = particleIndex;
            this.collider = collider;
            this.eventType = eventType;
        }

        public int particleIndex { get; }
        public Collider collider { get; }
        public VividParticleTriggerEventType eventType { get; }
    }

    [Serializable]
    public sealed class VividParticleTriggerModule
    {
        [SerializeField]
        private bool m_Enabled;

        [SerializeField]
        private VividParticleOverlapAction m_Inside = VividParticleOverlapAction.Ignore;

        [SerializeField]
        private VividParticleOverlapAction m_Outside = VividParticleOverlapAction.Ignore;

        [SerializeField]
        private VividParticleOverlapAction m_Enter = VividParticleOverlapAction.Ignore;

        [SerializeField]
        private VividParticleOverlapAction m_Exit = VividParticleOverlapAction.Ignore;

        [SerializeField]
        private VividParticleColliderQueryMode m_ColliderQueryMode = VividParticleColliderQueryMode.One;

        [SerializeField, Min(0.0f)]
        private float m_RadiusScale = 1.0f;

        [SerializeField]
        private bool m_VisualizeBounds;

        [SerializeField]
        private Component[] m_Colliders = Array.Empty<Component>();

        [NonSerialized]
        private Action m_OnChanged;

        public bool enabled { get => m_Enabled; set { m_Enabled = value; NotifyChanged(); } }
        public VividParticleOverlapAction inside { get => m_Inside; set { m_Inside = value; NotifyChanged(); } }
        public VividParticleOverlapAction outside { get => m_Outside; set { m_Outside = value; NotifyChanged(); } }
        public VividParticleOverlapAction enter { get => m_Enter; set { m_Enter = value; NotifyChanged(); } }
        public VividParticleOverlapAction exit { get => m_Exit; set { m_Exit = value; NotifyChanged(); } }
        public VividParticleColliderQueryMode colliderQueryMode { get => m_ColliderQueryMode; set { m_ColliderQueryMode = value; NotifyChanged(); } }
        public float radiusScale { get => m_RadiusScale; set { m_RadiusScale = Mathf.Max(0.0f, value); NotifyChanged(); } }
        public bool visualizeBounds { get => m_VisualizeBounds; set { m_VisualizeBounds = value; NotifyChanged(); } }
        public int colliderCount => m_Colliders?.Length ?? 0;

        public Component GetCollider(int index)
        {
            if ((uint)index >= (uint)colliderCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return m_Colliders[index];
        }

        public Component[] GetColliders()
        {
            return m_Colliders != null ? (Component[])m_Colliders.Clone() : Array.Empty<Component>();
        }

        public void AddCollider(Component collider)
        {
            int count = colliderCount;
            Array.Resize(ref m_Colliders, count + 1);
            m_Colliders[count] = collider;
            NotifyChanged();
        }

        public void SetCollider(int index, Component collider)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (index >= colliderCount)
                Array.Resize(ref m_Colliders, index + 1);
            m_Colliders[index] = collider;
            NotifyChanged();
        }

        public void RemoveCollider(int index)
        {
            int count = colliderCount;
            if ((uint)index >= (uint)count)
                throw new ArgumentOutOfRangeException(nameof(index));
            for (int source = index + 1; source < count; source++)
                m_Colliders[source - 1] = m_Colliders[source];
            Array.Resize(ref m_Colliders, count - 1);
            NotifyChanged();
        }

        public void RemoveCollider(Component collider)
        {
            if (m_Colliders == null)
                return;
            int index = Array.IndexOf(m_Colliders, collider);
            if (index >= 0)
                RemoveCollider(index);
        }

        public void RemoveAllColliders()
        {
            if (colliderCount == 0)
                return;
            m_Colliders = Array.Empty<Component>();
            NotifyChanged();
        }

        internal static VividParticleTriggerModule CreateDefault() => new();

        internal void CopyFrom(VividParticleTriggerModule source)
        {
            if (source == null)
                return;
            m_Enabled = source.m_Enabled;
            m_Inside = source.m_Inside;
            m_Outside = source.m_Outside;
            m_Enter = source.m_Enter;
            m_Exit = source.m_Exit;
            m_ColliderQueryMode = source.m_ColliderQueryMode;
            m_RadiusScale = source.m_RadiusScale;
            m_VisualizeBounds = source.m_VisualizeBounds;
            m_Colliders = source.m_Colliders != null
                ? (Component[])source.m_Colliders.Clone()
                : Array.Empty<Component>();
            Validate();
        }

        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(VividParticleOverlapAction), m_Inside))
                m_Inside = VividParticleOverlapAction.Ignore;
            if (!Enum.IsDefined(typeof(VividParticleOverlapAction), m_Outside))
                m_Outside = VividParticleOverlapAction.Ignore;
            if (!Enum.IsDefined(typeof(VividParticleOverlapAction), m_Enter))
                m_Enter = VividParticleOverlapAction.Ignore;
            if (!Enum.IsDefined(typeof(VividParticleOverlapAction), m_Exit))
                m_Exit = VividParticleOverlapAction.Ignore;
            if (!Enum.IsDefined(typeof(VividParticleColliderQueryMode), m_ColliderQueryMode))
                m_ColliderQueryMode = VividParticleColliderQueryMode.One;
            m_RadiusScale = Mathf.Max(0.0f, m_RadiusScale);
            m_Colliders ??= Array.Empty<Component>();
        }

        internal void SetChangeCallback(Action onChanged) => m_OnChanged = onChanged;
        private void NotifyChanged() => m_OnChanged?.Invoke();
    }

    internal struct VividParticleNativeTriggerEvent
    {
        public ulong ColliderEntityId;
        public int ParticleIndex;
        public int EventType;
        public int IsCallback;
        public VividParticleSubEmitterParticleData Particle;
    }
}
