using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [ScriptedImporter(1, "openpbrlut")]
    internal sealed class VividOpenPBRLutImporter : ScriptedImporter
    {
        internal const string DataDirectory = "Shaders/Material/ShaderPass/OpenPBR/Vendor/impl/data/";
        internal static readonly string[] DataFiles =
        {
            "openpbr_ideal_dielectric_energy_complement_data.h",
            "openpbr_ideal_dielectric_avg_energy_complement_data.h",
            "openpbr_ideal_dielectric_reflection_ratio_data.h",
            "openpbr_opaque_dielectric_energy_complement_data.h",
            "openpbr_opaque_dielectric_avg_energy_complement_data.h",
            "openpbr_ideal_metal_energy_complement_data.h",
            "openpbr_ideal_metal_avg_energy_complement_data.h",
            "openpbr_ltc_data.h"
        };

        public override void OnImportAsset(AssetImportContext ctx)
        {
            if (File.ReadAllText(ctx.assetPath).Trim() != "OpenPBR_LUTs_v1")
                throw new InvalidDataException("Unsupported OpenPBR LUT source version.");
            var tables = new float[8][];
            for (int i = 0; i < tables.Length; ++i)
            {
                string path = VividPackagePathUtility.GetPreferredAssetPath(DataDirectory + DataFiles[i]);
                ctx.DependsOnSourceAsset(path);
                tables[i] = ReadTable(path, i);
            }
            var ideal = CreateVolume("OpenPBRIdealDielectricEnergy", tables[0]);
            var opaque = CreateVolume("OpenPBROpaqueDielectricEnergy", tables[3]);
            var array = new Texture2DArray(32, 32, 6, TextureFormat.RGBAFloat, false, true)
            { name = "OpenPBRLuts2D", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 };
            for (int layer = 0; layer < 6; ++layer)
            {
                int id = layer < 2 ? layer + 1 : layer + 2;
                var pixels = new float[32 * 32 * 4];
                for (int p = 0; p < 32 * 32; ++p)
                    if (id == 7)
                    {
                        for (int c = 0; c < 3; ++c) pixels[p * 4 + c] = tables[id][p * 3 + c];
                    }
                    else pixels[p * 4] = tables[id][id == 6 ? p % 32 : p];
                array.SetPixelData(pixels, 0, layer);
            }
            array.Apply(false, true);
            var asset = ScriptableObject.CreateInstance<VividOpenPBRLuts>();
            asset.SetTextures(ideal, opaque, array);
            ctx.AddObjectToAsset("IdealDielectricEnergy", ideal);
            ctx.AddObjectToAsset("OpaqueDielectricEnergy", opaque);
            ctx.AddObjectToAsset("Tables2D", array);
            ctx.AddObjectToAsset("OpenPBRLuts", asset);
            ctx.SetMainObject(asset);
        }

        private static Texture3D CreateVolume(string name, float[] values)
        {
            var texture = new Texture3D(32, 32, 32, TextureFormat.RFloat, false)
            { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0 };
            // Vendor row-major order: cos(theta) fastest, alpha next, IOR last.
            texture.SetPixelData(values, 0);
            texture.Apply(false, true);
            return texture;
        }

        internal static float[] ReadTable(string path, int id)
        {
            string text = Regex.Replace(File.ReadAllText(path), @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
            if (id == 7) text = text.Replace("vec3", "");
            string[] tokens = text.Split(new[] { ',', '(', ')', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int expected = id == 0 || id == 3 ? 32768 : id == 6 ? 32 : id == 7 ? 3072 : 1024;
            if (tokens.Length != expected)
                throw new InvalidDataException($"{path}: expected {expected} table values, found {tokens.Length}.");
            var values = new float[expected];
            for (int i = 0; i < values.Length; ++i)
            {
                if (id == 7) values[i] = float.Parse(tokens[i], CultureInfo.InvariantCulture);
                else values[i] = ushort.Parse(tokens[i], CultureInfo.InvariantCulture) * (1.0f / 65535.0f);
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                    throw new InvalidDataException($"{path}: non-finite table value at {i}.");
            }
            return values;
        }
    }
}
