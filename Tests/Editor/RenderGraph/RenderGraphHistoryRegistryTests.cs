using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderGraphHistoryRegistryTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            RTHandles.Initialize(1, 1);
        }

        [SetUp]
        public void SetUp()
        {
            PassRecorder.Dispose();
            RenderGraphHistoryRegistry.Clear();
            RenderGraphBufferHistoryRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PassRecorder.Dispose();
            RenderGraphHistoryRegistry.Clear();
            RenderGraphBufferHistoryRegistry.Clear();
        }

        [Test]
        public void AcquireHistoryTextures_ReusesHandles_WhenDescriptorMatches()
        {
            using var cameraScope = new CameraScope();
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();

            try
            {
                var descriptor = new RenderGraphTextureDesc
                {
                    Width = 64,
                    Height = 32,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    Name = "HistoryA",
                };

                var firstFound = RenderGraphHistoryRegistry.AcquireHistoryTextures(
                    cameraScope.Camera,
                    graphAsset,
                    0,
                    descriptor,
                    out var firstPrevious,
                    out var firstCurrent,
                    out var firstHasValidData);
                var secondFound = RenderGraphHistoryRegistry.AcquireHistoryTextures(
                    cameraScope.Camera,
                    graphAsset,
                    0,
                    descriptor,
                    out var secondPrevious,
                    out var secondCurrent,
                    out var secondHasValidData);

                Assert.That(firstFound, Is.True);
                Assert.That(secondFound, Is.True);
                Assert.That(firstPrevious, Is.Not.Null);
                Assert.That(firstCurrent, Is.Not.Null);
                Assert.That(firstCurrent, Is.Not.SameAs(firstPrevious));
                Assert.That(secondPrevious, Is.SameAs(firstPrevious));
                Assert.That(secondCurrent, Is.SameAs(firstCurrent));
                Assert.That(firstHasValidData, Is.False);
                Assert.That(secondHasValidData, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void AcquireHistoryTextures_RecreatesHandles_WhenDescriptorChanges()
        {
            using var cameraScope = new CameraScope();
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();

            try
            {
                var firstDescriptor = new RenderGraphTextureDesc
                {
                    Width = 64,
                    Height = 32,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    Name = "HistoryA",
                };
                var secondDescriptor = new RenderGraphTextureDesc
                {
                    Width = 128,
                    Height = 64,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    Name = "HistoryA",
                };

                var firstFound = RenderGraphHistoryRegistry.AcquireHistoryTextures(
                    cameraScope.Camera,
                    graphAsset,
                    0,
                    firstDescriptor,
                    out var firstPrevious,
                    out var firstCurrent,
                    out _);
                var secondFound = RenderGraphHistoryRegistry.AcquireHistoryTextures(
                    cameraScope.Camera,
                    graphAsset,
                    0,
                    secondDescriptor,
                    out var secondPrevious,
                    out var secondCurrent,
                    out _);

                Assert.That(firstFound, Is.True);
                Assert.That(secondFound, Is.True);
                Assert.That(firstPrevious, Is.Not.Null);
                Assert.That(firstCurrent, Is.Not.Null);
                Assert.That(secondPrevious, Is.Not.Null);
                Assert.That(secondCurrent, Is.Not.Null);
                Assert.That(secondPrevious, Is.Not.SameAs(firstPrevious));
                Assert.That(secondCurrent, Is.Not.SameAs(firstCurrent));
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void CommitHistory_SwapsBuffers_AndUpdatesValidityState()
        {
            using var cameraScope = new CameraScope();
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();

            try
            {
                var descriptor = new RenderGraphTextureDesc
                {
                    Width = 32,
                    Height = 32,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    Name = "HistoryA",
                };

                var found = RenderGraphHistoryRegistry.AcquireHistoryTextures(
                    cameraScope.Camera,
                    graphAsset,
                    0,
                    descriptor,
                    out var previousHandle,
                    out var currentHandle,
                    out var hasValidData);

                Assert.That(found, Is.True);
                Assert.That(previousHandle, Is.Not.Null);
                Assert.That(currentHandle, Is.Not.Null);
                Assert.That(hasValidData, Is.False);

                RenderGraphHistoryRegistry.CommitHistory(cameraScope.Camera, graphAsset, 0);

                found = RenderGraphHistoryRegistry.TryGetHistoryTextures(
                    cameraScope.Camera,
                    graphAsset,
                    0,
                    out var committedPreviousHandle,
                    out var committedCurrentHandle,
                    out hasValidData);

                Assert.That(found, Is.True);
                Assert.That(hasValidData, Is.True);
                Assert.That(committedPreviousHandle, Is.SameAs(currentHandle));
                Assert.That(committedCurrentHandle, Is.SameAs(previousHandle));
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void BuildKey_UsesCameraAndGraphAssetEntityIds()
        {
            using var cameraScope = new CameraScope();
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();

            try
            {
                var buildKeyMethod = typeof(RenderGraphHistoryRegistry).GetMethod("BuildKey", BindingFlags.Static | BindingFlags.NonPublic);

                Assert.That(buildKeyMethod, Is.Not.Null);

                var key = (string)buildKeyMethod.Invoke(null, new object[] { cameraScope.Camera, graphAsset, 3 });

                Assert.That(key, Is.EqualTo($"{cameraScope.Camera.GetEntityId()}|{graphAsset.GetEntityId()}|3"));
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void CommitFrame_AdvancesTextureHistory_ForPassScopedHistory()
        {
            using var cameraScope = new CameraScope();
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<TextureHistoryAllocationPass>(),
            });

            try
            {
                SetCurrentCamera(cameraScope.Camera);
                Compile(graphAsset);

                var pass = GetCompiledPass<TextureHistoryAllocationPass>();
                Assert.That(pass, Is.Not.Null);

                var previous = GetTextureField(pass, TextureHistoryAllocationPass.PreviousFieldName);
                var current = GetTextureField(pass, TextureHistoryAllocationPass.CurrentFieldName);
                var historyKey = PassRecorder.BuildPassHistoryKey(pass, TextureHistoryAllocationPass.HistoryKey);
                Assert.That(historyKey, Is.Not.Null);

                var hasValidHistory = pass.AllocHistoryTexture(TextureHistoryAllocationPass.HistoryKey, previous, current, current.desc);
                Assert.That(hasValidHistory, Is.False);

                var found = RenderGraphHistoryRegistry.TryGetHistoryTextures(
                    cameraScope.Camera,
                    graphAsset,
                    historyKey,
                    out var firstPreviousHandle,
                    out var firstCurrentHandle,
                    out var hasValidData);
                Assert.That(found, Is.True);
                Assert.That(hasValidData, Is.False);
                Assert.That(firstPreviousHandle, Is.Not.Null);
                Assert.That(firstCurrentHandle, Is.Not.Null);

                PassRecorder.CommitFrame(graphAsset);

                hasValidHistory = pass.AllocHistoryTexture(TextureHistoryAllocationPass.HistoryKey, previous, current, current.desc);
                Assert.That(hasValidHistory, Is.True);
                found = RenderGraphHistoryRegistry.TryGetHistoryTextures(
                    cameraScope.Camera,
                    graphAsset,
                    historyKey,
                    out var secondPreviousHandle,
                    out var secondCurrentHandle,
                    out hasValidData);
                Assert.That(found, Is.True);
                Assert.That(hasValidData, Is.True);
                Assert.That(secondPreviousHandle, Is.SameAs(firstCurrentHandle));
                Assert.That(secondCurrentHandle, Is.SameAs(firstPreviousHandle));

                var resizedDescriptor = current.desc.Clone();
                resizedDescriptor.Width = 128;
                resizedDescriptor.Height = 64;
                hasValidHistory = pass.AllocHistoryTexture(TextureHistoryAllocationPass.HistoryKey, previous, current, resizedDescriptor);
                Assert.That(hasValidHistory, Is.False);
            }
            finally
            {
                PassRecorder.Dispose();
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void AbortFrame_DoesNotAdvanceTextureHistory()
        {
            using var cameraScope = new CameraScope();
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<TextureHistoryAllocationPass>(),
            });

            try
            {
                SetCurrentCamera(cameraScope.Camera);
                Compile(graphAsset);

                var pass = GetCompiledPass<TextureHistoryAllocationPass>();
                Assert.That(pass, Is.Not.Null);

                var previous = GetTextureField(pass, TextureHistoryAllocationPass.PreviousFieldName);
                var current = GetTextureField(pass, TextureHistoryAllocationPass.CurrentFieldName);
                var historyKey = PassRecorder.BuildPassHistoryKey(pass, TextureHistoryAllocationPass.HistoryKey);
                Assert.That(historyKey, Is.Not.Null);

                var hasValidHistory = pass.AllocHistoryTexture(TextureHistoryAllocationPass.HistoryKey, previous, current, current.desc);
                Assert.That(hasValidHistory, Is.False);

                var found = RenderGraphHistoryRegistry.TryGetHistoryTextures(
                    cameraScope.Camera,
                    graphAsset,
                    historyKey,
                    out var firstPreviousHandle,
                    out var firstCurrentHandle,
                    out var hasValidData);
                Assert.That(found, Is.True);
                Assert.That(hasValidData, Is.False);

                PassRecorder.AbortFrame();

                hasValidHistory = pass.AllocHistoryTexture(TextureHistoryAllocationPass.HistoryKey, previous, current, current.desc);
                Assert.That(hasValidHistory, Is.False);
                found = RenderGraphHistoryRegistry.TryGetHistoryTextures(
                    cameraScope.Camera,
                    graphAsset,
                    historyKey,
                    out var secondPreviousHandle,
                    out var secondCurrentHandle,
                    out hasValidData);
                Assert.That(found, Is.True);
                Assert.That(hasValidData, Is.False);
                Assert.That(secondPreviousHandle, Is.SameAs(firstPreviousHandle));
                Assert.That(secondCurrentHandle, Is.SameAs(firstCurrentHandle));
            }
            finally
            {
                PassRecorder.Dispose();
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void CommitFrame_AdvancesBufferHistory_AndClearsOnDispose()
        {
            using var cameraScope = new CameraScope();
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<BufferHistoryAllocationPass>(),
            });

            try
            {
                SetCurrentCamera(cameraScope.Camera);
                Compile(graphAsset);

                var pass = GetCompiledPass<BufferHistoryAllocationPass>();
                Assert.That(pass, Is.Not.Null);

                var previous = GetBufferField(pass, BufferHistoryAllocationPass.PreviousFieldName);
                var current = GetBufferField(pass, BufferHistoryAllocationPass.CurrentFieldName);
                var historyKey = PassRecorder.BuildPassHistoryKey(pass, BufferHistoryAllocationPass.HistoryKey);
                Assert.That(historyKey, Is.Not.Null);

                var hasValidHistory = pass.AllocHistoryBuffer(BufferHistoryAllocationPass.HistoryKey, previous, current, current.desc);
                Assert.That(hasValidHistory, Is.False);

                var firstPreviousBuffer = previous.ImportedGraphicsBuffer;
                var firstCurrentBuffer = current.ImportedGraphicsBuffer;
                Assert.That(firstPreviousBuffer, Is.Not.Null);
                Assert.That(firstCurrentBuffer, Is.Not.Null);
                Assert.That(firstCurrentBuffer, Is.Not.SameAs(firstPreviousBuffer));

                PassRecorder.CommitFrame(graphAsset);

                hasValidHistory = pass.AllocHistoryBuffer(BufferHistoryAllocationPass.HistoryKey, previous, current, current.desc);
                Assert.That(hasValidHistory, Is.True);
                Assert.That(previous.ImportedGraphicsBuffer, Is.SameAs(firstCurrentBuffer));
                Assert.That(current.ImportedGraphicsBuffer, Is.SameAs(firstPreviousBuffer));

                var resizedDescriptor = current.desc.Clone();
                resizedDescriptor.Count = 32;
                hasValidHistory = pass.AllocHistoryBuffer(BufferHistoryAllocationPass.HistoryKey, previous, current, resizedDescriptor);
                Assert.That(hasValidHistory, Is.False);
                Assert.That(previous.ImportedGraphicsBuffer, Is.Not.SameAs(firstCurrentBuffer));
                Assert.That(current.ImportedGraphicsBuffer, Is.Not.SameAs(firstPreviousBuffer));

                PassRecorder.Dispose();

                Assert.That(previous.ImportedGraphicsBuffer, Is.Null);
                Assert.That(current.ImportedGraphicsBuffer, Is.Null);
            }
            finally
            {
                PassRecorder.Dispose();
                Object.DestroyImmediate(graphAsset);
            }
        }

        private static void Compile(RenderGraphData graphAsset)
        {
            var method = typeof(PassRecorder).GetMethod("Compile", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { graphAsset });
        }

        private static void SetCurrentCamera(Camera camera)
        {
            var frameDataField = typeof(PassRecorder).GetField("s_FrameData", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(frameDataField, Is.Not.Null);

            var frameData = (ContextContainer)frameDataField.GetValue(null);
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.camera = camera;
        }

        private static TPass GetCompiledPass<TPass>() where TPass : class, IRenderPass
        {
            var field = typeof(PassRecorder).GetField("s_RenderPasses", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            var passes = (System.Collections.IEnumerable)field.GetValue(null);
            foreach (var pass in passes)
            {
                if (pass is TPass typedPass)
                    return typedPass;
            }

            return null;
        }

        private static RenderGraphTexture GetTextureField(object pass, string fieldName)
        {
            var field = pass.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static RenderGraphBuffer GetBufferField(object pass, string fieldName)
        {
            var field = pass.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (RenderGraphBuffer)field.GetValue(pass);
        }

        private static string GetPassTypeName<TPass>()
        {
            var type = typeof(TPass);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }

        private sealed class CameraScope : System.IDisposable
        {
            private readonly GameObject m_GameObject;

            internal CameraScope()
            {
                m_GameObject = new GameObject("HistoryRegistryCamera");
                Camera = m_GameObject.AddComponent<Camera>();
            }

            internal Camera Camera { get; }

            public void Dispose()
            {
                if (m_GameObject != null)
                    Object.DestroyImmediate(m_GameObject);
            }
        }
    }

    internal sealed class TextureHistoryAllocationPass : ComputePass
    {
        internal const string PreviousFieldName = "m_Previous";
        internal const string CurrentFieldName = "m_Current";
        internal const string HistoryKey = "TemporalHistory";

        [RenderGraphResource(
            Name = "PreviousHistory",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private readonly RenderGraphTexture m_Previous = new RenderGraphTexture
        {
            desc = RenderGraphTextureDesc.CreateColorTarget(64, 32, GraphicsFormat.R8G8B8A8_UNorm)
        };

        [RenderGraphResource(
            Name = "CurrentHistory",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private readonly RenderGraphTexture m_Current = new RenderGraphTexture
        {
            desc = RenderGraphTextureDesc.CreateColorTarget(64, 32, GraphicsFormat.R8G8B8A8_UNorm)
        };

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(ComputePassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    internal sealed class BufferHistoryAllocationPass : ComputePass
    {
        internal const string PreviousFieldName = "m_Previous";
        internal const string CurrentFieldName = "m_Current";
        internal const string HistoryKey = "TemporalBufferHistory";

        [RenderGraphResource(
            Name = "PreviousHistory",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private readonly RenderGraphBuffer m_Previous = new RenderGraphBuffer
        {
            desc = RenderGraphBufferDesc.CreateStructured(16, sizeof(uint))
        };

        [RenderGraphResource(
            Name = "CurrentHistory",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private readonly RenderGraphBuffer m_Current = new RenderGraphBuffer
        {
            desc = RenderGraphBufferDesc.CreateStructured(16, sizeof(uint))
        };

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(ComputePassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }
}
