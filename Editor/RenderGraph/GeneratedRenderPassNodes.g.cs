using System;

namespace VividRP.Editor.RenderGraph.Generated
{
    [Serializable]
    internal sealed class AtmosphereLUTPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.AtmosphereLUTPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class AtmosphericScatteringPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.AtmosphericScatteringPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class AutoExposurePass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.AutoExposurePass, VividRP.Runtime";
    }

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
    internal sealed class ExposureDebugPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.ExposureDebugPass, VividRP.Runtime";
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
    internal sealed class HZBGeneratePass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.HZBGeneratePass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class ImportTextureExamplePass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.ImportTextureExamplePass, VividRP.Runtime";
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
    internal sealed class PhysicallyBasedSkyPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.PhysicallyBasedSkyPass, VividRP.Runtime";
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
    internal sealed class ShadowClassifyPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.ShadowClassifyPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class SkyInjectionPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.SkyInjectionPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class SliderDebugPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.SliderDebugPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class TemporalAAPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.TemporalAAPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class TileDebugPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.TileDebugPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class VisibilityBufferGBufferResolvePass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.VisibilityBufferGBufferResolvePass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class VisibilityBufferPass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.VisibilityBufferPass, VividRP.Runtime";
    }

    [Serializable]
    internal sealed class VisibilityBufferResolvePass : RenderPassNodeData
    {
        protected override string RegisteredPassTypeName => "VividRP.Runtime.RenderPass.Core.VisibilityBufferResolvePass, VividRP.Runtime";
    }

}
