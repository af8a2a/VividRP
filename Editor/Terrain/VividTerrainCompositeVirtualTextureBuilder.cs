using System;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using Object = UnityEngine.Object;

namespace VividRP.Editor.TerrainTools
{
    internal enum VividVirtualTextureImportSourceKind
    {
        TextureSet = 0,
        TerrainComposite = 1,
    }

    [Serializable]
    internal struct VividTerrainCompositeLayerSource
    {
        [SerializeField]
        private Texture2D m_BaseColor;

        [SerializeField]
        private Texture2D m_Normal;

        [SerializeField]
        private Texture2D m_Mask;

        [SerializeField]
        private Vector4 m_TextureTilingOffset;

        [SerializeField]
        private float m_NormalScale;

        [SerializeField]
        private float m_Metallic;

        [SerializeField]
        private float m_Smoothness;

        internal VividTerrainCompositeLayerSource(
            in VividTerrainLayerData layer,
            Vector3 terrainSize)
        {
            m_BaseColor = layer.DiffuseTexture;
            m_Normal = layer.NormalMapTexture;
            m_Mask = layer.MaskMapTexture;
            m_TextureTilingOffset = VividTerrainSurfaceUtility.GetLayerTilingOffset(
                terrainSize,
                layer.TileSize,
                layer.TileOffset);
            m_NormalScale = layer.NormalScale;
            m_Metallic = Mathf.Clamp01(layer.Metallic);
            m_Smoothness = Mathf.Clamp01(layer.Smoothness);
        }

        internal Texture2D BaseColor => m_BaseColor;

        internal Texture2D Normal => m_Normal;

        internal Texture2D Mask => m_Mask;

        internal Vector4 TextureTilingOffset => m_TextureTilingOffset;

        internal Vector4 MaterialParams => new(
            m_NormalScale,
            m_Metallic,
            m_Smoothness,
            0.0f);

        internal int PresenceMask => (m_BaseColor != null ? 1 : 0)
                                     | (m_Normal != null ? 2 : 0)
                                     | (m_Mask != null ? 4 : 0);
    }

    [Serializable]
    internal struct VividTerrainCompositeSource
    {
        internal const int DefaultMaxResolution = 4096;
        internal const int MinimumResolution = 128;
        internal const int MaximumResolution = 4096;

        [SerializeField]
        private string m_SourceTerrainDataGUID;

        [SerializeField]
        private Vector2 m_TerrainSize;

        [SerializeField]
        private int m_MaxResolution;

        [SerializeField]
        private Vector2Int m_OutputResolution;

        [SerializeField]
        private VividTerrainCompositeLayerSource[] m_Layers;

        [SerializeField]
        private Texture2D[] m_ControlMaps;

        internal VividTerrainCompositeSource(
            string sourceTerrainDataGUID,
            Vector3 terrainSize,
            int maxResolution,
            VividTerrainCompositeLayerSource[] layers,
            Texture2D[] controlMaps)
        {
            m_SourceTerrainDataGUID = sourceTerrainDataGUID ?? string.Empty;
            m_TerrainSize = new Vector2(terrainSize.x, terrainSize.z);
            m_MaxResolution = Mathf.Clamp(
                maxResolution > 0 ? maxResolution : DefaultMaxResolution,
                MinimumResolution,
                MaximumResolution);
            m_OutputResolution = CalculateOutputResolution(m_TerrainSize, m_MaxResolution);
            m_Layers = layers ?? Array.Empty<VividTerrainCompositeLayerSource>();
            m_ControlMaps = controlMaps ?? Array.Empty<Texture2D>();
        }

        internal string SourceTerrainDataGUID => m_SourceTerrainDataGUID ?? string.Empty;

        internal Vector2 TerrainSize => m_TerrainSize;

        internal int MaxResolution => Mathf.Clamp(
            m_MaxResolution > 0 ? m_MaxResolution : DefaultMaxResolution,
            MinimumResolution,
            MaximumResolution);

        internal Vector2Int OutputResolution => m_OutputResolution.x > 0 && m_OutputResolution.y > 0
            ? m_OutputResolution
            : CalculateOutputResolution(TerrainSize, MaxResolution);

        internal VividTerrainCompositeLayerSource[] Layers =>
            m_Layers ?? Array.Empty<VividTerrainCompositeLayerSource>();

        internal Texture2D[] ControlMaps => m_ControlMaps ?? Array.Empty<Texture2D>();

        internal bool IsValid => Layers.Length is > 1 and <= VividTerrainData.MaximumSurfaceLayerCount;

        private static Vector2Int CalculateOutputResolution(Vector2 terrainSize, int maxResolution)
        {
            const int pageSize = 128;
            float sizeX = Mathf.Max(Mathf.Abs(terrainSize.x), Mathf.Epsilon);
            float sizeY = Mathf.Max(Mathf.Abs(terrainSize.y), Mathf.Epsilon);
            float longestSize = Mathf.Max(sizeX, sizeY);
            int targetWidth = Mathf.Max(1, Mathf.CeilToInt(maxResolution * sizeX / longestSize));
            int targetHeight = Mathf.Max(1, Mathf.CeilToInt(maxResolution * sizeY / longestSize));
            int maxPageCount = MaximumResolution / pageSize;
            int pageCountX = Mathf.Clamp(
                Mathf.NextPowerOfTwo(Mathf.CeilToInt(targetWidth / (float) pageSize)),
                1,
                maxPageCount);
            int pageCountY = Mathf.Clamp(
                Mathf.NextPowerOfTwo(Mathf.CeilToInt(targetHeight / (float) pageSize)),
                1,
                maxPageCount);
            return new Vector2Int(pageCountX * pageSize, pageCountY * pageSize);
        }
    }

    internal sealed class VividTerrainCompositeTextureSet : IDisposable
    {
        internal VividTerrainCompositeTextureSet(
            Texture2D baseColor,
            Texture2D normal,
            Texture2D mask)
        {
            BaseColor = baseColor;
            Normal = normal;
            Mask = mask;
        }

        internal Texture2D BaseColor { get; }

        internal Texture2D Normal { get; }

        internal Texture2D Mask { get; }

        public void Dispose()
        {
            Destroy(BaseColor);
            Destroy(Normal);
            Destroy(Mask);
        }

        private static void Destroy(Object target)
        {
            if (target != null)
                Object.DestroyImmediate(target);
        }
    }

    internal static class VividTerrainCompositeVirtualTextureBuilder
    {
        internal const string ComputeRelativePath =
            "Shaders/Core/Private/GPUDriven/TerrainCompositeBake.compute";
        private const int PageSize = 128;
        private const int ThreadGroupSize = 8;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int NormalId = Shader.PropertyToID("_Normal");
        private static readonly int MaskId = Shader.PropertyToID("_Mask");
        private static readonly int Control0Id = Shader.PropertyToID("_Control0");
        private static readonly int Control1Id = Shader.PropertyToID("_Control1");
        private static readonly int LayerTilingOffsetId = Shader.PropertyToID("_LayerTilingOffset");
        private static readonly int LayerMaterialParamsId = Shader.PropertyToID("_LayerMaterialParams");
        private static readonly int LayerPresenceId = Shader.PropertyToID("_LayerPresence");
        private static readonly int LayerCountId = Shader.PropertyToID("_LayerCount");
        private static readonly int LayerIndexId = Shader.PropertyToID("_LayerIndex");
        private static readonly int ControlCountId = Shader.PropertyToID("_ControlCount");
        private static readonly int OutputDimensionsId = Shader.PropertyToID("_OutputDimensions");
        private static readonly int AccumulatedBaseColorId = Shader.PropertyToID("_AccumulatedBaseColor");
        private static readonly int AccumulatedNormalId = Shader.PropertyToID("_AccumulatedNormal");
        private static readonly int AccumulatedMaskId = Shader.PropertyToID("_AccumulatedMask");
        private static readonly int OutputBaseColorId = Shader.PropertyToID("_OutputBaseColor");
        private static readonly int OutputNormalId = Shader.PropertyToID("_OutputNormal");
        private static readonly int OutputMaskId = Shader.PropertyToID("_OutputMask");

        internal static Action<float, string> ProgressHandler { get; set; }

        internal static VividTerrainCompositeTextureSet Generate(
            in VividTerrainCompositeSource source)
        {
            if (!source.IsValid)
                throw new ArgumentException("A terrain composite requires two to eight layers.", nameof(source));
            if (!SystemInfo.supportsComputeShaders)
                throw new InvalidOperationException("Terrain Composite SVT baking requires compute shader support.");
            if (!SystemInfo.supportsAsyncGPUReadback)
                throw new InvalidOperationException("Terrain Composite SVT baking requires asynchronous GPU readback support.");

            ComputeShader compute = LoadComputeShader();
            if (compute == null)
            {
                throw new InvalidOperationException(
                    $"Missing terrain composite compute shader '{ComputeRelativePath}'.");
            }

            ResolveDimensions(source, out int width, out int height, out int virtualMipCount);
            Texture2D baseColor = CreateOutputTexture(width, height, linear: false, "TerrainComposite_BaseColor");
            Texture2D normal = CreateOutputTexture(width, height, linear: true, "TerrainComposite_Normal");
            Texture2D mask = CreateOutputTexture(width, height, linear: true, "TerrainComposite_Mask");
            bool success = false;
            try
            {
                int accumulateKernel = compute.FindKernel("CSAccumulate");
                int finalizeKernel = compute.FindKernel("CSFinalize");
                BindSource(compute, accumulateKernel, source);
                for (int mip = 0; mip < virtualMipCount; mip++)
                {
                    int mipWidth = Mathf.Max(1, width >> mip);
                    int mipHeight = Mathf.Max(1, height >> mip);
                    ProgressHandler?.Invoke(
                        mip / (float) virtualMipCount,
                        $"Compositing terrain surface mip {mip + 1}/{virtualMipCount}");
                    GenerateMip(
                        compute,
                        accumulateKernel,
                        finalizeKernel,
                        source.Layers,
                        mip,
                        mipWidth,
                        mipHeight,
                        baseColor,
                        normal,
                        mask);
                }

                baseColor.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                normal.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                mask.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                ProgressHandler?.Invoke(1.0f, "Terrain surface composite complete");
                success = true;
                return new VividTerrainCompositeTextureSet(baseColor, normal, mask);
            }
            finally
            {
                if (!success)
                {
                    Object.DestroyImmediate(baseColor);
                    Object.DestroyImmediate(normal);
                    Object.DestroyImmediate(mask);
                }
            }
        }

        internal static void ResolveDimensions(
            in VividTerrainCompositeSource source,
            out int width,
            out int height,
            out int mipCount)
        {
            Vector2Int outputResolution = source.OutputResolution;
            width = outputResolution.x;
            height = outputResolution.y;
            int pageCountX = width / PageSize;
            int pageCountY = height / PageSize;
            mipCount = Mathf.FloorToInt(Mathf.Log(Mathf.Max(pageCountX, pageCountY), 2.0f)) + 1;
        }

        private static Texture2D CreateOutputTexture(int width, int height, bool linear, string name)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true, linear: linear)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }

        private static void BindSource(
            ComputeShader compute,
            int kernel,
            in VividTerrainCompositeSource source)
        {
            VividTerrainCompositeLayerSource[] layers = source.Layers;
            Texture2D[] controlMaps = source.ControlMaps;
            int requiredControlCount = Mathf.Min(
                Mathf.CeilToInt(layers.Length / 4.0f),
                VividTerrainData.MaximumControlMapCount);
            Texture2D control0 = controlMaps.Length > 0 ? controlMaps[0] : null;
            Texture2D control1 = controlMaps.Length > 1 ? controlMaps[1] : null;
            compute.SetTexture(kernel, Control0Id, control0 != null ? control0 : Texture2D.blackTexture);
            compute.SetTexture(kernel, Control1Id, control1 != null ? control1 : Texture2D.blackTexture);
            compute.SetInt(LayerCountId, layers.Length);
            compute.SetInt(ControlCountId, requiredControlCount);
        }

        private static void GenerateMip(
            ComputeShader compute,
            int accumulateKernel,
            int finalizeKernel,
            VividTerrainCompositeLayerSource[] layers,
            int mip,
            int width,
            int height,
            Texture2D baseColor,
            Texture2D normal,
            Texture2D mask)
        {
            RenderTexture accumulatedBaseColor = null;
            RenderTexture accumulatedNormal = null;
            RenderTexture accumulatedMask = null;
            RenderTexture baseColorOutput = null;
            RenderTexture normalOutput = null;
            RenderTexture maskOutput = null;
            try
            {
                accumulatedBaseColor = CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    "TerrainComposite_AccumulatedBaseColor");
                accumulatedNormal = CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    "TerrainComposite_AccumulatedNormal");
                accumulatedMask = CreateRenderTexture(
                    width,
                    height,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    "TerrainComposite_AccumulatedMask");
                baseColorOutput = CreateOutputRenderTexture(
                    width,
                    height,
                    "TerrainComposite_BaseColorOutput");
                normalOutput = CreateOutputRenderTexture(
                    width,
                    height,
                    "TerrainComposite_NormalOutput");
                maskOutput = CreateOutputRenderTexture(
                    width,
                    height,
                    "TerrainComposite_MaskOutput");
                compute.SetVector(
                    OutputDimensionsId,
                    new Vector4(width, height, 1.0f / width, 1.0f / height));
                BindAccumulationTextures(compute, accumulateKernel, accumulatedBaseColor, accumulatedNormal, accumulatedMask);
                BindAccumulationTextures(compute, finalizeKernel, accumulatedBaseColor, accumulatedNormal, accumulatedMask);
                int threadGroupsX = Mathf.CeilToInt(width / (float) ThreadGroupSize);
                int threadGroupsY = Mathf.CeilToInt(height / (float) ThreadGroupSize);
                // Keep one fixed texture set per dispatch; dynamically selected Texture2D bindings
                // resolve to fallback resources on affected Unity 6 DXC backends.
                for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                {
                    VividTerrainCompositeLayerSource layer = layers[layerIndex];
                    compute.SetTexture(
                        accumulateKernel,
                        BaseColorId,
                        layer.BaseColor != null ? layer.BaseColor : Texture2D.whiteTexture);
                    compute.SetTexture(
                        accumulateKernel,
                        NormalId,
                        layer.Normal != null ? layer.Normal : Texture2D.normalTexture);
                    compute.SetTexture(
                        accumulateKernel,
                        MaskId,
                        layer.Mask != null ? layer.Mask : Texture2D.whiteTexture);
                    compute.SetVector(LayerTilingOffsetId, layer.TextureTilingOffset);
                    compute.SetVector(LayerMaterialParamsId, layer.MaterialParams);
                    compute.SetInt(LayerPresenceId, layer.PresenceMask);
                    compute.SetInt(LayerIndexId, layerIndex);
                    compute.Dispatch(accumulateKernel, threadGroupsX, threadGroupsY, 1);
                }

                compute.SetTexture(finalizeKernel, OutputBaseColorId, baseColorOutput);
                compute.SetTexture(finalizeKernel, OutputNormalId, normalOutput);
                compute.SetTexture(finalizeKernel, OutputMaskId, maskOutput);
                compute.Dispatch(finalizeKernel, threadGroupsX, threadGroupsY, 1);

                CopyReadback(baseColorOutput, baseColor, mip, "base color");
                CopyReadback(normalOutput, normal, mip, "normal");
                CopyReadback(maskOutput, mask, mip, "mask");
            }
            finally
            {
                Release(accumulatedBaseColor);
                Release(accumulatedNormal);
                Release(accumulatedMask);
                Release(baseColorOutput);
                Release(normalOutput);
                Release(maskOutput);
            }
        }

        private static RenderTexture CreateOutputRenderTexture(int width, int height, string name)
        {
            return CreateRenderTexture(width, height, GraphicsFormat.R8G8B8A8_UNorm, name);
        }

        private static RenderTexture CreateRenderTexture(
            int width,
            int height,
            GraphicsFormat graphicsFormat,
            string name)
        {
            var descriptor = new RenderTextureDescriptor(
                width,
                height,
                graphicsFormat,
                depthBufferBits: 0)
            {
                enableRandomWrite = true,
                msaaSamples = 1,
                mipCount = 1,
                sRGB = false,
            };
            var texture = new RenderTexture(descriptor)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            if (!texture.Create())
            {
                Object.DestroyImmediate(texture);
                throw new InvalidOperationException(
                    $"Failed to create {width}x{height} terrain composite output texture.");
            }

            return texture;
        }

        private static void BindAccumulationTextures(
            ComputeShader compute,
            int kernel,
            RenderTexture baseColor,
            RenderTexture normal,
            RenderTexture mask)
        {
            compute.SetTexture(kernel, AccumulatedBaseColorId, baseColor);
            compute.SetTexture(kernel, AccumulatedNormalId, normal);
            compute.SetTexture(kernel, AccumulatedMaskId, mask);
        }

        private static void CopyReadback(
            RenderTexture source,
            Texture2D destination,
            int mip,
            string semantic)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(source, 0);
            request.WaitForCompletion();
            if (request.hasError)
                throw new InvalidOperationException($"Failed to read back terrain composite {semantic} mip {mip}.");

            NativeArray<Color32> pixels = request.GetData<Color32>();
            destination.SetPixelData(pixels, mip);
        }

        private static void Release(RenderTexture texture)
        {
            if (texture == null)
                return;
            texture.Release();
            Object.DestroyImmediate(texture);
        }

        private static ComputeShader LoadComputeShader()
        {
            string[] candidatePaths = VividPackagePathUtility.GetCandidateAssetPaths(ComputeRelativePath);
            for (int pathIndex = 0; pathIndex < candidatePaths.Length; pathIndex++)
            {
                ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(candidatePaths[pathIndex]);
                if (compute != null)
                    return compute;
            }

            return null;
        }

    }

    internal static class VividTerrainCompositeVirtualTextureAssetUtility
    {
        internal static bool BuildOrRefresh(
            string assetPath,
            in VividTerrainCompositeSource source,
            Action<float, string> progressHandler,
            out VividVirtualTextureAsset streamedAsset,
            out bool wasCreated,
            out string errorMessage)
        {
            streamedAsset = null;
            wasCreated = false;
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                errorMessage = "A terrain composite VT asset path is required.";
                return false;
            }

            if (!source.IsValid)
            {
                errorMessage = "A terrain composite VT requires two to eight captured terrain layers.";
                return false;
            }

            try
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                {
                    File.WriteAllText(assetPath, VividVirtualTextureAssetImporter.Version3Marker);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                    wasCreated = true;
                }

                if (AssetImporter.GetAtPath(assetPath) is not VividVirtualTextureAssetImporter importer)
                {
                    errorMessage = $"'{assetPath}' is not a Vivid virtual texture asset.";
                    return false;
                }

                importer.ConfigureTerrainCompositeSource(source);
                EditorUtility.SetDirty(importer);
                Action<float, string> previousProgressHandler =
                    VividTerrainCompositeVirtualTextureBuilder.ProgressHandler;
                try
                {
                    VividTerrainCompositeVirtualTextureBuilder.ProgressHandler = progressHandler;
                    importer.SaveAndReimport();
                }
                finally
                {
                    VividTerrainCompositeVirtualTextureBuilder.ProgressHandler = previousProgressHandler;
                }

                streamedAsset = AssetDatabase.LoadAssetAtPath<VividVirtualTextureAsset>(assetPath);
                if (streamedAsset == null || streamedAsset.BuiltData == null)
                {
                    errorMessage = $"Failed to import terrain composite VT asset '{assetPath}'.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                errorMessage = $"Failed to build terrain composite VT asset: {exception.Message}";
                return false;
            }
        }
    }

    internal static class VividTerrainCompositeUpgradeUtility
    {
        internal static bool CanUpgrade(VividTerrainData terrainData, out string reason)
        {
            if (terrainData == null)
            {
                reason = "Select a VividTerrainData asset.";
                return false;
            }

            string terrainAssetPath = AssetDatabase.GetAssetPath(terrainData);
            if (string.IsNullOrWhiteSpace(terrainAssetPath))
            {
                reason = $"Terrain data '{terrainData.name}' is not a persistent asset.";
                return false;
            }

            if (terrainData.SupportedSurfaceLayerCount <= 1)
            {
                reason = $"Terrain data '{terrainData.name}' does not require a composite surface.";
                return false;
            }

            if (!terrainData.HasCompleteControlMapData)
            {
                reason = $"Terrain data '{terrainData.name}' is missing one or more persistent control maps.";
                return false;
            }

            if (IsCompatibleComposite(terrainData.CompositeVirtualTexture))
            {
                reason = $"Terrain data '{terrainData.name}' already has a compatible Composite SVT.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal static bool TryUpgrade(
            VividTerrainData terrainData,
            int maxResolution,
            Action<float, string> progressHandler,
            out bool upgraded,
            out string errorMessage)
        {
            upgraded = false;
            errorMessage = string.Empty;
            if (IsCompatibleComposite(terrainData != null
                    ? terrainData.CompositeVirtualTexture
                    : null))
            {
                return true;
            }

            if (terrainData != null && terrainData.SupportedSurfaceLayerCount <= 1)
                return true;

            if (!CanUpgrade(terrainData, out errorMessage))
                return false;

            VividVirtualTextureAsset previousComposite = terrainData.CompositeVirtualTexture;
            string compositeAssetPath = string.Empty;
            try
            {
                int layerCount = terrainData.SupportedSurfaceLayerCount;
                var layers = new VividTerrainCompositeLayerSource[layerCount];
                for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
                {
                    layers[layerIndex] = new VividTerrainCompositeLayerSource(
                        terrainData.Layers[layerIndex],
                        terrainData.Size);
                }

                int controlMapCount = terrainData.RequiredControlMapCount;
                var controlMaps = new Texture2D[controlMapCount];
                for (int controlMapIndex = 0; controlMapIndex < controlMapCount; controlMapIndex++)
                    controlMaps[controlMapIndex] = terrainData.ControlMaps[controlMapIndex];

                var source = new VividTerrainCompositeSource(
                    terrainData.SourceTerrainDataGUID,
                    terrainData.Size,
                    maxResolution,
                    layers,
                    controlMaps);
                string terrainAssetPath = AssetDatabase.GetAssetPath(terrainData);
                compositeAssetPath = CreateCompositeAssetPath(terrainAssetPath);
                bool buildSucceeded = VividTerrainCompositeVirtualTextureAssetUtility.BuildOrRefresh(
                    compositeAssetPath,
                    source,
                    progressHandler,
                    out VividVirtualTextureAsset compositeAsset,
                    out _,
                    out errorMessage);
                if (!buildSucceeded || !IsCompatibleComposite(compositeAsset))
                {
                    if (buildSucceeded)
                    {
                        errorMessage =
                            $"Generated Composite SVT '{compositeAssetPath}' is not compatible with the GPUDriven VT stack.";
                    }

                    DeleteGeneratedComposite(compositeAssetPath);
                    return false;
                }

                terrainData.SetCompositeVirtualTexture(compositeAsset);
                EditorUtility.SetDirty(terrainData);
                AssetDatabase.SaveAssetIfDirty(terrainData);
                upgraded = true;
                return true;
            }
            catch (Exception exception)
            {
                terrainData.SetCompositeVirtualTexture(previousComposite);
                EditorUtility.SetDirty(terrainData);
                try
                {
                    AssetDatabase.SaveAssetIfDirty(terrainData);
                }
                catch
                {
                    // The failed save did not replace the serialized reference; keep cleaning the generated files.
                }
                DeleteGeneratedComposite(compositeAssetPath);
                errorMessage = $"Failed to upgrade terrain data '{terrainData.name}': {exception.Message}";
                return false;
            }
        }

        internal static bool IsCompatibleComposite(VividVirtualTextureAsset asset)
        {
            return asset != null
                   && asset.AddressMode == VividVirtualTextureAddressMode.Clamp
                   && (asset.ContentLayerMask & 7) == 7
                   && VirtualTextureGPUDrivenTextureBackend.IsCompatibleStreamedAsset(asset, out _);
        }

        private static string CreateCompositeAssetPath(string terrainAssetPath)
        {
            string directory = Path.GetDirectoryName(terrainAssetPath)?.Replace('\\', '/') ?? "Assets";
            string baseName = Path.GetFileNameWithoutExtension(terrainAssetPath);
            string candidate = Path.Combine(
                    directory,
                    $"{baseName}_CompositeSurface.{VividVirtualTextureAssetImporter.Extension}")
                .Replace('\\', '/');
            return AssetDatabase.GenerateUniqueAssetPath(candidate);
        }

        private static void DeleteGeneratedComposite(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return;

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.DeleteAsset(assetPath + ".stream");
        }
    }
}
