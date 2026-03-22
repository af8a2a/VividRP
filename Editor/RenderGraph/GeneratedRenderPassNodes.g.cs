using System;

namespace VividRP.Editor.RenderGraph.Generated
{
    [Serializable]
    internal sealed class ClassificationPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.ClassificationPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class ClusterDebugPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.ClusterDebugPass, VividRP.Runtime";
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
    internal sealed class Core_LightGridPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.LightGridPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class DeferredDirectionalLightingPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.DeferredDirectionalLightingPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class DeferredLightingPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.DeferredLightingPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class DirectionalRayTracedShadowDenoisePass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.DirectionalRayTracedShadowDenoisePass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class DirectionalRayTracedShadowPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.DirectionalRayTracedShadowPass, VividRP.Runtime";
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
    internal sealed class GenerateViewZPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.GenerateViewZPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class HDRISkyPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.HDRISkyPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class ImportTextureExamplePass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.ImportTextureExamplePass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class LTCAreaLightPreparePass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.LTCAreaLightPreparePass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class LightGridGlobalPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.LightGridGlobalPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class LightGridPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.LightGridPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class MotionVectorPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.MotionVectorPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class OverlayDebugPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.OverlayDebugPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class PreDepthPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.PreDepthPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class PreIntegratedFGDPreparePass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.PreIntegratedFGDPreparePass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class RTASBuildPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.RTASBuildPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class RTASInstanceDebugPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.RTASInstanceDebugPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class SIGMAShadowDenoisePass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.Sigma.SIGMAShadowDenoisePass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class SetupPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.SetupPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class ShadowClassifyPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.ShadowClassifyPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class SliderDebugPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.SliderDebugPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class TileDebugPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.TileDebugPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class VisibilityBufferPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.VisibilityBufferPass, VividRP.Runtime";
    }

}
