using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReflectionProbeAtlasDebugPassTests
    {
        [TearDown]
        public void TearDown()
        {
            VividRenderingDebugDisplaySettings.Data.Reset();
        }

        [Test]
        public void Initialize_RegistersDebugTextureOutput()
        {
            IRenderPass renderPass = new ReflectionProbeAtlasDebugPass();

            var resources = renderPass.Initialize();
            var debugTextureEntry = resources.Textures.Single(entry => entry.Name == "DebugTexture");

            Assert.That(resources.Textures, Has.Length.EqualTo(1));
            Assert.That(debugTextureEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(debugTextureEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(debugTextureEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void IsActive_TracksRenderingDebuggerMode()
        {
            var pass = new ReflectionProbeAtlasDebugPass();

            VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasDebugMode = ReflectionProbeAtlasDebugMode.None;
            Assert.That(pass.IsActive(new ContextContainer()), Is.False);

            VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasDebugMode = ReflectionProbeAtlasDebugMode.Atlas;
            Assert.That(pass.IsActive(new ContextContainer()), Is.True);

            VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasDebugMode = ReflectionProbeAtlasDebugMode.Slot;
            Assert.That(pass.IsActive(new ContextContainer()), Is.True);
        }

        [Test]
        public void ResolveSettings_UsesRenderingDebuggerValues()
        {
            var data = new VividRenderingDebugSettingsData
            {
                reflectionProbeAtlasDebugMode = ReflectionProbeAtlasDebugMode.Atlas,
                reflectionProbeAtlasArraySlice = 2,
                reflectionProbeAtlasMipLevel = 3,
                reflectionProbeAtlasExposure = 2.5f,
            };

            var settings = ReflectionProbeAtlasDebugPass.ResolveSettings(
                data,
                ReflectionProbeAtlasDebugMode.None,
                0,
                0,
                0f);

            Assert.That(settings.mode, Is.EqualTo(ReflectionProbeAtlasDebugMode.Atlas));
            Assert.That(settings.arraySlice, Is.EqualTo(2));
            Assert.That(settings.mipLevel, Is.EqualTo(3));
            Assert.That(settings.exposure, Is.EqualTo(2.5f));
        }

        [Test]
        public void ResolveSettings_ClampsIndicesAndExposure()
        {
            var settings = ReflectionProbeAtlasDebugPass.ResolveSettings(
                new VividRenderingDebugSettingsData
                {
                    reflectionProbeAtlasDebugMode = ReflectionProbeAtlasDebugMode.Atlas,
                    reflectionProbeAtlasArraySlice = -5,
                    reflectionProbeAtlasMipLevel = -2,
                    reflectionProbeAtlasExposure = 32f,
                },
                ReflectionProbeAtlasDebugMode.None,
                0,
                0,
                0f);

            Assert.That(settings.arraySlice, Is.Zero);
            Assert.That(settings.mipLevel, Is.Zero);
            Assert.That(settings.exposure, Is.EqualTo(16f));
        }

        [Test]
        public void ResolveSettings_UsesPassDefaults_WhenDebuggerDataIsMissing()
        {
            var settings = ReflectionProbeAtlasDebugPass.ResolveSettings(
                null,
                ReflectionProbeAtlasDebugMode.Atlas,
                4,
                5,
                -2f);

            Assert.That(settings.mode, Is.EqualTo(ReflectionProbeAtlasDebugMode.Atlas));
            Assert.That(settings.arraySlice, Is.EqualTo(4));
            Assert.That(settings.mipLevel, Is.EqualTo(5));
            Assert.That(settings.exposure, Is.EqualTo(-2f));
        }

        [Test]
        public void ResolveIndex_ClampsToAvailableCount()
        {
            Assert.That(ReflectionProbeAtlasDebugPass.ResolveIndex(4, 2), Is.EqualTo(1));
            Assert.That(ReflectionProbeAtlasDebugPass.ResolveIndex(-1, 2), Is.Zero);
            Assert.That(ReflectionProbeAtlasDebugPass.ResolveIndex(1, 0), Is.Zero);
        }

        [Test]
        public void Prepare_UsesCameraSizeAndFp16Output()
        {
            var pass = new ReflectionProbeAtlasDebugPass();
            var debugTexture = GetTextureField(pass, "m_DebugTexture");
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1280;
            cameraData.actualHeight = 720;

            VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasDebugMode = ReflectionProbeAtlasDebugMode.Atlas;
            pass.Prepare(frameData);

            Assert.That(debugTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(debugTexture.desc.Height, Is.EqualTo(720));
            Assert.That(debugTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(debugTexture.desc.FilterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(debugTexture.desc.ClearBuffer, Is.True);
        }

        [Test]
        public void ApplyEnumParameters_UpdatesMode()
        {
            var pass = new ReflectionProbeAtlasDebugPass();

            RenderGraphPassEnumParameterUtility.ApplyEnumParameters(
                pass,
                typeof(ReflectionProbeAtlasDebugPass),
                new List<RenderGraphPassEnumParameter>
                {
                    new()
                    {
                        FieldName = "m_Mode",
                        Value = (int)ReflectionProbeAtlasDebugMode.Slot,
                    }
                });

            Assert.That(pass.Mode, Is.EqualTo(ReflectionProbeAtlasDebugMode.Slot));
        }

        [Test]
        public void ApplyFloatParameters_UpdatesSliceMipAndExposure()
        {
            var pass = new ReflectionProbeAtlasDebugPass();

            RenderGraphPassFloatParameterUtility.ApplyFloatParameters(
                pass,
                typeof(ReflectionProbeAtlasDebugPass),
                new List<RenderGraphPassFloatParameter>
                {
                    new() { FieldName = "m_ArraySlice", Value = 3f },
                    new() { FieldName = "m_MipLevel", Value = 2f },
                    new() { FieldName = "m_Exposure", Value = 32f },
                });

            Assert.That(pass.ArraySlice, Is.EqualTo(3));
            Assert.That(pass.MipLevel, Is.EqualTo(2));
            Assert.That(pass.Exposure, Is.EqualTo(16f));
        }

        private static RenderGraphTexture GetTextureField(ReflectionProbeAtlasDebugPass pass, string fieldName)
        {
            var field = typeof(ReflectionProbeAtlasDebugPass).GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }
    }
}
