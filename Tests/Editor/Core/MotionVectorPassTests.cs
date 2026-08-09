using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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
                "CameraDepthStencil",
                "MotionVectors",
            }));
            Assert.That(resources.Textures.Single(entry => entry.Name == "CameraDepth").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "CameraDepth").IsDepthAttachment, Is.False);
            Assert.That(resources.Textures.Single(entry => entry.Name == "CameraDepthStencil").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(resources.Textures.Single(entry => entry.Name == "CameraDepthStencil").IsDepthAttachment, Is.True);
            Assert.That(renderListEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "FallbackRenderList", "RenderList" }));
        }

        [Test]
        public void Prepare_ConfiguresMotionVectorTargetsFromDepthInput_WhenSourceDescriptorIsExplicit()
        {
            var pass = new MotionVectorPass();
            var frameData = new ContextContainer();

            var depthStencilTexture = GetTextureField(pass, "m_CameraDepthStencilTexture");
            depthStencilTexture.desc.Width = 320;
            depthStencilTexture.desc.Height = 180;
            depthStencilTexture.desc.Slices = 2;
            depthStencilTexture.desc.Dimension = TextureDimension.Tex2DArray;
            depthStencilTexture.desc.DepthBufferBits = DepthBits.Depth24;
            depthStencilTexture.desc.UseDynamicScale = true;
            depthStencilTexture.desc.UseDynamicScaleExplicit = true;
            depthStencilTexture.desc.ScaleFactor = new Vector2(0.5f, 0.5f);

            pass.Prepare(frameData);

            var cameraDepthTexture = GetTextureField(pass, "m_CameraDepthTexture");
            var motionVectorTexture = GetTextureField(pass, "m_MotionVectorTexture");

            Assert.That(cameraDepthTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(cameraDepthTexture.desc.DepthBufferBits, Is.EqualTo(DepthBits.None));
            Assert.That(cameraDepthTexture.desc.FilterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(cameraDepthTexture.desc.ClearBuffer, Is.False);

            Assert.That(motionVectorTexture.desc.Width, Is.EqualTo(320));
            Assert.That(motionVectorTexture.desc.Height, Is.EqualTo(180));
            Assert.That(motionVectorTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16_SFloat));
            Assert.That(motionVectorTexture.desc.Dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(motionVectorTexture.desc.Slices, Is.EqualTo(2));
            Assert.That(motionVectorTexture.desc.UseDynamicScale, Is.True);
            Assert.That(motionVectorTexture.desc.UseDynamicScaleExplicit, Is.True);
            Assert.That(motionVectorTexture.desc.ScaleFactor, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(motionVectorTexture.desc.ClearBuffer, Is.False);
            Assert.That(motionVectorTexture.desc.ClearColor, Is.EqualTo(Color.clear));
        }

        [Test]
        public void ObjectMotionVectorStencilBit_UsesHDRPObjectMotionVectorBit()
        {
            Assert.That(MotionVectorPass.ObjectMotionVectorStencilBit, Is.EqualTo(1 << 5));
        }

        [Test]
        public void Prepare_EnablesCameraDepthAndMotionVectorFlags_WhenCameraIsAvailable()
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

                Assert.That((camera.depthTextureMode & DepthTextureMode.Depth) != 0, Is.True);
                Assert.That((camera.depthTextureMode & DepthTextureMode.MotionVectors) != 0, Is.True);
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
        public void Prepare_AssignsFallbackOverrideMaterial_WhenResourcesAreAvailable()
        {
            var pass = new MotionVectorPass();
            var frameData = new ContextContainer();

            pass.Create();
            pass.Prepare(frameData);

            var renderList = GetRenderListField(pass, "m_RenderList");
            var fallbackRenderList = GetRenderListField(pass, "m_FallbackRenderList");

            Assert.That(fallbackRenderList.desc.OverrideMaterial, Is.Not.Null);
            Assert.That(fallbackRenderList.desc.OverrideShader, Is.Null);
            Assert.That(fallbackRenderList.desc.ExcludeObjectMotionVectors, Is.True);
            Assert.That(fallbackRenderList.desc.ShaderTagNames, Is.EqualTo(new[]
            {
                "VividGBuffer",
                RenderGraphRenderListDesc.ForwardShaderTagName,
                RenderGraphRenderListDesc.DefaultUnlitShaderTagName,
            }));
            Assert.That(renderList.desc.ShaderTagNames, Is.EqualTo(new[]
            {
                MotionVectorPass.MotionVectorsShaderTagName,
            }));
        }

        [Test]
        public void Prepare_RestoresSupportedFallbackMaterialConfiguration_WhenSerializedDescHasOldValue()
        {
            var pass = new MotionVectorPass();
            var frameData = new ContextContainer();
            var fallbackRenderList = GetRenderListField(pass, "m_FallbackRenderList");
            fallbackRenderList.desc.ExcludeObjectMotionVectors = false;
            fallbackRenderList.desc.ShaderTagNames = new[] { MotionVectorPass.ObjectMotionVectorFallbackShaderTagName };

            pass.Prepare(frameData);

            Assert.That(fallbackRenderList.desc.ExcludeObjectMotionVectors, Is.True);
            Assert.That(fallbackRenderList.desc.OverrideMaterial, Is.Not.Null);
            Assert.That(fallbackRenderList.desc.OverrideShader, Is.Null);
            Assert.That(fallbackRenderList.desc.ShaderTagNames, Is.EqualTo(new[]
            {
                "VividGBuffer",
                RenderGraphRenderListDesc.ForwardShaderTagName,
                RenderGraphRenderListDesc.DefaultUnlitShaderTagName,
            }));
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsFalse_ForMotionVectorPass()
        {
            Assert.That((typeof(MotionVectorPass)).SupportsAsyncCompute(), Is.False);
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
