using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    public class PlaneManager
    {
        private static Lazy<PlaneManager> _instance = new Lazy<PlaneManager>(() => new PlaneManager());


        public static PlaneManager instance => _instance.Value;


       private List<ScreenSpacePlanarReflectionPlane> planes = new List<ScreenSpacePlanarReflectionPlane>();

       
       public List<ScreenSpacePlanarReflectionPlane> Planes => planes;
       
       
        public void PlaneAdd(ScreenSpacePlanarReflectionPlane plane)
        {
            planes.Add(plane);
        }

        public void PlaneRemove(ScreenSpacePlanarReflectionPlane plane)
        {
            planes.Remove(plane);
        }
    }
}