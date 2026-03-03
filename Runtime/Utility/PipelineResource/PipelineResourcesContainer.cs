using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime
{
    [Serializable]
    public class ResourceEntry
    {
        public string TypeName;
        public string FieldName;
        public UnityEngine.Object Asset;
    }

    public class PipelineResourcesContainer : ScriptableObject
    {
        [SerializeField] private List<ResourceEntry> m_Entries = new();
        public List<ResourceEntry> Entries => m_Entries;
    }
}
