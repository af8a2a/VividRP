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
    public sealed class VisibilityBufferGBufferResolvePassTests
    {
        [Test]
        public void Initialize_RegistersVisibilityInputsAndSurfaceSummaryOutputs()
        {
            IRenderPass renderPass = new VisibilityBufferGBufferResolvePass();

            var resources = renderPass.Initialize();
            var visibilityEntry = resources.Textures.Single(entry => entry.Name == "VisibilityBuffer");
            var attributes0Entry = resources.Textures.Single(entry => entry.Name == "VisibilityBufferAttributes0");
            var attributes1Entry = resources.Textures.Single(entry => entry.Name == "VisibilityBufferAttributes1");
            var barycentricsEntry = resources.Textures.Single(entry => entry.Name == "VisibilityBufferBarycentrics");
            var gbuffer0Entry = resources.Textures.Single(entry => entry.Name == "GBuffer0");
            var gbuffer1Entry = resources.Textures.Single(entry => entry.Name == "GBuffer1");
            var gbuffer2Entry = resources.Textures.Single(entry => entry.Name == "GBuffer2");
            var gbuffer3Entry = resources.Textures.Single(entry => entry.Name == "GBuffer3");
            var diffuseIrradianceEntry = resources.Textures.Single(
                entry => entry.Name == "DiffuseIrradiance");
            var layerAux0Entry = resources.Textures.Single(
                entry => entry.Name == "LayerAux0");
            var layerAux1Entry = resources.Textures.Single(
                entry => entry.Name == "LayerAux1");
            var sidecarTileListEntry = resources.Buffers.Single(
                entry => entry.Name == "DualSlabSidecarTileList");
            var sidecarIndirectArgsEntry = resources.Buffers.Single(
                entry => entry.Name == "DualSlabSidecarIndirectDrawArgs");

            Assert.That(resources.Textures, Has.Length.EqualTo(11));
            Assert.That(resources.Buffers, Has.Length.EqualTo(2));
            Assert.That(visibilityEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(visibilityEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32_UInt));
            Assert.That(attributes0Entry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                attributes0Entry.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(attributes1Entry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                attributes1Entry.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(barycentricsEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                barycentricsEntry.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16_SFloat));
            Assert.That(gbuffer0Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer0Entry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(gbuffer0Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));
            Assert.That(gbuffer1Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer1Entry.AttachmentIndex, Is.EqualTo(1));
            Assert.That(gbuffer1Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.A2B10G10R10_UNormPack32));
            Assert.That(gbuffer2Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer2Entry.AttachmentIndex, Is.EqualTo(2));
            Assert.That(gbuffer2Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(gbuffer3Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer3Entry.AttachmentIndex, Is.EqualTo(3));
            Assert.That(gbuffer3Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(diffuseIrradianceEntry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(diffuseIrradianceEntry.AttachmentIndex, Is.EqualTo(4));
            Assert.That(
                diffuseIrradianceEntry.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(layerAux0Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(layerAux0Entry.AttachmentIndex, Is.EqualTo(5));
            Assert.That(
                layerAux0Entry.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));
            Assert.That(layerAux1Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(layerAux1Entry.AttachmentIndex, Is.EqualTo(6));
            Assert.That(
                layerAux1Entry.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(gbuffer0Entry.Texture.desc.ClearBuffer, Is.True);
            Assert.That(gbuffer1Entry.Texture.desc.ClearBuffer, Is.True);
            Assert.That(gbuffer2Entry.Texture.desc.ClearBuffer, Is.True);
            Assert.That(gbuffer3Entry.Texture.desc.ClearBuffer, Is.True);
            Assert.That(diffuseIrradianceEntry.Texture.desc.ClearBuffer, Is.True);
            Assert.That(layerAux0Entry.Texture.desc.ClearBuffer, Is.True);
            Assert.That(layerAux1Entry.Texture.desc.ClearBuffer, Is.True);
            Assert.That(gbuffer0Entry.Texture.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(gbuffer1Entry.Texture.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(gbuffer2Entry.Texture.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(gbuffer3Entry.Texture.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(
                diffuseIrradianceEntry.Texture.desc.ClearColor,
                Is.EqualTo(Color.clear));
            Assert.That(layerAux0Entry.Texture.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(layerAux1Entry.Texture.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(sidecarTileListEntry.IsTransient, Is.True);
            Assert.That(sidecarTileListEntry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(
                sidecarTileListEntry.Buffer.desc.Target,
                Is.EqualTo(GraphicsBuffer.Target.Structured));
            Assert.That(sidecarIndirectArgsEntry.IsTransient, Is.True);
            Assert.That(sidecarIndirectArgsEntry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(
                sidecarIndirectArgsEntry.Buffer.desc.Target,
                Is.EqualTo(
                    GraphicsBuffer.Target.Structured
                    | GraphicsBuffer.Target.IndirectArguments));
        }

        [Test]
        public void Prepare_UsesVisibilityTextureSize_WhenConfigured()
        {
            var pass = new VisibilityBufferGBufferResolvePass();
            var visibilityTexture = GetTextureField(pass, "m_VisibilityBuffer");
            var gbuffer0Texture = GetTextureField(pass, "m_GBuffer0");
            var diffuseIrradianceTexture = GetTextureField(
                pass,
                "m_GBuffer4");
            var layerAux0Texture = GetTextureField(pass, "m_LayerAux0");
            var layerAux1Texture = GetTextureField(pass, "m_LayerAux1");

            visibilityTexture.desc.Width = 1600;
            visibilityTexture.desc.Height = 900;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;
            frameData.GetOrCreate<VividGPUDrivenFrameData>()
                .requiresDualSlabSidecar = true;

            pass.Prepare(frameData);

            Assert.That(gbuffer0Texture.desc.Width, Is.EqualTo(1600));
            Assert.That(gbuffer0Texture.desc.Height, Is.EqualTo(900));
            Assert.That(gbuffer0Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));
            Assert.That(gbuffer0Texture.desc.ClearBuffer, Is.True);
            Assert.That(gbuffer0Texture.desc.ClearColor, Is.EqualTo(Color.clear));
            Assert.That(diffuseIrradianceTexture.desc.Width, Is.EqualTo(1600));
            Assert.That(diffuseIrradianceTexture.desc.Height, Is.EqualTo(900));
            Assert.That(
                diffuseIrradianceTexture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(diffuseIrradianceTexture.desc.ClearBuffer, Is.True);
            Assert.That(
                diffuseIrradianceTexture.desc.ClearColor,
                Is.EqualTo(Color.clear));
            Assert.That(layerAux0Texture.desc.Width, Is.EqualTo(1600));
            Assert.That(layerAux0Texture.desc.Height, Is.EqualTo(900));
            Assert.That(
                layerAux0Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));
            Assert.That(layerAux1Texture.desc.Width, Is.EqualTo(1600));
            Assert.That(layerAux1Texture.desc.Height, Is.EqualTo(900));
            Assert.That(
                layerAux1Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
        }

        [Test]
        public void Prepare_AdaptsDefaultDualSlabSidecarAcrossFrames()
        {
            var pass = new VisibilityBufferGBufferResolvePass();
            var layerAux0Texture = GetTextureField(pass, "m_LayerAux0");
            var layerAux1Texture = GetTextureField(pass, "m_LayerAux1");
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var gpuDrivenFrameData =
                frameData.GetOrCreate<VividGPUDrivenFrameData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(layerAux0Texture.desc.Width, Is.EqualTo(1));
            Assert.That(layerAux0Texture.desc.Height, Is.EqualTo(1));
            Assert.That(layerAux1Texture.desc.Width, Is.EqualTo(1));
            Assert.That(layerAux1Texture.desc.Height, Is.EqualTo(1));

            gpuDrivenFrameData.requiresDualSlabSidecar = true;
            pass.Prepare(frameData);

            Assert.That(layerAux0Texture.desc.Width, Is.EqualTo(1920));
            Assert.That(layerAux0Texture.desc.Height, Is.EqualTo(1080));
            Assert.That(layerAux1Texture.desc.Width, Is.EqualTo(1920));
            Assert.That(layerAux1Texture.desc.Height, Is.EqualTo(1080));

            gpuDrivenFrameData.requiresDualSlabSidecar = false;
            pass.Prepare(frameData);

            Assert.That(layerAux0Texture.desc.Width, Is.EqualTo(1));
            Assert.That(layerAux0Texture.desc.Height, Is.EqualTo(1));
            Assert.That(layerAux1Texture.desc.Width, Is.EqualTo(1));
            Assert.That(layerAux1Texture.desc.Height, Is.EqualTo(1));
        }

        [Test]
        public void Prepare_ResizesDualSlabSidecarTileScratchForResolveExtent()
        {
            var pass = new VisibilityBufferGBufferResolvePass();
            var visibilityTexture = GetTextureField(pass, "m_VisibilityBuffer");
            var tileList = GetBufferField(
                pass,
                "m_DualSlabSidecarTileList");
            var indirectArgs = GetBufferField(
                pass,
                "m_DualSlabSidecarIndirectDrawArgs");
            visibilityTexture.desc.Width = 17;
            visibilityTexture.desc.Height = 9;

            var frameData = new ContextContainer();
            frameData.GetOrCreate<VividGPUDrivenFrameData>()
                .requiresDualSlabSidecar = true;

            pass.Prepare(frameData);

            Assert.That(
                tileList.desc.Count,
                Is.EqualTo(6),
                "17x9 at 8x8 must allocate a 3x2 tile list.");
            Assert.That(tileList.desc.Stride, Is.EqualTo(sizeof(uint)));
            Assert.That(indirectArgs.desc.Count, Is.EqualTo(4));
            Assert.That(indirectArgs.desc.Stride, Is.EqualTo(sizeof(uint)));
            Assert.That(
                indirectArgs.desc.Target,
                Is.EqualTo(
                    GraphicsBuffer.Target.Structured
                    | GraphicsBuffer.Target.IndirectArguments));
        }

        [Test]
        public void Prepare_DoesNotAllocate_WhenFrameDataIsStable()
        {
            var pass = new VisibilityBufferGBufferResolvePass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 32; index++)
                pass.Prepare(frameData);

            var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void Prepare_DoesNotOverwriteOverriddenGBufferDescriptors()
        {
            var pass = new VisibilityBufferGBufferResolvePass();
            var externalGBuffer0 = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 320,
                    Height = 240,
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                }
            };

            SetTextureField(pass, "m_GBuffer0", externalGBuffer0);

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(externalGBuffer0.desc.Width, Is.EqualTo(320));
            Assert.That(externalGBuffer0.desc.Height, Is.EqualTo(240));
            Assert.That(externalGBuffer0.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void Prepare_LeavesOverriddenDualSlabSidecarOwnerManaged()
        {
            var pass = new VisibilityBufferGBufferResolvePass();
            var externalLayerAux0 = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 320,
                    Height = 240,
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                }
            };
            var externalLayerAux1 = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 640,
                    Height = 360,
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                }
            };

            SetTextureField(pass, "m_LayerAux0", externalLayerAux0);
            SetTextureField(pass, "m_LayerAux1", externalLayerAux1);

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(externalLayerAux0.desc.Width, Is.EqualTo(320));
            Assert.That(externalLayerAux0.desc.Height, Is.EqualTo(240));
            Assert.That(externalLayerAux1.desc.Width, Is.EqualTo(640));
            Assert.That(externalLayerAux1.desc.Height, Is.EqualTo(360));
        }

        [Test]
        public void ResolveShader_ReconstructsBarycentricsAndVisibilityUvDerivatives()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(VisibilityBufferGBufferResolvePass).Assembly);
            Assert.That(package, Is.Not.Null);
            string path = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "VisibilityBufferGBufferResolve.shader");

            Assert.That(File.Exists(path), Is.True, path);
            string source = File.ReadAllText(path);
            string compactSource = string.Concat(
                source.Where(character => !char.IsWhiteSpace(character)));

            StringAssert.Contains("_VisibilityBufferBarycentrics", source);
            StringAssert.Contains("_VisibilityBufferAttributes0", source);
            StringAssert.Contains("_VisibilityBufferAttributes1", source);
            StringAssert.Contains("CalculateFullBarycentric(", source);
            StringAssert.Contains("InterpolateUV(", source);
            StringAssert.Contains("TransformWorldToHClip(", source);
            StringAssert.DoesNotContain(
                "SAMPLE_TEXTURE2D_LOD(_VisibilityBufferAttributes0,",
                compactSource);
            StringAssert.DoesNotContain(
                "SAMPLE_TEXTURE2D_LOD(_VisibilityBufferAttributes1,",
                compactSource);
            StringAssert.DoesNotContain(
                "SAMPLE_TEXTURE2D_LOD(_VisibilityBufferBarycentrics,",
                compactSource);

            string barycentricPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividBarycentric.hlsl");
            Assert.That(File.Exists(barycentricPath), Is.True, barycentricPath);
            string barycentricSource = File.ReadAllText(barycentricPath);
            StringAssert.Contains("pixelStepNDC.y *= -1.0f;", barycentricSource);
            StringAssert.Contains("result.ddy *= pixelStepNDC.y;", barycentricSource);
        }

        [Test]
        public void ResolveShader_ExportsSurfaceSummaryAndFailsUnsupportedProgramsClosed()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(VisibilityBufferGBufferResolvePass).Assembly);
            Assert.That(package, Is.Not.Null);
            string path = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "VisibilityBufferGBufferResolve.shader");

            Assert.That(File.Exists(path), Is.True, path);
            string source = File.ReadAllText(path);
            string surfaceProgramPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividMaterialSurface.hlsl");
            Assert.That(File.Exists(surfaceProgramPath), Is.True, surfaceProgramPath);
            string surfaceProgramSource = File.ReadAllText(surfaceProgramPath);
            string surfaceAotPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividMaterialSurfaceAOT.generated.hlsl");
            Assert.That(File.Exists(surfaceAotPath), Is.True, surfaceAotPath);
            string surfaceAotSource = File.ReadAllText(surfaceAotPath);
            string surfaceSummaryPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Public",
                "SurfaceSummaryGBuffer.hlsl");
            Assert.That(File.Exists(surfaceSummaryPath), Is.True, surfaceSummaryPath);
            string surfaceSummarySource = File.ReadAllText(surfaceSummaryPath);
            string postSurfaceSummaryPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividPostSurfaceSummary.hlsl");
            Assert.That(
                File.Exists(postSurfaceSummaryPath),
                Is.True,
                postSurfaceSummaryPath);
            string postSurfaceSummarySource =
                File.ReadAllText(postSurfaceSummaryPath);
            string compactSource = string.Concat(
                source.Where(character => !char.IsWhiteSpace(character)));
            string compactPostSurfaceSummarySource = string.Concat(
                postSurfaceSummarySource.Where(
                    character => !char.IsWhiteSpace(character)));

            StringAssert.Contains("SurfaceSummaryGBuffer.hlsl", source);
            StringAssert.Contains("VividMaterialSurface.hlsl", source);
            StringAssert.Contains("VividMaterialSurfaceAOT.generated.hlsl", surfaceProgramSource);
            StringAssert.Contains("VividSurfaceSummaryData", source);
            StringAssert.Contains("VividPostSurfaceSummaryInput", source);
            StringAssert.Contains("VividPostSurfaceSummaryOutput", source);
            StringAssert.Contains("VividPostSurfaceSummary(postSurfaceInput)", source);
            StringAssert.Contains("VividSurfaceSummaryGBufferOutput", source);
            StringAssert.Contains("VividPackSurfaceSummaryGBuffer(", source);
            StringAssert.Contains("VividDualSlabLayerSidecarOutput", source);
            StringAssert.Contains("VividPackDualSlabLayerSidecar(", source);
            StringAssert.Contains("VIVID_DUAL_SLAB_SIDECAR_OUTPUT", source);
            StringAssert.Contains("VIVID_DUAL_SLAB_LAYER_SIDECAR_MIN_WEIGHT", source);
            StringAssert.Contains(
                "#if !defined(VIVID_DUAL_SLAB_SIDECAR_OUTPUT)",
                source);
            StringAssert.DoesNotContain(
                "Shaders/Core/Public/GBuffer.hlsl",
                source);
            StringAssert.Contains("VividTryEvaluateAOTSurfaceProgram", source);
            StringAssert.Contains("aotSurfaceOutput.BaseSlab.BaseColor.rgb", source);
            StringAssert.Contains("aotSurfaceOutput.BaseSlab.AmbientOcclusion", source);
            StringAssert.Contains("float3 EvaluateAOTSlabNormalWS(", source);
            StringAssert.Contains("slab.NormalWS", source);
            StringAssert.Contains("slab.TangentWS", source);
            StringAssert.Contains("slab.NormalTS", source);
            StringAssert.Contains("slab.HasNormal", source);
            StringAssert.Contains(
                "EvaluateAOTSlabNormalWS(aotSurfaceOutput.BaseSlab,",
                compactSource);
            StringAssert.Contains("VividAOTDeferredExportContract", source);
            StringAssert.Contains("deferredExportContract.ExpectedClosureCount", source);
            StringAssert.Contains("deferredExportContract.LitClass", source);
            StringAssert.Contains("deferredExportContract.Topology", source);
            StringAssert.Contains(
                "VIVID_SURFACE_SUMMARY_GBUFFER_ABI_VERSION",
                source);
            StringAssert.Contains(
                "VIVID_DUAL_SLAB_LAYER_SIDECAR_ABI_VERSION",
                source);
            StringAssert.Contains("VividAOTDeferredExportHasShadingModel", source);
            StringAssert.Contains("VividAOTDeferredExportHasPayload", source);
            StringAssert.Contains("VividAOTDeferredExportHasPolicy", source);
            StringAssert.DoesNotContain("aotSurfaceOutput.ClosureCount == 1u", source);
            StringAssert.DoesNotContain("aotSurfaceOutput.ClosureCount == 2u", source);
            StringAssert.Contains(
                "expectedLayerOperator = deferredExportContract.Topology",
                source);
            StringAssert.Contains(
                "aotSurfaceOutput.LayerOperator == expectedLayerOperator",
                source);
            StringAssert.DoesNotContain("aotSurfaceOutput.LayerOperator == 1u", source);
            StringAssert.DoesNotContain("aotSurfaceOutput.LayerOperator == 2u", source);
            StringAssert.Contains("aotSurfaceOutput.Emission", source);
            StringAssert.Contains("VividEvaluateAOTSlabSurfaceDetail", surfaceProgramSource);
            StringAssert.DoesNotContain("VividEvaluateSlabSurfaceDetailGrad", source);
            StringAssert.DoesNotContain("VividTryLoadStandardSingleSlabSurfaceProgram", source);
            StringAssert.DoesNotContain("VividTryLoadDualSlabSurfaceProgram", source);
            StringAssert.DoesNotContain("VividEvaluateSlabSurfaceGrad", source);
            StringAssert.DoesNotContain(
                "VIVID_MATERIAL_PROGRAM_LEGACY_FALLBACK",
                source);
            StringAssert.Contains("switch (runtimeHeader.ProgramID)", surfaceAotSource);
            StringAssert.Contains("programData.SurfaceProgramID", source);
            StringAssert.DoesNotContain("programData.CoverageProgramID", source);
            StringAssert.DoesNotContain("programData.TransportProgramID", source);
            StringAssert.Contains(
                "runtimeHeader.ParameterAddress",
                surfaceAotSource);
            StringAssert.Contains(
                "_MaterialParameterDataCount",
                surfaceAotSource);
            StringAssert.Contains(
                "runtimeHeader.ResourceBindingAddress",
                surfaceAotSource);
            StringAssert.Contains(
                "_MaterialResourceDataCount",
                surfaceAotSource);
            StringAssert.Contains("VividLoadMaterialFloat", surfaceAotSource);
            StringAssert.Contains("PullMaterialResourceData", surfaceAotSource);
            StringAssert.DoesNotContain(
                "PullMaterialData(runtimeHeader.ParameterAddress)",
                surfaceAotSource);
            StringAssert.DoesNotContain(
                "result.materialData = PullMaterialData(",
                source);
            StringAssert.Contains(
                "result.materialProgramFailed=1u;",
                compactSource);
            StringAssert.DoesNotContain("programData.CapabilityFlags", source);
            StringAssert.Contains(
                "constboolsupportsUnlit=VividAOTDeferredExportHasShadingModel("
                + "deferredExportContract,"
                + "VIVID_AOT_DEFERRED_EXPORT_SHADING_MODEL_UNLIT);",
                compactSource);
            StringAssert.Contains(
                "(triangleData.materialRuntimeFlags"
                + "&VIVIDMATERIALRUNTIMEFLAGS_UNLIT)!=0u",
                compactSource);
            StringAssert.Contains(
                "constboolinvalidRuntimeUnlit=runtimeRequestsUnlit"
                + "&&!supportsUnlit;",
                compactSource);
            StringAssert.Contains(
                "constboolisUnlit=!failedAOTSurface&&supportsUnlit"
                + "&&(!supportsLit||runtimeRequestsUnlit);",
                compactSource);
            StringAssert.Contains("triangleData.materialProgramFailed", source);
            StringAssert.Contains(
                "constboolfailedAOTSurface=triangleData.materialProgramFailed!=0u"
                + "||!evaluatedAOTSurface||invalidRuntimeUnlit;",
                compactSource);
            StringAssert.Contains(
                "VIVIDMATERIALSURFACEPROGRAMID_DUAL_SLAB",
                compactSource);
            StringAssert.Contains(
                "output.surfaceData.diffuseAlbedo=input.baseColor"
                + "*(1.0f-saturatedMetallic);",
                compactPostSurfaceSummarySource);
            StringAssert.Contains(
                "output.surfaceData.specularF0=lerp("
                + "0.04f.xxx,input.baseColor,saturatedMetallic);",
                compactPostSurfaceSummarySource);
            StringAssert.Contains(
                "output.surfaceData.perceptualRoughness="
                + "input.perceptualRoughness;",
                compactPostSurfaceSummarySource);
            StringAssert.Contains(
                "output.surfaceData.ambientOcclusion=input.ambientOcclusion;",
                compactPostSurfaceSummarySource);
            StringAssert.Contains("SampleVividProbeVolume(", source);
            StringAssert.Contains("VividHasProbeVolumeGI()", source);
            StringAssert.Contains(
                "output.surfaceData.diffuseIrradiance=input.diffuseIrradiance;",
                compactPostSurfaceSummarySource);
            StringAssert.Contains(
                "VIVID_DEFERRED_EXPORT_CLASS_UNLIT",
                surfaceSummarySource);
            StringAssert.Contains(
                "VIVID_DEFERRED_EXPORT_CLASS_FAST_SLAB",
                surfaceSummarySource);
            StringAssert.Contains(
                "VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB",
                surfaceSummarySource);
            StringAssert.Contains(
                "VIVID_DEFERRED_EXPORT_CLASS_ERROR",
                surfaceSummarySource);
            StringAssert.Contains(
                "output.dualSlabLayerData.diffuseAlbedo",
                postSurfaceSummarySource);
            StringAssert.Contains(
                "output.dualSlabLayerData.specularF0",
                postSurfaceSummarySource);
            StringAssert.Contains(
                "output.dualSlabLayerData.perceptualRoughness",
                postSurfaceSummarySource);
            StringAssert.Contains(
                "output.dualSlabLayerData.layerWeight",
                postSurfaceSummarySource);
            StringAssert.Contains(
                "EvaluateAOTSlabNormalWS(aotSurfaceOutput.TopSlab,",
                compactSource);
            StringAssert.Contains(
                "aotSurfaceOutput.TopSlab.AmbientOcclusion",
                source);
            StringAssert.Contains(
                "postSurfaceInput.verticalLayer="
                + "deferredExportContract.Topology"
                + "==VIVID_AOT_DEFERRED_EXPORT_TOPOLOGY_VERTICAL_LAYER?1u:0u;",
                compactSource);
            StringAssert.Contains(
                "saturate(aotSurfaceOutput.LayerWeight)>" +
                "VIVID_DUAL_SLAB_LAYER_SIDECAR_MIN_WEIGHT",
                compactSource);
            StringAssert.Contains(
                "output.surfaceData.diffuseAlbedo=float3(1.0f,0.0f,1.0f);",
                compactPostSurfaceSummarySource);
            StringAssert.Contains(
                "output.surfaceData.emissive=float3(1.0f,0.0f,1.0f);",
                compactPostSurfaceSummarySource);
            StringAssert.Contains("VividAOTDeferredExportHasPolicy", source);
            StringAssert.Contains(
                "VividBuildDeferredExportHeader(",
                postSurfaceSummarySource);
            StringAssert.DoesNotContain(
                "VividBuildDeferredExportHeader(",
                source);
            StringAssert.DoesNotContain(
                "PackVividGBufferSurfaceData(",
                compactSource);
        }

        [Test]
        public void ResolvePass_UsesTileAdaptiveDualSidecarAfterFeedbackUavs()
        {
            var pass = new VisibilityBufferGBufferResolvePass();
            var coreTargets = typeof(VisibilityBufferGBufferResolvePass).GetField(
                "m_GBufferColorTargets",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pass)
                as RenderTargetIdentifier[];
            var sidecarTargets = typeof(VisibilityBufferGBufferResolvePass).GetField(
                "m_DualSlabSidecarTargets",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pass)
                as RenderTargetIdentifier[];

            Assert.That(coreTargets, Has.Length.EqualTo(5));
            Assert.That(sidecarTargets, Has.Length.EqualTo(2));

            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(VisibilityBufferGBufferResolvePass).Assembly);
            Assert.That(package, Is.Not.Null);
            string sourcePath = Path.Combine(
                package.resolvedPath,
                "Runtime",
                "RenderPass",
                "Core",
                "GPUDriven",
                "VisibilityBufferGBufferResolvePass.cs");
            string compactSource = string.Concat(
                File.ReadAllText(sourcePath)
                    .Where(character => !char.IsWhiteSpace(character)));
            string shaderPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "VisibilityBufferGBufferResolve.shader");
            string shaderSource = File.ReadAllText(shaderPath);

            var bindCoreIndex = compactSource.IndexOf(
                "BindGBufferTargets(nativeCmd);",
                global::System.StringComparison.Ordinal);
            var drawCoreIndex = compactSource.IndexOf(
                "CoreUtils.DrawFullScreen(nativeCmd,m_Material,m_DrawProperties,0);",
                global::System.StringComparison.Ordinal);
            var clearFeedbackIndex = compactSource.IndexOf(
                "if(hasFeedback)nativeCmd.ClearRandomWriteTargets();",
                global::System.StringComparison.Ordinal);
            var bindSidecarIndex = compactSource.IndexOf(
                "BindDualSlabSidecarTargets(nativeCmd);",
                global::System.StringComparison.Ordinal);
            var adaptiveReturnIndex = compactSource.IndexOf(
                "if(!m_RequiresDualSlabSidecar)return;",
                global::System.StringComparison.Ordinal);
            var clearSidecarIndex = compactSource.IndexOf(
                "CoreUtils.ClearRenderTarget(nativeCmd,"
                + "ClearFlag.Color,Color.clear);",
                global::System.StringComparison.Ordinal);
            var classifyTilesIndex = compactSource.IndexOf(
                "ClassifyDualSlabSidecarTiles(nativeCmd);",
                global::System.StringComparison.Ordinal);
            var indirectDrawIndex = compactSource.IndexOf(
                "nativeCmd.DrawProceduralIndirect(",
                global::System.StringComparison.Ordinal);
            Assert.That(bindCoreIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(drawCoreIndex, Is.GreaterThan(bindCoreIndex));
            Assert.That(clearFeedbackIndex, Is.GreaterThan(drawCoreIndex));
            Assert.That(adaptiveReturnIndex, Is.GreaterThan(clearFeedbackIndex));
            Assert.That(bindSidecarIndex, Is.GreaterThan(adaptiveReturnIndex));
            Assert.That(clearSidecarIndex, Is.GreaterThan(bindSidecarIndex));
            Assert.That(classifyTilesIndex, Is.GreaterThan(clearSidecarIndex));
            Assert.That(indirectDrawIndex, Is.GreaterThan(classifyTilesIndex));
            StringAssert.Contains(
                "m_DualSlabSidecarMaterial,DualSlabSidecarKeyword,true",
                compactSource);
            StringAssert.Contains(
                "m_DualSlabSidecarTiledMaterial,"
                + "DualSlabSidecarKeyword,true",
                compactSource);
            StringAssert.Contains(
                "m_DualSlabSidecarTiledMaterial,"
                + "DualSlabSidecarTiledKeyword,true",
                compactSource);
            StringAssert.Contains(
                "if(!CanUseTileAdaptiveSidecarResolve())"
                + "{CoreUtils.DrawFullScreen(nativeCmd,"
                + "m_DualSlabSidecarMaterial,m_DrawProperties,0);return;}",
                compactSource);
            StringAssert.Contains("_DualSlabSidecarTileList", shaderSource);
            StringAssert.Contains("SV_InstanceID", shaderSource);
            StringAssert.Contains(
                "VIVID_DUAL_SLAB_SIDECAR_TILED",
                shaderSource);
            StringAssert.Contains(
                "VIVID_DUAL_SLAB_SIDECAR_TILE_SIZE",
                shaderSource);
            StringAssert.Contains(
                "_DualSlabSidecarTileList[input.instanceID]",
                shaderSource);
            StringAssert.Contains(
                "(input.vertexID << 1u) & 2u",
                shaderSource);
            StringAssert.Contains(
                "1.0f - output.uv.y * 2.0f",
                shaderSource);
        }

        private static RenderGraphTexture GetTextureField(VisibilityBufferGBufferResolvePass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferGBufferResolvePass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture) field.GetValue(pass);
        }

        private static RenderGraphBuffer GetBufferField(
            VisibilityBufferGBufferResolvePass pass,
            string fieldName)
        {
            var field = typeof(VisibilityBufferGBufferResolvePass).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphBuffer) field.GetValue(pass);
        }

        private static void SetTextureField(VisibilityBufferGBufferResolvePass pass, string fieldName, RenderGraphTexture value)
        {
            var field = typeof(VisibilityBufferGBufferResolvePass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            field.SetValue(pass, value);
        }
    }
}
