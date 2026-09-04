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
            foreach (string path in importedAssets)
            {
                if (path.EndsWith("/VividSlabLut.compute", StringComparison.Ordinal)
                    || path.EndsWith("/VividSlabLutIntegration.hlsl", StringComparison.Ordinal)
                    || path.EndsWith("/VividSimpleSlabEnergy.hlsl", StringComparison.Ordinal)
                    || path.EndsWith("/VividSimpleSlabBSDF.hlsl", StringComparison.Ordinal)
                    || path.EndsWith("/VividSimpleSlabContract.hlsl", StringComparison.Ordinal))
                {
                    VividSlabLut.InvalidateSource();
                    return;
                }
            }
        }
    }
}
