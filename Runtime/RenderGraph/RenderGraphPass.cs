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

    /// <summary>
    /// Provides resolved RenderGraph handles to the pass during Record().
    /// Handles are keyed by the field name declared on the pass class.
    /// </summary>
    public class PassRecordContext
    {
        private readonly Dictionary<string, TextureHandle> m_Textures = new();
        private readonly Dictionary<string, BufferHandle> m_Buffers = new();
        private readonly Dictionary<string, RayTracingAccelerationStructureHandle> m_AccelerationStructures = new();

        public void SetTexture(string fieldName, TextureHandle handle) => m_Textures[fieldName] = handle;
        public void SetBuffer(string fieldName, BufferHandle handle) => m_Buffers[fieldName] = handle;

        public TextureHandle GetTexture(string fieldName)
        {
            return m_Textures.TryGetValue(fieldName, out var h) ? h : default;
        }

        public BufferHandle GetBuffer(string fieldName)
        {
            return m_Buffers.TryGetValue(fieldName, out var h) ? h : default;
        }

        public RayTracingAccelerationStructureHandle GetAccelerationStructure(string fieldName)
        {
            return m_AccelerationStructures.TryGetValue(fieldName, out var h) ? h : default;
        }
    }

    public interface IRenderPass
    {
        /// <summary>
        /// Prepare runtime resources (e.g. dynamic count buffer).
        /// Called each frame before the RenderGraph pass is recorded.
        /// After Prepare, the RenderGraph will automatically use the resource info
        /// collected by Initialize() to set up builder calls.
        /// </summary>
        void Prepare(ContextContainer frameData);

        /// <summary>
        /// Record rendering commands. Called from within the RenderGraph render func.
        /// Use the context to access resolved handles by field name.
        /// </summary>
        void Record(PassRecordContext context);

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
                    case RenderGraphTextureDesc:
                        entry.ResourceType = PassResourceType.Texture;
                        textures.Add(entry);
                        break;
                    case RenderGraphBufferDesc:
                        entry.ResourceType = PassResourceType.Buffer;
                        buffers.Add(entry);
                        break;
                    case RenderGraphAccelerationStructureDesc:
                        entry.ResourceType = PassResourceType.AccelerationStructure;
                        accelStructs.Add(entry);
                        break;
                }
            }

            return new PassResource
            {
                Textures = textures.ToArray(),
                Buffers = buffers.ToArray(),
            };
        }
    }


    public interface IComputePass : IRenderPass
    {
    }


    public interface IRasterPass : IRenderPass
    {
    }


    public interface IUnsafePass : IRenderPass
    {
    }
}