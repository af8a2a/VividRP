using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class DebugPassCameraFilterTests
    {
        [TestCase(CameraType.Game, false)]
        [TestCase(CameraType.SceneView, false)]
        [TestCase(CameraType.Preview, true)]
        [TestCase(CameraType.Reflection, true)]
        public void ShouldSkipExecution_ReturnsExpectedValue_ForCameraType(CameraType cameraType, bool expected)
        {
            var utilityType = typeof(ClusterDebugPass).Assembly.GetType("VividRP.Runtime.RenderPass.Core.DebugPassCameraUtility");
            var method = utilityType?.GetMethod(
                "ShouldSkipExecution",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(CameraType) },
                null);

            Assert.That(method, Is.Not.Null);
            Assert.That((bool)method.Invoke(null, new object[] { cameraType }), Is.EqualTo(expected));
        }

        [Test]
        public void ExposureDebugPass_Prepare_SetsSkipExecution_ForPreviewCamera()
        {
            PreviewRenderUtility preview = new();
            var pass = new ExposureDebugPass();

            try
            {
                var frameData = CreateFrameData(preview.camera);

                pass.Prepare(frameData);

                Assert.That(GetSkipExecutionField(pass), Is.True);
            }
            finally
            {
                preview.Cleanup();
            }
        }

        [Test]
        public void RTASInstanceDebugPass_Prepare_SetsSkipExecution_ForPreviewCamera()
        {
            PreviewRenderUtility preview = new();
            var pass = new RTASInstanceDebugPass();

            try
            {
                var frameData = CreateFrameData(preview.camera);

                pass.Prepare(frameData);

                Assert.That(GetSkipExecutionField(pass), Is.True);
            }
            finally
            {
                preview.Cleanup();
            }
        }

        [Test]
        public void VirtualTextureVisualizationPass_Prepare_SetsSkipExecution_ForPreviewCamera()
        {
            PreviewRenderUtility preview = new();
            var pass = new VirtualTextureVisualizationPass();

            try
            {
                var frameData = CreateFrameData(preview.camera);

                pass.Prepare(frameData);

                Assert.That(GetSkipExecutionField(pass), Is.True);
            }
            finally
            {
                preview.Cleanup();
            }
        }

        private static ContextContainer CreateFrameData(Camera camera)
        {
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.camera = camera;
            cameraData.actualWidth = 256;
            cameraData.actualHeight = 144;
            cameraData.pixelWidth = 256;
            cameraData.pixelHeight = 144;
            cameraData.pixelRect = new Rect(0f, 0f, 256f, 144f);
            return frameData;
        }

        private static bool GetSkipExecutionField(object pass)
        {
            var field = pass.GetType().GetField("m_ShouldSkipExecution", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (bool)field.GetValue(pass);
        }
    }
}
