using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    public class ExternalSystemManager
    {
        public delegate void ExternalSystemUpdateEvent();

        public delegate void ExternalSystemDisposeEvent();


        public static event ExternalSystemUpdateEvent UpdateEvents;

        public static event ExternalSystemDisposeEvent DisposeEvents;


        public static void ExecuteUpdate()
        {
            UpdateEvents?.Invoke();
        }

        public static void ExecuteDispose()
        {
            DisposeEvents?.Invoke();
        }
    }
}