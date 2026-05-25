using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VividRP.Editor
{
    [ScriptedImporter(1, Extension)]
    internal sealed class VividVirtualTextureAssetImporter : ScriptedImporter
    {
        internal const string Extension = "vividvt";

        public Texture2D SourceTexture;

        [Min(1)]
        public int PageSize = 128;

        [Min(0)]
        public int BorderSize = 4;

        [Min(0)]
        public int MipCount;

        public Color FallbackColor = Color.black;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset(nameof(VividVirtualTextureAsset), asset);
            ctx.SetMainObject(asset);

            if (SourceTexture == null)
                return;

            string sourceTexturePath = AssetDatabase.GetAssetPath(SourceTexture);
            if (!string.IsNullOrEmpty(sourceTexturePath))
                ctx.DependsOnSourceAsset(sourceTexturePath);

            var builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            builtData.name = $"{asset.name}_BuiltData";
            ctx.AddObjectToAsset(nameof(VividVirtualTextureBuiltData), builtData);

            var timer = Stopwatch.StartNew();
            VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
            {
                SourceTexture = SourceTexture,
                SourceTextureGUID = AssetDatabase.AssetPathToGUID(sourceTexturePath),
                SourceTexturePath = sourceTexturePath,
                PageSize = PageSize,
                BorderSize = BorderSize,
                MipCount = MipCount,
                FallbackColor = (Color32)FallbackColor,
                LogErrorHandler = message => ctx.LogImportError(message),
            });

            timer.Stop();
            Debug.Log($"Building virtual texture for {ctx.assetPath} took {timer.Elapsed.TotalMilliseconds:F3} ms.", asset);
        }

        [MenuItem("Assets/Create/VividRP/Virtual Texture Asset")]
        private static void CreateNewAsset(MenuCommand menuCommand)
        {
            string[] createdAssetPaths = CreateAssetsForSelection(Selection.objects);
            if (createdAssetPaths.Length > 0)
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<VividVirtualTextureAsset>(createdAssetPaths[^1]);
                return;
            }

            ProjectWindowUtil.CreateAssetWithTextContent("New Virtual Texture." + Extension, string.Empty);
        }

        internal static string[] CreateAssetsForSelection(IEnumerable<Object> selection)
        {
            var createdAssetPaths = new List<string>();
            foreach (Object selectedObject in selection)
            {
                if (selectedObject is not Texture2D texture)
                    continue;

                string assetPath = CreateAssetForTexture(texture);
                if (!string.IsNullOrEmpty(assetPath))
                    createdAssetPaths.Add(assetPath);
            }

            return createdAssetPaths.ToArray();
        }

        internal static string CreateAssetForTexture(Texture2D texture)
        {
            if (texture == null)
                return string.Empty;

            string sourcePath = AssetDatabase.GetAssetPath(texture);
            string folder = File.Exists(sourcePath) ? Path.GetDirectoryName(sourcePath) : sourcePath;
            if (string.IsNullOrEmpty(folder))
                folder = "Assets";
            folder = folder.Replace('\\', '/');

            string assetBaseName = !string.IsNullOrWhiteSpace(texture.name)
                ? texture.name
                : "VirtualTexture";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(folder, assetBaseName + "." + Extension).Replace('\\', '/'));

            File.WriteAllText(assetPath, string.Empty);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(assetPath) is not VividVirtualTextureAssetImporter importer)
                return string.Empty;

            importer.SourceTexture = texture;
            Save(assetPath, importer);
            return assetPath;
        }

        private static void Save(string assetPath, AssetImporter importer)
        {
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
