using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Build;
using UnityEngine;
using VividRP.Runtime;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VividRP.Editor
{
    [ScriptedImporter(3, Extension)]
    internal sealed class VividVirtualTextureAssetImporter : ScriptedImporter
    {
        internal const string Extension = "vividvt";
        internal const string Version3Marker = "VIVIDVT3";

        public Texture2D SourceTexture;

        public Texture2D NormalTexture;

        public Texture2D MaskTexture;

        public VividVirtualTextureBuildProfile BuildProfile;

        public VividVirtualTextureAddressMode AddressMode = VividVirtualTextureAddressMode.Clamp;

        public VividVirtualTextureStorageProfile StorageProfile = VividVirtualTextureStorageProfile.DesktopBCn;

        public VividVirtualTextureStreamCompression StreamCompression = VividVirtualTextureStreamCompression.Zstd;

        public VividVirtualTextureMaskStorage MaskStorage;

        public VividVirtualTextureBCQuality BCQuality = VividVirtualTextureBCQuality.Normal;

        [Range(1, 3)]
        public int ZstdLevel = 3;

        [Range(128, 256)]
        public int ChunkTargetKiB = 256;

        [SerializeField, HideInInspector]
        private bool m_StorageSettingsInitialized;

        [Min(1)]
        public int PageSize = 128;

        [Min(0)]
        public int BorderSize = 4;

        [Min(0)]
        public int MipCount;

        public Color FallbackColor = Color.black;

        public Color NormalFallbackColor = new(0.5f, 0.5f, 1f, 1f);

        public Color MaskFallbackColor = Color.white;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset(nameof(VividVirtualTextureAsset), asset);
            ctx.SetMainObject(asset);

            if ((BuildProfile == VividVirtualTextureBuildProfile.Generic && SourceTexture == null)
                || (SourceTexture == null && NormalTexture == null && MaskTexture == null))
                return;

            Texture2D primaryTexture = SourceTexture != null ? SourceTexture : NormalTexture != null ? NormalTexture : MaskTexture;
            string sourceTexturePath = AssetDatabase.GetAssetPath(primaryTexture);
            if (!string.IsNullOrEmpty(sourceTexturePath))
                ctx.DependsOnSourceAsset(sourceTexturePath);
            if (NormalTexture != null)
            {
                string normalTexturePath = AssetDatabase.GetAssetPath(NormalTexture);
                if (!string.IsNullOrEmpty(normalTexturePath))
                    ctx.DependsOnSourceAsset(normalTexturePath);
            }

            if (MaskTexture != null)
            {
                string maskTexturePath = AssetDatabase.GetAssetPath(MaskTexture);
                if (!string.IsNullOrEmpty(maskTexturePath))
                    ctx.DependsOnSourceAsset(maskTexturePath);
            }

            var builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            builtData.name = $"{asset.name}_BuiltData";
            ctx.AddObjectToAsset(nameof(VividVirtualTextureBuiltData), builtData);

            var timer = Stopwatch.StartNew();
            string virtualTextureGUID = AssetDatabase.AssetPathToGUID(ctx.assetPath);
            bool useVersion3Defaults = m_StorageSettingsInitialized || HasVersion3Marker(ctx.assetPath);
            VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
            {
                SourceTexture = SourceTexture,
                NormalTexture = NormalTexture,
                MaskTexture = MaskTexture,
                SourceTextureGUID = AssetDatabase.AssetPathToGUID(sourceTexturePath),
                SourceTexturePath = sourceTexturePath,
                PageSize = PageSize,
                BorderSize = BorderSize,
                MipCount = MipCount,
                FallbackColor = (Color32)FallbackColor,
                NormalFallbackColor = (Color32)NormalFallbackColor,
                MaskFallbackColor = (Color32)MaskFallbackColor,
                StreamDataPath = ctx.assetPath + ".stream",
                LogErrorHandler = message => ctx.LogImportError(message),
                BuildProfile = BuildProfile,
                AddressMode = AddressMode,
                RuntimeStreamDataPath = GetRuntimeStreamDataPath(virtualTextureGUID),
                StorageProfile = useVersion3Defaults
                    ? StorageProfile
                    : VividVirtualTextureStorageProfile.LegacyRGBA32,
                StreamCompression = useVersion3Defaults
                    ? StreamCompression
                    : VividVirtualTextureStreamCompression.None,
                MaskStorage = MaskStorage,
                BCQuality = useVersion3Defaults ? BCQuality : VividVirtualTextureBCQuality.Normal,
                ZstdLevel = Mathf.Clamp(ZstdLevel, 1, 3),
                ChunkTargetKiB = Mathf.Clamp(ChunkTargetKiB, 128, 256),
                LogWarningHandler = message => ctx.LogImportWarning(message),
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

            ProjectWindowUtil.CreateAssetWithTextContent("New Virtual Texture." + Extension, Version3Marker);
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

            File.WriteAllText(assetPath, Version3Marker);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(assetPath) is not VividVirtualTextureAssetImporter importer)
                return string.Empty;

            importer.SourceTexture = texture;
            importer.m_StorageSettingsInitialized = true;
            importer.StorageProfile = VividVirtualTextureStorageProfile.DesktopBCn;
            importer.StreamCompression = VividVirtualTextureStreamCompression.Zstd;
            importer.BCQuality = VividVirtualTextureBCQuality.Normal;
            importer.ZstdLevel = 3;
            importer.ChunkTargetKiB = 256;
            Save(assetPath, importer);
            return assetPath;
        }

        [MenuItem("Assets/VividRP/Virtual Texture/Upgrade To Desktop BCn", true)]
        private static bool CanUpgradeSelectedAsset()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return AssetImporter.GetAtPath(path) is VividVirtualTextureAssetImporter;
        }

        [MenuItem("Assets/VividRP/Virtual Texture/Upgrade To Desktop BCn")]
        private static void UpgradeSelectedAsset()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (AssetImporter.GetAtPath(path) is not VividVirtualTextureAssetImporter importer)
                return;

            importer.m_StorageSettingsInitialized = true;
            importer.StorageProfile = VividVirtualTextureStorageProfile.DesktopBCn;
            importer.StreamCompression = VividVirtualTextureStreamCompression.Zstd;
            importer.BCQuality = VividVirtualTextureBCQuality.Normal;
            importer.ZstdLevel = 3;
            importer.ChunkTargetKiB = 256;
            Save(path, importer);
        }

        private static bool HasVersion3Marker(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
                return false;

            using var reader = new StreamReader(assetPath);
            char[] marker = new char[Version3Marker.Length];
            return reader.Read(marker, 0, marker.Length) == marker.Length
                   && new string(marker).Equals(Version3Marker, StringComparison.Ordinal);
        }

        private static void Save(string assetPath, AssetImporter importer)
        {
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        internal static string GetRuntimeStreamDataPath(string virtualTextureGUID)
        {
            string fileName = !string.IsNullOrWhiteSpace(virtualTextureGUID)
                ? virtualTextureGUID
                : "UnidentifiedVirtualTexture";
            return $"VividRP/VirtualTextures/{fileName}.stream";
        }
    }

    internal sealed class VividVirtualTextureBuildPlayerProcessor : BuildPlayerProcessor
    {
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (buildPlayerContext == null)
                throw new ArgumentNullException(nameof(buildPlayerContext));

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] assetGUIDs = AssetDatabase.FindAssets($"t:{nameof(VividVirtualTextureAsset)}");
            for (int assetIndex = 0; assetIndex < assetGUIDs.Length; assetIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGUIDs[assetIndex]);
                VividVirtualTextureAsset asset = AssetDatabase.LoadAssetAtPath<VividVirtualTextureAsset>(assetPath);
                VividVirtualTextureBuiltData builtData = asset != null ? asset.BuiltData : null;
                if (builtData == null || !builtData.HasStreamData)
                    continue;

                string sourcePath = ResolveSourcePath(projectRoot, builtData.StreamDataPath);
                if (!File.Exists(sourcePath))
                {
                    throw new BuildFailedException(
                        $"Virtual texture asset '{assetPath}' is missing stream data '{sourcePath}'. Reimport the asset before building the Player.");
                }

                string runtimePath = !string.IsNullOrWhiteSpace(builtData.RuntimeStreamDataPath)
                    ? builtData.RuntimeStreamDataPath
                    : VividVirtualTextureAssetImporter.GetRuntimeStreamDataPath(assetGUIDs[assetIndex]);
                buildPlayerContext.AddAdditionalPathToStreamingAssets(sourcePath, runtimePath);
            }
        }

        private static string ResolveSourcePath(string projectRoot, string streamDataPath)
        {
            string normalizedPath = streamDataPath.Replace('\\', '/');
            return Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(Path.Combine(projectRoot, normalizedPath));
        }
    }
}
