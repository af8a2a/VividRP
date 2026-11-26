using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VividSubstrateBufferContractTests
    {
        private const string SubstrateBufferHlslPath =
            "Shaders/Core/Public/VividSubstrateBuffer.hlsl";
        private const string ComputeResolvePath =
            "Shaders/Core/Private/GPUDriven/VisibilityBufferGBufferResolve.compute";
        private const string ClassificationComputePath =
            "Shaders/Material/MaterialClassification.compute";
        private const string DeferredLitComputePath =
            "Shaders/Material/DeferredLit.compute";
        private const string DeferredLightingHlslPath =
            "Shaders/Core/Public/VividDeferredLighting.hlsl";

        [Test]
        public void PixelHeader_PackUnpack_RoundTripsAllFieldsBitExactly()
        {
            var expected = new VividSubstratePixelHeader
            {
                Mode = VividSubstratePixelMode.MultiClosure,
                ClosureCount = 4u,
                TopologyOp = VividSubstrateTopologyOp.VerticalLayer,
                FeatureMask = VividSubstrateFeatureMask.OpenPBROpaque
                    | VividSubstrateFeatureMask.Coat
                    | VividSubstrateFeatureMask.Anisotropy
                    | VividSubstrateFeatureMask.DualNormal,
                PayloadUintCount = 15u,
                Flags = VividSubstrateHeaderFlags.ReceiveDecals | VividSubstrateHeaderFlags.HasEmission,
                AbiVersion = VividSubstrateContract.AbiVersion,
            };

            uint packed = VividSubstrateContract.PackPixelHeader(expected);
            Assert.That(VividSubstrateContract.IsHeaderValid(packed), Is.True);

            VividSubstratePixelHeader unpacked = VividSubstrateContract.UnpackPixelHeader(packed);
            Assert.That(unpacked.Mode, Is.EqualTo(expected.Mode));
            Assert.That(unpacked.ClosureCount, Is.EqualTo(expected.ClosureCount));
            Assert.That(unpacked.TopologyOp, Is.EqualTo(expected.TopologyOp));
            Assert.That(unpacked.FeatureMask, Is.EqualTo(expected.FeatureMask));
            Assert.That(unpacked.PayloadUintCount, Is.EqualTo(expected.PayloadUintCount));
            Assert.That(unpacked.Flags, Is.EqualTo(expected.Flags));
            Assert.That(unpacked.AbiVersion, Is.EqualTo(expected.AbiVersion));
        }

        [Test]
        public void PixelHeader_Validation_RejectsEmptyErrorWrongAbiAndBudgetOverflow()
        {
            Assert.That(VividSubstrateContract.IsHeaderValid(0u), Is.False);

            var baseValid = new VividSubstratePixelHeader
            {
                Mode = VividSubstratePixelMode.SingleSimple,
                ClosureCount = 1u,
                TopologyOp = VividSubstrateTopologyOp.None,
                FeatureMask = VividSubstrateFeatureMask.OpenPBROpaque,
                PayloadUintCount = VividSubstrateContract.OpenPBRCoreUintCount,
                Flags = VividSubstrateHeaderFlags.None,
                AbiVersion = VividSubstrateContract.AbiVersion,
            };
            Assert.That(VividSubstrateContract.IsHeaderValid(VividSubstrateContract.PackPixelHeader(baseValid)), Is.True);

            var wrongAbi = baseValid;
            wrongAbi.AbiVersion = 2u;
            Assert.That(VividSubstrateContract.IsHeaderValid(VividSubstrateContract.PackPixelHeader(wrongAbi)), Is.False);

            var errorMode = baseValid;
            errorMode.Mode = VividSubstratePixelMode.Error;
            Assert.That(VividSubstrateContract.IsHeaderValid(VividSubstrateContract.PackPixelHeader(errorMode)), Is.False);

            var tooManyClosures = baseValid;
            tooManyClosures.ClosureCount = VividSubstrateContract.MaxClosuresPerPixel + 1u;
            Assert.That(VividSubstrateContract.IsHeaderValid(VividSubstrateContract.PackPixelHeader(tooManyClosures)), Is.False);

            var overflowBudget = baseValid;
            overflowBudget.PayloadUintCount = VividSubstrateContract.MaxSliceCount;
            Assert.That(VividSubstrateContract.IsHeaderValid(VividSubstrateContract.PackPixelHeader(overflowBudget)), Is.False);
        }

        [Test]
        public void SpecularIor_12BitQuantization_MeetsSubMilliErrorAcrossFullRange()
        {
            float[] testIors = { 1.0f, 1.33f, 1.45f, 1.5f, 1.77f, 2.0f, 2.417f, 2.5f, 3.0f };
            foreach (float ior in testIors)
            {
                uint packed = VividSubstrateContract.PackSpecularIor(ior);
                float unpacked = VividSubstrateContract.UnpackSpecularIor(packed);
                Assert.That(Mathf.Abs(unpacked - ior), Is.LessThan(0.0005f), $"IOR {ior}");
            }
        }

        [Test]
        public void SubstrateBufferHlsl_MatchesCSharpContractAndSinglePassPipeline()
        {
            string substrateSource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(SubstrateBufferHlslPath));
            string resolveSource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(ComputeResolvePath));
            string classifySource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(ClassificationComputePath));
            string deferredLitSource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(DeferredLitComputePath));
            string deferredHlslSource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(DeferredLightingHlslPath));

            StringAssert.Contains("#define VIVID_SUBSTRATE_ABI_VERSION 1u", substrateSource);
            StringAssert.Contains("#define VIVID_SUBSTRATE_DEFAULT_SLICE_COUNT 16u", substrateSource);
            StringAssert.Contains("#define VIVID_SUBSTRATE_MAX_SLICE_COUNT 24u", substrateSource);
            StringAssert.Contains("#define VIVID_SUBSTRATE_OPENPBR_CORE_UINT_COUNT 5u", substrateSource);
            StringAssert.Contains("VividWriteSubstrateOpenPBROpaqueSingle(", substrateSource);
            StringAssert.Contains("VividTryLoadSubstrateOpenPBROpaqueSingle(", substrateSource);
            StringAssert.Contains("VividGetSubstrateTileClassBit(", substrateSource);

            StringAssert.Contains("#pragma kernel ResolveVisibilityBuffer", resolveSource);
            StringAssert.Contains("RWTexture2DArray<uint> _OutSubstrateClosureBuffer;", resolveSource);
            StringAssert.Contains("VIVID_VT_ENABLE_FEEDBACK_RW_COMPUTE 1", resolveSource);
            StringAssert.Contains("VividWriteSubstrateOpenPBROpaqueSingle(", resolveSource);
            StringAssert.Contains("VividWriteSubstrateEmpty(", resolveSource);

            StringAssert.Contains("Texture2DArray<uint> _SubstrateClosureBuffer;", classifySource);
            StringAssert.Contains("VividGetSubstrateTileClassBit(", classifySource);

            StringAssert.Contains("#pragma kernel DeferredLit_Variant0 VIVID_DEFERRED_TIER_0", deferredLitSource);
            StringAssert.Contains("#pragma kernel DeferredLit_Variant1 VIVID_DEFERRED_TIER_1", deferredLitSource);
            StringAssert.Contains("#pragma kernel DeferredLit_Variant2 VIVID_DEFERRED_TIER_2", deferredLitSource);
            StringAssert.Contains("#pragma kernel DeferredLit_Variant3 VIVID_DEFERRED_TIER_3", deferredLitSource);
            StringAssert.Contains("Texture2DArray<uint> _SubstrateClosureBuffer;", deferredHlslSource);
            StringAssert.Contains("VividTryLoadSubstrateOpenPBROpaqueSingle(", deferredHlslSource);
        }

        [Test]
        public void Prepare_ResolveClassificationDeferred_ZeroManagedAllocation_WithSubstrateFrameData()
        {
            var resolvePass = new VisibilityBufferGBufferResolvePass();
            var classifyPass = new MaterialClassificationPass();
            var deferredPass = new DeferredLightingPass();
            try
            {
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.actualWidth = 1280;
                cameraData.actualHeight = 720;
                var gpuDrivenData = frameData.GetOrCreate<VividGPUDrivenFrameData>();
                gpuDrivenData.requiresSubstrateBuffer = true;
                frameData.GetOrCreate<VividSubstrateMaterialFrameData>();

                // Warmup
                resolvePass.Prepare(frameData);
                classifyPass.Prepare(frameData);
                deferredPass.Prepare(frameData);

                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 32; ++i)
                {
                    resolvePass.Prepare(frameData);
                    classifyPass.Prepare(frameData);
                    deferredPass.Prepare(frameData);
                }
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Assert.That(allocatedBytes, Is.Zero, "Hot-path Prepare with VividSubstrateMaterialFrameData must allocate zero managed memory.");
            }
            finally
            {
                resolvePass.Dispose();
                classifyPass.Dispose();
                deferredPass.Dispose();
            }
        }
    }
}
