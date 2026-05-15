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
}
