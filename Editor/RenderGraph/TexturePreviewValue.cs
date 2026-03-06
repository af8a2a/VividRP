using System;
using UnityEngine;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal sealed class TexturePreviewValue
    {
        [SerializeField]
        private Texture m_Texture;

        internal Texture Texture
        {
            get => m_Texture;
            set => m_Texture = value;
        }

        internal bool HasTexture => m_Texture != null;
    }
}
