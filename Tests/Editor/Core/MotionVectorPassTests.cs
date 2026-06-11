using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class MotionVectorPassTests
    {
        [Test]
        public void Initialize_RegistersDepthInputAndMotionVectorOutputs_WhenPassIsCreated()
        {
            IRenderPass renderPass = new MotionVectorPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();
            var renderListEntries = resources.RenderLists.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "CameraDepth",
                "MotionVectorDepth",
                "MotionVectors",
            }));
            Assert.That(renderListEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "FallbackRenderList", "RenderList" }));
        }

        [Test]
        public void Prepare_ConfiguresMotionVectorTargetsFromDepthInput_WhenSourceDescriptorIsExplicit()
        {
            var pass = new MotionVectorPass();
            var frameData = new ContextContainer();

            var sourceTexture = GetTextureField(pass, "m_CameraDepthTexture");
            sourceTexture.desc.Width = 320;
            sourceTexture.desc.Height = 180;
            sourceTexture.desc.Slices = 2;
            sourceTexture.desc.Dimension = TextureDimension.Tex2DArray;
            sourceTexture.desc.DepthBufferBits = DepthBits.Depth24;
            sourceTexture.desc.UseDynamicScale = true;
            sourceTexture.desc.UseDynamicScaleExplicit = true;
            sourceTexture.desc.ScaleFactor = new Vector2(0.5f, 0.5f);

            pass.Prepare(frameData);

            var motionVectorTexture = GetTextureField(pass, "m_MotionVectorTexture");
            var motionVectorDepthTexture = GetTextureField(pass, "m_MotionVectorDepthTexture");

            Assert.That(motionVectorTexture.desc.Width, Is.EqualTo(320));
            Assert.That(motionVectorTexture.desc.Height, Is.EqualTo(180));
            Assert.That(motionVectorTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16_SFloat));
            Assert.That(motionVectorTexture.desc.Dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(motionVectorTexture.desc.Slices, Is.EqualTo(2));
            Assert.That(motionVectorTexture.desc.UseDynamicScale, Is.True);
            Assert.That(motionVectorTexture.desc.UseDynamicScaleExplicit, Is.True);
            Assert.That(motionVectorTexture.desc.ScaleFactor, Is.EqualTo(new Vector2(0.5f, 0.5f)));

            Assert.That(motionVectorDepthTexture.desc.Width, Is.EqualTo(320));
            Assert.That(motionVectorDepthTexture.desc.Height, Is.EqualTo(180));
            Assert.That(motionVectorDepthTexture.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth24));
            Assert.That(motionVectorDepthTexture.desc.Dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(motionVectorDepthTexture.desc.Slices, Is.EqualTo(2));
        }

        [Test]
        public void Prepare_DoesNotMutateCameraMotionVectorDepthFlags_WhenCameraIsAvailable()
        {
            var pass = new MotionVectorPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var gameObject = new GameObject("MotionVectorPassCamera");

            try
            {
                var camera = gameObject.AddComponent<Camera>();
                cameraData.camera = camera;
                cameraData.actualWidth = 640;
                cameraData.actualHeight = 360;
                camera.depthTextureMode = DepthTextureMode.None;

                pass.Prepare(frameData);

                Assert.That(camera.depthTextureMode, Is.EqualTo(DepthTextureMode.None));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Constructor_ConfiguresDefaultRenderListForObjectMotionVectors()
        {
            var pass = new MotionVectorPass();
            var renderList = GetRenderListField(pass, "m_RenderList");
            var fallbackRenderList = GetRenderListField(pass, "m_FallbackRenderList");

            Assert.That(renderList.desc.RenderQueueRange, Is.EqualTo(RenderGraphRenderQueueRange.Opaque));
            Assert.That(renderList.desc.SortingCriteria, Is.EqualTo(SortingCriteria.CommonOpaque));
            Assert.That(renderList.desc.RendererConfiguration, Is.EqualTo(PerObjectData.MotionVectors));
            Assert.That(renderList.desc.ShaderTagNames, Is.EqualTo(new[] { MotionVectorPass.MotionVectorsShaderTagName }));

            Assert.That(fallbackRenderList.desc.RenderQueueRange, Is.EqualTo(RenderGraphRenderQueueRange.Opaque));
            Assert.That(fallbackRenderList.desc.SortingCriteria, Is.EqualTo(SortingCriteria.CommonOpaque));
            Assert.That(fallbackRenderList.desc.RendererConfiguration, Is.EqualTo(PerObjectData.MotionVectors));
            Assert.That(fallbackRenderList.desc.ExcludeObjectMotionVectors, Is.True);
            Assert.That(fallbackRenderList.desc.ShaderTagNames, Is.EqualTo(new[]
            {
                "VividGBuffer",
                RenderGraphRenderListDesc.ForwardShaderTagName,
                RenderGraphRenderListDesc.DefaultUnlitShaderTagName,
            }));
        }

        [Test]
        public void Prepare_AssignsFallbackOverrideShader_WhenResourcesAreAvailable()
        {
            var pass = new MotionVectorPass();
            var frameData = new ContextContainer();

            pass.Create();
            pass.Prepare(frameData);

            var renderList = GetRenderListField(pass, "m_RenderList");
            var fallbackRenderList = GetRenderListField(pass, "m_FallbackRenderList");

            Assert.That(fallbackRenderList.desc.OverrideShader, Is.Not.Null);
            Assert.That(fallbackRenderList.desc.ExcludeObjectMotionVectors, Is.True);
            Assert.That(renderList.desc.ShaderTagNames, Is.EqualTo(new[]
            {
                MotionVectorPass.MotionVectorsShaderTagName,
            }));
        }

        [Test]
        public void Prepare_RestoresFallbackObjectMotionExclusion_WhenSerializedDescHasOldValue()
        {
            var pass = new MotionVectorPass();
            var frameData = new ContextContainer();
            var fallbackRenderList = GetRenderListField(pass, "m_FallbackRenderList");
            fallbackRenderList.desc.ExcludeObjectMotionVectors = false;

            pass.Prepare(frameData);

            Assert.That(fallbackRenderList.desc.ExcludeObjectMotionVectors, Is.True);
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsFalse_ForMotionVectorPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(MotionVectorPass)), Is.False);
        }

        private static RenderGraphTexture GetTextureField(MotionVectorPass pass, string fieldName)
        {
            var field = typeof(MotionVectorPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static RenderGraphRenderList GetRenderListField(MotionVectorPass pass, string fieldName)
        {
            var field = typeof(MotionVectorPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            return (RenderGraphRenderList)field.GetValue(pass);
        }
    }
}
