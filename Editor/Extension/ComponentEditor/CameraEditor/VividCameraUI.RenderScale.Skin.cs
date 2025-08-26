using System.Linq;
using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
    static partial class VividCameraUI
    {
        public partial class Upscaler
        {
            public class Styles
            {
                /// <summary>
                /// Header of the section
                /// </summary>
                public static readonly GUIContent header = EditorGUIUtility.TrTextContent("Upscaler", "These settings control what the camera used Upscaler.");

                public static GUIContent renderScaleText = EditorGUIUtility.TrTextContent("Render Scale", "Scales the camera render target allowing the game to render at a resolution different than native resolution. UI is always rendered at native resolution.");
                public static GUIContent upscalingTechniqueText = EditorGUIUtility.TrTextContent("Upscaling Technique", "Controls the Technique used for upscaling when render scale is lower than 1.0.");
                public static GUIContent fsrOverrideSharpness = EditorGUIUtility.TrTextContent("Override FSR Sharpness", "Overrides the FSR sharpness value for the render pipeline asset.");
                public static GUIContent fsrSharpnessText = EditorGUIUtility.TrTextContent("FSR Sharpness", "Controls the intensity of the sharpening filter used by FidelityFX Super Resolution.");

            }
        }
    }
}
