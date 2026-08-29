using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.MeshShader;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Editor.Tests
{
    public class VisibilityBufferPassTests
    {
        [Test]
        public void Initialize_RegistersFourVisibilityTargetsAndDepthWithoutGPUDrivenBufferPorts()
        {
            IRenderPass renderPass = new VisibilityBufferPass();

            var resources = renderPass.Initialize();
            var visibilityEntry = resources.Textures.Single(entry => entry.Name == "VisibilityBuffer");
            var attributes0Entry = resources.Textures.Single(entry => entry.Name == "VisibilityBufferAttributes0");
            var attributes1Entry = resources.Textures.Single(entry => entry.Name == "VisibilityBufferAttributes1");
            var barycentricsEntry = resources.Textures.Single(entry => entry.Name == "VisibilityBufferBarycentrics");
            var depthEntry = resources.Textures.Single(entry => entry.Name == "Depth");
            Assert.That(resources.Textures, Has.Length.EqualTo(5));
            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(resources.RenderLists, Is.Empty);
            Assert.That(renderPass, Is.InstanceOf<UnsafePass>());

            Assert.That(visibilityEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(visibilityEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(visibilityEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32_UInt));
            Assert.That(attributes0Entry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(attributes0Entry.AttachmentIndex, Is.EqualTo(1));
            Assert.That(
                attributes0Entry.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(attributes1Entry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(attributes1Entry.AttachmentIndex, Is.EqualTo(2));
            Assert.That(
                attributes1Entry.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(barycentricsEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(barycentricsEntry.AttachmentIndex, Is.EqualTo(3));
            Assert.That(
                barycentricsEntry.Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R16G16_SFloat));

            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(depthEntry.IsDepthAttachment, Is.True);
            Assert.That(depthEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth32));

        }

        [Test]
        public void Prepare_ResizesDefaultOutputs_AndLeavesGPUDrivenBuffersNull_WhenFrameDataDoesNotProvideThem()
        {
            VividGPUDrivenSystem.Shutdown();

            try
            {
                var pass = new VisibilityBufferPass();
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.actualWidth = 1024;
                cameraData.actualHeight = 576;

                pass.Prepare(frameData);

                var visibilityTexture = GetTextureField(pass, "m_VisibilityBuffer");
                var attributes0Texture = GetTextureField(pass, "m_Attributes0");
                var attributes1Texture = GetTextureField(pass, "m_Attributes1");
                var barycentricsTexture = GetTextureField(pass, "m_Barycentrics");
                var depthTexture = GetTextureField(pass, "m_Depth");
                var renderRequestsBuffer = GetBufferField(pass, "m_VisibleMeshletRenderRequests");
                var indirectArgsBuffer = GetBufferField(pass, "m_VisibleMeshletIndirectArgs");

                Assert.That(visibilityTexture.desc.Width, Is.EqualTo(1024));
                Assert.That(visibilityTexture.desc.Height, Is.EqualTo(576));
                Assert.That(attributes0Texture.desc.Width, Is.EqualTo(1024));
                Assert.That(attributes0Texture.desc.Height, Is.EqualTo(576));
                Assert.That(attributes1Texture.desc.Width, Is.EqualTo(1024));
                Assert.That(attributes1Texture.desc.Height, Is.EqualTo(576));
                Assert.That(barycentricsTexture.desc.Width, Is.EqualTo(1024));
                Assert.That(barycentricsTexture.desc.Height, Is.EqualTo(576));
                Assert.That(depthTexture.desc.Width, Is.EqualTo(1024));
                Assert.That(depthTexture.desc.Height, Is.EqualTo(576));
                Assert.That(renderRequestsBuffer, Is.Null);
                Assert.That(indirectArgsBuffer, Is.Null);
            }
            finally
            {
                VividGPUDrivenSystem.Shutdown();
            }
        }

        [Test]
        public void Prepare_DoesNotOverwriteOverriddenOutputDescriptors()
        {
            var pass = new VisibilityBufferPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            var externalVisibility = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 320,
                    Height = 240,
                    ColorFormat = GraphicsFormat.R32G32_UInt,
                }
            };
            var externalDepth = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 640,
                    Height = 360,
                    ColorFormat = GraphicsFormat.None,
                    DepthBufferBits = DepthBits.Depth16,
                }
            };

            SetTextureField(pass, "m_VisibilityBuffer", externalVisibility);
            SetTextureField(pass, "m_Depth", externalDepth);

            pass.Prepare(frameData);

            Assert.That(externalVisibility.desc.Width, Is.EqualTo(320));
            Assert.That(externalVisibility.desc.Height, Is.EqualTo(240));
            Assert.That(externalDepth.desc.Width, Is.EqualTo(640));
            Assert.That(externalDepth.desc.Height, Is.EqualTo(360));
        }

        [Test]
        public void Prepare_ConsumesPrimitiveDrawSetFromFrameData()
        {
            VividGPUDrivenSystem.Shutdown();
            var drawSet = new VividPrimitiveDrawSet();
            var pass = new VisibilityBufferPass();
            try
            {
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.actualWidth = 16;
                cameraData.actualHeight = 16;
                VividGPUDrivenFrameData frameGPUDrivenData =
                    frameData.GetOrCreate<VividGPUDrivenFrameData>();
                frameGPUDrivenData.primitiveDrawSet = drawSet;
                frameGPUDrivenData.primitiveShadowDrawSet = drawSet;

                pass.Prepare(frameData);

                FieldInfo field = typeof(VisibilityBufferPass).GetField(
                    "m_PrimitiveDrawSet",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                Assert.That(field.GetValue(pass), Is.SameAs(drawSet));

                VividGPUDrivenFrameData gpuDrivenFrameData =
                    frameData.GetOrCreate<VividGPUDrivenFrameData>();
                gpuDrivenFrameData.Reset();
                Assert.That(gpuDrivenFrameData.primitiveDrawSet, Is.Null);
                Assert.That(gpuDrivenFrameData.primitiveShadowDrawSet, Is.Null);
            }
            finally
            {
                pass.Dispose();
                drawSet.Dispose();
                VividGPUDrivenSystem.Shutdown();
            }
        }

        [Test]
        public void DrawRendererLists_FiltersByDrawSetBucketBeforeLegacyBatchMask()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VisibilityBufferPass).Assembly);
            Assert.That(package, Is.Not.Null);
            string path = Path.Combine(
                package.resolvedPath,
                "Runtime",
                "RenderPass",
                "Core",
                "GPUDriven",
                "VisibilityBufferPass.cs");

            Assert.That(File.Exists(path), Is.True, path);
            string source = File.ReadAllText(path);
            int drawSetBranch = source.IndexOf("if (m_PrimitiveDrawSet?.IsBuilt == true)");
            int bucketFilter = source.IndexOf("m_PrimitiveDrawSet.TryGetBucket(batchKey", drawSetBranch);
            int zeroBucketFilter = source.IndexOf("bucket.DrawCount == 0u", bucketFilter);
            int legacyFallback = source.IndexOf(
                "else if (system != null && !system.IsMainViewRendererBatchActive(batchKey))",
                zeroBucketFilter);

            Assert.That(drawSetBranch, Is.GreaterThanOrEqualTo(0));
            Assert.That(bucketFilter, Is.GreaterThan(drawSetBranch));
            Assert.That(zeroBucketFilter, Is.GreaterThan(bucketFilter));
            Assert.That(legacyFallback, Is.GreaterThan(zeroBucketFilter));
        }

        [Test]
        public void Prepare_UsesGPUDrivenBuffersDirectlyFromFrameData()
        {
            VividGPUDrivenSystem.Shutdown();
            using var renderRequestsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(uint) * 2);
            using var indirectArgsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                4,
                sizeof(uint));
            var pass = new VisibilityBufferPass();

            try
            {
                var frameData = new ContextContainer();
                VividGPUDrivenFrameData gpuDrivenFrameData =
                    frameData.GetOrCreate<VividGPUDrivenFrameData>();
                gpuDrivenFrameData.visibleMeshletRenderRequestsBuffer = renderRequestsBuffer;
                gpuDrivenFrameData.visibleMeshletIndirectDrawArgsBuffer = indirectArgsBuffer;

                pass.Prepare(frameData);

                Assert.That(
                    GetBufferField(pass, "m_VisibleMeshletRenderRequests"),
                    Is.SameAs(renderRequestsBuffer));
                Assert.That(
                    GetBufferField(pass, "m_VisibleMeshletIndirectArgs"),
                    Is.SameAs(indirectArgsBuffer));
            }
            finally
            {
                pass.Dispose();
                VividGPUDrivenSystem.Shutdown();
            }
        }

        [Test]
        public void RasterizationPath_DefaultsToIndirectAndCanSelectExperimentalMeshShader()
        {
            var pass = new VisibilityBufferPass();

            Assert.That(
                pass.RasterizationPath,
                Is.EqualTo(VisibilityBufferRasterizationPath.DrawProceduralIndirect));

            pass.RasterizationPath = VisibilityBufferRasterizationPath.ExperimentalMeshShader;

            Assert.That(
                pass.RasterizationPath,
                Is.EqualTo(VisibilityBufferRasterizationPath.ExperimentalMeshShader));
        }

        [Test]
        public void MeshShaderHlsl_UsesGpuIndirectCountsAndPerPrimitiveVisibility()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VisibilityBufferPass).Assembly);
            Assert.That(package, Is.Not.Null);
            string path = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "VisibilityBufferMeshShader.hlsl");

            Assert.That(File.Exists(path), Is.True, path);
            string source = File.ReadAllText(path);

            StringAssert.Contains("void AmplificationMain()", source);
            StringAssert.Contains("_VisibleMeshletIndirectArgs.Load", source);
            StringAssert.Contains("DispatchMesh(dispatchGroupCountX, dispatchGroupCountY, 1u", source);
            StringAssert.Contains("void MeshMain(", source);
            StringAssert.Contains("out primitives VividMeshPrimitiveOutput", source);
            StringAssert.Contains(
                "Runtime/SubSystem/GPUDriven/VividGPUDrivenStructs.cs.hlsl",
                source);
            StringAssert.Contains("VividMeshletDecode.hlsli", source);
            StringAssert.Contains("VividVisibilityBuffer.hlsl", source);
            StringAssert.Contains("PackVisibilityBufferValue(visibilityBufferValue)", source);
            StringAssert.Contains("VividVisibilityBufferFragmentOutput PixelMain", source);
            StringAssert.DoesNotContain("struct VividInstanceData", source);
            StringAssert.DoesNotContain("struct VividMeshletRenderRequestPacked", source);
        }

        [Test]
        public void MeshShaderDxilInterop_MatchesNativeX64Layout()
        {
            Assert.That(IntPtr.Size, Is.EqualTo(8));
            Assert.That(Marshal.SizeOf<VividMeshShaderPlugin.NativeBytecode>(), Is.EqualTo(16));
            Assert.That(
                Marshal.OffsetOf<VividMeshShaderPlugin.NativeBytecode>(
                    nameof(VividMeshShaderPlugin.NativeBytecode.Size)).ToInt64(),
                Is.EqualTo(8));
            Assert.That(
                Marshal.SizeOf<VividMeshShaderPlugin.NativeShaderObjectDxilDesc>(),
                Is.EqualTo(112));
            Assert.That(
                Marshal.OffsetOf<VividMeshShaderPlugin.NativeShaderObjectDxilDesc>(
                    nameof(VividMeshShaderPlugin.NativeShaderObjectDxilDesc.AmplificationShader)).ToInt64(),
                Is.EqualTo(8));
            Assert.That(
                Marshal.OffsetOf<VividMeshShaderPlugin.NativeShaderObjectDxilDesc>(
                    nameof(VividMeshShaderPlugin.NativeShaderObjectDxilDesc.MeshShader)).ToInt64(),
                Is.EqualTo(24));
            Assert.That(
                Marshal.OffsetOf<VividMeshShaderPlugin.NativeShaderObjectDxilDesc>(
                    nameof(VividMeshShaderPlugin.NativeShaderObjectDxilDesc.PixelShader)).ToInt64(),
                Is.EqualTo(40));
            Assert.That(
                Marshal.OffsetOf<VividMeshShaderPlugin.NativeShaderObjectDxilDesc>(
                    nameof(VividMeshShaderPlugin.NativeShaderObjectDxilDesc.RenderState)).ToInt64(),
                Is.EqualTo(56));
        }

        [Test]
        public void MeshShaderRenderState_MatchesUnityD3D12FrontFaceConvention()
        {
            var renderState = new VividMeshShaderRenderState(
                VividMeshShaderCullMode.Back,
                VividMeshShaderCompareFunction.GreaterEqual);

            VividMeshShaderPlugin.NativeRenderStateDesc nativeRenderState =
                VividMeshShaderPlugin.CreateNativeRenderState(renderState);

            Assert.That(nativeRenderState.FrontCounterClockwise, Is.EqualTo(1u));
        }

        [Test]
        public void MeshShaderObject_RejectsMissingProgramBeforeCallingNativePlugin()
        {
            var renderState = new VividMeshShaderRenderState(
                VividMeshShaderCullMode.Back,
                VividMeshShaderCompareFunction.GreaterEqual);

            bool created = VividMeshShaderObject.TryCreate(
                null,
                renderState,
                out VividMeshShaderObject shaderObject,
                out string error);

            Assert.That(created, Is.False);
            Assert.That(shaderObject, Is.Null);
            StringAssert.Contains("program asset is missing", error);
        }

        [Test]
        public void MeshShaderStateBoundary_RejectsMissingCommandBufferBeforeCallingNativePlugin()
        {
            Assert.Throws<ArgumentNullException>(
                () => VividMeshShaderPlugin.QueueStateBoundary(null));
        }

        [Test]
        public void VisibilityShader_WritesSharedVisibilityAttributeAbi()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VisibilityBufferPass).Assembly);
            Assert.That(package, Is.Not.Null);
            string path = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "VisibilityBufferPass.shader");

            Assert.That(File.Exists(path), Is.True, path);
            string source = File.ReadAllText(path);

            StringAssert.Contains("VividVisibilityBufferFragmentOutput Frag", source);
            StringAssert.Contains("#pragma require barycentrics", source);
            StringAssert.Contains("SV_Barycentrics", source);
            StringAssert.Contains("PackVividVisibilityBufferFragmentOutput", source);
            StringAssert.Contains("output.uv0 = vertex.UV.xy", source);
            StringAssert.Contains("output.geometricNormalWS", source);
        }

        [Test]
        public void CoverageProgram_IsSharedByVisibilityAndShadowPasses()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VisibilityBufferPass).Assembly);
            Assert.That(package, Is.Not.Null);
            string gpuDrivenShaderPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Private",
                "GPUDriven");
            string coveragePath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividMaterialCoverage.hlsl");
            string coverageAotPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividMaterialCoverageAOT.generated.hlsl");
            string visibilityBufferPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Public",
                "GPUDriven",
                "VividVisibilityBuffer.hlsl");
            string visibilitySource = File.ReadAllText(
                Path.Combine(gpuDrivenShaderPath, "VisibilityBufferPass.shader"));
            string shadowSource = File.ReadAllText(
                Path.Combine(gpuDrivenShaderPath, "VisibilityBufferShadowCasterPass.shader"));
            string coverageSource = File.ReadAllText(coveragePath);
            Assert.That(File.Exists(coverageAotPath), Is.True, coverageAotPath);
            string coverageAotSource = File.ReadAllText(coverageAotPath);
            string visibilityBufferSource = File.ReadAllText(visibilityBufferPath);

            StringAssert.Contains("programData.CoverageProgramID", coverageSource);
            StringAssert.Contains(
                "VividMaterialCoverageAOT.generated.hlsl",
                coverageSource);
            StringAssert.Contains("VividTryEvaluateAOTCoverageProgram", coverageSource);
            StringAssert.DoesNotContain("programData.SurfaceProgramID", coverageSource);
            StringAssert.DoesNotContain("programData.TransportProgramID", coverageSource);
            StringAssert.DoesNotContain(
                "VIVIDMATERIALPARAMETERLAYOUTID_DUAL_SLAB_MATERIAL_DATA",
                coverageSource);
            StringAssert.DoesNotContain("VividGetBaseSlabMaterialData(materialData)", coverageSource);
            StringAssert.Contains("VividSampleBaseColorGrad", coverageSource);
            StringAssert.DoesNotContain("VividSampleBaseColor(", coverageSource);
            StringAssert.Contains("const float2 uvDdx = uv0Ddx * tiling;", coverageSource);
            StringAssert.Contains("const float2 uvDdy = uv0Ddy * tiling;", coverageSource);
            StringAssert.Contains("switch (runtimeHeader.ProgramID)", coverageAotSource);
            StringAssert.Contains(
                "VIVIDMATERIALPARAMETERLAYOUTID_LEGACY_MATERIAL_DATA",
                coverageAotSource);
            StringAssert.Contains(
                "VIVIDMATERIALPARAMETERLAYOUTID_DUAL_SLAB_MATERIAL_DATA",
                coverageAotSource);
            StringAssert.Contains("runtimeHeader.ParameterAddress", coverageAotSource);
            StringAssert.Contains("runtimeHeader.ResourceBindingAddress", coverageAotSource);
            StringAssert.Contains("_MaterialDataCount", coverageAotSource);
            StringAssert.Contains("_DualSlabMaterialDataCount", coverageAotSource);
            StringAssert.Contains("_SurfaceBindingDataCount", coverageAotSource);
            StringAssert.Contains("VividCreateSurfaceSampleContextGrad", coverageAotSource);
            StringAssert.Contains("VividSampleBaseColorGrad", coverageAotSource);
            StringAssert.DoesNotContain("PositionCS", coverageAotSource);
            StringAssert.DoesNotContain("ddx(", coverageAotSource);
            StringAssert.DoesNotContain("ddy(", coverageAotSource);
            StringAssert.Contains("VividEvaluateCoverageProgram", visibilitySource);
            StringAssert.Contains("VividEvaluateCoverageProgram", shadowSource);
            StringAssert.Contains("VIVID_MATERIAL_COVERAGE_KNOWN_FAILURE", visibilitySource);
            StringAssert.Contains("VIVID_MATERIAL_COVERAGE_KNOWN_FAILURE", shadowSource);
            StringAssert.Contains("clip(-1.0f)", visibilitySource);
            StringAssert.Contains("clip(-1.0f)", shadowSource);
            StringAssert.Contains("VividEvaluateBaseColorAlphaCoverage", visibilitySource);
            StringAssert.Contains("VividEvaluateBaseColorAlphaCoverage", shadowSource);
            StringAssert.Contains("#pragma shader_feature_local_fragment _ALPHATEST_ON", shadowSource);
            int shadowVaryings = shadowSource.IndexOf("struct Varyings");
            int shadowFragment = shadowSource.IndexOf("void Frag(Varyings input)");
            Assert.That(shadowVaryings, Is.GreaterThanOrEqualTo(0));
            Assert.That(shadowFragment, Is.GreaterThan(shadowVaryings));
            string shadowVertexInterface = shadowSource.Substring(
                shadowVaryings,
                shadowFragment - shadowVaryings);
            StringAssert.Contains(
                "nointerpolation uint instanceIndex : TEXCOORD0;",
                shadowVertexInterface);
            StringAssert.Contains("float2 uv0 : TEXCOORD1;", shadowVertexInterface);
            StringAssert.DoesNotContain("#ifdef _ALPHATEST_ON", shadowVertexInterface);
            int visibilityDdx = visibilitySource.IndexOf("ddx(input.uv0)");
            int visibilityDdy = visibilitySource.IndexOf("ddy(input.uv0)");
            int visibilityProgram = visibilitySource.IndexOf("VividEvaluateCoverageProgram");
            int visibilityPack = visibilitySource.IndexOf(
                "PackVividVisibilityBufferFragmentOutput");
            int shadowDdx = shadowSource.IndexOf("ddx(input.uv0)");
            int shadowDdy = shadowSource.IndexOf("ddy(input.uv0)");
            int shadowProgram = shadowSource.IndexOf("VividEvaluateCoverageProgram");
            Assert.That(visibilityDdx, Is.GreaterThanOrEqualTo(0));
            Assert.That(visibilityProgram, Is.GreaterThan(visibilityDdx));
            Assert.That(visibilityDdy, Is.GreaterThanOrEqualTo(0));
            Assert.That(visibilityProgram, Is.GreaterThan(visibilityDdy));
            Assert.That(visibilityPack, Is.GreaterThan(visibilityProgram));
            Assert.That(CountOccurrences(visibilitySource, "ddx(input.uv0)"), Is.EqualTo(1));
            Assert.That(CountOccurrences(visibilitySource, "ddy(input.uv0)"), Is.EqualTo(1));
            Assert.That(shadowDdx, Is.GreaterThanOrEqualTo(0));
            Assert.That(shadowProgram, Is.GreaterThan(shadowDdx));
            Assert.That(shadowDdy, Is.GreaterThanOrEqualTo(0));
            Assert.That(shadowProgram, Is.GreaterThan(shadowDdy));
            Assert.That(CountOccurrences(shadowSource, "ddx(input.uv0)"), Is.EqualTo(1));
            Assert.That(CountOccurrences(shadowSource, "ddy(input.uv0)"), Is.EqualTo(1));
            StringAssert.Contains("float2 uv0Ddx", visibilityBufferSource);
            StringAssert.Contains("float2 uv0Ddy", visibilityBufferSource);
            StringAssert.Contains(
                "output.attributes0 = float4(uv0, uv0Ddx)",
                visibilityBufferSource);
            StringAssert.Contains("uv0Ddy,", visibilityBufferSource);
            StringAssert.DoesNotContain("ddx(uv0)", visibilityBufferSource);
            StringAssert.DoesNotContain("ddy(uv0)", visibilityBufferSource);
            StringAssert.Contains("uv0Ddx,", visibilitySource);
            StringAssert.Contains("uv0Ddy,", visibilitySource);
            StringAssert.Contains(
                "PackVividVisibilityBufferFragmentOutput(\n"
                + "                    input.visibilityValue,\n"
                + "                    input.uv0,\n"
                + "                    uv0Ddx,\n"
                + "                    uv0Ddy,",
                visibilitySource.Replace("\r\n", "\n"));
            StringAssert.DoesNotContain("float4 SampleAlbedo(", visibilitySource);
            StringAssert.DoesNotContain("float4 SampleAlbedo(", shadowSource);
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int startIndex = 0;
            while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
            {
                count++;
                startIndex += value.Length;
            }
            return count;
        }

        private static RenderGraphTexture GetTextureField(VisibilityBufferPass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static void SetTextureField(VisibilityBufferPass pass, string fieldName, RenderGraphTexture value)
        {
            var field = typeof(VisibilityBufferPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            field.SetValue(pass, value);
        }

        private static GraphicsBuffer GetBufferField(VisibilityBufferPass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (GraphicsBuffer)field.GetValue(pass);
        }

    }
}
