using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.Scripting.LifecycleManagement;

namespace VividRP.Editor
{
    internal static class StandardLitRMOTexturePacker
    {
        internal const string PackingShaderName = "Hidden/VividRP/Editor/StandardLit RMO Texture Packer";
        internal const string OutputImporterUserDataPrefix = "VividRP.RMO.v1|";
        private const float SmoothnessFromAlbedoThreshold = 0.5f;

        [NoAutoStaticsCleanup]
        private static readonly Dictionary<string, string> PendingOutputImports =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly struct ChannelSource
        {
            internal ChannelSource(
                Texture texture,
                int channel,
                bool invert,
                float scale,
                float fallback,
                bool treatAsDataTexture)
            {
                Texture = texture;
                Channel = channel;
                Invert = invert;
                Scale = scale;
                Fallback = fallback;
                TreatAsDataTexture = treatAsDataTexture;
            }

            internal Texture Texture { get; }
            internal int Channel { get; }
            internal bool Invert { get; }
            internal float Scale { get; }
            internal float Fallback { get; }
            internal bool TreatAsDataTexture { get; }
        }

        internal static Texture2D Pack(
            Texture roughnessMap,
            Texture metallicMap,
            Texture ambientOcclusionMap,
            float roughnessFallback,
            float metallicFallback,
            float ambientOcclusionFallback)
        {
            return Pack(
                new ChannelSource(roughnessMap, 0, false, 1.0f, roughnessFallback, true),
                new ChannelSource(metallicMap, 0, false, 1.0f, metallicFallback, true),
                new ChannelSource(ambientOcclusionMap, 0, false, 1.0f, ambientOcclusionFallback, true));
        }

        internal static bool CanPackMaterial(Material material)
        {
            if (material == null || !material.HasProperty("_RMOMap"))
            {
                return false;
            }

            if (GetTexture(material, "_RoughnessMap") != null
                || GetTexture(material, "_MetallicGlossMap") != null
                || GetTexture(material, "_OcclusionMap") != null)
            {
                return true;
            }

            return GetFloat(material, "_SmoothnessTextureChannel", 0.0f) > SmoothnessFromAlbedoThreshold
                && GetTexture(material, "_BaseMap") != null;
        }

        internal static Texture2D PackMaterial(Material material)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            ResolveMaterialSources(
                material,
                out ChannelSource roughnessSource,
                out ChannelSource metallicSource,
                out ChannelSource ambientOcclusionSource);
            return Pack(roughnessSource, metallicSource, ambientOcclusionSource);
        }

        private static Texture2D Pack(
            ChannelSource roughnessSource,
            ChannelSource metallicSource,
            ChannelSource ambientOcclusionSource)
        {
            ResolveOutputSize(
                roughnessSource.Texture,
                metallicSource.Texture,
                ambientOcclusionSource.Texture,
                out int width,
                out int height);

            Shader packingShader = Shader.Find(PackingShaderName);
            if (packingShader == null)
            {
                throw new InvalidOperationException(
                    $"Could not find the RMO packing shader '{PackingShaderName}'.");
            }

            Material packingMaterial = null;
            RenderTexture renderTexture = null;
            Texture2D packedTexture = null;
            RenderTexture previousRenderTexture = RenderTexture.active;
            bool previousSRGBWrite = GL.sRGBWrite;

            try
            {
                packingMaterial = new Material(packingShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                ConfigureSource(packingMaterial, "_Roughness", roughnessSource);
                ConfigureSource(packingMaterial, "_Metallic", metallicSource);
                ConfigureSource(packingMaterial, "_AmbientOcclusion", ambientOcclusionSource);

                renderTexture = RenderTexture.GetTemporary(
                    width,
                    height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear);
                GL.sRGBWrite = false;
                Graphics.Blit(Texture2D.whiteTexture, renderTexture, packingMaterial, 0);

                RenderTexture.active = renderTexture;
                packedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                {
                    name = "RMO",
                };
                packedTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                packedTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                return packedTexture;
            }
            catch
            {
                if (packedTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(packedTexture);
                }

                throw;
            }
            finally
            {
                GL.sRGBWrite = previousSRGBWrite;
                RenderTexture.active = previousRenderTexture;
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }

                if (packingMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(packingMaterial);
                }
            }
        }

        private static void ConfigureSource(
            Material packingMaterial,
            string propertyPrefix,
            ChannelSource source)
        {
            packingMaterial.SetTexture(
                propertyPrefix + "Map",
                source.Texture != null ? source.Texture : Texture2D.whiteTexture);
            packingMaterial.SetVector(propertyPrefix + "ChannelMask", GetChannelMask(source.Channel));
            packingMaterial.SetVector(
                propertyPrefix + "Transform",
                new Vector4(
                    source.Scale,
                    source.Invert ? 1.0f : 0.0f,
                    Mathf.Clamp01(source.Fallback),
                    source.Texture != null ? 1.0f : 0.0f));
            packingMaterial.SetFloat(
                propertyPrefix + "UndoSRGB",
                NeedsSRGBCompensation(source) ? 1.0f : 0.0f);
        }

        private static bool NeedsSRGBCompensation(ChannelSource source)
        {
            if (!source.TreatAsDataTexture || source.Texture == null || source.Channel == 3)
            {
                return false;
            }

            return QualitySettings.activeColorSpace == ColorSpace.Linear
                && source.Texture.isDataSRGB;
        }

        private static Vector4 GetChannelMask(int channel)
        {
            return channel switch
            {
                1 => new Vector4(0.0f, 1.0f, 0.0f, 0.0f),
                2 => new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
                3 => new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                _ => new Vector4(1.0f, 0.0f, 0.0f, 0.0f),
            };
        }

        internal static bool TryPackToAsset(
            string assetPath,
            Texture roughnessMap,
            Texture metallicMap,
            Texture ambientOcclusionMap,
            float roughnessFallback,
            float metallicFallback,
            float ambientOcclusionFallback,
            string importerUserData,
            out Texture2D packedTexture,
            out string errorMessage)
        {
            return TryPackSourcesToAsset(
                assetPath,
                new ChannelSource(roughnessMap, 0, false, 1.0f, roughnessFallback, true),
                new ChannelSource(metallicMap, 0, false, 1.0f, metallicFallback, true),
                new ChannelSource(ambientOcclusionMap, 0, false, 1.0f, ambientOcclusionFallback, true),
                importerUserData,
                out packedTexture,
                out errorMessage);
        }

        internal static bool TryPackMaterialToAsset(
            string assetPath,
            Material material,
            out Texture2D packedTexture,
            out string errorMessage)
        {
            if (material == null)
            {
                packedTexture = null;
                errorMessage = "A material is required to pack an RMO texture.";
                return false;
            }

            ResolveMaterialSources(
                material,
                out ChannelSource roughnessSource,
                out ChannelSource metallicSource,
                out ChannelSource ambientOcclusionSource);
            return TryPackSourcesToAsset(
                assetPath,
                roughnessSource,
                metallicSource,
                ambientOcclusionSource,
                string.Empty,
                out packedTexture,
                out errorMessage);
        }

        private static bool TryPackSourcesToAsset(
            string assetPath,
            ChannelSource roughnessSource,
            ChannelSource metallicSource,
            ChannelSource ambientOcclusionSource,
            string importerUserData,
            out Texture2D packedTexture,
            out string errorMessage)
        {
            packedTexture = null;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetExtension(assetPath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "The RMO output must be a PNG inside the project's Assets folder.";
                return false;
            }

            byte[] pngData;
            Texture2D generatedTexture = null;
            try
            {
                generatedTexture = Pack(
                    roughnessSource,
                    metallicSource,
                    ambientOcclusionSource);
                pngData = generatedTexture.EncodeToPNG();
            }
            catch (Exception exception)
            {
                errorMessage = exception.Message;
                return false;
            }
            finally
            {
                if (generatedTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatedTexture);
                }
            }

            if (string.IsNullOrEmpty(importerUserData))
            {
                importerUserData = OutputImporterUserDataPrefix + "manual";
            }

            PendingOutputImports[assetPath] = importerUserData;
            try
            {
                if (AssetImporter.GetAtPath(assetPath) is TextureImporter existingTextureImporter)
                {
                    ConfigureOutputImporter(existingTextureImporter, importerUserData);
                    AssetDatabase.WriteImportSettingsIfDirty(assetPath);
                }

                string absolutePath = GetAbsoluteAssetPath(assetPath);
                string directoryPath = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                File.WriteAllBytes(absolutePath, pngData);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                if (AssetImporter.GetAtPath(assetPath) is not TextureImporter textureImporter)
                {
                    errorMessage = $"Unity did not create a texture importer for '{assetPath}'.";
                    return false;
                }

                if (!HasOutputImporterSettings(textureImporter, importerUserData))
                {
                    ConfigureOutputImporter(textureImporter, importerUserData);
                    textureImporter.SaveAndReimport();
                }

                packedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (packedTexture != null)
                {
                    return true;
                }

                errorMessage = $"Unity could not load the generated RMO texture at '{assetPath}'.";
                return false;
            }
            catch (Exception exception)
            {
                errorMessage = exception.Message;
                return false;
            }
            finally
            {
                PendingOutputImports.Remove(assetPath);
            }
        }

        internal static bool IsRMOOutputAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            if (PendingOutputImports.ContainsKey(assetPath))
            {
                return true;
            }

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            if (string.Equals(fileName, "RMO", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith("_RMO", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return AssetImporter.GetAtPath(assetPath) is TextureImporter textureImporter
                && !string.IsNullOrEmpty(textureImporter.userData)
                && textureImporter.userData.StartsWith(
                    OutputImporterUserDataPrefix,
                    StringComparison.Ordinal);
        }

        internal static void ApplyPendingOutputImporterSettings(
            string assetPath,
            TextureImporter textureImporter)
        {
            if (textureImporter != null
                && PendingOutputImports.TryGetValue(assetPath, out string importerUserData))
            {
                ConfigureOutputImporter(textureImporter, importerUserData);
            }
        }

        private static void ConfigureOutputImporter(
            TextureImporter textureImporter,
            string importerUserData)
        {
            textureImporter.sRGBTexture = false;
            textureImporter.alphaSource = TextureImporterAlphaSource.None;
            textureImporter.wrapMode = TextureWrapMode.Repeat;
            textureImporter.filterMode = FilterMode.Trilinear;
            textureImporter.mipmapEnabled = true;
            textureImporter.userData = importerUserData;
        }

        private static bool HasOutputImporterSettings(
            TextureImporter textureImporter,
            string importerUserData)
        {
            return !textureImporter.sRGBTexture
                && textureImporter.alphaSource == TextureImporterAlphaSource.None
                && textureImporter.wrapMode == TextureWrapMode.Repeat
                && textureImporter.filterMode == FilterMode.Trilinear
                && textureImporter.mipmapEnabled
                && string.Equals(textureImporter.userData, importerUserData, StringComparison.Ordinal);
        }

        private static void ResolveMaterialSources(
            Material material,
            out ChannelSource roughnessSource,
            out ChannelSource metallicSource,
            out ChannelSource ambientOcclusionSource)
        {
            Texture roughnessMap = GetTexture(material, "_RoughnessMap");
            Texture metallicMap = GetTexture(material, "_MetallicGlossMap");
            Texture ambientOcclusionMap = GetTexture(material, "_OcclusionMap");
            float roughnessFallback = 1.0f - GetFloat(material, "_Smoothness", 0.5f);

            if (roughnessMap != null)
            {
                roughnessSource = new ChannelSource(
                    roughnessMap, 0, false, 1.0f, roughnessFallback, true);
            }
            else if (GetFloat(material, "_SmoothnessTextureChannel", 0.0f)
                     > SmoothnessFromAlbedoThreshold)
            {
                float baseAlpha = material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor").a
                    : 1.0f;
                roughnessSource = new ChannelSource(
                    GetTexture(material, "_BaseMap"),
                    3,
                    true,
                    baseAlpha,
                    1.0f - baseAlpha,
                    false);
            }
            else if (metallicMap != null)
            {
                roughnessSource = new ChannelSource(
                    metallicMap, 3, true, 1.0f, roughnessFallback, true);
            }
            else
            {
                roughnessSource = new ChannelSource(
                    null, 0, false, 1.0f, roughnessFallback, false);
            }

            metallicSource = new ChannelSource(
                metallicMap,
                0,
                false,
                1.0f,
                GetFloat(material, "_Metallic", 0.0f),
                true);
            ambientOcclusionSource = new ChannelSource(
                ambientOcclusionMap, 1, false, 1.0f, 1.0f, true);
        }

        private static Texture GetTexture(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTexture(propertyName) : null;
        }

        private static float GetFloat(Material material, string propertyName, float fallback)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
        }

        private static void ResolveOutputSize(
            Texture roughnessMap,
            Texture metallicMap,
            Texture ambientOcclusionMap,
            out int width,
            out int height)
        {
            width = 1;
            height = 1;
            IncludeTextureSize(roughnessMap, ref width, ref height);
            IncludeTextureSize(metallicMap, ref width, ref height);
            IncludeTextureSize(ambientOcclusionMap, ref width, ref height);
        }

        private static void IncludeTextureSize(Texture texture, ref int width, ref int height)
        {
            if (texture == null)
            {
                return;
            }

            width = Mathf.Max(width, texture.width);
            height = Mathf.Max(height, texture.height);
        }

        private static string GetAbsoluteAssetPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

    }

    internal sealed class StandardLitRMOTexturePostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            StandardLitRMOTexturePacker.ApplyPendingOutputImporterSettings(
                assetPath,
                assetImporter as TextureImporter);
        }
    }

    internal static class StandardLitRMOAutoPacker
    {
        private const string GeneratedFolderName = "VividRPGenerated";
        private const string ImporterUserDataPrefix =
            StandardLitRMOTexturePacker.OutputImporterUserDataPrefix;

        [NoAutoStaticsCleanup]
        private static readonly Dictionary<string, PackRequest> PendingRequests =
            new Dictionary<string, PackRequest>(StringComparer.OrdinalIgnoreCase);

        [NoAutoStaticsCleanup]
        private static bool s_DelayCallRegistered;
        [NoAutoStaticsCleanup]
        private static bool s_IsProcessing;

        internal static void BindOrSchedule(
            string modelAssetPath,
            IImportedMaterialDescription description,
            Material material)
        {
            if (description == null
                || material == null
                || string.IsNullOrEmpty(modelAssetPath)
                || !modelAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            description.TryGetTexture("roughness_map", out ImportedTextureProperty roughnessProperty);
            description.TryGetTexture("metalness_map", out ImportedTextureProperty metallicProperty);
            description.TryGetTexture("ao_map", out ImportedTextureProperty ambientOcclusionProperty);

            if (!roughnessProperty.IsAssigned
                && !metallicProperty.IsAssigned
                && !ambientOcclusionProperty.IsAssigned)
            {
                return;
            }

            var request = new PackRequest(
                modelAssetPath,
                GetGeneratedAssetPath(modelAssetPath, material.name),
                roughnessProperty.Texture,
                metallicProperty.Texture,
                ambientOcclusionProperty.Texture,
                1.0f - GetFloat(material, "_Smoothness", 0.5f),
                GetFloat(material, "_Metallic", 0.0f),
                1.0f);

            string fingerprint = request.GetFingerprint();
            if (TryLoadGeneratedTexture(request.OutputAssetPath, fingerprint, out Texture2D packedTexture))
            {
                Bind(material, packedTexture);
                return;
            }

            if (s_IsProcessing)
            {
                return;
            }

            PendingRequests[request.OutputAssetPath] = request;
            if (s_DelayCallRegistered)
            {
                return;
            }

            s_DelayCallRegistered = true;
            EditorApplication.delayCall += ProcessPendingRequests;
        }

        internal static string GetGeneratedAssetPath(string modelAssetPath, string materialName)
        {
            string modelDirectory = Path.GetDirectoryName(modelAssetPath)?.Replace('\\', '/');
            string modelName = Path.GetFileNameWithoutExtension(modelAssetPath);
            string safeMaterialName = SanitizeFileName(materialName);
            return $"{modelDirectory}/{GeneratedFolderName}/{modelName}_{safeMaterialName}_RMO.png";
        }

        private static void ProcessPendingRequests()
        {
            s_DelayCallRegistered = false;
            if (s_IsProcessing || PendingRequests.Count == 0)
            {
                return;
            }

            s_IsProcessing = true;
            var requests = new List<PackRequest>(PendingRequests.Values);
            PendingRequests.Clear();
            var modelPathsToReimport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (PackRequest request in requests)
                {
                    string fingerprint = request.GetFingerprint();
                    if (!TryLoadGeneratedTexture(request.OutputAssetPath, fingerprint, out _)
                        && !StandardLitRMOTexturePacker.TryPackToAsset(
                            request.OutputAssetPath,
                            request.RoughnessMap,
                            request.MetallicMap,
                            request.AmbientOcclusionMap,
                            request.RoughnessFallback,
                            request.MetallicFallback,
                            request.AmbientOcclusionFallback,
                            fingerprint,
                            out _,
                            out string errorMessage))
                    {
                        Debug.LogError(
                            $"VividRP StandardLit could not generate '{request.OutputAssetPath}': {errorMessage}");
                        continue;
                    }

                    modelPathsToReimport.Add(request.ModelAssetPath);
                }

                foreach (string modelPath in modelPathsToReimport)
                {
                    AssetDatabase.ImportAsset(
                        modelPath,
                        ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                s_IsProcessing = false;
                if (PendingRequests.Count > 0 && !s_DelayCallRegistered)
                {
                    s_DelayCallRegistered = true;
                    EditorApplication.delayCall += ProcessPendingRequests;
                }
            }
        }

        private static bool TryLoadGeneratedTexture(
            string assetPath,
            string fingerprint,
            out Texture2D packedTexture)
        {
            packedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            return packedTexture != null
                && AssetImporter.GetAtPath(assetPath) is TextureImporter textureImporter
                && string.Equals(textureImporter.userData, fingerprint, StringComparison.Ordinal);
        }

        private static void Bind(Material material, Texture2D packedTexture)
        {
            material.SetTexture("_RMOMap", packedTexture);
            material.SetTexture("_MetallicGlossMap", null);
            material.SetTexture("_RoughnessMap", null);
            material.SetTexture("_OcclusionMap", null);
            StandardLitMaterialUtility.SetupMaterial(material, null, false);
        }

        private static float GetFloat(Material material, string propertyName, float fallback)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
        }

        private static string SanitizeFileName(string value)
        {
            string sanitized = string.IsNullOrWhiteSpace(value) ? "Material" : value.Trim();
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalidCharacter, '_');
            }

            return sanitized;
        }

        private sealed class PackRequest
        {
            internal PackRequest(
                string modelAssetPath,
                string outputAssetPath,
                Texture roughnessMap,
                Texture metallicMap,
                Texture ambientOcclusionMap,
                float roughnessFallback,
                float metallicFallback,
                float ambientOcclusionFallback)
            {
                ModelAssetPath = modelAssetPath;
                OutputAssetPath = outputAssetPath;
                RoughnessMap = roughnessMap;
                MetallicMap = metallicMap;
                AmbientOcclusionMap = ambientOcclusionMap;
                RoughnessFallback = Mathf.Clamp01(roughnessFallback);
                MetallicFallback = Mathf.Clamp01(metallicFallback);
                AmbientOcclusionFallback = Mathf.Clamp01(ambientOcclusionFallback);
            }

            internal string ModelAssetPath { get; }
            internal string OutputAssetPath { get; }
            internal Texture RoughnessMap { get; }
            internal Texture MetallicMap { get; }
            internal Texture AmbientOcclusionMap { get; }
            internal float RoughnessFallback { get; }
            internal float MetallicFallback { get; }
            internal float AmbientOcclusionFallback { get; }

            internal string GetFingerprint()
            {
                return string.Concat(
                    ImporterUserDataPrefix,
                    GetTextureFingerprint(RoughnessMap), "|",
                    GetTextureFingerprint(MetallicMap), "|",
                    GetTextureFingerprint(AmbientOcclusionMap), "|",
                    RoughnessFallback.ToString("R", CultureInfo.InvariantCulture), "|",
                    MetallicFallback.ToString("R", CultureInfo.InvariantCulture), "|",
                    AmbientOcclusionFallback.ToString("R", CultureInfo.InvariantCulture));
            }

            private static string GetTextureFingerprint(Texture texture)
            {
                if (texture == null)
                {
                    return "none";
                }

                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    texture,
                    out string guid,
                    out long localId))
                {
                    return $"{guid}:{localId}:{texture.imageContentsHash}";
                }

                return $"memory:{texture.name}:{texture.imageContentsHash}";
            }
        }
    }

    public sealed class StandardLitRMOTexturePackerWindow : EditorWindow
    {
        private Texture2D m_RoughnessMap;
        private Texture2D m_MetallicMap;
        private Texture2D m_AmbientOcclusionMap;
        private float m_RoughnessFallback = 0.5f;
        private float m_MetallicFallback;
        private float m_AmbientOcclusionFallback = 1.0f;

        [MenuItem("Tools/VividRP/Material/RMO Texture Packer")]
        private static void Open()
        {
            GetWindow<StandardLitRMOTexturePackerWindow>("RMO Texture Packer");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Packs each source texture's red channel into R = Roughness, G = Metallic, B = AO. "
                + "Unassigned channels use the fallback values below.",
                MessageType.Info);

            m_RoughnessMap = (Texture2D)EditorGUILayout.ObjectField(
                "Roughness (R)", m_RoughnessMap, typeof(Texture2D), false);
            m_RoughnessFallback = EditorGUILayout.Slider(
                "Roughness Fallback", m_RoughnessFallback, 0.0f, 1.0f);

            m_MetallicMap = (Texture2D)EditorGUILayout.ObjectField(
                "Metallic (R)", m_MetallicMap, typeof(Texture2D), false);
            m_MetallicFallback = EditorGUILayout.Slider(
                "Metallic Fallback", m_MetallicFallback, 0.0f, 1.0f);

            m_AmbientOcclusionMap = (Texture2D)EditorGUILayout.ObjectField(
                "Ambient Occlusion (R)", m_AmbientOcclusionMap, typeof(Texture2D), false);
            m_AmbientOcclusionFallback = EditorGUILayout.Slider(
                "AO Fallback", m_AmbientOcclusionFallback, 0.0f, 1.0f);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(
                m_RoughnessMap == null && m_MetallicMap == null && m_AmbientOcclusionMap == null))
            {
                if (GUILayout.Button("Pack RMO Texture"))
                {
                    PackSelectedTextures();
                }
            }
        }

        private void PackSelectedTextures()
        {
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Save RMO Texture",
                "RMO",
                "png",
                "Choose where to save the packed RMO texture.");
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            if (!StandardLitRMOTexturePacker.TryPackToAsset(
                assetPath,
                m_RoughnessMap,
                m_MetallicMap,
                m_AmbientOcclusionMap,
                m_RoughnessFallback,
                m_MetallicFallback,
                m_AmbientOcclusionFallback,
                string.Empty,
                out Texture2D packedTexture,
                out string errorMessage))
            {
                EditorUtility.DisplayDialog("RMO Texture Packer", errorMessage, "OK");
                return;
            }

            Selection.activeObject = packedTexture;
            EditorGUIUtility.PingObject(packedTexture);
        }
    }
}
