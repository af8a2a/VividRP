using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal partial class DenoiseSystem : CameraRelatedSystem<DenoiseSystem>
    {

        public SpatialDenoiser spatialDenoiser = new();

        public TemporalFilter temporalDenoiser = new();
        public SIGMADenoiser nrdSIGMADenoiser = new();

        
        public TextureHandle historyValidity;
        


        protected override void Initialize(Camera camera)
        {
            spatialDenoiser?.Init();
            temporalDenoiser?.Init();
            nrdSIGMADenoiser?.Init(camera);
            
        }


        public override void Dispose()
        {
            spatialDenoiser?.Release();
            temporalDenoiser?.Release();

        }
    }
}