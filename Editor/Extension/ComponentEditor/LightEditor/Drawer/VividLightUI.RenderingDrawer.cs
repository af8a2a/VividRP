using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    partial class VividLightUI
    {
        static void DrawRenderingContent(VividSerializedLight serializedLight, Editor owner)
        {
            if (!serializedLight.settings.isCompletelyBaked)
            {
                EditorGUI.BeginChangeCheck();

                GUI.enabled = UniversalRenderPipeline.asset.useRenderingLayers;
                EditorGUILayout.PropertyField(serializedLight.renderingLayers,
                    Styles.RenderingLayers);
                // GUI.enabled = true;
                // if (EditorGUI.EndChangeCheck())
                // {
                //     if (!serializedLight.customShadowLayers.boolValue)
                //         SyncLightAndShadowLayers(serializedLight, serializedLight.renderingLayers);
                // }
            }

            // EditorGUILayout.PropertyField(serializedLight.settings.cullingMask, Styles.CullingMask);
            // if (serializedLight.settings.cullingMask.intValue != -1)
            // {
            //     EditorGUILayout.HelpBox(Styles.CullingMaskWarning.text, MessageType.Info);
            // }
        }
    }
}