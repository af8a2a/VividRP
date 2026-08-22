using System;
using System.IO;
using UnityEditor;

namespace VividRP.Editor.GPUDriven
{
    internal static class GPUDrivenGeneratedAssetPathUtility
    {
        internal const string RootFolderName = "GPUDrivenGenerated";
        internal const string MaterialProxyFolderName = "MaterialProxy";
        internal const string MeshletAssetFolderName = "MeshletAsset";
        internal const string StreamedVirtualTextureFolderName = "SVT";
        internal const string StreamedVirtualTextureBinaryFolderName = "Bin";

        internal static string EnsureMaterialProxyFolder(string sourceFolder)
        {
            return EnsureGeneratedTree(sourceFolder) + "/" + MaterialProxyFolderName;
        }

        internal static string EnsureMeshletAssetFolder(string sourceFolder)
        {
            return EnsureGeneratedTree(sourceFolder) + "/" + MeshletAssetFolderName;
        }

        internal static string ResolveStreamedVirtualTextureFolderForProxy(string proxyAssetPath)
        {
            string proxyFolder = NormalizeAssetPath(Path.GetDirectoryName(proxyAssetPath));
            string generatedRoot = NormalizeAssetPath(Path.GetDirectoryName(proxyFolder));
            if (string.Equals(
                    Path.GetFileName(proxyFolder),
                    MaterialProxyFolderName,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    Path.GetFileName(generatedRoot),
                    RootFolderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                EnsureGeneratedTreeAtRoot(generatedRoot);
                return generatedRoot + "/" + StreamedVirtualTextureFolderName;
            }

            return proxyFolder;
        }

        internal static string ResolveStreamDataPath(string virtualTextureAssetPath)
        {
            string normalizedAssetPath = NormalizeAssetPath(virtualTextureAssetPath);
            string assetFolder = NormalizeAssetPath(Path.GetDirectoryName(normalizedAssetPath));
            string generatedRoot = NormalizeAssetPath(Path.GetDirectoryName(assetFolder));
            if (string.Equals(
                    Path.GetFileName(assetFolder),
                    StreamedVirtualTextureFolderName,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    Path.GetFileName(generatedRoot),
                    RootFolderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeAssetPath(Path.Combine(
                    assetFolder,
                    StreamedVirtualTextureBinaryFolderName,
                    Path.GetFileName(normalizedAssetPath) + ".stream"));
            }

            return normalizedAssetPath + ".stream";
        }

        private static string EnsureGeneratedTree(string sourceFolder)
        {
            string normalizedSourceFolder = NormalizeAssetPath(sourceFolder);
            if (string.IsNullOrWhiteSpace(normalizedSourceFolder))
            {
                normalizedSourceFolder = "Assets";
            }

            string generatedRoot = FindGeneratedRoot(normalizedSourceFolder);
            if (string.IsNullOrEmpty(generatedRoot))
            {
                generatedRoot = EnsureFolder(normalizedSourceFolder, RootFolderName);
            }

            EnsureGeneratedTreeAtRoot(generatedRoot);
            return generatedRoot;
        }

        private static string FindGeneratedRoot(string folderPath)
        {
            string[] segments = folderPath.Split('/');
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                if (!string.Equals(
                        segments[segmentIndex],
                        RootFolderName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return string.Join("/", segments, 0, segmentIndex + 1);
            }

            return string.Empty;
        }

        private static void EnsureGeneratedTreeAtRoot(string generatedRoot)
        {
            EnsureFolder(generatedRoot, MaterialProxyFolderName);
            EnsureFolder(generatedRoot, MeshletAssetFolderName);
            string streamedVirtualTextureFolder = EnsureFolder(
                generatedRoot,
                StreamedVirtualTextureFolderName);
            EnsureFolder(streamedVirtualTextureFolder, StreamedVirtualTextureBinaryFolderName);
        }

        private static string EnsureFolder(string parentFolder, string folderName)
        {
            string normalizedParent = NormalizeAssetPath(parentFolder).TrimEnd('/');
            string folderPath = normalizedParent + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder(normalizedParent, folderName);
            }

            return folderPath;
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
