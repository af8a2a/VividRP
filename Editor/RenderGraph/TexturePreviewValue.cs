using System;
using UnityEngine;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal sealed class TexturePreviewValue
    {
        [SerializeField]
        private Texture m_Texture;

        [SerializeField]
        private string m_SourcePassTypeName;

        [SerializeField]
        private string m_SourceFieldName;

        [SerializeField]
        private bool m_HasConnectedTextureInput;

        internal Texture Texture
        {
            get => m_Texture;
            set => m_Texture = value;
        }

        internal bool HasTexture => m_Texture != null;

        internal bool HasConnectedTextureInput => m_HasConnectedTextureInput;

        internal void SetConnectedPassOutput(Type passType, string fieldName)
        {
            m_SourcePassTypeName = passType?.AssemblyQualifiedName;
            m_SourceFieldName = fieldName;
            m_HasConnectedTextureInput = true;
        }

        internal void SetConnectedTextureInput()
        {
            m_SourcePassTypeName = null;
            m_SourceFieldName = null;
            m_HasConnectedTextureInput = true;
        }

        internal void ClearConnectionMetadata()
        {
            m_SourcePassTypeName = null;
            m_SourceFieldName = null;
            m_HasConnectedTextureInput = false;
        }

        internal bool TryGetConnectedPassOutput(out Type passType, out string fieldName)
        {
            fieldName = m_SourceFieldName;
            passType = string.IsNullOrEmpty(m_SourcePassTypeName)
                ? null
                : Type.GetType(m_SourcePassTypeName, throwOnError: false);

            if (passType != null && !string.IsNullOrEmpty(fieldName))
                return true;

            passType = null;
            fieldName = null;
            return false;
        }
    }
}
