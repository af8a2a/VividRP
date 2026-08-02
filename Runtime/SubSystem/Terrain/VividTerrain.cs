using UnityEngine;

namespace VividRP.Runtime
{
    /// <summary>
    /// Runtime owner for terrain data baked from a Unity Terrain.
    /// Rendering and LOD selection are intentionally handled by later GPUDriven stages.
    /// </summary>
    [AddComponentMenu("VividRP/Terrain/Vivid Terrain")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class VividTerrain : MonoBehaviour
    {
        [SerializeField]
        private VividTerrainData m_Data;

        public VividTerrainData Data => m_Data;

        public bool HasBakedData => m_Data != null && m_Data.IsValid;

        public Bounds LocalBounds => m_Data != null ? m_Data.LocalBounds : default;

        public void SetData(VividTerrainData data)
        {
            m_Data = data;
        }
    }
}
