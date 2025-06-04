namespace UnityEngine.Rendering.Universal
{
    public class RaytracingData:ContextItem
    {
        
        /// <summary>
        /// RayTracing system for current camera.
        /// </summary>
        internal RayTracingSystem rayTracingSystem;

        public override void Reset()
        {
            rayTracingSystem = null;
        }
    }
}