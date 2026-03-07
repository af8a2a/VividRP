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
