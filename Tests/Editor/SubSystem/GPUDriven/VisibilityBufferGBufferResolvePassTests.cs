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
        public void Initialize_RegistersVisibilityInputsAndGBufferOutputs()
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
            var gbuffer4Entry = resources.Textures.Single(entry => entry.Name == "GBuffer4");

            Assert.That(resources.Textures, Has.Length.EqualTo(9));
            Assert.That(visibilityEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(visibilityEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32_UInt));
            Assert.That(attributes0Entry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                attributes0Entry.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
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
            Assert.That(gbuffer0Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(gbuffer1Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer1Entry.AttachmentIndex, Is.EqualTo(1));
            Assert.That(gbuffer1Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.A2B10G10R10_UNormPack32));
            Assert.That(gbuffer2Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer2Entry.AttachmentIndex, Is.EqualTo(2));
            Assert.That(gbuffer2Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(gbuffer3Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer3Entry.AttachmentIndex, Is.EqualTo(3));
            Assert.That(gbuffer3Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(gbuffer4Entry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(gbuffer4Entry.AttachmentIndex, Is.EqualTo(4));
            Assert.That(gbuffer4Entry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void Prepare_UsesVisibilityTextureSize_WhenConfigured()
        {
            var pass = new VisibilityBufferGBufferResolvePass();
            var visibilityTexture = GetTextureField(pass, "m_VisibilityBuffer");
            var gbuffer0Texture = GetTextureField(pass, "m_GBuffer0");

            visibilityTexture.desc.Width = 1600;
            visibilityTexture.desc.Height = 900;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(gbuffer0Texture.desc.Width, Is.EqualTo(1600));
            Assert.That(gbuffer0Texture.desc.Height, Is.EqualTo(900));
            Assert.That(gbuffer0Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
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
        public void ResolveShader_ConsumesHardwareBarycentricsAndVisibilityUvDerivatives()
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

            StringAssert.Contains("_VisibilityBufferBarycentrics", source);
            StringAssert.Contains("DecodeVividVisibilityBufferBarycentrics", source);
            StringAssert.Contains("_VisibilityBufferAttributes0", source);
            StringAssert.Contains("_VisibilityBufferAttributes1", source);
            StringAssert.Contains("interpolatedUV.uv = attributes0.xy;", source);
            StringAssert.Contains("interpolatedUV.ddx = attributes0.zw;", source);
            StringAssert.Contains("interpolatedUV.ddy = attributes1.xy;", source);
            StringAssert.DoesNotContain("CalculateFullBarycentric(", source);
            StringAssert.DoesNotContain("clipPosition", source);
        }

        [Test]
        public void ResolveShader_ConsumesGeneratedSurfaceProgramAndFailsKnownMissClosed()
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
            string compactSource = string.Concat(
                source.Where(character => !char.IsWhiteSpace(character)));

            StringAssert.Contains("VividMaterialSurface.hlsl", source);
            StringAssert.Contains("VividMaterialSurfaceAOT.generated.hlsl", surfaceProgramSource);
            StringAssert.Contains("VividTryEvaluateAOTSurfaceProgram", source);
            StringAssert.Contains("aotSurfaceOutput.BaseSlab.BaseColor.rgb", source);
            StringAssert.Contains("aotSurfaceOutput.BaseSlab.AmbientOcclusion", source);
            StringAssert.Contains("aotSurfaceOutput.TopSlab.AmbientOcclusion", source);
            StringAssert.Contains("float3 EvaluateAOTSlabNormalWS(", source);
            StringAssert.Contains("slab.NormalWS", source);
            StringAssert.Contains("slab.TangentWS", source);
            StringAssert.Contains("slab.NormalTS", source);
            StringAssert.Contains("slab.HasNormal", source);
            StringAssert.Contains(
                "EvaluateAOTSlabNormalWS(aotSurfaceOutput.BaseSlab,",
                compactSource);
            StringAssert.Contains(
                "EvaluateAOTSlabNormalWS(aotSurfaceOutput.TopSlab,",
                compactSource);
            StringAssert.Contains("aotSurfaceOutput.ClosureCount", source);
            StringAssert.Contains("aotSurfaceOutput.LayerOperator", source);
            StringAssert.Contains("aotSurfaceOutput.Emission", source);
            StringAssert.Contains("VividEvaluateAOTSlabSurfaceDetail", surfaceProgramSource);
            StringAssert.DoesNotContain("VividEvaluateSlabSurfaceDetailGrad", source);
            StringAssert.Contains("VividTryLoadStandardSingleSlabSurfaceProgram", source);
            StringAssert.Contains("VividTryLoadDualSlabSurfaceProgram", source);
            StringAssert.DoesNotContain("VividEvaluateSlabSurfaceGrad", source);
            StringAssert.Contains("Both", source);
            StringAssert.Contains("degrade to the same blend", source);
            StringAssert.DoesNotContain(
                "triangleData.dualSlabMaterialData.LayerOperator",
                source);
            StringAssert.Contains(
                "VIVIDMATERIALPROGRAMCAPABILITIES_LEGACY_GBUFFER_EXPORT",
                source);
            StringAssert.Contains("PullMaterialRuntimeHeader(materialIndex)", surfaceProgramSource);
            StringAssert.Contains(
                "PullMaterialProgramData(runtimeHeader.ProgramID)",
                surfaceProgramSource);
            StringAssert.Contains("programData.SurfaceProgramID", surfaceProgramSource);
            StringAssert.DoesNotContain("programData.CoverageProgramID", surfaceProgramSource);
            StringAssert.DoesNotContain("programData.TransportProgramID", surfaceProgramSource);
            StringAssert.Contains(
                "PullMaterialData(runtimeHeader.ParameterAddress)",
                surfaceProgramSource);
            StringAssert.Contains(
                "runtimeHeader.ParameterAddress >= _MaterialDataCount",
                surfaceProgramSource);
            StringAssert.Contains(
                "runtimeHeader.ResourceBindingAddress",
                surfaceProgramSource);
            StringAssert.Contains(
                "runtimeHeader.ResourceBindingAddress >= _SurfaceBindingDataCount",
                surfaceProgramSource);
            StringAssert.Contains(
                "result.materialData = PullMaterialData(result.instanceData.MaterialIndex)",
                source);
            StringAssert.Contains("result.materialData.SurfaceBindingIndex", source);
            StringAssert.Contains(
                "(programData.CapabilityFlags&VIVIDMATERIALPROGRAMCAPABILITIES_UNLIT)!=0u"
                + "&&(runtimeHeader.Flags&VIVIDMATERIALRUNTIMEFLAGS_UNLIT)!=0u",
                compactSource);
            StringAssert.Contains(
                "(result.materialData.MaterialFlags&VIVIDMATERIALFLAGS_UNLIT)!=0u",
                compactSource);
            StringAssert.Contains(
                "surfaceData.materialFeatures=triangleData.isUnlit!=0u"
                + "?0u:VIVID_MATERIALFEATURE_DEFAULT;",
                compactSource);
            StringAssert.Contains("triangleData.materialProgramFailed", source);
            StringAssert.Contains(
                "failedAOTSurface?float3(1.0f,0.0f,1.0f)"
                + ":evaluatedAOTSurface?aotSurfaceOutput.Emission"
                + ":triangleData.materialData.Emission.rgb",
                compactSource);
            StringAssert.DoesNotContain(
                "if(result.isDualSlab!=0u){result.materialData=PullMaterialData(",
                compactSource);
            StringAssert.DoesNotContain(
                "VividEvaluateSlabSurfaceGrad(",
                compactSource);
            StringAssert.DoesNotContain(
                "surfaceData.materialFeatures=VIVID_MATERIALFEATURE_DEFAULT;",
                compactSource);
        }

        private static RenderGraphTexture GetTextureField(VisibilityBufferGBufferResolvePass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferGBufferResolvePass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture) field.GetValue(pass);
        }

        private static void SetTextureField(VisibilityBufferGBufferResolvePass pass, string fieldName, RenderGraphTexture value)
        {
            var field = typeof(VisibilityBufferGBufferResolvePass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            field.SetValue(pass, value);
        }
    }
}
