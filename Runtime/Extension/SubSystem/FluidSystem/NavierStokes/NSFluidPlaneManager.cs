using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    public class NSFluidPlaneManager : Singleton<NSFluidPlaneManager>
    {
        List<NSFluidPlane> _fluidPlanes = new List<NSFluidPlane>();

        public void Add(NSFluidPlane fluidPlane)
        {
            _fluidPlanes.Add(fluidPlane);
        }


        public void Remove(NSFluidPlane fluidPlane)
        {
            _fluidPlanes.Remove(fluidPlane);
        }

        public IReadOnlyList<NSFluidPlane> GetFluidPlanes() => _fluidPlanes;
    }
}