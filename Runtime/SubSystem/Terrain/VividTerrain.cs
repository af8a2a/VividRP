using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.PrimitiveScene;

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

        [System.NonSerialized]
        private VividPrimitiveHandle m_PrimitiveHandle = VividPrimitiveHandle.Invalid;

        [System.NonSerialized]
        private int m_TrackedGameObjectLayer = -1;

        public VividTerrainData Data => m_Data;

        public bool HasBakedData => TryValidateData(out _);

        public Bounds LocalBounds => m_Data != null ? m_Data.LocalBounds : default;

        public ShadowCastingMode ShadowCastingMode => m_ShadowCastingMode;

        public bool ReceiveShadows => m_ReceiveShadows;

        public uint RenderingLayerMask => m_RenderingLayerMask;

        public bool TryValidateData(out string reason)
        {
            if (m_Data == null)
            {
                reason = "No VividTerrainData asset is assigned.";
                return false;
            }

            return m_Data.TryValidate(out reason);
        }

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
            m_TrackedGameObjectLayer = gameObject.layer;
            transform.hasChanged = false;
        }

        internal VividPrimitiveHandle primitiveHandle => m_PrimitiveHandle;

        internal void NotifyPrimitiveHandleAssigned(VividPrimitiveHandle handle)
        {
            m_PrimitiveHandle = handle;
        }

        internal void InvalidatePrimitiveHandle()
        {
            m_PrimitiveHandle = VividPrimitiveHandle.Invalid;
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

            if (m_TrackedGameObjectLayer != gameObject.layer)
            {
                VividMeshletRendererDatabase.instance.UpdateTerrainRenderData(this);
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
