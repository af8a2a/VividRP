using System.IO;
using System.Text;
using UnityEditor;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [InitializeOnLoad]
    internal static class RenderPassNodeRegistryGenerator
    {
        private const string GeneratedRelativePath = "Editor/RenderGraph/GeneratedRenderPassNodes.g.cs";

        static RenderPassNodeRegistryGenerator()
        {
            EditorApplication.delayCall += GenerateIfNeeded;
        }

        private static void GenerateIfNeeded()
        {
            if (EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += GenerateIfNeeded;
                return;
            }

            var generatedAssetPath = VividPackagePathUtility.GetPreferredAssetPath(GeneratedRelativePath);
            var fullPath = Path.GetFullPath(generatedAssetPath);
            var existingSource = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(
                TypeCache.GetTypesDerivedFrom<IRenderPass>());
            var generatedSource = RenderPassNodeRegistryBuilder.BuildSource(registrations);
            if (string.Equals(existingSource, generatedSource, System.StringComparison.Ordinal))
            {
                RenderPassNodeRegistry.Rebuild();
                return;
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, generatedSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            AssetDatabase.ImportAsset(generatedAssetPath);
        }
    }
}
