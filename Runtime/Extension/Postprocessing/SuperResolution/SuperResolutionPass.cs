using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.SuperResolution
{
    public class SuperResolutionPass
    {
        
        
        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle source)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            
            
            bool useTemporalAA = cameraData.IsTemporalAAEnabled();

            // STP is only enabled when TAA is enabled and all of its runtime requirements are met.
            // Using IsSTPRequested() vs IsSTPEnabled() for perf reason here, as we already know TAA status
            bool isSTPRequested = cameraData.IsSTPRequested();
            bool useSTP = useTemporalAA && isSTPRequested;

            if (useSTP)
            {
                
            }
            
            

            SuperResolutionUtil.UpdateCameraResolution(renderGraph, cameraData, new Vector2Int(cameraData.pixelWidth, cameraData.pixelHeight));
            return source;
        }
    }
}