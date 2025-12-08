using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    partial class VividLightUI
    {
        static void DrawShadowsContent(VividSerializedLight serializedLight, Editor owner)
        {
            if (serializedLight.settings.lightType.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Cannot multi edit shadows from different light types.", MessageType.Info);
                return;
            }

            serializedLight.settings.DrawShadowsType();

            if (serializedLight.settings.shadowsType.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Cannot multi edit different shadow types", MessageType.Info);
                return;
            }

            if (serializedLight.settings.light.shadows == LightShadows.None)
                return;

            var lightType = serializedLight.settings.light.type;

            using (new EditorGUI.IndentLevelScope())
            {
                if (serializedLight.settings.isBakedOrMixed)
                {
                    switch (lightType)
                    {
                        // Baked Shadow radius
                        case LightType.Point:
                        case LightType.Spot:
                            serializedLight.settings.DrawShapeRadius();
                            break;
                        case LightType.Directional:
                            serializedLight.settings.DrawBakedShadowAngle();
                            break;
                    }
                }

                if (lightType != LightType.Rectangle && !serializedLight.settings.isCompletelyBaked)
                {
                    EditorGUILayout.LabelField(Styles.ShadowRealtimeSettings, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        // Resolution
                        if (lightType == LightType.Point || lightType == LightType.Spot)
                            DrawShadowsResolutionGUI(serializedLight);

                        EditorGUILayout.Slider(serializedLight.settings.shadowsStrength, 0f, 1f, Styles.ShadowStrength);

                        // Bias
                        DrawAdditionalShadowData(serializedLight, owner);

                        // this min bound should match the calculation in SharedLightData::GetNearPlaneMinBound()
                        float nearPlaneMinBound = Mathf.Min(0.01f * serializedLight.settings.range.floatValue, 0.1f);
                        EditorGUILayout.Slider(serializedLight.settings.shadowsNearPlane, nearPlaneMinBound, 10.0f, Styles.ShadowNearPlane);
                        var isHololens = false;
                        var isQuest = false;
#if XR_MANAGEMENT_4_0_1_OR_NEWER
                        var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
                        var buildTargetSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(buildTargetGroup);
                        if (buildTargetSettings != null && buildTargetSettings.AssignedSettings != null && buildTargetSettings.AssignedSettings.activeLoaders.Count > 0)
                        {
                            isHololens = buildTargetGroup == BuildTargetGroup.WSA;
                            isQuest = buildTargetGroup == BuildTargetGroup.Android;
                        }

#endif
                        // Soft Shadow Quality
                        if (serializedLight.settings.light.shadows == LightShadows.Soft)
                            EditorGUILayout.PropertyField(serializedLight.softShadowQualityProp, Styles.SoftShadowQuality);

                        if (isHololens || isQuest)
                        {
                            EditorGUILayout.HelpBox(
                                "Per-light soft shadow quality level is not supported on HoloLens and Oculus platforms. Use the Soft Shadow Quality setting in the URP Asset instead",
                                MessageType.Warning
                            );
                        }
                    }

                    if (UniversalRenderPipeline.asset.useRenderingLayers)
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(serializedLight.customShadowLayers, Styles.customShadowLayers);
                        // Undo the changes in the light component because the SyncLightAndShadowLayers will change the value automatically when link is ticked
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (serializedLight.customShadowLayers.boolValue)
                            {
                                serializedLight.settings.light.renderingLayerMask = serializedLight.shadowRenderingLayers.intValue;
                            }
                            else
                            {
                                serializedLight.serializedAdditionalDataObject
                                    .ApplyModifiedProperties(); // we need to push above modification the modification on object as it is used to sync
                                SyncLightAndShadowLayers(serializedLight, serializedLight.renderingLayers);
                            }
                        }

                        if (serializedLight.customShadowLayers.boolValue)
                        {
                            using (new EditorGUI.IndentLevelScope())
                            {
                                EditorGUI.BeginChangeCheck();
                                EditorGUILayout.PropertyField(serializedLight.shadowRenderingLayers, Styles.ShadowLayer);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    serializedLight.settings.light.renderingLayerMask = serializedLight.shadowRenderingLayers.intValue;
                                    serializedLight.Apply();
                                }
                            }
                        }
                    }
                }
            }

            if (!UnityEditor.Lightmapping.bakedGI && !serializedLight.settings.lightmapping.hasMultipleDifferentValues &&
                serializedLight.settings.isBakedOrMixed)
                EditorGUILayout.HelpBox(Styles.BakingWarning.text, MessageType.Warning);
        }

        static void DrawAdditionalShadowData(VividSerializedLight serializedLight, Editor editor)
        {
            // 0: Custom bias - 1: Bias values defined in Pipeline settings
            int selectedUseAdditionalData = serializedLight.additionalLightData.usePipelineSettings ? 1 : 0;
            Rect r = EditorGUILayout.GetControlRect(true);
            EditorGUI.BeginProperty(r, Styles.shadowBias, serializedLight.useAdditionalDataProp);
            {
                using (var checkScope = new EditorGUI.ChangeCheckScope())
                {
                    selectedUseAdditionalData = EditorGUI.IntPopup(r, Styles.shadowBias, selectedUseAdditionalData, Styles.displayedDefaultOptions,
                        Styles.optionDefaultValues);
                    if (checkScope.changed)
                    {
                        Undo.RecordObjects(serializedLight.lightsAdditionalData, "Modified light additional data");
                        foreach (var additionData in serializedLight.lightsAdditionalData)
                            additionData.usePipelineSettings = selectedUseAdditionalData != 0;

                        serializedLight.Apply();
                        (editor as VividLightEditor)?.ReconstructReferenceToAdditionalDataSO();
                    }
                }
            }
            EditorGUI.EndProperty();

            if (!serializedLight.useAdditionalDataProp.hasMultipleDifferentValues)
            {
                if (selectedUseAdditionalData != 1) // Custom Bias
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        using (var checkScope = new EditorGUI.ChangeCheckScope())
                        {
                            EditorGUILayout.Slider(serializedLight.settings.shadowsBias, 0f, 10f, Styles.ShadowDepthBias);
                            EditorGUILayout.Slider(serializedLight.settings.shadowsNormalBias, 0f, 10f, Styles.ShadowNormalBias);
                            if (checkScope.changed)
                                serializedLight.Apply();
                        }
                    }
                }
            }
        }

        static void DrawShadowsResolutionGUI(VividSerializedLight serializedLight)
        {
            int shadowResolutionTier = serializedLight.additionalLightData.additionalLightsShadowResolutionTier;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (var checkScope = new EditorGUI.ChangeCheckScope())
                {
                    Rect r = EditorGUILayout.GetControlRect(true);
                    r.width += 30;

                    shadowResolutionTier = EditorGUI.IntPopup(r, Styles.ShadowResolution, shadowResolutionTier, Styles.ShadowResolutionDefaultOptions,
                        Styles.ShadowResolutionDefaultValues);
                    if (shadowResolutionTier == UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierCustom)
                    {
                        // show the custom value field GUI.
                        var newResolution = EditorGUILayout.IntField(serializedLight.settings.shadowsResolution.intValue, GUILayout.ExpandWidth(false));
                        serializedLight.settings.shadowsResolution.intValue = Mathf.Max(UniversalAdditionalLightData.AdditionalLightsShadowMinimumResolution,
                            Mathf.NextPowerOfTwo(newResolution));
                    }
                    else
                    {
                        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpAsset)
                            EditorGUILayout.LabelField($"{urpAsset.GetAdditionalLightsShadowResolution(shadowResolutionTier)} ({urpAsset.name})",
                                GUILayout.ExpandWidth(false));
                    }

                    if (checkScope.changed)
                    {
                        serializedLight.additionalLightsShadowResolutionTierProp.intValue = shadowResolutionTier;
                        serializedLight.Apply();
                    }
                }
            }

            EditorGUILayout.HelpBox(Styles.ShadowInfo.text, MessageType.Info);
        }
    }
}