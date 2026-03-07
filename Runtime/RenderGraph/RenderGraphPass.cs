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
            }
        }
    }

    internal static class PassResourceCollector
    {
        public static PassResource Collect(object pass)
        {
            var type = pass.GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var textures = new List<PassResourceEntry>();
            var buffers = new List<PassResourceEntry>();
            var renderLists = new List<PassResourceEntry>();

            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (attr == null)
                    continue;

                var value = field.GetValue(pass);
                if (value == null)
                    continue;

                switch (value)
                {
                    case RenderGraphTexture texture:
                        textures.Add(CreateEntry(
                            field,
                            string.IsNullOrEmpty(attr.Name) ? field.Name : attr.Name,
                            attr.Access,
                            PassResourceType.Texture,
                            texture,
                            attr.AttachmentIndex,
                            attr.IsDepthAttachment));
                        break;
                    case IEnumerable<RenderGraphTexture> textureCollection:
                        AddTextureCollectionEntries(textures, field, attr, textureCollection);
                        break;
                    case RenderGraphBuffer buffer:
                        buffers.Add(CreateEntry(
                            field,
                            string.IsNullOrEmpty(attr.Name) ? field.Name : attr.Name,
                            attr.Access,
                            PassResourceType.Buffer,
                            buffer,
                            attr.AttachmentIndex,
                            attr.IsDepthAttachment));
                        break;
                    case RenderGraphRenderList renderList:
                        renderLists.Add(CreateEntry(
                            field,
                            string.IsNullOrEmpty(attr.Name) ? field.Name : attr.Name,
                            attr.Access,
                            PassResourceType.RenderList,
                            renderList,
                            attr.AttachmentIndex,
                            attr.IsDepthAttachment));
                        break;
                }
            }

            return new PassResource
            {
                Textures = textures.ToArray(),
                Buffers = buffers.ToArray(),
                RenderLists = renderLists.ToArray(),
            };
        }

        private static PassResourceEntry CreateEntry(
            FieldInfo field,
            string name,
            AccessFlags access,
            PassResourceType resourceType,
            object descriptor,
            int attachmentIndex,
            bool isDepthAttachment)
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
            };
        }

        private static void AddTextureCollectionEntries(
            List<PassResourceEntry> textures,
            FieldInfo field,
            RenderGraphResource attr,
            IEnumerable<RenderGraphTexture> textureCollection)
        {
            var baseName = string.IsNullOrEmpty(attr.Name) ? field.Name : attr.Name;
            var collectionIndex = 0;

            foreach (var texture in textureCollection)
            {
                var entryAttachmentIndex = attr.AttachmentIndex >= 0
                    ? attr.AttachmentIndex + collectionIndex
                    : -1;

                var entryNameSuffix = entryAttachmentIndex >= 0
                    ? entryAttachmentIndex.ToString()
                    : collectionIndex.ToString();

                if (texture != null)
                {
                    textures.Add(CreateEntry(
                        null,
                        $"{baseName}{entryNameSuffix}",
                        attr.Access,
                        PassResourceType.Texture,
                        texture,
                        entryAttachmentIndex,
                        attr.IsDepthAttachment));
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
    }

    public abstract class ComputePass : IRenderPass
    {
        public abstract void Create();
        public abstract void Prepare(ContextContainer frameData);

        /// <summary>
        /// Record rendering commands. Called from within the RenderGraph render func.
        /// Use the context to access resolved handles by field name.
        /// </summary>
        public abstract void Record(ComputeGraphContext context);

        public abstract void Dispose();
    }

    public abstract class RasterPass : IRenderPass
    {
        public abstract void Create();
        public abstract void Prepare(ContextContainer frameData);

        /// <summary>
        /// Record rendering commands. Called from within the RenderGraph render func.
        /// Use the context to access resolved handles by field name.
        /// </summary>
        public abstract void Record(RasterGraphContext context);

        public abstract void Dispose();
    }

    public abstract class UnsafePass : IRenderPass
    {
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
        /// Use the context to access resolved handles by field name.
        /// </summary>
        public abstract void Record(UnsafeGraphContext context);

        public abstract void Dispose();
    }
}
