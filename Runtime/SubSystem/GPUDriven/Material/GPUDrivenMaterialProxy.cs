using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    public enum GPUDrivenMaterialProxyModel
    {
        StandardLit = 0,
        DualSlab = 1,
    }

    public enum GPUDrivenMaterialMaskMode
    {
        None = 0,
        MetallicSmoothness = 1,
        Roughness = 2,
        PackedMetallicOcclusionSmoothness = 3,
        RoughnessMetallicOcclusion = 4,
    }

    public enum GPUDrivenMaterialProxyTextureMode
    {
        Bindless = 0,
        VirtualTexture = 1,
    }

    public enum GPUDrivenMaterialParameterType
    {
        Bool = 0,
        Float = 1,
        Float2 = 2,
        Float3 = 3,
        Float4 = 4,
    }

    [Serializable]
    public struct GPUDrivenMaterialParameterOverride
    {
        [SerializeField]
        private string m_Symbol;

        [SerializeField]
        private GPUDrivenMaterialParameterType m_Type;

        [SerializeField]
        private Vector4 m_Value;

        public GPUDrivenMaterialParameterOverride(
            string symbol,
            GPUDrivenMaterialParameterType type,
            Vector4 value)
        {
            m_Symbol = symbol;
            m_Type = type;
            m_Value = value;
        }

        public string Symbol => m_Symbol;

        public GPUDrivenMaterialParameterType Type => m_Type;

        public Vector4 Value => m_Value;
    }

    [Serializable]
    public struct GPUDrivenMaterialTextureOverride
    {
        [SerializeField]
        private string m_Symbol;

        [SerializeField]
        private Texture2D m_Texture;

        [SerializeField]
        private VividVirtualTextureAsset m_StreamedVirtualTexture;

        [SerializeField]
        private Vector4 m_TilingOffset;

        public GPUDrivenMaterialTextureOverride(
            string symbol,
            Texture2D texture,
            Vector4 tilingOffset)
            : this(symbol, texture, null, tilingOffset)
        {
        }

        private GPUDrivenMaterialTextureOverride(
            string symbol,
            Texture2D texture,
            VividVirtualTextureAsset streamedVirtualTexture,
            Vector4 tilingOffset)
        {
            m_Symbol = symbol;
            m_Texture = texture;
            m_StreamedVirtualTexture = streamedVirtualTexture;
            m_TilingOffset = tilingOffset;
        }

        internal static GPUDrivenMaterialTextureOverride ForVirtualTexture(
            string symbol,
            VividVirtualTextureAsset streamedVirtualTexture,
            Vector4 tilingOffset)
        {
            return new GPUDrivenMaterialTextureOverride(
                symbol,
                null,
                streamedVirtualTexture,
                tilingOffset);
        }

        public string Symbol => m_Symbol;

        public Texture2D Texture => m_Texture;

        public VividVirtualTextureAsset StreamedVirtualTexture =>
            m_StreamedVirtualTexture;

        public Vector4 TilingOffset => m_TilingOffset;
    }

    [CreateAssetMenu(menuName = "VividRP/GPUDriven/Material Proxy", fileName = "New GPUDriven Material Proxy")]
    public sealed class GPUDrivenMaterialProxy : ScriptableObject
    {
        [SerializeField]
        private Material m_SourceMaterial;

        [SerializeField]
        private MaterialGraphImportAsset m_MaterialGraph;

        [SerializeField]
        private GPUDrivenMaterialProxyModel m_Model = GPUDrivenMaterialProxyModel.StandardLit;

        [SerializeField]
        private GPUDrivenDualSlabMaterialDefinition m_DualSlabDefinition;

        [SerializeField, Range(0.0f, 1.0f)]
        private float m_LayerWeight = 0.5f;

        [SerializeField]
        private GPUDrivenMaterialProxyTextureMode m_TextureMode =
            GPUDrivenMaterialProxyTextureMode.Bindless;

        [SerializeField]
        private Texture2D m_BaseMap;

        [SerializeField]
        private Color m_BaseColor = Color.white;

        [SerializeField]
        private Vector4 m_TextureTilingOffset = new(1.0f, 1.0f, 0.0f, 0.0f);

        [SerializeField]
        private Texture2D m_BumpMap;

        [SerializeField]
        private float m_BumpScale = 1.0f;

        [SerializeField]
        private Texture2D m_MaskMap;

        [SerializeField]
        private VividVirtualTextureAsset m_StreamedVirtualTexture;

        [SerializeField]
        private GPUDrivenMaterialMaskMode m_MaskMode =
            GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness;

        [SerializeField]
        private float m_Metallic;

        [SerializeField]
        private float m_Roughness = 0.5f;

        [SerializeField]
        private Vector2 m_MetallicRemap = new(0.0f, 1.0f);

        [SerializeField]
        private Vector2 m_SmoothnessRemap = new(0.0f, 1.0f);

        [SerializeField]
        private Vector2 m_AmbientOcclusionRemap = new(0.0f, 1.0f);

        [SerializeField]
        private Color m_EmissionColor = Color.black;

        [SerializeField]
        private bool m_AlphaClip;

        [SerializeField]
        private float m_Cutoff = 0.5f;

        [SerializeField]
        private CullMode m_CullMode = CullMode.Back;

        [SerializeField]
        private bool m_DisableLighting;

        [SerializeField]
        private List<GPUDrivenMaterialParameterOverride> m_ParameterOverrides =
            new();

        [SerializeField]
        private List<GPUDrivenMaterialTextureOverride> m_TextureOverrides =
            new();

        [SerializeField]
        [HideInInspector]
        private uint m_Revision = 1;

        public Material SourceMaterial
        {
            get => m_SourceMaterial;
            set => SetValue(ref m_SourceMaterial, value);
        }

        public MaterialGraphImportAsset MaterialGraph
        {
            get => m_MaterialGraph;
            set => SetValue(ref m_MaterialGraph, value);
        }

        public GPUDrivenMaterialProxyModel Model
        {
            get => m_Model;
            set => SetValue(ref m_Model, value);
        }

        public GPUDrivenDualSlabMaterialDefinition DualSlabDefinition
        {
            get => m_DualSlabDefinition;
            set => SetValue(ref m_DualSlabDefinition, value);
        }

        public float LayerWeight
        {
            get => m_LayerWeight;
            set => SetValue(ref m_LayerWeight, Mathf.Clamp01(value));
        }

        public GPUDrivenMaterialProxyTextureMode TextureMode
        {
            get => m_TextureMode;
            set => SetTextureMode(value);
        }

        public Texture2D BaseMap
        {
            get => m_BaseMap;
            set => SetRawTexture(ref m_BaseMap, value);
        }

        public Color BaseColor
        {
            get => m_BaseColor;
            set => SetValue(ref m_BaseColor, value);
        }

        public Vector4 TextureTilingOffset
        {
            get => m_TextureTilingOffset;
            set => SetValue(ref m_TextureTilingOffset, value);
        }

        public Texture2D BumpMap
        {
            get => m_BumpMap;
            set => SetRawTexture(ref m_BumpMap, value);
        }

        public float BumpScale
        {
            get => m_BumpScale;
            set => SetValue(ref m_BumpScale, value);
        }

        public Texture2D MaskMap
        {
            get => m_MaskMap;
            set => SetRawTexture(ref m_MaskMap, value);
        }

        public VividVirtualTextureAsset StreamedVirtualTexture
        {
            get => m_StreamedVirtualTexture;
            set
            {
                if (value != null)
                {
                    SetTextureMode(GPUDrivenMaterialProxyTextureMode.VirtualTexture);
                }

                SetValue(ref m_StreamedVirtualTexture, value);
            }
        }

        public GPUDrivenMaterialMaskMode MaskMode
        {
            get => m_MaskMode;
            set => SetValue(ref m_MaskMode, value);
        }

        public float Metallic
        {
            get => m_Metallic;
            set => SetValue(ref m_Metallic, value);
        }

        public float Roughness
        {
            get => m_Roughness;
            set => SetValue(ref m_Roughness, value);
        }

        public Vector2 MetallicRemap
        {
            get => m_MetallicRemap;
            set => SetValue(ref m_MetallicRemap, value);
        }

        public Vector2 SmoothnessRemap
        {
            get => m_SmoothnessRemap;
            set => SetValue(ref m_SmoothnessRemap, value);
        }

        public Vector2 AmbientOcclusionRemap
        {
            get => m_AmbientOcclusionRemap;
            set => SetValue(ref m_AmbientOcclusionRemap, value);
        }

        public Color EmissionColor
        {
            get => m_EmissionColor;
            set => SetValue(ref m_EmissionColor, value);
        }

        public bool AlphaClip
        {
            get => m_AlphaClip;
            set => SetValue(ref m_AlphaClip, value);
        }

        public float Cutoff
        {
            get => m_Cutoff;
            set => SetValue(ref m_Cutoff, value);
        }

        public CullMode CullMode
        {
            get => m_CullMode;
            set => SetValue(ref m_CullMode, value);
        }

        public bool DisableLighting
        {
            get => m_DisableLighting;
            set => SetValue(ref m_DisableLighting, value);
        }

        public IReadOnlyList<GPUDrivenMaterialParameterOverride>
            ParameterOverrides => m_ParameterOverrides;

        public IReadOnlyList<GPUDrivenMaterialTextureOverride>
            TextureOverrides => m_TextureOverrides;

        public uint Revision => m_Revision;

        public void SetParameterOverride(
            string symbol,
            GPUDrivenMaterialParameterType type,
            Vector4 value)
        {
            RequirePropertySymbol(symbol);
            m_ParameterOverrides ??= new List<GPUDrivenMaterialParameterOverride>();
            var item = new GPUDrivenMaterialParameterOverride(symbol, type, value);
            for (int i = 0; i < m_ParameterOverrides.Count; i++)
            {
                if (!string.Equals(
                        m_ParameterOverrides[i].Symbol,
                        symbol,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (m_ParameterOverrides[i].Type == type
                    && m_ParameterOverrides[i].Value == value)
                {
                    return;
                }
                m_ParameterOverrides[i] = item;
                IncrementRevision();
                return;
            }
            m_ParameterOverrides.Add(item);
            IncrementRevision();
        }

        public void SetTextureOverride(
            string symbol,
            Texture2D texture)
        {
            SetTextureOverride(
                symbol,
                texture,
                new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
        }

        public void SetTextureOverride(
            string symbol,
            Texture2D texture,
            Vector4 tilingOffset)
        {
            SetTextureOverride(new GPUDrivenMaterialTextureOverride(
                symbol,
                texture,
                tilingOffset));
        }

        public void SetVirtualTextureOverride(
            string symbol,
            VividVirtualTextureAsset streamedVirtualTexture)
        {
            SetVirtualTextureOverride(
                symbol,
                streamedVirtualTexture,
                new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
        }

        public void SetVirtualTextureOverride(
            string symbol,
            VividVirtualTextureAsset streamedVirtualTexture,
            Vector4 tilingOffset)
        {
            SetTextureOverride(GPUDrivenMaterialTextureOverride.ForVirtualTexture(
                symbol,
                streamedVirtualTexture,
                tilingOffset));
        }

        private void SetTextureOverride(GPUDrivenMaterialTextureOverride item)
        {
            RequirePropertySymbol(item.Symbol);
            m_TextureOverrides ??= new List<GPUDrivenMaterialTextureOverride>();
            for (int i = 0; i < m_TextureOverrides.Count; i++)
            {
                if (!string.Equals(
                        m_TextureOverrides[i].Symbol,
                        item.Symbol,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (m_TextureOverrides[i].Texture == item.Texture
                    && m_TextureOverrides[i].StreamedVirtualTexture
                        == item.StreamedVirtualTexture
                    && m_TextureOverrides[i].TilingOffset == item.TilingOffset)
                {
                    return;
                }
                m_TextureOverrides[i] = item;
                IncrementRevision();
                return;
            }
            m_TextureOverrides.Add(item);
            IncrementRevision();
        }

        public bool RemoveParameterOverride(string symbol)
        {
            if (m_ParameterOverrides == null)
                return false;
            for (int i = 0; i < m_ParameterOverrides.Count; i++)
            {
                if (!string.Equals(
                        m_ParameterOverrides[i].Symbol,
                        symbol,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                m_ParameterOverrides.RemoveAt(i);
                IncrementRevision();
                return true;
            }
            return false;
        }

        public bool RemoveTextureOverride(string symbol)
        {
            if (m_TextureOverrides == null)
                return false;
            for (int i = 0; i < m_TextureOverrides.Count; i++)
            {
                if (!string.Equals(
                        m_TextureOverrides[i].Symbol,
                        symbol,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                m_TextureOverrides.RemoveAt(i);
                IncrementRevision();
                return true;
            }
            return false;
        }

        internal bool TryGetParameterOverride(
            in MaterialParameterDeclaration declaration,
            out Vector4 value)
        {
            if (m_ParameterOverrides != null)
            {
                for (int i = 0; i < m_ParameterOverrides.Count; i++)
                {
                    GPUDrivenMaterialParameterOverride item =
                        m_ParameterOverrides[i];
                    if (!string.Equals(
                            item.Symbol,
                            declaration.Symbol,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (!Matches(item.Type, declaration.Type))
                    {
                        throw new InvalidOperationException(
                            $"Material parameter override '{item.Symbol}' is {item.Type}, but the compiled graph requires {declaration.Type}.");
                    }
                    value = item.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        internal bool TryGetTextureOverride(
            in MaterialResourceDeclaration declaration,
            out GPUDrivenMaterialTextureOverride value)
        {
            if (declaration.Type != MaterialValueType.Texture2D)
            {
                throw new InvalidOperationException(
                    $"Material resource '{declaration.Symbol}' is not Texture2D.");
            }
            if (m_TextureOverrides != null)
            {
                for (int i = 0; i < m_TextureOverrides.Count; i++)
                {
                    GPUDrivenMaterialTextureOverride item = m_TextureOverrides[i];
                    if (!string.Equals(
                            item.Symbol,
                            declaration.Symbol,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    value = item;
                    return true;
                }
            }

            value = default;
            return false;
        }

        internal void IncrementRevision()
        {
            unchecked
            {
                m_Revision++;
                if (m_Revision == 0)
                {
                    m_Revision = 1;
                }
            }
        }

        private void OnValidate()
        {
            IncrementRevision();
        }

        private void SetRawTexture(ref Texture2D field, Texture2D value)
        {
            if (value != null)
            {
                SetTextureMode(GPUDrivenMaterialProxyTextureMode.Bindless);
            }

            SetValue(ref field, value);
        }

        private void SetTextureMode(GPUDrivenMaterialProxyTextureMode textureMode)
        {
            bool changed = m_TextureMode != textureMode;
            m_TextureMode = textureMode;
            changed |= ClearIncompatibleTextureResources(textureMode);
            if (changed)
            {
                IncrementRevision();
            }
        }

        private bool ClearIncompatibleTextureResources(
            GPUDrivenMaterialProxyTextureMode textureMode)
        {
            bool changed = false;
            if (textureMode == GPUDrivenMaterialProxyTextureMode.VirtualTexture)
            {
                changed |= ClearValue(ref m_BaseMap);
                changed |= ClearValue(ref m_BumpMap);
                changed |= ClearValue(ref m_MaskMap);
            }
            else
            {
                changed |= ClearValue(ref m_StreamedVirtualTexture);
            }

            return changed;
        }

        private static bool ClearValue<T>(ref T field)
            where T : UnityEngine.Object
        {
            if (field == null)
            {
                return false;
            }

            field = null;
            return true;
        }

        private void SetValue<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            IncrementRevision();
        }

        private static bool Matches(
            GPUDrivenMaterialParameterType source,
            MaterialValueType target)
        {
            switch (source)
            {
                case GPUDrivenMaterialParameterType.Bool:
                    return target == MaterialValueType.Bool;
                case GPUDrivenMaterialParameterType.Float:
                    return target == MaterialValueType.Float;
                case GPUDrivenMaterialParameterType.Float2:
                    return target == MaterialValueType.Float2;
                case GPUDrivenMaterialParameterType.Float3:
                    return target == MaterialValueType.Float3;
                case GPUDrivenMaterialParameterType.Float4:
                    return target == MaterialValueType.Float4;
                default:
                    return false;
            }
        }

        private static void RequirePropertySymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                throw new ArgumentException("A material property symbol is required.", nameof(symbol));
        }
    }
}
