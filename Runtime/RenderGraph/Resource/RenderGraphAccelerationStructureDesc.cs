using System;
using UnityEngine;
using UnityEngine.Rendering;
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
        public RayTracingAccelerationStructure.ManagementMode ManagementMode =
            RayTracingAccelerationStructure.ManagementMode.Automatic;
        public RayTracingAccelerationStructure.RayTracingModeMask RayTracingModeMask =
            RayTracingAccelerationStructure.RayTracingModeMask.Everything;
        public LayerMask LayerMask = ~0;
        public RayTracingAccelerationStructureBuildFlags BuildFlagsStaticGeometries =
            RayTracingAccelerationStructureBuildFlags.None;
        public RayTracingAccelerationStructureBuildFlags BuildFlagsDynamicGeometries =
            RayTracingAccelerationStructureBuildFlags.None;
        public bool EnableCompaction;

        public RenderGraphAccelerationStructureDesc Clone()
        {
            return (RenderGraphAccelerationStructureDesc)MemberwiseClone();
        }

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
        /// Converts this serializable descriptor to Unity's RayTracingAccelerationStructure.Settings.
        /// </summary>
        public RayTracingAccelerationStructure.Settings ToSettings()
        {
            return new RayTracingAccelerationStructure.Settings
            {
                managementMode = ManagementMode,
                rayTracingModeMask = RayTracingModeMask,
                layerMask = LayerMask,
                buildFlagsStaticGeometries = BuildFlagsStaticGeometries,
                buildFlagsDynamicGeometries = BuildFlagsDynamicGeometries,
                enableCompaction = EnableCompaction,
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
        /// Creates a RenderGraphAccelerationStructureDesc from Unity's RayTracingAccelerationStructure.Settings.
        /// </summary>
        public static RenderGraphAccelerationStructureDesc FromSettings(RayTracingAccelerationStructure.Settings settings)
        {
            return new RenderGraphAccelerationStructureDesc
            {
                ManagementMode = settings.managementMode,
                RayTracingModeMask = settings.rayTracingModeMask,
                LayerMask = settings.layerMask,
                BuildFlagsStaticGeometries = settings.buildFlagsStaticGeometries,
                BuildFlagsDynamicGeometries = settings.buildFlagsDynamicGeometries,
                EnableCompaction = settings.enableCompaction,
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

    [Serializable]
    public sealed class RenderGraphAccelerationStructure : IDisposable
    {
        public RenderGraphAccelerationStructureDesc desc;

        [NonSerialized] private RayTracingAccelerationStructure m_AccelerationStructure;
        [NonSerialized] private bool m_OwnsAccelerationStructure = true;

        public RenderGraphAccelerationStructure()
        {
            desc = new RenderGraphAccelerationStructureDesc();
        }

        internal RayTracingAccelerationStructureHandle innerHandle;

        internal bool HasAccelerationStructure => m_AccelerationStructure != null;

        internal RayTracingAccelerationStructure GetOrCreateAccelerationStructure()
        {
            EnsureCreated();
            return m_AccelerationStructure;
        }

        public void EnsureCreated()
        {
            if (m_AccelerationStructure != null)
                return;

            var settings = desc != null
                ? desc.ToSettings()
                : new RenderGraphAccelerationStructureDesc().ToSettings();
            m_AccelerationStructure = new RayTracingAccelerationStructure(settings);
            m_OwnsAccelerationStructure = true;
        }

        public void SetAccelerationStructure(RayTracingAccelerationStructure accelerationStructure, bool transferOwnership = false)
        {
            if (ReferenceEquals(m_AccelerationStructure, accelerationStructure))
            {
                m_OwnsAccelerationStructure = transferOwnership;
                return;
            }

            ReleaseOwnedAccelerationStructure();
            m_AccelerationStructure = accelerationStructure;
            m_OwnsAccelerationStructure = transferOwnership;
            innerHandle = default;
        }

        public void Dispose()
        {
            ReleaseOwnedAccelerationStructure();
            m_AccelerationStructure = null;
            m_OwnsAccelerationStructure = false;
            innerHandle = default;
        }

        public static implicit operator RayTracingAccelerationStructureHandle(RenderGraphAccelerationStructure accelerationStructure)
        {
            return accelerationStructure != null ? accelerationStructure.innerHandle : default;
        }

        public static implicit operator RayTracingAccelerationStructure(RenderGraphAccelerationStructure accelerationStructure)
        {
            return accelerationStructure?.m_AccelerationStructure;
        }

        private void ReleaseOwnedAccelerationStructure()
        {
            if (m_OwnsAccelerationStructure)
                m_AccelerationStructure?.Dispose();
        }
    }
}
