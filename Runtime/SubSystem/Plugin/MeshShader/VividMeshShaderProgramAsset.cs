using System;
using UnityEngine;

namespace VividRP.Runtime.MeshShader
{
    /// <summary>
    /// Editor-compiled DXIL stages consumed by the native mesh-shader plugin.
    /// </summary>
    public sealed class VividMeshShaderProgramAsset : ScriptableObject
    {
        internal const uint CurrentRootLayoutVersion = 1;

        [SerializeField, HideInInspector]
        private byte[] m_AmplificationDxil = Array.Empty<byte>();

        [SerializeField, HideInInspector]
        private byte[] m_MeshDxil = Array.Empty<byte>();

        [SerializeField, HideInInspector]
        private byte[] m_PixelDxil = Array.Empty<byte>();

        [SerializeField, HideInInspector]
        private string m_SourceAssetPath = string.Empty;

        [SerializeField, HideInInspector]
        private string m_CompilerVersion = string.Empty;

        [SerializeField, HideInInspector]
        private uint m_CompilerAbiVersion;

        [SerializeField, HideInInspector]
        private uint m_RootLayoutVersion = CurrentRootLayoutVersion;

        public ReadOnlyMemory<byte> AmplificationDxil =>
            m_AmplificationDxil ?? Array.Empty<byte>();

        public ReadOnlyMemory<byte> MeshDxil =>
            m_MeshDxil ?? Array.Empty<byte>();

        public ReadOnlyMemory<byte> PixelDxil =>
            m_PixelDxil ?? Array.Empty<byte>();

        public string SourceAssetPath => m_SourceAssetPath;

        public string CompilerVersion => m_CompilerVersion;

        public uint CompilerAbiVersion => m_CompilerAbiVersion;

        public uint RootLayoutVersion => m_RootLayoutVersion;

        internal byte[] AmplificationDxilBytes =>
            m_AmplificationDxil ?? Array.Empty<byte>();

        internal byte[] MeshDxilBytes =>
            m_MeshDxil ?? Array.Empty<byte>();

        internal byte[] PixelDxilBytes =>
            m_PixelDxil ?? Array.Empty<byte>();

        internal void Initialize(
            byte[] amplificationDxil,
            byte[] meshDxil,
            byte[] pixelDxil,
            string sourceAssetPath,
            string compilerVersion,
            uint compilerAbiVersion,
            uint rootLayoutVersion)
        {
            m_AmplificationDxil = amplificationDxil ?? Array.Empty<byte>();
            m_MeshDxil = meshDxil ?? Array.Empty<byte>();
            m_PixelDxil = pixelDxil ?? Array.Empty<byte>();
            m_SourceAssetPath = sourceAssetPath ?? string.Empty;
            m_CompilerVersion = compilerVersion ?? string.Empty;
            m_CompilerAbiVersion = compilerAbiVersion;
            m_RootLayoutVersion = rootLayoutVersion;
        }
    }
}
