using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.RenderPipeline
{
    internal static class VividDefaultVolumeProfileEditorUtility
    {
        internal const string DefaultVolumeProfilePath = "Assets/VividRPDefaultVolumeProfile.asset";

        internal static VolumeProfile EnsureDefaultVolumeProfile(
            VividRenderPipelineGlobalSettings globalSettings,
            string assetPath = DefaultVolumeProfilePath)
        {
            if (globalSettings == null)
                return null;

            var volumeSettings = globalSettings.GetSettings<VividDefaultVolumeProfileSettings>();
            if (volumeSettings == null)
                return null;

            var profile = volumeSettings.volumeProfile;
            if (profile == null)
            {
                profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(assetPath);
                if (profile == null)
                {
                    CoreUtils.EnsureFolderTreeInAssetFilePath(assetPath);
                    profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    profile.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                    AssetDatabase.CreateAsset(profile, assetPath);
                    AssetDatabase.SaveAssetIfDirty(profile);
                }

                volumeSettings.volumeProfile = profile;
                EditorUtility.SetDirty(globalSettings);
                AssetDatabase.SaveAssetIfDirty(globalSettings);
            }

            if (RenderPipelineManager.currentPipeline is VividRenderPipeline)
                VolumeProfileUtils.UpdateGlobalDefaultVolumeProfile<VividRenderPipeline>(profile);

            VividVolumeManagerUtility.UpgradeDefaultVolumeProfileValues(profile);
            if (profile.TryGet<AutoExposure>(out var autoExposure)
                && autoExposure.ConsumeUnrealDefaultProfileUpgradePendingSave())
            {
                EditorUtility.SetDirty(autoExposure);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }

            return profile;
        }
    }
}
