using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.TerrainTools
{
    internal static class VividTerrainMenus
    {
        private const string ContextMenuPath = "CONTEXT/Terrain/Create VividTerrain Copy";
        private const string GameObjectMenuPath = "GameObject/VividRP/GPUDriven/Create VividTerrain Copy";

        [MenuItem(ContextMenuPath)]
        private static void CreateContextTerrainCopy(MenuCommand command)
        {
            CreateCopy(command.context as UnityEngine.Terrain);
        }

        [MenuItem(ContextMenuPath, true)]
        private static bool ValidateCreateContextTerrainCopy(MenuCommand command)
        {
            return command.context is UnityEngine.Terrain terrain && terrain.terrainData != null;
        }

        [MenuItem(GameObjectMenuPath, false, 22)]
        private static void CreateSelectedTerrainCopy(MenuCommand command)
        {
            UnityEngine.Terrain terrain = command.context is GameObject contextObject
                ? contextObject.GetComponent<UnityEngine.Terrain>()
                : Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponent<UnityEngine.Terrain>()
                    : null;
            CreateCopy(terrain);
        }

        [MenuItem(GameObjectMenuPath, true)]
        private static bool ValidateCreateSelectedTerrainCopy(MenuCommand command)
        {
            GameObject target = command.context as GameObject ?? Selection.activeGameObject;
            return target != null
                   && target.TryGetComponent(out UnityEngine.Terrain terrain)
                   && terrain.terrainData != null;
        }

        private static void CreateCopy(UnityEngine.Terrain terrain)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                return;
            }

            string assetPath = VividTerrainConversionUtility.GetDefaultAssetPath(terrain);
            VividTerrainConversionUtility.Result result = VividTerrainConversionUtility.CreateCopy(
                terrain,
                assetPath,
                VividTerrainBakeSettings.Default
            );
            Debug.Log(
                $"[VividRP] Created VividTerrain copy '{result.Component.name}' and baked " +
                $"{result.Data.GeometryChunkCount}/{result.Data.Chunks.Count} geometry chunks to '{result.AssetPath}'. " +
                $"The source Terrain '{terrain.name}' was left unchanged. " +
                "VividTerrain rendering is not part of this prototype yet.",
                result.Component
            );
        }
    }
}
