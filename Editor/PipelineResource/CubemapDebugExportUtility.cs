using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VividRP.Editor
{
    internal static class CubemapDebugExportUtility
    {
        private const string ExportMenuPath = "Assets/VividRP/Export Selected Cubemap Faces";

        private static readonly CubemapFace[] s_Faces =
        {
            CubemapFace.PositiveX,
            CubemapFace.NegativeX,
            CubemapFace.PositiveY,
            CubemapFace.NegativeY,
            CubemapFace.PositiveZ,
            CubemapFace.NegativeZ
        };

        [MenuItem(ExportMenuPath, false)]
        private static void ExportSelectedCubemap()
        {
            if (!TryGetSelectedCubemap(out var cubemap, out var assetPath))
                return;

            var exportDirectory = CreateExportDirectory(cubemap.name);
            ExportCubemap(cubemap, assetPath, exportDirectory);
            EditorUtility.RevealInFinder(exportDirectory);
            Debug.Log($"Exported cubemap debug faces for '{cubemap.name}' to '{exportDirectory}'.", cubemap);
        }

        [MenuItem(ExportMenuPath, true)]
        private static bool ValidateExportSelectedCubemap()
        {
            return TryGetSelectedCubemap(out _, out _);
        }

        internal static string CreateExportDirectory(string cubemapName)
        {
            var sanitizedName = string.IsNullOrWhiteSpace(cubemapName)
                ? "Cubemap"
                : MakeFileNameSafe(cubemapName);

            return Path.Combine(
                GetProjectRoot(),
                "Logs",
                "CubemapDebug",
                $"{sanitizedName}_{DateTime.Now:yyyyMMdd_HHmmss}");
        }

        internal static void ExportCubemap(Cubemap cubemap, string assetPath, string exportDirectory)
        {
            if (cubemap == null)
                throw new ArgumentNullException(nameof(cubemap));

            if (string.IsNullOrWhiteSpace(exportDirectory))
                throw new ArgumentException("Export directory must be provided.", nameof(exportDirectory));

            Directory.CreateDirectory(exportDirectory);

            var originalReadable = cubemap.isReadable;
            var importer = string.IsNullOrEmpty(assetPath) ? null : AssetImporter.GetAtPath(assetPath) as TextureImporter;

            try
            {
                cubemap = EnsureReadableCubemap(cubemap, assetPath, importer, originalReadable);

                var summary = new StringBuilder();
                summary.AppendLine($"AssetPath: {assetPath}");
                summary.AppendLine($"Name: {cubemap.name}");
                summary.AppendLine($"Size: {cubemap.width}x{cubemap.height}");
                summary.AppendLine($"MipCount: {cubemap.mipmapCount}");
                summary.AppendLine($"Format: {cubemap.format}");
                summary.AppendLine($"GraphicsFormat: {cubemap.graphicsFormat}");
                summary.AppendLine($"Readable: {cubemap.isReadable}");
                summary.AppendLine();

                for (var mip = 0; mip < cubemap.mipmapCount; mip++)
                {
                    var faceSize = Mathf.Max(1, cubemap.width >> mip);
                    for (var faceIndex = 0; faceIndex < s_Faces.Length; faceIndex++)
                    {
                        var face = s_Faces[faceIndex];
                        var pixels = cubemap.GetPixels(face, mip);
                        ExportFace(exportDirectory, face, mip, faceSize, pixels, summary);
                    }
                }

                File.WriteAllText(Path.Combine(exportDirectory, "summary.txt"), summary.ToString());
            }
            finally
            {
                RestoreImporterReadableState(assetPath, importer, originalReadable);
            }
        }

        private static Cubemap EnsureReadableCubemap(Cubemap cubemap, string assetPath, TextureImporter importer, bool originalReadable)
        {
            if (cubemap.isReadable)
                return cubemap;

            if (importer == null || string.IsNullOrEmpty(assetPath))
                throw new InvalidOperationException("Selected cubemap is not readable and cannot be reimported from an asset path.");

            importer.isReadable = true;
            importer.SaveAndReimport();

            var reloadedCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(assetPath);
            if (reloadedCubemap == null || !reloadedCubemap.isReadable)
            {
                RestoreImporterReadableState(assetPath, importer, originalReadable);
                throw new InvalidOperationException($"Failed to reload readable cubemap from '{assetPath}'.");
            }

            return reloadedCubemap;
        }

        private static void RestoreImporterReadableState(string assetPath, TextureImporter importer, bool originalReadable)
        {
            if (importer == null || string.IsNullOrEmpty(assetPath))
                return;

            var currentImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (currentImporter == null || currentImporter.isReadable == originalReadable)
                return;

            currentImporter.isReadable = originalReadable;
            currentImporter.SaveAndReimport();
        }

        private static void ExportFace(string exportDirectory, CubemapFace face, int mip, int faceSize, Color[] pixels, StringBuilder summary)
        {
            var faceName = face.ToString();
            var baseFileName = $"{faceName}_Mip{mip:D2}_{faceSize}x{faceSize}";

            using var hdrTexture = new TemporaryTexture2D(faceSize, faceSize, TextureFormat.RGBAFloat, true);
            hdrTexture.Texture.SetPixels(pixels);
            hdrTexture.Texture.Apply(false, false);

            using var previewTexture = new TemporaryTexture2D(faceSize, faceSize, TextureFormat.RGBA32, false);
            previewTexture.Texture.SetPixels(ConvertToPreviewPixels(pixels));
            previewTexture.Texture.Apply(false, false);

            File.WriteAllBytes(Path.Combine(exportDirectory, baseFileName + "_Preview.png"), previewTexture.Texture.EncodeToPNG());
            File.WriteAllBytes(
                Path.Combine(exportDirectory, baseFileName + ".exr"),
                hdrTexture.Texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));

            AppendFaceStats(summary, faceName, mip, faceSize, pixels);
        }

        private static void AppendFaceStats(StringBuilder summary, string faceName, int mip, int faceSize, Color[] pixels)
        {
            var minColor = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var maxColor = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            var sumColor = Vector3.zero;
            var nonFinitePixels = 0;
            var ldrWhitePixels = 0;

            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                if (!IsFinite(pixel.r) || !IsFinite(pixel.g) || !IsFinite(pixel.b))
                {
                    nonFinitePixels++;
                    continue;
                }

                var pixelRgb = new Vector3(pixel.r, pixel.g, pixel.b);
                minColor = Vector3.Min(minColor, pixelRgb);
                maxColor = Vector3.Max(maxColor, pixelRgb);
                sumColor += pixelRgb;

                if (pixel.r >= 0.999f && pixel.g >= 0.999f && pixel.b >= 0.999f)
                    ldrWhitePixels++;
            }

            var finitePixelCount = Mathf.Max(1, pixels.Length - nonFinitePixels);
            var averageColor = sumColor / finitePixelCount;

            summary.Append(faceName);
            summary.Append(" mip ");
            summary.Append(mip.ToString(CultureInfo.InvariantCulture));
            summary.Append(" size ");
            summary.Append(faceSize.ToString(CultureInfo.InvariantCulture));
            summary.Append(": min=");
            summary.Append(FormatVector3(minColor));
            summary.Append(" max=");
            summary.Append(FormatVector3(maxColor));
            summary.Append(" avg=");
            summary.Append(FormatVector3(averageColor));
            summary.Append(" nonFinite=");
            summary.Append(nonFinitePixels.ToString(CultureInfo.InvariantCulture));
            summary.Append(" ldrWhite=");
            summary.Append(ldrWhitePixels.ToString(CultureInfo.InvariantCulture));
            summary.AppendLine();
        }

        private static Color[] ConvertToPreviewPixels(Color[] pixels)
        {
            var previewPixels = new Color[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                previewPixels[i] = new Color(
                    Mathf.Clamp01(pixel.r),
                    Mathf.Clamp01(pixel.g),
                    Mathf.Clamp01(pixel.b),
                    1.0f);
            }

            return previewPixels;
        }

        private static string FormatVector3(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:0.####}, {1:0.####}, {2:0.####})",
                value.x,
                value.y,
                value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool TryGetSelectedCubemap(out Cubemap cubemap, out string assetPath)
        {
            cubemap = Selection.activeObject as Cubemap;
            assetPath = cubemap != null ? AssetDatabase.GetAssetPath(cubemap) : string.Empty;
            return cubemap != null;
        }

        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string MakeFileNameSafe(string name)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            for (var i = 0; i < name.Length; i++)
            {
                var character = name[i];
                builder.Append(Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character);
            }

            return builder.ToString();
        }

        private sealed class TemporaryTexture2D : IDisposable
        {
            internal TemporaryTexture2D(int width, int height, TextureFormat format, bool linear)
            {
                Texture = new Texture2D(width, height, format, false, linear)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            internal Texture2D Texture { get; }

            public void Dispose()
            {
                if (Texture != null)
                    UnityEngine.Object.DestroyImmediate(Texture);
            }
        }
    }
}
