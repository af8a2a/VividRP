using NUnit.Framework;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderGraphAccelerationStructureDescTests
    {
        [Test]
        public void ToSettings_PreservesConfiguredFields()
        {
            var desc = new RenderGraphAccelerationStructureDesc
            {
                Name = "SceneRTAS",
                ManagementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
                RayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.DynamicGeometry,
                LayerMask = 1 << 3,
                BuildFlagsStaticGeometries = RayTracingAccelerationStructureBuildFlags.PreferFastTrace,
                BuildFlagsDynamicGeometries = RayTracingAccelerationStructureBuildFlags.PreferFastBuild,
                EnableCompaction = true,
            };

            var settings = desc.ToSettings();
            var renderGraphDesc = desc.ToAccelerationStructureDesc();

            Assert.That(renderGraphDesc.name, Is.EqualTo("SceneRTAS"));
            Assert.That(settings.managementMode, Is.EqualTo(RayTracingAccelerationStructure.ManagementMode.Manual));
            Assert.That(settings.rayTracingModeMask, Is.EqualTo(RayTracingAccelerationStructure.RayTracingModeMask.DynamicGeometry));
            Assert.That(settings.layerMask, Is.EqualTo(1 << 3));
            Assert.That(settings.buildFlagsStaticGeometries, Is.EqualTo(RayTracingAccelerationStructureBuildFlags.PreferFastTrace));
            Assert.That(settings.buildFlagsDynamicGeometries, Is.EqualTo(RayTracingAccelerationStructureBuildFlags.PreferFastBuild));
            Assert.That(settings.enableCompaction, Is.True);
        }

        [Test]
        public void IsEquivalentTo_ReturnsFalse_WhenDescriptorSettingsDiffer()
        {
            var lhs = new RenderGraphAccelerationStructureDesc
            {
                Name = "SceneRTAS",
                ManagementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
                LayerMask = 1 << 2,
            };
            var rhs = lhs.Clone();

            Assert.That(lhs.IsEquivalentTo(rhs), Is.True);

            rhs.LayerMask = 1 << 3;

            Assert.That(lhs.IsEquivalentTo(rhs), Is.False);
        }
    }
}
