using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace VividRP.Runtime
{
    [Serializable]
    public class ResourceEntry
    {
        public string ResourceName;

        [FormerlySerializedAs("Asset")]
        public UnityEngine.Object ResourceObject;
    }

    public class PipelineResourcesContainer : ScriptableObject
    {
        [SerializeField] private List<ResourceEntry> m_Entries = new();
        public List<ResourceEntry> Entries => m_Entries;
    }
}
