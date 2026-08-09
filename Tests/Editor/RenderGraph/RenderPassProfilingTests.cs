using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class RenderPassProfilingTests
    {
        [TearDown]
        public void TearDown()
        {
            PassRecorder.Dispose();
            RenderPassProfilingUtility.Clear();
        }

        [Test]
        public void GetMarkers_ReturnsCachedMarkers_ForSamePassDisplayNameAndIndex()
        {
            var pass = new ProfilingTestPass();

            var markers = RenderPassProfilingUtility.GetMarkers(pass, "ProfilingTestPass", 3);
            var cachedMarkers = RenderPassProfilingUtility.GetMarkers(pass, "ProfilingTestPass", 3);

            Assert.That(cachedMarkers, Is.SameAs(markers));
        }

        [Test]
        public void GetMarkers_UsesProfilingSampler_ForGpuRecordTiming()
        {
            var pass = new ProfilingTestPass();

            var markers = RenderPassProfilingUtility.GetMarkers(pass, "ProfilingTestPass", 3);

            Assert.That(markers.Record, Is.TypeOf<ProfilingSampler>());
            Assert.That(markers.Record.name, Is.EqualTo("VividRP.RenderPass.Record/3:ProfilingTestPass"));
        }

        [Test]
        public void GetMarkers_StoresGraphName_ForRenderGraphPassNameReuse()
        {
            var pass = new ProfilingTestPass();

            var markers = RenderPassProfilingUtility.GetMarkers(pass, null, 3);

            Assert.That(markers.GraphName, Is.EqualTo(nameof(ProfilingTestPass)));
        }

        [Test]
        public void GetMarkers_DoesNotAllocate_ForCachedDefaultPassLookup()
        {
            var pass = new ProfilingTestPass();
            RenderPassProfilingUtility.GetMarkers(pass, null, 3);

            var before = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 128; i++)
                RenderPassProfilingUtility.GetMarkers(pass, null, 3);
            var after = global::System.GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero);
        }

        [Test]
        public void GetMarkers_ReturnsSeparateMarkers_WhenDisplayNameDiffers()
        {
            var pass = new ProfilingTestPass();

            var defaultMarkers = RenderPassProfilingUtility.GetMarkers(pass, "ProfilingTestPass", 3);
            var injectedMarkers = RenderPassProfilingUtility.GetMarkers(pass, "ProfilingTestPass (Injected)", 3);

            Assert.That(injectedMarkers, Is.Not.SameAs(defaultMarkers));
        }

        [Test]
        public void PassRecorder_UsesPassDefinitionName_ForGraphAndLifecycleMarkers()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<DrawObjectPass>(),
                PassName = "Transparent Characters",
            });

            try
            {
                Compile(graphAsset);
                var pass = GetCompiledPasses()[0];
                var markers = GetPassMarkers(pass);

                Assert.That(markers.GraphName, Is.EqualTo("Transparent Characters"));
                Assert.That(markers.DisplayName, Is.EqualTo("0:Transparent Characters"));
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void PassRecorder_FallsBackToPassTypeName_WhenLegacyPassNameIsMissing()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<DrawObjectPass>(),
            });

            try
            {
                Compile(graphAsset);
                var markers = GetPassMarkers(GetCompiledPasses()[0]);

                Assert.That(markers.GraphName, Is.EqualTo(nameof(DrawObjectPass)));
                Assert.That(markers.DisplayName, Is.EqualTo($"0:{nameof(DrawObjectPass)}"));
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void PassRecorder_ExplicitChildPassName_OverridesAuthoredPassName()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<DrawObjectPass>(),
                PassName = "Authored Outer Pass",
            });

            try
            {
                Compile(graphAsset);
                var pass = GetCompiledPasses()[0];
                var markers = GetPassMarkers(pass, "Explicit Child Pass");

                Assert.That(markers.GraphName, Is.EqualTo("Explicit Child Pass"));
                Assert.That(markers.DisplayName, Is.EqualTo("0:Explicit Child Pass"));
            }
            finally
            {
                Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void PrepareRenderPass_CallsResizeBeforePrepare_OnlyWhenRenderSizeChanges()
        {
            var pass = new ResizeTrackingPass();
            var cameraData = PassRecorder.GetFrameData().GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 640;
            cameraData.actualHeight = 360;

            Prepare(pass);
            Prepare(pass);

            cameraData.actualWidth = 1280;
            cameraData.actualHeight = 720;
            Prepare(pass);

            Assert.That(pass.Events, Is.EqualTo(new[]
            {
                "Resize:640x360",
                "Prepare",
                "Prepare",
                "Resize:1280x720",
                "Prepare",
            }));
        }

        private static void Compile(RenderGraphData graphAsset)
        {
            var method = typeof(PassRecorder).GetMethod("Compile", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { graphAsset });
        }

        private static IList<IRenderPass> GetCompiledPasses()
        {
            var field = typeof(PassRecorder).GetField("s_RenderPasses", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            return (IList<IRenderPass>)field.GetValue(null);
        }

        private static RenderPassProfilerMarkers GetPassMarkers(IRenderPass pass, string displayName = null)
        {
            var method = typeof(PassRecorder).GetMethod("GetPassMarkers", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (RenderPassProfilerMarkers)method.Invoke(null, new object[] { pass, displayName });
        }

        private static void Prepare(IRenderPass pass)
        {
            var method = typeof(PassRecorder).GetMethod("PrepareRenderPass", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { pass, null });
        }

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }

        private sealed class ProfilingTestPass : RasterPass
        {
            public override void Create()
            {
            }

            public override void Prepare(ContextContainer frameData)
            {
            }

            public override void Record(RasterPassContext context)
            {
            }

            public override void Dispose()
            {
            }
        }

        private sealed class ResizeTrackingPass : RasterPass
        {
            public readonly List<string> Events = new();

            public override void Create()
            {
            }

            public override void Resize(int width, int height)
            {
                Events.Add($"Resize:{width}x{height}");
            }

            public override void Prepare(ContextContainer frameData)
            {
                Events.Add("Prepare");
            }

            public override void Record(RasterPassContext context)
            {
            }

            public override void Dispose()
            {
            }
        }
    }
}
