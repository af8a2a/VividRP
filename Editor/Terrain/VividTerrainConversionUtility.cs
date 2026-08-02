using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.TerrainTools
{
    internal static class VividTerrainConversionUtility
    {
        internal readonly struct Result
        {
            public Result(VividTerrain component, VividTerrainData data, string assetPath)
            {
                Component = component;
                Data = data;
                AssetPath = assetPath;
            }

            public VividTerrain Component { get; }

            public VividTerrainData Data { get; }

            public string AssetPath { get; }
        }

        internal static Result CreateCopy(
            UnityEngine.Terrain sourceTerrain,
            string assetPath,
            VividTerrainBakeSettings settings)
        {
            if (sourceTerrain == null)
            {
                throw new ArgumentNullException(nameof(sourceTerrain));
            }

            TerrainData terrainData = sourceTerrain.terrainData;
            if (terrainData == null)
            {
                throw new InvalidOperationException($"Terrain '{sourceTerrain.name}' has no TerrainData assigned.");
            }

            ResolveSourceAssetIdentity(terrainData, out string sourceGuid, out long sourceLocalFileId);
            VividTerrainData bakedData;
            try
            {
                bakedData = VividTerrainBaker.BakeToAsset(
                    assetPath,
                    new VividTerrainBaker.Parameters
                    {
                        SourceTerrainData = terrainData,
                        SourceMaterial = sourceTerrain.materialTemplate,
                        SourceTerrainDataGUID = sourceGuid,
                        SourceTerrainDataLocalFileID = sourceLocalFileId,
                        Settings = settings,
                        ProgressHandler = (progress, message) =>
                            EditorUtility.DisplayProgressBar("Bake VividTerrain", message, progress),
                        LogErrorHandler = message => Debug.LogError($"[VividRP] {message}", sourceTerrain),
                    }
                );
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            GameObject convertedObject = CreateTerrainCopy(sourceTerrain);
            UnityEngine.Terrain copiedTerrain = convertedObject.GetComponent<UnityEngine.Terrain>();
            VividTerrain component = convertedObject.GetComponent<VividTerrain>();
            if (component == null)
            {
                component = Undo.AddComponent<VividTerrain>(convertedObject);
            }

            Undo.RecordObject(component, "Assign VividTerrain Data");
            component.SetData(bakedData);
            EditorUtility.SetDirty(component);

            if (copiedTerrain != null)
            {
                Undo.DestroyObjectImmediate(copiedTerrain);
            }

            Selection.activeObject = component;
            return new Result(component, bakedData, assetPath);
        }

        private static GameObject CreateTerrainCopy(UnityEngine.Terrain sourceTerrain)
        {
            GameObject sourceObject = sourceTerrain.gameObject;
            Transform sourceTransform = sourceObject.transform;
            GameObject copy = UnityEngine.Object.Instantiate(sourceObject);
            copy.name = sourceObject.name + " VividTerrain";

            Transform copyTransform = copy.transform;
            copyTransform.SetParent(sourceTransform.parent, worldPositionStays: false);
            copyTransform.SetLocalPositionAndRotation(sourceTransform.localPosition, sourceTransform.localRotation);
            copyTransform.localScale = sourceTransform.localScale;
            copyTransform.SetSiblingIndex(sourceTransform.GetSiblingIndex() + 1);

            Undo.RegisterCreatedObjectUndo(copy, "Create VividTerrain Copy");
            return copy;
        }

        internal static string GetDefaultAssetPath(UnityEngine.Terrain sourceTerrain)
        {
            if (sourceTerrain == null)
            {
                return string.Empty;
            }

            string sourcePath = sourceTerrain.terrainData != null
                ? AssetDatabase.GetAssetPath(sourceTerrain.terrainData)
                : string.Empty;
            string targetFolder = "Assets";
            if (!string.IsNullOrEmpty(sourcePath) && sourcePath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                targetFolder = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? "Assets";
            }

            string fileName = SanitizeFileName(sourceTerrain.name) + "_VividTerrain.asset";
            return AssetDatabase.GenerateUniqueAssetPath($"{targetFolder}/{fileName}");
        }

        private static void ResolveSourceAssetIdentity(
            TerrainData terrainData,
            out string sourceGuid,
            out long sourceLocalFileId)
        {
            sourceGuid = string.Empty;
            sourceLocalFileId = 0L;
            if (terrainData == null)
            {
                return;
            }

            string sourcePath = AssetDatabase.GetAssetPath(terrainData);
            if (!string.IsNullOrEmpty(sourcePath))
            {
                sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            }

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    terrainData,
                    out string resolvedGuid,
                    out long resolvedLocalFileId))
            {
                if (!string.IsNullOrEmpty(resolvedGuid))
                {
                    sourceGuid = resolvedGuid;
                }

                sourceLocalFileId = resolvedLocalFileId;
            }
        }

        private static string SanitizeFileName(string value)
        {
            string fileName = string.IsNullOrWhiteSpace(value) ? "Terrain" : value;
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            for (int index = 0; index < invalidCharacters.Length; index++)
            {
                fileName = fileName.Replace(invalidCharacters[index], '_');
            }

            return fileName;
        }
    }
}
