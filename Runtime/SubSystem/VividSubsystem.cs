using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public abstract class VividSubsystem<TSelf>
        where TSelf : VividSubsystem<TSelf>, new()
    {
        private static TSelf s_Instance;
        private bool m_Initialized;

        protected static TSelf Instance => s_Instance ??= new TSelf();

        protected static TSelf RawInstance => s_Instance;

        protected static bool HasInstance => s_Instance != null;

        public static bool IsInitialized => s_Instance != null && s_Instance.m_Initialized;

        protected static void ClearInstance()
        {
            s_Instance = null;
        }

        protected static void EnsurePreRenderSubscribed()
        {
            FrameContextSystem.SubsystemPreRender -= DispatchUpdate;
            FrameContextSystem.SubsystemPreRender += DispatchUpdate;
        }

        public static void Initialize()
        {
            if (s_Instance != null && s_Instance.m_Initialized)
                return;

            FrameContextSystem.SubsystemPreRender -= DispatchUpdate;
            FrameContextSystem.SubsystemPreRender += DispatchUpdate;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
#endif
            TSelf instance = Instance;
            instance.OnInitialize();
            instance.m_Initialized = true;
        }

        public static void Deinitialize()
        {
            if (s_Instance == null || !s_Instance.m_Initialized)
                return;

            TSelf instance = s_Instance;
            instance.OnDeinitialize();

            FrameContextSystem.SubsystemPreRender -= DispatchUpdate;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
#endif
            instance.m_Initialized = false;
            s_Instance = null;
        }

#if UNITY_EDITOR
        private static void OnBeforeAssemblyReload()
        {
            Deinitialize();
        }
#endif

        private static void DispatchUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
            Instance.OnUpdate(frameData, cmd);
        }

        protected virtual void OnInitialize()
        {
        }

        protected virtual void OnDeinitialize()
        {
        }

        protected abstract void OnUpdate(ContextContainer frameData, CommandBuffer cmd);
    }

    internal sealed class VividSceneLightSystem : VividSubsystem<VividSceneLightSystem>
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void AutoInitialize()
        {
            Initialize();
        }

        internal static void EnsureInitialized()
        {
            Initialize();
        }

        protected override void OnInitialize()
        {
            InsertIntoPlayerLoop();
        }

        protected override void OnDeinitialize()
        {
            VividLightRenderDatabase.instance.ReleaseSceneLightPrepareResources();
            RemoveFromPlayerLoop();
        }

        protected override void OnUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
        }

        private static void PlayerLoopKick()
        {
            if (!IsInitialized)
                return;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemSceneLightKickMarker.Auto())
            {
                VividLightRenderDatabase.instance.BuildSceneLightSnapshotAndSchedulePrepare(true);
            }
        }

        private static void InsertIntoPlayerLoop()
        {
            var rootLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (var index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                var subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != typeof(PreLateUpdate))
                    continue;

                var updatedSubSystems = new List<PlayerLoopSystem>(subSystem.subSystemList.Length + 1);
                var alreadyPresent = false;
                foreach (var nestedSystem in subSystem.subSystemList)
                {
                    if (nestedSystem.type == typeof(VividSceneLightSystemPlayerLoopMarker))
                        alreadyPresent = true;
                    updatedSubSystems.Add(nestedSystem);
                }

                if (!alreadyPresent)
                    updatedSubSystems.Add(CreatePlayerLoopSystem());

                subSystem.subSystemList = updatedSubSystems.ToArray();
                rootLoop.subSystemList[index] = subSystem;
                break;
            }

            PlayerLoop.SetPlayerLoop(rootLoop);
        }

        private static void RemoveFromPlayerLoop()
        {
            var rootLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (var index = 0; index < rootLoop.subSystemList.Length; index++)
            {
                var subSystem = rootLoop.subSystemList[index];
                if (subSystem.type != typeof(PreLateUpdate))
                    continue;

                var updatedSubSystems = new List<PlayerLoopSystem>(subSystem.subSystemList.Length);
                foreach (var nestedSystem in subSystem.subSystemList)
                {
                    if (nestedSystem.type != typeof(VividSceneLightSystemPlayerLoopMarker))
                        updatedSubSystems.Add(nestedSystem);
                }

                subSystem.subSystemList = updatedSubSystems.ToArray();
                rootLoop.subSystemList[index] = subSystem;
                break;
            }

            PlayerLoop.SetPlayerLoop(rootLoop);
        }

        private static PlayerLoopSystem CreatePlayerLoopSystem()
        {
            return new PlayerLoopSystem
            {
                type = typeof(VividSceneLightSystemPlayerLoopMarker),
                updateDelegate = PlayerLoopKick,
            };
        }

        private sealed class VividSceneLightSystemPlayerLoopMarker
        {
        }
    }
}
