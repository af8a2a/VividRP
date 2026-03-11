using System;

namespace VividRP.Editor.RenderGraph.Generated
{
    [Serializable]
    internal sealed class ClassificationPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.ClassificationPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class ColorGradingPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.ColorGradingPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class CopyDepthPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.CopyDepthPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class DrawObjectPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.DrawObjectPass, VividRP.Runtime";
    }

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
    internal sealed class GBufferPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.GBufferPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class HDRISkyPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.HDRISkyPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class SetupPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.SetupPass, VividRP.Runtime";
    }

}
