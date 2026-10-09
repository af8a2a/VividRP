using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    // Imported immutable textures. Unity owns upload, restoration and lifetime.
    public sealed class VividOpenPBRLuts : ScriptableObject
    {
        internal const int Resolution = 32;
        internal const int Count = 3;
        internal const int ArrayLayers = 6;
        internal static readonly int[] TextureIds =
        {
            Shader.PropertyToID("_OpenPBRIdealDielectricEnergy"),
            Shader.PropertyToID("_OpenPBROpaqueDielectricEnergy"),
            Shader.PropertyToID("_OpenPBRLuts2D")
        };
        [SerializeField] private Texture3D m_IdealDielectricEnergy;
        [SerializeField] private Texture3D m_OpaqueDielectricEnergy;
        [SerializeField] private Texture2DArray m_Tables2D;

        internal Texture GetTexture(int index) => index == 0 ? m_IdealDielectricEnergy
            : index == 1 ? m_OpaqueDielectricEnergy : m_Tables2D;

        internal void SetTextures(Texture3D ideal, Texture3D opaque, Texture2DArray tables)
        {
            m_IdealDielectricEnergy = ideal;
            m_OpaqueDielectricEnergy = opaque;
            m_Tables2D = tables;
        }

        internal void Bind(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(TextureIds[0], m_IdealDielectricEnergy);
            cmd.SetGlobalTexture(TextureIds[1], m_OpaqueDielectricEnergy);
            cmd.SetGlobalTexture(TextureIds[2], m_Tables2D);
        }
    }
}
