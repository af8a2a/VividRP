using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    /// <summary>
    /// Automatically records a RenderGraph pass from a PassResource.
    /// Creates resources, calls builder methods, and invokes the pass's Record().
    /// </summary>
    public static partial class PassRecorder
    {
        private const BindingFlags BuilderMethodFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private delegate ResourceHandle UseResourceDelegate(
            RenderGraphBuilders builder, in ResourceHandle handle, AccessFlags access);

        // CoreRP has no typed RTAS builder API. Bind its resource path once to avoid
        // per-pass reflection, boxed handles/flags, and an Invoke argument array.
        private static readonly UseResourceDelegate s_UseAccelerationStructureResource =
            (UseResourceDelegate)Delegate.CreateDelegate(
                typeof(UseResourceDelegate),
                typeof(RenderGraphBuilders).GetMethod("UseResource", BuilderMethodFlags));

        private readonly struct ImportedPassHandle : IEquatable<ImportedPassHandle>
        {
            private ImportedPassHandle(TextureHandle textureHandle, BufferHandle bufferHandle, bool isBuffer, AccessFlags access)
            {
                TextureHandle = textureHandle;
                BufferHandle = bufferHandle;
                IsBuffer = isBuffer;
                Access = access;
            }

            public static ImportedPassHandle Texture(TextureHandle handle, AccessFlags access)
            {
                return new ImportedPassHandle(handle, default, false, access);
            }

            public static ImportedPassHandle Buffer(BufferHandle handle, AccessFlags access)
            {
                return new ImportedPassHandle(default, handle, true, access);
            }

            public TextureHandle TextureHandle { get; }

            public BufferHandle BufferHandle { get; }

            public bool IsBuffer { get; }

            public AccessFlags Access { get; }

            public bool IsValid => IsBuffer ? BufferHandle.IsValid() : TextureHandle.IsValid();

            public bool HasSameHandle(ImportedPassHandle other)
            {
                if (IsBuffer != other.IsBuffer)
                    return false;

                return IsBuffer
                    ? AreImportedBufferHandlesEqual(BufferHandle, other.BufferHandle)
                    : TextureHandle.Equals(other.TextureHandle);
            }

            public ImportedPassHandle WithAccess(AccessFlags access)
            {
                return new ImportedPassHandle(TextureHandle, BufferHandle, IsBuffer, access);
            }

            public bool Equals(ImportedPassHandle other)
            {
                return Access == other.Access && HasSameHandle(other);
            }

            public override bool Equals(object obj)
            {
                return obj is ImportedPassHandle other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(TextureHandle, BufferHandle, IsBuffer, Access);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe bool AreImportedBufferHandlesEqual(BufferHandle left, BufferHandle right)
        {
            // BufferHandle has no typed equality, while converting it to GraphicsBuffer requires the execution-time registry.
            return UnsafeUtility.MemCmp(
                UnsafeUtility.AddressOf(ref left),
                UnsafeUtility.AddressOf(ref right),
                UnsafeUtility.SizeOf<BufferHandle>()) == 0;
        }

        private class ComputePassData
        {
            public ComputePass Pass;
            public RenderPassProfilerMarkers Markers;
        }

        private class RasterPassData
        {
            public RasterPass Pass;
            public RenderPassProfilerMarkers Markers;
        }

        private class UnsafePassData
        {
            public UnsafePass Pass;
            public RenderPassProfilerMarkers Markers;
        }

        private static readonly BaseRenderFunc<ComputePassData, ComputeGraphContext> s_ComputeRenderFunc =
            ExecuteComputePass;
        private static readonly BaseRenderFunc<RasterPassData, RasterGraphContext> s_RasterRenderFunc =
            ExecuteRasterPass;
        private static readonly BaseRenderFunc<UnsafePassData, UnsafeGraphContext> s_UnsafeRenderFunc =
            ExecuteUnsafePass;

        /// <summary>
        /// Records a compute pass. Creates resources from PassResource, sets up builder calls,
        /// and wires the render func to call pass.Record().
        /// </summary>
        internal static void RecordComputePass(
            RenderGraph renderGraph,
            ComputePass pass,
            PassResource resource,
            RenderGraphPassDefinition passDefinition,
            bool enableAsyncCompute,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache,
            string passName = null)
        {
            var markers = GetPassMarkers(pass, passName);
            using var recordGraphScope = markers.RecordGraph.Auto();
            using (markers.RecordGraphBuild.Auto())
            {
                using var builder = renderGraph.AddComputePass<ComputePassData>(
                    markers.GraphName, out var passData);

                passData.Pass = pass;
                passData.Markers = markers;

                using (markers.RecordGraphSetupResources.Auto())
                {
                    SetupComputeResources(
                        renderGraph,
                        builder,
                        resource,
                        textureCache,
                        bufferCache,
                        renderListCache,
                        accelerationStructureCache);
                }

                using (markers.RecordGraphSetupImportedHandles.Auto())
                {
                    SetupImportedHandles(builder, pass);
                }

                using (markers.RecordGraphConfigureBuilder.Auto())
                {
                    ConfigureGlobalStateModification(builder, pass);
                    builder.AllowPassCulling(ShouldAllowPassCulling(pass, resource));
                    if (ShouldEnableAsyncCompute(enableAsyncCompute, pass, passDefinition))
                        builder.EnableAsyncCompute(true);
                }

                using (markers.RecordGraphSetRenderFunc.Auto())
                {
                    builder.SetRenderFunc<ComputePassData>(s_ComputeRenderFunc);
                }
            }
        }

        /// <summary>
        /// Records a raster pass. Creates resources, sets up attachments and builder calls,
        /// and wires the render func to call pass.Record().
        /// </summary>
        internal static void RecordRasterPass(
            RenderGraph renderGraph,
            RasterPass pass,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache,
            string passName = null)
        {
            var markers = GetPassMarkers(pass, passName);
            using var recordGraphScope = markers.RecordGraph.Auto();
            using (markers.RecordGraphBuild.Auto())
            {
                using var builder = renderGraph.AddRasterRenderPass<RasterPassData>(
                    markers.GraphName, out var passData);

                passData.Pass = pass;
                passData.Markers = markers;

                using (markers.RecordGraphSetupResources.Auto())
                {
                    SetupRasterResources(
                        renderGraph,
                        builder,
                        resource,
                        textureCache,
                        bufferCache,
                        renderListCache,
                        accelerationStructureCache);
                }

                using (markers.RecordGraphSetupImportedHandles.Auto())
                {
                    SetupImportedHandles(builder, pass);
                }

                using (markers.RecordGraphConfigureBuilder.Auto())
                {
                    ConfigureGlobalStateModification(builder, pass);
                    builder.AllowPassCulling(ShouldAllowPassCulling(pass, resource));
                }

                using (markers.RecordGraphSetRenderFunc.Auto())
                {
                    builder.SetRenderFunc<RasterPassData>(s_RasterRenderFunc);
                }
            }
        }

        /// <summary>
        /// Records an unsafe pass. Creates resources from PassResource, sets up builder calls,
        /// and wires the render func to call pass.Record().
        /// </summary>
        internal static void RecordUnsafePass(
            RenderGraph renderGraph,
            UnsafePass pass,
            PassResource resource,
            RenderGraphPassDefinition passDefinition,
            bool enableAsyncCompute,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache,
            string passName = null)
        {
            var markers = GetPassMarkers(pass, passName);
            using var recordGraphScope = markers.RecordGraph.Auto();
            using (markers.RecordGraphBuild.Auto())
            {
                using var builder = renderGraph.AddUnsafePass<UnsafePassData>(
                    markers.GraphName, out var passData);

                passData.Pass = pass;
                passData.Markers = markers;

                using (markers.RecordGraphSetupResources.Auto())
                {
                    SetupUnsafeResources(
                        renderGraph,
                        builder,
                        resource,
                        textureCache,
                        bufferCache,
                        renderListCache,
                        accelerationStructureCache);
                }

                using (markers.RecordGraphSetupImportedHandles.Auto())
                {
                    SetupImportedHandles(builder, pass);
                }

                using (markers.RecordGraphConfigureBuilder.Auto())
                {
                    ConfigureGlobalStateModification(builder, pass);
                    builder.AllowPassCulling(ShouldAllowPassCulling(pass, resource));
                    if (ShouldEnableAsyncCompute(enableAsyncCompute, pass, passDefinition))
                        builder.EnableAsyncCompute(true);
                }

                using (markers.RecordGraphSetRenderFunc.Auto())
                {
                    builder.SetRenderFunc<UnsafePassData>(s_UnsafeRenderFunc);
                }
            }
        }

        private readonly struct WrittenHistoryEntry : IEquatable<WrittenHistoryEntry>
        {
            private readonly CameraHistoryTexture m_Texture;
            private readonly CameraHistoryBuffer m_Buffer;

            public WrittenHistoryEntry(CameraHistoryTexture texture)
            {
                m_Texture = texture;
                m_Buffer = null;
            }

            public WrittenHistoryEntry(CameraHistoryBuffer buffer)
            {
                m_Texture = null;
                m_Buffer = buffer;
            }

            public void MarkWritten()
            {
                m_Texture?.MarkWritten();
                m_Buffer?.MarkWritten();
            }

            public bool Equals(WrittenHistoryEntry other)
            {
                return ReferenceEquals(m_Texture, other.m_Texture)
                    && ReferenceEquals(m_Buffer, other.m_Buffer);
            }

            public override bool Equals(object obj)
            {
                return obj is WrittenHistoryEntry other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(m_Texture, m_Buffer);
            }
        }

        private static readonly Dictionary<IRenderPass, List<WrittenHistoryEntry>> s_PassWrittenHistories = new();

        internal static void RegisterHistoryWriteForPass(IRenderPass pass, CameraHistoryTexture history)
        {
            if (pass == null || history == null)
                return;

            RegisterHistoryWriteEntry(pass, new WrittenHistoryEntry(history));
        }

        internal static void RegisterHistoryWriteForPass(IRenderPass pass, CameraHistoryBuffer history)
        {
            if (pass == null || history == null)
                return;

            RegisterHistoryWriteEntry(pass, new WrittenHistoryEntry(history));
        }

        private static void RegisterHistoryWriteEntry(IRenderPass pass, WrittenHistoryEntry entry)
        {
            if (!s_PassWrittenHistories.TryGetValue(pass, out var entries))
            {
                entries = new List<WrittenHistoryEntry>(8);
                s_PassWrittenHistories[pass] = entries;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Equals(entry))
                    return;
            }

            entries.Add(entry);
        }

        internal static void MarkRegisteredHistoriesWritten(IRenderPass pass)
        {
            if (pass == null
                || !s_PassWrittenHistories.TryGetValue(pass, out var entries)
                || entries == null)
            {
                return;
            }

            for (var i = 0; i < entries.Count; i++)
                entries[i].MarkWritten();
        }

        private static void ClearPassWrittenHistories()
        {
            foreach (var entries in s_PassWrittenHistories.Values)
                entries?.Clear();
        }

        private static void DisposePassWrittenHistories()
        {
            s_PassWrittenHistories.Clear();
        }

        private static void RemovePassWrittenHistories(IRenderPass pass)
        {
            if (pass != null)
                s_PassWrittenHistories.Remove(pass);
        }

        private static void ExecuteComputePass(ComputePassData data, ComputeGraphContext ctx)
        {
            using var recordScope = new ProfilingScope(ctx.cmd, data.Markers.Record);
            data.Pass.Record(new ComputePassContext(ctx, s_FrameData));
            MarkRegisteredHistoriesWritten(data.Pass);
        }

        private static void ExecuteRasterPass(RasterPassData data, RasterGraphContext ctx)
        {
            using var recordScope = new ProfilingScope(ctx.cmd, data.Markers.Record);
            data.Pass.Record(new RasterPassContext(ctx, s_FrameData));
            MarkRegisteredHistoriesWritten(data.Pass);
        }

        private static void ExecuteUnsafePass(UnsafePassData data, UnsafeGraphContext ctx)
        {
            var passContext = new UnsafePassContext(ctx, s_FrameData);
            using var recordScope = new ProfilingScope(ctx.cmd, data.Markers.Record);
            data.Pass.Record(passContext);
            MarkRegisteredHistoriesWritten(data.Pass);
        }

        internal static bool ShouldEnableAsyncCompute(
            bool enableAsyncCompute,
            IRenderPass pass,
            RenderGraphPassDefinition passDefinition)
        {
            return enableAsyncCompute
                && pass != null
                && passDefinition != null
                && passDefinition.EnableAsyncCompute
                && SystemInfo.supportsAsyncCompute
                && pass.GetType().SupportsAsyncCompute();
        }

        private static void SetupImportedHandles(IBaseRenderGraphBuilder builder, IRenderPass pass)
        {
            if (pass is IBlueNoiseConsumerPass)
                BlueNoise.Instance?.RegisterPassResources(pass);

            if (builder == null
                || pass == null
                || !s_PassImportedHandles.TryGetValue(pass, out var importedHandles)
                || importedHandles == null)
            {
                return;
            }

            foreach (var importedHandle in importedHandles)
            {
                if (!importedHandle.IsValid)
                    continue;

                if (importedHandle.IsBuffer)
                    builder.UseBuffer(importedHandle.BufferHandle, importedHandle.Access);
                else
                    builder.UseTexture(importedHandle.TextureHandle, importedHandle.Access);
            }
        }

        internal static bool ShouldAllowPassCulling(IRenderPass pass, PassResource resource)
        {
            if (pass == null)
                return false;

            if (pass is IRenderGraphSideEffectPass || pass is IAllowGlobalStateModificationPass)
                return false;

            if (HasRegisteredHistoryWrites(pass) || HasDeclaredCameraHistoryWrite(pass.GetType()))
                return false;

            return HasWritableGraphOutput(pass, resource);
        }

        private static bool HasRegisteredHistoryWrites(IRenderPass pass)
        {
            return pass != null
                && s_PassWrittenHistories.TryGetValue(pass, out var entries)
                && entries != null
                && entries.Count > 0;
        }

        private static bool HasDeclaredCameraHistoryWrite(Type passType)
        {
            return passType != null && PassTypeMetadataCache.GetOrCreate(passType).HasCameraHistoryWrite;
        }

        private static bool HasWritableGraphOutput(IRenderPass pass, PassResource resource)
        {
            if (resource != null)
            {
                var textures = resource.Textures;
                if (textures != null)
                {
                    for (var i = 0; i < textures.Length; i++)
                    {
                        var entry = textures[i];
                        if (entry != null && !entry.IsTransient && entry.Texture != null && (entry.Access & AccessFlags.Write) != 0)
                            return true;
                    }
                }

                var buffers = resource.Buffers;
                if (buffers != null)
                {
                    for (var i = 0; i < buffers.Length; i++)
                    {
                        var entry = buffers[i];
                        if (entry != null && !entry.IsTransient && entry.Buffer != null && (entry.Access & AccessFlags.Write) != 0)
                            return true;
                    }
                }

                var accelerationStructures = resource.AccelerationStructures;
                if (accelerationStructures != null)
                {
                    for (var i = 0; i < accelerationStructures.Length; i++)
                    {
                        var entry = accelerationStructures[i];
                        if (entry != null && !entry.IsTransient && entry.AccelerationStructure != null && (entry.Access & AccessFlags.Write) != 0)
                            return true;
                    }
                }
            }

            if (pass != null
                && s_PassImportedHandles.TryGetValue(pass, out var importedHandles)
                && importedHandles != null)
            {
                for (var i = 0; i < importedHandles.Count; i++)
                {
                    var handle = importedHandles[i];
                    if (handle.IsValid && (handle.Access & AccessFlags.Write) != 0)
                        return true;
                }
            }

            return false;
        }

        private static void ConfigureGlobalStateModification(IBaseRenderGraphBuilder builder, IRenderPass pass)
        {
            if (builder == null || pass is not IAllowGlobalStateModificationPass)
                return;

            builder.AllowGlobalStateModification(true);
        }

        internal static TextureHandle GetOrCreateTextureHandle(
            RenderGraph renderGraph,
            RenderGraphTexture texture,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache)
        {
            if (textureCache.TryGetValue(texture, out var handle))
                return handle;

            // Check if this texture has an imported RTHandle
            if (texture != null && s_ImportedRTHandles.TryGetValue(texture, out var rtHandle))
            {
                handle = renderGraph.ImportTexture(rtHandle);
                texture.SetImportedHandle(handle);
                textureCache.Add(texture, handle);
                return handle;
            }

            if (texture != null && texture.HasImportedHandle)
            {
                handle = texture.innerHandle;
                textureCache.Add(texture, handle);
                return handle;
            }

            handle = renderGraph.CreateTexture(texture.desc);
            textureCache.Add(texture, handle);
            return handle;
        }

        internal static BufferHandle GetOrCreateBufferHandle(
            RenderGraph renderGraph,
            RenderGraphBuffer buffer,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache)
        {
            if (bufferCache.TryGetValue(buffer, out var handle))
                return handle;

            if (buffer != null && buffer.HasImportedHandle)
            {
                handle = buffer.innerHandle;
                bufferCache.Add(buffer, handle);
                return handle;
            }

            if (buffer != null && buffer.HasImportedBuffer)
            {
                handle = renderGraph.ImportBuffer(buffer.ImportedGraphicsBuffer);
                bufferCache.Add(buffer, handle);
                return handle;
            }

            handle = renderGraph.CreateBuffer(buffer.desc);
            bufferCache.Add(buffer, handle);
            return handle;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TextureHandle CreateTransientTextureHandle(
            IBaseRenderGraphBuilder builder,
            RenderGraphTexture texture)
        {
            if (builder == null || texture == null)
                return default;

            if (s_ImportedRTHandles.ContainsKey(texture) || texture.HasImportedHandle)
            {
                Debug.LogWarning(
                    $"[VividRP] Transient texture '{texture.desc?.Name ?? "<Unnamed>"}' ignores imported texture handles.");
            }

            var handle = builder.CreateTransientTexture(texture.desc);
            texture.innerHandle = handle;
            return handle;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BufferHandle CreateTransientBufferHandle(
            IBaseRenderGraphBuilder builder,
            RenderGraphBuffer buffer)
        {
            if (builder == null || buffer == null)
                return default;

            if (buffer.HasImportedBuffer || buffer.HasImportedHandle)
            {
                Debug.LogWarning(
                    $"[VividRP] Transient buffer '{buffer.desc?.Name ?? "<Unnamed>"}' ignores imported buffer handles.");
            }

            var handle = builder.CreateTransientBuffer(buffer.desc);
            buffer.ClearImportedHandle();
            buffer.innerHandle = handle;
            return handle;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RendererListHandle GetOrCreateRenderListHandle(
            RenderGraph renderGraph,
            RenderGraphRenderList renderList,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache)
        {
            if (renderList == null)
                return default;

            if (renderListCache.TryGetValue(renderList, out var handle))
                return handle;

            var camera = s_FrameData.GetOrCreate<VividCameraData>().camera;
            if (camera == null)
                return default;

            var renderingData = s_FrameData.GetOrCreate<VividRenderingData>();
            var desc = renderList.desc?.CreateRendererListDesc(renderingData.cullingResults, camera) ?? default;
            if (!desc.IsValid())
                return default;

            handle = renderGraph.CreateRendererList(desc);
            renderListCache.Add(renderList, handle);
            return handle;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RayTracingAccelerationStructureHandle GetOrCreateAccelerationStructureHandle(
            RenderGraph renderGraph,
            RenderGraphAccelerationStructure accelerationStructure,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            if (accelerationStructure == null)
                return default;

            if (accelerationStructureCache.TryGetValue(accelerationStructure, out var handle))
                return handle;

            if (!accelerationStructure.HasAccelerationStructure)
            {
                if (!SystemInfo.supportsRayTracing)
                    return default;

                accelerationStructure.EnsureCreated();
            }

            var nativeAccelerationStructure = (RayTracingAccelerationStructure)accelerationStructure;
            if (nativeAccelerationStructure == null)
                return default;

            var name = accelerationStructure.desc?.Name;
            handle = renderGraph.ImportRayTracingAccelerationStructure(nativeAccelerationStructure, name);
            accelerationStructure.innerHandle = handle;
            accelerationStructureCache.Add(accelerationStructure, handle);
            return handle;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetupRenderLists(
            RenderGraph renderGraph,
            IBaseRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache)
        {
            using var setupRenderListsScope = RenderPassProfilingUtility.RecordGraphSetupRenderListsMarker.Auto();

            var usesRendererList = false;

            foreach (var entry in resource.RenderLists)
            {
                var renderList = entry.RenderList;
                if (renderList == null)
                    continue;

                var handle = GetOrCreateRenderListHandle(renderGraph, renderList, renderListCache);
                renderList.innerHandle = handle;

                if (!handle.IsValid())
                    continue;

                builder.UseRendererList(handle);
                usesRendererList = true;
            }

            if (usesRendererList)
                builder.UseAllGlobalTextures(true);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetupAccelerationStructures(
            RenderGraph renderGraph,
            IBaseRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            using var setupAccelerationStructuresScope = RenderPassProfilingUtility.RecordGraphSetupAccelerationStructuresMarker.Auto();

            foreach (var entry in resource.AccelerationStructures)
            {
                var accelerationStructure = entry.AccelerationStructure;
                if (accelerationStructure == null)
                    continue;

                var handle = GetOrCreateAccelerationStructureHandle(
                    renderGraph,
                    accelerationStructure,
                    accelerationStructureCache);
                if (!handle.IsValid())
                    continue;

                UseAccelerationStructure(builder, handle, entry.Access);
            }
        }

        /// <summary>
        /// Sets up resources for compute and unsafe passes using IBaseRenderGraphBuilder.
        /// </summary>
        private static void SetupUnsafeResources(
            RenderGraph renderGraph,
            IUnsafeRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            using (RenderPassProfilingUtility.RecordGraphSetupTexturesMarker.Auto())
            {
                foreach (var entry in resource.Textures)
                {
                    var texture = entry.Texture;
                    if (texture == null) continue;

                    if (entry.IsTransient)
                    {
                        CreateTransientTextureHandle(builder, texture);
                        continue;
                    }

                    // Read-only textures are expected to be imported or created externally.
                    // Create a placeholder — the pipeline will replace this with the actual handle.
                    var handle = GetOrCreateTextureHandle(renderGraph, texture, textureCache);
                    texture.innerHandle = handle;
                    builder.UseTexture(handle, entry.Access);
                }
            }

            using (RenderPassProfilingUtility.RecordGraphSetupBuffersMarker.Auto())
            {
                foreach (var entry in resource.Buffers)
                {
                    var buffer = entry.Buffer;
                    if (buffer == null) continue;

                    if (entry.IsTransient)
                    {
                        CreateTransientBufferHandle(builder, buffer);
                        continue;
                    }

                    var handle = GetOrCreateBufferHandle(renderGraph, buffer, bufferCache);
                    buffer.innerHandle = handle;
                    builder.UseBuffer(handle, entry.Access);
                }
            }

            SetupRenderLists(renderGraph, builder, resource, renderListCache);
            SetupAccelerationStructures(renderGraph, builder, resource, accelerationStructureCache);
        }

        /// <summary>
        /// Sets up resources for compute and unsafe passes using IBaseRenderGraphBuilder.
        /// </summary>
        private static void SetupComputeResources(
            RenderGraph renderGraph,
            IComputeRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            using (RenderPassProfilingUtility.RecordGraphSetupTexturesMarker.Auto())
            {
                foreach (var entry in resource.Textures)
                {
                    var texture = entry.Texture;
                    if (texture == null) continue;

                    if (entry.IsTransient)
                    {
                        CreateTransientTextureHandle(builder, texture);
                        continue;
                    }

                    // Read-only textures are expected to be imported or created externally.
                    // Create a placeholder — the pipeline will replace this with the actual handle.
                    var handle = GetOrCreateTextureHandle(renderGraph, texture, textureCache);
                    texture.innerHandle = handle;
                    builder.UseTexture(handle, entry.Access);
                }
            }

            using (RenderPassProfilingUtility.RecordGraphSetupBuffersMarker.Auto())
            {
                foreach (var entry in resource.Buffers)
                {
                    var buffer = entry.Buffer;
                    if (buffer == null) continue;

                    if (entry.IsTransient)
                    {
                        CreateTransientBufferHandle(builder, buffer);
                        continue;
                    }

                    var handle = GetOrCreateBufferHandle(renderGraph, buffer, bufferCache);
                    buffer.innerHandle = handle;
                    builder.UseBuffer(handle, entry.Access);
                }
            }

            SetupRenderLists(renderGraph, builder, resource, renderListCache);
            SetupAccelerationStructures(renderGraph, builder, resource, accelerationStructureCache);
        }

        /// <summary>
        /// Sets up resources for raster passes. Handles color/depth attachments
        /// in addition to regular texture/buffer usage.
        /// </summary>
        private static void SetupRasterResources(
            RenderGraph renderGraph,
            IRasterRenderGraphBuilder builder,
            PassResource resource,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            using (RenderPassProfilingUtility.RecordGraphSetupTexturesMarker.Auto())
            {
                foreach (var entry in resource.Textures)
                {
                    var texture = entry.Texture;
                    if (texture == null) continue;

                    var handle = entry.IsTransient
                        ? CreateTransientTextureHandle(builder, texture)
                        : GetOrCreateTextureHandle(renderGraph, texture, textureCache);
                    texture.innerHandle = handle;

                    if (entry.InputAttachmentIndex >= 0)
                    {
                        builder.SetInputAttachment(handle, entry.InputAttachmentIndex, entry.Access);
                    }
                    else if (entry.IsDepthAttachment)
                    {
                        builder.SetRenderAttachmentDepth(handle, entry.Access);
                    }
                    else if (entry.AttachmentIndex >= 0)
                    {
                        builder.SetRenderAttachment(handle, entry.AttachmentIndex, entry.Access);
                    }
                    else
                    {
                        if (!entry.IsTransient)
                            builder.UseTexture(handle, entry.Access);
                    }
                }
            }

            using (RenderPassProfilingUtility.RecordGraphSetupBuffersMarker.Auto())
            {
                foreach (var entry in resource.Buffers)
                {
                    var buffer = entry.Buffer;
                    if (buffer == null) continue;

                    if (entry.IsTransient)
                    {
                        CreateTransientBufferHandle(builder, buffer);
                        continue;
                    }

                    var handle = GetOrCreateBufferHandle(renderGraph, buffer, bufferCache);
                    buffer.innerHandle = handle;
                    builder.UseBuffer(handle, entry.Access);
                }
            }

            SetupRenderLists(renderGraph, builder, resource, renderListCache);
            SetupAccelerationStructures(renderGraph, builder, resource, accelerationStructureCache);
        }

        internal static void UseAccelerationStructure(
            IBaseRenderGraphBuilder builder,
            RayTracingAccelerationStructureHandle handle,
            AccessFlags access)
        {
            if (builder == null || !handle.IsValid())
                return;

            InvokeAccelerationStructureBuilderMethod(builder, handle, access);
        }

        private static void InvokeAccelerationStructureBuilderMethod(
            IBaseRenderGraphBuilder builder,
            RayTracingAccelerationStructureHandle handle,
            AccessFlags access)
        {
            var builderAccess = access;

            if ((builderAccess & AccessFlags.Write) != 0
                && (builderAccess & AccessFlags.Read) == 0)
            {
                builderAccess |= AccessFlags.Discard;
            }

            s_UseAccelerationStructureResource((RenderGraphBuilders)builder, in handle.handle, builderAccess);
        }
    }
}
