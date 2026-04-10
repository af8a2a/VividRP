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
    public class AerialPerspectivePassTests
    {
        [Test]
        public void Initialize_RegistersColorDepthAndLutInputsPlusOutput()
        {
            IRenderPass renderPass = new AerialPerspectivePass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "AtmosphericScatteringLUT",
                "CameraDepth",
                "Color",
                "OutputColor"
            }));
            Assert.That(textureEntries.Single(entry => entry.Name == "OutputColor").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(
                textureEntries.Where(entry => entry.Name != "OutputColor").Select(entry => entry.Access).Distinct(),
                Is.EqualTo(new[] { AccessFlags.Read }));
        }

        [Test]
        public void Prepare_ConfiguresOutputToCameraDimensions_WhenSourceUsesPlaceholderSize()
        {
            var pass = new AerialPerspectivePass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 800;
            cameraData.actualHeight = 450;

            pass.Prepare(frameData);

            var outputTexture = GetFieldValue<RenderGraphTexture>(pass, "m_OutputTexture");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(800));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(450));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void VividRPCoreResources_DeclaresAerialPerspectiveShader()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.AerialPerspectiveShader));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/AtmosphericScattering/OpaqueAtmosphericScattering"));
        }

        [Test]
        public void Source_UsesShaderFallbackAndAtmosphericScatteringLutBindings()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "AerialPerspectivePass.cs"));

            Assert.That(source, Does.Contain("internal const string AerialPerspectiveShaderName = \"Hidden/VividRP/AerialPerspective\";"));
            Assert.That(source, Does.Contain("shader ??= Shader.Find(AerialPerspectiveShaderName);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(frameData, out m_MaterialParameters)"));
            Assert.That(source, Does.Contain("m_Parameters.skyFogParams.x > 0.5f"));
            Assert.That(source, Does.Contain("m_AtmosphericScatteringLUT = RenderGraphTexture.CreateInput(\"AtmosphericScatteringLUT\", GraphicsFormat.R16G16B16A16_SFloat);"));
            Assert.That(source, Does.Contain("texture.desc.Dimension = TextureDimension.Tex3D;"));
            Assert.That(source, Does.Contain("var hasValidAtmosphericScatteringLut = HasValidAtmosphericScatteringLut(atmosphericScatteringLut);"));
            Assert.That(source, Does.Contain("mpb.SetTexture("));
            Assert.That(source, Does.Contain("AtmosphericScatteringLutId,"));
            Assert.That(source, Does.Contain("hasValidAtmosphericScatteringLut ? atmosphericScatteringLut : m_FallbackAtmosphericScatteringLut);"));
            Assert.That(source, Does.Contain("mpb.SetMatrix(PixelCoordToViewDirWSId, m_IsActive ? m_Parameters.pixelCoordToViewDirWS : Matrix4x4.identity);"));
            Assert.That(source, Does.Contain("mpb.SetVector(SkyFogParamsId, fogParams);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyMaterialPropertyBinder.Apply(mpb, m_MaterialParameters, VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume());"));
            Assert.That(source, Does.Contain("texture.dimension != TextureDimension.Tex3D"));
            Assert.That(source, Does.Not.Contain("SetBuffer("));
            Assert.That(source, Does.Not.Contain("VividExposureData"));
            Assert.That(source, Does.Contain("CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);"));
        }

        [Test]
        public void Shader_Source_UsesAtmosphericScatteringLut()
        {
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphericScattering", "OpaqueAtmosphericScattering.shader"));
            var hlslSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphericScattering", "AtmosphericScattering.hlsl"));

            Assert.That(shaderSource, Does.Contain("Shader \"Hidden/VividRP/AerialPerspective\""));
            Assert.That(shaderSource, Does.Contain("Name \"OpaqueAtmosphericScattering\""));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/AtmosphericScattering/AtmosphericScattering.hlsl\""));
            Assert.That(hlslSource, Does.Contain("TEXTURE2D_X(_InputColor);"));
            Assert.That(hlslSource, Does.Contain("TEXTURE2D_X_FLOAT(_DepthTexture);"));
            Assert.That(hlslSource, Does.Contain("EvaluateCameraAtmosphericScattering(viewDirectionWS, positionNDC, tFrag, fogColor, fogOpacity);"));
            Assert.That(hlslSource, Does.Contain("ComputeWorldSpacePosition(positionNDC, deviceDepth, UNITY_MATRIX_I_VP);"));
            Assert.That(hlslSource, Does.Contain("float3 SanitizeSkyRadiance(float3 color)"));
            Assert.That(hlslSource, Does.Contain("if (_SkyFogParams.x <= 0.5f)"));
            Assert.That(hlslSource, Does.Contain("if (_SkyFogParams.w > 0.0f)"));
            Assert.That(hlslSource, Does.Contain("float3 composedColor = fogColor + (1.0f - fogOpacity) * inputColor.rgb;"));
            Assert.That(hlslSource, Does.Not.Contain("_TransmittanceLUT"));
            Assert.That(hlslSource, Does.Not.Contain("_MultiScatteringLUT"));
        }

        private static T GetFieldValue<T>(AerialPerspectivePass pass, string fieldName)
        {
            var field = typeof(AerialPerspectivePass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

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
