using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("VividRP/Lighting/Scene Surface Cache")]
    public sealed class VividSdfSceneSurfaceCache : MonoBehaviour
    {
        private static readonly List<VividSdfSceneSurfaceCache> s_Active = new();

        [SerializeField] private VividSdfSurfaceCache m_Cache;

        public VividSdfSurfaceCache Cache
        {
            get => m_Cache;
            set => m_Cache = value;
        }

        internal static IReadOnlyList<VividSdfSceneSurfaceCache> Active => s_Active;

        private void OnEnable()
        {
            if (!s_Active.Contains(this))
                s_Active.Add(this);
        }

        private void OnDisable() => s_Active.Remove(this);
    }
}
