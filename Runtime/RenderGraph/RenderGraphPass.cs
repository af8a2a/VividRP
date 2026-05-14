using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    /// <summary>
    /// Holds all resource entries collected from a pass via reflection.
    /// The RenderGraph recording layer uses this to create resources and set up builder calls.
    /// </summary>
    public class PassResource
    {
        public PassResourceEntry[] Textures = Array.Empty<PassResourceEntry>();
        public PassResourceEntry[] Buffers = Array.Empty<PassResourceEntry>();
        public PassResourceEntry[] RenderLists = Array.Empty<PassResourceEntry>();
        public PassResourceEntry[] AccelerationStructures = Array.Empty<PassResourceEntry>();

        /// <summary>
        /// All entries across all resource types.
        /// </summary>
        public IEnumerable<PassResourceEntry> AllEntries
        {
            get
            {
                foreach (var e in Textures) yield return e;
                foreach (var e in Buffers) yield return e;
                foreach (var e in RenderLists) yield return e;
                foreach (var e in AccelerationStructures) yield return e;
            }
        }
    }

    internal static class RenderGraphPassReflectionUtility
    {
        private const BindingFlags DeclaredInstanceFieldFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        internal static IEnumerable<FieldInfo> EnumerateInstanceFields(Type type)
        {
            if (type == null)
                yield break;

            var hierarchy = new Stack<Type>();
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
                hierarchy.Push(current);

            while (hierarchy.Count > 0)
            {
                foreach (var field in hierarchy.Pop().GetFields(DeclaredInstanceFieldFlags))
                    yield return field;
            }
        }

        internal static IEnumerable<FieldInfo> EnumerateRenderGraphResourceFields(Type type)
        {
            foreach (var field in EnumerateInstanceFields(type))
            {
                if (field.GetCustomAttribute<RenderGraphResource>() != null)
                    yield return field;
            }
        }

        internal static FieldInfo GetInstanceField(Type type, string fieldName)
        {
            if (type == null || string.IsNullOrEmpty(fieldName))
                return null;

            foreach (var field in EnumerateInstanceFields(type))
            {
                if (field.Name == fieldName)
                    return field;
            }

            return null;
        }

        internal static string GetRenderGraphResourceName(FieldInfo field, RenderGraphResource attr)
        {
            if (field == null)
                return attr?.Name;

            return string.IsNullOrEmpty(attr?.Name)
                ? field.Name
                : attr.Name;
        }

        internal static string GetRenderGraphResourceCollectionName(string baseName, int attachmentIndex, int collectionIndex)
        {
            var entryNameSuffix = attachmentIndex >= 0
                ? attachmentIndex.ToString()
                : collectionIndex.ToString();

            return $"{baseName}{entryNameSuffix}";
        }

        internal static bool HasTransientResourceAttribute(FieldInfo field)
        {
            return field?.GetCustomAttribute<TransientResourceAttribute>() != null;
        }

        internal static bool IsSupportedTransientResourceFieldType(Type fieldType)
        {
            return fieldType == typeof(RenderGraphTexture)
                || fieldType == typeof(RenderGraphBuffer);
        }

        internal static bool IsDeclaredTransientResourceField(FieldInfo field)
        {
            return field != null
                && field.GetCustomAttribute<RenderGraphResource>() != null
                && HasTransientResourceAttribute(field);
        }

        internal static bool IsTransientResourceField(FieldInfo field)
        {
            return IsDeclaredTransientResourceField(field)
                && IsSupportedTransientResourceFieldType(field.FieldType);
        }
    }

    internal static class PassResourceCollector
    {
        public static PassResource Collect(object pass)
        {
            var type = pass.GetType();

            var textures = new List<PassResourceEntry>();
            var buffers = new List<PassResourceEntry>();
            var renderLists = new List<PassResourceEntry>();
            var accelerationStructures = new List<PassResourceEntry>();

            foreach (var field in RenderGraphPassReflectionUtility.EnumerateRenderGraphResourceFields(type))
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                var isTransient = RenderGraphPassReflectionUtility.IsTransientResourceField(field);

                var value = field.GetValue(pass);
                if (value == null)
                    continue;

                switch (value)
                {
                    case RenderGraphTexture texture:
                        textures.Add(CreateEntry(
                            field,
                            RenderGraphPassReflectionUtility.GetRenderGraphResourceName(field, attr),
                            attr.Access,
                            PassResourceType.Texture,
                            texture,
                            attr.AttachmentIndex,
                            attr.IsDepthAttachment,
                            isTransient));
                        break;
                    case IEnumerable<RenderGraphTexture> textureCollection:
                        AddTextureCollectionEntries(textures, field, attr, textureCollection);
                        break;
                    case RenderGraphBuffer buffer:
                        buffers.Add(CreateEntry(
                            field,
                            RenderGraphPassReflectionUtility.GetRenderGraphResourceName(field, attr),
                            attr.Access,
                            PassResourceType.Buffer,
                            buffer,
                            attr.AttachmentIndex,
                            attr.IsDepthAttachment,
                            isTransient));
                        break;
                    case RenderGraphRenderList renderList:
                        renderLists.Add(CreateEntry(
                            field,
                            RenderGraphPassReflectionUtility.GetRenderGraphResourceName(field, attr),
                            attr.Access,
                            PassResourceType.RenderList,
                            renderList,
                            attr.AttachmentIndex,
                            attr.IsDepthAttachment,
                            isTransient: false));
                        break;
                    case RenderGraphAccelerationStructure accelerationStructure:
                        accelerationStructures.Add(CreateEntry(
                            field,
                            RenderGraphPassReflectionUtility.GetRenderGraphResourceName(field, attr),
                            attr.Access,
                            PassResourceType.AccelerationStructure,
                            accelerationStructure,
                            attr.AttachmentIndex,
                            attr.IsDepthAttachment,
                            isTransient: false));
                        break;
                }
            }

            return new PassResource
            {
                Textures = textures.ToArray(),
                Buffers = buffers.ToArray(),
                RenderLists = renderLists.ToArray(),
                AccelerationStructures = accelerationStructures.ToArray(),
            };
        }

        private static PassResourceEntry CreateEntry(
            FieldInfo field,
            string name,
            AccessFlags access,
            PassResourceType resourceType,
            object descriptor,
            int attachmentIndex,
            bool isDepthAttachment,
            bool isTransient)
        {
            return new PassResourceEntry
            {
                Field = field,
                Name = name,
                Access = access,
                ResourceType = resourceType,
                Descriptor = descriptor,
                AttachmentIndex = attachmentIndex,
                IsDepthAttachment = isDepthAttachment,
                IsTransient = isTransient,
            };
        }

        private static void AddTextureCollectionEntries(
            List<PassResourceEntry> textures,
            FieldInfo field,
            RenderGraphResource attr,
            IEnumerable<RenderGraphTexture> textureCollection)
        {
            var baseName = RenderGraphPassReflectionUtility.GetRenderGraphResourceName(field, attr);
            var collectionIndex = 0;

            foreach (var texture in textureCollection)
            {
                var entryAttachmentIndex = attr.AttachmentIndex >= 0
                    ? attr.AttachmentIndex + collectionIndex
                    : -1;

                if (texture != null)
                {
                    textures.Add(CreateEntry(
                        null,
                        RenderGraphPassReflectionUtility.GetRenderGraphResourceCollectionName(baseName, entryAttachmentIndex, collectionIndex),
                        attr.Access,
                        PassResourceType.Texture,
                        texture,
                        entryAttachmentIndex,
                        attr.IsDepthAttachment,
                        isTransient: false));
                }

                collectionIndex++;
            }
        }
    }

    public interface IDynamicPassResourceLayout
    {
        bool IsPassResourceLayoutDirty { get; }

        void ClearPassResourceLayoutDirty();
    }

    /// <summary>
    /// Marks a dynamic pass whose resource fields stay fixed while their descriptor instances may change.
    /// </summary>
    public interface IStablePassResourceLayout : IDynamicPassResourceLayout
    {
    }

    internal static class PassResourceReferenceRefreshUtility
    {
        internal static bool TryRefresh(object pass, PassResource resources)
        {
            if (pass == null || resources == null)
                return false;

            return TryRefreshEntries(pass, resources.Textures)
                && TryRefreshEntries(pass, resources.Buffers)
                && TryRefreshEntries(pass, resources.RenderLists)
                && TryRefreshEntries(pass, resources.AccelerationStructures);
        }

        private static bool TryRefreshEntries(object pass, PassResourceEntry[] entries)
        {
            if (entries == null)
                return true;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var field = entry?.Field;
                if (field == null)
                    continue;

                var descriptor = field.GetValue(pass);
                if (descriptor == null || !MatchesResourceType(descriptor, entry.ResourceType))
                    return false;

                entry.Descriptor = descriptor;
            }

            return true;
        }

        private static bool MatchesResourceType(object descriptor, PassResourceType resourceType)
        {
            return resourceType switch
            {
                PassResourceType.Texture => descriptor is RenderGraphTexture,
                PassResourceType.Buffer => descriptor is RenderGraphBuffer,
                PassResourceType.RenderList => descriptor is RenderGraphRenderList,
                PassResourceType.AccelerationStructure => descriptor is RenderGraphAccelerationStructure,
                _ => false
            };
        }
    }

    public interface IAsyncComputeSupportedPass
    {
    }

    public interface IAllowGlobalStateModificationPass
    {
    }

    /// <summary>
    /// Marks passes that sample the shared blue-noise resources during execution.
    /// </summary>
    public interface IBlueNoiseConsumerPass
    {
    }

    /// <summary>
    /// Marks passes whose work has effects outside same-frame RenderGraph resource consumers.
    /// Examples include history updates, readbacks, imported resource updates, or other persistent side effects.
    /// </summary>
    public interface IRenderGraphSideEffectPass
    {
    }

    public interface IRenderGizmoPrePostProcessBoundaryPass
    {
    }

    internal interface IPostProcessSourceOverridePass : IDynamicPassResourceLayout
    {
        RenderGraphTexture GetSourceTexture();

        void SetSourceTexture(RenderGraphTexture sourceTexture);

        void RestoreSourceTexture();
    }

    //only used for  Antialiasing
    public interface IRenderGraphRecordingPass
    {
        void RecordGraph(RenderGraphRecordingContext context);
    }

    /// <summary>
    /// Optional hook for passes that must refresh resource references after all passes have completed Prepare()
    /// and before the pass's resources are collected for RenderGraph recording.
    /// </summary>
    public interface IRenderGraphPreparePass
    {
        void PrepareRenderGraph(ContextContainer frameData);
    }

    public sealed class RenderGraphRecordingContext
    {
        internal RenderGraphRecordingContext(
            RenderGraph renderGraph,
            ContextContainer frameData,
            RenderGraphPassDefinition passDefinition,
            bool enableAsyncCompute,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache)
        {
            RenderGraph = renderGraph;
            FrameData = frameData;
            PassDefinition = passDefinition;
            EnableAsyncCompute = enableAsyncCompute;
            TextureCache = textureCache;
            BufferCache = bufferCache;
            RenderListCache = renderListCache;
            AccelerationStructureCache = accelerationStructureCache;
        }

        public RenderGraph RenderGraph { get; }

        public ContextContainer FrameData { get; }

        internal RenderGraphPassDefinition PassDefinition { get; }

        internal bool EnableAsyncCompute { get; }

        internal Dictionary<RenderGraphTexture, TextureHandle> TextureCache { get; }

        internal Dictionary<RenderGraphBuffer, BufferHandle> BufferCache { get; }

        internal Dictionary<RenderGraphRenderList, RendererListHandle> RenderListCache { get; }

        internal Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> AccelerationStructureCache { get; }

        internal TextureHandle GetOrCreateTextureHandle(RenderGraphTexture texture)
        {
            if (RenderGraph == null || texture == null)
                return default;

            return PassRecorder.GetOrCreateTextureHandle(RenderGraph, texture, TextureCache);
        }

        internal BufferHandle GetOrCreateBufferHandle(RenderGraphBuffer buffer)
        {
            if (RenderGraph == null || buffer == null)
                return default;

            return PassRecorder.GetOrCreateBufferHandle(RenderGraph, buffer, BufferCache);
        }

        internal void RegisterTextureHandle(RenderGraphTexture texture, TextureHandle handle)
        {
            if (texture == null || !handle.IsValid())
                return;

            texture.innerHandle = handle;
            TextureCache[texture] = handle;
        }

        internal void RecordComputePass(
            ComputePass pass,
            PassResource resource,
            RenderGraphPassDefinition passDefinition = null,
            string passName = null)
        {
            PassRecorder.RecordComputePass(
                RenderGraph,
                pass,
                resource,
                passDefinition,
                EnableAsyncCompute,
                TextureCache,
                BufferCache,
                RenderListCache,
                AccelerationStructureCache,
                passName);
        }

        internal void RecordUnsafePass(
            UnsafePass pass,
            PassResource resource,
            RenderGraphPassDefinition passDefinition = null,
            string passName = null)
        {
            PassRecorder.RecordUnsafePass(
                RenderGraph,
                pass,
                resource,
                passDefinition,
                EnableAsyncCompute,
                TextureCache,
                BufferCache,
                RenderListCache,
                AccelerationStructureCache,
                passName);
        }
    }

    internal static class RenderGraphPassExecutionUtility
    {
        internal static bool SupportsAsyncCompute(Type passType)
        {
            if (passType == null)
                return false;

            if (!typeof(IAsyncComputeSupportedPass).IsAssignableFrom(passType))
                return false;

            return typeof(ComputePass).IsAssignableFrom(passType)
                || typeof(UnsafePass).IsAssignableFrom(passType);
        }
    }

    public interface IRenderPass
    {

        /// <summary>
        /// Collects all [RenderGraphResource]-annotated fields via reflection
        /// and returns a PassResource describing the pass's resource requirements.
        /// Called once (or when the pass layout changes) to bake resource info.
        /// </summary>
        PassResource Initialize()
        {
            return PassResourceCollector.Collect(this);
        }

        /// <summary>
        /// Prepare runtime resources (e.g. dynamic count buffer).
        /// Called each frame before the RenderGraph pass is recorded.
        /// After Prepare, the RenderGraph will automatically use the resource info
        /// collected by Initialize() to set up builder calls.
        /// </summary>
        void Prepare(ContextContainer frameData);

        /// <summary>
        /// Called once to create persistent objects (e.g. shaders/materials).
        /// </summary>
        void Create();

        /// <summary>
        /// Called when the pipeline is disposed or the graph is recompiled.
        /// </summary>
        void Dispose();

        /// <summary>
        /// Imports an external RTHandle into the RenderGraph for use in this pass.
        /// Call this in Prepare() to import external resources.
        /// Returns a TextureHandle that can be assigned to pass member variables.
        /// </summary>
        /// <param name="rtHandle">The external RTHandle to import</param>
        /// <returns>TextureHandle that can be used in Record()</returns>
        TextureHandle Import(RTHandle rtHandle);

        /// <summary>
        /// Allocates or reuses a pass-scoped history texture pair during Prepare().
        /// previous receives the last valid frame, current is registered as this frame's output.
        /// </summary>
        bool AllocHistoryTexture(string key, RenderGraphTexture previous, RenderGraphTexture current, RenderGraphTextureDesc desc);

        /// <summary>
        /// Allocates or reuses a pass-scoped history buffer pair during Prepare().
        /// previous receives the last valid frame, current is registered as this frame's output.
        /// </summary>
        bool AllocHistoryBuffer(string key, RenderGraphBuffer previous, RenderGraphBuffer current, RenderGraphBufferDesc desc);
    }

    public abstract class ComputePass : IRenderPass
    {
        protected  ProfilingSampler profilingSampler;

        public abstract void Create();
        public abstract void Prepare(ContextContainer frameData);

        /// <summary>
        /// Record rendering commands. Called from within the RenderGraph render func.
        /// Use the context to access resolved handles and frame ContextItem values.
        /// </summary>
        public abstract void Record(ComputePassContext context);

        public abstract void Dispose();

        /// <summary>
        /// Imports an external RTHandle into the RenderGraph for use in this pass.
        /// Call this in Prepare() to import external resources.
        /// </summary>
        public TextureHandle Import(RTHandle rtHandle)
        {
            return PassRecorder.ImportTextureForPass(this, rtHandle);
        }

        public bool AllocHistoryTexture(string key, RenderGraphTexture previous, RenderGraphTexture current, RenderGraphTextureDesc desc)
        {
            return PassRecorder.AllocHistoryTextureForPass(this, key, previous, current, desc);
        }

        public bool AllocHistoryBuffer(string key, RenderGraphBuffer previous, RenderGraphBuffer current, RenderGraphBufferDesc desc)
        {
            return PassRecorder.AllocHistoryBufferForPass(this, key, previous, current, desc);
        }
    }

    public abstract class RasterPass : IRenderPass
    {
        protected ProfilingSampler profilingSampler;

        public abstract void Create();
        public abstract void Prepare(ContextContainer frameData);

        /// <summary>
        /// Record rendering commands. Called from within the RenderGraph render func.
        /// Use the context to access resolved handles and frame ContextItem values.
        /// </summary>
        public abstract void Record(RasterPassContext context);

        public abstract void Dispose();

        /// <summary>
        /// Imports an external RTHandle into the RenderGraph for use in this pass.
        /// Call this in Prepare() to import external resources.
        /// </summary>
        public TextureHandle Import(RTHandle rtHandle)
        {
            return PassRecorder.ImportTextureForPass(this, rtHandle);
        }

        public bool AllocHistoryTexture(string key, RenderGraphTexture previous, RenderGraphTexture current, RenderGraphTextureDesc desc)
        {
            return PassRecorder.AllocHistoryTextureForPass(this, key, previous, current, desc);
        }

        public bool AllocHistoryBuffer(string key, RenderGraphBuffer previous, RenderGraphBuffer current, RenderGraphBufferDesc desc)
        {
            return PassRecorder.AllocHistoryBufferForPass(this, key, previous, current, desc);
        }
    }

    public abstract class UnsafePass : IRenderPass
    {

       protected ProfilingSampler profilingSampler;
        public abstract void Create();

        /// <summary>
        /// Prepare runtime resources (e.g. dynamic count buffer).
        /// Called each frame before the RenderGraph pass is recorded.
        /// After Prepare, the RenderGraph will automatically use the resource info
        /// collected by Initialize() to set up builder calls.
        /// </summary>
        public abstract void Prepare(ContextContainer frameData);

        /// <summary>
        /// Record rendering commands. Called from within the RenderGraph render func.
        /// Use the context to access resolved handles and frame ContextItem values.
        /// </summary>
        public abstract void Record(UnsafePassContext context);

        public abstract void Dispose();

        /// <summary>
        /// Imports an external RTHandle into the RenderGraph for use in this pass.
        /// Call this in Prepare() to import external resources.
        /// </summary>
        public TextureHandle Import(RTHandle rtHandle)
        {
            return PassRecorder.ImportTextureForPass(this, rtHandle);
        }

        public bool AllocHistoryTexture(string key, RenderGraphTexture previous, RenderGraphTexture current, RenderGraphTextureDesc desc)
        {
            return PassRecorder.AllocHistoryTextureForPass(this, key, previous, current, desc);
        }

        public bool AllocHistoryBuffer(string key, RenderGraphBuffer previous, RenderGraphBuffer current, RenderGraphBufferDesc desc)
        {
            return PassRecorder.AllocHistoryBufferForPass(this, key, previous, current, desc);
        }
    }
}
