using UnityEditor;
using VividRP.Runtime;

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
            EditorApplication.delayCall += () =>
            {
                var globalSettings = VividRenderPipelineGlobalSettings.Ensure();
                VividDefaultVolumeProfileEditorUtility.EnsureDefaultVolumeProfile(globalSettings);
            };
        }
    }
}
