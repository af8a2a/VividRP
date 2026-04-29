using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal static class VividLocalVolumetricFogMenuItems
    {
        internal const string CreateLocalVolumetricFogMenuPath = "GameObject/Rendering/Local Volumetric Fog";

        [MenuItem(CreateLocalVolumetricFogMenuPath, priority = 12)]
        private static void CreateLocalVolumetricFog(MenuCommand menuCommand)
        {
            CreateLocalVolumetricFogGameObject(menuCommand.context as GameObject);
        }

        internal static GameObject CreateLocalVolumetricFogGameObject(GameObject parent)
        {
            var gameObject = new GameObject("Local Volumetric Fog");
            GameObjectUtility.SetParentAndAlign(gameObject, parent);
            gameObject.AddComponent<VividLocalVolumetricFog>();

            Undo.RegisterCreatedObjectUndo(gameObject, "Create Local Volumetric Fog");
            Selection.activeGameObject = gameObject;
            return gameObject;
        }
    }
}
