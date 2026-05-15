using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public abstract class VividSubsystem<TSelf>
        where TSelf : VividSubsystem<TSelf>, new()
    {
        private static TSelf s_Instance;
        private bool m_Initialized;

        protected static TSelf Instance => s_Instance ??= new TSelf();

        protected static bool HasInstance => s_Instance != null;

        public static bool IsInitialized => s_Instance != null && s_Instance.m_Initialized;

        public static void Initialize()
        {
            TSelf instance = Instance;
            if (instance.m_Initialized)
                return;

            FrameContextSystem.SubsystemPreRender -= instance.OnUpdate;
            FrameContextSystem.SubsystemPreRender += instance.OnUpdate;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
#endif
            instance.OnInitialize();
            instance.m_Initialized = true;
        }

        public static void Deinitialize()
        {
            if (s_Instance == null || !s_Instance.m_Initialized)
                return;

            s_Instance.OnDeinitialize();

            FrameContextSystem.SubsystemPreRender -= s_Instance.OnUpdate;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
#endif
            s_Instance.m_Initialized = false;
        }

#if UNITY_EDITOR
        private static void OnBeforeAssemblyReload()
        {
            Deinitialize();
        }
#endif

        protected virtual void OnInitialize()
        {
        }

        protected virtual void OnDeinitialize()
        {
        }

        protected abstract void OnUpdate(ContextContainer frameData, CommandBuffer cmd);
    }
}
