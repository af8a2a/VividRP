using System.Collections.Generic;
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
    public class SliderDebugPassTests
    {
        [Test]
        public void Initialize_RegistersTwoTextureInputsAndOneColorOutput()
        {
            IRenderPass renderPass = new SliderDebugPass();

            var resources = renderPass.Initialize();
            var leftEntry = resources.Textures.Single(entry => entry.Name == "LeftTexture");
            var rightEntry = resources.Textures.Single(entry => entry.Name == "RightTexture");
            var outputEntry = resources.Textures.Single(entry => entry.Name == "OutputTexture");

            Assert.That(resources.Textures, Has.Length.EqualTo(3));
            Assert.That(leftEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(rightEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(outputEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
        }

        [Test]
        public void Prepare_UsesLargestExplicitInputSize_WhenInputsHaveConfiguredDimensions()
        {
            var pass = new SliderDebugPass();
            var leftTexture = GetTextureField(pass, "m_LeftTexture");
            var rightTexture = GetTextureField(pass, "m_RightTexture");
            var outputTexture = GetTextureField(pass, "m_OutputTexture");

            leftTexture.desc.Width = 640;
            leftTexture.desc.Height = 360;
            leftTexture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;

            rightTexture.desc.Width = 1280;
            rightTexture.desc.Height = 720;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(720));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void Prepare_FallsBackToCameraSize_WhenInputTexturesUsePlaceholderDescriptors()
        {
            var pass = new SliderDebugPass();
            var outputTexture = GetTextureField(pass, "m_OutputTexture");

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 960;
            cameraData.actualHeight = 540;

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(960));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(540));
        }

        [Test]
        public void ApplyFloatParameters_ClampsSliderIntoConfiguredRange()
        {
            var pass = new SliderDebugPass();

            RenderGraphPassFloatParameterUtility.ApplyFloatParameters(
                pass,
                typeof(SliderDebugPass),
                new List<RenderGraphPassFloatParameter>
                {
                    new()
                    {
                        FieldName = "m_Slider",
                        Value = 150f,
                    }
                });

            Assert.That(GetFloatField(pass, "m_Slider"), Is.EqualTo(100f));
        }

        private static RenderGraphTexture GetTextureField(SliderDebugPass pass, string fieldName)
        {
            var field = typeof(SliderDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            return texture;
        }

        private static float GetFloatField(SliderDebugPass pass, string fieldName)
        {
            var field = typeof(SliderDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (float)field.GetValue(pass);
        }
    }
}
