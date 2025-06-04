namespace UnityEngine.Rendering.Universal
{
    public partial class ExternalSystemManager
    {
        
        public delegate void CleanUnusedEvenet();

        
        public static event CleanUnusedEvenet CleanUnusedEvents;
        
        
                
        
        public static void ExecuteCleanUnused()
        {
            CleanUnusedEvents?.Invoke();
        }



    }
}