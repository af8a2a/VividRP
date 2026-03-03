using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    /// <summary>
    /// Serializable acceleration structure descriptor for RenderGraph resources.
    /// Mirrors UnityEngine.Rendering.RenderGraphModule.RayTracingAccelerationStructureDesc but can be serialized in assets.
    /// </summary>
    [Serializable]
    public class RenderGraphAccelerationStructureDesc
    {
        public string Name = "AccelerationStructure";

        /// <summary>
        /// Converts this serializable descriptor to Unity's RayTracingAccelerationStructureDesc.
        /// </summary>
        public RayTracingAccelerationStructureDesc ToAccelerationStructureDesc()
        {
            return new RayTracingAccelerationStructureDesc
            {
                name = Name
            };
        }

        /// <summary>
        /// Creates a RenderGraphAccelerationStructureDesc from Unity's RayTracingAccelerationStructureDesc.
        /// </summary>
        public static RenderGraphAccelerationStructureDesc FromAccelerationStructureDesc(RayTracingAccelerationStructureDesc desc)
        {
            return new RenderGraphAccelerationStructureDesc
            {
                Name = desc.name
            };
        }

        /// <summary>
        /// Creates a default descriptor for an acceleration structure.
        /// </summary>
        public static RenderGraphAccelerationStructureDesc Create(string name = "AccelerationStructure")
        {
            return new RenderGraphAccelerationStructureDesc
            {
                Name = name
            };
        }
    }
}
