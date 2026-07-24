using System;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VividPerObjectLayoutTests
    {
        [TearDown]
        public void TearDown()
        {
            VividPerObjectBuffer.DisposeAll();
        }

        [Test]
        public void Packing_IsTightAndDeterministic_ForAllPropertyTypes()
        {
            VividPerObjectLayout layout = CreateFullLayout();
            VividPerObjectLayout secondLayout = CreateFullLayout();
            Assert.That(layout.GetProperty("_Count").Offset, Is.EqualTo(4));
            Assert.That(layout.GetProperty("_Dissolve").Offset, Is.EqualTo(8));
            Assert.That(layout.GetProperty("_Direction").Offset, Is.EqualTo(12));
            Assert.That(layout.GetProperty("_Tint").Offset, Is.EqualTo(28));
            Assert.That(layout.GetProperty("_Deformation").Offset, Is.EqualTo(44));
            Assert.That(layout.RecordStride, Is.EqualTo(112));
            Assert.That(layout.Signature, Is.EqualTo(secondLayout.Signature));
        }

        [Test]
        public void InitializeRecord_WritesSignatureDefaultsAndColumnMajorMatrix()
        {
            VividPerObjectLayout layout = CreateFullLayout();
            var data = new byte[layout.RecordStride + 16];
            layout.InitializeRecord(data, 16);

            Assert.That(ReadUInt(data, 16), Is.EqualTo(layout.Signature));
            Assert.That(ReadInt(data, 20), Is.EqualTo(7));
            Assert.That(ReadFloat(data, 24), Is.EqualTo(0.25f));
            Assert.That(ReadFloat(data, 28), Is.EqualTo(1.0f));
            Assert.That(ReadFloat(data, 44), Is.EqualTo(0.1f).Within(0.0001f));

            int matrixAddress = 16 + layout.GetProperty("_Deformation").Offset;
            Assert.That(ReadFloat(data, matrixAddress), Is.EqualTo(1.0f));
            Assert.That(ReadFloat(data, matrixAddress + 4), Is.EqualTo(5.0f));
            Assert.That(ReadFloat(data, matrixAddress + 16), Is.EqualTo(2.0f));
            Assert.That(ReadFloat(data, matrixAddress + 60), Is.EqualTo(16.0f));
        }

        [TestCase("9Invalid")]
        [TestCase("Invalid-Name")]
        [TestCase("名字")]
        public void Validation_Throws_ForInvalidShaderIdentifier(string identifier)
        {
            VividPerObjectLayout layout = new TestLayout(identifier, _ => { });
            Assert.Throws<InvalidOperationException>(() => layout.Validate());
        }

        [Test]
        public void Validation_Throws_ForDuplicatePropertyName()
        {
            VividPerObjectLayout layout = new TestLayout("Duplicate", builder =>
            {
                builder.AddFloat("_Value");
                builder.AddInt("_Value");
            });
            Assert.Throws<InvalidOperationException>(() => layout.Validate());
        }

        [Test]
        public void CodeLayout_SharedInstanceAndGenericBindUseDeclaredLayout()
        {
            Assert.That(CodeFloatLayout.Instance, Is.SameAs(CodeFloatLayout.Instance));
            Assert.That(CodeFloatLayout.Instance.RecordStride, Is.EqualTo(16));

            var gameObject = new GameObject("Generic Per Object Renderer");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            try
            {
                VividPerObjectBlock block = VividPerObjectBuffer.Bind<CodeFloatLayout>(renderer);
                block.SetFloat(CodeFloatLayout.Instance.Value, 2.5f);

                int address = VividPerObjectBufferSystem.GetRecordAddressForTests(block);
                Assert.That(
                    ReadFloat(VividPerObjectBufferSystem.GetDataForTests(), address + 4),
                    Is.EqualTo(2.5f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DifferentCodeLayouts_ShareAllocatorWithIndependentStrides()
        {
            VividPerObjectLayout character = CreateFullLayout();
            VividPerObjectLayout decal = CreateLayout("Decal", builder =>
            {
                builder.AddVector("_DecalSlot0");
                builder.AddVector("_DecalSlot1");
            });
            Assert.That(character.RecordStride, Is.EqualTo(112));
            Assert.That(decal.RecordStride, Is.EqualTo(48));

            var characterObject = new GameObject("Character Per Object Renderer");
            var decalObject = new GameObject("Decal Per Object Renderer");
            MeshRenderer characterRenderer = characterObject.AddComponent<MeshRenderer>();
            MeshRenderer decalRenderer = decalObject.AddComponent<MeshRenderer>();
            try
            {
                VividPerObjectBlock characterBlock = VividPerObjectBuffer.Bind(characterRenderer, character);
                VividPerObjectBlock decalBlock = VividPerObjectBuffer.Bind(decalRenderer, decal);
                characterBlock.SetFloat("_Dissolve", 0.75f);
                decalBlock.SetVector("_DecalSlot1", new Vector4(1, 2, 3, 4));

                int characterAddress = VividPerObjectBufferSystem.GetRecordAddressForTests(characterBlock);
                int decalAddress = VividPerObjectBufferSystem.GetRecordAddressForTests(decalBlock);
                byte[] data = VividPerObjectBufferSystem.GetDataForTests();
                Assert.That(characterAddress, Is.Not.EqualTo(decalAddress));
                Assert.That(ReadFloat(data, characterAddress + 8), Is.EqualTo(0.75f));
                Assert.That(
                    ReadFloat(data, decalAddress + decal.GetProperty("_DecalSlot1").Offset),
                    Is.EqualTo(1.0f));
                Assert.That(
                    VividPerObjectBuffer.GetStats().UsedBytes,
                    Is.EqualTo(VividPerObjectRecordAllocator.ReservedBytes + 112 + 48));
            }
            finally
            {
                Object.DestroyImmediate(characterObject);
                Object.DestroyImmediate(decalObject);
            }
        }

        [Test]
        public void Allocator_ReusesBestFitAndCoalescesAdjacentRanges()
        {
            var allocator = new VividPerObjectRecordAllocator(128, 128);
            int first = allocator.Allocate(16, out _);
            int second = allocator.Allocate(32, out _);
            int third = allocator.Allocate(16, out _);

            allocator.Free(second, 32);
            int reused = allocator.Allocate(16, out _);
            Assert.That(reused, Is.EqualTo(second));

            allocator.Free(reused, 16);
            allocator.Free(first, 16);
            allocator.Free(third, 16);
            Assert.That(allocator.UsedBytes, Is.EqualTo(VividPerObjectRecordAllocator.ReservedBytes));
            Assert.That(allocator.LargestFreeBlock, Is.EqualTo(112));
        }

        [Test]
        public void Allocator_GrowsWithoutMovingExistingRecords_AndEnforcesLimit()
        {
            var allocator = new VividPerObjectRecordAllocator(32, 64);
            int address = allocator.Allocate(16, out bool firstGrowth);
            allocator.Data[address] = 123;
            int secondAddress = allocator.Allocate(16, out bool grew);

            Assert.That(firstGrowth, Is.False);
            Assert.That(grew, Is.True);
            Assert.That(address, Is.EqualTo(16));
            Assert.That(secondAddress, Is.EqualTo(32));
            Assert.That(allocator.Data[address], Is.EqualTo(123));
            Assert.Throws<InvalidOperationException>(() => allocator.Allocate(32, out _));
        }

        [Test]
        public void BindSetAndUnbind_PreserveRendererStateAndAvoidPropertyBlocks()
        {
            VividPerObjectLayout layout = CreateFullLayout();
            var gameObject = new GameObject("Per Object Renderer");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            const uint originalValue = 0x12345678u;
            renderer.SetShaderUserValue(originalValue);
            try
            {
                VividPerObjectBlock block = VividPerObjectBuffer.Bind(renderer, layout);
                VividPerObjectBlock repeated = VividPerObjectBuffer.Bind(renderer, layout);
                Assert.That(repeated, Is.EqualTo(block));
                Assert.That(renderer.GetShaderUserValue() & VividPerObjectBufferSystem.ShaderUserValueMagicMask,
                    Is.EqualTo(VividPerObjectBufferSystem.ShaderUserValueMagic));

                var matrix = new Matrix4x4();
                matrix.SetColumn(0, new Vector4(21, 22, 23, 24));
                matrix.SetColumn(1, new Vector4(25, 26, 27, 28));
                matrix.SetColumn(2, new Vector4(29, 30, 31, 32));
                matrix.SetColumn(3, new Vector4(33, 34, 35, 36));
                block.SetInt("_Count", 11);
                block.SetFloat(Shader.PropertyToID("_Dissolve"), 0.75f);
                block.SetVector(layout.GetProperty("_Direction"), new Vector4(2, 3, 4, 5));
                block.SetColor("_Tint", new Color(0.6f, 0.7f, 0.8f, 0.9f));
                block.SetMatrix("_Deformation", matrix);

                int address = VividPerObjectBufferSystem.GetRecordAddressForTests(block);
                byte[] data = VividPerObjectBufferSystem.GetDataForTests();
                Assert.That(ReadInt(data, address + 4), Is.EqualTo(11));
                Assert.That(ReadFloat(data, address + 8), Is.EqualTo(0.75f));
                Assert.That(ReadFloat(data, address + 12), Is.EqualTo(2.0f));
                Assert.That(ReadFloat(data, address + 28), Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(ReadFloat(data, address + 44), Is.EqualTo(21.0f));
                Assert.That(renderer.HasPropertyBlock(), Is.False);

                VividPerObjectBuffer.Unbind(renderer);
                Assert.That(block.IsValid, Is.False);
                Assert.That(renderer.GetShaderUserValue(), Is.EqualTo(originalValue));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SetValue_DoesNotDirtyRecord_WhenBitsAreUnchanged()
        {
            VividPerObjectLayout layout = CreateFullLayout();
            var gameObject = new GameObject("Per Object Dirty Renderer");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            try
            {
                VividPerObjectBlock block = VividPerObjectBuffer.Bind(renderer, layout);
                VividPerObjectBufferSystem.ClearDirtyRangesForTests();

                block.SetFloat("_Dissolve", 0.25f);
                Assert.That(VividPerObjectBuffer.GetStats().DirtyRangeCount, Is.Zero);

                block.SetFloat("_Dissolve", 0.5f);
                Assert.That(VividPerObjectBuffer.GetStats().DirtyRangeCount, Is.EqualTo(1));
                VividPerObjectBufferSystem.ClearDirtyRangesForTests();
                block.SetFloat("_Dissolve", 0.5f);
                Assert.That(VividPerObjectBuffer.GetStats().DirtyRangeCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SetValue_DoesNotDirtyRecord_ForUnchangedVectorColorAndMatrix()
        {
            VividPerObjectLayout layout = CreateFullLayout();
            var gameObject = new GameObject("Per Object Unchanged Values Renderer");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            try
            {
                VividPerObjectBlock block = VividPerObjectBuffer.Bind(renderer, layout);
                VividPerObjectBufferSystem.ClearDirtyRangesForTests();

                block.SetVector("_Direction", new Vector4(1, 2, 3, 4));
                block.SetColor("_Tint", new Color(0.1f, 0.2f, 0.3f, 0.4f));

                var matrix = new Matrix4x4();
                int next = 1;
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                        matrix[row, column] = next++;
                }
                block.SetMatrix("_Deformation", matrix);

                Assert.That(VividPerObjectBuffer.GetStats().DirtyRangeCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }


        [Test]
        public void Rebind_InvalidatesOldBlockAndRestoresOriginalValueOnUnbind()
        {
            VividPerObjectLayout firstLayout = CreateFullLayout();
            VividPerObjectLayout secondLayout = CreateFloatLayout("Second", "_Other", 3.0f);
            var gameObject = new GameObject("Per Object Rebind Renderer");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.SetShaderUserValue(77u);
            try
            {
                VividPerObjectBlock first = VividPerObjectBuffer.Bind(renderer, firstLayout);
                VividPerObjectBlock second = VividPerObjectBuffer.Bind(renderer, secondLayout);

                Assert.That(first.IsValid, Is.False);
                Assert.That(second.IsValid, Is.True);
                Assert.Throws<InvalidOperationException>(() => first.SetFloat("_Dissolve", 1.0f));
                VividPerObjectBuffer.Unbind(renderer);
                Assert.That(renderer.GetShaderUserValue(), Is.EqualTo(77u));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SetValue_Throws_ForWrongTypeAndForeignHandle()
        {
            VividPerObjectLayout layout = CreateFullLayout();
            VividPerObjectLayout foreignLayout = CreateFloatLayout("Foreign", "_Foreign", 0.0f);
            var gameObject = new GameObject("Per Object Error Renderer");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            try
            {
                VividPerObjectBlock block = VividPerObjectBuffer.Bind(renderer, layout);
                Assert.Throws<ArgumentException>(() => block.SetInt("_Dissolve", 1));
                Assert.Throws<ArgumentException>(() => block.SetFloat(foreignLayout.GetProperty("_Foreign"), 1.0f));
                Assert.Throws<ArgumentException>(() => block.SetFloat("_Missing", 1.0f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Bind_Throws_ForUnsupportedRenderer()
        {
            VividPerObjectLayout layout = CreateFullLayout();
            var gameObject = new GameObject("Unsupported Renderer");
            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            try
            {
                Assert.Throws<NotSupportedException>(() => VividPerObjectBuffer.Bind(renderer, layout));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DestroyedRenderer_IsReclaimedBySweep()
        {
            VividPerObjectLayout layout = CreateFullLayout();
            var gameObject = new GameObject("Destroyed Per Object Renderer");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            VividPerObjectBuffer.Bind(renderer, layout);
            Object.DestroyImmediate(gameObject);

            VividPerObjectBufferSystem.SweepDestroyedRenderersForTests();
            Assert.That(VividPerObjectBuffer.GetStats().ActiveRendererCount, Is.Zero);
            Assert.That(VividPerObjectBuffer.GetStats().UsedBytes,
                Is.EqualTo(VividPerObjectRecordAllocator.ReservedBytes));
        }

        [Test]
        public void DestroyedRenderer_PeriodicSweepDelaysFullScanUntilInterval()
        {
            VividPerObjectLayout layout = CreateFullLayout();
            var gameObject = new GameObject("Periodically Swept Per Object Renderer");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            VividPerObjectBuffer.Bind(renderer, layout);

            VividPerObjectBufferSystem.SweepDestroyedRenderersIfNeededForTests();
            Object.DestroyImmediate(gameObject);

            for (int i = 0; i < 15; i++)
                VividPerObjectBufferSystem.SweepDestroyedRenderersIfNeededForTests();

            Assert.That(VividPerObjectBuffer.GetStats().ActiveRendererCount, Is.EqualTo(1));

            VividPerObjectBufferSystem.SweepDestroyedRenderersIfNeededForTests();
            Assert.That(VividPerObjectBuffer.GetStats().ActiveRendererCount, Is.Zero);
            Assert.That(
                VividPerObjectBuffer.GetStats().UsedBytes,
                Is.EqualTo(VividPerObjectRecordAllocator.ReservedBytes));
        }

        [Test]
        public void PublicApi_ThrowsFromWorkerThread()
        {
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    VividPerObjectBuffer.GetStats();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });
            thread.Start();
            thread.Join();

            Assert.That(captured, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void PrepareAndBind_FallbackUploadsOnlyOnceAcrossCameras()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A graphics device is required for GraphicsBuffer upload validation.");

            VividPerObjectLayout layout = CreateFullLayout();
            var gameObject = new GameObject("Per Object Upload Renderer");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            var firstCommandBuffer = new CommandBuffer();
            var secondCommandBuffer = new CommandBuffer();
            try
            {
                VividPerObjectBuffer.Bind(renderer, layout);
                VividPerObjectBufferSystem.SetForceFallbackForTests(true);
                VividPerObjectBuffer.PrepareAndBind(firstCommandBuffer);
                VividPerObjectBufferStats firstStats = VividPerObjectBuffer.GetStats();
                Assert.That(firstStats.LastUploadBytes, Is.GreaterThan(0));
                Assert.That(firstStats.DirtyRangeCount, Is.Zero);

                VividPerObjectBuffer.PrepareAndBind(secondCommandBuffer);
                VividPerObjectBufferStats secondStats = VividPerObjectBuffer.GetStats();
                Assert.That(secondStats.LastUploadBytes, Is.EqualTo(firstStats.LastUploadBytes));
                Assert.That(secondStats.LastUploadRangeCount, Is.EqualTo(firstStats.LastUploadRangeCount));
            }
            finally
            {
                firstCommandBuffer.Dispose();
                secondCommandBuffer.Dispose();
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ThousandRenderers_LocalFallbackUpdateUploadsOnlyChangedBytes()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A graphics device is required for GraphicsBuffer upload validation.");

            const int rendererCount = 1001;
            VividPerObjectLayout layout = CreateFullLayout();
            var gameObjects = new GameObject[rendererCount];
            var blocks = new VividPerObjectBlock[rendererCount];
            var initialUpload = new CommandBuffer();
            var changedUpload = new CommandBuffer();
            var unchangedUpload = new CommandBuffer();
            try
            {
                for (int i = 0; i < rendererCount; i++)
                {
                    var gameObject = new GameObject($"Per Object Stress Renderer {i}");
                    MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                    gameObjects[i] = gameObject;
                    blocks[i] = VividPerObjectBuffer.Bind(renderer, layout);
                    Assert.That(renderer.HasPropertyBlock(), Is.False);
                }

                VividPerObjectBufferSystem.SetForceFallbackForTests(true);
                VividPerObjectBuffer.PrepareAndBind(initialUpload);
                VividPerObjectBufferStats baseline = VividPerObjectBuffer.GetStats();
                Assert.That(baseline.ActiveRendererCount, Is.EqualTo(rendererCount));
                Assert.That(baseline.CapacityBytes, Is.GreaterThan(64 * 1024));

                blocks[rendererCount / 2].SetInt("_Count", 8);
                VividPerObjectBuffer.PrepareAndBind(changedUpload);
                VividPerObjectBufferStats changed = VividPerObjectBuffer.GetStats();
                Assert.That(changed.LastUploadBytes - baseline.LastUploadBytes, Is.EqualTo(sizeof(int)));
                Assert.That(changed.LastUploadRangeCount - baseline.LastUploadRangeCount, Is.EqualTo(1));

                VividPerObjectBuffer.PrepareAndBind(unchangedUpload);
                VividPerObjectBufferStats unchanged = VividPerObjectBuffer.GetStats();
                Assert.That(unchanged.LastUploadBytes, Is.EqualTo(changed.LastUploadBytes));
                Assert.That(unchanged.LastUploadRangeCount, Is.EqualTo(changed.LastUploadRangeCount));
            }
            finally
            {
                initialUpload.Dispose();
                changedUpload.Dispose();
                unchangedUpload.Dispose();
                for (int i = 0; i < gameObjects.Length; i++)
                {
                    if (gameObjects[i] != null)
                        Object.DestroyImmediate(gameObjects[i]);
                }
            }
        }

        internal static VividPerObjectLayout CreateFullLayout()
        {
            var matrix = new Matrix4x4();
            int next = 1;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                    matrix[row, column] = next++;
            }

            return new TestLayout("Character", builder =>
            {
                builder.AddInt("_Count", 7);
                builder.AddFloat("_Dissolve", 0.25f);
                builder.AddVector("_Direction", new Vector4(1, 2, 3, 4));
                builder.AddColor("_Tint", new Color(0.1f, 0.2f, 0.3f, 0.4f));
                builder.AddMatrix("_Deformation", matrix);
            });
        }

        internal static VividPerObjectLayout CreateFloatLayout(
            string identifier,
            string propertyName,
            float defaultValue)
        {
            return new TestLayout(identifier, builder =>
            {
                builder.AddFloat(propertyName, defaultValue);
            });
        }

        internal static VividPerObjectLayout CreateLayout(
            string identifier,
            Action<VividPerObjectLayoutBuilder> define)
        {
            return new TestLayout(identifier, define);
        }

        private sealed class TestLayout : VividPerObjectLayout
        {
            private readonly string m_ShaderIdentifier;
            private readonly Action<VividPerObjectLayoutBuilder> m_Define;

            internal TestLayout(string shaderIdentifier, Action<VividPerObjectLayoutBuilder> define)
            {
                m_ShaderIdentifier = shaderIdentifier;
                m_Define = define;
            }

            public override string ShaderIdentifier => m_ShaderIdentifier;

            protected override void Define(VividPerObjectLayoutBuilder builder)
            {
                m_Define(builder);
            }
        }

        private sealed class CodeFloatLayout : VividPerObjectLayout<CodeFloatLayout>
        {
            public CodeFloatLayout()
            {
            }

            public override string ShaderIdentifier => "CodeFloat";

            internal VividPerObjectPropertyHandle Value => GetProperty("_Value");

            protected override void Define(VividPerObjectLayoutBuilder builder)
            {
                builder.AddFloat("_Value", 1.0f);
            }
        }

        private static uint ReadUInt(byte[] data, int offset)
        {
            return BitConverter.ToUInt32(data, offset);
        }

        private static int ReadInt(byte[] data, int offset)
        {
            return BitConverter.ToInt32(data, offset);
        }

        private static float ReadFloat(byte[] data, int offset)
        {
            return BitConverter.ToSingle(data, offset);
        }
    }
}
