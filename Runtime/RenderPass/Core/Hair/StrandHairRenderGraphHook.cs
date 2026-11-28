using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Zero-allocation RenderGraph integration hook that schedules:
    ///   1) StrandHairVisibilityPass (Async Compute immediately after final opaque HZB,
    ///      overlapping with GTAO, VSM, Lumen GI, CSM, and DeferredLighting)
    ///   2) StrandHairShadingPass (Async Compute after DeferredLighting, overlapping with
    ///      DrawObject / SkyInjection)
    ///   3) StrandHairCompositionPass (after SkyInjection on the main queue)
    /// Falls back gracefully when HZB or SkyInjection passes are absent in a custom .vrdg.
    /// </summary>
    internal static class StrandHairRenderGraphHook
    {
        private sealed class VisPassData
        {
            public StrandHairVisibilityPass Pass;
            public TextureHandle Depth;
            public TextureHandle Hzb;
        }

        private sealed class ShadePassData
        {
            public StrandHairShadingPass Pass;
            public StrandHairGpuPool GpuPool;
            public BufferHandle DirectionalLights;
            public BufferHandle PunctualLights;
            public BufferHandle AreaLights;
            public TextureHandle CsmShadowAtlas;
            public TextureHandle SkyIblCubemap;
        }

        private sealed class CompPassData
        {
            public StrandHairCompositionPass Pass;
            public StrandHairGpuPool GpuPool;
            public TextureHandle Color;
        }

        private static readonly BaseRenderFunc<VisPassData, ComputeGraphContext> s_ExecuteVis = ExecuteVis;
        private static readonly BaseRenderFunc<ShadePassData, ComputeGraphContext> s_ExecuteShade = ExecuteShade;
        private static readonly BaseRenderFunc<CompPassData, ComputeGraphContext> s_ExecuteComp = ExecuteComp;

        private static readonly List<BufferHandle> s_SlotMaskHandles = new List<BufferHandle>(16);
        private static readonly List<BufferHandle> s_SlotVertListHandles = new List<BufferHandle>(16);
        private static readonly List<BufferHandle> s_SlotShadeArgsHandles = new List<BufferHandle>(16);
        private static readonly List<BufferHandle> s_SlotShadingHandles = new List<BufferHandle>(16);

        private static StrandHairVisibilityPass s_VisPass;
        private static StrandHairShadingPass s_ShadePass;
        private static StrandHairCompositionPass s_CompPass;
        private static bool s_DisposeHooked;

        // Per-frame staged state across early HZB -> DeferredLighting -> SkyInjection boundaries
        private static bool s_VisibilityScheduledThisFrame;
        private static BufferHandle s_ScheduledVisAtlasHandle;

        private static bool s_HasPendingComposition;
        private static TextureHandle s_PendingColorHandle;
        private static BufferHandle s_PendingVisAtlasHandle;

        internal static void ClearPerFrameState()
        {
            s_VisibilityScheduledThisFrame = false;
            s_ScheduledVisAtlasHandle = default;
            s_HasPendingComposition = false;
            s_PendingColorHandle = default;
            s_PendingVisAtlasHandle = default;
            s_SlotMaskHandles.Clear();
            s_SlotVertListHandles.Clear();
            s_SlotShadeArgsHandles.Clear();
            s_SlotShadingHandles.Clear();
        }

        /// <summary>
        /// Phase S3 Early Async Compute hook: schedules StrandHairVisibilityPass right after
        /// the final opaque HZBGeneratePass so software rasterization + counting sort + vertex
        /// compaction run on the D3D12 Async Compute Queue in parallel with GTAO, VSM, Lumen GI,
        /// CSM, and DeferredLighting.
        /// </summary>
        internal static void TryRecordEarlyVisibilityAfterHZB(
            RenderGraph renderGraph,
            ContextContainer frameData,
            RenderGraphTexture depthTexture,
            RenderGraphTexture hzbTexture,
            bool enableAsyncCompute)
        {
            if (s_VisibilityScheduledThisFrame
                || HairGroomSystem.ActiveCount == 0
                || renderGraph == null
                || frameData == null
                || depthTexture == null
                || !depthTexture.innerHandle.IsValid())
            {
                return;
            }

            if (frameData.Contains<StrandHairFrameData>()
                && frameData.Get<StrandHairFrameData>().VisibilityRecordedThisFrame)
            {
                return;
            }

            EnsurePassesCreated();

            int targetWidth = depthTexture.desc != null && depthTexture.desc.Width > 0 ? depthTexture.desc.Width : 0;
            int targetHeight = depthTexture.desc != null && depthTexture.desc.Height > 0 ? depthTexture.desc.Height : 0;
            if (!s_VisPass.PrepareInternal(frameData, hzbTexture?.desc, targetWidth, targetHeight))
                return;

            ScheduleVisibilityPass(renderGraph, frameData, depthTexture, hzbTexture, enableAsyncCompute);
        }

        internal static void TryRecordAfterDeferredLighting(
            RenderGraph renderGraph,
            ContextContainer frameData,
            DeferredLightingPass deferredLightingPass,
            RenderGraphTexture hzbTexture,
            RenderGraphTexture csmShadowAtlasTexture,
            bool deferCompositionUntilAfterSky,
            bool enableAsyncCompute)
        {
            if (HairGroomSystem.ActiveCount == 0
                || renderGraph == null
                || frameData == null
                || deferredLightingPass == null)
            {
                ClearPerFrameState();
                return;
            }

            RenderGraphTexture depthTex = deferredLightingPass.DepthTexture;
            RenderGraphTexture colorTex = deferredLightingPass.ColorTexture;
            if (depthTex == null || !depthTex.innerHandle.IsValid()
                || colorTex == null || !colorTex.innerHandle.IsValid())
            {
                ClearPerFrameState();
                return;
            }

            EnsurePassesCreated();

            // Fallback: if early visibility after HZB did not run (e.g. graph without HZB), record it now
            if (!s_VisibilityScheduledThisFrame)
            {
                if (frameData.Contains<StrandHairFrameData>()
                    && frameData.Get<StrandHairFrameData>().VisibilityRecordedThisFrame)
                {
                    ClearPerFrameState();
                    return;
                }

                int targetWidth = colorTex.desc != null && colorTex.desc.Width > 0 ? colorTex.desc.Width : 0;
                int targetHeight = colorTex.desc != null && colorTex.desc.Height > 0 ? colorTex.desc.Height : 0;
                if (!s_VisPass.PrepareInternal(frameData, hzbTexture?.desc, targetWidth, targetHeight))
                {
                    ClearPerFrameState();
                    return;
                }

                ScheduleVisibilityPass(renderGraph, frameData, depthTex, hzbTexture, enableAsyncCompute);
            }

            if (!s_VisibilityScheduledThisFrame || !s_ScheduledVisAtlasHandle.IsValid())
            {
                ClearPerFrameState();
                return;
            }

            if (!s_ShadePass.PrepareInternal(frameData, s_VisPass.GpuPool, deferredLightingPass))
            {
                ClearPerFrameState();
                return;
            }

            if (!s_CompPass.PrepareInternal(frameData, s_VisPass.GpuPool))
            {
                ClearPerFrameState();
                return;
            }

            TextureHandle colorHandle = colorTex.innerHandle;
            BufferHandle visAtlasHandle = s_ScheduledVisAtlasHandle;

            BufferHandle dirLightsHandle = ResolveBufferHandle(deferredLightingPass.DirectionalLightBuffer);
            BufferHandle puncLightsHandle = ResolveBufferHandle(deferredLightingPass.PunctualLightBuffer);
            BufferHandle areaLightsHandle = ResolveBufferHandle(deferredLightingPass.AreaLightBuffer);
            TextureHandle csmHandle = csmShadowAtlasTexture != null && csmShadowAtlasTexture.innerHandle.IsValid()
                ? csmShadowAtlasTexture.innerHandle
                : renderGraph.defaultResources.blackTextureArrayXR;
            TextureHandle skyHandle = deferredLightingPass.SkyIBLCubemapTexture != null
                && deferredLightingPass.SkyIBLCubemapTexture.innerHandle.IsValid()
                ? deferredLightingPass.SkyIBLCubemapTexture.innerHandle
                : default;

            bool useAsync = enableAsyncCompute && StrandHairGpuPool.EnableAsyncCompute;

            // 2. StrandHairShadingPass (Async Compute overlapping DrawObject & SkyInjection)
            using (var shadeBuilder = renderGraph.AddComputePass<ShadePassData>(
                nameof(StrandHairShadingPass),
                out var shadeData))
            {
                shadeData.Pass = s_ShadePass;
                shadeData.GpuPool = s_VisPass.GpuPool;
                shadeData.DirectionalLights = dirLightsHandle;
                shadeData.PunctualLights = puncLightsHandle;
                shadeData.AreaLights = areaLightsHandle;
                shadeData.CsmShadowAtlas = csmHandle;
                shadeData.SkyIblCubemap = skyHandle;

                if (dirLightsHandle.IsValid())
                    shadeBuilder.UseBuffer(dirLightsHandle, AccessFlags.Read);
                if (puncLightsHandle.IsValid())
                    shadeBuilder.UseBuffer(puncLightsHandle, AccessFlags.Read);
                if (areaLightsHandle.IsValid())
                    shadeBuilder.UseBuffer(areaLightsHandle, AccessFlags.Read);
                if (csmHandle.IsValid())
                    shadeBuilder.UseTexture(csmHandle, AccessFlags.Read);
                if (skyHandle.IsValid())
                    shadeBuilder.UseTexture(skyHandle, AccessFlags.Read);
                for (int i = 0; i < s_SlotMaskHandles.Count; i++)
                    shadeBuilder.UseBuffer(s_SlotMaskHandles[i], AccessFlags.Read);
                for (int i = 0; i < s_SlotVertListHandles.Count; i++)
                    shadeBuilder.UseBuffer(s_SlotVertListHandles[i], AccessFlags.Read);
                for (int i = 0; i < s_SlotShadeArgsHandles.Count; i++)
                    shadeBuilder.UseBuffer(s_SlotShadeArgsHandles[i], AccessFlags.Read);
                for (int i = 0; i < s_SlotShadingHandles.Count; i++)
                    shadeBuilder.UseBuffer(s_SlotShadingHandles[i], AccessFlags.Write);
                if (useAsync)
                    shadeBuilder.EnableAsyncCompute(true);
                shadeBuilder.AllowPassCulling(false);
                shadeBuilder.SetRenderFunc(s_ExecuteShade);
            }

            // 3. StrandHairCompositionPass (after SkyInjectionPass if active, else immediately)
            if (deferCompositionUntilAfterSky)
            {
                s_HasPendingComposition = true;
                s_PendingColorHandle = colorHandle;
                s_PendingVisAtlasHandle = visAtlasHandle;
            }
            else
            {
                RecordCompositionPass(renderGraph, colorHandle, visAtlasHandle);
                ClearPerFrameState();
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
            RecordCompositionPass(renderGraph, colorHandle, visAtlasHandle);
            ClearPerFrameState();
        }

        private static void ScheduleVisibilityPass(
            RenderGraph renderGraph,
            ContextContainer frameData,
            RenderGraphTexture depthTexture,
            RenderGraphTexture hzbTexture,
            bool enableAsyncCompute)
        {
            s_SlotMaskHandles.Clear();
            s_SlotVertListHandles.Clear();
            s_SlotShadeArgsHandles.Clear();
            s_SlotShadingHandles.Clear();

            var hairFrameData = frameData.Get<StrandHairFrameData>();
            if (hairFrameData == null || hairFrameData.VisAtlasBuffer == null || !hairFrameData.VisAtlasBuffer.IsValid())
                return;

            int allocCount = hairFrameData.Allocations.Count;
            for (int slotIdx = 0; slotIdx < allocCount; slotIdx++)
            {
                StrandHairModelAtlasAllocation alloc = hairFrameData.Allocations[slotIdx];
                if (!alloc.IsVisible || alloc.TileCount <= 0)
                    continue;

                HairGroomRenderEntry entry = hairFrameData.ActiveEntries[alloc.EntryIndex];
                HairGroomGpuResource gpuRes = entry.Asset?.GetOrCreateGpuResource();
                if (gpuRes == null || !gpuRes.IsValid)
                    continue;

                StrandHairModelScratchSlot slot = s_VisPass.GpuPool.GetOrCreateSlot(
                    slotIdx,
                    gpuRes.VertexCount,
                    gpuRes.SegmentCount);
                if (slot.VisibleVertexMaskBuffer != null && slot.VisibleVertexMaskBuffer.IsValid())
                    s_SlotMaskHandles.Add(renderGraph.ImportBuffer(slot.VisibleVertexMaskBuffer));
                if (slot.VisibleVertexListBuffer != null && slot.VisibleVertexListBuffer.IsValid())
                    s_SlotVertListHandles.Add(renderGraph.ImportBuffer(slot.VisibleVertexListBuffer));
                if (slot.IndirectShadeArgsBuffer != null && slot.IndirectShadeArgsBuffer.IsValid())
                    s_SlotShadeArgsHandles.Add(renderGraph.ImportBuffer(slot.IndirectShadeArgsBuffer));
                if (slot.VertexShadingBuffer != null && slot.VertexShadingBuffer.IsValid())
                    s_SlotShadingHandles.Add(renderGraph.ImportBuffer(slot.VertexShadingBuffer));
            }

            TextureHandle depthHandle = depthTexture.innerHandle;
            TextureHandle hzbHandle = hzbTexture != null && hzbTexture.innerHandle.IsValid()
                ? hzbTexture.innerHandle
                : default;
            BufferHandle visAtlasHandle = renderGraph.ImportBuffer(hairFrameData.VisAtlasBuffer);
            bool useAsync = enableAsyncCompute && StrandHairGpuPool.EnableAsyncCompute;

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
                for (int i = 0; i < s_SlotMaskHandles.Count; i++)
                    visBuilder.UseBuffer(s_SlotMaskHandles[i], AccessFlags.Write);
                for (int i = 0; i < s_SlotVertListHandles.Count; i++)
                    visBuilder.UseBuffer(s_SlotVertListHandles[i], AccessFlags.Write);
                for (int i = 0; i < s_SlotShadeArgsHandles.Count; i++)
                    visBuilder.UseBuffer(s_SlotShadeArgsHandles[i], AccessFlags.Write);
                if (useAsync)
                    visBuilder.EnableAsyncCompute(true);
                visBuilder.AllowPassCulling(false);
                visBuilder.SetRenderFunc(s_ExecuteVis);
            }

            s_VisibilityScheduledThisFrame = true;
            s_ScheduledVisAtlasHandle = visAtlasHandle;
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
                for (int i = 0; i < s_SlotShadingHandles.Count; i++)
                    compBuilder.UseBuffer(s_SlotShadingHandles[i], AccessFlags.Read);
                compBuilder.UseTexture(colorHandle, AccessFlags.ReadWrite);
                compBuilder.AllowPassCulling(false);
                compBuilder.SetRenderFunc(s_ExecuteComp);
            }
        }

        private static BufferHandle ResolveBufferHandle(RenderGraphBuffer rgb)
        {
            return rgb != null && rgb.innerHandle.IsValid() ? rgb.innerHandle : default;
        }

        private static void ExecuteVis(VisPassData data, ComputeGraphContext ctx)
        {
            data.Pass.RecordWithHandles(ctx.cmd, data.Depth, data.Hzb);
        }

        private static void ExecuteShade(ShadePassData data, ComputeGraphContext ctx)
        {
            data.Pass.RecordWithHandles(
                ctx.cmd,
                ctx.defaultResources,
                data.GpuPool,
                data.DirectionalLights,
                data.PunctualLights,
                data.AreaLights,
                data.CsmShadowAtlas,
                data.SkyIblCubemap);
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

            if (s_ShadePass == null)
            {
                s_ShadePass = new StrandHairShadingPass();
                s_ShadePass.Create();
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
            s_ShadePass?.Dispose();
            s_ShadePass = null;
            s_CompPass?.Dispose();
            s_CompPass = null;
        }
    }
}
