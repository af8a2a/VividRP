using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class ExposureResourceData : ContextItem
    {
        public bool useCurrentCamera;
        public TextureHandle parent;
        public TextureHandle current;
        public TextureHandle previous;

        public bool useFetchedExposure;
        public float fetchedGpuExposure;


        public override void Reset()
        {
            parent = TextureHandle.nullHandle;
            current = TextureHandle.nullHandle;
            previous = TextureHandle.nullHandle;
            useFetchedExposure = false;
            fetchedGpuExposure = 1.0f;
        }
    }
}