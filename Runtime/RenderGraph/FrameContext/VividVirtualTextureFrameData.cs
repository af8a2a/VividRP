using System.Collections.Generic;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividVirtualTextureFrameData : ContextItem
    {
        private readonly List<VirtualTextureSpaceBinding> m_Bindings = new();

        internal IReadOnlyList<VirtualTextureSpaceBinding> Bindings => m_Bindings;

        public override void Reset()
        {
            m_Bindings.Clear();
        }

        internal void AddBinding(in VirtualTextureSpaceBinding binding)
        {
            m_Bindings.Add(binding);
        }
    }
}
