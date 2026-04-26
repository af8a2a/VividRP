using System.IO;
using NUnit.Framework;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderPassProfilingTests
    {
        [TearDown]
        public void TearDown()
        {
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
        public void PassRecorder_WrapsRenderPassRecord_WithCpuAndCommandProfiling()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.cs"))
                         + File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.Execution.cs"));

            Assert.That(source, Does.Contain("data.Markers.Record.Auto()"));
            Assert.That(source, Does.Contain("data.Markers.CommandSampler"));
            Assert.That(source, Does.Contain("markers.GraphName, out var passData"));
            Assert.That(source, Does.Not.Contain("passName ?? pass.GetType().Name"));
            Assert.That(source, Does.Contain("s_ComputeRenderFunc"));
            Assert.That(source, Does.Contain("s_RasterRenderFunc"));
            Assert.That(source, Does.Contain("s_UnsafeRenderFunc"));
            Assert.That(source, Does.Contain("s_RenderGizmosRenderFunc"));
            Assert.That(source, Does.Not.Contain("static (data, ctx)"));
            Assert.That(source, Does.Not.Contain("static (data, context)"));
        }

        [Test]
        public void PrepareFrame_UsesFineGrainedMarkers_ForGcAttribution()
        {
            var profilingSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "RenderPassProfiling.cs"));
            var passRecorderSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.Execution.cs"));
            var frameContextSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "FrameContextSystem.cs"));

            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/EnsureCompiled"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/ClearHistoryImports"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/ClearCodeManagedHistory"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/FrameContext.Update"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/PrepareHistoryTargets"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/ClearImportedTextures"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/FrameContext.PurgeDestroyedCameras"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/FrameContext.ResolveData"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/FrameContext.AdvanceTemporal"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/FrameContext.PopulateTemporal"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/FrameContext.AutoExposure"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/FrameContext.BuildShaderVariables"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/FrameContext.SetShaderGlobals"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/FrameContext.AdaptiveProbeVolume"));
            Assert.That(profilingSource, Does.Contain("VividRP.RenderPass.PrepareFrame/FrameContext.SubsystemPreRender"));

            Assert.That(passRecorderSource, Does.Contain("PrepareFrameEnsureCompiledMarker.Auto()"));
            Assert.That(passRecorderSource, Does.Contain("PrepareFrameClearHistoryImportsMarker.Auto()"));
            Assert.That(passRecorderSource, Does.Contain("PrepareFrameClearCodeManagedHistoryMarker.Auto()"));
            Assert.That(passRecorderSource, Does.Contain("PrepareFrameContextUpdateMarker.Auto()"));
            Assert.That(passRecorderSource, Does.Contain("PrepareFramePrepareHistoryTargetsMarker.Auto()"));
            Assert.That(passRecorderSource, Does.Contain("PrepareFrameClearImportedTexturesMarker.Auto()"));

            Assert.That(frameContextSource, Does.Contain("PrepareFrameContextPurgeDestroyedCamerasMarker.Auto()"));
            Assert.That(frameContextSource, Does.Contain("PrepareFrameContextResolveDataMarker.Auto()"));
            Assert.That(frameContextSource, Does.Contain("PrepareFrameContextAdvanceTemporalMarker.Auto()"));
            Assert.That(frameContextSource, Does.Contain("PrepareFrameContextPopulateTemporalMarker.Auto()"));
            Assert.That(frameContextSource, Does.Contain("PrepareFrameContextAutoExposureMarker.Auto()"));
            Assert.That(frameContextSource, Does.Contain("PrepareFrameContextBuildShaderVariablesMarker.Auto()"));
            Assert.That(frameContextSource, Does.Contain("PrepareFrameContextSetShaderGlobalsMarker.Auto()"));
            Assert.That(frameContextSource, Does.Contain("PrepareFrameContextAdaptiveProbeVolumeMarker.Auto()"));
            Assert.That(frameContextSource, Does.Contain("PrepareFrameContextSubsystemPreRenderMarker.Auto()"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var path = Path.Combine("Packages", "VividRP");
            foreach (var part in relativeParts)
                path = Path.Combine(path, part);

            return path;
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
    }
}
