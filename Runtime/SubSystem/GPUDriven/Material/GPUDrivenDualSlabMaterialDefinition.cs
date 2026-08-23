using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    [CreateAssetMenu(
        menuName = "VividRP/GPUDriven/Dual Slab Material Definition",
        fileName = "New GPUDriven Dual Slab Material Definition")]
    public sealed class GPUDrivenDualSlabMaterialDefinition : ScriptableObject
    {
        [SerializeField]
        private GPUDrivenMaterialProxy m_TopSlab;

        [SerializeField]
        private VividDualSlabOperator m_Operator = VividDualSlabOperator.HorizontalMix;

        [SerializeField]
        [HideInInspector]
        private uint m_Revision = 1u;

        public GPUDrivenMaterialProxy TopSlab
        {
            get => m_TopSlab;
            set => SetValue(ref m_TopSlab, value);
        }

        public VividDualSlabOperator Operator
        {
            get => m_Operator;
            set => SetValue(ref m_Operator, value);
        }

        public uint Revision => m_Revision;

        private void OnValidate()
        {
            IncrementRevision();
        }

        private void SetValue<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            IncrementRevision();
        }

        private void IncrementRevision()
        {
            unchecked
            {
                m_Revision++;
                if (m_Revision == 0u)
                    m_Revision = 1u;
            }
        }
    }
}
