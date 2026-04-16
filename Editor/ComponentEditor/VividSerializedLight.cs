using UnityEditor;
using UnityEditor.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal sealed class VividSerializedLight : ISerializedLight
    {
        public LightEditor.Settings settings { get; }
        public SerializedObject serializedObject { get; }
        public SerializedObject serializedAdditionalDataObject { get; }
        public SerializedProperty intensity { get; }

        internal SerializedProperty usePipelineSettings { get; }
        internal SerializedProperty customShadowLayers { get; }
        internal SerializedProperty shadowRenderingLayers { get; }
        internal SerializedProperty enableRayTracedShadow { get; }
        internal SerializedProperty rayTracedShadowRayLength { get; }
        internal SerializedProperty rayTracedShadowRayBias { get; }
        internal SerializedProperty rayTracedShadowDistantRayBias { get; }
        internal SerializedProperty rayTracedShadowSunAngularDiameter { get; }
        internal SerializedProperty screenSpaceShadowQuality { get; }
        internal SerializedProperty shadowAtlasResolution { get; }
        internal SerializedProperty depthBias { get; }
        internal SerializedProperty normalBias { get; }
        internal SerializedProperty slopeBias { get; }
        internal SerializedProperty interactsWithSky { get; }
        internal SerializedProperty angularDiameter { get; }
        internal SerializedProperty diameterMultiplierMode { get; }
        internal SerializedProperty diameterMultiplier { get; }
        internal SerializedProperty diameterOverride { get; }
        internal SerializedProperty celestialBodyShadingSource { get; }
        internal SerializedProperty sunLightOverride { get; }
        internal SerializedProperty sunColor { get; }
        internal SerializedProperty sunIntensity { get; }
        internal SerializedProperty moonPhase { get; }
        internal SerializedProperty moonPhaseRotation { get; }
        internal SerializedProperty earthshine { get; }
        internal SerializedProperty flareSize { get; }
        internal SerializedProperty flareTint { get; }
        internal SerializedProperty flareFalloff { get; }
        internal SerializedProperty flareMultiplier { get; }
        internal SerializedProperty surfaceTexture { get; }
        internal SerializedProperty surfaceTint { get; }
        internal SerializedProperty distance { get; }
        internal VividAdditionalLightData[] lightsAdditionalData { get; }

        public VividSerializedLight(SerializedObject serializedObject, LightEditor.Settings settings = null)
        {
            this.serializedObject = serializedObject;

            if (settings == null)
            {
                this.settings = new LightEditor.Settings(serializedObject);
                this.settings.OnEnable();
            }
            else
            {
                this.settings = settings;
                this.settings.OnEnable();
            }

            lightsAdditionalData = CoreEditorUtils.GetAdditionalData<VividAdditionalLightData>(
                serializedObject.targetObjects,
                VividAdditionalLightDataEditorUtility.Initialize);

            serializedAdditionalDataObject = new SerializedObject(lightsAdditionalData);
            intensity = serializedObject.FindProperty("m_Intensity");

            usePipelineSettings = serializedAdditionalDataObject.FindProperty("m_UsePipelineSettings");
            customShadowLayers = serializedAdditionalDataObject.FindProperty("m_CustomShadowLayers");
            shadowRenderingLayers = serializedAdditionalDataObject.FindProperty("m_ShadowRenderingLayersMask");
            enableRayTracedShadow = serializedAdditionalDataObject.FindProperty("m_EnableRayTracedShadow");
            rayTracedShadowRayLength = serializedAdditionalDataObject.FindProperty("m_RayTracedShadowRayLength");
            rayTracedShadowRayBias = serializedAdditionalDataObject.FindProperty("m_RayTracedShadowRayBias");
            rayTracedShadowDistantRayBias = serializedAdditionalDataObject.FindProperty("m_RayTracedShadowDistantRayBias");
            rayTracedShadowSunAngularDiameter = serializedAdditionalDataObject.FindProperty("m_RayTracedShadowSunAngularDiameter");
            screenSpaceShadowQuality = serializedAdditionalDataObject.FindProperty("m_ScreenSpaceShadowQuality");
            shadowAtlasResolution = serializedAdditionalDataObject.FindProperty("m_ShadowAtlasResolution");
            depthBias = serializedAdditionalDataObject.FindProperty("m_DepthBias");
            normalBias = serializedAdditionalDataObject.FindProperty("m_NormalBias");
            slopeBias = serializedAdditionalDataObject.FindProperty("m_SlopeBias");
            interactsWithSky = serializedAdditionalDataObject.FindProperty("m_InteractsWithSky");
            angularDiameter = serializedAdditionalDataObject.FindProperty("m_AngularDiameter");
            diameterMultiplierMode = serializedAdditionalDataObject.FindProperty("m_DiameterMultiplierMode");
            diameterMultiplier = serializedAdditionalDataObject.FindProperty("m_DiameterMultiplier");
            diameterOverride = serializedAdditionalDataObject.FindProperty("m_DiameterOverride");
            celestialBodyShadingSource = serializedAdditionalDataObject.FindProperty("m_CelestialBodyShadingSource");
            sunLightOverride = serializedAdditionalDataObject.FindProperty("m_SunLightOverride");
            sunColor = serializedAdditionalDataObject.FindProperty("m_SunColor");
            sunIntensity = serializedAdditionalDataObject.FindProperty("m_SunIntensity");
            moonPhase = serializedAdditionalDataObject.FindProperty("m_MoonPhase");
            moonPhaseRotation = serializedAdditionalDataObject.FindProperty("m_MoonPhaseRotation");
            earthshine = serializedAdditionalDataObject.FindProperty("m_Earthshine");
            flareSize = serializedAdditionalDataObject.FindProperty("m_FlareSize");
            flareTint = serializedAdditionalDataObject.FindProperty("m_FlareTint");
            flareFalloff = serializedAdditionalDataObject.FindProperty("m_FlareFalloff");
            flareMultiplier = serializedAdditionalDataObject.FindProperty("m_FlareMultiplier");
            surfaceTexture = serializedAdditionalDataObject.FindProperty("m_SurfaceTexture");
            surfaceTint = serializedAdditionalDataObject.FindProperty("m_SurfaceTint");
            distance = serializedAdditionalDataObject.FindProperty("m_Distance");
        }

        public void Update()
        {
            serializedObject.Update();
            serializedAdditionalDataObject.Update();
            settings.Update();
        }

        public void Apply()
        {
            serializedObject.ApplyModifiedProperties();
            serializedAdditionalDataObject.ApplyModifiedProperties();
            settings.ApplyModifiedProperties();
        }
    }
}
