using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    /// <summary>
    /// Imports binary .vividhair files exported from Houdini 22 / DCC tools directly
    /// into a lightweight Unity HairGroomAsset without YAML serialization overhead.
    /// </summary>
    [ScriptedImporter(1, "vividhair")]
    public sealed class HairGroomImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            byte[] bytes = File.ReadAllBytes(ctx.assetPath);
            var asset = ScriptableObject.CreateInstance<HairGroomAsset>();
            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            asset.ImportFromBinary(bytes);

            ctx.AddObjectToAsset("HairGroom", asset);
            ctx.SetMainObject(asset);
        }
    }
}
