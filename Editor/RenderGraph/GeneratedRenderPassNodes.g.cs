using System;

namespace VividRP.Editor.RenderGraph.Generated
{
    [Serializable]
    internal sealed class FinalBlitPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.FinalBlitPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class FullScreenPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.FullScreenPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class SetupPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.SetupPass, VividRP.Runtime";
    }

}
