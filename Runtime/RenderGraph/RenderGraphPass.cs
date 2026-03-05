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

        /// <summary>
        /// All entries across all resource types.
        /// </summary>
        public IEnumerable<PassResourceEntry> AllEntries
        {
            get
            {
                foreach (var e in Textures) yield return e;
                foreach (var e in Buffers) yield return e;
            }
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
            var type = GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var textures = new List<PassResourceEntry>();
            var buffers = new List<PassResourceEntry>();
            var accelStructs = new List<PassResourceEntry>();

            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (attr == null)
                    continue;

                var value = field.GetValue(this);
                if (value == null)
                    continue;

                var entry = new PassResourceEntry
                {
                    Field = field,
                    Name = string.IsNullOrEmpty(attr.Name) ? field.Name : attr.Name,
                    Access = attr.Access,
                    AttachmentIndex = attr.AttachmentIndex,
                    IsDepthAttachment = attr.IsDepthAttachment,
                    Descriptor = value
                };

                switch (value)
                {
                    case RenderGraphTexture:
                        entry.ResourceType = PassResourceType.Texture;
                        textures.Add(entry);
                        break;
                    case RenderGraphBuffer:
                        entry.ResourceType = PassResourceType.Buffer;
                        buffers.Add(entry);
                        break;
                        // case RenderGraphAccelerationStructureDesc:
                        //     entry.ResourceType = PassResourceType.AccelerationStructure;
                        //     accelStructs.Add(entry);
                        //     break;
                }
            }

            return new PassResource
            {
                Textures = textures.ToArray(),
                Buffers = buffers.ToArray(),
            };
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
