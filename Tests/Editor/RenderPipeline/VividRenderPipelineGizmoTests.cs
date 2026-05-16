using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VividRenderPipelineGizmoTests
    {
        [TestCase(CameraType.SceneView, false)]
        [TestCase(CameraType.Game, false)]
        [TestCase(CameraType.Preview, false)]
        [TestCase(CameraType.Reflection, false)]
        public void ShouldEmitWorldGeometry_ReturnsExpectedValue_ForCameraType(CameraType cameraType, bool expected)
        {
            Assert.That(VividRenderPipeline.ShouldEmitWorldGeometry(cameraType), Is.EqualTo(expected));
        }

        [TestCase(CameraType.SceneView, true)]
        [TestCase(CameraType.Game, true)]
        [TestCase(CameraType.Preview, false)]
        [TestCase(CameraType.Reflection, false)]
        public void CanRenderGizmos_ReturnsExpectedValue_ForCameraType(CameraType cameraType, bool expected)
        {
            Assert.That(VividRenderPipeline.CanRenderGizmos(cameraType), Is.EqualTo(expected));
        }

        [TestCase(CameraType.SceneView, false)]
        [TestCase(CameraType.Game, true)]
        [TestCase(CameraType.Preview, false)]
        [TestCase(CameraType.Reflection, false)]
        public void ShouldRenderPreImageEffectGizmosInRenderGraph_ReturnsExpectedValue_ForCameraType(
            CameraType cameraType,
            bool expected)
        {
            Assert.That(VividRenderPipeline.ShouldRenderPreImageEffectGizmosInRenderGraph(cameraType), Is.EqualTo(expected));
        }

        [Test]
        public void HasRenderGizmoPrePostProcessBoundary_ReturnsTrue_WhenBoundaryPassExists()
        {
            IReadOnlyList<IRenderPass> renderPasses = new IRenderPass[]
            {
                new FullScreenPass(),
                new FinalBlitPass(),
            };

            Assert.That(PassRecorder.HasRenderGizmoPrePostProcessBoundary(renderPasses), Is.True);
        }

        [Test]
        public void HasRenderGizmoPrePostProcessBoundary_ReturnsFalse_WhenBoundaryPassDoesNotExist()
        {
            IReadOnlyList<IRenderPass> renderPasses = new IRenderPass[]
            {
                new FullScreenPass(),
            };

            Assert.That(PassRecorder.HasRenderGizmoPrePostProcessBoundary(renderPasses), Is.False);
        }

        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        public void ShouldRenderPreImageEffectGizmosOutsideRenderGraph_ReturnsExpectedValue(
            bool hasBoundary,
            bool renderedInGraph,
            bool expected)
        {
            Assert.That(
                PassRecorder.ShouldRenderPreImageEffectGizmosOutsideRenderGraph(hasBoundary, renderedInGraph),
                Is.EqualTo(expected));
        }
    }
}
