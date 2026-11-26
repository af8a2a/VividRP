using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class VividPhase3MaterialPipelineTests
    {
        [Test]
        public void MaterialBinningContract_MapsProgramIdsAndComputesIndirectOffsetsCorrectly()
        {
            Assert.That(VividMaterialBinningContract.EmptyBinIndex, Is.EqualTo(0));
            Assert.That(VividMaterialBinningContract.MaxDirectProgramBins, Is.EqualTo(64));
            Assert.That(VividMaterialBinningContract.OverflowBinIndex, Is.EqualTo(65));
            Assert.That(VividMaterialBinningContract.TotalBinCount, Is.EqualTo(66));
            Assert.That(VividMaterialBinningContract.TotalIndirectArgsUints, Is.EqualTo(198));

            Assert.That(VividMaterialBinningContract.MapProgramIdToBinIndex(0u, false), Is.EqualTo(65));
            Assert.That(VividMaterialBinningContract.MapProgramIdToBinIndex(0u, true), Is.EqualTo(65));
            Assert.That(VividMaterialBinningContract.MapProgramIdToBinIndex(1u, true), Is.EqualTo(1));
            Assert.That(VividMaterialBinningContract.MapProgramIdToBinIndex(64u, true), Is.EqualTo(64));
            Assert.That(VividMaterialBinningContract.MapProgramIdToBinIndex(65u, true), Is.EqualTo(65));

            Assert.That(VividMaterialBinningContract.GetIndirectArgsByteOffset(0), Is.EqualTo(0));
            Assert.That(VividMaterialBinningContract.GetIndirectArgsByteOffset(7), Is.EqualTo(84));
            Assert.That(VividMaterialBinningContract.ComputeTileStride(1920, 1080), Is.EqualTo(240 * 135));
            Assert.That(
                VividMaterialBinningContract.ComputeTileListElementCount(1920, 1080),
                Is.EqualTo(66 * 240 * 135));
        }

        [Test]
        public void MaterialGenericLayoutInspectorModel_ResolvesLocalInheritedAndDefaultParameters()
        {
            var layout = new MaterialGenericLayout(
                new[]
                {
                    new MaterialParameterDeclaration("BaseColor", MaterialValueType.Float3),
                    new MaterialParameterDeclaration("Roughness", MaterialValueType.Float),
                    new MaterialParameterDeclaration("UseCoat", MaterialValueType.Bool),
                },
                new[]
                {
                    new MaterialResourceDeclaration("AlbedoTex", MaterialValueType.Texture2D, MaterialTextureSampleClass.Raw),
                });

            var model = MaterialGenericLayoutInspectorModel.Build(
                layout,
                (in MaterialParameterDeclaration decl, out Vector4 val, out bool inherited) =>
                {
                    if (decl.Symbol == "BaseColor")
                    {
                        val = new Vector4(0.8f, 0.2f, 0.1f, 1.0f);
                        inherited = false;
                        return true;
                    }
                    if (decl.Symbol == "Roughness")
                    {
                        val = new Vector4(0.35f, 0f, 0f, 0f);
                        inherited = true;
                        return true;
                    }
                    val = default;
                    inherited = false;
                    return false;
                },
                (in MaterialResourceDeclaration decl, out GPUDrivenMaterialTextureOverride tex, out bool inherited) =>
                {
                    tex = default;
                    inherited = false;
                    return false;
                });

            Assert.That(model.Parameters.Count, Is.EqualTo(3));
            Assert.That(model.Textures.Count, Is.EqualTo(1));
            Assert.That(model.ParameterByteStride, Is.GreaterThanOrEqualTo(20));

            MaterialInstanceParameterField baseColorField = default;
            MaterialInstanceParameterField roughnessField = default;
            MaterialInstanceParameterField useCoatField = default;
            for (int i = 0; i < model.Parameters.Count; i++)
            {
                var p = model.Parameters[i];
                if (p.Symbol == "BaseColor") baseColorField = p;
                else if (p.Symbol == "Roughness") roughnessField = p;
                else if (p.Symbol == "UseCoat") useCoatField = p;
            }

            Assert.That(baseColorField.IsOverriddenLocally, Is.True);
            Assert.That(baseColorField.IsInheritedFromParent, Is.False);
            Assert.That(baseColorField.EffectiveValue.x, Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(MaterialGenericLayoutInspectorModel.IsLikelyColorSymbol(baseColorField.Symbol), Is.True);

            Assert.That(roughnessField.IsOverriddenLocally, Is.False);
            Assert.That(roughnessField.IsInheritedFromParent, Is.True);
            Assert.That(roughnessField.EffectiveValue.x, Is.EqualTo(0.35f).Within(1e-5f));

            Assert.That(useCoatField.IsOverriddenLocally, Is.False);
            Assert.That(useCoatField.IsInheritedFromParent, Is.False);
            Assert.That(useCoatField.EffectiveValue.x, Is.EqualTo(0.0f));
        }

        [Test]
        public void MaterialGraph_Phase3NodeLibrary_NormalMapMathNoiseAndRemap_CompilesToValidHlsl()
        {
            // Graph A: NormalMapDecode + ValueNoise2D + GradientNoise2D + Clamp + Smoothstep + Remap
            var graphA = new MaterialGraph();
            MaterialGraphValue uvA = graphA.ExternalInput("uv", MaterialExternalInput.UV0);
            MaterialGraphValue nwsA = graphA.ExternalInput("nws", MaterialExternalInput.GeometryNormalWS);
            MaterialGraphValue zeroA = graphA.Constant("zero", 0.0f);
            MaterialGraphValue oneA = graphA.Constant("one", 1.0f);
            MaterialGraphValue scaleA = graphA.Constant("scale", 6.0f);
            MaterialGraphValue vnA = graphA.ValueNoise2D("vnoise", uvA, scaleA);
            MaterialGraphValue gnA = graphA.GradientNoise2D("gnoise", uvA, scaleA);
            MaterialGraphValue clampedA = graphA.Clamp("clamp", vnA, zeroA, oneA);
            MaterialGraphValue smoothedA = graphA.Smoothstep("smooth", zeroA, oneA, gnA);
            MaterialGraphValue remappedA = graphA.Remap("remap", clampedA, zeroA, oneA, zeroA, smoothedA);
            MaterialGraphValue unpackedN = graphA.UnpackNormal("unpackN", graphA.Constant("packedN", new float4(0.5f, 0.5f, 0.5f, 0.5f)), oneA);
            MaterialGraphValue normOut = graphA.Normalize("normOut", graphA.Add("addN", nwsA, unpackedN));
            MaterialGraphValue white3A = graphA.Constant("white3", new float3(1.0f, 1.0f, 1.0f));
            MaterialGraphClosure openPbrA = graphA.OpenPBROpaque(
                "pbr", oneA, white3A, remappedA, zeroA, oneA, white3A, remappedA,
                graphA.Constant("ior", 1.5f), normOut, zeroA, graphA.Constant("emC", float3.zero));
            graphA.Output("out", openPbrA, oneA, zeroA, graphA.Constant("em", float3.zero),
                MaterialFeatureMask.None, MaterialShadingModelMask.OpenPBROpaque);

            MaterialGraphCompilationResult compiledA = MaterialGraphCompiler.Compile(
                graphA,
                GPUDrivenMaterialCompiler.ProgramVersion);
            Assert.That(
                compiledA.Succeeded,
                Is.True,
                string.Join("\n", System.Linq.Enumerable.Select(compiledA.Diagnostics, d => $"{d.Severity} {d.Code} {d.SourceNodeId}.{d.SourcePort}: {d.Message}")));
            string hlslA = compiledA.Program.SurfaceHlsl.Source;
            StringAssert.Contains("VividAOTValueNoise2D(", hlslA);
            StringAssert.Contains("VividAOTGradientNoise2D(", hlslA);
            StringAssert.Contains("VividAOTUnpackNormalTS(", hlslA);
            StringAssert.Contains("smoothstep(", hlslA);
            StringAssert.Contains("clamp(", hlslA);

            // Graph B: Abs + Sin + Cos + Floor + Frac + Sqrt + Pow
            var graphB = new MaterialGraph();
            MaterialGraphValue uvB = graphB.ExternalInput("uv", MaterialExternalInput.UV0);
            MaterialGraphValue nwsB = graphB.ExternalInput("nws", MaterialExternalInput.GeometryNormalWS);
            MaterialGraphValue zeroB = graphB.Constant("zero", 0.0f);
            MaterialGraphValue oneB = graphB.Constant("one", 1.0f);
            MaterialGraphValue uB = graphB.Swizzle("u", uvB, MaterialSwizzleMask.X);
            MaterialGraphValue vB = graphB.Swizzle("v", uvB, MaterialSwizzleMask.Y);
            MaterialGraphValue mathA = graphB.Sqrt("sqrt", graphB.Abs("abs", graphB.Sin("sin", uB)));
            MaterialGraphValue mathB = graphB.Pow("pow", graphB.Frac("frac", graphB.Cos("cos", vB)), graphB.Floor("floor", graphB.Constant("two", 2.5f)));
            MaterialGraphValue roughB = graphB.Saturate("r", graphB.Add("sum", mathA, mathB));
            MaterialGraphValue white3B = graphB.Constant("white3", new float3(1.0f, 1.0f, 1.0f));
            MaterialGraphClosure openPbrB = graphB.OpenPBROpaque(
                "pbr", oneB, white3B, roughB, zeroB, oneB, white3B, roughB,
                graphB.Constant("ior", 1.5f), nwsB, zeroB, graphB.Constant("emC", float3.zero));
            graphB.Output("out", openPbrB, oneB, zeroB, graphB.Constant("em", float3.zero),
                MaterialFeatureMask.None, MaterialShadingModelMask.OpenPBROpaque);

            MaterialGraphCompilationResult compiledB = MaterialGraphCompiler.Compile(
                graphB,
                GPUDrivenMaterialCompiler.ProgramVersion);
            Assert.That(
                compiledB.Succeeded,
                Is.True,
                string.Join("\n", System.Linq.Enumerable.Select(compiledB.Diagnostics, d => $"{d.Severity} {d.Code} {d.SourceNodeId}.{d.SourcePort}: {d.Message}")));
            string hlslB = compiledB.Program.SurfaceHlsl.Source;
            StringAssert.Contains("pow(", hlslB);
            StringAssert.Contains("sin(", hlslB);
            StringAssert.Contains("cos(", hlslB);
            StringAssert.Contains("abs(", hlslB);
            StringAssert.Contains("floor(", hlslB);
            StringAssert.Contains("frac(", hlslB);
            StringAssert.Contains("sqrt(", hlslB);

            // Also validate Phase 3.1 standalone per-ProgramID binned compute shader generation
            string standalone = MaterialSurfaceHlslSourceBuilder.GenerateStandaloneBinnedComputeShader(
                compiledA.Program.SurfaceHlsl,
                compiledA.Program.DeferredExportContract,
                7u);
            StringAssert.Contains("#pragma kernel ResolveVisibilityBufferProgram_7", standalone);
            StringAssert.Contains(compiledA.Program.SurfaceHlsl.EntryPoint, standalone);
            Assert.That(standalone.Contains("switch("), Is.False);
        }

        [Test]
        public void MaterialGraph_SubgraphInlining_BindsParametersAndRejectsCycles()
        {
            // Build reusable procedural roughness subgraph: Smoothstep(0.1, 0.9, GradientNoise2D(UV, Scale))
            var subgraph = new MaterialGraph();
            MaterialGraphValue subUv = subgraph.Parameter("sub_uv", new MaterialParameterDeclaration("InUV", MaterialValueType.Float2));
            MaterialGraphValue subScale = subgraph.Parameter("sub_scale", new MaterialParameterDeclaration("InScale", MaterialValueType.Float));
            MaterialGraphValue subNoise = subgraph.GradientNoise2D("sub_noise", subUv, subScale);
            subgraph.Smoothstep("sub_out", subgraph.Constant("e0", 0.1f), subgraph.Constant("e1", 0.9f), subNoise);

            // Host graph inlines the subgraph twice with different scale bindings
            var host = new MaterialGraph();
            MaterialGraphValue hostUv = host.ExternalInput("uv", MaterialExternalInput.UV0);
            MaterialGraphValue hostNormal = host.ExternalInput("nws", MaterialExternalInput.GeometryNormalWS);

            MaterialGraphValue coarseRough = host.InlineValueSubgraph(
                "sg_coarse",
                subgraph,
                "sub_out",
                new Dictionary<string, MaterialGraphValue>
                {
                    ["InUV"] = hostUv,
                    ["InScale"] = host.Constant("scale_coarse", 4.0f),
                });
            MaterialGraphValue fineRough = host.InlineValueSubgraph(
                "sg_fine",
                subgraph,
                "sub_out",
                new Dictionary<string, MaterialGraphValue>
                {
                    ["InUV"] = hostUv,
                    ["InScale"] = host.Constant("scale_fine", 16.0f),
                });

            MaterialGraphValue finalRough = host.Lerp("mix_rough", coarseRough, fineRough, host.Constant("blend", 0.35f));
            MaterialGraphClosure openPbr = host.OpenPBROpaque(
                "pbr",
                host.Constant("bw", 1.0f),
                host.Constant("bc", new float3(0.6f, 0.5f, 0.4f)),
                finalRough,
                host.Constant("m", 0.0f),
                host.Constant("sw", 1.0f),
                host.Constant("sc", new float3(1.0f, 1.0f, 1.0f)),
                finalRough,
                host.Constant("ior", 1.5f),
                hostNormal,
                host.Constant("el", 0.0f),
                host.Constant("ec", float3.zero));
            host.Output(
                "out",
                openPbr,
                host.Constant("cov", 1.0f),
                host.Constant("clip", 0.0f),
                host.Constant("em", float3.zero),
                MaterialFeatureMask.None,
                MaterialShadingModelMask.OpenPBROpaque);

            MaterialGraphCompilationResult compiled = MaterialGraphCompiler.Compile(
                host,
                GPUDrivenMaterialCompiler.ProgramVersion);
            Assert.That(
                compiled.Succeeded,
                Is.True,
                string.Join("\n", System.Linq.Enumerable.Select(compiled.Diagnostics, d => $"{d.Severity} {d.Code} {d.SourceNodeId}.{d.SourcePort}: {d.Message}")));
            StringAssert.Contains("VividAOTGradientNoise2D(", compiled.Program.SurfaceHlsl.Source);

            // Self-referencing subgraph must throw cleanly
            Assert.Throws<InvalidOperationException>(() =>
                host.InlineValueSubgraph("self_cycle", host, "mix_rough"));
        }
    }
}
