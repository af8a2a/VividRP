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
    public class AtmosphericScatteringPassTests
    {
        [Test]
        public void Initialize_RegistersColorDepthInputsAndOutput()
        {
            IRenderPass renderPass = new AtmosphericScatteringPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
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
            var pass = new AtmosphericScatteringPass();
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
        public void AtmosphericScatteringPass_InheritsFromUnsafePass()
        {
            Assert.That(typeof(UnsafePass).IsAssignableFrom(typeof(AtmosphericScatteringPass)), Is.True);
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
        public void Source_UsesShaderFallbackAndFrameContextAtmosphericScatteringLutHandle()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sky", "AtmosphericScatteringPass.cs"));

            Assert.That(source, Does.Contain("public sealed class AtmosphericScatteringPass : UnsafePass"));
            Assert.That(source, Does.Contain("internal const string OpaqueAtmosphericScatteringPassName = \"Opaque Atmospheric Scattering\";"));
            Assert.That(source, Does.Contain("internal const string OpaqueAtmosphericScatteringShaderName = \"Hidden/VividRP/OpaqueAtmosphericScattering\";"));
            Assert.That(source, Does.Contain("profilingSampler = new ProfilingSampler(OpaqueAtmosphericScatteringPassName);"));
            Assert.That(source, Does.Contain("SkyManager.Initialize();"));
            Assert.That(source, Does.Contain("shader ??= Shader.Find(OpaqueAtmosphericScatteringShaderName);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(frameData, out m_MaterialParameters)"));
            Assert.That(source, Does.Contain("var skyData = frameData?.GetOrCreate<VividSkyData>();"));
            Assert.That(source, Does.Contain("m_AtmosphericScatteringLutHandle = skyData?.atmosphericScatteringLutHandle;"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTextureForPass(this, m_AtmosphericScatteringLutHandle);"));
            Assert.That(source, Does.Contain("m_Parameters.skyFogParams.x > 0.5f"));
            Assert.That(source, Does.Contain("m_OutputTexture = RenderGraphTexture.CreateOutput(\"OutputColor\", GraphicsFormat.R16G16B16A16_SFloat);"));
            Assert.That(source, Does.Contain("var atmosphericScatteringLut = ResolveTexture(m_AtmosphericScatteringLutHandle);"));
            Assert.That(source, Does.Contain("var hasValidAtmosphericScatteringLut = HasValidAtmosphericScatteringLut(atmosphericScatteringLut);"));
            Assert.That(source, Does.Contain("mpb.SetTexture("));
            Assert.That(source, Does.Contain("AtmosphericScatteringLutId,"));
            Assert.That(source, Does.Contain("hasValidAtmosphericScatteringLut ? atmosphericScatteringLut : m_FallbackAtmosphericScatteringLut);"));
            Assert.That(source, Does.Contain("mpb.SetMatrix(PixelCoordToViewDirWSId, m_IsActive ? m_Parameters.pixelCoordToViewDirWS : Matrix4x4.identity);"));
            Assert.That(source, Does.Contain("mpb.SetVector(SkyFogParamsId, fogParams);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyMaterialPropertyBinder.Apply(mpb, m_MaterialParameters, VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume());"));
            Assert.That(source, Does.Contain("texture.dimension != TextureDimension.Tex3D"));
            Assert.That(source, Does.Not.Contain("RenderGraphTexture m_AtmosphericScatteringLUT"));
            Assert.That(source, Does.Not.Contain("SkyManager.ImportAtmosphericScatteringLut("));
            Assert.That(source, Does.Not.Contain("SetBuffer("));
            Assert.That(source, Does.Not.Contain("VividExposureData"));
            Assert.That(source, Does.Contain("var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);"));
            Assert.That(source, Does.Contain("nativeCmd.SetRenderTarget(m_OutputTexture);"));
            Assert.That(source, Does.Contain("CoreUtils.DrawFullScreen(nativeCmd, m_Material, mpb, 0);"));
        }

        [Test]
        public void Shader_Source_UsesAtmosphericScatteringLut()
        {
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphericScattering", "OpaqueAtmosphericScattering.shader"));
            var hlslSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphericScattering", "AtmosphericScattering.hlsl"));

            Assert.That(shaderSource, Does.Contain("Shader \"Hidden/VividRP/OpaqueAtmosphericScattering\""));
            Assert.That(shaderSource, Does.Contain("Name \"OpaqueAtmosphericScattering\""));
            Assert.That(shaderSource, Does.Contain("#define OPAQUE_FOG_PASS"));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/AtmosphericScattering/AtmosphericScattering.hlsl\""));
            Assert.That(hlslSource, Does.Contain("TEXTURE2D_X(_InputColor);"));
            Assert.That(hlslSource, Does.Contain("TEXTURE2D_X_FLOAT(_DepthTexture);"));
            Assert.That(hlslSource, Does.Contain("float4 _SkyFogParams;"));
            Assert.That(hlslSource, Does.Contain("float4 FragOpaqueAtmosphericScattering(Varyings input) : SV_Target"));
            Assert.That(hlslSource, Does.Contain("float2 positionNDC = saturate(input.positionCS.xy * _ScreenSize.zw);"));
            Assert.That(hlslSource, Does.Contain("float3 positionWS = ComputeWorldSpacePosition(positionNDC, deviceDepth, UNITY_MATRIX_I_VP);"));
            Assert.That(hlslSource, Does.Contain("float3 viewDirectionWS = -GetSkyViewDirWS(input.positionCS.xy);"));
            Assert.That(hlslSource, Does.Contain("EvaluateCameraAtmosphericScattering(viewDirectionWS, positionNDC, tFrag, fogColor, fogOpacity);"));
            Assert.That(hlslSource, Does.Contain("float3 SanitizeSkyRadiance(float3 color)"));
            Assert.That(hlslSource, Does.Contain("if (_SkyFogParams.x <= 0.5f)"));
            Assert.That(hlslSource, Does.Contain("if (_SkyFogParams.w > 0.0f)"));
            Assert.That(hlslSource, Does.Contain("float3 composedColor = fogColor + (1.0f - fogOpacity) * inputColor.rgb;"));
            Assert.That(hlslSource, Does.Not.Contain("struct OpaqueAtmosphericScatteringPositionInputs"));
            Assert.That(hlslSource, Does.Not.Contain("bool EvaluateAtmosphericScattering("));
        }

        private static T GetFieldValue<T>(AtmosphericScatteringPass pass, string fieldName)
        {
            var field = typeof(AtmosphericScatteringPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

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
