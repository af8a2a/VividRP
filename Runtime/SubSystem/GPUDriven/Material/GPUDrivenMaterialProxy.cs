using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    public enum GPUDrivenMaterialProxyModel
    {
        StandardLit = 0,
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

    [CreateAssetMenu(menuName = "VividRP/GPUDriven/Material Proxy", fileName = "New GPUDriven Material Proxy")]
    public sealed class GPUDrivenMaterialProxy : ScriptableObject
    {
        [SerializeField]
        private Material m_SourceMaterial;

        [SerializeField]
        private GPUDrivenMaterialProxyModel m_Model = GPUDrivenMaterialProxyModel.StandardLit;

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
        [HideInInspector]
        private uint m_Revision = 1;

        public Material SourceMaterial
        {
            get => m_SourceMaterial;
            set => SetValue(ref m_SourceMaterial, value);
        }

        public GPUDrivenMaterialProxyModel Model
        {
            get => m_Model;
            set => SetValue(ref m_Model, value);
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

        public uint Revision => m_Revision;

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
            where T : Object
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
    }
}
