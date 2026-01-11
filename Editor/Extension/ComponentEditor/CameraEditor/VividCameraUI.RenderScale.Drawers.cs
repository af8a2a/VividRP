using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    using CED = CoreEditorDrawer<VividSerializedCamera>;

    static partial class VividCameraUI
    {
        static bool s_IsRunningTAAU = false;

        partial class Upscaler
        {
            public static readonly CED.IDrawer Drawer;


            static void DrawerTAAU(VividSerializedCamera p, Editor owner)
            {
                using (new EditorGUI.DisabledScope(s_IsRunningTAAU))
                {
                    EditorGUILayout.PropertyField(p.taaQualityLevel, Styles.TAAQualityLevel);
                }
                if (s_IsRunningTAAU)
                    p.taaQualityLevel.intValue = (int)TAAQualityLevel.High;


                EditorGUILayout.PropertyField(p.taaSharpenMode, Styles.TAASharpeningMode);
                EditorGUI.indentLevel++;
                if (p.taaSharpenMode.intValue != (int)TAASharpenMode.ContrastAdaptiveSharpening)
                {
                    EditorGUILayout.PropertyField(p.taaSharpenStrength, Styles.TAASharpen);
                    if (p.taaSharpenMode.intValue == (int)TAASharpenMode.PostSharpen)
                    {
                        EditorGUILayout.PropertyField(p.taaRingingReduction, Styles.TAARingingReduction);
                    }
                }
                EditorGUI.indentLevel--;

                if (p.taaQualityLevel.intValue > (int)TAAQualityLevel.Low)
                {
                    EditorGUILayout.PropertyField(p.taaHistorySharpening, Styles.TAAHistorySharpening);
                    EditorGUILayout.PropertyField(p.taaAntiFlicker, Styles.TAAAntiFlicker);
                }

                if (p.taaQualityLevel.intValue == (int)TAAQualityLevel.High)
                {
                    EditorGUILayout.PropertyField(p.taaMotionVectorRejection, Styles.TAAMotionVectorRejection);
                    EditorGUILayout.PropertyField(p.taaAntiRinging, Styles.TAAAntiRinging);
                }

                if (k_ExpandedState[Expandable.Upscaler] && k_ExpandedAdditionalState[ExpandableAdditional.Upscaler])
                {
                    EditorGUILayout.PropertyField(p.taaBaseBlendFactor, Styles.TAABaseBlendFactor);
                    using (new EditorGUI.DisabledScope(s_IsRunningTAAU))
                    {
                        EditorGUILayout.PropertyField(p.taaJitterScale, Styles.TAAJitterScale);
                    }
                }
            }


            static void DrawerDLSS(VividSerializedCamera p, Editor owner)
            {
                // EditorGUILayout.Space();
                EditorGUILayout.LabelField(Styles.DLSSHeader, EditorStyles.boldLabel);
                //
                // // Quality Level selector
                EditorGUILayout.PropertyField(p.dlssQualityLevel);
            }


            static void DrawerUpscaler(VividSerializedCamera p, Editor owner)
            {
                p.renderScale.floatValue = EditorGUILayout.Slider(Styles.renderScaleText, p.renderScale.floatValue, UniversalRenderPipeline.minRenderScale,
                    UniversalRenderPipeline.maxRenderScale);


                EditorGUILayout.PropertyField(p.upscalerTechnique, Styles.upscalingTechniqueText);


                if (p.upscalerTechnique.intValue == (int)UpscalingTechnique.TAAU)
                {
                    DrawerTAAU(p, owner);
                }
                else if (p.upscalerTechnique.intValue == (int)UpscalingTechnique.DLSS)
                {
                    DrawerDLSS(p, owner);
                }
            }
            
            static Upscaler()
            {
                Drawer = CED.FoldoutGroup(
                    VividCameraUI.Upscaler.Styles.header,
                    Expandable.Upscaler,
                    k_ExpandedState,
                    FoldoutOption.Indent,
                    CED.Conditional(
                        (serialized, owner) => (CameraRenderType)serialized.cameraType.intValue == CameraRenderType.Base,
                        CED.Group(
                            DrawerUpscaler
                        )
                    )
                );
            }
        }
    }
}