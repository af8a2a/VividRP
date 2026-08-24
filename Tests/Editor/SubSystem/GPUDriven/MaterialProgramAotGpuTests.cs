using System;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using IRMaterialParameter = VividRP.Runtime.GPUDriven.MaterialParameter;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    [Parallelizable(ParallelScope.None)]
    public sealed class MaterialProgramAotGpuTests
    {
        private const string TemporaryFolder = "Assets/VividMaterialProgramAotGpuTests";
        private const string CoverageIncludePath =
            TemporaryFolder + "/VividMaterialCoverageAOT.generated.hlsl";
        private const string SurfaceIncludePath =
            TemporaryFolder + "/VividMaterialSurfaceAOT.generated.hlsl";
        private const string CapabilityShaderPath =
            TemporaryFolder + "/MaterialProgramAotGpuCapability.shader";
        private const string ShaderPath =
            TemporaryFolder + "/MaterialProgramAotGpuTest.shader";
        private const uint CustomProgramIndex = 3u;

        [TearDown]
        public void TearDown()
        {
            RenderTexture.active = null;
            AssetDatabase.DeleteAsset(TemporaryFolder);
        }

        [Test]
        public void FrozenCatalog_NonBuiltinCoverageAndSurface_DispatchEndToEndOnGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A graphics device is required for the Material Program AOT GPU validation.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                Assert.Ignore(
                    "The Material Program AOT GPU validation requires Direct3D 12, DXC, and Shader Model 6.6.");
            }
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                || !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
            {
                Assert.Ignore(
                    "The Material Program AOT GPU validation requires RGBA32F render and readback formats.");
            }

            EnsureTemporaryFolder();
            RequireShaderModel66();
            CompiledMaterialProgram customProgram = BuildCustomSingleSlabProgram();
            CompiledMaterialProgram builtinProgram =
                GPUDrivenMaterialCompiler.GetMaterialProgram(
                    VividMaterialProgramID.StandardSingleSlab);
            Assert.That(
                customProgram.CoverageHlsl.EntryPoint,
                Is.Not.EqualTo(builtinProgram.CoverageHlsl.EntryPoint));
            Assert.That(
                customProgram.SurfaceHlsl.EntryPoint,
                Is.Not.EqualTo(builtinProgram.SurfaceHlsl.EntryPoint));
            MaterialProgramCatalog catalog = MaterialProgramCatalog.Bake(
                MaterialProgramBuiltinCatalog.Templates,
                MaterialProgramCatalogBakeSlot.Reserved("P0.ReservedForGpuTest"),
                MaterialProgramCatalogBakeSlot.Reserved("P1.ReservedForGpuTest"),
                MaterialProgramCatalogBakeSlot.Reserved("P2.ReservedForGpuTest"),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "P3.NonBuiltinCoverageAndSurfaceGpuTest",
                    customProgram));
            var customProgramID = (VividMaterialProgramID) CustomProgramIndex;
            MaterialProgramCatalog.ManifestEntry catalogEntry =
                catalog.GetEntry(customProgramID);
            Assert.That(catalogEntry.ProgramID, Is.EqualTo(customProgramID));

            MaterialCoverageHlslGenerator.Generate(catalog, CoverageIncludePath);
            MaterialSurfaceHlslGenerator.Generate(catalog, SurfaceIncludePath);
            File.WriteAllText(ShaderPath, BuildTestShaderSource());
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.That(shader, Is.Not.Null);
            Assert.That(
                ShaderUtil.ShaderHasError(shader),
                Is.False,
                "The generated non-builtin Coverage/Surface dispatcher shader failed to compile.");

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            GraphicsBuffer runtimeHeaderBuffer = null;
            GraphicsBuffer programBuffer = null;
            GraphicsBuffer materialBuffer = null;
            GraphicsBuffer surfaceBindingBuffer = null;
            var target = new RenderTexture(
                3,
                1,
                0,
                RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            var readback = new Texture2D(
                3,
                1,
                TextureFormat.RGBAFloat,
                mipChain: false,
                linear: true);
            var commandBuffer = new CommandBuffer
            {
                name = "Vivid Material Program AOT GPU Test",
            };
            try
            {
                VividMaterialRuntimeHeader[] runtimeHeaders =
                {
                    new VividMaterialRuntimeHeader
                    {
                        ProgramID = customProgramID,
                        ParameterAddress = 0u,
                        ResourceBindingAddress = 0u,
                        Flags = VividMaterialRuntimeFlags.AlphaClip,
                    },
                };
                VividMaterialProgramData[] runtimePrograms =
                    catalog.CreateRuntimeProgramTable();
                VividMaterialData[] materialParameters =
                {
                    new VividMaterialData
                    {
                        AlbedoColor = new float4(0.8f, 0.6f, 0.4f, 0.8f),
                        TextureTilingOffset = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                        Emission = new float4(0.1f, 0.2f, 0.3f, 0.0f),
                        MetallicSmoothnessRemap = new float4(0.0f, 1.0f, 0.0f, 1.0f),
                        AmbientOcclusionRemap = new float4(0.0f, 1.0f, 0.0f, 0.0f),
                        NormalsStrength = 1.0f,
                        Roughness = 0.25f,
                        Metallic = 0.6f,
                        AlphaClipThreshold = 0.3f,
                    },
                };
                VividSurfaceBindingData[] surfaceBindings =
                {
                    new VividSurfaceBindingData
                    {
                        BaseColorResource = VividSurfaceBindingData.InvalidResource,
                        NormalResource = VividSurfaceBindingData.InvalidResource,
                        MaskResource = VividSurfaceBindingData.InvalidResource,
                        Flags = VividSurfaceBindingFlags.None,
                        UVScaleBias = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                    },
                };

                runtimeHeaderBuffer = CreateStructuredBuffer(runtimeHeaders);
                programBuffer = CreateStructuredBuffer(runtimePrograms);
                materialBuffer = CreateStructuredBuffer(materialParameters);
                surfaceBindingBuffer = CreateStructuredBuffer(surfaceBindings);
                material.SetBuffer("_MaterialRuntimeHeaders", runtimeHeaderBuffer);
                material.SetInt("_MaterialRuntimeHeaderCount", runtimeHeaders.Length);
                material.SetBuffer("_MaterialPrograms", programBuffer);
                material.SetInt("_MaterialProgramCount", runtimePrograms.Length);
                material.SetBuffer("_MaterialData", materialBuffer);
                material.SetInt("_MaterialDataCount", materialParameters.Length);
                material.SetBuffer("_SurfaceBindingData", surfaceBindingBuffer);
                material.SetInt("_SurfaceBindingDataCount", surfaceBindings.Length);

                target.Create();
                commandBuffer.SetRenderTarget(target);
                commandBuffer.ClearRenderTarget(
                    clearDepth: false,
                    clearColor: true,
                    backgroundColor: Color.black);
                commandBuffer.DrawProcedural(
                    Matrix4x4.identity,
                    material,
                    shaderPass: 0,
                    topology: MeshTopology.Triangles,
                    vertexCount: 3,
                    instanceCount: 1);
                Graphics.ExecuteCommandBuffer(commandBuffer);

                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 3, 1), 0, 0);
                readback.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                AssertColor(
                    readback.GetPixel(0, 0),
                    new Color(0.4f, 0.3f, 1.0f, 1.0f),
                    "Coverage, alpha threshold, and dispatcher success");
                AssertColor(
                    readback.GetPixel(1, 0),
                    new Color(0.4f, 0.15f, 0.3f, 0.75f),
                    "Surface base color and transformed roughness");
                AssertColor(
                    readback.GetPixel(2, 0),
                    new Color(0.3f, 0.15f, 0.3f, 0.45f),
                    "Surface transformed metallic and emission");
            }
            finally
            {
                commandBuffer.Dispose();
                runtimeHeaderBuffer?.Dispose();
                programBuffer?.Dispose();
                materialBuffer?.Dispose();
                surfaceBindingBuffer?.Dispose();
                RenderTexture.active = null;
                target.Release();
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
            }
        }

        private static CompiledMaterialProgram BuildCustomSingleSlabProgram()
        {
            var values = new MaterialValueIR();
            MaterialValue uv = values.ExternalInput(MaterialExternalInput.UV0);
            MaterialValue texture = values.TextureResource(MaterialTextureResource.BaseColor);
            MaterialValue sample = values.TextureSampleGrad(
                texture,
                uv,
                values.Ddx(uv),
                values.Ddy(uv));
            MaterialValue sampledBaseColor = values.Multiply(
                sample,
                values.Parameter(IRMaterialParameter.BaseColor));
            MaterialValue surfaceBaseColor = values.Multiply(
                sampledBaseColor,
                values.Constant(new float4(0.5f, 0.25f, 0.75f, 1.0f)));
            MaterialValue coverage = values.Saturate(values.Multiply(
                values.Swizzle(sampledBaseColor, MaterialSwizzleMask.W),
                values.Constant(0.5f)));
            MaterialValue roughness = values.OneMinus(
                values.Parameter(IRMaterialParameter.Roughness));
            MaterialValue metallic = values.Saturate(values.Multiply(
                values.Parameter(IRMaterialParameter.Metallic),
                values.Constant(0.5f)));
            MaterialValue alphaClipThreshold =
                values.Parameter(IRMaterialParameter.AlphaClipThreshold);
            MaterialValue emission = values.Add(
                values.Parameter(IRMaterialParameter.Emission),
                values.Constant(new float3(0.05f, 0.1f, 0.15f)));
            MaterialValue normal =
                values.ExternalInput(MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent =
                values.ExternalInput(MaterialExternalInput.GeometryTangentWS);
            var closures = new ClosureExpressionGraph(values);
            MaterialClosure surfaceClosure = closures.Slab(
                surfaceBaseColor,
                roughness,
                metallic,
                normal,
                tangent,
                ClosureFeatureMask.BaseColorTexture
                | ClosureFeatureMask.NormalTexture
                | ClosureFeatureMask.MaskTexture);
            var module = new MaterialIRModule(
                values,
                new MaterialOutputRoots(coverage, alphaClipThreshold, emission),
                closures,
                surfaceClosure,
                ClosureTopologyBudget.Prototype,
                MaterialFeatureMask.AlphaClip,
                MaterialShadingModelMask.StandardLit | MaterialShadingModelMask.Unlit);
            return CompiledMaterialProgram.Compile(
                module,
                GPUDrivenMaterialCompiler.ProgramVersion);
        }

        private static GraphicsBuffer CreateStructuredBuffer<T>(T[] data)
            where T : struct
        {
            var buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                data.Length,
                Marshal.SizeOf<T>());
            buffer.SetData(data);
            return buffer;
        }

        private static string BuildTestShaderSource()
        {
            return $@"Shader ""Hidden/VividRP/Tests/MaterialProgramAotGpu""
{{
    SubShader
    {{
        Pass
        {{
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 5.0
            #pragma use_dxc
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment Frag
            #include ""Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl""
            #include ""Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl""
            #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS 1
            #include_with_pragmas ""Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividSurfaceSampling.hlsl""

            // Pull the shared Slab detail helpers without importing the builtin dispatcher.
            #define VIVID_MATERIAL_SURFACE_AOT_GENERATED_INCLUDED
            #include ""Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialSurface.hlsl""
            #undef VIVID_MATERIAL_SURFACE_AOT_GENERATED_INCLUDED

            struct VividMaterialCoverageEvaluation
            {{
                float Coverage;
                float AlphaClipThreshold;
            }};

            #include ""{CoverageIncludePath}""
            #include ""{SurfaceIncludePath}""

            struct Varyings
            {{
                float4 positionCS : SV_POSITION;
            }};

            Varyings Vert(uint vertexID : SV_VertexID)
            {{
                const float2 corner = float2(
                    (vertexID << 1u) & 2u,
                    vertexID & 2u);
                Varyings output;
                output.positionCS = float4(corner * 2.0f - 1.0f, 0.0f, 1.0f);
                return output;
            }}

            float4 Frag(Varyings input) : SV_Target
            {{
                const uint materialIndex = 0u;
                const VividMaterialRuntimeHeader runtimeHeader =
                    PullMaterialRuntimeHeader(materialIndex);
                const VividMaterialProgramData programData =
                    PullMaterialProgramData(runtimeHeader.ProgramID);

                VividAOTCoverageContext coverageContext;
                coverageContext.UV0 = float2(0.25f, 0.75f);
                coverageContext.UV0Ddx = 0.0f.xx;
                coverageContext.UV0Ddy = 0.0f.xx;
                VividMaterialCoverageEvaluation coverage =
                    (VividMaterialCoverageEvaluation) 0;
                const bool coverageSucceeded =
                    VividTryEvaluateAOTCoverageProgram(
                        runtimeHeader,
                        programData,
                        coverageContext,
                        coverage);

                VividMaterialData materialParameters = (VividMaterialData) 0;
                VividSurfaceBindingData surfaceBinding =
                    (VividSurfaceBindingData) 0;
                const bool payloadLoaded =
                    runtimeHeader.ProgramID < _MaterialProgramCount
                    && programData.Version == VIVID_MATERIAL_PROGRAM_VERSION
                    && programData.SurfaceProgramID
                        == VIVIDMATERIALSURFACEPROGRAMID_STANDARD_SINGLE_SLAB
                    && programData.ParameterLayoutID
                        == VIVIDMATERIALPARAMETERLAYOUTID_LEGACY_MATERIAL_DATA
                    && programData.ResourceLayoutID
                        == VIVIDMATERIALRESOURCELAYOUTID_LEGACY_SURFACE_BINDING
                    && runtimeHeader.ParameterAddress < _MaterialDataCount
                    && runtimeHeader.ResourceBindingAddress < _SurfaceBindingDataCount;
                if (payloadLoaded)
                {{
                    materialParameters = PullMaterialData(
                        runtimeHeader.ParameterAddress);
                    surfaceBinding = PullSurfaceBindingData(
                        runtimeHeader.ResourceBindingAddress);
                }}

                VividAOTSurfaceContext surfaceContext;
                surfaceContext.UV0 = coverageContext.UV0;
                surfaceContext.UV0Ddx = coverageContext.UV0Ddx;
                surfaceContext.UV0Ddy = coverageContext.UV0Ddy;
                surfaceContext.GeometryNormalWS = float3(0.0f, 0.0f, 1.0f);
                surfaceContext.GeometryTangentWS = float4(1.0f, 0.0f, 0.0f, 1.0f);
                surfaceContext.PositionCS = input.positionCS;
                VividAOTSurfaceProgramOutput surfaceOutput =
                    (VividAOTSurfaceProgramOutput) 0;
                const bool surfaceSucceeded = payloadLoaded
                    && VividTryEvaluateAOTSurfaceProgram(
                        runtimeHeader.ProgramID,
                        materialParameters,
                        (VividDualSlabMaterialData) 0,
                        surfaceBinding,
                        (VividSurfaceBindingData) 0,
                        surfaceContext,
                        surfaceOutput);

                const uint outputIndex = (uint) input.positionCS.x;
                if (outputIndex == 0u)
                {{
                    return float4(
                        coverage.Coverage,
                        coverage.AlphaClipThreshold,
                        coverageSucceeded ? 1.0f : 0.0f,
                        surfaceSucceeded ? 1.0f : 0.0f);
                }}
                if (outputIndex == 1u)
                {{
                    return float4(
                        surfaceOutput.BaseSlab.BaseColor.rgb,
                        surfaceOutput.BaseSlab.PerceptualRoughness);
                }}
                return float4(
                    surfaceOutput.BaseSlab.Metallic,
                    surfaceOutput.Emission);
            }}
            ENDHLSL
        }}
    }}
}}";
        }

        private static void RequireShaderModel66()
        {
            File.WriteAllText(
                CapabilityShaderPath,
                @"Shader ""Hidden/VividRP/Tests/MaterialProgramAotGpuCapability""
{
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma use_dxc
            #pragma editor_sync_compilation
            #pragma require Int64BufferAtomics
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(uint vertexID : SV_VertexID) : SV_POSITION
            {
                const float2 corner = float2(
                    (vertexID << 1u) & 2u,
                    vertexID & 2u);
                return float4(corner * 2.0f - 1.0f, 0.0f, 1.0f);
            }

            float4 Frag() : SV_Target
            {
                return 0.0f.xxxx;
            }
            ENDHLSL
        }
    }
}");
            AssetDatabase.ImportAsset(
                CapabilityShaderPath,
                ImportAssetOptions.ForceSynchronousImport);
            Shader capabilityShader =
                AssetDatabase.LoadAssetAtPath<Shader>(CapabilityShaderPath);
            if (capabilityShader == null
                || !capabilityShader.isSupported
                || ShaderUtil.ShaderHasError(capabilityShader))
            {
                Assert.Ignore(
                    "The current D3D12 device/editor cannot compile the required DXC Shader Model 6.6 feature set.");
            }
        }

        private static void EnsureTemporaryFolder()
        {
            AssetDatabase.DeleteAsset(TemporaryFolder);
            if (!AssetDatabase.IsValidFolder(TemporaryFolder))
            {
                AssetDatabase.CreateFolder(
                    "Assets",
                    "VividMaterialProgramAotGpuTests");
            }
        }

        private static void AssertColor(
            Color actual,
            Color expected,
            string message)
        {
            const float tolerance = 0.015f;
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance), message);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance), message);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance), message);
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance), message);
        }
    }
}
