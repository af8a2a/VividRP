using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    using CED = CoreEditorDrawer<VividSerializedCamera>;

    static partial class VividCameraUI
    {
        partial class Upscaler
        {
            
            public static readonly CED.IDrawer Drawer;
            

            static void DrawerUpscaler(VividSerializedCamera p, Editor owner)
            {
                p.renderScale.floatValue = EditorGUILayout.Slider(Styles.renderScaleText, p.renderScale.floatValue, UniversalRenderPipeline.minRenderScale,
                    UniversalRenderPipeline.maxRenderScale);
                
                
                EditorGUILayout.PropertyField(p.upscalerTechnique, Styles.upscalingTechniqueText);


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