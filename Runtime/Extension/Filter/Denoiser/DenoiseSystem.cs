using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal partial class DenoiseSystem : CameraRelatedSystem<DenoiseSystem>
    {

        public SpatialDenoiser spatialDenoiser = new();

        public TemporalFilter temporalDenoiser = new();

        
        public TextureHandle historyValidity;
        


        protected override void Initialize(Camera camera)
        {
            spatialDenoiser?.Init();
            temporalDenoiser?.Init();
            
        }


        public override void Dispose()
        {
            spatialDenoiser?.Release();
            temporalDenoiser?.Release();
        }
    }
}