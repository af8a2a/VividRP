using System;
using UnityEditor;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal sealed class VividSlabLutPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (ShouldInvalidateLut(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths))
                VividSlabLut.InvalidateSource();
        }

        internal static bool ShouldInvalidateLut(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            return ContainsLutSource(importedAssets) || ContainsLutSource(deletedAssets)
                || ContainsLutSource(movedAssets) || ContainsLutSource(movedFromAssetPaths);
        }

        private static bool ContainsLutSource(string[] paths)
        {
            foreach (string path in paths)
            {
                if (path.EndsWith("/VividSlabLut.compute", StringComparison.Ordinal)
                    || path.EndsWith("/VividSlabLutIntegration.hlsl", StringComparison.Ordinal)
                    || path.EndsWith("/VividSimpleSlabEnergy.hlsl", StringComparison.Ordinal)
                    || path.EndsWith("/VividSimpleSlabBSDF.hlsl", StringComparison.Ordinal)
                    || path.EndsWith("/VividSimpleSlabContract.hlsl", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
