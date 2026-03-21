using System.IO;
using UnityEditor;

namespace VividRP.Editor
{
    internal static class VividPackagePathUtility
    {
        private static readonly string[] s_PackageRoots =
        {
            "Packages/VividRP",
            "Packages/com.af8a2a.vividrp",
        };

        internal static string GetPreferredPackageRoot()
        {
            for (var i = 0; i < s_PackageRoots.Length; i++)
            {
                var packageRoot = s_PackageRoots[i];
                if (AssetDatabase.IsValidFolder(packageRoot) || Directory.Exists(Path.GetFullPath(packageRoot)))
                    return packageRoot;
            }

            return s_PackageRoots[0];
        }

        internal static string GetPreferredAssetPath(string relativeAssetPath)
        {
            return CombineAssetPath(GetPreferredPackageRoot(), relativeAssetPath);
        }

        internal static string[] GetCandidateAssetPaths(string relativeAssetPath)
        {
            var candidatePaths = new string[s_PackageRoots.Length];
            for (var i = 0; i < s_PackageRoots.Length; i++)
                candidatePaths[i] = CombineAssetPath(s_PackageRoots[i], relativeAssetPath);

            return candidatePaths;
        }

        private static string CombineAssetPath(string assetRoot, string relativeAssetPath)
        {
            var normalizedRelativePath = string.IsNullOrEmpty(relativeAssetPath)
                ? string.Empty
                : relativeAssetPath.TrimStart('/', '\\').Replace('\\', '/');

            return string.IsNullOrEmpty(normalizedRelativePath)
                ? assetRoot
                : $"{assetRoot}/{normalizedRelativePath}";
        }
    }
}
