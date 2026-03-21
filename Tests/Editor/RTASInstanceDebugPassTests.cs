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
    public sealed class RTASInstanceDebugPassTests
    {
        [Test]
        public void Initialize_RegistersAccelerationStructureInput_AndOutputTexture()
        {
            IRenderPass renderPass = new RTASInstanceDebugPass();

            var resources = renderPass.Initialize();
            var accelerationStructureEntry = resources.AccelerationStructures.Single();
            var outputEntry = resources.Textures.Single(entry => entry.Name == "OutputTexture");

            Assert.That(resources.AccelerationStructures, Has.Length.EqualTo(1));
            Assert.That(accelerationStructureEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures, Has.Length.EqualTo(1));
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.Texture.desc.EnableRandomWrite, Is.True);
            Assert.That(outputEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void ApplyEnumParameters_UpdatesVisualizationMode()
        {
            var pass = new RTASInstanceDebugPass();

            RenderGraphPassEnumParameterUtility.ApplyEnumParameters(
                pass,
                typeof(RTASInstanceDebugPass),
                new List<RenderGraphPassEnumParameter>
                {
                    new()
                    {
                        FieldName = "m_VisualizationMode",
                        Value = (int)RTASInstanceDebugVisualizationMode.PrimitiveIndex,
                    }
                });

            Assert.That(pass.VisualizationMode, Is.EqualTo(RTASInstanceDebugVisualizationMode.PrimitiveIndex));
        }

        [Test]
        public void Prepare_UsesCameraSizeAndKeepsRandomWriteEnabled()
        {
            var pass = new RTASInstanceDebugPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();

            cameraData.actualWidth = 1280;
            cameraData.actualHeight = 720;

            pass.Prepare(frameData);

            var outputTexture = GetTextureField(pass, "m_OutputTexture");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(720));
            Assert.That(outputTexture.desc.EnableRandomWrite, Is.True);
            Assert.That(outputTexture.desc.FilterMode, Is.EqualTo(FilterMode.Point));
        }

        private static RenderGraphTexture GetTextureField(RTASInstanceDebugPass pass, string fieldName)
        {
            var field = typeof(RTASInstanceDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            return texture;
        }
    }
}
