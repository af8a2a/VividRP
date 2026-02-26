using UnityEngine;

namespace VividRP.Runtime.Utility
{
    public static class VividResources
    {
        [ResourcePath("Hidden/VividRP/Blit")]
        public static Shader BlitShader;

        [ResourcePath("Hidden/VividRP/CoreBlit")]
        public static Shader CoreBlitShader;

        [ResourcePath("Hidden/VividRP/CoreBlitColorAndDepth")]
        public static Shader CoreBlitColorAndDepthShader;

        [ResourcePath("Hidden/VividRP/FullScreenUV")]
        public static Shader FullScreenUVShader;
    }
}
