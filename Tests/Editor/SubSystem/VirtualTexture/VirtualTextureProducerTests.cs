using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Editor;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureProducerTests
    {
        [Test]
        public void CheckerSourceProducer_CopiesNeighborTexelsIntoInternalGutter()
        {
            VirtualTextureSpaceDesc desc = CreateDesc();
            var leftPage = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
            var rightPage = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
            var leftRequest = new VTRequest(
                1,
                new VirtualTexturePageCoord(0, 0, 0),
                0,
                1,
                1,
                0);
            var rightRequest = new VTRequest(
                1,
                new VirtualTexturePageCoord(1, 0, 0),
                1,
                1,
                1,
                0);

            VTCheckerSourcePageProducer.Instance.WritePage(desc, leftRequest, leftPage);
            VTCheckerSourcePageProducer.Instance.WritePage(desc, rightRequest, rightPage);

            int y = desc.BorderSize + 3;
            Color32 leftRightGutter = leftPage[GetPixelIndex(desc, desc.BorderSize + desc.PageSize, y)];
            Color32 rightInteriorStart = rightPage[GetPixelIndex(desc, desc.BorderSize, y)];
            Color32 rightLeftGutter = rightPage[GetPixelIndex(desc, desc.BorderSize - 1, y)];
            Color32 leftInteriorEnd = leftPage[GetPixelIndex(desc, desc.BorderSize + desc.PageSize - 1, y)];

            Assert.That(leftRightGutter, Is.EqualTo(rightInteriorStart));
            Assert.That(rightLeftGutter, Is.EqualTo(leftInteriorEnd));
        }

        [Test]
        public void CheckerSourceProducer_ClampsOuterGutterToVirtualTextureEdge()
        {
            VirtualTextureSpaceDesc desc = CreateDesc();
            var pixels = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
            var request = new VTRequest(
                1,
                new VirtualTexturePageCoord(0, 0, 0),
                0,
                1,
                1,
                0);

            VTCheckerSourcePageProducer.Instance.WritePage(desc, request, pixels);

            int y = desc.BorderSize + 2;
            Color32 outerLeftGutter = pixels[GetPixelIndex(desc, 0, y)];
            Color32 firstInterior = pixels[GetPixelIndex(desc, desc.BorderSize, y)];
            Color32 outerTopGutter = pixels[GetPixelIndex(desc, desc.BorderSize + 2, 0)];
            Color32 firstInteriorRow = pixels[GetPixelIndex(desc, desc.BorderSize + 2, desc.BorderSize)];

            Assert.That(outerLeftGutter, Is.EqualTo(firstInterior));
            Assert.That(outerTopGutter, Is.EqualTo(firstInteriorRow));
        }

        [Test]
        public void CheckerSourceProducer_EvaluatesPredictableSourceTexelsAcrossMips()
        {
            VirtualTextureSpaceDesc desc = CreateDesc();

            Color32 mip0 = VTCheckerSourcePageProducer.EvaluateSourceTexel(desc, 0, 9, 11);
            Color32 mip1 = VTCheckerSourcePageProducer.EvaluateSourceTexel(desc, 1, 9, 11);
            Color32 mip0Clamped = VTCheckerSourcePageProducer.EvaluateSourceTexel(desc, 0, -10, -20);
            Color32 mip0Origin = VTCheckerSourcePageProducer.EvaluateSourceTexel(desc, 0, 0, 0);

            Assert.That(mip0, Is.Not.EqualTo(mip1));
            Assert.That(mip0Clamped, Is.EqualTo(mip0Origin));
        }

        [Test]
        public void Texture2DPageProducer_CopiesSourceTexelsIntoInternalGutter()
        {
            Texture2D sourceTexture = CreateSourceTexture(4, 4);
            VirtualTextureSpaceDesc desc = CreateDesc();
            var leftPage = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
            var rightPage = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
            var producer = new VTTexture2DPageProducer(sourceTexture);
            var leftRequest = new VTRequest(
                1,
                new VirtualTexturePageCoord(0, 0, 0),
                0,
                1,
                1,
                0);
            var rightRequest = new VTRequest(
                1,
                new VirtualTexturePageCoord(1, 0, 0),
                1,
                1,
                1,
                0);

            try
            {
                producer.WritePage(desc, leftRequest, leftPage);
                producer.WritePage(desc, rightRequest, rightPage);

                int y = desc.BorderSize + 1;
                Color32 leftRightGutter = leftPage[GetPixelIndex(desc, desc.BorderSize + desc.PageSize, y)];
                Color32 rightInteriorStart = rightPage[GetPixelIndex(desc, desc.BorderSize, y)];
                Color32 outerLeftGutter = leftPage[GetPixelIndex(desc, 0, y)];
                Color32 firstInterior = leftPage[GetPixelIndex(desc, desc.BorderSize, y)];

                Assert.That(leftRightGutter, Is.EqualTo(rightInteriorStart));
                Assert.That(outerLeftGutter, Is.EqualTo(firstInterior));
            }
            finally
            {
                Object.DestroyImmediate(sourceTexture);
            }
        }

        [Test]
        public void VirtualTextureAssetProducer_ReplaysBakedTileWithoutSourceTexture()
        {
            Texture2D sourceTexture = CreateSourceTexture(4, 4);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            var coord = new VirtualTexturePageCoord(1, 0, 0);

            try
            {
                asset.name = "BakedProducer";
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = sourceTexture,
                    SourceTextureGUID = "source-guid",
                    SourceTexturePath = "Assets/Source.png",
                    PageSize = 2,
                    BorderSize = 1,
                    MipCount = 2,
                    FallbackColor = new Color32(9, 8, 7, 255),
                });

                VirtualTextureSpaceDesc desc = builtData.CreateSpaceDesc(
                    "BakedProducer",
                    cachePageCount: 2,
                    maxUploadsPerFrame: 1,
                    feedbackCapacity: 16);
                var expectedProducer = new VTTexture2DPageProducer(sourceTexture);
                var request = new VTRequest(1, coord, 0, 1, 1, 0);
                var expectedPixels = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
                expectedProducer.WritePage(desc, request, expectedPixels);

                Object.DestroyImmediate(sourceTexture);
                sourceTexture = null;

                IVTPageProducer producer = VTRuntimeProducerUtility.Resolve(asset, desc);
                Assert.That(producer, Is.Not.Null);
                Assert.That(producer.RequestPageData(desc, request), Is.EqualTo(VTPageRequestStatus.Available));

                IVTPageFinalizer finalizer = (IVTPageFinalizer)producer.ProducePageData(desc, request);
                Assert.That(finalizer, Is.Not.Null);

                var stagingTexture = new Texture2DArray(
                    desc.PhysicalPageSize,
                    desc.PhysicalPageSize,
                    1,
                    desc.GraphicsFormat,
                    TextureCreationFlags.None);
                var scratchPixels = new Color32[expectedPixels.Length];

                try
                {
                    finalizer.FinalizeUpload(stagingTexture, 0, scratchPixels);
                    stagingTexture.Apply(false, false);

                    Assert.That(stagingTexture.GetPixels32(0, 0), Is.EqualTo(expectedPixels));
                    Assert.That(asset.SourceTextureGUID, Is.EqualTo("source-guid"));
                    Assert.That(builtData.FallbackColor, Is.EqualTo(new Color32(9, 8, 7, 255)));
                    Assert.That(builtData.Chunks[0].ContainsByteRange(0, builtData.Tiles[0].ByteSize), Is.True);
                }
                finally
                {
                    finalizer.Dispose();
                    Object.DestroyImmediate(stagingTexture);
                }
            }
            finally
            {
                if (sourceTexture != null)
                    Object.DestroyImmediate(sourceTexture);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
            }
        }

        [Test]
        public void DemoController_AutoSizesVirtualSpaceFromSourceTexture()
        {
            var gameObject = new GameObject("VTAutoSizeController");
            gameObject.SetActive(false);
            var sourceTexture = new Texture2D(1024, 512, TextureFormat.RGBA32, false);

            try
            {
                var controller = gameObject.AddComponent<VirtualTextureDemoController>();
                SetPrivateField(controller, "m_SourceTexture", sourceTexture);
                SetPrivateField(controller, "m_PageSize", 256);
                SetPrivateField(controller, "m_AutoSizeFromSourceTexture", true);

                VirtualTextureSpaceDesc desc = InvokeCreateDescriptor(controller);

                Assert.That(desc.VirtualPageCountX, Is.EqualTo(4));
                Assert.That(desc.VirtualPageCountY, Is.EqualTo(2));
                Assert.That(desc.MipCount, Is.EqualTo(3));
            }
            finally
            {
                Object.DestroyImmediate(sourceTexture);
                Object.DestroyImmediate(gameObject);
                VirtualTextureSystem.Deinitialize();
            }
        }

        private static VirtualTextureSpaceDesc CreateDesc()
        {
            return new VirtualTextureSpaceDesc(
                "CheckerSource",
                pageSize: 8,
                borderSize: 2,
                virtualPageCountX: 2,
                virtualPageCountY: 2,
                mipCount: 2,
                cachePageCount: 4,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: 1,
                feedbackCapacity: 16);
        }

        private static int GetPixelIndex(in VirtualTextureSpaceDesc desc, int x, int y)
        {
            return y * desc.PhysicalPageSize + x;
        }

        private static Texture2D CreateSourceTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = new Color32((byte)(x * 40), (byte)(y * 40), 0, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private static void SetPrivateField<T>(VirtualTextureDemoController controller, string fieldName, T value)
        {
            FieldInfo field = typeof(VirtualTextureDemoController).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(controller, value);
        }

        private static VirtualTextureSpaceDesc InvokeCreateDescriptor(VirtualTextureDemoController controller)
        {
            MethodInfo method = typeof(VirtualTextureDemoController).GetMethod(
                "CreateDescriptor",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (VirtualTextureSpaceDesc)method.Invoke(controller, null);
        }
    }
}
