using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class SubsurfaceScatteringPassTests
    {
        [Test]
        public void SubstrateContract_InlineSubsurfaceExtension_FitsWithinDefaultSliceBudget()
        {
            Assert.That(VividSubstrateContract.SubsurfaceExtUintCount, Is.EqualTo(2));
            Assert.That(VividSubstrateContract.SubsurfaceMfpMinMeters, Is.GreaterThan(0f));
            Assert.That(VividSubstrateContract.SubsurfaceMfpMaxMeters, Is.GreaterThan(VividSubstrateContract.SubsurfaceMfpMinMeters));
            Assert.That(
                VividSubstrateContract.SingleComplexMaxTotalSlices,
                Is.LessThanOrEqualTo(VividSubstrateContract.DefaultSliceCount),
                "Single-complex Substrate pixel including inline 2-uint SSS must stay within the 16-slice default budget.");

            Assert.That(
                VividSubstrateContract.ComputeSingleClosureExtensionSlices(VividSubstrateFeatureMask.Subsurface),
                Is.EqualTo(2));

            // Typical SkinSlab feature set: Header(1) + Core(6) + Coat(2) + Fuzz(1) + Subsurface(2) = 12 slices
            var skinMask = VividSubstrateFeatureMask.OpenPBROpaque
                | VividSubstrateFeatureMask.Coat
                | VividSubstrateFeatureMask.Fuzz
                | VividSubstrateFeatureMask.Subsurface;
            int skinTotalSlices = 1 + VividSubstrateContract.OpenPBRCoreUintCount
                + VividSubstrateContract.ComputeSingleClosureExtensionSlices(skinMask);
            Assert.That(skinTotalSlices, Is.EqualTo(12));
            Assert.That(skinTotalSlices, Is.LessThanOrEqualTo(VividSubstrateContract.DefaultSliceCount));

            // Worst-case single-complex feature set
            var allFeatures = VividSubstrateFeatureMask.OpenPBROpaque
                | VividSubstrateFeatureMask.Coat
                | VividSubstrateFeatureMask.Fuzz
                | VividSubstrateFeatureMask.Anisotropy
                | VividSubstrateFeatureMask.DualNormal
                | VividSubstrateFeatureMask.Subsurface;
            int maxSlices = 1 + VividSubstrateContract.OpenPBRCoreUintCount
                + VividSubstrateContract.ComputeSingleClosureExtensionSlices(allFeatures);
            Assert.That(maxSlices, Is.EqualTo(VividSubstrateContract.SingleComplexMaxTotalSlices));
        }

        [Test]
        public void SubsurfaceScatteringPass_Lifecycle_TracksShouldSplitDeferredDiffuse()
        {
            bool prevEnable = SubsurfaceScatteringPass.EnableSubsurfaceScattering;
            var pass = new SubsurfaceScatteringPass();
            try
            {
                SubsurfaceScatteringPass.EnableSubsurfaceScattering = true;
                bool beforeCreate = SubsurfaceScatteringPass.ShouldSplitDeferredDiffuse;

                pass.Create();
                Assert.That(SubsurfaceScatteringPass.ShouldSplitDeferredDiffuse, Is.True);

                // Calling Create twice must not double-count the instance
                pass.Create();
                SubsurfaceScatteringPass.EnableSubsurfaceScattering = false;
                Assert.That(SubsurfaceScatteringPass.ShouldSplitDeferredDiffuse, Is.False);

                SubsurfaceScatteringPass.EnableSubsurfaceScattering = true;
                Assert.That(SubsurfaceScatteringPass.ShouldSplitDeferredDiffuse, Is.True);

                pass.Dispose();
                Assert.That(SubsurfaceScatteringPass.ShouldSplitDeferredDiffuse, Is.EqualTo(beforeCreate));
            }
            finally
            {
                pass.Dispose();
                SubsurfaceScatteringPass.EnableSubsurfaceScattering = prevEnable;
            }
        }

        [Test]
        public void SubsurfaceScatteringPass_AndGBufferWriters_DeclareNonCyclicSubstrateSlotAccess()
        {
            var sssSlots = typeof(SubsurfaceScatteringPass)
                .GetCustomAttributes<FrameResourceSlotAttribute>(true)
                .Where(a => a.SlotName == FrameResourceSlots.SubstrateClosureBufferName)
                .ToArray();
            Assert.That(sssSlots, Has.Length.EqualTo(1));
            Assert.That(sssSlots[0].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(sssSlots[0].Optional, Is.True);

            var gbufferSlots = typeof(GBufferPass)
                .GetCustomAttributes<FrameResourceSlotAttribute>(true)
                .Where(a => a.SlotName == FrameResourceSlots.SubstrateClosureBufferName)
                .ToArray();
            var resolveSlots = typeof(VisibilityBufferGBufferResolvePass)
                .GetCustomAttributes<FrameResourceSlotAttribute>(true)
                .Where(a => a.SlotName == FrameResourceSlots.SubstrateClosureBufferName)
                .ToArray();

            Assert.That(gbufferSlots, Has.Length.EqualTo(1));
            Assert.That(resolveSlots, Has.Length.EqualTo(1));
            Assert.That(
                gbufferSlots[0].Access,
                Is.EqualTo(AccessFlags.ReadWrite),
                "GBufferPass and VisibilityBufferGBufferResolvePass must both use ReadWrite on SubstrateClosureBuffer so hybrid GBuffer->Resolve graphs do not form a slot dependency cycle.");
            Assert.That(
                resolveSlots[0].Access,
                Is.EqualTo(AccessFlags.ReadWrite),
                "GBufferPass and VisibilityBufferGBufferResolvePass must both use ReadWrite on SubstrateClosureBuffer so hybrid GBuffer->Resolve graphs do not form a slot dependency cycle.");
        }

        [Test]
        public void SubsurfaceShaders_ContainExpectedBurleyAndSlabContracts()
        {
            string packageRoot = Path.GetFullPath("Packages/com.vivid.render-pipelines");
            string computePath = Path.Combine(packageRoot, "Shaders/Core/Private/SubsurfaceScattering.compute");
            string skinSlabPath = Path.Combine(packageRoot, "Shaders/Material/Skin/SkinSlab.slab.shader");

            Assert.That(File.Exists(computePath), Is.True, computePath);
            Assert.That(File.Exists(skinSlabPath), Is.True, skinSlabPath);

            string computeSrc = File.ReadAllText(computePath);
            Assert.That(computeSrc, Does.Contain("KClearSubsurfaceTileArgs"));
            Assert.That(computeSrc, Does.Contain("KClassifySubsurfaceTiles"));
            Assert.That(computeSrc, Does.Contain("KSubsurfaceBurleyScatter"));
            Assert.That(computeSrc, Does.Contain("Texture2D<float2> _SubsurfaceBlueNoise"));
            Assert.That(computeSrc, Does.Contain("BurleySampleRadius"));

            string skinSlabSrc = File.ReadAllText(skinSlabPath);
            Assert.That(skinSlabSrc, Does.Contain("VIVID_SLAB_USE_BASE_SUBSHADER"));
            Assert.That(skinSlabSrc, Does.Contain("v2.subsurfaceWeight"));
            Assert.That(skinSlabSrc, Does.Contain("v2.subsurfaceMfpMeters"));
        }
    }
}
