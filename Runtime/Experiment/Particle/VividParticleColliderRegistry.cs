using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime.GPUDriven.ObjectDispatching;
using Object = UnityEngine.Object;

namespace VividRP.Runtime.Particle
{
    internal enum VividParticleNativeColliderShape
    {
        Sphere,
        Box,
        Capsule,
    }

    internal struct VividParticleNativeCollider
    {
        public ulong EntityId;
        public int Layer;
        public int Shape;
        public int IsTrigger;
        public int IsDynamic;
        public int Active;
        public float3 Center;
        public float3 AxisX;
        public float3 AxisY;
        public float3 AxisZ;
        public float3 HalfExtents;
        public float3 SegmentA;
        public float3 SegmentB;
        public float Radius;
    }

    internal static unsafe class VividParticleColliderRegistry
    {
        private static readonly List<Collider> s_Colliders = new();
        private static readonly Dictionary<EntityId, int> s_ColliderIndices = new();
        private static readonly Dictionary<EntityId, int> s_NativeColliderIndices = new();
        private static readonly List<ColliderSnapshot> s_Snapshots = new();
        private static ColliderObjectTracker s_ObjectTracker;
        private static NativeList<VividParticleNativeCollider> s_NativeColliders;
        private static bool s_Initialized;
        private static bool s_Dirty = true;
        private static int s_Version;
        private static int s_DiscoveryCount;
        private static int s_UnsupportedColliderCount;

        public static int count => s_NativeColliders.IsCreated ? s_NativeColliders.Length : 0;
        public static int version => s_Version;
        public static int discoveryCount => s_DiscoveryCount;
        public static int unsupportedColliderCount => s_UnsupportedColliderCount;
        public static VividParticleNativeCollider* colliders => s_NativeColliders.IsCreated
            ? (VividParticleNativeCollider*)s_NativeColliders.GetUnsafeReadOnlyPtr()
            : null;

        public static bool Prepare()
        {
            EnsureInitialized();
            DetectRuntimeChanges();
            if (!s_Dirty)
                return false;

            if (!s_NativeColliders.IsCreated)
            {
                s_NativeColliders = new NativeList<VividParticleNativeCollider>(
                    math.max(16, s_Colliders.Count),
                    Allocator.Persistent);
            }

            s_NativeColliders.Clear();
            s_NativeColliderIndices.Clear();
            s_UnsupportedColliderCount = 0;
            for (int index = 0; index < s_Colliders.Count; index++)
            {
                Collider collider = s_Colliders[index];
                if (collider == null)
                    continue;
                if (TryCreateNative(collider, out VividParticleNativeCollider nativeCollider))
                {
                    s_NativeColliderIndices[collider.GetEntityId()] = s_NativeColliders.Length;
                    s_NativeColliders.Add(nativeCollider);
                }
                else
                    s_UnsupportedColliderCount++;
            }

            s_Dirty = false;
            s_Version++;
            return true;
        }

        public static void MarkDirty() => s_Dirty = true;

        public static bool TryGetCollider(ulong entityId, out Collider collider)
        {
            EntityId id = EntityId.FromULong(entityId);
            if (s_ColliderIndices.TryGetValue(id, out int index)
                && (uint)index < (uint)s_Colliders.Count)
            {
                collider = s_Colliders[index];
                return collider != null;
            }
            collider = null;
            return false;
        }

        public static void ResolveTriggerColliders(
            VividParticleTriggerModule module,
            NativeList<int> destination)
        {
            destination.Clear();
            if (module == null)
                return;
            for (int index = 0; index < module.colliderCount; index++)
            {
                if (module.GetCollider(index) is not Collider collider)
                    continue;
                if (s_NativeColliderIndices.TryGetValue(collider.GetEntityId(), out int nativeIndex))
                    destination.Add(nativeIndex);
            }
        }

        public static void ClearForTests()
        {
            if (s_ObjectTracker != null)
                ObjectDispatcherService.UnregisterObjectTracker(s_ObjectTracker);
            s_ObjectTracker = null;
            s_Colliders.Clear();
            s_ColliderIndices.Clear();
            s_NativeColliderIndices.Clear();
            s_Snapshots.Clear();
            if (s_NativeColliders.IsCreated)
                s_NativeColliders.Dispose();
            s_NativeColliders = default;
            s_Initialized = false;
            s_Dirty = true;
            s_Version = 0;
            s_DiscoveryCount = 0;
            s_UnsupportedColliderCount = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ClearForTests();

        private static void EnsureInitialized()
        {
            if (s_Initialized)
                return;
            s_Initialized = true;
            s_ObjectTracker = new ColliderObjectTracker();
            RebuildTrackedColliders();
            ObjectDispatcherService.RegisterObjectTracker(s_ObjectTracker);
        }

        private static void RebuildTrackedColliders()
        {
            s_Colliders.Clear();
            s_ColliderIndices.Clear();
            s_Snapshots.Clear();
            Collider[] colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include);
            s_DiscoveryCount++;
            for (int index = 0; index < colliders.Length; index++)
                AddOrUpdate(colliders[index]);
            s_Dirty = true;
        }

        private static void ProcessData(
            List<Object> changed,
            NativeArray<EntityId> changedIds,
            NativeArray<EntityId> destroyedIds)
        {
            for (int index = 0; index < destroyedIds.Length; index++)
                Remove(destroyedIds[index]);
            for (int index = 0; index < changed.Count; index++)
            {
                if (changed[index] is Collider collider)
                    AddOrUpdate(collider);
            }
        }

        private static void AddOrUpdate(Collider collider)
        {
            if (collider == null || collider.transform == null)
                return;
            EntityId entityId = collider.GetEntityId();
            if (entityId.Equals(EntityId.None))
                return;
            ColliderSnapshot snapshot = ColliderSnapshot.Capture(collider);
            if (s_ColliderIndices.TryGetValue(entityId, out int existingIndex))
            {
                s_Colliders[existingIndex] = collider;
                if (!s_Snapshots[existingIndex].Equals(snapshot))
                {
                    s_Snapshots[existingIndex] = snapshot;
                    s_Dirty = true;
                }
                return;
            }

            s_ColliderIndices.Add(entityId, s_Colliders.Count);
            s_Colliders.Add(collider);
            s_Snapshots.Add(snapshot);
            s_Dirty = true;
        }

        private static void Remove(EntityId entityId)
        {
            if (entityId.Equals(EntityId.None)
                || !s_ColliderIndices.TryGetValue(entityId, out int index))
                return;
            int lastIndex = s_Colliders.Count - 1;
            Collider lastCollider = s_Colliders[lastIndex];
            s_Colliders[index] = lastCollider;
            s_Snapshots[index] = s_Snapshots[lastIndex];
            s_Colliders.RemoveAt(lastIndex);
            s_Snapshots.RemoveAt(lastIndex);
            s_ColliderIndices.Remove(entityId);
            if (index != lastIndex && lastCollider != null)
                s_ColliderIndices[lastCollider.GetEntityId()] = index;
            s_Dirty = true;
        }

        private static void DetectRuntimeChanges()
        {
            for (int index = s_Colliders.Count - 1; index >= 0; index--)
            {
                Collider collider = s_Colliders[index];
                if (collider == null)
                {
                    RebuildTrackedColliders();
                    return;
                }
                ColliderSnapshot snapshot = ColliderSnapshot.Capture(collider);
                if (!s_Snapshots[index].Equals(snapshot))
                {
                    s_Snapshots[index] = snapshot;
                    s_Dirty = true;
                }
            }
        }

        private static bool TryCreateNative(
            Collider collider,
            out VividParticleNativeCollider nativeCollider)
        {
            nativeCollider = default;
            Transform transform = collider.transform;
            Vector3 scale = Abs(transform.lossyScale);
            Quaternion rotation = transform.rotation;
            Vector3 axisX = rotation * Vector3.right;
            Vector3 axisY = rotation * Vector3.up;
            Vector3 axisZ = rotation * Vector3.forward;
            nativeCollider.EntityId = EntityId.ToULong(collider.GetEntityId());
            nativeCollider.Layer = collider.gameObject.layer;
            nativeCollider.IsTrigger = collider.isTrigger ? 1 : 0;
            nativeCollider.IsDynamic = collider.attachedRigidbody != null
                && !collider.attachedRigidbody.isKinematic
                    ? 1
                    : 0;
            nativeCollider.Active = collider.enabled && collider.gameObject.activeInHierarchy ? 1 : 0;
            nativeCollider.AxisX = ToFloat3(axisX.normalized);
            nativeCollider.AxisY = ToFloat3(axisY.normalized);
            nativeCollider.AxisZ = ToFloat3(axisZ.normalized);

            switch (collider)
            {
                case SphereCollider sphere:
                    nativeCollider.Shape = (int)VividParticleNativeColliderShape.Sphere;
                    nativeCollider.Center = ToFloat3(transform.TransformPoint(sphere.center));
                    nativeCollider.Radius = math.max(0.0f, sphere.radius * MaxComponent(scale));
                    return true;

                case BoxCollider box:
                    nativeCollider.Shape = (int)VividParticleNativeColliderShape.Box;
                    nativeCollider.Center = ToFloat3(transform.TransformPoint(box.center));
                    nativeCollider.HalfExtents = ToFloat3(Vector3.Scale(box.size * 0.5f, scale));
                    return true;

                case CapsuleCollider capsule:
                    nativeCollider.Shape = (int)VividParticleNativeColliderShape.Capsule;
                    nativeCollider.Center = ToFloat3(transform.TransformPoint(capsule.center));
                    ResolveCapsuleAxes(
                        capsule.direction,
                        scale,
                        nativeCollider.AxisX,
                        nativeCollider.AxisY,
                        nativeCollider.AxisZ,
                        out float3 capsuleAxis,
                        out float axisScale,
                        out float radialScale);
                    nativeCollider.Radius = math.max(0.0f, capsule.radius * radialScale);
                    float halfSegment = math.max(
                        0.0f,
                        capsule.height * axisScale * 0.5f - nativeCollider.Radius);
                    nativeCollider.SegmentA = nativeCollider.Center - capsuleAxis * halfSegment;
                    nativeCollider.SegmentB = nativeCollider.Center + capsuleAxis * halfSegment;
                    return true;

                default:
                    return false;
            }
        }

        private static void ResolveCapsuleAxes(
            int direction,
            Vector3 scale,
            float3 axisX,
            float3 axisY,
            float3 axisZ,
            out float3 axis,
            out float axisScale,
            out float radialScale)
        {
            switch (direction)
            {
                case 0:
                    axis = axisX;
                    axisScale = scale.x;
                    radialScale = math.max(scale.y, scale.z);
                    break;
                case 2:
                    axis = axisZ;
                    axisScale = scale.z;
                    radialScale = math.max(scale.x, scale.y);
                    break;
                default:
                    axis = axisY;
                    axisScale = scale.y;
                    radialScale = math.max(scale.x, scale.z);
                    break;
            }
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static float MaxComponent(Vector3 value) => Mathf.Max(value.x, value.y, value.z);
        private static float3 ToFloat3(Vector3 value) => new(value.x, value.y, value.z);

        private readonly struct ColliderSnapshot : IEquatable<ColliderSnapshot>
        {
            private readonly Matrix4x4 m_LocalToWorld;
            private readonly Vector3 m_Center;
            private readonly Vector3 m_Size;
            private readonly float m_Radius;
            private readonly float m_Height;
            private readonly int m_Direction;
            private readonly int m_Layer;
            private readonly int m_Shape;
            private readonly bool m_Enabled;
            private readonly bool m_Active;
            private readonly bool m_IsTrigger;
            private readonly bool m_IsDynamic;

            private ColliderSnapshot(Collider collider)
            {
                m_LocalToWorld = collider.transform.localToWorldMatrix;
                m_Center = Vector3.zero;
                m_Size = Vector3.zero;
                m_Radius = 0.0f;
                m_Height = 0.0f;
                m_Direction = 0;
                m_Shape = -1;
                switch (collider)
                {
                    case SphereCollider sphere:
                        m_Shape = (int)VividParticleNativeColliderShape.Sphere;
                        m_Center = sphere.center;
                        m_Radius = sphere.radius;
                        break;
                    case BoxCollider box:
                        m_Shape = (int)VividParticleNativeColliderShape.Box;
                        m_Center = box.center;
                        m_Size = box.size;
                        break;
                    case CapsuleCollider capsule:
                        m_Shape = (int)VividParticleNativeColliderShape.Capsule;
                        m_Center = capsule.center;
                        m_Radius = capsule.radius;
                        m_Height = capsule.height;
                        m_Direction = capsule.direction;
                        break;
                }
                m_Layer = collider.gameObject.layer;
                m_Enabled = collider.enabled;
                m_Active = collider.gameObject.activeInHierarchy;
                m_IsTrigger = collider.isTrigger;
                m_IsDynamic = collider.attachedRigidbody != null
                    && !collider.attachedRigidbody.isKinematic;
            }

            public static ColliderSnapshot Capture(Collider collider) => new(collider);

            public bool Equals(ColliderSnapshot other)
            {
                return m_LocalToWorld == other.m_LocalToWorld
                    && m_Center == other.m_Center
                    && m_Size == other.m_Size
                    && m_Radius.Equals(other.m_Radius)
                    && m_Height.Equals(other.m_Height)
                    && m_Direction == other.m_Direction
                    && m_Layer == other.m_Layer
                    && m_Shape == other.m_Shape
                    && m_Enabled == other.m_Enabled
                    && m_Active == other.m_Active
                    && m_IsTrigger == other.m_IsTrigger
                    && m_IsDynamic == other.m_IsDynamic;
            }
        }

        private sealed class ColliderObjectTracker : ObjectTracker<Collider>
        {
            public ColliderObjectTracker()
                : base(ObjectDispatcherService.TypeTrackingFlags.SceneObjects)
            {
            }

            public override void ProcessData(
                List<Object> changed,
                NativeArray<EntityId> changedId,
                NativeArray<EntityId> destroyedId)
            {
                VividParticleColliderRegistry.ProcessData(changed, changedId, destroyedId);
            }
        }
    }
}
