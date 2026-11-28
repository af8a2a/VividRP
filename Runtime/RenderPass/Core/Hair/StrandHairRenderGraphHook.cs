using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Zero-allocation RenderGraph integration hook that automatically schedules
    /// StrandHairVisibilityPass + StrandHairCompositionPass whenever active
    /// HairGroomRenderers exist and the .vrdg graph does not already contain
    /// explicit Strand Hair nodes.
    /// </summary>
    internal static class StrandHairRenderGraphHook
    {
        private sealed class VisPassData
        {
            public StrandHairVisibilityPass Pass;
            public TextureHandle Depth;
            public TextureHandle Hzb;
        }

        private sealed class CompPassData
        {
            public StrandHairCompositionPass Pass;
            public StrandHairGpuPool GpuPool;
            public TextureHandle Color;
        }

        private static readonly BaseRenderFunc<VisPassData, ComputeGraphContext> s_ExecuteVis = ExecuteVis;
        private static readonly BaseRenderFunc<CompPassData, ComputeGraphContext> s_ExecuteComp = ExecuteComp;

        private static StrandHairVisibilityPass s_VisPass;
        private static StrandHairCompositionPass s_CompPass;
        private static bool s_DisposeHooked;

        // Pending state when composition is deferred until right after SkyInjectionPass
        private static bool s_HasPendingComposition;
        private static TextureHandle s_PendingColorHandle;
        private static BufferHandle s_PendingVisAtlasHandle;

        internal static void ClearPerFrameState()
        {
            s_HasPendingComposition = false;
            s_PendingColorHandle = default;
            s_PendingVisAtlasHandle = default;
        }

        internal static void TryRecordAfterDeferredLighting(
            RenderGraph renderGraph,
            ContextContainer frameData,
            DeferredLightingPass deferredLightingPass,
            RenderGraphTexture hzbTexture,
            bool deferCompositionUntilAfterSky)
        {
            ClearPerFrameState();

            if (HairGroomSystem.ActiveCount == 0
                || renderGraph == null
                || frameData == null
                || deferredLightingPass == null)
            {
                return;
            }

            // Skip if explicit .vrdg StrandHairVisibilityPass already recorded this frame
            if (frameData.Contains<StrandHairFrameData>()
                && frameData.Get<StrandHairFrameData>().VisibilityRecordedThisFrame)
            {
                return;
            }

            RenderGraphTexture depthTex = deferredLightingPass.DepthTexture;
            RenderGraphTexture colorTex = deferredLightingPass.ColorTexture;
            if (depthTex == null || !depthTex.innerHandle.IsValid()
                || colorTex == null || !colorTex.innerHandle.IsValid())
            {
                return;
            }

            EnsurePassesCreated();

            int targetWidth = colorTex.desc != null && colorTex.desc.Width > 0 ? colorTex.desc.Width : 0;
            int targetHeight = colorTex.desc != null && colorTex.desc.Height > 0 ? colorTex.desc.Height : 0;

            if (!s_VisPass.PrepareInternal(frameData, hzbTexture?.desc, targetWidth, targetHeight))
                return;

            if (!s_CompPass.PrepareInternal(frameData, s_VisPass.GpuPool))
                return;

            var hairFrameData = frameData.Get<StrandHairFrameData>();
            if (hairFrameData == null || hairFrameData.VisAtlasBuffer == null || !hairFrameData.VisAtlasBuffer.IsValid())
                return;

            TextureHandle depthHandle = depthTex.innerHandle;
            TextureHandle hzbHandle = hzbTexture != null && hzbTexture.innerHandle.IsValid()
                ? hzbTexture.innerHandle
                : default;
            TextureHandle colorHandle = colorTex.innerHandle;
            BufferHandle visAtlasHandle = renderGraph.ImportBuffer(hairFrameData.VisAtlasBuffer);

            using (var visBuilder = renderGraph.AddComputePass<VisPassData>(
                nameof(StrandHairVisibilityPass),
                out var visData))
            {
                visData.Pass = s_VisPass;
                visData.Depth = depthHandle;
                visData.Hzb = hzbHandle;

                visBuilder.UseTexture(depthHandle, AccessFlags.Read);
                if (hzbHandle.IsValid())
                    visBuilder.UseTexture(hzbHandle, AccessFlags.Read);
                visBuilder.UseBuffer(visAtlasHandle, AccessFlags.Write);
                visBuilder.AllowPassCulling(false);
                visBuilder.SetRenderFunc(s_ExecuteVis);
            }

            if (deferCompositionUntilAfterSky)
            {
                s_HasPendingComposition = true;
                s_PendingColorHandle = colorHandle;
                s_PendingVisAtlasHandle = visAtlasHandle;
            }
            else
            {
                RecordCompositionPass(renderGraph, colorHandle, visAtlasHandle);
            }
        }

        internal static void TryRecordDeferredComposition(
            RenderGraph renderGraph,
            ContextContainer frameData)
        {
            if (!s_HasPendingComposition
                || renderGraph == null
                || s_CompPass == null
                || s_VisPass == null
                || !s_PendingColorHandle.IsValid()
                || !s_PendingVisAtlasHandle.IsValid())
            {
                ClearPerFrameState();
                return;
            }

            TextureHandle colorHandle = s_PendingColorHandle;
            BufferHandle visAtlasHandle = s_PendingVisAtlasHandle;
            ClearPerFrameState();
            RecordCompositionPass(renderGraph, colorHandle, visAtlasHandle);
        }

        private static void RecordCompositionPass(
            RenderGraph renderGraph,
            TextureHandle colorHandle,
            BufferHandle visAtlasHandle)
        {
            using (var compBuilder = renderGraph.AddComputePass<CompPassData>(
                nameof(StrandHairCompositionPass),
                out var compData))
            {
                compData.Pass = s_CompPass;
                compData.GpuPool = s_VisPass.GpuPool;
                compData.Color = colorHandle;

                compBuilder.UseBuffer(visAtlasHandle, AccessFlags.Read);
                compBuilder.UseTexture(colorHandle, AccessFlags.ReadWrite);
                compBuilder.AllowPassCulling(false);
                compBuilder.SetRenderFunc(s_ExecuteComp);
            }
        }

        private static void ExecuteVis(VisPassData data, ComputeGraphContext ctx)
        {
            data.Pass.RecordWithHandles(ctx.cmd, data.Depth, data.Hzb);
        }

        private static void ExecuteComp(CompPassData data, ComputeGraphContext ctx)
        {
            data.Pass.RecordWithHandle(ctx.cmd, data.Color, data.GpuPool);
        }

        private static void EnsurePassesCreated()
        {
            if (s_VisPass == null)
            {
                s_VisPass = new StrandHairVisibilityPass();
                s_VisPass.Create();
            }

            if (s_CompPass == null)
            {
                s_CompPass = new StrandHairCompositionPass();
                s_CompPass.Create();
            }

            if (!s_DisposeHooked)
            {
                FrameContextSystem.SubsystemDispose += Dispose;
                s_DisposeHooked = true;
            }
        }

        internal static void Dispose()
        {
            ClearPerFrameState();
            s_VisPass?.Dispose();
            s_VisPass = null;
            s_CompPass?.Dispose();
            s_CompPass = null;
        }
    }
}
