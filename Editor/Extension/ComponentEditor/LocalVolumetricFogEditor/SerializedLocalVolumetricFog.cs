using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    class SerializedLocalVolumetricFog
    {
        public SerializedProperty parameters;

        public SerializedProperty albedo;
        public SerializedProperty meanFreePath;

        public SerializedProperty fogMode;
        public SerializedProperty fogMaterial;
        public SerializedProperty fogTexture;

        public SerializedProperty textureScrollingSpeed;
        public SerializedProperty textureTiling;

        public SerializedProperty priority;

        public SerializedProperty size;
        public SerializedProperty positiveFade;
        public SerializedProperty negativeFade;

        public SerializedProperty editorUniformFade;

        SerializedObject m_SerializedObject;

        static readonly string s_FogVolumeVoxelizeStr = "FogVolumeVoxelize";

        public SerializedLocalVolumetricFog(SerializedObject serializedObject)
        {
            m_SerializedObject = serializedObject;

            parameters = serializedObject.FindProperty("parameters");

            albedo = parameters.FindPropertyRelative("albedo");
            meanFreePath = parameters.FindPropertyRelative("meanFreePath");

            fogMode = parameters.FindPropertyRelative("fogMode");
            fogMaterial = parameters.FindPropertyRelative("fogMaterial");
            fogTexture = parameters.FindPropertyRelative("fogTexture");

            textureScrollingSpeed = parameters.FindPropertyRelative("textureScrollingSpeed");
            textureTiling = parameters.FindPropertyRelative("textureTiling");

            priority = parameters.FindPropertyRelative("priority");
            size = parameters.FindPropertyRelative("size");
            positiveFade = parameters.FindPropertyRelative("positiveFade");
            negativeFade = parameters.FindPropertyRelative("negativeFade");

            editorUniformFade = parameters.FindPropertyRelative("m_EditorUniformFade");
        }


        public void Apply()
        {
            positiveFade.vector3Value = negativeFade.vector3Value = new Vector3(
                size.vector3Value.x < 0.00001 ? 0 : 1f - (size.vector3Value.x - editorUniformFade.floatValue) / size.vector3Value.x,
                size.vector3Value.y < 0.00001 ? 0 : 1f - (size.vector3Value.y - editorUniformFade.floatValue) / size.vector3Value.y,
                size.vector3Value.z < 0.00001 ? 0 : 1f - (size.vector3Value.z - editorUniformFade.floatValue) / size.vector3Value.z
            );

            m_SerializedObject.ApplyModifiedProperties();
        }
    }
}
