using UnityEditor;
using UnityEngine;
using VividRP.Runtime.SubSystem.Decal;

namespace VividRP.Editor
{
    internal static class DecalProjectorMenuItems
    {
        internal const string CreateDecalProjectorMenuPath = "GameObject/Rendering/VividRP Decal Projector";

        [MenuItem(CreateDecalProjectorMenuPath, priority = 10)]
        private static void CreateDecalProjector(MenuCommand menuCommand)
        {
            CreateDecalProjectorGameObject(menuCommand.context as GameObject);
        }

        internal static GameObject CreateDecalProjectorGameObject(GameObject parent)
        {
            var gameObject = new GameObject("Decal Projector");
            GameObjectUtility.SetParentAndAlign(gameObject, parent);
            var projector = gameObject.AddComponent<DecalProjector>();
            projector.transform.RotateAround(projector.transform.position, projector.transform.right, 90.0f);

            Undo.RegisterCreatedObjectUndo(gameObject, "Create Decal Projector");
            Selection.activeGameObject = gameObject;
            return gameObject;
        }
    }
}
