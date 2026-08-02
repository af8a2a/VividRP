using System.Collections.Generic;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividVirtualTextureFrameData : ContextItem
    {
        private readonly List<VirtualTextureSpaceBinding> m_Bindings = new();

        internal IReadOnlyList<VirtualTextureSpaceBinding> Bindings => m_Bindings;

        internal int BindingCount => m_Bindings.Count;

        public override void Reset()
        {
            m_Bindings.Clear();
        }

        internal int AddBinding(in VirtualTextureSpaceBinding binding)
        {
            int bindingIndex = m_Bindings.Count;
            m_Bindings.Add(binding.WithBindingIndex(bindingIndex));
            return bindingIndex;
        }

        internal bool TryGetBinding(int bindingIndex, out VirtualTextureSpaceBinding binding)
        {
            if (bindingIndex >= 0 && bindingIndex < m_Bindings.Count)
            {
                binding = m_Bindings[bindingIndex];
                return true;
            }

            binding = default;
            return false;
        }

        internal bool TryGetBindingForAllocation(int allocationId, out VirtualTextureSpaceBinding binding)
        {
            for (int bindingIndex = 0; bindingIndex < m_Bindings.Count; bindingIndex++)
            {
                VirtualTextureSpaceBinding candidate = m_Bindings[bindingIndex];
                if (candidate.AllocationId != allocationId)
                    continue;

                binding = candidate;
                return true;
            }

            binding = default;
            return false;
        }

        internal bool TryGetDefaultBinding(out VirtualTextureSpaceBinding binding)
        {
            for (int bindingIndex = 0; bindingIndex < m_Bindings.Count; bindingIndex++)
            {
                if (m_Bindings[bindingIndex].PrivateSpace)
                    continue;

                binding = m_Bindings[bindingIndex];
                return true;
            }

            binding = default;
            return false;
        }
    }
}
