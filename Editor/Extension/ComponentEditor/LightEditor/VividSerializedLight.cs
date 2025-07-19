using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    internal class VividSerializedLight : ISerializedLight
    {
        /// <summary>The base settings of the light</summary>
        public LightEditor.Settings settings { get; }

        /// <summary>The light serialized</summary>
        public SerializedObject serializedObject { get; }

        /// <summary>The additional light data serialized</summary>
        public SerializedObject serializedAdditionalDataObject { get; private set; }

        public UniversalAdditionalLightData additionalLightData => lightsAdditionalData[0];
        public UniversalAdditionalLightData[] lightsAdditionalData { get; private set; }

        // Common SRP's Lights properties
        public SerializedProperty intensity { get; }

        #region URP

        // URP Light Properties
        public SerializedProperty useAdditionalDataProp { get; } // Does light use shadow bias settings defined in UniversalRP asset file?
        public SerializedProperty additionalLightsShadowResolutionTierProp { get; } // Index of the AdditionalLights ShadowResolution Tier
        public SerializedProperty softShadowQualityProp { get; } // Per light soft shadow filtering quality.
        public SerializedProperty lightCookieSizeProp { get; } // Multi dimensional light cookie size replacing `cookieSize` in legacy light.
        public SerializedProperty lightCookieOffsetProp { get; } // Multi dimensional light cookie offset.


        // Light layers related
        public SerializedProperty renderingLayers { get; }
        public SerializedProperty customShadowLayers { get; }
        public SerializedProperty shadowRenderingLayers { get; }

        #endregion


        #region Cluster lighting

        public SerializedProperty angularDiameter { get; }

        // Shape Puntual
        public SerializedProperty shapeRadius { get; }

        public SerializedProperty baseContributionProp { get; }

        #endregion


        #region AreaLight

        public SerializedProperty shapeWidth;
        public SerializedProperty shapeHeight;


        #endregion
        
        #region Method

        public bool HasMultipleLightTypes(Editor owner)
        {
            return owner.serializedObject.FindProperty("m_Type").hasMultipleDifferentValues;
        }

        #endregion
        
        

        /// <summary>Method that updates the <see cref="SerializedObject"/> of the Light and the Additional Light Data</summary>
        public void Update()
        {
            serializedObject.Update();
            serializedAdditionalDataObject.Update();
            settings.Update();
        }

        /// <summary>Method that applies the modified properties the <see cref="SerializedObject"/> of the Light and the Light Camera Data</summary>
        public void Apply()
        {
            serializedObject.ApplyModifiedProperties();
            serializedAdditionalDataObject.ApplyModifiedProperties();
            settings.ApplyModifiedProperties();
        }

        /// <summary>Constructor</summary>
        /// <param name="serializedObject"><see cref="SerializedObject"/> with the light</param>
        /// <param name="settings"><see cref="LightEditor.Settings"/>with the settings</param>
        public VividSerializedLight(SerializedObject serializedObject,LightEditor.Settings settings)
        {
            this.settings = settings;
            settings.OnEnable();

            this.serializedObject = serializedObject;

            lightsAdditionalData = CoreEditorUtils
                .GetAdditionalData<UniversalAdditionalLightData>(serializedObject.targetObjects);
            serializedAdditionalDataObject = new SerializedObject(lightsAdditionalData);


            intensity = serializedObject.FindProperty("m_Intensity");

            using (var o = new PropertyFetcher<UniversalAdditionalLightData>(serializedAdditionalDataObject))
            {
                #region URP

                useAdditionalDataProp = o.Find(x => x.usePipelineSettings);
                additionalLightsShadowResolutionTierProp = o.Find(x => x.additionalLightsShadowResolutionTier);
                softShadowQualityProp = o.Find(x => x.softShadowQuality);
                lightCookieSizeProp = o.Find(x => x.lightCookieSize);
                lightCookieOffsetProp = o.Find(x => x.lightCookieOffset);

                renderingLayers = o.Find(x => x.renderingLayers);
                customShadowLayers = o.Find(x => x.customShadowLayers);
                shadowRenderingLayers = o.Find(x => x.shadowRenderingLayers);

                #endregion


                #region Cluster lighting

                angularDiameter = o.Find(x => x.angularDiameter);
                shapeRadius = o.Find(x => x.shapeRadius);
                baseContributionProp = o.Find(x => x.baseContribution);

                #endregion
            }


            settings.ApplyModifiedProperties();
        }
    }
}