using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public class VividCameraData : ContextItem
    {
        public Camera camera;
        public VividAdditionalCameraData additionalData;
        public VividCameraRenderType renderType;
        public bool clearDepth;
        public int actualWidth;
        public int actualHeight;
        public int pixelWidth;
        public int pixelHeight;
        public Rect pixelRect;

        public override void Reset()
        {
            camera = null;
            additionalData = null;
            renderType = VividCameraRenderType.Base;
            clearDepth = true;
            actualWidth = 0;
            actualHeight = 0;
            pixelWidth = 0;
            pixelHeight = 0;
            pixelRect = default;
        }
    }
}
