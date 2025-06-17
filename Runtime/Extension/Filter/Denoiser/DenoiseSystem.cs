using System;

namespace UnityEngine.Rendering.Universal
{
    internal partial class DenoiseSystem : IDisposable
    {
        private static  Lazy<DenoiseSystem> _instance = new Lazy<DenoiseSystem>(() => new DenoiseSystem());

        public static DenoiseSystem instance => _instance.Value;

        public SpatialDenoiser spatialDenoiser;

        public DenoiseSystem()
        {
            spatialDenoiser = new SpatialDenoiser();
        }


        public void Initialize()
        {
            spatialDenoiser?.Init();
        }

        
        public static void ClearAll()
        {
            instance?.Dispose();
        }

        public void Dispose()
        {
            spatialDenoiser?.Release();
        }
    }
}