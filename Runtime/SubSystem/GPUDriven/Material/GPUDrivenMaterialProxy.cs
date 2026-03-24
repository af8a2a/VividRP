using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    public enum GPUDrivenMaterialProxyModel
    {
        StandardLit = 0,
    }

    [CreateAssetMenu(menuName = "VividRP/GPUDriven/Material Proxy", fileName = "New GPUDriven Material Proxy")]
    public sealed class GPUDrivenMaterialProxy : ScriptableObject
    {
        [SerializeField]
        private Material m_SourceMaterial;

        [SerializeField]
        private GPUDrivenMaterialProxyModel m_Model = GPUDrivenMaterialProxyModel.StandardLit;

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
            set => m_SourceMaterial = value;
        }

        public GPUDrivenMaterialProxyModel Model
        {
            get => m_Model;
            set => m_Model = value;
        }

        public Texture2D BaseMap
        {
            get => m_BaseMap;
            set => m_BaseMap = value;
        }

        public Color BaseColor
        {
            get => m_BaseColor;
            set => m_BaseColor = value;
        }

        public Vector4 TextureTilingOffset
        {
            get => m_TextureTilingOffset;
            set => m_TextureTilingOffset = value;
        }

        public Texture2D BumpMap
        {
            get => m_BumpMap;
            set => m_BumpMap = value;
        }

        public float BumpScale
        {
            get => m_BumpScale;
            set => m_BumpScale = value;
        }

        public float Metallic
        {
            get => m_Metallic;
            set => m_Metallic = value;
        }

        public float Roughness
        {
            get => m_Roughness;
            set => m_Roughness = value;
        }

        public Color EmissionColor
        {
            get => m_EmissionColor;
            set => m_EmissionColor = value;
        }

        public bool AlphaClip
        {
            get => m_AlphaClip;
            set => m_AlphaClip = value;
        }

        public float Cutoff
        {
            get => m_Cutoff;
            set => m_Cutoff = value;
        }

        public CullMode CullMode
        {
            get => m_CullMode;
            set => m_CullMode = value;
        }

        public bool DisableLighting
        {
            get => m_DisableLighting;
            set => m_DisableLighting = value;
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
    }
}
