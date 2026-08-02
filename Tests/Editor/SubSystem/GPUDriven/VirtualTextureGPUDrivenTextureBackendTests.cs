using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureGPUDrivenTextureBackendTests
    {
        [SetUp]
        public void SetUp()
        {
            VirtualTextureSystem.Deinitialize();
        }

        [TearDown]
        public void TearDown()
        {
            VirtualTextureSystem.Deinitialize();
        }

        [Test]
        public void Constructor_CreatesPrivateThreeLayerSharedSpace()
        {
            using var backend = new VirtualTextureGPUDrivenTextureBackend();

            Assert.That(backend.IsAvailable, Is.True, backend.UnavailableReason);
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.LayerCount, Is.EqualTo(3));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(0).Semantic, Is.EqualTo(VTLayerSemantic.BaseColor));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(1).Semantic, Is.EqualTo(VTLayerSemantic.Normal));
            Assert.That(backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(2).Semantic, Is.EqualTo(VTLayerSemantic.Mask));
            Assert.That(
                backend.VirtualTextureSpaceDesc.StackDesc.GetLayer(1).FallbackColor,
                Is.EqualTo(new Color32(128, 128, 255, 128)));
            Assert.That(
                VirtualTextureSystem.TryGetAllocationForTesting(
                    backend.VirtualTextureSpaceId,
                    out VTAllocatedVirtualTexture allocation),
                Is.True);
            Assert.That(allocation.AllocationId, Is.EqualTo(backend.VirtualTextureAllocationId));
            Assert.That(allocation.Description.PrivateSpace, Is.True);
        }

        [Test]
        public void CreateSurfaceBinding_AllocatesOneAlignedRegionAndPacksLayerAndMaxMip()
        {
            Texture2D baseColor = null;
            Texture2D normal = null;
            Texture2D mask = null;
            try
            {
                baseColor = new Texture2D(256, 256);
                normal = new Texture2D(128, 128);
                mask = new Texture2D(64, 64);
                using var backend = new VirtualTextureGPUDrivenTextureBackend();

                uint revisionBefore = backend.BindingRevision;
                var binding = backend.CreateSurfaceBinding(new GPUDrivenSurfaceTextureSet(baseColor, normal, mask));

                Assert.That(binding.Flags, Is.EqualTo(
                    VividSurfaceBindingFlags.BaseColor
                    | VividSurfaceBindingFlags.Normal
                    | VividSurfaceBindingFlags.Mask));
                Assert.That(binding.BaseColorResource & 0xFFu, Is.EqualTo(0u));
                Assert.That(binding.NormalResource & 0xFFu, Is.EqualTo(1u));
                Assert.That(binding.MaskResource & 0xFFu, Is.EqualTo(2u));
                Assert.That(binding.BaseColorResource >> 8, Is.EqualTo(1u));
                Assert.That(binding.NormalResource >> 8, Is.EqualTo(1u));
                Assert.That(binding.MaskResource >> 8, Is.EqualTo(1u));
                Assert.That(binding.UVScaleBias, Is.EqualTo(new float4(2.0f / 128.0f, 2.0f / 128.0f, 0.0f, 0.0f)));
                Assert.That(backend.AtlasEntryCount, Is.EqualTo(1));
                Assert.That(backend.AllocatedPageCount, Is.EqualTo(4));
                Assert.That(backend.BindingRevision, Is.GreaterThan(revisionBefore));
            }
            finally
            {
                Destroy(baseColor);
                Destroy(normal);
                Destroy(mask);
            }
        }

        [Test]
        public void CreateSurfaceBinding_DeduplicatesTextureSetAndKeepsRevisionStable()
        {
            Texture2D baseColor = null;
            try
            {
                baseColor = new Texture2D(128, 128);
                using var backend = new VirtualTextureGPUDrivenTextureBackend();
                var textures = new GPUDrivenSurfaceTextureSet(baseColor, null, null);

                VividSurfaceBindingData first = backend.CreateSurfaceBinding(textures);
                uint revisionAfterFirst = backend.BindingRevision;
                VividSurfaceBindingData second = backend.CreateSurfaceBinding(textures);

                Assert.That(second.BaseColorResource, Is.EqualTo(first.BaseColorResource));
                Assert.That(second.UVScaleBias, Is.EqualTo(first.UVScaleBias));
                Assert.That(backend.AtlasEntryCount, Is.EqualTo(1));
                Assert.That(backend.BindingRevision, Is.EqualTo(revisionAfterFirst));
            }
            finally
            {
                Destroy(baseColor);
            }
        }

        [Test]
        public void CreateSurfaceBinding_NoTexturesUsesBackendNeutralFallback()
        {
            using var backend = new VirtualTextureGPUDrivenTextureBackend();

            VividSurfaceBindingData binding = backend.CreateSurfaceBinding(
                new GPUDrivenSurfaceTextureSet(null, null, null));

            Assert.That(binding.Flags, Is.EqualTo(VividSurfaceBindingFlags.None));
            Assert.That(binding.BaseColorResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
            Assert.That(binding.NormalResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
            Assert.That(binding.MaskResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
            Assert.That(binding.UVScaleBias, Is.EqualTo(new float4(1.0f, 1.0f, 0.0f, 0.0f)));
            Assert.That(backend.AtlasEntryCount, Is.Zero);
        }

        [Test]
        public void RepeatSourceSampling_LazilyBuildsReadableMipData()
        {
            Texture2D source = null;
            try
            {
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.SetPixels32(new[]
                {
                    new Color32(255, 0, 0, 255),
                    new Color32(0, 255, 0, 255),
                    new Color32(0, 0, 255, 255),
                    new Color32(255, 255, 255, 255),
                });
                source.Apply(false, false);

                var producer = new VTTexture2DPageProducer(source);
                Color32 sample = producer.SampleSource(0, 0.25f, 0.25f, true);

                Assert.That(sample, Is.EqualTo(new Color32(255, 0, 0, 255)));
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void SurfaceSamplingContract_UsesOneResolveContextForGradientsAndFeedback()
        {
            string source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VirtualTextureSurfaceSampling.hlsl"));

            Assert.That(source, Does.Contain("struct VividSurfaceSampleContext"));
            Assert.That(source, Does.Contain("VTComputeRequestedMipRangeGrad("));
            Assert.That(source, Does.Contain("VividGetSurfaceVirtualTextureMaxMip("));
            Assert.That(source, Does.Contain("VTWriteFeedback("));
            Assert.That(source, Does.Contain("VTWriteFallbackSample("));
            Assert.That(source, Does.Contain("VividSampleBaseColorGrad("));
            Assert.That(source, Does.Contain("VividSampleNormalGrad("));
            Assert.That(source, Does.Contain("VividSampleMaskGrad("));
            Assert.That(source, Does.Not.Contain("GetBindlessTexture2D("));
        }

        private static string GetPackageFilePath(params string[] parts)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] candidates =
            {
                Path.Combine(projectRoot, "Packages", "Custom_URP"),
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate))
                    return Path.Combine(candidate, Path.Combine(parts));
            }

            return Path.Combine(candidates[2], Path.Combine(parts));
        }

        private static void Destroy(Object value)
        {
            if (value != null)
                Object.DestroyImmediate(value);
        }
    }
}
