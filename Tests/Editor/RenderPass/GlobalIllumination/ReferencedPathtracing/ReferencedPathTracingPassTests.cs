using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredReferencedPathtracingPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ReferencedPathTracingPass);
        }

        [Test]
        public void Pass_DeclaresRequiredRecorderCapabilities()
        {
            Assert.That(
                typeof(IAllowGlobalStateModificationPass).IsAssignableFrom(typeof(ReferencedPathTracingPass)),
                Is.True);
            Assert.That(
                typeof(IBlueNoiseConsumerPass).IsAssignableFrom(
                    typeof(ReferencedPathTracingPass)),
                Is.True);
        }

        [Test]
        public void SamplingShader_UsesIndexedStableDimensionContract()
        {
            var rayTracingSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.raytrace"));
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));
            var samplingSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingSampling.hlsl"));

            Assert.That(
                ReferencedPathTracingPass.IndexedBndKeywordName,
                Is.EqualTo("VIVID_REFERENCE_PT_INDEXED_BND"));
            Assert.That(
                rayTracingSource,
                Does.Contain(
                    "#pragma multi_compile_local _ " +
                    "VIVID_REFERENCE_PT_INDEXED_BND"));
            Assert.That(
                samplingSource,
                Does.Contain(
                    "#define REFERENCED_PATH_SAMPLING_CONTRACT_VERSION 11"));
            Assert.That(
                samplingSource,
                Does.Contain("uint sampleBlock = sampleIndex >> 8u;"));
            Assert.That(
                samplingSource,
                Does.Contain("uint sampleInBlock = sampleIndex & 255u;"));
            Assert.That(
                samplingSource,
                Does.Contain("GetBNDSequenceSample256SPP("));
            Assert.That(
                samplingSource,
                Does.Contain("ReferencedPathtracingGetIndexedHashSample("));
            Assert.That(
                rayGenerationSource,
                Does.Contain("ReferencedPathtracingGetPathSample3D("));
            Assert.That(
                rayGenerationSource,
                Does.Not.Contain("NextReferencedPathtracingRng"));
        }

        [Test]
        public void RTXTF_UsesVendoredLibraryForOpaqueMaterialSampling()
        {
            var materialSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "ShaderPass",
                "ReferencedPathtracing.hlsl"));
            var integrationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "ShaderPass",
                "ReferencedPathtracingRTXTF.hlsl"));
            var samplerSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "ThirdParty",
                "RTXTF",
                "STFSamplerState.hlsli"));
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));

            Assert.That(
                samplerSource,
                Does.Contain("struct STF_SamplerState"));
            Assert.That(
                integrationSource,
                Does.Contain("Shaders/ThirdParty/RTXTF/STFSamplerState.hlsli"));
            Assert.That(
                integrationSource,
                Does.Contain("samplerState.Texture2DSampleLevel("));
            Assert.That(
                integrationSource,
                Does.Contain("defined(_ALPHATEST_ON)"));
            Assert.That(
                integrationSource,
                Does.Contain("defined(_SURFACE_TYPE_TRANSPARENT)"));
            Assert.That(
                integrationSource,
                Does.Contain("STF_MAGNIFICATION_METHOD_NONE"));
            Assert.That(
                materialSource,
                Does.Contain("ReferencedPathtracingCreateRTXTFState("));
            Assert.That(
                rayGenerationSource,
                Does.Contain("kReferencedPathtracingRTXTFDimensionOffset"));
        }

        [Test]
        public void GeometryOpacityEstimator_UsesUnweightedScalarCoverage()
        {
            var materialSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "ShaderPass",
                "ReferencedPathtracing.hlsl"));
            var indirectDiffuseSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Material",
                    "ShaderPass",
                    "IndirectDiffuse.hlsl"));
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));

            Assert.That(
                indirectDiffuseSource,
                Does.Contain("SampleOpenPbrGeometryOpacity("));
            Assert.That(
                indirectDiffuseSource,
                Does.Contain("float SampleOpenPbrGeometryOpacity("));
            Assert.That(
                indirectDiffuseSource,
                Does.Contain(
                    "textureLod).r);"));
            Assert.That(
                indirectDiffuseSource,
                Does.Not.Contain("_OpacityColor"));
            Assert.That(
                materialSource,
                Does.Contain(
                    "bool surfaceBranch = opacityRandom < geometryOpacity;"));
            Assert.That(
                materialSource,
                Does.Not.Contain("branchWeight"));
            Assert.That(
                rayGenerationSource,
                Does.Not.Contain("stochasticTransparencyWeight"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingVisibilityPayload opaqueVisibilityPayload;"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "throughput * cameraBackground.rgb"));
        }

        [Test]
        public void VisibilityTrace_UsesIndependentOpaquePayloadAndNonOpaqueMaterialFallback()
        {
            var commonSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingCommon.hlsl"));
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));

            Assert.That(
                commonSource,
                Does.Contain("struct ReferencedPathtracingVisibilityPayload"));
            Assert.That(
                commonSource,
                Does.Contain("uint hit;"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "#define REFERENCED_PATHTRACING_PAYLOAD_UINT4_COUNT 10"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("| RAY_FLAG_CULL_NON_OPAQUE"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("| RAY_FLAG_CULL_OPAQUE"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("MissReferencedPathtracingVisibility("));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "LoadReferencedPathtracingPayloadHit(nonOpaqueVisibilityPayload)"));
        }

        [Test]
        public void SurfaceTrace_Uses160BytePayloadWithPackedUnitVectors()
        {
            var commonSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingCommon.hlsl"));
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));

            Assert.That(
                commonSource,
                Does.Contain(
                    "#define REFERENCED_PATHTRACING_PAYLOAD_UINT4_COUNT 10"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "#define REFERENCED_PATHTRACING_PAYLOAD_DWORD_COUNT 40u"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "#define REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_INSTANCE_INDEX 39u"));
            Assert.That(
                commonSource,
                Does.Contain("PackReferencedPathtracingUnitVector("));
            Assert.That(
                commonSource,
                Does.Contain("UnpackReferencedPathtracingUnitVector("));
            Assert.That(
                commonSource,
                Does.Not.Contain("REFERENCED_PAYLOAD_RESULT_POSITION_WS "));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ray.Origin + ray.Direction * payload.hitDistance"));
        }

        [Test]
        public void SolidTransmission_UsesOpenPbrRefractionAndNestedMediumTransport()
        {
            var openPbrBridgeSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "ShaderPass",
                "OpenPBR",
                "OpenPBR.hlsl"));
            var openPbrVolumeBridgeSource = File.ReadAllText(
                GetPackageFilePath(
                    "Shaders",
                    "Material",
                    "ShaderPass",
                    "OpenPBR",
                    "OpenPBRVolume.hlsl"));
            var adapterSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "ShaderPass",
                "StandardLitOpenPBRAdapter.hlsl"));
            var materialSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "ShaderPass",
                "ReferencedPathtracing.hlsl"));
            var commonSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingCommon.hlsl"));
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));

            Assert.That(
                openPbrBridgeSource,
                Does.Contain(
                    "VIVIDRP_OPENPBR_FEATURE_EnableTranslucency true"));
            Assert.That(
                openPbrVolumeBridgeSource,
                Does.Contain(
                    "Vendor/openpbr_homogeneous_volume.h"));
            Assert.That(
                openPbrVolumeBridgeSource,
                Does.Not.Contain("Vendor/openpbr.h"));
            Assert.That(
                adapterSource,
                Does.Contain(
                    "inputs.transmission_weight = SampleOpenPbrTransmissionWeight("));
            Assert.That(
                adapterSource,
                Does.Contain(
                    "computeTargetTextureLOD(_TransmissionMap, textureBaseLambda)"));
            Assert.That(
                adapterSource,
                Does.Contain(
                    "inputs.transmission_depth = max(transmissionDepth, 0.0);"));
            Assert.That(
                adapterSource,
                Does.Contain(
                    "inputs.transmission_scatter = max(transmissionScatter, 0.0);"));
            Assert.That(
                adapterSource,
                Does.Not.Contain(
                    "VividReferencedPathtracingIsFinite"));
            Assert.That(
                adapterSource,
                Does.Contain(
                    "inputs.transmission_scatter_anisotropy ="));
            Assert.That(
                materialSource,
                Does.Contain(
                    "preparedBsdf.volume.extinction_coefficient"));
            Assert.That(
                materialSource,
                Does.Contain("preparedBsdf.volume.albedo"));
            Assert.That(
                materialSource,
                Does.Contain("preparedBsdf.volume.anisotropy"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "#define REFERENCED_PAYLOAD_RESULT_NEXT_MEDIUM_SCATTERING 38u"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "PackReferencedPathtracingMaterialMediumExtinction("));
            Assert.That(
                commonSource,
                Does.Contain(
                    "PackReferencedPathtracingMaterialMediumScattering("));
            Assert.That(
                materialSource,
                Does.Contain("result.mediumTransition ="));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "kReferencedPathtracingMaximumMaterialMediumDepth"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateMaterialMediumTransmittance("));
            Assert.That(
                rayGenerationSource,
                Does.Contain("openpbr_sample_event_distance("));
            Assert.That(
                rayGenerationSource,
                Does.Contain("OpenPBRVolume.hlsl"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "openpbr_calculate_weight_for_event_at_distance("));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "openpbr_sample_anisotropic_phase_function("));
            Assert.That(
                rayGenerationSource,
                Does.Contain("materialMediumScatteringStack"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("++materialMediumDepth;"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("--materialMediumDepth;"));
        }

        [Test]
        public void ShaderExecutionReorderingVariant_VendorsNativePluginAndWrapsSurfaceTrace()
        {
            var passSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathTracingPass.cs"));
            var rayTracingSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.raytrace"));
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));
            var nvApiHeader = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "NVAPI",
                "nvHLSLExtns.h"));
            var managedBinding = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "SubSystem",
                "Plugin",
                "NVAPI",
                "NvApiSer.cs"));

            Assert.That(
                ReferencedPathTracingPass
                    .ShaderExecutionReorderingKeywordName,
                Is.EqualTo("VIVID_REFERENCE_PT_SER"));
            Assert.That(
                ReferencedPathTracingPass
                    .ShaderExecutionReorderingUavSlot,
                Is.EqualTo(31u));
            Assert.That(
                passSource,
                Does.Contain("Shader.PropertyToID(\"g_NvidiaExt\")"));
            Assert.That(
                passSource,
                Does.Contain("GraphicsBuffer.Target.Counter"));
            Assert.That(
                passSource,
                Does.Contain("NvidiaShaderExtensionStructStride = 256"));
            Assert.That(
                passSource,
                Does.Contain("SetRayTracingBufferParam("));
            Assert.That(
                rayTracingSource,
                Does.Contain(
                    "#pragma multi_compile_local _ VIVID_REFERENCE_PT_SER"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("#define NV_SHADER_EXTN_SLOT u31"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("NvTraceRayHitObject("));
            Assert.That(
                rayGenerationSource,
                Does.Contain("NvReorderThread(hitObject);"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("NvInvokeHitObject("));
            Assert.That(
                nvApiHeader,
                Does.Contain("void NvReorderThread(NvHitObject HitObj)"));
            Assert.That(
                managedBinding,
                Does.Contain("private const string DllName = \"Unity_NVAPI\";"));
            Assert.That(
                managedBinding,
                Does.Contain("MarshalAs(UnmanagedType.I1)"));
            Assert.That(
                File.Exists(GetPackageFilePath(
                    "Runtime",
                    "SubSystem",
                    "Plugin",
                    "NVAPI",
                    "Plugins",
                    "x86_64",
                    "Unity_NVAPI.dll")),
                Is.True);
            var pluginImporter = AssetImporter.GetAtPath(
                    "Packages/com.vivid.render-pipelines/Runtime/SubSystem/Plugin/" +
                    "NVAPI/Plugins/x86_64/Unity_NVAPI.dll")
                as PluginImporter;
            Assert.That(pluginImporter, Is.Not.Null);
            Assert.That(
                pluginImporter.GetCompatibleWithAnyPlatform(),
                Is.False);
            Assert.That(
                pluginImporter.GetCompatibleWithEditor(),
                Is.True);
            Assert.That(
                pluginImporter.GetEditorData("OS"),
                Is.EqualTo("Windows"));
            Assert.That(
                pluginImporter.GetCompatibleWithPlatform(
                    BuildTarget.StandaloneWindows64),
                Is.True);
            Assert.That(
                File.Exists(GetPackageFilePath(
                    "NVAPINative~",
                    "src",
                    "Plugin.cpp")),
                Is.True);
            Assert.That(
                File.Exists(GetPackageFilePath(
                    "NVAPINative~",
                    "External",
                    "NVAPI",
                    "nvapi.h")),
                Is.True);
        }

        [Test]
        public void CapturePass_ConsumesRawFp32AccumulationAndIsNeverCulled()
        {
            IRenderPass renderPass = new ReferencedPathTracingCapturePass();

            var resources = renderPass.Initialize();
            var rawAccumulation = resources.Textures.Single();

            Assert.That(
                typeof(IRenderGraphSideEffectPass).IsAssignableFrom(
                    typeof(ReferencedPathTracingCapturePass)),
                Is.True);
            Assert.That(
                rawAccumulation.Name,
                Is.EqualTo("PathTracingAccumulationRaw"));
            Assert.That(rawAccumulation.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                rawAccumulation.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
        }

        [Test]
        public void Initialize_RegistersUnifiedReferenceLightInputsAndReblurOutputs()
        {
            IRenderPass renderPass = new ReferencedPathTracingPass();

            var resources = renderPass.Initialize();
            var accelerationStructure = resources.AccelerationStructures.Single();
            var referenceLightList = resources.Buffers.Single(
                resource => resource.Name == "ReferenceLightList");
            var referenceLightListParameters = resources.Buffers.Single(
                resource => resource.Name == "ReferenceLightListParameters");
            var environmentImportanceDistribution = resources.Buffers.Single(
                resource => resource.Name == "EnvironmentImportanceDistribution");
            var pathTracingRadiance = resources.Textures.Single(
                resource => resource.Name == "PathTracingRadiance");
            var pathTracingAlbedo = resources.Textures.Single(
                resource => resource.Name == "PathTracingAlbedo");
            var pathTracingNormal = resources.Textures.Single(
                resource => resource.Name == "PathTracingNormal");
            var debugTexture = resources.Textures.Single(
                resource => resource.Name == "DebugTexture");
            var environmentTexture = resources.Textures.Single(
                resource => resource.Name == "PathTracingEnvironment");
            var environmentBackgroundTexture = resources.Textures.Single(
                resource => resource.Name == "PathTracingEnvironmentBackground");
            var diffuse = resources.Textures.Single(
                resource => resource.Name == "DiffuseRadianceHitDistance");
            var specular = resources.Textures.Single(
                resource => resource.Name == "SpecularRadianceHitDistance");
            var directLighting = resources.Textures.Single(
                resource => resource.Name == "PathTracingDirectLighting");
            var emission = resources.Textures.Single(resource => resource.Name == "PathTracingEmission");
            var environmentDirectDiffuse = resources.Textures.Single(
                resource => resource.Name == "EnvironmentDirectDiffuse");
            var environmentDirectSpecular = resources.Textures.Single(
                resource => resource.Name == "EnvironmentDirectSpecular");
            var diffuseRayDirectionHitDistance = resources.Textures.Single(
                resource => resource.Name == "DiffuseRayDirectionHitDistance");
            var specularRayDirectionHitDistance = resources.Textures.Single(
                resource => resource.Name == "SpecularRayDirectionHitDistance");

            Assert.That(accelerationStructure.Name, Is.EqualTo("SceneRTAS"));
            Assert.That(accelerationStructure.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                resources.Buffers.Select(resource => resource.Name),
                Is.EquivalentTo(new[]
                {
                    "ReferenceLightList",
                    "ReferenceLightListParameters",
                    "EnvironmentImportanceDistribution"
                }));
            Assert.That(resources.Buffers.All(resource => resource.Access == AccessFlags.Read), Is.True);
            Assert.That(
                referenceLightList.Buffer.desc.Stride,
                Is.EqualTo(ReferencedPathTracingLightRecord.Stride));
            Assert.That(
                referenceLightListParameters.Buffer.desc.Stride,
                Is.EqualTo(ReferencedPathTracingLightListParameters.Stride));
            Assert.That(
                environmentImportanceDistribution.Buffer.desc.Count,
                Is.EqualTo(ReferencedPathTracingEnvironmentImportanceLayout.ElementCount));
            Assert.That(
                environmentImportanceDistribution.Buffer.desc.Stride,
                Is.EqualTo(ReferencedPathTracingEnvironmentImportanceLayout.ElementStride));
            Assert.That(environmentTexture.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                environmentTexture.Texture.desc.Dimension,
                Is.EqualTo(TextureDimension.Cube));
            Assert.That(
                environmentTexture.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(environmentTexture.Texture.desc.UseMipMap, Is.True);
            Assert.That(environmentBackgroundTexture.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                environmentBackgroundTexture.Texture.desc.Dimension,
                Is.EqualTo(TextureDimension.Cube));
            Assert.That(
                environmentBackgroundTexture.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(environmentBackgroundTexture.Texture.desc.UseMipMap, Is.True);
            Assert.That(pathTracingRadiance.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(pathTracingAlbedo.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(pathTracingNormal.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(
                new[]
                {
                    pathTracingRadiance,
                    pathTracingAlbedo,
                    pathTracingNormal
                }.All(resource =>
                    resource.Texture.desc.ColorFormat
                        == GraphicsFormat.R32G32B32A32_SFloat),
                Is.True);
            Assert.That(pathTracingRadiance.Texture.desc.EnableRandomWrite, Is.True);
            Assert.That(pathTracingAlbedo.Texture.desc.EnableRandomWrite, Is.True);
            Assert.That(pathTracingNormal.Texture.desc.EnableRandomWrite, Is.True);
            Assert.That(pathTracingRadiance.Texture.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(debugTexture.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(
                debugTexture.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(debugTexture.Texture.desc.EnableRandomWrite, Is.True);
            Assert.That(debugTexture.Texture.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(diffuse.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(specular.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(directLighting.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(emission.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(environmentDirectDiffuse.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(environmentDirectSpecular.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(diffuse.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(specular.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(diffuse.Texture.desc.ClearBuffer, Is.True);
            Assert.That(specular.Texture.desc.ClearBuffer, Is.True);
            Assert.That(
                directLighting.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(emission.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(
                environmentDirectDiffuse.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(
                environmentDirectSpecular.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(environmentDirectDiffuse.Texture.desc.ClearBuffer, Is.True);
            Assert.That(environmentDirectSpecular.Texture.desc.ClearBuffer, Is.True);
            Assert.That(
                diffuseRayDirectionHitDistance.Access,
                Is.EqualTo(AccessFlags.Write));
            Assert.That(
                specularRayDirectionHitDistance.Access,
                Is.EqualTo(AccessFlags.Write));
            Assert.That(
                diffuseRayDirectionHitDistance.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(
                specularRayDirectionHitDistance.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(
                diffuseRayDirectionHitDistance.Texture.desc.ClearBuffer,
                Is.True);
            Assert.That(
                specularRayDirectionHitDistance.Texture.desc.ClearBuffer,
                Is.True);
            Assert.That(
                diffuseRayDirectionHitDistance.Texture.desc.ClearColor.a,
                Is.EqualTo(ReferencedPathTracingPass.DlssInfiniteHitDistance));
            Assert.That(
                specularRayDirectionHitDistance.Texture.desc.ClearColor.a,
                Is.EqualTo(ReferencedPathTracingPass.DlssInfiniteHitDistance));
        }

        [Test]
        public void Prepare_ResizesReferenceDenoisingOutputsToCameraDimensions()
        {
            var pass = new ReferencedPathTracingPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 960;
            cameraData.actualHeight = 540;

            pass.Prepare(frameData);

            var output = GetField<RenderGraphTexture>(pass, "m_PathTracingRadiance");
            var albedo = GetField<RenderGraphTexture>(pass, "m_PathTracingAlbedo");
            var normal = GetField<RenderGraphTexture>(pass, "m_PathTracingNormal");
            var debugTexture = GetField<RenderGraphTexture>(
                pass,
                "m_DebugTexture");
            var environmentDirectDiffuse = GetField<RenderGraphTexture>(
                pass,
                "m_EnvironmentDirectDiffuse");
            var environmentDirectSpecular = GetField<RenderGraphTexture>(
                pass,
                "m_EnvironmentDirectSpecular");
            Assert.That(output.desc.Width, Is.EqualTo(960));
            Assert.That(output.desc.Height, Is.EqualTo(540));
            Assert.That(output.desc.FilterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(output.desc.EnableRandomWrite, Is.True);
            Assert.That(albedo.desc.Width, Is.EqualTo(960));
            Assert.That(albedo.desc.Height, Is.EqualTo(540));
            Assert.That(normal.desc.Width, Is.EqualTo(960));
            Assert.That(normal.desc.Height, Is.EqualTo(540));
            Assert.That(debugTexture.desc.Width, Is.EqualTo(960));
            Assert.That(debugTexture.desc.Height, Is.EqualTo(540));
            Assert.That(debugTexture.desc.FilterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(debugTexture.desc.EnableRandomWrite, Is.True);
            Assert.That(environmentDirectDiffuse.desc.Width, Is.EqualTo(960));
            Assert.That(environmentDirectDiffuse.desc.Height, Is.EqualTo(540));
            Assert.That(environmentDirectSpecular.desc.Width, Is.EqualTo(960));
            Assert.That(environmentDirectSpecular.desc.Height, Is.EqualTo(540));
        }

        [Test]
        public void Prepare_CachesPerspectiveCameraRayRangeAndPosition()
        {
            var gameObject = new GameObject("ReferencedPathtracingPassTests.Camera");
            var camera = gameObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.25f;
            camera.farClipPlane = 750.0f;
            camera.transform.position = new Vector3(1.0f, 2.0f, 3.0f);

            try
            {
                var pass = new ReferencedPathTracingPass();
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.SetCamera(camera);
                cameraData.actualWidth = 320;
                cameraData.actualHeight = 180;
                cameraData.frameIndex = 37;

                pass.Prepare(frameData);

                Assert.That(GetField<bool>(pass, "m_ShouldSkipExecution"), Is.False);
                Assert.That(GetField<float>(pass, "m_RayMinDistance"), Is.EqualTo(0.25f));
                Assert.That(GetField<float>(pass, "m_RayMaxDistance"), Is.EqualTo(750.0f));
                Assert.That(GetField<int>(pass, "m_FrameIndex"), Is.EqualTo(37));
                Assert.That(GetField<Vector4>(pass, "m_CameraPositionWS"), Is.EqualTo(new Vector4(1.0f, 2.0f, 3.0f, 1.0f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void UnifiedNeeCandidate_PreservesProposalPdfsAndSegmentLightMis()
        {
            var commonSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingCommon.hlsl"));
            var closestHitSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "ShaderPass",
                "ReferencedPathtracing.hlsl"));
            var lightListSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingLightList.hlsl"));
            var candidateSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingNEECandidate.hlsl"));
            var segmentLightSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingSegmentLight.hlsl"));
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));
            var reblurResolveSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "NRD",
                "REBLUR",
                "REBLUR_DiffuseSpecular_Resolve.compute"));

            Assert.That(commonSource, Does.Contain("float neeSelectionPdf;"));
            Assert.That(commonSource, Does.Contain("float neeSolidAnglePdf;"));
            Assert.That(commonSource, Does.Contain("float neeBsdfPdf;"));
            Assert.That(commonSource, Does.Contain("uint neeLightType;"));
            Assert.That(
                commonSource,
                Does.Contain("int _ReferencedLightSpatialIndexEnabled;"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "ReferencedPathtracingGetDirectionalLightSolidAnglePdf"));
            Assert.That(
                commonSource,
                Does.Contain("lightPdf = rcp(solidAngle);"));
            Assert.That(
                commonSource,
                Does.Contain("ReferencedPathtracingGetLightEstimatorWeight"));
            Assert.That(
                commonSource,
                Does.Contain("ReferencedPathtracingGetBsdfEstimatorWeight"));
            Assert.That(
                lightListSource,
                Does.Contain("ReferencedPathtracingSampleUnifiedLightSource"));
            Assert.That(
                lightListSource,
                Does.Contain("ReferencedPathtracingIsLightNeeEligible"));
            Assert.That(
                lightListSource,
                Does.Contain(
                    "REFERENCED_ENVIRONMENT_AVERAGE_LUMINANCE_OFFSET"));
            Assert.That(
                lightListSource,
                Does.Contain(
                    "ReferencedPathtracingGetUnifiedEnvironmentSelectionPdf"));
            Assert.That(
                lightListSource,
                Does.Contain(
                    "ReferencedPathtracingResolveLightSpatialCandidateSet"));
            Assert.That(
                lightListSource,
                Does.Contain(
                    "REFERENCED_LIGHT_CONTEXT_FULL_SCAN_FALLBACK"));
            Assert.That(
                lightListSource,
                Does.Contain(
                    "parameters.incompleteLocalProposalLightCount == 0u"));
            Assert.That(
                candidateSource,
                Does.Contain("struct ReferencedPathtracingNEECandidate"));
            Assert.That(
                candidateSource,
                Does.Contain("float3 incidentRadianceOverPdf;"));
            Assert.That(
                candidateSource,
                Does.Contain(
                    "ReferencedPathtracingSampleUnifiedNEECandidate"));
            Assert.That(
                closestHitSource,
                Does.Contain(
                    "ReferencedPathtracingSampleUnifiedNEECandidate"));
            Assert.That(
                closestHitSource,
                Does.Contain("payload.neeSelectionPdf"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "GetReferencedPathtracingNEELightEstimatorWeight"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateUnifiedEnvironmentLightPdf"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingGetUnifiedReferenceLightSelectionPdf"));
            Assert.That(
                segmentLightSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateDirectionalLightPdf"));
            Assert.That(
                segmentLightSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateRectangleSegmentLight"));
            Assert.That(
                segmentLightSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateDiscSegmentLight"));
            Assert.That(
                segmentLightSource,
                Does.Contain(
                    "distanceSquared / (lightFacingCosine * sampleArea)"));
            Assert.That(
                segmentLightSource,
                Does.Not.Contain("REFERENCED_LIGHT_TYPE_POINT"));
            Assert.That(
                segmentLightSource,
                Does.Not.Contain("REFERENCED_LIGHT_TYPE_TUBE"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("payload.nextThroughputWeight"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingEvaluateSegmentLight"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "ReferencedPathtracingGetContextLightIndex"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("lightSpatialIndexDiagnostic"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "RWTexture2D<float4> _ReferencedPathTracingDebugTexture;"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "_ReferencedPathTracingRadiance[pixelCoord] ="));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "float4(physicalRadiance, physicalOutputAlpha)"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "_ReferencedPathTracingAlbedo[pixelCoord] ="));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "_ReferencedPathTracingNormal[pixelCoord] ="));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "_ReferencedPathTracingDebugTexture[pixelCoord] ="));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "float4(debugRadiance, debugOutputAlpha)"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("neeTransportDiagnostic"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("segmentTransportDiagnostic"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "primaryDenoiserMainLightDiffuseRadiance"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "primaryDenoiserMainLightSpecularRadiance"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "CombineReferencedPathtracingDenoiserHitDistance"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "kReferencedPathtracingDlssInfiniteHitDistance = 65504.0"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "PackReferencedPathtracingDlssRayDirectionHitDistance"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "_ReferencedDiffuseRayDirectionHitDistance[pixelCoord] ="));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "_ReferencedSpecularRayDirectionHitDistance[pixelCoord] ="));
            Assert.That(
                rayGenerationSource,
                Does.Contain("diffuseHitDistanceValid"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("specularHitDistanceValid"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("diffuseDlssHitDistance"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("specularDlssHitDistance"));
            Assert.That(
                rayGenerationSource,
                Does.Not.Contain(
                    "primaryHit != 0u ? diffuseHitDistance : 0.0"));
            Assert.That(
                rayGenerationSource,
                Does.Not.Contain(
                    "primaryHit != 0u ? specularHitDistance : 0.0"));
            Assert.That(
                reblurResolveSource,
                Does.Contain("_ReblurMainLightInSignals != 0"));
            Assert.That(
                reblurResolveSource,
                Does.Contain("+ unfilteredDirectLighting"));
            Assert.That(
                rayGenerationSource,
                Does.Not.Contain("payload.mainLightDirectionWS"));
            Assert.That(
                rayGenerationSource,
                Does.Not.Contain("payload.environmentDirectionWS"));
        }

        [Test]
        public void ReferenceAtmosphereA0_BindsPhysicalContractAndGuardsHdriEvaluation()
        {
            var commonSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingCommon.hlsl"));
            var passSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathTracingPass.cs"));
            var atmosphereSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathTracingEnvironmentState.cs"));

            Assert.That(
                commonSource,
                Does.Contain(
                    "kReferencedEnvironmentModeReferenceAtmosphere"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "_ReferencedEnvironmentMode == kReferencedEnvironmentModeHdri"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "ReferencedPathtracingHasReferenceAtmosphere"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "_ReferencedAtmospherePlanetCenterBottomRadius"));
            Assert.That(
                commonSource,
                Does.Contain("_ReferencedAtmosphereRayleighScattering"));
            Assert.That(
                commonSource,
                Does.Contain("_ReferencedAtmosphereMieScattering"));
            Assert.That(
                commonSource,
                Does.Contain("_ReferencedAtmosphereOzoneExtinction"));
            Assert.That(
                commonSource,
                Does.Contain("_ReferencedAtmosphereSunIlluminance"));
            Assert.That(
                passSource,
                Does.Contain("BindAtmosphereContract(cmd);"));
            Assert.That(
                passSource,
                Does.Contain(
                    "ReferencedPathTracingAtmosphereState.Resolve("));
            Assert.That(
                passSource,
                Does.Contain(
                    "m_AtmosphereState"));
            Assert.That(
                atmosphereSource,
                Does.Not.Contain(
                    "PhysicallyBasedSkyShaderParameterBuilder"));
        }

        [Test]
        public void RenderGraphNode_DefinesUnifiedReferenceLightInputsAndReblurOutputs()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredReferencedPathtracingPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_SceneAccelerationStructure"), Is.Not.Null);
                Assert.That(
                    node.GetInputPortByName("m_ReferenceLightList"),
                    Is.Not.Null);
                Assert.That(
                    node.GetInputPortByName("m_ReferenceLightListParameters"),
                    Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRLightBuffer"), Is.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRParameterBuffer"), Is.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRReservoirBuffer"), Is.Null);
                Assert.That(node.GetInputPortByName("m_ReGIRLightPdfTexture"), Is.Null);
                Assert.That(node.GetInputPortByName("m_EnvironmentTexture"), Is.Not.Null);
                Assert.That(
                    node.GetInputPortByName("m_EnvironmentBackgroundTexture"),
                    Is.Not.Null);
                Assert.That(
                    node.GetInputPortByName("m_EnvironmentImportanceDistribution"),
                    Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_PathTracingRadiance"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_PathTracingAlbedo"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_PathTracingNormal"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_WorldPositionTexture"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_DebugTexture"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DebugTexture"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_DiffuseRadianceHitDistance"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_SpecularRadianceHitDistance"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DirectLighting"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_Emission"), Is.Not.Null);
                Assert.That(
                    node.GetOutputPortByName("m_EnvironmentDirectDiffuse"),
                    Is.Not.Null);
                Assert.That(
                    node.GetOutputPortByName("m_EnvironmentDirectSpecular"),
                    Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void LegacyWorldPositionBinding_ResolvesToPathTracingRadiance()
        {
            var field = RenderGraphPassReflectionUtility.GetInstanceField(
                typeof(ReferencedPathTracingPass),
                "m_WorldPositionTexture");

            Assert.That(field, Is.Not.Null);
            Assert.That(field.Name, Is.EqualTo("m_PathTracingRadiance"));
        }

        [Test]
        public void Prepare_UsesRenderingDebuggerPathTracingModes()
        {
            VividRenderingDebugDisplaySettings.Data.Reset();
            try
            {
                VividRenderingDebugDisplaySettings.Data
                    .referencedPathTracingTransportDebugMode =
                        ReferencedPathTracingTransportDebugMode.NeePdfs;
                VividRenderingDebugDisplaySettings.Data
                    .referencedPathTracingEnvironmentDebugMode =
                        ReferencedPathTracingEnvironmentDebugMode
                            .IndirectMissOnly;
                var pass = new ReferencedPathTracingPass();
                var frameData = new ContextContainer();
                frameData.GetOrCreate<VividCameraData>();

                pass.Prepare(frameData);

                var settings =
                    GetField<ReferencedPathTracingDebugSettings>(
                        pass,
                        "m_DebugSettings");
                Assert.That(
                    settings.transportMode,
                    Is.EqualTo(
                        ReferencedPathTracingTransportDebugMode.NeePdfs));
                Assert.That(
                    settings.environmentMode,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentDebugMode
                            .IndirectMissOnly));
            }
            finally
            {
                VividRenderingDebugDisplaySettings.Data.Reset();
            }
        }

        [Test]
        public void DebugSettings_DefaultToCombinedWithoutRenderingDebuggerData()
        {
            var settings =
                ReferencedPathTracingDebugSettings.Resolve(null);

            Assert.That(
                settings.transportMode,
                Is.EqualTo(
                    ReferencedPathTracingTransportDebugMode.Combined));
            Assert.That(
                settings.environmentMode,
                Is.EqualTo(
                    ReferencedPathTracingEnvironmentDebugMode.Combined));
        }

        [Test]
        public void PhysicalCamera_UsesThinLensSamplingAndReservedDimensions()
        {
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));
            var samplingSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingSampling.hlsl"));

            Assert.That(
                samplingSource,
                Does.Contain(
                    "ReferencedPathtracingSampleConcentricDisk"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "kReferencedPathtracingLensDimension"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "GetReferencedPathtracingPhysicalCameraRay"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("focusPoint - rayOrigin"));
            Assert.That(
                rayGenerationSource,
                Does.Contain(
                    "_RayMinDistance / cameraForwardProjection"));
        }

        [Test]
        public void PhysicalCameraState_UsesOnlyCameraOpticalSettings()
        {
            var cameraObject = new GameObject(
                "ReferencedPathTracingPassTests.PhysicalCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.focalLength = 50.0f;
            camera.aperture = 2.0f;
            camera.focusDistance = 7.0f;
            camera.anamorphism = 0.0f;
            var settings = DepthOfFieldSettingsData.CreateDefault();
            settings.enabled = true;
            settings.focusMode = DepthOfFieldMode.UsePhysicalCamera;
            settings.focusDistanceMode = FocusDistanceMode.Volume;
            settings.focusDistance = 12.0f;

            try
            {
                var cameraFocus =
                    ReferencedPathTracingPhysicalCameraState.Resolve(
                        camera,
                        settings);
                Assert.That(cameraFocus.enabled, Is.True);
                Assert.That(
                    cameraFocus.focusDistance,
                    Is.EqualTo(7.0f).Within(1e-6f));
                Assert.That(
                    cameraFocus.lensRadius.x,
                    Is.EqualTo(0.0125f).Within(1e-6f));
                Assert.That(
                    cameraFocus.lensRadius.y,
                    Is.EqualTo(0.0125f).Within(1e-6f));

                settings.focusDistanceMode = FocusDistanceMode.Camera;
                settings.focusDistance = 30.0f;
                var changedVolumeFocus =
                    ReferencedPathTracingPhysicalCameraState.Resolve(
                        camera,
                        settings);
                Assert.That(
                    changedVolumeFocus.signature,
                    Is.EqualTo(cameraFocus.signature));

                camera.focusDistance = 9.0f;
                var refocused =
                    ReferencedPathTracingPhysicalCameraState.Resolve(
                        camera,
                        settings);
                Assert.That(
                    refocused.focusDistance,
                    Is.EqualTo(9.0f).Within(1e-6f));
                Assert.That(
                    refocused.signature,
                    Is.Not.EqualTo(cameraFocus.signature));

                camera.aperture = 4.0f;
                var stoppedDown =
                    ReferencedPathTracingPhysicalCameraState.Resolve(
                        camera,
                        settings);
                Assert.That(
                    stoppedDown.lensRadius.x,
                    Is.EqualTo(0.00625f).Within(1e-6f));
                Assert.That(
                    stoppedDown.signature,
                    Is.Not.EqualTo(refocused.signature));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void EnvironmentState_ResolvesHdriVisibilityLightingAndSamplingIndependently()
        {
            var cubemap = new Cubemap(4, TextureFormat.RGBAHalf, true);
            var settings = ScriptableObject.CreateInstance<ReferencedPathTracingSettingsVolume>();

            try
            {
                settings.active = true;
                settings.environmentLighting.value = true;
                settings.environmentCameraVisible.value = false;
                settings.environmentSamplingMode.value =
                    ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling;
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.HDRI,
                    specularCubemap = cubemap,
                    tint = new Color(0.5f, 0.75f, 1.0f, 0.25f),
                    exposure = 2.0f,
                    rotation = 45.0f,
                    skyHash = 1234
                };

                var state = ReferencedPathTracingEnvironmentState.Resolve(skyData, settings);

                Assert.That(
                    state.mode,
                    Is.EqualTo(ReferencedPathTracingEnvironmentMode.Hdri));
                Assert.That(state.hasHdri, Is.True);
                Assert.That(state.lightingEnabled, Is.True);
                Assert.That(state.cameraVisible, Is.False);
                Assert.That(state.importanceSamplingEnabled, Is.True);
                Assert.That(state.neeEnabled, Is.True);
                Assert.That(
                    state.samplingMode,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling));
                Assert.That(
                    state.estimatorMode,
                    Is.EqualTo(ReferencedPathTracingEnvironmentEstimatorMode.Mis));
                Assert.That(state.tint, Is.EqualTo(new Color(0.5f, 0.75f, 1.0f, 1.0f)));
                Assert.That(state.intensityMultiplier, Is.EqualTo(2.0f));
                Assert.That(state.rotation, Is.EqualTo(45.0f));
                Assert.That(state.skyHash, Is.EqualTo(1234));
                Assert.That(state.contentHash, Is.Not.Zero);
                Assert.That(state.backgroundResolution, Is.EqualTo(4));
                Assert.That(state.lightingResolution, Is.EqualTo(4));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void EnvironmentMode_IsolatesReferenceAtmosphereFromHdriEnergy()
        {
            var cubemap = new Cubemap(4, TextureFormat.RGBAHalf, true);
            var settings =
                ScriptableObject.CreateInstance<
                    ReferencedPathTracingSettingsVolume>();

            try
            {
                settings.active = true;
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.HDRI,
                    specularCubemap = cubemap,
                    tint = Color.white,
                    exposure = 1.0f,
                    skyHash = 7
                };

                settings.environmentMode.value =
                    ReferencedPathTracingEnvironmentMode.Hdri;
                var hdri =
                    ReferencedPathTracingEnvironmentState.Resolve(
                        skyData,
                        settings);
                settings.environmentMode.value =
                    ReferencedPathTracingEnvironmentMode.ReferenceAtmosphere;
                var atmosphere =
                    ReferencedPathTracingEnvironmentState.Resolve(
                        skyData,
                        settings);

                Assert.That(hdri.hasHdri, Is.True);
                Assert.That(
                    atmosphere.mode,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentMode.ReferenceAtmosphere));
                Assert.That(atmosphere.hasHdri, Is.False);
                Assert.That(atmosphere.lightingEnabled, Is.False);
                Assert.That(atmosphere.cameraVisible, Is.False);
                Assert.That(atmosphere.importanceSamplingEnabled, Is.False);
                Assert.That(atmosphere.neeEnabled, Is.False);
                Assert.That(
                    atmosphere.signature,
                    Is.Not.EqualTo(hdri.signature));
                Assert.That(
                    atmosphere.samplingSignature,
                    Is.Not.EqualTo(hdri.samplingSignature));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void ReferenceAtmosphereState_CapturesPhysicalSkyWithoutRasterResources()
        {
            var settings =
                ScriptableObject.CreateInstance<
                    ReferencedPathTracingSettingsVolume>();
            var skyVolume =
                ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                settings.active = true;
                settings.environmentMode.value =
                    ReferencedPathTracingEnvironmentMode.ReferenceAtmosphere;
                settings.referenceAtmosphereHoldout.value = true;
                settings.referenceClouds.value = true;
                settings.referenceCloudsCameraVisible.value = false;
                settings.referenceGroundCameraVisible.value = false;
                skyVolume.planetRadius.value = 10000.0f;
                skyVolume.airMaximumAltitude.value = 2000.0f;
                skyVolume.groundTint.value =
                    new Color(0.25f, 0.5f, 0.75f, 1.0f);
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.PhysicallyBased,
                    skyHash = 123
                };
                var lightData = new VividLightData
                {
                    directionalLights = new[]
                    {
                        new VividLightData.DirectionalLightData
                        {
                            directionWS =
                                new Vector3(0.0f, 2.0f, 0.0f),
                            color = new Vector3(100.0f, 90.0f, 80.0f),
                            angularDiameter = 0.01f,
                            shadowStrength = 0.75f
                        }
                    },
                    directionalLightCount = 1,
                    mainDirectionalLightIndex = 0,
                    mainDirectionalLightEntityId = EntityId.None
                };

                var state =
                    ReferencedPathTracingAtmosphereState.Resolve(
                        skyData,
                        skyVolume,
                        null,
                        lightData,
                        settings);
                var metadata =
                    ReferencedPathTracingAtmosphereMetadata.Capture(state);

                Assert.That(state.active, Is.True);
                Assert.That(state.cloudsActive, Is.True);
                Assert.That(state.skyHash, Is.EqualTo(123));
                Assert.That(
                    (state.flags
                        & ReferencedPathTracingAtmosphereFlags
                            .AtmosphereCameraVisible) != 0,
                    Is.True);
                Assert.That(
                    (state.flags
                        & ReferencedPathTracingAtmosphereFlags
                            .AtmosphereHoldout) != 0,
                    Is.True);
                Assert.That(
                    (state.flags
                        & ReferencedPathTracingAtmosphereFlags.CloudsEnabled)
                        != 0,
                    Is.True);
                Assert.That(
                    (state.flags
                        & ReferencedPathTracingAtmosphereFlags
                            .CloudsCameraVisible) != 0,
                    Is.False);
                Assert.That(
                    (state.flags
                        & ReferencedPathTracingAtmosphereFlags
                            .GroundCameraVisible) != 0,
                    Is.False);
                Assert.That(metadata.active, Is.True);
                Assert.That(
                    metadata.validationContractVersion,
                    Is.EqualTo(
                        ReferencedPathTracingAtmosphereValidationGate
                            .ContractVersion));
                Assert.That(
                    metadata.transportMode,
                    Is.EqualTo(
                        ReferencedPathTracingAtmosphereTransportMode
                            .NumericalReference));
                Assert.That(
                    metadata.usesOpticalDepthLutApproximation,
                    Is.False);
                Assert.That(
                    metadata.numericalReferenceEligible,
                    Is.False);
                Assert.That(
                    metadata.atmosphereTransmittanceSampleCount,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentImportanceLayout
                            .AtmosphereTransportReferenceSampleCount));
                Assert.That(
                    metadata.maximumAtmosphereTrackingStepCount,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentImportanceLayout
                            .MaximumAtmosphereTrackingStepCount));
                Assert.That(
                    metadata.maximumCloudTrackingStepCount,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentImportanceLayout
                            .MaximumCloudTrackingStepCount));
                Assert.That(
                    metadata.cloudContractVersion,
                    Is.EqualTo(
                        ReferencedPathTracingAtmosphereState
                            .CloudContractVersion));
                Assert.That(
                    metadata.cloudBottomRadius,
                    Is.EqualTo(11500.0f));
                Assert.That(
                    metadata.cloudTopRadius,
                    Is.EqualTo(metadata.topRadius));
                Assert.That(
                    metadata.cloudCoverage,
                    Is.EqualTo(0.55f));
                Assert.That(
                    metadata.cloudAccelerationVersion,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentImportanceLayout
                            .CloudVersion));
                Assert.That(
                    metadata.cloudAccelerationResolution,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentImportanceLayout
                            .CloudRadialResolution));
                Assert.That(
                    metadata.cloudShadowReferenceSampleCount,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentImportanceLayout
                            .CloudShadowNumericalReferenceSampleCount));
                Assert.That(
                    metadata.cloudShadowUsesDeterministicApproximation,
                    Is.True);
                Assert.That(
                    metadata.cloudTransportUsesBiasedApproximation,
                    Is.True);
                Assert.That(
                    metadata.opticalDepthContractVersion,
                    Is.EqualTo(
                        ReferencedPathTracingAtmosphereState
                            .OpticalDepthContractVersion));
                Assert.That(
                    metadata.localSegmentMaximumSampleCount,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentImportanceLayout
                            .AtmosphereLocalSegmentMaximumSampleCount));
                Assert.That(
                    metadata.localSegmentSamplesPerProfileScale,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentImportanceLayout
                            .AtmosphereLocalSegmentSamplesPerProfileScale));
                Assert.That(
                    metadata.localSegmentProfileScaleCount,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentImportanceLayout
                            .AtmosphereLocalSegmentProfileScaleCount));
                Assert.That(metadata.bottomRadius, Is.EqualTo(10000.0f));
                Assert.That(metadata.topRadius, Is.GreaterThan(10000.0f));
                Assert.That(
                    metadata.groundAlbedo.r,
                    Is.EqualTo(skyVolume.groundTint.value.linear.r)
                        .Within(1e-6f));
                Assert.That(
                    metadata.rayleighScattering.sqrMagnitude,
                    Is.GreaterThan(0.0f));
                Assert.That(
                    metadata.rayleighScaleHeight,
                    Is.GreaterThan(0.0f));
                Assert.That(
                    metadata.mieScaleHeight,
                    Is.GreaterThan(0.0f));
                Assert.That(
                    metadata.ozoneLayerWidth,
                    Is.GreaterThan(0.0f));
                Assert.That(metadata.hasSun, Is.True);
                Assert.That(metadata.sunDirection, Is.EqualTo(Vector3.up));
                Assert.That(
                    metadata.sunIlluminance,
                    Is.EqualTo(new Vector3(100.0f, 90.0f, 80.0f)));
                Assert.That(
                    metadata.sunAngularDiameter,
                    Is.EqualTo(0.01f));
                Assert.That(
                    metadata.sunShadowStrength,
                    Is.EqualTo(0.75f));

                var originalSignature = state.signature;
                var originalOpticalDepthSignature =
                    state.opticalDepthSignature;
                var originalCloudSignature =
                    state.cloudSignature;
                lightData.directionalLights[0].color =
                    new Vector3(200.0f, 180.0f, 160.0f);
                state = ReferencedPathTracingAtmosphereState.Resolve(
                    skyData,
                    skyVolume,
                    null,
                    lightData,
                    settings);
                Assert.That(
                    state.signature,
                    Is.Not.EqualTo(originalSignature));
                Assert.That(
                    state.opticalDepthSignature,
                    Is.EqualTo(originalOpticalDepthSignature));
                Assert.That(
                    state.cloudSignature,
                    Is.EqualTo(originalCloudSignature));
                lightData.directionalLights[0].color =
                    new Vector3(100.0f, 90.0f, 80.0f);
                skyData.skyHash = 124;
                state = ReferencedPathTracingAtmosphereState.Resolve(
                    skyData,
                    skyVolume,
                    null,
                    lightData,
                    settings);
                Assert.That(
                    state.signature,
                    Is.EqualTo(originalSignature));
                skyVolume.planetRadius.value = 11000.0f;
                state = ReferencedPathTracingAtmosphereState.Resolve(
                    skyData,
                    skyVolume,
                    null,
                    lightData,
                    settings);
                Assert.That(
                    state.signature,
                    Is.Not.EqualTo(originalSignature));
                Assert.That(
                    state.opticalDepthSignature,
                    Is.Not.EqualTo(originalOpticalDepthSignature));
                var radiusAdjustedCloudSignature =
                    state.cloudSignature;
                settings.referenceCloudCoverage.value = 0.75f;
                state = ReferencedPathTracingAtmosphereState.Resolve(
                    skyData,
                    skyVolume,
                    null,
                    lightData,
                    settings);
                Assert.That(
                    state.cloudSignature,
                    Is.Not.EqualTo(radiusAdjustedCloudSignature));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(skyVolume);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ReferenceAtmosphereState_CapturesCameraRelativeGroundPolicy()
        {
            var settings =
                ScriptableObject.CreateInstance<
                    ReferencedPathTracingSettingsVolume>();
            var skyVolume =
                ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();
            var skySettings =
                ScriptableObject.CreateInstance<SkySettingsVolume>();

            try
            {
                settings.active = true;
                settings.environmentMode.value =
                    ReferencedPathTracingEnvironmentMode.ReferenceAtmosphere;
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.PhysicallyBased
                };

                skySettings.renderingSpace.value = RenderingSpace.Camera;
                var cameraRelativeState =
                    ReferencedPathTracingAtmosphereState.Resolve(
                        skyData,
                        skyVolume,
                        null,
                        null,
                        settings,
                        skySettings);
                var cameraRelativeMetadata =
                    ReferencedPathTracingAtmosphereMetadata.Capture(
                        cameraRelativeState);

                Assert.That(
                    (cameraRelativeState.flags
                        & ReferencedPathTracingAtmosphereFlags
                            .CameraRelativeRenderingSpace) != 0,
                    Is.True);
                Assert.That(
                    cameraRelativeMetadata.cameraRelativeRenderingSpace,
                    Is.True);

                skySettings.renderingSpace.value = RenderingSpace.World;
                var worldState =
                    ReferencedPathTracingAtmosphereState.Resolve(
                        skyData,
                        skyVolume,
                        null,
                        null,
                        settings,
                        skySettings);
                var worldMetadata =
                    ReferencedPathTracingAtmosphereMetadata.Capture(
                        worldState);

                Assert.That(
                    (worldState.flags
                        & ReferencedPathTracingAtmosphereFlags
                            .CameraRelativeRenderingSpace) != 0,
                    Is.False);
                Assert.That(
                    worldMetadata.cameraRelativeRenderingSpace,
                    Is.False);
                Assert.That(
                    cameraRelativeState.signature,
                    Is.Not.EqualTo(worldState.signature));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(skyVolume);
                UnityEngine.Object.DestroyImmediate(skySettings);
            }
        }

        [Test]
        public void EnvironmentMetadata_CapturesRawLightingContractWithoutDisplayExposure()
        {
            var cubemap = new Cubemap(8, TextureFormat.RGBAHalf, true)
            {
                name = "Metadata HDRI"
            };
            var settings =
                ScriptableObject.CreateInstance<ReferencedPathTracingSettingsVolume>();

            try
            {
                settings.active = true;
                settings.environmentSamplingMode.value =
                    ReferencedPathTracingEnvironmentSamplingMode.UniformSphere;
                settings.environmentEstimatorMode.value =
                    ReferencedPathTracingEnvironmentEstimatorMode.Mis;
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.HDRI,
                    specularCubemap = cubemap,
                    tint = Color.white,
                    exposure = 3.0f,
                    rotation = 75.0f,
                    skyHash = 91,
                    skyContentHash = 17
                };

                var metadata = ReferencedPathTracingEnvironmentMetadata.Capture(
                    skyData,
                    settings);

                Assert.That(
                    metadata.contractVersion,
                    Is.EqualTo(ReferencedPathTracingEnvironmentMetadata.ContractVersion));
                Assert.That(
                    metadata.mode,
                    Is.EqualTo(ReferencedPathTracingEnvironmentMode.Hdri));
                Assert.That(metadata.assetName, Is.EqualTo("Metadata HDRI"));
                Assert.That(metadata.skyHash, Is.EqualTo(91));
                Assert.That(metadata.contentHash, Is.EqualTo(17));
                Assert.That(metadata.backgroundResolution, Is.EqualTo(8));
                Assert.That(metadata.lightingResolution, Is.EqualTo(8));
                Assert.That(metadata.lightingEnabled, Is.True);
                Assert.That(metadata.cameraVisible, Is.True);
                Assert.That(metadata.rotation, Is.EqualTo(75.0f));
                Assert.That(metadata.physicalIntensityMultiplier, Is.EqualTo(3.0f));
                Assert.That(
                    metadata.samplingMode,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentSamplingMode.UniformSphere));
                Assert.That(
                    metadata.estimatorMode,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentEstimatorMode.Mis));
                Assert.That(
                    metadata.debugMode,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentDebugMode.Combined));
                Assert.That(
                    metadata.pdfVersion,
                    Is.EqualTo(ReferencedPathTracingEnvironmentImportanceLayout.Version));
                Assert.That(metadata.rawRadianceIsPreExposed, Is.False);
                Assert.That(metadata.atmosphere, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void SampleSequence_AdvancesOncePerFrameAndResetsOnSignature()
        {
            var gameObject =
                new GameObject("ReferencedPathTracingSampleSequenceTests.Camera");
            var camera = gameObject.AddComponent<Camera>();

            try
            {
                var first = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    100,
                    11ul,
                    true);
                var duplicatePrepare = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    100,
                    11ul,
                    false);
                var nextFrame = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    101,
                    11ul,
                    false);
                var changedScene = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    102,
                    12ul,
                    false);
                ReferencedPathTracingSampleSequence.RequestReset(camera);
                var requestedReset = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    103,
                    12ul,
                    false);

                Assert.That(first, Is.Zero);
                Assert.That(duplicatePrepare, Is.Zero);
                Assert.That(nextFrame, Is.EqualTo(1u));
                Assert.That(changedScene, Is.Zero);
                Assert.That(requestedReset, Is.Zero);
            }
            finally
            {
                ReferencedPathTracingSampleSequence.Dispose();
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void FaceSubsurfaceV1_UsesBurleySurfaceQueryAndDiffuseGuides()
        {
            var adapterSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "ShaderPass",
                "StandardLitOpenPBRAdapter.hlsl"));
            var materialSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "ShaderPass",
                "ReferencedPathtracing.hlsl"));
            var gBufferSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "ShaderPass",
                "RaytracingGBuffer.hlsl"));
            var commonSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingCommon.hlsl"));
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));
            var subsurfaceSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracingSubsurface.hlsl"));

            Assert.That(
                adapterSource,
                Does.Contain("inputs.subsurface_weight = saturate("));
            Assert.That(
                adapterSource,
                Does.Contain("inputs.subsurface_radius_scale = max("));
            Assert.That(
                adapterSource,
                Does.Contain("supportsHybridSubsurface"));
            Assert.That(
                adapterSource,
                Does.Contain("surfaceIsOpaque"));
            Assert.That(
                materialSource,
                Does.Contain("transportInputs.subsurface_weight = 0.0;"));
            Assert.That(
                materialSource,
                Does.Contain(
                    "REFERENCED_PATHTRACING_QUERY_SUBSURFACE_SURFACE"));
            Assert.That(
                materialSource,
                Does.Contain("result.surfaceInstanceIndex = InstanceIndex();"));
            Assert.That(
                materialSource,
                Does.Not.Contain("GeometryIndex()"));
            Assert.That(
                gBufferSource,
                Does.Contain("material.subsurfaceAlbedo"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "#define REFERENCED_PATHTRACING_PAYLOAD_UINT4_COUNT 10"));
            Assert.That(
                commonSource,
                Does.Contain("#define REFERENCED_PAYLOAD_INPUT_QUERY_MODE 22u"));
            Assert.That(
                commonSource,
                Does.Contain(
                    "#define REFERENCED_PAYLOAD_RESULT_SUBSURFACE_WEIGHT 33u"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("ReferencedPathtracingSampleBurleySubsurface("));
            Assert.That(
                rayGenerationSource,
                Does.Contain("RAY_FLAG_CULL_BACK_FACING_TRIANGLES"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("matchesSubsurfaceSurface"));
            Assert.That(
                subsurfaceSource,
                Does.Contain("ReferencedPathtracingBurleyDiffusionShape("));
            Assert.That(
                subsurfaceSource,
                Does.Contain("0.25 * diffusionRate"));
        }

        [Test]
        public void SampleSequence_StopsAtTargetAndRestartsWhenTargetChanges()
        {
            var gameObject =
                new GameObject("ReferencedPathTracingFiniteSequenceTests.Camera");
            var camera = gameObject.AddComponent<Camera>();

            try
            {
                var first = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    200,
                    21ul,
                    true,
                    2u);
                var second = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    201,
                    21ul,
                    false,
                    2u);
                var converged = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    202,
                    21ul,
                    false,
                    2u);
                var remainsConverged = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    203,
                    21ul,
                    false,
                    2u);
                var changedTarget = ReferencedPathTracingSampleSequence.Resolve(
                    camera,
                    204,
                    21ul,
                    false,
                    3u);

                Assert.That(first, Is.Zero);
                Assert.That(second, Is.EqualTo(1u));
                Assert.That(converged, Is.EqualTo(2u));
                Assert.That(remainsConverged, Is.EqualTo(2u));
                Assert.That(changedTarget, Is.Zero);
            }
            finally
            {
                ReferencedPathTracingSampleSequence.Dispose();
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SceneSignature_TracksMeshletRendererTransformChanges()
        {
            var rendererData = new[]
            {
                new VividMeshletRendererRenderData
                {
                    meshletRendererEntityId = EntityId.FromULong(1ul),
                    sourceMeshEntityId = EntityId.FromULong(2ul),
                    objectToWorldMatrix = Matrix4x4.identity,
                    renderingLayerMask = uint.MaxValue,
                    flags = VividMeshletRendererFlags.ActiveInHierarchy
                        | VividMeshletRendererFlags.Enabled
                        | VividMeshletRendererFlags.Valid,
                    shadowCastingMode = ShadowCastingMode.On,
                    subMeshCount = 1
                }
            };
            var rendererResources = new[]
            {
                new VividMeshletRendererResources(
                    null,
                    null,
                    null,
                    null,
                    null,
                    null)
            };

            var original = ReferencedPathTracingSceneSignatureUtility.Compute(
                rendererData,
                rendererResources);
            rendererData[0].objectToWorldMatrix =
                Matrix4x4.Translate(Vector3.right);
            var moved = ReferencedPathTracingSceneSignatureUtility.Compute(
                rendererData,
                rendererResources);

            Assert.That(moved, Is.Not.EqualTo(original));
        }

        [Test]
        public void SceneSignature_TracksStandardRendererTransformChanges()
        {
            var gameObject = new GameObject(
                "ReferencedPathTracingSceneSignatureTests.Renderer");
            var mesh = new Mesh();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();

            try
            {
                var original =
                    ReferencedPathTracingSceneSignatureUtility.Compute(
                        new Renderer[] { renderer });
                gameObject.transform.position = Vector3.right;
                var moved =
                    ReferencedPathTracingSceneSignatureUtility.Compute(
                        new Renderer[] { renderer });

                Assert.That(moved, Is.Not.EqualTo(original));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SceneSignature_TracksFaceSubsurfaceMaterialChanges()
        {
            var gameObject = new GameObject(
                "ReferencedPathTracingSceneSignatureTests.FaceSSS");
            var mesh = new Mesh();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            var shader = Shader.Find("VividRP/Material/StandardLit");
            Material material = null;

            try
            {
                Assert.That(shader, Is.Not.Null);
                material = new Material(shader);
                renderer.sharedMaterial = material;
                var original =
                    ReferencedPathTracingSceneSignatureUtility.Compute(
                        new Renderer[] { renderer });
                material.SetFloat("_SubsurfaceWeight", 1.0f);
                var changed =
                    ReferencedPathTracingSceneSignatureUtility.Compute(
                        new Renderer[] { renderer });
                material.SetFloat("_SubsurfaceTransmissionWeight", 1.0f);
                var transmissionChanged =
                    ReferencedPathTracingSceneSignatureUtility.Compute(
                        new Renderer[] { renderer });

                Assert.That(changed, Is.Not.EqualTo(original));
                Assert.That(
                    transmissionChanged,
                    Is.Not.EqualTo(changed));
            }
            finally
            {
                if (material != null)
                    UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(gameObject);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void V1FreezeGate_RequiresEveryCanonicalCaseAndPassedGpuEvidence()
        {
            var captures = ReferencedPathTracingV1Corpus.Cases
                .Select(CreateValidFrozenCapture)
                .ToArray();

            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCorpus(
                    captures,
                    out var failure),
                Is.True,
                failure);

            captures[0].validation.status =
                ReferencedPathTracingValidationStatus.NotRun;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCorpus(
                    captures,
                    out failure),
                Is.False);
            Assert.That(failure, Does.Contain("GPU validation evidence"));

            captures[0].validation.status =
                ReferencedPathTracingValidationStatus.Passed;
            captures[0].transportConformance.status =
                ReferencedPathTracingValidationStatus.NotRun;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCorpus(
                    captures,
                    out failure),
                Is.False);
            Assert.That(failure, Does.Contain("Transport conformance"));

            captures[0].transportConformance.status =
                ReferencedPathTracingValidationStatus.Passed;
            captures[0]
                .transportConformance
                .lightProposalMeasurements[1]
                .globalProposalProbability = 0.5f;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCorpus(
                    captures,
                    out failure),
                Is.False);
            Assert.That(
                failure,
                Does.Contain("does not match the capture settings"));
        }

        [Test]
        public void V1FreezeGate_RejectsReGIRPreExposureAndWrongCameraVisibility()
        {
            var corpusCase = ReferencedPathTracingV1Corpus.Cases.Single(
                item => item.id == "hdri-camera-hidden-lighting");
            var capture = CreateValidFrozenCapture(corpusCase);

            capture.usesReGIR = true;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out _),
                Is.False);

            capture.usesReGIR = false;
            capture.rawRadianceIsPreExposed = true;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out _),
                Is.False);

            capture.rawRadianceIsPreExposed = false;
            capture.environment.cameraVisible = true;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out _),
                Is.False);

            capture.environment.cameraVisible = false;
            capture.transportDebugMode =
                ReferencedPathTracingTransportDebugMode.NeePdfs;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out _),
                Is.False);

            capture.transportDebugMode =
                ReferencedPathTracingTransportDebugMode.Combined;
            capture.environment.debugMode =
                ReferencedPathTracingEnvironmentDebugMode.IndirectMissOnly;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out _),
                Is.False);

            capture.environment.debugMode =
                ReferencedPathTracingEnvironmentDebugMode.Combined;
            capture.usesShadingPointLightSelection = false;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out _),
                Is.False);

            capture.usesShadingPointLightSelection = true;
            capture.globalLightProposalProbability = 0.5f;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out _),
                Is.False);

            capture.globalLightProposalProbability =
                corpusCase.globalLightProposalProbability;
            capture.usesLightSpatialIndex = false;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out _),
                Is.False);
        }

        [Test]
        public void V1FreezeGate_RejectsNonCanonicalPathSamplingContract()
        {
            var corpusCase = ReferencedPathTracingV1Corpus.Cases[0];
            var capture = CreateValidFrozenCapture(corpusCase);

            capture.pathSamplingMode =
                ReferencedPathTracingSamplingMode.IndexedHash;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out var fallbackFailure),
                Is.False);
            Assert.That(
                fallbackFailure,
                Does.Contain("Path sampling contract"));

            capture.pathSamplingMode = corpusCase.pathSamplingMode;
            capture.samplingContractVersion = 0;
            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out var versionFailure),
                Is.False);
            Assert.That(
                versionFailure,
                Does.Contain("Path sampling contract"));
        }

        [Test]
        public void V1FreezeGate_RejectsReferenceAtmosphereMode()
        {
            var corpusCase = ReferencedPathTracingV1Corpus.Cases[0];
            var capture = CreateValidFrozenCapture(corpusCase);
            capture.environment.mode =
                ReferencedPathTracingEnvironmentMode.ReferenceAtmosphere;
            capture.environment.atmosphere =
                new ReferencedPathTracingAtmosphereMetadata
                {
                    contractVersion =
                        ReferencedPathTracingAtmosphereState.ContractVersion,
                    active = true
                };

            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out var failure),
                Is.False);
            Assert.That(failure, Does.Contain("HDRI environment contract"));
        }

        [Test]
        public void V1FreezeGate_RejectsPhysicalCameraDofCapture()
        {
            var capture = CreateValidFrozenCapture(
                ReferencedPathTracingV1Corpus.Cases[0]);
            capture.usesPhysicalCameraDof = true;

            Assert.That(
                ReferencedPathTracingV1FreezeGate
                    .ValidateCaptureContract(
                        capture,
                        out var failure),
                Is.False);
            Assert.That(failure, Does.Contain("pinhole camera"));
        }

        [Test]
        public void V1FreezeGate_RejectsNonCanonicalShadingNormalContract()
        {
            var corpusCase = ReferencedPathTracingV1Corpus.Cases[0];
            var capture = CreateValidFrozenCapture(corpusCase);
            capture.shadingNormalContractVersion = 0;

            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out var failure),
                Is.False);
            Assert.That(
                failure,
                Does.Contain("Shading-normal transport contract"));
        }

        [Test]
        public void V1FreezeGate_RejectsNonCanonicalThinWalledTransmissionContract()
        {
            var corpusCase = ReferencedPathTracingV1Corpus.Cases[0];
            var capture = CreateValidFrozenCapture(corpusCase);
            capture.thinWalledTransmissionContractVersion = 0;

            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out var failure),
                Is.False);
            Assert.That(
                failure,
                Does.Contain("Thin-walled transmission contract"));
        }

        [Test]
        public void V1FreezeGate_RejectsNonCanonicalGeometryOpacityContract()
        {
            var corpusCase = ReferencedPathTracingV1Corpus.Cases[0];
            var capture = CreateValidFrozenCapture(corpusCase);
            capture.coloredOpacityContractVersion = 0;

            Assert.That(
                ReferencedPathTracingV1FreezeGate.ValidateCaptureContract(
                    capture,
                    out var failure),
                Is.False);
            Assert.That(
                failure,
                Does.Contain("Geometry-opacity transport contract"));
        }

        [Test]
        public void EnvironmentState_DisablesUnsupportedSkyAndSanitizesInvalidValues()
        {
            var cubemap = new Cubemap(1, TextureFormat.RGBAHalf, false);
            var settings = ScriptableObject.CreateInstance<ReferencedPathTracingSettingsVolume>();

            try
            {
                settings.active = true;
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.PhysicallyBased,
                    specularCubemap = cubemap,
                    tint = new Color(float.NaN, -1.0f, float.PositiveInfinity, 1.0f),
                    exposure = float.PositiveInfinity,
                    rotation = float.NaN,
                    skyHash = 99
                };

                var state = ReferencedPathTracingEnvironmentState.Resolve(skyData, settings);

                Assert.That(state.hasHdri, Is.False);
                Assert.That(state.lightingEnabled, Is.False);
                Assert.That(state.cameraVisible, Is.False);
                Assert.That(state.importanceSamplingEnabled, Is.False);
                Assert.That(state.neeEnabled, Is.False);
                Assert.That(state.tint, Is.EqualTo(Color.white));
                Assert.That(state.intensityMultiplier, Is.Zero);
                Assert.That(state.rotation, Is.Zero);
                Assert.That(state.skyHash, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void EnvironmentState_SignatureTracksSkyAndPathTracingSettings()
        {
            var cubemap = new Cubemap(1, TextureFormat.RGBAHalf, false);
            var replacementCubemap = new Cubemap(1, TextureFormat.RGBAHalf, false);
            var settings = ScriptableObject.CreateInstance<ReferencedPathTracingSettingsVolume>();

            try
            {
                settings.active = true;
                var skyData = new VividSkyData
                {
                    activeSkyType = SkyType.HDRI,
                    specularCubemap = cubemap,
                    tint = Color.white,
                    exposure = 1.0f,
                    rotation = 0.0f,
                    skyHash = 42
                };

                var original = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                skyData.skyContentHash = original.contentHash + 1;
                var contentChanged = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                skyData.skyContentHash = original.contentHash;
                skyData.skyHash = 43;
                var nonContentSkyStateChanged =
                    ReferencedPathTracingEnvironmentState.Resolve(
                        skyData,
                        settings);
                skyData.skyHash = 42;
                skyData.rotation = 30.0f;
                var rotated = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                skyData.rotation = 0.0f;
                skyData.tint = new Color(0.5f, 1.0f, 1.0f, 1.0f);
                var tinted = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                skyData.tint = Color.white;
                skyData.exposure = 2.0f;
                var intensified = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                skyData.exposure = 1.0f;
                settings.environmentCameraVisible.value = false;
                var hidden = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentCameraVisible.value = true;
                settings.environmentEstimatorMode.value =
                    ReferencedPathTracingEnvironmentEstimatorMode.LightOnly;
                var lightOnly = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentEstimatorMode.value =
                    ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly;
                var estimatorBsdfOnly = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentEstimatorMode.value =
                    ReferencedPathTracingEnvironmentEstimatorMode.Mis;
                settings.environmentSamplingMode.value =
                    ReferencedPathTracingEnvironmentSamplingMode.BsdfOnly;
                var bsdfOnly = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentSamplingMode.value =
                    ReferencedPathTracingEnvironmentSamplingMode.UniformSphere;
                var uniformSphere = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);
                settings.environmentSamplingMode.value =
                    ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling;
                skyData.specularCubemap = replacementCubemap;
                var replacement = ReferencedPathTracingEnvironmentState.Resolve(
                    skyData,
                    settings);

                Assert.That(rotated.signature, Is.Not.EqualTo(original.signature));
                Assert.That(
                    contentChanged.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    contentChanged.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(
                    nonContentSkyStateChanged.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    nonContentSkyStateChanged.samplingSignature,
                    Is.EqualTo(original.samplingSignature));
                Assert.That(
                    rotated.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(
                    tinted.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(
                    intensified.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(hidden.signature, Is.Not.EqualTo(original.signature));
                Assert.That(
                    hidden.samplingSignature,
                    Is.EqualTo(original.samplingSignature));
                Assert.That(lightOnly.signature, Is.Not.EqualTo(original.signature));
                Assert.That(
                    lightOnly.samplingSignature,
                    Is.EqualTo(original.samplingSignature));
                Assert.That(
                    estimatorBsdfOnly.signature,
                    Is.Not.EqualTo(original.signature));
                Assert.That(
                    estimatorBsdfOnly.samplingSignature,
                    Is.EqualTo(original.samplingSignature));
                Assert.That(bsdfOnly.signature, Is.Not.EqualTo(original.signature));
                Assert.That(
                    bsdfOnly.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(replacement.signature, Is.Not.EqualTo(original.signature));
                Assert.That(
                    replacement.samplingSignature,
                    Is.Not.EqualTo(original.samplingSignature));
                Assert.That(bsdfOnly.importanceSamplingEnabled, Is.False);
                Assert.That(bsdfOnly.neeEnabled, Is.False);
                Assert.That(bsdfOnly.lightingEnabled, Is.True);
                Assert.That(lightOnly.neeEnabled, Is.True);
                Assert.That(estimatorBsdfOnly.neeEnabled, Is.False);
                Assert.That(uniformSphere.importanceSamplingEnabled, Is.False);
                Assert.That(uniformSphere.neeEnabled, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(replacementCubemap);
                UnityEngine.Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void CameraBackgroundState_TracksSkyModeAndSceneLinearClearColor()
        {
            var cameraObject = new GameObject("Reference PT Camera Background Test");
            var camera = cameraObject.AddComponent<Camera>();

            try
            {
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.backgroundColor = new Color(0.5f, 0.25f, 0.75f, 0.4f);
                var cameraData = new VividCameraData
                {
                    camera = camera
                };

                var sky = ReferencedPathTracingCameraBackgroundState.Resolve(cameraData);
                camera.clearFlags = CameraClearFlags.SolidColor;
                var solidColor =
                    ReferencedPathTracingCameraBackgroundState.Resolve(cameraData);
                camera.backgroundColor = new Color(0.25f, 0.5f, 0.75f, 0.8f);
                var changedColor =
                    ReferencedPathTracingCameraBackgroundState.Resolve(cameraData);

                Assert.That(sky.skyRequested, Is.True);
                Assert.That(
                    sky.clearColor.r,
                    Is.EqualTo(Mathf.GammaToLinearSpace(0.5f)).Within(1e-6f));
                Assert.That(
                    sky.clearColor.g,
                    Is.EqualTo(Mathf.GammaToLinearSpace(0.25f)).Within(1e-6f));
                Assert.That(
                    sky.clearColor.b,
                    Is.EqualTo(Mathf.GammaToLinearSpace(0.75f)).Within(1e-6f));
                Assert.That(sky.clearColor.a, Is.EqualTo(0.4f).Within(1e-6f));
                Assert.That(solidColor.skyRequested, Is.False);
                Assert.That(solidColor.signature, Is.Not.EqualTo(sky.signature));
                Assert.That(changedColor.signature, Is.Not.EqualTo(solidColor.signature));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void StandardLitShader_DeclaresReferencedPathtracingDxrPass()
        {
            var shader = Shader.Find("VividRP/Material/StandardLit");
            Assert.That(shader, Is.Not.Null);

            var material = new Material(shader);
            try
            {
                var passIndex = material.FindPass(ReferencedPathTracingPass.MaterialShaderPassName);

                Assert.That(passIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    shader.FindPassTagValue(passIndex, new ShaderTagId("LightMode")),
                    Is.EqualTo(new ShaderTagId(ReferencedPathTracingPass.MaterialShaderPassName)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static T GetField<T>(ReferencedPathTracingPass pass, string fieldName)
        {
            var field = typeof(ReferencedPathTracingPass).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(ReferencedPathTracingPass).Assembly);

            Assert.That(packageInfo, Is.Not.Null);
            return Path.Combine(packageInfo.resolvedPath, Path.Combine(relativeParts));
        }

        private static ReferencedPathTracingCaptureMetadata
            CreateValidFrozenCapture(
                ReferencedPathTracingV1CorpusCase corpusCase)
        {
            return new ReferencedPathTracingCaptureMetadata
            {
                freezeContractVersion =
                    ReferencedPathTracingV1FreezeGate.ContractVersion,
                corpusVersion = ReferencedPathTracingV1Corpus.Version,
                integratorVersion =
                    ReferencedPathTracingIntegratorState.Version,
                corpusCaseId = corpusCase.id,
                width = corpusCase.width,
                height = corpusCase.height,
                targetSampleCount = corpusCase.targetSampleCount,
                accumulatedSampleCount =
                    (ulong)corpusCase.targetSampleCount,
                deterministicSampling = true,
                fixedSeed = corpusCase.fixedSeed,
                pathSamplingMode = corpusCase.pathSamplingMode,
                samplingContractVersion =
                    ReferencedPathTracingSamplingContract.Version,
                physicalCameraContractVersion =
                    ReferencedPathTracingPhysicalCameraState.Version,
                usesPhysicalCameraDof = false,
                shadingNormalContractVersion =
                    ReferencedPathTracingShadingNormalContract.Version,
                thinWalledTransmissionContractVersion =
                    ReferencedPathTracingThinWalledTransmissionContract
                        .Version,
                coloredOpacityContractVersion =
                    ReferencedPathTracingGeometryOpacityContract.Version,
                maxBounceCount = corpusCase.maxBounceCount,
                russianRouletteStartBounce =
                    corpusCase.russianRouletteStartBounce,
                integratorSignature = 1ul,
                estimatorMode = corpusCase.estimatorMode,
                transportDebugMode =
                    ReferencedPathTracingTransportDebugMode.Combined,
                usesShadingPointLightSelection =
                    corpusCase.usesShadingPointLightSelection,
                globalLightProposalProbability =
                    corpusCase.globalLightProposalProbability,
                usesLightSpatialIndex =
                    corpusCase.usesLightSpatialIndex,
                lightSpatialIndexVersion =
                    (int)ReferencedPathTracingLightSpatialIndexBuilder.Version,
                lightSpatialIndexResolution =
                    ReferencedPathTracingLightSpatialIndexBuilder
                        .GridResolution,
                lightSpatialIndexCellCapacity =
                    ReferencedPathTracingLightSpatialIndexBuilder
                        .CellCapacity,
                usesReGIR = false,
                usesDenoiser = false,
                usesRasterGI = false,
                rawRadianceIsPreExposed = false,
                hasMainDirectionalLight = false,
                localLightCount = 0,
                unsupportedMaterialCount = 0,
                standardLitOnly = true,
                imageOriginBottomLeft = true,
                environment =
                    new ReferencedPathTracingEnvironmentMetadata
                    {
                        contractVersion =
                            ReferencedPathTracingEnvironmentMetadata.ContractVersion,
                        mode = ReferencedPathTracingEnvironmentMode.Hdri,
                        contentHash = 1,
                        backgroundResolution = 1024,
                        lightingResolution = 256,
                        lightingEnabled = true,
                        cameraVisible =
                            corpusCase.id
                            != "hdri-camera-hidden-lighting",
                        samplingMode = corpusCase.samplingMode,
                        estimatorMode = corpusCase.estimatorMode,
                        debugMode =
                            ReferencedPathTracingEnvironmentDebugMode.Combined,
                        physicalIntensityMultiplier = 1.0f,
                        pdfVersion =
                            ReferencedPathTracingEnvironmentImportanceLayout.Version,
                        rawRadianceIsPreExposed = false
                    },
                validation = new ReferencedPathTracingValidationEvidence
                {
                    status = ReferencedPathTracingValidationStatus.Passed,
                    graphicsApi = "Direct3D12",
                    deviceName = "Canonical Test Device",
                    referenceImageSha256 = new string('a', 64),
                    finitePixelFraction = 1.0f,
                    negativeRadianceFraction = 0.0f,
                    relativeMeanError = 0.01f
                },
                transportConformance =
                    new ReferencedPathTracingTransportConformanceEvidence
                {
                    status = ReferencedPathTracingValidationStatus.Passed,
                    estimatorMeasurements = new[]
                    {
                        CreateEstimatorMeasurement(
                            ReferencedPathTracingEnvironmentEstimatorMode.Mis,
                            1.0f),
                        CreateEstimatorMeasurement(
                            ReferencedPathTracingEnvironmentEstimatorMode.LightOnly,
                            1.005f),
                        CreateEstimatorMeasurement(
                            ReferencedPathTracingEnvironmentEstimatorMode.BsdfOnly,
                            0.995f)
                    },
                    lightProposalMeasurements = new[]
                    {
                        CreateLightProposalMeasurement(
                            false,
                            1.0f,
                            1.0f,
                            0.4f),
                        CreateLightProposalMeasurement(
                            true,
                            corpusCase.globalLightProposalProbability,
                            1.005f,
                            0.25f)
                    },
                    lightSelection =
                        new ReferencedPathTracingLightSelectionEvidence
                    {
                        sampleCount = 10000,
                        declaredSelectionPdfs =
                            new[] { 0.25f, 0.75f },
                        observedSelectionCounts =
                            new[] { 2500, 7500 }
                    },
                    pdfConsistency =
                        new ReferencedPathTracingPdfConsistencyEvidence
                    {
                        comparisonCount = 10000,
                        nonFiniteCount = 0,
                        maximumRelativeError = 1e-6f
                    }
                }
            };
        }

        private static ReferencedPathTracingLightProposalMeasurement
            CreateLightProposalMeasurement(
                bool enabled,
                float globalProposalProbability,
                float meanLuminance,
                float luminanceVariance)
        {
            return new ReferencedPathTracingLightProposalMeasurement
            {
                shadingPointSelectionEnabled = enabled,
                globalProposalProbability = globalProposalProbability,
                sampleCount = 4096,
                meanLuminance = meanLuminance,
                standardError = 0.005f,
                luminanceVariance = luminanceVariance,
                finitePixelFraction = 1.0f,
                negativeRadianceFraction = 0.0f
            };
        }

        private static ReferencedPathTracingEstimatorMeasurement
            CreateEstimatorMeasurement(
                ReferencedPathTracingEnvironmentEstimatorMode mode,
                float meanLuminance)
        {
            return new ReferencedPathTracingEstimatorMeasurement
            {
                estimatorMode = mode,
                sampleCount = 4096,
                meanLuminance = meanLuminance,
                standardError = 0.005f,
                finitePixelFraction = 1.0f,
                negativeRadianceFraction = 0.0f
            };
        }
    }
}
