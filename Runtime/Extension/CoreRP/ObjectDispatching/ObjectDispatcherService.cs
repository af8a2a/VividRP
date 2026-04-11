#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using Object = UnityEngine.Object;

namespace VividRP.Runtime.GPUDriven.ObjectDispatching
{
    public sealed class ObjectDispatcherService : IDisposable
    {
        [Flags]
        public enum TypeTrackingFlags
        {
            SceneObjects = 1,
            Assets = 2,
            EditorOnlyObjects = 4,
            Default = SceneObjects | Assets,
            All = SceneObjects | Assets | EditorOnlyObjects,
        }

        private static readonly Type[] s_SingleTrackedType = new Type[1];
        private static ObjectDispatcherService s_Instance;

        private readonly List<Object> m_ChangedObjects = new();
        private readonly Dictionary<Type, HashSet<ObjectTracker>> m_ObjectTrackers = new();
        private readonly ObjectDispatcher m_Dispatcher;
        private bool m_IsDisposed;

        private ObjectDispatcherService()
        {
            m_Dispatcher = new ObjectDispatcher();

#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += OnAssemblyReload;
#endif
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            RemoveFromPlayerLoop();

#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= OnAssemblyReload;
#endif

            m_Dispatcher.Dispose();

            foreach (KeyValuePair<Type, HashSet<ObjectTracker>> pair in m_ObjectTrackers)
            {
                pair.Value.Clear();
            }

            m_ObjectTrackers.Clear();
            m_ChangedObjects.Clear();
            m_IsDisposed = true;

            if (ReferenceEquals(s_Instance, this))
            {
                s_Instance = null;
            }
        }

        internal static void RegisterObjectTracker(ObjectTracker tracker)
        {
            if (tracker == null)
            {
                throw new ArgumentNullException(nameof(tracker));
            }

            EnsureInitialized();
            s_Instance.RegisterTracker(tracker);
        }

        internal static void UnregisterObjectTracker(ObjectTracker tracker)
        {
            if (tracker == null || s_Instance == null)
            {
                return;
            }

            s_Instance.UnregisterTracker(tracker);
        }

        public static void ProcessUpdates()
        {
            EnsureInitialized();
            s_Instance.OnUpdate();
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
#endif
        private static void Init()
        {
            EnsureInitialized();
        }

        private static void EnsureInitialized()
        {
            if (s_Instance != null)
            {
                return;
            }

            s_Instance = new ObjectDispatcherService();
            s_Instance.InsertIntoPlayerLoop();
        }

        private void RegisterTracker(ObjectTracker tracker)
        {
            if (!m_ObjectTrackers.TryGetValue(tracker.TrackedType, out HashSet<ObjectTracker> trackers))
            {
                trackers = new HashSet<ObjectTracker>();
                m_ObjectTrackers.Add(tracker.TrackedType, trackers);
            }

            if (!trackers.Add(tracker))
            {
                return;
            }

            m_Dispatcher.EnableTypeTracking(
                (ObjectDispatcher.TypeTrackingFlags) tracker.TrackingFlags,
                SingleTrackedTypeArray(tracker.TrackedType)
            );
        }

        private void UnregisterTracker(ObjectTracker tracker)
        {
            if (m_IsDisposed || !m_Dispatcher.valid)
            {
                return;
            }

            if (!m_ObjectTrackers.TryGetValue(tracker.TrackedType, out HashSet<ObjectTracker> trackers) ||
                !trackers.Remove(tracker) ||
                trackers.Count > 0)
            {
                return;
            }

            m_Dispatcher.DisableTypeTracking(SingleTrackedTypeArray(tracker.TrackedType));
        }

        private static Type[] SingleTrackedTypeArray(Type trackedType)
        {
            s_SingleTrackedType[0] = trackedType;
            return s_SingleTrackedType;
        }

        private void InsertIntoPlayerLoop()
        {
            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();
            bool isAdded = false;

            for (int index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                PlayerLoopSystem subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != typeof(PostLateUpdate))
                {
                    continue;
                }

                var updatedSubSystems = new List<PlayerLoopSystem>();
                foreach (PlayerLoopSystem nestedSystem in subSystem.subSystemList)
                {
                    if (!isAdded && nestedSystem.type == typeof(PostLateUpdate.FinishFrameRendering))
                    {
                        updatedSubSystems.Add(CreatePlayerLoopSystem());
                        isAdded = true;
                    }

                    updatedSubSystems.Add(nestedSystem);
                }

                if (!isAdded)
                {
                    updatedSubSystems.Add(CreatePlayerLoopSystem());
                    isAdded = true;
                }

                subSystem.subSystemList = updatedSubSystems.ToArray();
                rootLoop.subSystemList[index] = subSystem;
                break;
            }

            PlayerLoop.SetPlayerLoop(rootLoop);
        }

        private void RemoveFromPlayerLoop()
        {
            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (int index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                PlayerLoopSystem subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != typeof(PostLateUpdate))
                {
                    continue;
                }

                var updatedSubSystems = new List<PlayerLoopSystem>();
                foreach (PlayerLoopSystem nestedSystem in subSystem.subSystemList)
                {
                    if (nestedSystem.type != typeof(ObjectDispatcherService))
                    {
                        updatedSubSystems.Add(nestedSystem);
                    }
                }

                subSystem.subSystemList = updatedSubSystems.ToArray();
                rootLoop.subSystemList[index] = subSystem;
                break;
            }

            PlayerLoop.SetPlayerLoop(rootLoop);
        }

        private void OnAssemblyReload()
        {
            Dispose();
        }

        private void OnUpdate()
        {
            if (m_IsDisposed || !m_Dispatcher.valid)
            {
                return;
            }

            foreach (KeyValuePair<Type, HashSet<ObjectTracker>> pair in m_ObjectTrackers)
            {
                HashSet<ObjectTracker> trackers = pair.Value;
                if (trackers.Count == 0)
                {
                    continue;
                }

                m_ChangedObjects.Clear();
                m_Dispatcher.GetTypeChangesAndClear(
                    pair.Key,
                    m_ChangedObjects,
                    out NativeArray<EntityId> changedId,
                    out NativeArray<EntityId> destroyedId,
                    Allocator.Temp,
                    false
                );

                try
                {
                    foreach (ObjectTracker tracker in trackers)
                    {
                        tracker.ProcessData(m_ChangedObjects, changedId, destroyedId);
                    }
                }
                finally
                {
                    changedId.Dispose();
                    destroyedId.Dispose();
                    m_ChangedObjects.Clear();
                }
            }
        }

        private PlayerLoopSystem CreatePlayerLoopSystem()
        {
            return new PlayerLoopSystem
            {
                type = typeof(ObjectDispatcherService),
                updateDelegate = OnUpdate,
            };
        }
    }
}
