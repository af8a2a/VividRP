using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderGraphBufferHistoryRegistryTests
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
            RenderGraphBufferHistoryRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PassRecorder.Dispose();
            RenderGraphBufferHistoryRegistry.Clear();
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
                m_GameObject = new GameObject("BufferHistoryRegistryCamera");
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

    internal sealed class BufferHistoryAllocationPass : ComputePass
    {
        internal const string PreviousFieldName = "m_Previous";
        internal const string CurrentFieldName = "m_Current";
        internal const string HistoryKey = "TemporalBufferHistory";

        [RenderGraphResource(
            Name = "PreviousHistory",
            Access = AccessFlags.Read)]
        private readonly RenderGraphBuffer m_Previous = new RenderGraphBuffer
        {
            desc = RenderGraphBufferDesc.CreateStructured(16, sizeof(uint))
        };

        [RenderGraphResource(
            Name = "CurrentHistory",
            Access = AccessFlags.Write)]
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
