using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime
{
    /// <summary>
    /// Runtime owner and GPUDriven registration point for terrain data baked from a Unity Terrain.
    /// </summary>
    [AddComponentMenu("VividRP/Terrain/Vivid Terrain")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class VividTerrain : MonoBehaviour
    {
        [SerializeField]
        private VividTerrainData m_Data;

        [SerializeField]
        private ShadowCastingMode m_ShadowCastingMode = ShadowCastingMode.On;

        [SerializeField]
        private bool m_ReceiveShadows = true;

        [SerializeField]
        private uint m_RenderingLayerMask = 1u;

        private VividTerrainData m_TrackedData;

        public VividTerrainData Data => m_Data;

        public bool HasBakedData => m_Data != null && m_Data.IsValid;

        public Bounds LocalBounds => m_Data != null ? m_Data.LocalBounds : default;

        public ShadowCastingMode ShadowCastingMode => m_ShadowCastingMode;

        public bool ReceiveShadows => m_ReceiveShadows;

        public uint RenderingLayerMask => m_RenderingLayerMask;

        public void SetData(VividTerrainData data)
        {
            if (m_Data == data)
            {
                return;
            }

            m_Data = data;
            SyncDatabaseRegistration();
        }

        internal void NotifyTerrainDataSynchronized()
        {
            m_TrackedData = m_Data;
            transform.hasChanged = false;
        }

        private void OnEnable()
        {
            SyncDatabaseRegistration();
        }

        private void LateUpdate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (m_TrackedData != m_Data)
            {
                SyncDatabaseRegistration();
                return;
            }

            if (transform.hasChanged)
            {
                VividMeshletRendererDatabase.instance.UpdateTerrainTransformData(this);
            }
        }

        private void OnDisable()
        {
            VividMeshletRendererDatabase.instance.UnregisterTerrain(this);
        }

        private void OnDestroy()
        {
            VividMeshletRendererDatabase.instance.UnregisterTerrain(this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            SyncDatabaseRegistration();
        }
#endif

        private void SyncDatabaseRegistration()
        {
            if (!isActiveAndEnabled)
            {
                VividMeshletRendererDatabase.instance.UnregisterTerrain(this);
                return;
            }

            VividMeshletRendererDatabase.instance.UpdateTerrainData(this);
        }
    }
}
