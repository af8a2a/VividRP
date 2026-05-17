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
                "OutputColor",
                "SkyTexture",
                "VBufferLighting"
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
        public void Prepare_DoesNotAllocate_WhenSkyDataIsStable()
        {
            var pass = new AtmosphericScatteringPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var skyData = frameData.GetOrCreate<VividSkyData>();
            cameraData.actualWidth = 800;
            cameraData.actualHeight = 450;
            skyData.activeSkyType = SkyType.None;

            pass.Prepare(frameData);

            var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 32; index++)
                pass.Prepare(frameData);

            var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void Initialize_ConfiguresCameraDepthInputForPointSampling()
        {
            var pass = new AtmosphericScatteringPass();
            var depthTexture = GetFieldValue<RenderGraphTexture>(pass, "m_DepthTexture");

            Assert.That(depthTexture.desc.FilterMode, Is.EqualTo(FilterMode.Point));
        }

        [Test]
        public void AtmosphericScatteringPass_InheritsFromRasterPass()
        {
            Assert.That(typeof(RasterPass).IsAssignableFrom(typeof(AtmosphericScatteringPass)), Is.True);
            Assert.That(typeof(IAllowGlobalStateModificationPass).IsAssignableFrom(typeof(AtmosphericScatteringPass)), Is.True);
        }

        [Test]
        public void Source_UsesShaderFallbackAndFrameContextAtmosphericScatteringLutHandle()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Sky", "AtmosphericScatteringPass.cs"));

            Assert.That(source, Does.Contain("public sealed class AtmosphericScatteringPass : RasterPass, IAllowGlobalStateModificationPass"));
            Assert.That(source, Does.Contain("internal const string OpaqueAtmosphericScatteringPassName = \"Opaque Atmospheric Scattering\";"));
            Assert.That(source, Does.Contain("internal const string OpaqueAtmosphericScatteringShaderName = \"Hidden/VividRP/OpaqueAtmosphericScattering\";"));
            Assert.That(source, Does.Contain("private const int HDRISkyShaderPassIndex = 1;"));
            Assert.That(source, Does.Contain("profilingSampler = new ProfilingSampler(OpaqueAtmosphericScatteringPassName);"));
            Assert.That(source, Does.Contain("SkyManager.Initialize();"));
            Assert.That(source, Does.Contain("shader ??= Shader.Find(OpaqueAtmosphericScatteringShaderName);"));
            Assert.That(source, Does.Contain("var skyData = frameData?.GetOrCreate<VividSkyData>();"));
            Assert.That(source, Does.Contain("m_ActiveSkyType = skyData?.activeSkyType ?? SkyType.None;"));
            Assert.That(source, Does.Contain("if (m_ActiveSkyType == SkyType.HDRI)"));
            Assert.That(source, Does.Contain("m_IsActive = TryBuildHDRISkyFogParameters(out m_FogParameters);"));
            Assert.That(source, Does.Contain("m_IsActive = PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters)"));
            Assert.That(source, Does.Contain("m_FogParameters = m_Parameters.skyFogParams;"));
            Assert.That(source, Does.Contain("m_AtmosphericScatteringLutHandle = skyData?.atmosphericScatteringLutHandle;"));
            Assert.That(source, Does.Contain("if (m_ActiveSkyType != SkyType.HDRI && m_AtmosphericScatteringLutHandle != null)"));
            Assert.That(source, Does.Contain("SkyManager.ImportSpecularCubemap(m_SkyTexture, skyData);"));
            Assert.That(source, Does.Contain("m_SkyMaxMipLevel = skyData != null && skyData.activeSkyType != SkyType.None"));
            Assert.That(source, Does.Contain("m_Parameters.skyFogParams.x > 0.5f"));
            Assert.That(source, Does.Contain("m_OutputTexture = RenderGraphTexture.CreateOutput(\"OutputColor\", GraphicsFormat.R16G16B16A16_SFloat);"));
            Assert.That(source, Does.Contain("m_VBufferLighting = VolumetricDensityPass.CreateVBufferTexture(\"VBufferLighting\");"));
            Assert.That(source, Does.Contain("var atmosphericScatteringLut = TextureResolveUtility.ResolveTexture(m_AtmosphericScatteringLutHandle);"));
            Assert.That(source, Does.Contain("var skyTexture = TextureResolveUtility.ResolveTexture(m_SkyTexture);"));
            Assert.That(source, Does.Contain("var vBufferLighting = TextureResolveUtility.ResolveTexture(m_VBufferLighting.innerHandle);"));
            Assert.That(source, Does.Contain("var hasValidAtmosphericScatteringLut = HasValidAtmosphericScatteringLut(atmosphericScatteringLut);"));
            Assert.That(source, Does.Contain("var hasValidSkyTexture = HasValidSkyTexture(skyTexture);"));
            Assert.That(source, Does.Contain("var hasVBufferLighting = HasValidVBuffer(vBufferLighting);"));
            Assert.That(source, Does.Contain("m_DepthTexture.desc.FilterMode = FilterMode.Point;"));
            Assert.That(source, Does.Contain("var hasUsableDepthTexture = HasUsableDepthTexture(depthTexture);"));
            Assert.That(source, Does.Contain("var requiresAtmosphericScatteringLut = m_ActiveSkyType != SkyType.HDRI;"));
            Assert.That(source, Does.Contain("var volumetricData = frameData.GetOrCreate<VividVolumetricData>();"));
            Assert.That(source, Does.Contain("m_VolumetricEnabled = volumetricData.enabled;"));
            Assert.That(source, Does.Contain("m_ShaderVariablesVolumetric = volumetricData.shaderVariables;"));
            Assert.That(source, Does.Contain("mpb.SetTexture("));
            Assert.That(source, Does.Contain("SetDepthTexture(mpb, depthTexture);"));
            Assert.That(source, Does.Contain("properties.SetTexture(DepthTextureId, renderTexture, RenderTextureSubElement.Depth);"));
            Assert.That(source, Does.Contain("AtmosphericScatteringLutId,"));
            Assert.That(source, Does.Contain("hasValidAtmosphericScatteringLut ? atmosphericScatteringLut : m_FallbackAtmosphericScatteringLut);"));
            Assert.That(source, Does.Contain("mpb.SetTexture(SkyTextureId, hasValidSkyTexture ? skyTexture : m_FallbackSkyTexture);"));
            Assert.That(source, Does.Contain("mpb.SetTexture(VBufferLightingId, hasVBufferLighting ? vBufferLighting : m_FallbackVBufferLighting);"));
            Assert.That(source, Does.Contain("mpb.SetFloat(VolumetricEnabledId, m_VolumetricEnabled && hasVBufferLighting ? 1.0f : 0.0f);"));
            Assert.That(source, Does.Contain("mpb.SetFloat(FogColorModeId, m_IsActive && hasValidSkyTexture ? 1.0f : 0.0f);"));
            Assert.That(source, Does.Contain("mpb.SetVector(MipFogParametersId, BuildMipFogParameters(fogParams));"));
            Assert.That(source, Does.Contain("mpb.SetColor(SkyTextureTintId, m_SkyTint);"));
            Assert.That(source, Does.Contain("SkyTextureParamsId,"));
            Assert.That(source, Does.Contain("m_IsActive && m_ActiveSkyType != SkyType.HDRI"));
            Assert.That(source, Does.Contain("mpb.SetVector(SkyFogParamsId, fogParams);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyMaterialPropertyBinder.Apply(mpb, m_MaterialParameters, VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume());"));
            Assert.That(source, Does.Contain("texture.dimension != TextureDimension.Tex3D"));
            Assert.That(source, Does.Contain("texture.dimension != TextureDimension.Cube"));
            Assert.That(source, Does.Contain("private static RenderGraphTexture CreateSkyTexture(string name)"));
            Assert.That(source, Does.Not.Contain("RenderGraphTexture m_AtmosphericScatteringLUT"));
            Assert.That(source, Does.Not.Contain("SkyManager.ImportAtmosphericScatteringLut("));
            Assert.That(source, Does.Not.Contain("SetBuffer("));
            Assert.That(source, Does.Not.Contain("VividExposureData"));
            Assert.That(source, Does.Not.Contain("CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);"));
            Assert.That(source, Does.Contain("ConstantBuffer.PushGlobal(context.cmd, m_ShaderVariablesVolumetric, ShaderVariablesVolumetricId);"));
            Assert.That(source, Does.Not.Contain("nativeCmd.SetRenderTarget(m_OutputTexture);"));
            Assert.That(source, Does.Contain("CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, ResolveShaderPassIndex());"));
            Assert.That(source, Does.Contain("return m_ActiveSkyType == SkyType.HDRI"));
            Assert.That(source, Does.Contain("? HDRISkyShaderPassIndex"));
            Assert.That(source, Does.Contain("private static bool TryBuildHDRISkyFogParameters(out Vector4 fogParameters)"));
            Assert.That(source, Does.Contain("volume == null || !volume.IsHeightFogActive()"));
        }

        [Test]
        public void Shader_Source_UsesAtmosphericScatteringLut()
        {
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphericScattering", "OpaqueAtmosphericScattering.shader"));
            var hlslSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AtmosphericScattering", "AtmosphericScattering.hlsl"));

            Assert.That(shaderSource, Does.Contain("Shader \"Hidden/VividRP/OpaqueAtmosphericScattering\""));
            Assert.That(shaderSource, Does.Contain("Name \"Default\""));
            Assert.That(shaderSource, Does.Contain("Name \"HDRISky\""));
            Assert.That(shaderSource, Does.Contain("#define OPAQUE_FOG_PASS"));
            Assert.That(shaderSource, Does.Contain("#define ATMOSPHERE_NO_AERIAL_PERSPECTIVE"));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/AtmosphericScattering/AtmosphericScattering.hlsl\""));
            Assert.That(hlslSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl\""));
            Assert.That(hlslSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Private/Volumetric/VBuffer.hlsl\""));
            Assert.That(hlslSource, Does.Contain("#include \"AtmosphericScattering.cs.hlsl\""));
            Assert.That(hlslSource, Does.Contain("#include \"ShaderVariablesAtmosphericScattering.hlsl\""));
            Assert.That(hlslSource, Does.Contain("TEXTURE2D_X(_InputColor);"));
            Assert.That(hlslSource, Does.Contain("TEXTURE2D_X_FLOAT(_DepthTexture);"));
            Assert.That(hlslSource, Does.Contain("TEXTURE3D(_VBufferLighting);"));
            Assert.That(hlslSource, Does.Contain("float4 _SkyFogParams;"));
            Assert.That(hlslSource, Does.Contain("float _VolumetricEnabled;"));
            Assert.That(hlslSource, Does.Contain("float3 GetViewForwardDir()"));
            Assert.That(hlslSource, Does.Contain("float3 RotateSkyDirectionAroundYAxis(float3 directionWS, float rotationDegrees)"));
            Assert.That(hlslSource, Does.Contain("float3 SampleSkyTexture(float3 directionWS, float mipLevel)"));
            Assert.That(hlslSource, Does.Contain("return VividApplyPreExposure(skyRadiance * _SkyTextureTint.rgb * _SkyTextureExposure);"));
            Assert.That(hlslSource, Does.Contain("float3 GetFogColor(float3 V, float fragDist)"));
            Assert.That(hlslSource, Does.Contain("if (_FogColorMode == FOGCOLORMODE_SKY_COLOR && HasSkyTexture())"));
            Assert.That(hlslSource, Does.Contain("float ComputeAtmosphericScatteringDistance(float3 V, float linearDepth, bool isSky)"));
            Assert.That(hlslSource, Does.Contain("float ComputeHDRISkyFogOpacity(float tFrag)"));
            Assert.That(hlslSource, Does.Contain("bool TryGetAtmosphericScatteringRay("));
            Assert.That(hlslSource, Does.Contain("float ResolveVBufferLightingDistance(float2 positionNDC, float deviceDepth, bool hasAtmosphericDistance, float atmosphericDistance)"));
            Assert.That(hlslSource, Does.Contain("float4 CompositeVBufferLighting(float4 inputColor, float2 positionNDC, float linearDistance)"));
            Assert.That(hlslSource, Does.Contain("float4 FragOpaqueAtmosphericScattering(Varyings input) : SV_Target"));
            Assert.That(hlslSource, Does.Contain("float4 FragOpaqueAtmosphericScatteringForHDRISky(Varyings input) : SV_Target"));
            Assert.That(hlslSource, Does.Contain("float2 positionSS = input.positionCS.xy;"));
            Assert.That(hlslSource, Does.Contain("bool isSky = IsFarDepth(deviceDepth);"));
            Assert.That(hlslSource, Does.Contain("float2 positionNDC = positionSS * _ScreenSize.zw;"));
            Assert.That(hlslSource, Does.Contain("float3 V = GetSkyViewDirWS(positionSS);"));
            Assert.That(hlslSource, Does.Contain("float linearDepth = LinearEyeDepth(deviceDepth, _ZBufferParams);"));
            Assert.That(hlslSource, Does.Contain("float viewForwardDot = dot(-V, GetViewForwardDir());"));
            Assert.That(hlslSource, Does.Contain("bool hasAtmosphericDistance = _SkyFogParams.x > 0.5f"));
            Assert.That(hlslSource, Does.Contain("float vBufferLightingDistance = ResolveVBufferLightingDistance(positionNDC, deviceDepth, hasAtmosphericDistance, tFrag);"));
            Assert.That(hlslSource, Does.Contain("EvaluateAtmosphericScattering(-V, positionNDC, tFrag, fogColor, fogOpacity);"));
            Assert.That(hlslSource, Does.Contain("bool doBiquadraticReconstruction = _VBufferGaussianFiltering <= 0.5f;"));
            Assert.That(hlslSource, Does.Contain("float4 lighting = SampleVBuffer("));
            Assert.That(hlslSource, Does.Contain("TEXTURE3D_ARGS(_VBufferLighting, sampler_LinearClamp),"));
            Assert.That(hlslSource, Does.Contain("_VBufferLightingViewportScale3,"));
            Assert.That(hlslSource, Does.Contain("_VBufferLightingViewportLimit3,"));
            Assert.That(hlslSource, Does.Not.Contain("float3 vBufferUVW = GetVBufferUVW(positionNDC, linearDistance);"));
            Assert.That(hlslSource, Does.Not.Contain("_VBufferLighting.SampleLevel(sampler_LinearClamp"));
            Assert.That(hlslSource, Does.Contain("return float4(inputColor.rgb * saturate(lighting.a) + lighting.rgb, inputColor.a);"));
            Assert.That(hlslSource, Does.Contain("float3 SanitizeSkyRadiance(float3 color)"));
            Assert.That(hlslSource, Does.Contain("if (!hasAtmosphericDistance)"));
            Assert.That(hlslSource, Does.Contain("fogColor = lerp(fogColor, SanitizeSkyRadiance(GetFogColor(V, tFrag)), fogOpacity);"));
            Assert.That(hlslSource, Does.Contain("float3 composedColor = fogColor + (1.0f - fogOpacity) * inputColor.rgb;"));
            Assert.That(hlslSource, Does.Contain("float fogOpacity = ComputeHDRISkyFogOpacity(tFrag);"));
            Assert.That(hlslSource, Does.Contain("float3 volColor = SanitizeSkyRadiance(GetFogColor(V, tFrag)) * volOpacity;"));
            Assert.That(hlslSource, Does.Contain("float3 composedColor = volColor + (1.0f - volOpacity) * inputColor.rgb;"));
            Assert.That(hlslSource, Does.Not.Contain("struct OpaqueAtmosphericScatteringPositionInputs"));
            Assert.That(hlslSource, Does.Not.Contain("bool EvaluateAtmosphericScattering("));
            Assert.That(hlslSource, Does.Not.Contain("float3 positionWS = ComputeWorldSpacePosition(positionNDC, deviceDepth, UNITY_MATRIX_I_VP);"));
            Assert.That(hlslSource, Does.Not.Contain("float tFrag = distance(positionWS, _WorldSpaceCameraPos);"));
            Assert.That(hlslSource, Does.Not.Contain("if (IsFarDepth(deviceDepth))"));
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
