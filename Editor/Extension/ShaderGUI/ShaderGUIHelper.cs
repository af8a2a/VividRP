
using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
    public static class ShaderGUIHelper
    {
        public static void ConvertLerp2(float from, float to, out float z, out float w)
        {
            // lerp from + val * (to - from) (sub-mad)
            // z: to - from w: from,
            // lerp2 val * z + w (mad)
            z = to - from;
            w = from;
        }

        public static void ConvertLinearStep2(float from, float to, out float z, out float w)
        {
            // (val - from) / (to - from) (sub-sub-rcp)
            // z : 1.0f / (to - from) w:  -from / (to - from)
            // LinearStep2 val * z + w (mad)
            var v = Mathf.Max((to - from), 0.0001f);
            z = 1.0f / v;
            w = -from / v;
        }

        public static void ConvertSmoothStep2(float from, float to, out float z, out float w)
        {
            // (val - from) / (to - from) val * val * (3 * val - 2) (sub-sub-rcp-mad-mul-mul) 
            // z : 1.0f / (to - from) w:  -from / (to - from)
            // SmoothStep2 val * z + w  val * val * (3 * val - 2) (mad-mad-mul-mul)
            ConvertLinearStep2(from, to, out z, out w);
        }

        public static void ConvertIvnLinearStep2(float from, float to, out float z, out float w)
        {
            // (to - val) / (to - from) (sub-sub-rcp)
            // z : - 1.0f / (to - from) w:  to / (to - from)
            // IvnLinearStep2 val * z + w (mad)
            var v = Mathf.Max((to - from), 0.0001f);
            z = -1.0f / v;
            w = to / v;
        }
    }
}