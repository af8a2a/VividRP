using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
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
        private const string OpenPBRV2HlslPath =
            "Shaders/Core/Public/VividOpenPBRV2.hlsl";
        private const string StandardLitToOpenPBRHlslPath =
            "Shaders/Core/Public/VividStandardLitToOpenPBR.hlsl";
        private const string StandardLitAdapterHlslPath =
            "Shaders/Material/ShaderPass/StandardLitOpenPBRAdapter.hlsl";

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
        public void SingleComplexV2_ExtensionBudgets_FitWithinDefault64ByteBudget()
        {
            Assert.That(
                VividSubstrateContract.ComputeSingleClosureTotalSlices(VividSubstrateFeatureMask.OpenPBROpaque),
                Is.EqualTo(6));
            Assert.That(
                VividSubstrateContract.ComputeSingleClosureTotalSlices(
                    VividSubstrateFeatureMask.OpenPBROpaque | VividSubstrateFeatureMask.Coat),
                Is.EqualTo(8));
            Assert.That(
                VividSubstrateContract.ComputeSingleClosureTotalSlices(
                    VividSubstrateFeatureMask.OpenPBROpaque
                    | VividSubstrateFeatureMask.Coat
                    | VividSubstrateFeatureMask.DualNormal),
                Is.EqualTo(9));
            Assert.That(
                VividSubstrateContract.ComputeSingleClosureTotalSlices(
                    VividSubstrateFeatureMask.OpenPBROpaque
                    | VividSubstrateFeatureMask.Coat
                    | VividSubstrateFeatureMask.DualNormal
                    | VividSubstrateFeatureMask.Fuzz
                    | VividSubstrateFeatureMask.Anisotropy),
                Is.EqualTo(VividSubstrateContract.SingleComplexMaxTotalSlices));
            Assert.That(
                VividSubstrateContract.SingleComplexMaxTotalSlices,
                Is.LessThanOrEqualTo(VividSubstrateContract.DefaultSliceCount),
                "All Phase 2.1 single-closure features combined must fit inside the default 64 B/px budget.");

            foreach (float ior in new[] { 1.0f, 1.33f, 1.5f, 1.6f, 2.0f, 3.0f })
            {
                uint q = VividSubstrateContract.PackCoatIor8(ior);
                float u = VividSubstrateContract.UnpackCoatIor8(q);
                Assert.That(Mathf.Abs(u - ior), Is.LessThan(0.005f), $"Coat IOR {ior}");
            }
        }

        [Test]
        public void MultiClosure_OperatorWord_PackUnpack_RoundTripsBitLayout()
        {
            var expected = new VividSubstrateOperatorDesc
            {
                OpKind = VividSubstrateTopologyOp.VerticalLayer,
                Weight = 0.75f,
                OpticalThickness = 1.85f,
                TransmittanceColorSrgb = new float3(0.9f, 0.45f, 0.2f),
            };

            uint packed = VividSubstrateContract.PackOperatorWord(in expected);
            VividSubstrateOperatorDesc unpacked = VividSubstrateContract.UnpackOperatorWord(packed);

            Assert.That(unpacked.OpKind, Is.EqualTo(VividSubstrateTopologyOp.VerticalLayer));
            Assert.That(Mathf.Abs(unpacked.Weight - expected.Weight), Is.LessThanOrEqualTo(1.0f / 255.0f + 1e-4f));
            Assert.That(
                Mathf.Abs(unpacked.OpticalThickness - expected.OpticalThickness),
                Is.LessThanOrEqualTo(VividSubstrateContract.MaxOpticalThickness / 127.0f + 1e-4f));
            Assert.That(Mathf.Abs(unpacked.TransmittanceColorSrgb.x - expected.TransmittanceColorSrgb.x), Is.LessThanOrEqualTo(1.0f / 31.0f + 1e-4f));
            Assert.That(Mathf.Abs(unpacked.TransmittanceColorSrgb.y - expected.TransmittanceColorSrgb.y), Is.LessThanOrEqualTo(1.0f / 31.0f + 1e-4f));
            Assert.That(Mathf.Abs(unpacked.TransmittanceColorSrgb.z - expected.TransmittanceColorSrgb.z), Is.LessThanOrEqualTo(1.0f / 31.0f + 1e-4f));

            var hMix = new VividSubstrateOperatorDesc
            {
                OpKind = VividSubstrateTopologyOp.HorizontalMix,
                Weight = 0.5f,
                OpticalThickness = 0.0f,
                TransmittanceColorSrgb = new float3(1.0f, 1.0f, 1.0f),
            };
            VividSubstrateOperatorDesc unpackedHMix = VividSubstrateContract.UnpackOperatorWord(
                VividSubstrateContract.PackOperatorWord(in hMix));
            Assert.That(unpackedHMix.OpKind, Is.EqualTo(VividSubstrateTopologyOp.HorizontalMix));
            Assert.That(unpackedHMix.OpticalThickness, Is.EqualTo(0.0f));
            Assert.That(unpackedHMix.TransmittanceColorSrgb.x, Is.EqualTo(1.0f));
        }

        [Test]
        public void MultiClosure_SliceBudgets_AndTopologyBudget_FitDefault64BAndMax96B()
        {
            // 2 core closures + 1 op = 1 + 1 + 10 = 12 uints = 48 B/px <= 64 B/px default
            int twoCoreSlices = VividSubstrateContract.ComputeMultiClosureTotalSlices(2);
            Assert.That(twoCoreSlices, Is.EqualTo(12));
            Assert.That(VividSubstrateContract.FitsSliceBudget(twoCoreSlices, VividSubstrateContract.DefaultSliceCount), Is.True);

            // 3 core closures + 2 ops = 1 + 2 + 15 = 18 uints = 72 B/px <= 96 B/px max
            int threeCoreSlices = VividSubstrateContract.ComputeMultiClosureTotalSlices(3);
            Assert.That(threeCoreSlices, Is.EqualTo(18));
            Assert.That(VividSubstrateContract.FitsSliceBudget(threeCoreSlices, VividSubstrateContract.MaxSliceCount), Is.True);

            // 4 core closures + 3 ops = 1 + 3 + 20 = 24 uints = 96 B/px == 96 B/px max
            int fourCoreSlices = VividSubstrateContract.ComputeMultiClosureTotalSlices(4);
            Assert.That(fourCoreSlices, Is.EqualTo(24));
            Assert.That(VividSubstrateContract.FitsSliceBudget(fourCoreSlices, VividSubstrateContract.MaxSliceCount), Is.True);

            // 2 closures with Coat + Anisotropy = 12 + 2 + 2 = 16 uints = 64 B/px == default budget
            ReadOnlySpan<VividSubstrateFeatureMask> twoExtended = stackalloc VividSubstrateFeatureMask[]
            {
                VividSubstrateFeatureMask.OpenPBROpaque | VividSubstrateFeatureMask.Anisotropy,
                VividSubstrateFeatureMask.OpenPBROpaque | VividSubstrateFeatureMask.Coat,
            };
            Assert.That(VividSubstrateContract.ComputeMultiClosureTotalSlices(twoExtended), Is.EqualTo(16));
            Assert.That(VividSubstrateContract.FitsSliceBudget(16, VividSubstrateContract.DefaultSliceCount), Is.True);

            // ClosureTopologyBudget.SubstrateMultiClosure = (4 closures, 3 operators)
            ClosureTopologyBudget budget = ClosureTopologyBudget.SubstrateMultiClosure;
            Assert.That(budget.MaxClosureCount, Is.EqualTo(4));
            Assert.That(budget.MaxOperatorCount, Is.EqualTo(3));
            Assert.That(budget.Allows(4, 3), Is.True);
            Assert.That(budget.Allows(5, 3), Is.False);
            Assert.That(budget.Allows(4, 4), Is.False);
        }

        [Test]
        public void SubstrateBufferHlsl_MatchesCSharpContractAndSinglePassPipeline()
        {
            string substrateSource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(SubstrateBufferHlslPath));
            string resolveSource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(ComputeResolvePath));
            string classifySource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(ClassificationComputePath));
            string deferredLitSource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(DeferredLitComputePath));
            string deferredHlslSource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(DeferredLightingHlslPath));
            string v2HlslSource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(OpenPBRV2HlslPath));
            string stdLitMapSource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(StandardLitToOpenPBRHlslPath));
            string stdLitAdapterSource = File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(StandardLitAdapterHlslPath));

            StringAssert.Contains("#define VIVID_SUBSTRATE_ABI_VERSION 1u", substrateSource);
            StringAssert.Contains("#define VIVID_SUBSTRATE_DEFAULT_SLICE_COUNT 16u", substrateSource);
            StringAssert.Contains("#define VIVID_SUBSTRATE_MAX_SLICE_COUNT 24u", substrateSource);
            StringAssert.Contains("#define VIVID_SUBSTRATE_OPENPBR_CORE_UINT_COUNT 5u", substrateSource);
            StringAssert.Contains("#define VIVID_SUBSTRATE_SINGLE_COMPLEX_MAX_SLICES 13u", substrateSource);
            StringAssert.Contains("#define VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL 3u", substrateSource);
            StringAssert.Contains("VividWriteSubstrateOpenPBROpaqueSingle(", substrateSource);
            StringAssert.Contains("VividWriteSubstrateOpenPBRV2Single(", substrateSource);
            StringAssert.Contains("VividWriteSubstrateMultiClosure(", substrateSource);
            StringAssert.Contains("VividTryLoadSubstrateOpenPBROpaqueSingle(", substrateSource);
            StringAssert.Contains("VividTryLoadSubstrateOpenPBRV2Single(", substrateSource);
            StringAssert.Contains("VividTryLoadSubstrateMultiClosure(", substrateSource);
            StringAssert.Contains("VividPackSubstrateOperatorWord(", substrateSource);
            StringAssert.Contains("VividUnpackSubstrateOperatorWord(", substrateSource);
            StringAssert.Contains("VividGetSubstrateTileClassBit(", substrateSource);

            StringAssert.Contains("VividTryResolveOpenPBRV2Inputs(", v2HlslSource);
            StringAssert.Contains("VividEvaluateOpenPBRV2AlbedoComponents(", v2HlslSource);
            StringAssert.Contains("VividBuildOpenPBRV2TopLayerSummary(", v2HlslSource);
            StringAssert.Contains("VividComputeAnisotropicBentNormal(", v2HlslSource);
            StringAssert.Contains("VividComputeSlabBeerLambertTransmittance(", v2HlslSource);
            StringAssert.Contains("VividComputeMultiClosureIndirectWeights(", v2HlslSource);
            StringAssert.Contains("VividComputeMultiClosureDirectWeights(", v2HlslSource);

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
            StringAssert.Contains("TryEvaluateDeferredOpenPBRV2Lighting(", deferredHlslSource);
            StringAssert.Contains("TryEvaluateDeferredSubstrateMultiClosureLighting(", deferredHlslSource);
            StringAssert.Contains("VIVID_DEFERRED_ENABLE_SUBSTRATE_MULTI_CLOSURE", deferredHlslSource);

            // Phase 2.3 unified StandardLit -> OpenPBR mapping single source of truth
            StringAssert.Contains("#define VIVID_STANDARD_LIT_TO_OPENPBR_VERSION 1u", stdLitMapSource);
            StringAssert.Contains("VividStandardLitApplyCoreToOpenPBRResolved(", stdLitMapSource);
            StringAssert.Contains("VividStandardLitToOpenPBRV2Inputs(", stdLitMapSource);
            StringAssert.Contains("VividStandardLitToOpenPBR.hlsl", stdLitAdapterSource);
            StringAssert.Contains("VividStandardLitApplyCoreToOpenPBRResolved(", stdLitAdapterSource);
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
