using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using ResourcePathAttribute = VividRP.Runtime.ResourcePathAttribute;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyPassTests
    {
        [Test]
        public void Initialize_RegistersDepthInputColorOutputAndSkyViewLut_WhenPassIsCreated()
        {
            IRenderPass renderPass = new PhysicallyBasedSkyPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "Color", "Depth", "SkyViewLUT" }));

            var colorEntry = textureEntries.Single(entry => entry.Name == "Color");
            var depthEntry = textureEntries.Single(entry => entry.Name == "Depth");
            var skyViewEntry = textureEntries.Single(entry => entry.Name == "SkyViewLUT");

            Assert.That(colorEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(colorEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(colorEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));

            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(depthEntry.AttachmentIndex, Is.EqualTo(-1));
            Assert.That(depthEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(depthEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.None));

            Assert.That(skyViewEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(skyViewEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void Prepare_ResizesTexturesAndKeepsPassInactive_WhenSkyTypeIsNotPhysicallyBased()
        {
            var pass = new PhysicallyBasedSkyPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var skyData = frameData.GetOrCreate<VividSkyData>();

            cameraData.actualWidth = 640;
            cameraData.actualHeight = 360;
            skyData.activeSkyType = SkyType.HDRI;

            pass.Prepare(frameData);

            AssertTextureSize(pass, "m_ColorTarget", 640, 360);
            AssertTextureSize(pass, "m_DepthTexture", 640, 360);
            Assert.That(GetFieldValue<bool>(pass, "m_IsActive"), Is.False);
        }

        [Test]
        public void VividRPCoreResources_DeclaresPhysicallyBasedSkyShader()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.PhysicallyBasedSkyShader));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/PhysicallyBasedSky"));
        }

        [Test]
        public void Source_UsesShaderFallbackAndBindsSkyViewLut()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PhysicallyBasedSkyPass.cs"));

            Assert.That(source, Does.Contain("internal const string PhysicallyBasedSkyShaderName = \"Hidden/VividRP/PhysicallyBasedSky\";"));
            Assert.That(source, Does.Contain("shader ??= Shader.Find(PhysicallyBasedSkyShaderName);"));
            Assert.That(source, Does.Contain("m_SkyViewLUT = RenderGraphTexture.CreateInput(\"SkyViewLUT\", GraphicsFormat.R16G16B16A16_SFloat);"));
            Assert.That(source, Does.Contain("m_IsActive = PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters);"));
            Assert.That(source, Does.Contain("var skyViewTexture = m_SkyViewLUT != null"));
            Assert.That(source, Does.Contain("mpb.SetTexture(SkyViewLutId, skyViewTexture ?? Texture2D.blackTexture);"));
            Assert.That(source, Does.Contain("mpb.SetFloat(SkyUseLutId, skyViewTexture != null ? 1.0f : 0.0f);"));
            Assert.That(source, Does.Contain("mpb.SetVector(SkyPlanetParamsId, m_Parameters.skyPlanetParams);"));
            Assert.That(source, Does.Contain("m_HasMaterialParameters = PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(frameData, out m_MaterialParameters);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyMaterialPropertyBinder.Apply(mpb, m_MaterialParameters, VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume());"));
            Assert.That(source, Does.Not.Contain("SetBuffer("));
            Assert.That(source, Does.Not.Contain("VividExposureData"));
            Assert.That(source, Does.Contain("CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);"));
        }

        [Test]
        public void Shader_Source_UsesSkyViewLutWithRaymarchFallback()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "PhysicallyBasedSky.shader"));
            var bridgeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "PhysicallyBasedSkyBridge.hlsl"));

            Assert.That(source, Does.Contain("Shader \"Hidden/VividRP/PhysicallyBasedSky\""));
            Assert.That(source, Does.Contain("Name \"PhysicallyBasedSky\""));
            Assert.That(source, Does.Contain("Name \"PhysicallyBasedSkyBaking\""));
            Assert.That(source, Does.Contain("ZTest LEqual"));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyBridge.hlsl\""));
            Assert.That(source, Does.Contain("return float4(VividApplyPreExposure(EvaluateSkyColor(input.positionCS.xy)), 1.0f);"));
            Assert.That(source, Does.Contain("float4 FragBaking(Varyings input) : SV_Target"));
            Assert.That(source, Does.Contain("return float4(EvaluateSkyColor(input.positionCS.xy), 1.0f);"));
            Assert.That(bridgeSource, Does.Contain("TEXTURE2D(_SkyViewLUT);"));
            Assert.That(bridgeSource, Does.Contain("float _SkyUseLUT;"));
            Assert.That(bridgeSource, Does.Contain("EncodeSkyViewUv"));
            Assert.That(bridgeSource, Does.Contain("frac(azimuth / (2.0f * PI) + 0.5f)"));
            Assert.That(bridgeSource, Does.Contain("SAMPLE_TEXTURE2D(_SkyViewLUT, sampler_SkyViewLUT"));
            Assert.That(bridgeSource, Does.Contain("VIEW_SAMPLE_COUNT = 12"));
            Assert.That(bridgeSource, Does.Contain("LIGHT_SAMPLE_COUNT = 6"));
            Assert.That(bridgeSource, Does.Contain("GetSkyViewDirWS"));
            Assert.That(bridgeSource, Does.Contain("float3 SanitizeSkyRadiance(float3 color)"));
            Assert.That(bridgeSource, Does.Contain("float3 EvaluateSunDisk(float3 directionWS)"));
            Assert.That(bridgeSource, Does.Contain("skyColor += EvaluateSunDisk(viewDirWS);"));
            Assert.That(bridgeSource, Does.Contain("EvaluateSky"));
            Assert.That(bridgeSource, Does.Contain("_SkyUseLUT > 0.5f"));
            Assert.That(bridgeSource, Does.Contain("TEXTURECUBE(_GroundAlbedoTexture);"));
            Assert.That(bridgeSource, Does.Contain("TEXTURECUBE(_GroundEmissionTexture);"));
            Assert.That(bridgeSource, Does.Contain("TEXTURECUBE(_SpaceEmissionTexture);"));
            Assert.That(bridgeSource, Does.Contain("float4 _GroundAlbedo_PlanetRadius;"));
            Assert.That(bridgeSource, Does.Contain("float4 _HorizonTint;"));
            Assert.That(bridgeSource, Does.Contain("float4 _ZenithTint;"));
            Assert.That(bridgeSource, Does.Contain("void ApplyArtisticOverrides(float3 viewDirection, inout float3 skyColor)"));
            Assert.That(bridgeSource, Does.Contain("SampleGroundEmission"));
            Assert.That(bridgeSource, Does.Contain("SampleSpaceEmission"));
        }

        private static void AssertTextureSize(PhysicallyBasedSkyPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);

            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }

        private static T GetFieldValue<T>(PhysicallyBasedSkyPass pass, string fieldName)
        {
            var field = typeof(PhysicallyBasedSkyPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
