using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.TerrainTools
{
    internal static class VividTerrainMenus
    {
        private const string ContextMenuPath = "CONTEXT/Terrain/Create VividTerrain Copy";
        private const string GameObjectMenuPath = "GameObject/VividRP/GPUDriven/Create VividTerrain Copy";
        private const string UpgradeCompositeMenuPath =
            "Assets/VividRP/Terrain/Build Missing Composite SVT";

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

        [MenuItem(UpgradeCompositeMenuPath, false, 2200)]
        private static void UpgradeSelectedTerrainAssets()
        {
            List<VividTerrainData> selectedTerrains = GetSelectedTerrainAssets();
            if (selectedTerrains.Count == 0)
                return;

            int upgradedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            try
            {
                for (int terrainIndex = 0; terrainIndex < selectedTerrains.Count; terrainIndex++)
                {
                    VividTerrainData terrainData = selectedTerrains[terrainIndex];
                    float terrainProgressStart = terrainIndex / (float) selectedTerrains.Count;
                    float terrainProgressScale = 1.0f / selectedTerrains.Count;
                    bool success;
                    bool upgraded;
                    string errorMessage;
                    try
                    {
                        success = VividTerrainCompositeUpgradeUtility.TryUpgrade(
                            terrainData,
                            VividTerrainCompositeSource.DefaultMaxResolution,
                            (progress, message) => EditorUtility.DisplayProgressBar(
                                "Build Terrain Composite SVT",
                                $"{terrainData.name}: {message}",
                                terrainProgressStart + progress * terrainProgressScale),
                            out upgraded,
                            out errorMessage);
                    }
                    catch (Exception exception)
                    {
                        success = false;
                        upgraded = false;
                        errorMessage = exception.Message;
                    }
                    if (success && upgraded)
                    {
                        upgradedCount++;
                        Debug.Log(
                            $"[VividRP] Added Composite SVT '{terrainData.CompositeVirtualTexture.name}' "
                            + $"to terrain data '{terrainData.name}'.",
                            terrainData);
                    }
                    else if (success)
                    {
                        skippedCount++;
                    }
                    else
                    {
                        failedCount++;
                        Debug.LogError($"[VividRP] Could not upgrade '{terrainData.name}': {errorMessage}", terrainData);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[VividRP] Terrain Composite SVT upgrade complete: "
                + $"{upgradedCount} upgraded, {skippedCount} skipped, {failedCount} failed.");
        }

        [MenuItem(UpgradeCompositeMenuPath, true)]
        private static bool ValidateUpgradeSelectedTerrainAssets()
        {
            return GetSelectedTerrainAssets().Count > 0;
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
                $"Chunk LOD range: {result.Data.GeometryChunkLODRange.x}..{result.Data.GeometryChunkLODRange.y}.",
                result.Component
            );
        }

        private static List<VividTerrainData> GetSelectedTerrainAssets()
        {
            var selectedTerrains = new List<VividTerrainData>();
            UnityEngine.Object[] selectedObjects = Selection.objects;
            for (int objectIndex = 0; objectIndex < selectedObjects.Length; objectIndex++)
            {
                if (selectedObjects[objectIndex] is VividTerrainData terrainData
                    && !selectedTerrains.Contains(terrainData))
                {
                    selectedTerrains.Add(terrainData);
                }
            }

            return selectedTerrains;
        }
    }
}
