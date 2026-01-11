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

                
                
                public static readonly GUIContent TAASharpen = EditorGUIUtility.TrTextContent("Sharpen Strength", "The intensity of the sharpen filter used to counterbalance the blur introduced by TAA. A high value might create artifacts such as dark lines depending on the frame content.");
                public static readonly GUIContent TAARingingReduction = EditorGUIUtility.TrTextContent("Ringing Reduction", "How much the sharpening result is taken from the result without ringing. This reduces unnatural dark outlines, but might also decrease the impact of sharpening. Values above 0.0 lead to extra cost.");

                public static readonly GUIContent TAAHistorySharpening = EditorGUIUtility.TrTextContent("History Sharpening", "Values closer to 0 lead to softer look when movement is detected, but can further reduce aliasing. Values closer to 1 lead to sharper results, with the risk of reintroducing a bit of aliasing.");
                public static readonly GUIContent TAAAntiFlicker = EditorGUIUtility.TrTextContent("Anti-flickering", "With high values flickering might be reduced, but it can lead to more ghosting or disocclusion artifacts.");
                public static readonly GUIContent TAAMotionVectorRejection = EditorGUIUtility.TrTextContent("Speed Rejection", "Higher this value, more likely history will be rejected when current and reprojected history motion vector differ by a substantial amount. High values can decrease ghosting but will also reintroduce aliasing on the aforementioned cases.");
                public static readonly GUIContent TAAQualityLevel = EditorGUIUtility.TrTextContent("Quality Preset", "Low quality is fast, but can lead to more ghosting and blurrier output when moving, Medium quality has better ghosting handling and sharper results upon movement, High allows for velocity rejection policy, has better antialiasing and has mechanism to combat ringing for over sharpening the history.");
                public static readonly GUIContent TAASharpeningMode = EditorGUIUtility.TrTextContent("Sharpening Mode", "Low quality is fast, but is prone to artifact and sub-optimal results, PostSharpen is more expensive, but leads to higher quality sharpening. Finally CAS will also be of higher quality than Low Quality option, offering strong sharpening but limited control.");
                public static readonly GUIContent TAAAntiRinging = EditorGUIUtility.TrTextContent("Anti-ringing", "When enabled, ringing artifacts (dark or strangely saturated edges) caused by history sharpening will be improved. This comes at a potential loss of sharpness upon motion.");
                // Advanced TAA
                public static readonly GUIContent TAABaseBlendFactor = EditorGUIUtility.TrTextContent("Base Blend Factor", "Determines how much the history buffer is blended together with current frame result. Higher values means more history contribution, which leads to better anti aliasing, but also more prone to ghosting.");
                public static readonly GUIContent TAAJitterScale = EditorGUIUtility.TrTextContent("Jitter Scale", "Determines the scale to the jitter applied when TAA is enabled. Lowering this value will lead to less visible flickering and jittering, but also will produce more aliased images.");

                // DLSS Settings
                public static readonly GUIContent DLSSHeader = EditorGUIUtility.TrTextContent("DLSS Settings", "NVIDIA DLSS Super Resolution settings.");
                public static readonly GUIContent DLSSQualityLevel = EditorGUIUtility.TrTextContent("Quality Level", "DLSS quality mode. Higher quality = less upscaling. DLAA = no upscaling, only anti-aliasing.");
                public static readonly string[] DLSSPresetOptions = new string[]
                {
                    "Default - Auto select preset",
                    "Preset J - Reduced ghosting, more flickering",
                    "Preset K - Transformer-based, best quality",
                    "Preset L - Default for Ultra Performance",
                    "Preset M - Default for Performance"
                };
                public static readonly uint[] DLSSPresetValues = new uint[] { 0, 10, 11, 12, 13 };
            }
        }
    }
}
