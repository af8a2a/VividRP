using UnityEditor;
using VividRP.Runtime.RenderPipeline;

namespace VividRP.Editor.RenderPipeline
{
    class VividGlobalSettingsPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            EditorApplication.delayCall += () => VividRenderPipelineGlobalSettings.Ensure();
        }
    }
}
