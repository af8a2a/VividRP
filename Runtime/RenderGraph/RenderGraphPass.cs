using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Serialization;

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
        public PassBypassRule[] BypassRules = Array.Empty<PassBypassRule>();

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

    public sealed class PassBypassRule
    {
        public string SourceFieldName;
        public string OutputFieldName;
        public PassResourceType ResourceType;
        public FieldInfo SourceField;
        public FieldInfo OutputField;
        internal PassResourceEntry SourceEntry;
        internal PassResourceEntry OutputEntry;
        internal int AppliedBypassVersion;
    }

    internal static class RenderGraphPassReflectionUtility
    {
        private const BindingFlags DeclaredInstanceFieldFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        internal static IEnumerable<FieldInfo> EnumerateInstanceFields(this Type type)
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

        internal static IEnumerable<FieldInfo> EnumerateRenderGraphResourceFields(this Type type)
        {
            foreach (var field in EnumerateInstanceFields(type))
            {
                if (field.GetCustomAttribute<RenderGraphResource>() != null)
                    yield return field;
            }
        }

        internal static FieldInfo GetInstanceField(this Type type, string fieldName)
        {
            if (type == null || string.IsNullOrEmpty(fieldName))
                return null;

            foreach (var field in EnumerateInstanceFields(type))
            {
                if (field.Name == fieldName)
                    return field;
            }

            foreach (var field in EnumerateInstanceFields(type))
            {
                foreach (var formerlySerializedAs in field.GetCustomAttributes<FormerlySerializedAsAttribute>())
                {
                    if (formerlySerializedAs.oldName == fieldName)
                        return field;
                }
            }

            return null;
        }

        internal static string GetRenderGraphResourceName(this FieldInfo field, RenderGraphResource attr)
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

        internal static bool HasTransientResourceAttribute(this FieldInfo field)
        {
            return field?.GetCustomAttribute<TransientResourceAttribute>() != null;
        }

        internal static bool IsSupportedTransientResourceFieldType(this Type fieldType)
        {
            return fieldType == typeof(RenderGraphTexture)
                || fieldType == typeof(RenderGraphBuffer);
        }

        internal static bool IsDeclaredTransientResourceField(this FieldInfo field)
        {
            return field != null
                && field.GetCustomAttribute<RenderGraphResource>() != null
                && HasTransientResourceAttribute(field);
        }

        internal static bool IsTransientResourceField(this FieldInfo field)
        {
            return IsDeclaredTransientResourceField(field)
                && IsSupportedTransientResourceFieldType(field.FieldType);
        }

        internal static IEnumerable<FrameResourceSlotAttribute> EnumerateFrameResourceSlotAccesses(this Type type)
        {
            var metadata = PassTypeMetadataCache.GetOrCreate(type);
            return metadata != null ? metadata.SlotAccesses : Array.Empty<FrameResourceSlotAttribute>();
        }

        internal static IEnumerable<CameraHistoryAccessAttribute> EnumerateCameraHistoryAccesses(this Type type)
        {
            var metadata = PassTypeMetadataCache.GetOrCreate(type);
            return metadata != null ? metadata.HistoryAccesses : Array.Empty<CameraHistoryAccessAttribute>();
        }

        internal static bool HasCameraHistoryWriteAccess(this Type type)
        {
            var metadata = PassTypeMetadataCache.GetOrCreate(type);
            return metadata != null && metadata.HasCameraHistoryWrite;
        }
    }

    internal static class PassResourceCollector
    {
        public static PassResource Collect(this object pass)
        {
            var type = pass.GetType();
            var metadata = PassTypeMetadataCache.GetOrCreate(type);
            var resourceFields = metadata.ResourceFields;

            var textures = new List<PassResourceEntry>();
            var buffers = new List<PassResourceEntry>();
            var renderLists = new List<PassResourceEntry>();
            var accelerationStructures = new List<PassResourceEntry>();
            var bypassRules = new List<PassBypassRule>();

            for (var i = 0; i < resourceFields.Length; i++)
            {
                var fieldMeta = resourceFields[i];
                var field = fieldMeta.Field;
                var attr = fieldMeta.ResourceAttribute;
                var isTransient = fieldMeta.IsTransient;

                var value = fieldMeta.Getter(pass);
                if (value == null)
                    continue;

                switch (value)
                {
                    case RenderGraphTexture texture:
                        textures.Add(CreateEntry(
                            field,
                            fieldMeta.Getter,
                            fieldMeta.DisplayName,
                            attr.Access,
                            PassResourceType.Texture,
                            texture,
                            attr.AttachmentIndex,
                            attr.InputAttachmentIndex,
                            attr.IsDepthAttachment,
                            isTransient));
                        break;
                    case IEnumerable<RenderGraphTexture> textureCollection:
                        AddTextureCollectionEntries(textures, field, attr, textureCollection);
                        break;
                    case RenderGraphBuffer buffer:
                        buffers.Add(CreateEntry(
                            field,
                            fieldMeta.Getter,
                            fieldMeta.DisplayName,
                            attr.Access,
                            PassResourceType.Buffer,
                            buffer,
                            attr.AttachmentIndex,
                            attr.InputAttachmentIndex,
                            attr.IsDepthAttachment,
                            isTransient));
                        break;
                    case RenderGraphRenderList renderList:
                        renderLists.Add(CreateEntry(
                            field,
                            fieldMeta.Getter,
                            fieldMeta.DisplayName,
                            attr.Access,
                            PassResourceType.RenderList,
                            renderList,
                            attr.AttachmentIndex,
                            attr.InputAttachmentIndex,
                            attr.IsDepthAttachment,
                            isTransient: false));
                        break;
                    case RenderGraphAccelerationStructure accelerationStructure:
                        accelerationStructures.Add(CreateEntry(
                            field,
                            fieldMeta.Getter,
                            fieldMeta.DisplayName,
                            attr.Access,
                            PassResourceType.AccelerationStructure,
                            accelerationStructure,
                            attr.AttachmentIndex,
                            attr.InputAttachmentIndex,
                            attr.IsDepthAttachment,
                            isTransient: false));
                        break;
                }

                if (fieldMeta.BypassAttribute != null)
                    TryAddBypassRule(type, field, attr, isTransient, fieldMeta.BypassAttribute, bypassRules);
            }

            var textureEntries = textures.ToArray();
            var bufferEntries = buffers.ToArray();
            var renderListEntries = renderLists.ToArray();
            var accelerationStructureEntries = accelerationStructures.ToArray();
            var bypassRuleEntries = bypassRules.ToArray();
            ResolveBypassRuleEntries(bypassRuleEntries, textureEntries, bufferEntries);

            return new PassResource
            {
                Textures = textureEntries,
                Buffers = bufferEntries,
                RenderLists = renderListEntries,
                AccelerationStructures = accelerationStructureEntries,
                BypassRules = bypassRuleEntries,
            };
        }

        private static void ResolveBypassRuleEntries(
            PassBypassRule[] bypassRules,
            PassResourceEntry[] textureEntries,
            PassResourceEntry[] bufferEntries)
        {
            if (bypassRules == null)
                return;

            for (var i = 0; i < bypassRules.Length; i++)
            {
                var rule = bypassRules[i];
                if (rule == null)
                    continue;

                var entries = rule.ResourceType == PassResourceType.Buffer ? bufferEntries : textureEntries;
                rule.SourceEntry = FindEntry(entries, rule.SourceField);
                rule.OutputEntry = FindEntry(entries, rule.OutputField);
            }
        }

        private static PassResourceEntry FindEntry(PassResourceEntry[] entries, FieldInfo field)
        {
            if (entries == null || field == null)
                return null;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry != null && ReferenceEquals(entry.Field, field))
                    return entry;
            }

            return null;
        }

        private static void TryAddBypassRule(
            Type passType,
            FieldInfo outputField,
            RenderGraphResource outputAttr,
            bool outputIsTransient,
            PassBypassAttribute bypassAttr,
            List<PassBypassRule> bypassRules)
        {
            if (bypassAttr == null)
                return;

            if (!TryValidateBypassRule(
                    passType,
                    outputField,
                    outputAttr,
                    outputIsTransient,
                    bypassAttr.SourceFieldName,
                    out var sourceField,
                    out var resourceType,
                    out var error))
            {
                Debug.LogWarning(error);
                return;
            }

            bypassRules.Add(new PassBypassRule
            {
                SourceFieldName = sourceField.Name,
                OutputFieldName = outputField.Name,
                ResourceType = resourceType,
                SourceField = sourceField,
                OutputField = outputField,
            });
        }

        internal static bool TryValidateBypassRule(
            Type passType,
            FieldInfo outputField,
            RenderGraphResource outputAttr,
            bool outputIsTransient,
            string sourceFieldName,
            out FieldInfo sourceField,
            out PassResourceType resourceType,
            out string error)
        {
            sourceField = null;
            resourceType = default;
            error = null;

            var passTypeName = passType?.FullName ?? "<Unknown Pass>";
            var outputFieldName = outputField?.Name ?? "<Unknown Field>";
            if (passType == null || outputField == null || outputAttr == null)
            {
                error = $"[VividRP] Invalid {nameof(PassBypassAttribute)} on '{passTypeName}.{outputFieldName}'. The output field must be marked with {nameof(RenderGraphResource)}.";
                return false;
            }

            if (outputIsTransient || outputField.IsDeclaredTransientResourceField())
            {
                error = $"[VividRP] Invalid {nameof(PassBypassAttribute)} on '{passTypeName}.{outputFieldName}'. Transient resources cannot be bypass outputs.";
                return false;
            }

            if ((outputAttr.Access & AccessFlags.Write) == 0)
            {
                error = $"[VividRP] Invalid {nameof(PassBypassAttribute)} on '{passTypeName}.{outputFieldName}'. The bypass output must be writable.";
                return false;
            }

            if (!TryGetBypassResourceType(outputField.FieldType, out resourceType))
            {
                error = $"[VividRP] Invalid {nameof(PassBypassAttribute)} on '{passTypeName}.{outputFieldName}'. Only {nameof(RenderGraphTexture)} and {nameof(RenderGraphBuffer)} fields are supported.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sourceFieldName))
            {
                error = $"[VividRP] Invalid {nameof(PassBypassAttribute)} on '{passTypeName}.{outputFieldName}'. Source field name is empty.";
                return false;
            }

            var metadata = PassTypeMetadataCache.GetOrCreate(passType);
            RenderGraphResource sourceAttr;
            bool sourceIsDeclaredTransient;
            if (metadata.TryGetResourceField(sourceFieldName, out var sourceFieldMeta))
            {
                sourceField = sourceFieldMeta.Field;
                sourceAttr = sourceFieldMeta.ResourceAttribute;
                sourceIsDeclaredTransient = sourceFieldMeta.IsDeclaredTransient;
            }
            else
            {
                sourceField = passType.GetInstanceField(sourceFieldName);
                if (sourceField == null)
                {
                    error = $"[VividRP] Invalid {nameof(PassBypassAttribute)} on '{passTypeName}.{outputFieldName}'. Source field '{sourceFieldName}' was not found.";
                    return false;
                }

                sourceAttr = sourceField.GetCustomAttribute<RenderGraphResource>();
                sourceIsDeclaredTransient = sourceField.IsDeclaredTransientResourceField();
            }

            if (sourceAttr == null)
            {
                error = $"[VividRP] Invalid {nameof(PassBypassAttribute)} on '{passTypeName}.{outputFieldName}'. Source field '{sourceField.Name}' must be marked with {nameof(RenderGraphResource)}.";
                return false;
            }

            if (sourceIsDeclaredTransient)
            {
                error = $"[VividRP] Invalid {nameof(PassBypassAttribute)} on '{passTypeName}.{outputFieldName}'. Source field '{sourceField.Name}' cannot be transient.";
                return false;
            }

            if (sourceField.FieldType != outputField.FieldType)
            {
                error = $"[VividRP] Invalid {nameof(PassBypassAttribute)} on '{passTypeName}.{outputFieldName}'. Source field '{sourceField.Name}' must use the same resource type as the output.";
                return false;
            }

            if ((sourceAttr.Access & AccessFlags.Read) == 0)
            {
                error = $"[VividRP] Invalid {nameof(PassBypassAttribute)} on '{passTypeName}.{outputFieldName}'. Source field '{sourceField.Name}' must be readable.";
                return false;
            }

            return true;
        }

        private static bool TryGetBypassResourceType(Type fieldType, out PassResourceType resourceType)
        {
            if (fieldType == typeof(RenderGraphTexture))
            {
                resourceType = PassResourceType.Texture;
                return true;
            }

            if (fieldType == typeof(RenderGraphBuffer))
            {
                resourceType = PassResourceType.Buffer;
                return true;
            }

            resourceType = default;
            return false;
        }

        private static PassResourceEntry CreateEntry(
            FieldInfo field,
            Func<object, object> getter,
            string name,
            AccessFlags access,
            PassResourceType resourceType,
            object descriptor,
            int attachmentIndex,
            int inputAttachmentIndex,
            bool isDepthAttachment,
            bool isTransient)
        {
            return new PassResourceEntry
            {
                Field = field,
                Getter = getter,
                Name = name,
                Access = access,
                ResourceType = resourceType,
                Descriptor = descriptor,
                AttachmentIndex = attachmentIndex,
                InputAttachmentIndex = inputAttachmentIndex,
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
            var baseName = field.GetRenderGraphResourceName(attr);
            var collectionIndex = 0;

            foreach (var texture in textureCollection)
            {
                var entryAttachmentIndex = attr.AttachmentIndex >= 0
                    ? attr.AttachmentIndex + collectionIndex
                    : -1;
                var entryInputAttachmentIndex = attr.InputAttachmentIndex >= 0
                    ? attr.InputAttachmentIndex + collectionIndex
                    : -1;

                if (texture != null)
                {
                    textures.Add(CreateEntry(
                        null,
                        null,
                        RenderGraphPassReflectionUtility.GetRenderGraphResourceCollectionName(baseName, entryAttachmentIndex, collectionIndex),
                        attr.Access,
                        PassResourceType.Texture,
                        texture,
                        entryAttachmentIndex,
                        entryInputAttachmentIndex,
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
        internal static bool TryRefresh(this object pass, PassResource resources)
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

                var descriptor = entry.Getter != null
                    ? entry.Getter(pass)
                    : field.GetValue(pass);
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

    internal interface ISceneColorBoundaryPass
    {
        RenderGraphTexture GetSceneColorInput();
    }

    internal interface IPostProcessSourceOverridePass : IDynamicPassResourceLayout, ISceneColorBoundaryPass
    {
        RenderGraphTexture GetSourceTexture();

        RenderGraphTexture ISceneColorBoundaryPass.GetSceneColorInput() => GetSourceTexture();

        void SetSourceTexture(RenderGraphTexture sourceTexture);

        void RestoreSourceTexture();
    }

    /// <summary>
    /// Allows a logical RenderGraph pass node to record one or more fine-grained CoreRP RenderGraph sub-passes,
    /// enabling per-sub-pass transient resource lifetimes, physical memory aliasing, and finer barrier scheduling.
    /// </summary>
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

    public readonly struct RenderGraphRecordingContext
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
            : this(
                renderGraph,
                frameData,
                passDefinition,
                enableAsyncCompute,
                textureCache,
                bufferCache,
                renderListCache,
                accelerationStructureCache,
                passResources: null,
                passName: passDefinition?.PassName)
        {
        }

        internal RenderGraphRecordingContext(
            RenderGraph renderGraph,
            ContextContainer frameData,
            RenderGraphPassDefinition passDefinition,
            bool enableAsyncCompute,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            Dictionary<RenderGraphBuffer, BufferHandle> bufferCache,
            Dictionary<RenderGraphRenderList, RendererListHandle> renderListCache,
            Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> accelerationStructureCache,
            PassResource passResources,
            string passName)
        {
            RenderGraph = renderGraph;
            FrameData = frameData;
            PassDefinition = passDefinition;
            EnableAsyncCompute = enableAsyncCompute;
            TextureCache = textureCache;
            BufferCache = bufferCache;
            RenderListCache = renderListCache;
            AccelerationStructureCache = accelerationStructureCache;
            PassResources = passResources;
            PassName = passName;
        }

        public RenderGraph RenderGraph { get; }

        public ContextContainer FrameData { get; }

        internal RenderGraphPassDefinition PassDefinition { get; }

        internal bool EnableAsyncCompute { get; }

        internal Dictionary<RenderGraphTexture, TextureHandle> TextureCache { get; }

        internal Dictionary<RenderGraphBuffer, BufferHandle> BufferCache { get; }

        internal Dictionary<RenderGraphRenderList, RendererListHandle> RenderListCache { get; }

        internal Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle> AccelerationStructureCache { get; }

        internal PassResource PassResources { get; }

        internal string PassName { get; }

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

        internal void RecordRasterPass(
            RasterPass pass,
            PassResource resource,
            string passName = null)
        {
            PassRecorder.RecordRasterPass(
                RenderGraph,
                pass,
                resource,
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

        internal void RecordDefaultComputePass(ComputePass pass, string passName = null)
        {
            if (pass == null || PassResources == null)
                return;

            RecordComputePass(pass, PassResources, PassDefinition, passName ?? PassName);
        }

        internal void RecordDefaultRasterPass(RasterPass pass, string passName = null)
        {
            if (pass == null || PassResources == null)
                return;

            RecordRasterPass(pass, PassResources, passName ?? PassName);
        }

        internal void RecordDefaultUnsafePass(UnsafePass pass, string passName = null)
        {
            if (pass == null || PassResources == null)
                return;

            RecordUnsafePass(pass, PassResources, PassDefinition, passName ?? PassName);
        }
    }

    internal static class RenderGraphPassExecutionUtility
    {
        internal static bool SupportsAsyncCompute(this Type passType)
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
            return this.Collect();
        }

        /// <summary>
        /// Updates resolution-dependent resources.
        /// Called before Prepare() the first time an active pass is prepared and whenever the
        /// actual render resolution changes. Width and height are always at least one.
        /// </summary>
        void Resize(int width, int height)
        {
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

        bool IsActive(ContextContainer frameData)
        {
            return true;
        }
    }

    public abstract class ComputePass : IRenderPass
    {
        protected  ProfilingSampler profilingSampler;

        public abstract void Create();
        public virtual void Resize(int width, int height) { }
        public abstract void Prepare(ContextContainer frameData);
        public virtual bool IsActive(ContextContainer frameData) => true;

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

    }

    public abstract class RasterPass : IRenderPass
    {
        protected ProfilingSampler profilingSampler;

        public abstract void Create();
        public virtual void Resize(int width, int height) { }
        public abstract void Prepare(ContextContainer frameData);
        public virtual bool IsActive(ContextContainer frameData) => true;

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

    }

    public abstract class UnsafePass : IRenderPass
    {

       protected ProfilingSampler profilingSampler;
        public abstract void Create();

        public virtual void Resize(int width, int height) { }

        /// <summary>
        /// Prepare runtime resources (e.g. dynamic count buffer).
        /// Called each frame before the RenderGraph pass is recorded.
        /// After Prepare, the RenderGraph will automatically use the resource info
        /// collected by Initialize() to set up builder calls.
        /// </summary>
        public abstract void Prepare(ContextContainer frameData);
        public virtual bool IsActive(ContextContainer frameData) => true;

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

    }
}
