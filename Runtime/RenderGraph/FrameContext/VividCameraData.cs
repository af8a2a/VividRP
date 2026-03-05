using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividCameraData : ContextItem
    {
        public Camera camera;
        public int actualWidth;
        public int actualHeight;
        public int pixelWidth;
        public int pixelHeight;

        public override void Reset()
        {
            camera = null;
        }
    }
}