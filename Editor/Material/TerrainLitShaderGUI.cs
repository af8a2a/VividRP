using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Editor
{
    public sealed class TerrainLitShaderGUI : ShaderGUI, ITerrainLayerCustomUI
    {
        private bool m_ShowLayerRemapping;

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            TerrainLitMaterialUtility.SetupMaterial(material);
        }

        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            TerrainLitMaterialUtility.SetupMaterial(material);
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            DrawTerrainMaterialProperties(materialEditor, properties);
        }

        bool ITerrainLayerCustomUI.OnTerrainLayerGUI(TerrainLayer terrainLayer, Terrain terrain)
        {
            if (terrainLayer == null)
                return false;

            bool heightBlendEnabled = TerrainLitMaterialUtility.IsHeightBlendEnabled(terrain != null ? terrain.materialTemplate : null);

            EditorGUI.BeginChangeCheck();

            Texture2D diffuseTexture = (Texture2D)EditorGUILayout.ObjectField("Diffuse", terrainLayer.diffuseTexture, typeof(Texture2D), false);

            Vector4 diffuseRemapMin = terrainLayer.diffuseRemapMin;
            Vector4 diffuseRemapMax = terrainLayer.diffuseRemapMax;
            Color diffuseTint = new(diffuseRemapMax.x, diffuseRemapMax.y, diffuseRemapMax.z, 1.0f);
            diffuseTint = EditorGUILayout.ColorField(EditorGUIUtility.TrTextContent("Color Tint"), diffuseTint, true, false, false);
            bool opacityAsDensity = !heightBlendEnabled && EditorGUILayout.Toggle("Opacity as Density", diffuseRemapMin.w > 0.5f);

            Texture2D normalMapTexture = (Texture2D)EditorGUILayout.ObjectField("Normal Map", terrainLayer.normalMapTexture, typeof(Texture2D), false);
            float normalScale = terrainLayer.normalScale;
            if (normalMapTexture != null)
                normalScale = EditorGUILayout.FloatField("Normal Scale", normalScale);

            Texture2D maskMapTexture = (Texture2D)EditorGUILayout.ObjectField(
                heightBlendEnabled ? "Mask Map (M/AO/H/S)" : "Mask Map (M/AO/S)",
                terrainLayer.maskMapTexture,
                typeof(Texture2D),
                false);

            Vector4 maskRemapMin = terrainLayer.maskMapRemapMin;
            Vector4 maskRemapMax = terrainLayer.maskMapRemapMax;
            float metallic = terrainLayer.metallic;
            float smoothness = terrainLayer.smoothness;
            TerrainLayerSmoothnessSource smoothnessSource = terrainLayer.smoothnessSource;

            m_ShowLayerRemapping = EditorGUILayout.Foldout(m_ShowLayerRemapping, maskMapTexture != null ? "Channel Remapping" : "Default Values", true);
            if (m_ShowLayerRemapping)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    if (maskMapTexture != null)
                    {
                        DrawMinMaxSlider("R: Metallic", ref maskRemapMin.x, ref maskRemapMax.x);
                        DrawMinMaxSlider("G: AO", ref maskRemapMin.y, ref maskRemapMax.y);
                        if (heightBlendEnabled)
                            DrawMinMaxSlider("B: Height", ref maskRemapMin.z, ref maskRemapMax.z);
                        DrawMinMaxSlider("A: Smoothness", ref maskRemapMin.w, ref maskRemapMax.w);
                    }
                    else
                    {
                        metallic = EditorGUILayout.Slider("R: Metallic", metallic, 0.0f, 1.0f);
                        maskRemapMax.y = EditorGUILayout.Slider("G: AO", maskRemapMax.y, 0.0f, 1.0f);
                        if (heightBlendEnabled)
                        {
                            maskRemapMax.z = EditorGUILayout.FloatField("B: Height", maskRemapMax.z);
                            maskRemapMin.z = Mathf.Min(maskRemapMin.z, maskRemapMax.z);
                        }

                        smoothnessSource = (TerrainLayerSmoothnessSource)EditorGUILayout.EnumPopup("Smoothness Source", smoothnessSource);
                        if (smoothnessSource != TerrainLayerSmoothnessSource.DiffuseAlphaChannel)
                            smoothness = EditorGUILayout.Slider("A: Smoothness", smoothness, 0.0f, 1.0f);

                        maskRemapMin.y = Mathf.Min(maskRemapMin.y, maskRemapMax.y);
                    }
                }
            }

            Vector2 tileSize = EditorGUILayout.Vector2Field("Tile Size", terrainLayer.tileSize);
            Vector2 tileOffset = EditorGUILayout.Vector2Field("Tile Offset", terrainLayer.tileOffset);

            if (!EditorGUI.EndChangeCheck())
                return true;

            Undo.RecordObject(terrainLayer, "Edit Terrain Layer");
            diffuseRemapMin.x = 0.0f;
            diffuseRemapMin.y = 0.0f;
            diffuseRemapMin.z = 0.0f;
            diffuseRemapMin.w = opacityAsDensity ? 1.0f : 0.0f;
            diffuseRemapMax.x = diffuseTint.r;
            diffuseRemapMax.y = diffuseTint.g;
            diffuseRemapMax.z = diffuseTint.b;
            diffuseRemapMax.w = 1.0f;

            terrainLayer.diffuseTexture = diffuseTexture;
            terrainLayer.diffuseRemapMin = diffuseRemapMin;
            terrainLayer.diffuseRemapMax = diffuseRemapMax;
            terrainLayer.normalMapTexture = normalMapTexture;
            terrainLayer.normalScale = normalScale;
            terrainLayer.maskMapTexture = maskMapTexture;
            terrainLayer.maskMapRemapMin = maskRemapMin;
            terrainLayer.maskMapRemapMax = maskRemapMax;
            terrainLayer.metallic = metallic;
            terrainLayer.smoothness = smoothness;
            terrainLayer.smoothnessSource = smoothnessSource;
            terrainLayer.tileSize = tileSize;
            terrainLayer.tileOffset = tileOffset;
            EditorUtility.SetDirty(terrainLayer);
            return true;
        }

        private static void DrawTerrainMaterialProperties(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            if (materialEditor == null)
                return;

            EditorGUI.BeginChangeCheck();

            DrawProperty(materialEditor, properties, "_EnableHeightBlend", "Enable Height-based Blend");
            var heightBlend = FindPropertyOptional(properties, "_EnableHeightBlend");
            if (heightBlend != null && heightBlend.floatValue > 0.5f)
            {
                using (new EditorGUI.IndentLevelScope())
                    DrawProperty(materialEditor, properties, "_HeightTransition", "Height Transition");
            }

            DrawProperty(materialEditor, properties, "_EnableInstancedPerPixelNormal", "Enable Per-pixel Normal");
            DrawProperty(materialEditor, properties, "_ReceivesSSR", "Receive SSR");
            DrawProperty(materialEditor, properties, "_SupportDecals", "Receive Decals");

            materialEditor.EnableInstancingField();

            if (!EditorGUI.EndChangeCheck())
                return;

            foreach (Object target in materialEditor.targets)
            {
                if (target is Material material)
                    TerrainLitMaterialUtility.SetupMaterial(material);
            }
        }

        private static void DrawProperty(MaterialEditor materialEditor, MaterialProperty[] properties, string propertyName, string displayName)
        {
            var property = FindPropertyOptional(properties, propertyName);
            if (property != null)
                materialEditor.ShaderProperty(property, displayName);
        }

        private static MaterialProperty FindPropertyOptional(MaterialProperty[] properties, string propertyName)
        {
            return FindProperty(propertyName, properties, false);
        }

        private static void DrawMinMaxSlider(string label, ref float min, ref float max)
        {
            EditorGUILayout.MinMaxSlider(label, ref min, ref max, 0.0f, 1.0f);
            min = Mathf.Clamp01(min);
            max = Mathf.Clamp01(max);
            if (max < min)
                max = min;
        }
    }

    internal static class TerrainLitMaterialUtility
    {
        internal const string TerrainLitShaderName = "VividRP/Terrain/TerrainLit";
        internal const string HeightBlendKeyword = "_TERRAIN_BLEND_HEIGHT";
        internal const string InstancedPerPixelNormalKeyword = "_TERRAIN_INSTANCED_PERPIXEL_NORMAL";
        internal const string AlphaTestKeyword = "_ALPHATEST_ON";

        private const float EnabledThreshold = 0.5f;

        internal static void SetupMaterial(Material material)
        {
            if (material == null)
                return;

            CoreUtils.SetKeyword(material, HeightBlendKeyword, IsHeightBlendEnabled(material));
            CoreUtils.SetKeyword(material, InstancedPerPixelNormalKeyword, GetFloat(material, "_EnableInstancedPerPixelNormal") > EnabledThreshold);

            bool alphaClip = GetFloat(material, "_AlphaClip") > EnabledThreshold || material.IsKeywordEnabled(AlphaTestKeyword);
            CoreUtils.SetKeyword(material, AlphaTestKeyword, alphaClip);

            material.SetOverrideTag("RenderType", alphaClip ? "TransparentCutout" : "Opaque");
            material.renderQueue = alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            material.globalIlluminationFlags |= MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        internal static bool IsHeightBlendEnabled(Material material)
        {
            return GetFloat(material, "_EnableHeightBlend") > EnabledThreshold;
        }

        private static float GetFloat(Material material, string propertyName)
        {
            return material != null && material.HasProperty(propertyName)
                ? material.GetFloat(propertyName)
                : 0.0f;
        }
    }
}
