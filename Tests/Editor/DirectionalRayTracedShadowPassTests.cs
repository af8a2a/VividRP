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
    public sealed class DirectionalRayTracedShadowPassTests
    {
        [Test]
        public void Initialize_RegistersExpectedInputsAndOutput()
        {
            IRenderPass renderPass = new DirectionalRayTracedShadowPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(resources.AccelerationStructures, Has.Length.EqualTo(1));
            Assert.That(resources.AccelerationStructures[0].Name, Is.EqualTo("SceneRTAS"));
            Assert.That(resources.AccelerationStructures[0].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "Depth",
                "DirectionalShadowTexture",
                "GBuffer1"
            }));
            Assert.That(textureEntries.Single(entry => entry.Name == "Depth").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "GBuffer1").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "DirectionalShadowTexture").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(
                textureEntries.Single(entry => entry.Name == "DirectionalShadowTexture").Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16_SFloat));
        }

        [Test]
        public void Prepare_UsesCameraSizeAndKeepsWhiteOutputConfiguration()
        {
            var pass = new DirectionalRayTracedShadowPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 960;
            cameraData.actualHeight = 540;

            pass.Prepare(frameData);

            var outputTexture = GetTextureField(pass, "m_DirectionalShadowTexture");

            Assert.That(outputTexture.desc.Width, Is.EqualTo(960));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(540));
            Assert.That(outputTexture.desc.EnableRandomWrite, Is.True);
            Assert.That(outputTexture.desc.ClearColor, Is.EqualTo(Color.white));
            Assert.That(outputTexture.desc.FilterMode, Is.EqualTo(FilterMode.Point));
        }

        [Test]
        public void ResolveShadowRequest_ReturnsConfiguredDirectionalLight_WhenMainLightEnablesRayTracedShadow()
        {
            var lightObject = new GameObject("Directional Shadow Test Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Hard;
            lightObject.transform.forward = Vector3.forward;

            try
            {
                var additionalData = light.GetVividAdditionalLightData();
                additionalData.enableRayTracedShadow = true;
                additionalData.rayTracedShadowRayLength = 123f;
                additionalData.rayTracedShadowRayBias = 0.02f;
                additionalData.rayTracedShadowDistantRayBias = 0.09f;

                var lightData = new VividLightData();
                lightData.UpdateDirectionalLights(new[] { light }, light);

                var request = DirectionalRayTracedShadowPass.ResolveShadowRequest(lightData, true, true);

                Assert.That(request.ShouldTrace, Is.True);
                Assert.That(request.LightEntityId, Is.EqualTo(light.GetEntityId()));
                Assert.That(request.LightDirectionWS, Is.EqualTo(-lightObject.transform.forward));
                Assert.That(request.RayLength, Is.EqualTo(123f));
                Assert.That(request.RayBias, Is.EqualTo(0.02f));
                Assert.That(request.DistantRayBias, Is.EqualTo(0.09f));
            }
            finally
            {
                Object.DestroyImmediate(lightObject);
            }
        }

        [Test]
        public void ResolveShadowRequest_ReturnsDefault_WhenMainDirectionalLightIsMissing()
        {
            var request = DirectionalRayTracedShadowPass.ResolveShadowRequest(new VividLightData(), true, true);

            Assert.That(request.ShouldTrace, Is.False);
            Assert.That(request.LightEntityId, Is.EqualTo(EntityId.None));
        }

        [Test]
        public void ResolveShadowRequest_ReturnsDefault_WhenLightComponentIsDisabled()
        {
            var lightObject = new GameObject("Disabled Directional Shadow Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Hard;

            try
            {
                var additionalData = light.GetVividAdditionalLightData();
                additionalData.enableRayTracedShadow = true;
                additionalData.enabled = false;

                var lightData = new VividLightData();
                lightData.UpdateDirectionalLights(new[] { light }, light);

                var request = DirectionalRayTracedShadowPass.ResolveShadowRequest(lightData, true, true);

                Assert.That(request.ShouldTrace, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(lightObject);
            }
        }

        [Test]
        public void ResolveShadowRequest_ReturnsDefault_WhenRayTracingSupportOrRtasIsUnavailable()
        {
            var lightObject = new GameObject("Unavailable Directional Shadow Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Hard;

            try
            {
                var additionalData = light.GetVividAdditionalLightData();
                additionalData.enableRayTracedShadow = true;

                var lightData = new VividLightData();
                lightData.UpdateDirectionalLights(new[] { light }, light);

                var unsupportedRequest = DirectionalRayTracedShadowPass.ResolveShadowRequest(lightData, false, true);
                var missingRtasRequest = DirectionalRayTracedShadowPass.ResolveShadowRequest(lightData, true, false);

                Assert.That(unsupportedRequest.ShouldTrace, Is.False);
                Assert.That(missingRtasRequest.ShouldTrace, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(lightObject);
            }
        }

        private static RenderGraphTexture GetTextureField(DirectionalRayTracedShadowPass pass, string fieldName)
        {
            var field = typeof(DirectionalRayTracedShadowPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            return texture;
        }
    }
}
