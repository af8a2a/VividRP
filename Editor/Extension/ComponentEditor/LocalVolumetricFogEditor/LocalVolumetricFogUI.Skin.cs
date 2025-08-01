using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
    static partial class LocalVolumetricFogUI
    {
        internal static class Styles
        {
            public static readonly GUIContent[] s_Toolbar_Contents = new GUIContent[]
            {
                EditorGUIUtility.IconContent("EditCollider", "|Modify the base shape. (SHIFT+1)"),
                EditorGUIUtility.IconContent("PreMatCube", "|Modify the influence volume. (SHIFT+2)")
            };

            public static readonly GUIContent s_FogTextureHeader = new GUIContent("Fog Texture");
            public static readonly GUIContent s_FogMaterialHeader = new GUIContent("Fog Material");

            public static readonly GUIContent s_PriorityLabel = new GUIContent("Priority", "Priority.");
            public static readonly GUIContent s_AlbedoLabel = new GUIContent("Single Scattering Albedo", "The color this fog scatters light to.");
            public static readonly GUIContent s_MeanFreePathLabel = new GUIContent("Fog Distance", "Density at the base of the fog. Determines how far you can see through the fog in meters.");
            public static readonly GUIContent s_FogModeLabel = EditorGUIUtility.TrTextContent("Fog Mode", "Texture fog mode uses a 3D texture as color and density mask. Material fog mode uses a Fog Volume Material to mask color and density.");

            public static readonly GUIContent s_Size = new GUIContent("Size", "Modify the size of this Local Volumetric Fog. This is independent of the Transform's Scale.");
            public static readonly GUIContent s_BlendLabel = new GUIContent("Blend Distance", "Interior distance from the Size where the fog fades in completely.");

            public static readonly GUIContent s_FogTextureLabel = new GUIContent("Texture", "The fog Texture for the Density Mask.");
            public static readonly GUIContent s_TextureScrollLabel = new GUIContent("Scroll Speed", "Modify the speed for each axis at which HDRP scrolls the fog Texture.");
            public static readonly GUIContent s_TextureTileLabel = new GUIContent("Tiling", "Modify the tiling of the fog Texture on each axis individually.");

            public static readonly GUIContent s_FogMaterialLabel = EditorGUIUtility.TrTextContent("Material", "The material used to mask the color and density.");

            public static readonly Color k_GizmoColorBase = new Color(180 / 255f, 180 / 255f, 180 / 255f, 8 / 255f).gamma;

            public static readonly Color[] k_BaseHandlesColor = new Color[]
            {
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma,
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma,
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma,
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma,
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma,
                new Color(180 / 255f, 180 / 255f, 180 / 255f, 255 / 255f).gamma
            };

        }
    }
}
