using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal partial class DenoiseSystem : IDisposable
    {
        private static Lazy<DenoiseSystem> _instance = new Lazy<DenoiseSystem>(() => new DenoiseSystem());

        public static DenoiseSystem instance => _instance.Value;

        public SpatialDenoiser spatialDenoiser;

        public TemporalFilter temporalDenoiser;

        
        public TextureHandle historyValidity;
        
        public static SpatialDenoiser GetSpatialDenoiser()
        {
            return instance.spatialDenoiser;
        }


        public static TemporalFilter GetTemporalFilter()
        {
            return instance.temporalDenoiser;
        }


        public DenoiseSystem()
        {
            spatialDenoiser = new SpatialDenoiser();
            temporalDenoiser = new TemporalFilter();
        }


        public void Initialize()
        {
            spatialDenoiser?.Init();
            temporalDenoiser?.Init();
        }


        public static void ClearAll()
        {
            instance?.Dispose();
        }

        public void Dispose()
        {
            spatialDenoiser?.Release();
            temporalDenoiser?.Release();
        }
    }
}