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
    internal struct VividParticleNativeWindZone
    {
        public float3 Position;
        public float3 Forward;
        public float Radius;
        public float WindMain;
        public float PulseMagnitude;
        public float PulseFrequency;
        public int Mode;
        public int Active;
    }

    internal static unsafe class VividParticleWindZoneRegistry
    {
        private static readonly List<WindZone> s_Zones = new();
        private static readonly Dictionary<EntityId, int> s_ZoneIndices = new();
        private static readonly List<WindZoneSnapshot> s_Snapshots = new();
        private static WindZoneObjectTracker s_ObjectTracker;
        private static NativeList<VividParticleNativeWindZone> s_NativeZones;
        private static bool s_Initialized;
        private static bool s_Dirty = true;
        private static int s_Version;
        private static int s_DiscoveryCount;

        public static int count => s_NativeZones.IsCreated ? s_NativeZones.Length : 0;
        public static int version => s_Version;
        public static int discoveryCount => s_DiscoveryCount;
        public static VividParticleNativeWindZone* zones => s_NativeZones.IsCreated
            ? (VividParticleNativeWindZone*)s_NativeZones.GetUnsafeReadOnlyPtr()
            : null;

        public static bool Prepare()
        {
            EnsureInitialized();
            DetectRuntimeChanges();
            if (!s_Dirty)
                return false;

            if (!s_NativeZones.IsCreated)
            {
                s_NativeZones = new NativeList<VividParticleNativeWindZone>(
                    math.max(4, s_Zones.Count),
                    Allocator.Persistent);
            }

            s_NativeZones.Clear();
            for (int index = 0; index < s_Zones.Count; index++)
            {
                WindZone zone = s_Zones[index];
                if (zone == null)
                    continue;
                Transform zoneTransform = zone.transform;
                Vector3 forward = zoneTransform.forward;
                Vector3 position = zoneTransform.position;
                s_NativeZones.Add(new VividParticleNativeWindZone
                {
                    Position = new float3(position.x, position.y, position.z),
                    Forward = math.normalizesafe(new float3(forward.x, forward.y, forward.z), new float3(0.0f, 0.0f, 1.0f)),
                    Radius = math.max(0.0f, zone.radius),
                    WindMain = zone.windMain,
                    PulseMagnitude = math.max(0.0f, zone.windPulseMagnitude),
                    PulseFrequency = math.max(0.0f, zone.windPulseFrequency),
                    Mode = (int)zone.mode,
                    Active = zone.gameObject.activeInHierarchy ? 1 : 0,
                });
            }

            s_Dirty = false;
            s_Version++;
            return true;
        }

        public static void MarkDirty()
        {
            s_Dirty = true;
        }

        public static void ClearForTests()
        {
            if (s_ObjectTracker != null)
                ObjectDispatcherService.UnregisterObjectTracker(s_ObjectTracker);
            s_ObjectTracker = null;
            s_Zones.Clear();
            s_ZoneIndices.Clear();
            s_Snapshots.Clear();
            if (s_NativeZones.IsCreated)
                s_NativeZones.Dispose();
            s_NativeZones = default;
            s_Initialized = false;
            s_Dirty = true;
            s_Version = 0;
            s_DiscoveryCount = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ClearForTests();

        private static void EnsureInitialized()
        {
            if (s_Initialized)
                return;

            s_Initialized = true;
            s_ObjectTracker = new WindZoneObjectTracker();
            RebuildTrackedZones();
            ObjectDispatcherService.RegisterObjectTracker(s_ObjectTracker);
        }

        private static void RebuildTrackedZones()
        {
            s_Zones.Clear();
            s_ZoneIndices.Clear();
            s_Snapshots.Clear();
            WindZone[] zones = Object.FindObjectsByType<WindZone>(
                FindObjectsInactive.Include);
            s_DiscoveryCount++;
            for (int index = 0; index < zones.Length; index++)
                AddOrUpdate(zones[index]);
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
                if (changed[index] is WindZone zone)
                    AddOrUpdate(zone);
            }
        }

        private static void AddOrUpdate(WindZone zone)
        {
            if (zone == null || zone.transform == null)
                return;
            EntityId entityId = zone.GetEntityId();
            if (entityId.Equals(EntityId.None))
                return;

            WindZoneSnapshot snapshot = WindZoneSnapshot.Capture(zone);
            if (s_ZoneIndices.TryGetValue(entityId, out int existingIndex))
            {
                s_Zones[existingIndex] = zone;
                if (!s_Snapshots[existingIndex].Equals(snapshot))
                {
                    s_Snapshots[existingIndex] = snapshot;
                    s_Dirty = true;
                }
                return;
            }

            s_ZoneIndices.Add(entityId, s_Zones.Count);
            s_Zones.Add(zone);
            s_Snapshots.Add(snapshot);
            s_Dirty = true;
        }

        private static void Remove(EntityId entityId)
        {
            if (entityId.Equals(EntityId.None)
                || !s_ZoneIndices.TryGetValue(entityId, out int index))
            {
                return;
            }

            int lastIndex = s_Zones.Count - 1;
            WindZone lastZone = s_Zones[lastIndex];
            s_Zones[index] = lastZone;
            s_Snapshots[index] = s_Snapshots[lastIndex];
            s_Zones.RemoveAt(lastIndex);
            s_Snapshots.RemoveAt(lastIndex);
            s_ZoneIndices.Remove(entityId);
            if (index != lastIndex && lastZone != null)
                s_ZoneIndices[lastZone.GetEntityId()] = index;
            s_Dirty = true;
        }

        private static void DetectRuntimeChanges()
        {
            for (int index = s_Zones.Count - 1; index >= 0; index--)
            {
                WindZone zone = s_Zones[index];
                if (zone == null)
                {
                    RebuildTrackedZones();
                    return;
                }

                WindZoneSnapshot snapshot = WindZoneSnapshot.Capture(zone);
                if (!s_Snapshots[index].Equals(snapshot))
                {
                    s_Snapshots[index] = snapshot;
                    s_Dirty = true;
                }
            }
        }

        private readonly struct WindZoneSnapshot : IEquatable<WindZoneSnapshot>
        {
            private readonly Vector3 m_Position;
            private readonly Vector3 m_Forward;
            private readonly float m_Radius;
            private readonly float m_WindMain;
            private readonly float m_PulseMagnitude;
            private readonly float m_PulseFrequency;
            private readonly WindZoneMode m_Mode;
            private readonly bool m_Active;

            private WindZoneSnapshot(WindZone zone)
            {
                m_Position = zone.transform.position;
                m_Forward = zone.transform.forward;
                m_Radius = zone.radius;
                m_WindMain = zone.windMain;
                m_PulseMagnitude = zone.windPulseMagnitude;
                m_PulseFrequency = zone.windPulseFrequency;
                m_Mode = zone.mode;
                m_Active = zone.gameObject.activeInHierarchy;
            }

            public static WindZoneSnapshot Capture(WindZone zone) => new(zone);

            public bool Equals(WindZoneSnapshot other)
            {
                return m_Position == other.m_Position
                    && m_Forward == other.m_Forward
                    && m_Radius == other.m_Radius
                    && m_WindMain == other.m_WindMain
                    && m_PulseMagnitude == other.m_PulseMagnitude
                    && m_PulseFrequency == other.m_PulseFrequency
                    && m_Mode == other.m_Mode
                    && m_Active == other.m_Active;
            }
        }

        private sealed class WindZoneObjectTracker : ObjectTracker<WindZone>
        {
            public WindZoneObjectTracker()
                : base(ObjectDispatcherService.TypeTrackingFlags.SceneObjects)
            {
            }

            public override void ProcessData(
                List<Object> changed,
                NativeArray<EntityId> changedId,
                NativeArray<EntityId> destroyedId)
            {
                VividParticleWindZoneRegistry.ProcessData(changed, changedId, destroyedId);
            }
        }
    }
}
