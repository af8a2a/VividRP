using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class RaytracingData:ContextItem
    {
        
        /// <summary>
        /// RayTracing system for current camera.
        /// </summary>
        internal RayTracingSystem rayTracingSystem;

        
        
        internal TextureHandle rayTracingShadowTexture;
        
        public override void Reset()
        {
            rayTracingSystem = null;
            rayTracingShadowTexture = TextureHandle.nullHandle;
        }
    }
}