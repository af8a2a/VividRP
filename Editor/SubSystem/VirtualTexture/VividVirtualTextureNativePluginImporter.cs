using System;
using UnityEditor;

namespace VividRP.Editor
{
    internal sealed class VividVirtualTextureNativePluginImporter : AssetPostprocessor
    {
        private const string PluginDirectory = "/Runtime/Plugins/x86_64/";

        private static readonly string[] s_PluginNames =
        {
            "VividVTStreamingNative.dll",
            "dstorage.dll",
            "dstoragecore.dll",
        };

        private void OnPreprocessAsset()
        {
            if (assetImporter is not PluginImporter importer || !IsVirtualTextureStreamingPlugin(assetPath))
                return;

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetEditorData("OS", "Windows");
            importer.SetEditorData("CPU", "x86_64");
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
        }

        [InitializeOnLoadMethod]
        private static void ScheduleExistingPluginConfiguration()
        {
            EditorApplication.delayCall += ConfigureExistingPlugins;
        }

        private static void ConfigureExistingPlugins()
        {
            string[] assetPaths = AssetDatabase.GetAllAssetPaths();
            for (int pathIndex = 0; pathIndex < assetPaths.Length; pathIndex++)
            {
                string path = assetPaths[pathIndex];
                if (!IsVirtualTextureStreamingPlugin(path)
                    || AssetImporter.GetAtPath(path) is not PluginImporter importer)
                {
                    continue;
                }

                bool requiresUpdate = importer.GetCompatibleWithAnyPlatform()
                                      || !importer.GetCompatibleWithEditor()
                                      || !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64)
                                      || importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows)
                                      || importer.GetCompatibleWithPlatform(BuildTarget.StandaloneLinux64)
                                      || importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX)
                                      || !string.Equals(importer.GetEditorData("OS"), "Windows", StringComparison.Ordinal)
                                      || !string.Equals(importer.GetEditorData("CPU"), "x86_64", StringComparison.Ordinal)
                                      || !string.Equals(
                                          importer.GetPlatformData(BuildTarget.StandaloneWindows64, "CPU"),
                                          "x86_64",
                                          StringComparison.Ordinal);
                if (!requiresUpdate)
                    continue;

                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(true);
                importer.SetEditorData("OS", "Windows");
                importer.SetEditorData("CPU", "x86_64");
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
                importer.SaveAndReimport();
            }
        }

        private static bool IsVirtualTextureStreamingPlugin(string path)
        {
            if (string.IsNullOrWhiteSpace(path)
                || path.IndexOf(PluginDirectory, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            for (int pluginIndex = 0; pluginIndex < s_PluginNames.Length; pluginIndex++)
            {
                if (path.EndsWith(s_PluginNames[pluginIndex], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
