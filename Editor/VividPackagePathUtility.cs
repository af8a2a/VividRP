using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace VividRP.Editor
{
    internal static class VividPackagePathUtility
    {
        private static readonly string[] s_KnownPackageRoots =
        {
            "Packages/com.vivid.render-pipelines",
            "Packages/com.af8a2a.vividrp",
            "Packages/VividRP",
            "Packages/Custom_URP",
        };

        internal static string GetPreferredPackageRoot()
        {
            var packageRoots = GetCandidatePackageRoots();
            for (var i = 0; i < packageRoots.Length; i++)
            {
                var packageRoot = packageRoots[i];
                if (IsAvailablePackageRoot(packageRoot))
                    return packageRoot;
            }

            return s_KnownPackageRoots[0];
        }

        internal static string GetPreferredAssetPath(string relativeAssetPath)
        {
            return CombineAssetPath(GetPreferredPackageRoot(), relativeAssetPath);
        }

        internal static string[] GetCandidateAssetPaths(string relativeAssetPath)
        {
            var packageRoots = GetCandidatePackageRoots();
            var candidatePaths = new string[packageRoots.Length];
            for (var i = 0; i < packageRoots.Length; i++)
                candidatePaths[i] = CombineAssetPath(packageRoots[i], relativeAssetPath);

            return candidatePaths;
        }

        internal static string[] GetCandidatePackageRoots()
        {
            return EnumerateCandidatePackageRoots()
                .Where(packageRoot => !string.IsNullOrEmpty(packageRoot))
                .Distinct()
                .ToArray();
        }

        private static IEnumerable<string> EnumerateCandidatePackageRoots()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(VividPackagePathUtility).Assembly);
            if (!string.IsNullOrEmpty(packageInfo?.assetPath))
                yield return NormalizeAssetPath(packageInfo.assetPath);

            for (var i = 0; i < s_KnownPackageRoots.Length; i++)
                yield return s_KnownPackageRoots[i];
        }

        private static bool IsAvailablePackageRoot(string packageRoot)
        {
            if (string.IsNullOrEmpty(packageRoot))
                return false;

            if (AssetDatabase.IsValidFolder(packageRoot))
                return true;

            return Directory.Exists(Path.GetFullPath(packageRoot));
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

        private static string NormalizeAssetPath(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }
    }
}
