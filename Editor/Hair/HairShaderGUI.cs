using UnityEditor;
using UnityEngine;

namespace VividRP.Editor
{
    public sealed class HairShaderGUI : ShaderGUI
    {
        public override void AssignNewShaderToMaterial(
            Material material,
            Shader oldShader,
            Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            HairMaterialUtility.SetupMaterial(material);
        }

        public override void ValidateMaterial(Material material)
        {
            HairMaterialUtility.SetupMaterial(material);
        }

        public override void OnGUI(
            MaterialEditor materialEditor,
            MaterialProperty[] properties)
        {
            if (materialEditor == null)
                return;

            EditorGUI.BeginChangeCheck();
            DrawAbsorption(materialEditor, properties);
            DrawScattering(materialEditor, properties);
            DrawInterface(materialEditor, properties);
            DrawEmission(materialEditor, properties);

            if (EditorGUI.EndChangeCheck())
                SetupTargetMaterials(materialEditor.targets);
        }

        private static void DrawAbsorption(
            MaterialEditor materialEditor,
            MaterialProperty[] properties)
        {
            EditorGUILayout.LabelField("Absorption", EditorStyles.boldLabel);
            MaterialProperty model = FindProperty(
                HairMaterialUtility.AbsorptionModelProperty,
                properties);
            materialEditor.ShaderProperty(model, model.displayName);

            int mode = Mathf.Clamp(Mathf.RoundToInt(model.floatValue), 0, 2);
            if (model.hasMixedValue || mode == HairMaterialUtility.ColorAbsorption)
            {
                DrawProperty(
                    materialEditor,
                    properties,
                    HairMaterialUtility.BaseColorProperty);
            }

            if (model.hasMixedValue || mode != HairMaterialUtility.ColorAbsorption)
            {
                DrawProperty(
                    materialEditor,
                    properties,
                    HairMaterialUtility.MelaninProperty);
                DrawProperty(
                    materialEditor,
                    properties,
                    HairMaterialUtility.MelaninRednessProperty);
            }

            EditorGUILayout.HelpBox(
                mode == HairMaterialUtility.ColorAbsorption
                    ? "Absorption Color is converted to a Chiang absorption coefficient."
                    : "Melanin Concentration and Redness drive the physical absorption model.",
                MessageType.None);
        }

        private static void DrawScattering(
            MaterialEditor materialEditor,
            MaterialProperty[] properties)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scattering", EditorStyles.boldLabel);
            DrawProperty(
                materialEditor,
                properties,
                HairMaterialUtility.LongitudinalRoughnessProperty);
            DrawProperty(
                materialEditor,
                properties,
                HairMaterialUtility.AzimuthalRoughnessProperty);
            DrawProperty(
                materialEditor,
                properties,
                HairMaterialUtility.CuticleAngleProperty);
        }

        private static void DrawInterface(
            MaterialEditor materialEditor,
            MaterialProperty[] properties)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fiber Interface", EditorStyles.boldLabel);
            DrawProperty(
                materialEditor,
                properties,
                HairMaterialUtility.IorProperty);
            DrawProperty(
                materialEditor,
                properties,
                HairMaterialUtility.FresnelApproximationProperty);
        }

        private static void DrawEmission(
            MaterialEditor materialEditor,
            MaterialProperty[] properties)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Emission", EditorStyles.boldLabel);
            DrawProperty(
                materialEditor,
                properties,
                HairMaterialUtility.EmissionColorProperty);
        }

        private static void DrawProperty(
            MaterialEditor materialEditor,
            MaterialProperty[] properties,
            string propertyName)
        {
            MaterialProperty property = FindProperty(propertyName, properties);
            materialEditor.ShaderProperty(property, property.displayName);
        }

        private static void SetupTargetMaterials(Object[] targets)
        {
            if (targets == null)
                return;

            foreach (Object target in targets)
            {
                if (target is Material material)
                    HairMaterialUtility.SetupMaterial(material);
            }
        }
    }

    internal static class HairMaterialUtility
    {
        internal const string ShaderName = "VividRP/Material/Hair";
        internal const string AbsorptionModelProperty = "_HairAbsorptionModel";
        internal const string BaseColorProperty = "_HairBaseColor";
        internal const string MelaninProperty = "_HairMelanin";
        internal const string MelaninRednessProperty = "_HairMelaninRedness";
        internal const string LongitudinalRoughnessProperty =
            "_HairLongitudinalRoughness";
        internal const string AzimuthalRoughnessProperty =
            "_HairAzimuthalRoughness";
        internal const string IorProperty = "_HairIor";
        internal const string CuticleAngleProperty =
            "_HairCuticleAngleDegrees";
        internal const string FresnelApproximationProperty =
            "_HairFresnelApproximation";
        internal const string EmissionColorProperty = "_HairEmissionColor";
        internal const string MaterialVersionProperty = "_HairMaterialVersion";

        internal const int ColorAbsorption = 0;
        internal const int PhysicalAbsorption = 1;
        internal const int NormalizedPhysicalAbsorption = 2;
        internal const float CurrentMaterialVersion = 1.0f;

        internal static void SetupMaterial(Material material)
        {
            if (material == null)
                return;

            SetFloat(
                material,
                AbsorptionModelProperty,
                Mathf.Clamp(
                    Mathf.RoundToInt(GetFiniteFloat(
                        material,
                        AbsorptionModelProperty,
                        PhysicalAbsorption)),
                    ColorAbsorption,
                    NormalizedPhysicalAbsorption));
            SetColor(
                material,
                BaseColorProperty,
                SanitizeBaseColor(GetColor(
                    material,
                    BaseColorProperty,
                    new Color(0.227f, 0.130f, 0.035f, 1.0f))));
            SetClampedFloat(material, MelaninProperty, 0.0f, 1.0f, 0.805f);
            SetClampedFloat(
                material,
                MelaninRednessProperty,
                0.0f,
                1.0f,
                0.05f);
            SetClampedFloat(
                material,
                LongitudinalRoughnessProperty,
                0.001f,
                1.0f,
                0.4f);
            SetClampedFloat(
                material,
                AzimuthalRoughnessProperty,
                0.001f,
                1.0f,
                0.6f);
            SetClampedFloat(material, IorProperty, 1.0001f, 3.0f, 1.55f);
            SetClampedFloat(
                material,
                CuticleAngleProperty,
                0.0f,
                10.0f,
                3.0f);
            SetFloat(
                material,
                FresnelApproximationProperty,
                GetFiniteFloat(
                    material,
                    FresnelApproximationProperty,
                    1.0f) > 0.5f
                    ? 1.0f
                    : 0.0f);
            SetColor(
                material,
                EmissionColorProperty,
                SanitizeEmission(GetColor(
                    material,
                    EmissionColorProperty,
                    Color.black)));
            SetFloat(material, MaterialVersionProperty, CurrentMaterialVersion);
            SyncGlobalIlluminationFlags(material);
        }

        private static void SetClampedFloat(
            Material material,
            string propertyName,
            float minimum,
            float maximum,
            float fallback)
        {
            SetFloat(
                material,
                propertyName,
                Mathf.Clamp(
                    GetFiniteFloat(material, propertyName, fallback),
                    minimum,
                    maximum));
        }

        private static Color SanitizeBaseColor(Color color)
        {
            return new Color(
                Mathf.Clamp01(GetFinite(color.r, 0.227f)),
                Mathf.Clamp01(GetFinite(color.g, 0.130f)),
                Mathf.Clamp01(GetFinite(color.b, 0.035f)),
                Mathf.Clamp01(GetFinite(color.a, 1.0f)));
        }

        private static Color SanitizeEmission(Color color)
        {
            return new Color(
                Mathf.Max(GetFinite(color.r, 0.0f), 0.0f),
                Mathf.Max(GetFinite(color.g, 0.0f), 0.0f),
                Mathf.Max(GetFinite(color.b, 0.0f), 0.0f),
                Mathf.Max(GetFinite(color.a, 0.0f), 0.0f));
        }

        private static void SyncGlobalIlluminationFlags(Material material)
        {
            Color emission = GetColor(material, EmissionColorProperty, Color.black);
            if (emission.maxColorComponent > 0.001f)
            {
                material.globalIlluminationFlags &=
                    ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            else
            {
                material.globalIlluminationFlags |=
                    MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
        }

        private static float GetFiniteFloat(
            Material material,
            string propertyName,
            float fallback)
        {
            return material.HasProperty(propertyName)
                ? GetFinite(material.GetFloat(propertyName), fallback)
                : fallback;
        }

        private static Color GetColor(
            Material material,
            string propertyName,
            Color fallback)
        {
            return material.HasProperty(propertyName)
                ? material.GetColor(propertyName)
                : fallback;
        }

        private static float GetFinite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : value;
        }

        private static void SetFloat(
            Material material,
            string propertyName,
            float value)
        {
            if (material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        private static void SetColor(
            Material material,
            string propertyName,
            Color value)
        {
            if (material.HasProperty(propertyName))
                material.SetColor(propertyName, value);
        }
    }
}
