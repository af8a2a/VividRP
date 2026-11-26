using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class MaterialSlabHlslPayload
    {
        internal string PropertiesBlock { get; set; } = string.Empty;

        internal string RasterCBufferBlock { get; set; } = string.Empty;

        internal string RasterTexturesBlock { get; set; } = string.Empty;

        internal string RasterEvaluateFunction { get; set; } = string.Empty;

        internal string ComputeEvaluateFunction { get; set; } = string.Empty;

        internal int ParameterStrideInWords { get; set; }

        internal VividSlabParameterSlot[] ParameterSlots { get; set; } = Array.Empty<VividSlabParameterSlot>();

        internal VividSlabResourceSlot[] ResourceSlots { get; set; } = Array.Empty<VividSlabResourceSlot>();

        internal int EstimatedSubstrateBytesPerPixel { get; set; } =
            VividSubstrateContract.OpenPBROpaqueSingleTotalSlices * VividSubstrateContract.BytesPerSlice;
    }

    /// <summary>
    /// Dual-target StageLIR -> HLSL backend for <c>*.slab.shader</c> (Phase S1).
    /// Lowers a single <see cref="CompiledMaterialProgram"/> into:
    /// <list type="bullet">
    ///   <item>Raster HLSL reading <c>UnityPerMaterial</c> CBUFFER and <c>TEXTURE2D</c></item>
    ///   <item>Standalone GPUDriven Compute HLSL reading <c>_MaterialParameterData</c> and
    ///         VT-first / Bindless <c>VividSurfaceSampling.hlsl</c></item>
    /// </list>
    /// Both targets output identical <c>VividOpenPBRV2Inputs</c> into Substrate Closure Buffer.
    /// </summary>
    internal static class MaterialSlabHlslBackend
    {
        internal static MaterialSlabHlslPayload Compile(CompiledMaterialProgram program)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));

            MaterialIRModule module = program.Module;
            MaterialStageLIR stageLIR = program.Lowering.SurfaceProgram.StageLIR;
            MaterialGenericLayout genericLayout = program.Lowering.GenericLayout;
            MaterialIRVerifier.VerifyStageLIR(stageLIR).ThrowIfInvalid();

            ClosureExpressionNode root = module.ClosureGraph.GetNode(module.SurfaceClosure);
            if (root.Opcode != ClosureExpressionOpcode.OpenPBROpaque
                && root.Opcode != ClosureExpressionOpcode.Slab)
            {
                throw new NotSupportedException(
                    $"Phase S1 Slab backend supports single-closure OpenPBROpaque and SingleSlab graphs (got '{root.Opcode}'). Multi-Closure is scheduled for Phase S2.");
            }

            VividSlabParameterSlot[] paramSlots = BuildParameterSlots(genericLayout);
            VividSlabResourceSlot[] resSlots = BuildResourceSlots(genericLayout);

            int estimatedBytes = module.Topology.EstimateSubstrateSliceCount()
                * VividSubstrateContract.BytesPerSlice;

            return new MaterialSlabHlslPayload
            {
                PropertiesBlock = BuildPropertiesBlock(paramSlots, resSlots),
                RasterCBufferBlock = BuildRasterCBufferBlock(paramSlots, resSlots),
                RasterTexturesBlock = BuildRasterTexturesBlock(resSlots),
                RasterEvaluateFunction = EmitEvaluateFunction(module, stageLIR, genericLayout, isCompute: false),
                ComputeEvaluateFunction = EmitEvaluateFunction(module, stageLIR, genericLayout, isCompute: true),
                ParameterStrideInWords = genericLayout.ParameterStrideInWords,
                ParameterSlots = paramSlots,
                ResourceSlots = resSlots,
                EstimatedSubstrateBytesPerPixel = estimatedBytes,
            };
        }

        internal static string ToShaderPropertyName(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return "_Param";
            return symbol[0] == '_' ? symbol : "_" + symbol;
        }

        private static VividSlabParameterSlot[] BuildParameterSlots(MaterialGenericLayout layout)
        {
            var slots = new VividSlabParameterSlot[layout.ParameterBindings.Count];
            for (int i = 0; i < layout.ParameterBindings.Count; i++)
            {
                MaterialGenericParameterBinding binding = layout.ParameterBindings[i];
                slots[i] = new VividSlabParameterSlot
                {
                    Symbol = binding.Declaration.Symbol,
                    PropertyName = ToShaderPropertyName(binding.Declaration.Symbol),
                    Kind = VividSlabProgramDescriptor.FromMaterialValueType(binding.Declaration.Type),
                    WordOffset = binding.WordOffset,
                    WordCount = binding.WordCount,
                    DefaultValue = GetDefaultParameterValue(binding.Declaration),
                };
            }
            return slots;
        }

        private static VividSlabResourceSlot[] BuildResourceSlots(MaterialGenericLayout layout)
        {
            var slots = new VividSlabResourceSlot[layout.ResourceBindings.Count];
            for (int i = 0; i < layout.ResourceBindings.Count; i++)
            {
                MaterialGenericResourceBinding binding = layout.ResourceBindings[i];
                slots[i] = new VividSlabResourceSlot
                {
                    Symbol = binding.Declaration.Symbol,
                    PropertyName = ToShaderPropertyName(binding.Declaration.Symbol),
                    SampleKind = VividSlabProgramDescriptor.FromSampleClass(binding.Declaration.SampleClass),
                    SlotIndex = binding.Slot,
                };
            }
            return slots;
        }

        private static Vector4 GetDefaultParameterValue(in MaterialParameterDeclaration decl)
        {
            OpenPBROpaqueInputs defaults = OpenPBROpaqueContract.CreateDefault();
            switch (decl.Symbol)
            {
                case "BaseWeight": return new Vector4(defaults.BaseWeight, 0f, 0f, 0f);
                case "BaseColor": return new Vector4(defaults.BaseColor.x, defaults.BaseColor.y, defaults.BaseColor.z, 1f);
                case "BaseDiffuseRoughness": return new Vector4(defaults.BaseDiffuseRoughness, 0f, 0f, 0f);
                case "BaseMetalness":
                case "Metallic": return new Vector4(0f, 0f, 0f, 0f);
                case "SpecularWeight": return new Vector4(defaults.SpecularWeight, 0f, 0f, 0f);
                case "SpecularColor": return new Vector4(defaults.SpecularColor.x, defaults.SpecularColor.y, defaults.SpecularColor.z, 1f);
                case "SpecularRoughness":
                case "Roughness": return new Vector4(defaults.SpecularRoughness, 0f, 0f, 0f);
                case "SpecularIor": return new Vector4(defaults.SpecularIor, 0f, 0f, 0f);
                case "NormalWS": return new Vector4(defaults.NormalWS.x, defaults.NormalWS.y, defaults.NormalWS.z, 0f);
                case "EmissionLuminance": return new Vector4(0f, 0f, 0f, 0f);
                case "EmissionColor":
                case "Emission": return new Vector4(0f, 0f, 0f, 1f);
                case "AlphaClipThreshold": return new Vector4(0f, 0f, 0f, 0f);
                default:
                    return decl.Type switch
                    {
                        MaterialValueType.Bool => new Vector4(0f, 0f, 0f, 0f),
                        MaterialValueType.Float => new Vector4(0.5f, 0f, 0f, 0f),
                        MaterialValueType.Float2 => new Vector4(0.5f, 0.5f, 0f, 0f),
                        MaterialValueType.Float3 => new Vector4(1f, 1f, 1f, 1f),
                        _ => new Vector4(1f, 1f, 1f, 1f),
                    };
            }
        }

        private static string BuildPropertiesBlock(
            VividSlabParameterSlot[] paramSlots,
            VividSlabResourceSlot[] resSlots)
        {
            if (paramSlots.Length == 0 && resSlots.Length == 0)
                return "// [VividSlab] No linked MaterialGraph properties.";

            var sb = new StringBuilder(512);
            sb.AppendLine("// [VividSlab] Auto-generated MaterialGraph Properties");
            for (int i = 0; i < paramSlots.Length; i++)
            {
                VividSlabParameterSlot p = paramSlots[i];
                switch (p.Kind)
                {
                    case VividSlabParameterKind.Bool:
                        sb.Append("        [Toggle] ")
                            .Append(p.PropertyName).Append(" (\"")
                            .Append(p.Symbol).Append("\", Float) = ")
                            .AppendLine(FormatShaderLabFloat(p.DefaultValue.x));
                        break;
                    case VividSlabParameterKind.Float:
                        sb.Append("        ")
                            .Append(p.PropertyName).Append(" (\"")
                            .Append(p.Symbol).Append("\", Float) = ")
                            .AppendLine(FormatShaderLabFloat(p.DefaultValue.x));
                        break;
                    default:
                        bool isColor = p.Symbol.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0
                            || string.Equals(p.Symbol, "Emission", StringComparison.OrdinalIgnoreCase);
                        string shaderType = isColor ? "Color" : "Vector";
                        sb.Append("        ")
                            .Append(p.PropertyName).Append(" (\"")
                            .Append(p.Symbol).Append("\", ").Append(shaderType).Append(") = (")
                            .Append(FormatShaderLabFloat(p.DefaultValue.x)).Append(", ")
                            .Append(FormatShaderLabFloat(p.DefaultValue.y)).Append(", ")
                            .Append(FormatShaderLabFloat(p.DefaultValue.z)).Append(", ")
                            .Append(FormatShaderLabFloat(p.DefaultValue.w)).AppendLine(")");
                        break;
                }
            }

            for (int i = 0; i < resSlots.Length; i++)
            {
                VividSlabResourceSlot r = resSlots[i];
                string defaultTex = r.SampleKind == VividSlabTextureSampleKind.Normal ? "bump" : "white";
                sb.Append("        ")
                    .Append(r.PropertyName).Append(" (\"")
                    .Append(r.Symbol).Append("\", 2D) = \"")
                    .Append(defaultTex).AppendLine("\" {}");
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildRasterCBufferBlock(
            VividSlabParameterSlot[] paramSlots,
            VividSlabResourceSlot[] resSlots)
        {
            if (paramSlots.Length == 0 && resSlots.Length == 0)
                return "// [VividSlab] No linked MaterialGraph CBUFFER fields.";

            var sb = new StringBuilder(512);
            sb.AppendLine("// [VividSlab] Auto-generated MaterialGraph UnityPerMaterial fields");
            for (int i = 0; i < paramSlots.Length; i++)
            {
                VividSlabParameterSlot p = paramSlots[i];
                string hlslType = p.Kind == VividSlabParameterKind.Bool || p.Kind == VividSlabParameterKind.Float
                    ? "float"
                    : "float4";
                sb.Append("        ").Append(hlslType).Append(' ').Append(p.PropertyName).AppendLine(";");
            }

            for (int i = 0; i < resSlots.Length; i++)
            {
                sb.Append("        float4 ").Append(resSlots[i].PropertyName).AppendLine("_ST;");
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildRasterTexturesBlock(VividSlabResourceSlot[] resSlots)
        {
            if (resSlots.Length == 0)
                return "// [VividSlab] No linked MaterialGraph texture declarations.";

            var sb = new StringBuilder(256);
            sb.AppendLine("// [VividSlab] Auto-generated MaterialGraph Textures");
            for (int i = 0; i < resSlots.Length; i++)
            {
                string prop = resSlots[i].PropertyName;
                sb.Append("        TEXTURE2D(").Append(prop).AppendLine(");");
                sb.Append("        SAMPLER(sampler").Append(prop).AppendLine(");");
            }

            return sb.ToString().TrimEnd();
        }

        private static string EmitEvaluateFunction(
            MaterialIRModule module,
            MaterialStageLIR stageLIR,
            MaterialGenericLayout genericLayout,
            bool isCompute)
        {
            var sb = new StringBuilder(Math.Max(1536, stageLIR.NodeCount * 96));
            sb.AppendLine("void VividSlabEvaluateGraphSurface(");
            sb.AppendLine("    const VividSlabEvalContext ctx,");
            sb.AppendLine("    out VividOpenPBRV2Inputs v2Inputs,");
            sb.AppendLine("    out float ambientOcclusion)");
            sb.AppendLine("{");
            sb.AppendLine("    v2Inputs = VividCreateDefaultOpenPBRV2Inputs();");
            sb.AppendLine("    ambientOcclusion = 1.0f;");

            for (int nodeIndex = 0; nodeIndex < stageLIR.Nodes.Count; nodeIndex++)
            {
                MaterialStageLIRNode node = stageLIR.Nodes[nodeIndex];
                if (node.Opcode == MaterialStageLIROpcode.TextureResource)
                    continue;

                if (node.Opcode == MaterialStageLIROpcode.TextureSampleGrad)
                {
                    EmitTextureSample(sb, stageLIR, node, nodeIndex, genericLayout, isCompute);
                    continue;
                }

                sb.Append("    const ")
                    .Append(GetHlslType(node.Type))
                    .Append(' ')
                    .Append(Value(nodeIndex))
                    .Append(" = ")
                    .Append(EmitNodeExpression(stageLIR, node, genericLayout, isCompute))
                    .AppendLine(";");
            }

            ClosureExpressionNode root = module.ClosureGraph.GetNode(module.SurfaceClosure);
            if (root.Opcode == ClosureExpressionOpcode.OpenPBROpaque)
            {
                ClosureOpenPBROpaqueExpression pbr = root.OpenPBROpaque;
                sb.Append("    v2Inputs.core.baseWeight = saturate(").Append(Mapped(stageLIR, pbr.BaseWeight)).AppendLine(");");
                sb.Append("    v2Inputs.core.baseColor = saturate(").Append(Mapped(stageLIR, pbr.BaseColor)).AppendLine(");");
                sb.Append("    v2Inputs.core.baseDiffuseRoughness = saturate(").Append(Mapped(stageLIR, pbr.BaseDiffuseRoughness)).AppendLine(");");
                sb.Append("    v2Inputs.core.baseMetalness = saturate(").Append(Mapped(stageLIR, pbr.BaseMetalness)).AppendLine(");");
                sb.Append("    v2Inputs.core.specularWeight = saturate(").Append(Mapped(stageLIR, pbr.SpecularWeight)).AppendLine(");");
                sb.Append("    v2Inputs.core.specularColor = saturate(").Append(Mapped(stageLIR, pbr.SpecularColor)).AppendLine(");");
                sb.Append("    v2Inputs.core.specularRoughness = max(saturate(").Append(Mapped(stageLIR, pbr.SpecularRoughness)).AppendLine("), VIVID_STANDARD_LIT_MIN_ROUGHNESS);");
                sb.Append("    v2Inputs.core.specularIor = VividSanitizeStandardLitSpecularIor(").Append(Mapped(stageLIR, pbr.SpecularIor)).AppendLine(");");
                sb.Append("    v2Inputs.core.normalWS = VividSanitizeSurfaceSummaryNormal(").Append(Mapped(stageLIR, pbr.NormalWS)).AppendLine(");");
                sb.Append("    v2Inputs.core.emissionLuminance = max(").Append(Mapped(stageLIR, pbr.EmissionLuminance)).AppendLine(", 0.0f);");
                sb.Append("    v2Inputs.core.emissionColor = max(").Append(Mapped(stageLIR, pbr.EmissionColor)).AppendLine(", 0.0f);");
                sb.AppendLine("    v2Inputs.coatNormalWS = v2Inputs.core.normalWS;");
                sb.AppendLine("    v2Inputs.tangentWS = dot(ctx.geometryTangentWS.xyz, ctx.geometryTangentWS.xyz) > 1e-8f ? normalize(ctx.geometryTangentWS.xyz) : float3(1.0f, 0.0f, 0.0f);");
                sb.AppendLine("    v2Inputs.tangentHandedness = ctx.geometryTangentWS.w < 0.0f ? -1.0f : 1.0f;");
            }
            else
            {
                ClosureSlabExpression slab = root.Slab;
                sb.AppendLine("    VividStandardLitSurfaceParams slabParams = VividCreateDefaultStandardLitSurfaceParams();");
                sb.Append("    slabParams.baseColor = (").Append(Mapped(stageLIR, slab.BaseColor)).AppendLine(").rgb;");
                sb.Append("    slabParams.smoothness = saturate(1.0f - saturate(").Append(Mapped(stageLIR, slab.Roughness)).AppendLine("));");
                sb.Append("    slabParams.metallic = saturate(").Append(Mapped(stageLIR, slab.Metallic)).AppendLine(");");
                sb.Append("    slabParams.normalWS = VividSanitizeSurfaceSummaryNormal(").Append(Mapped(stageLIR, slab.Normal)).AppendLine(");");
                sb.AppendLine("    slabParams.coatNormalWS = slabParams.normalWS;");
                sb.Append("    float4 slabTangent = ").Append(Mapped(stageLIR, slab.Tangent)).AppendLine(";");
                sb.AppendLine("    slabParams.tangentWS = dot(slabTangent.xyz, slabTangent.xyz) > 1e-8f ? normalize(slabTangent.xyz) : float3(1.0f, 0.0f, 0.0f);");
                sb.AppendLine("    slabParams.tangentHandedness = slabTangent.w < 0.0f ? -1.0f : 1.0f;");
                sb.Append("    slabParams.emission = max(").Append(Mapped(stageLIR, module.Outputs.Emission)).AppendLine(", 0.0f);");
                sb.AppendLine("    v2Inputs = VividStandardLitToOpenPBRV2Inputs(slabParams);");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void EmitTextureSample(
            StringBuilder sb,
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode sample,
            int sampleIndex,
            MaterialGenericLayout genericLayout,
            bool isCompute)
        {
            MaterialStageLIRNode resourceNode = stageLIR.Nodes[sample.Operand0];
            if (!stageLIR.Values.TryGetResourceDeclaration(
                    resourceNode.Semantic,
                    out MaterialResourceDeclaration declaration)
                || !genericLayout.TryGetResourceBinding(
                    declaration,
                    out MaterialGenericResourceBinding genericBinding))
            {
                throw new NotSupportedException(
                    $"Slab texture declaration @{resourceNode.Semantic} has no generic binding.");
            }

            if (!isCompute)
            {
                string prop = ToShaderPropertyName(declaration.Symbol);
                sb.Append("    const float4 ")
                    .Append(Value(sampleIndex))
                    .Append(" = SAMPLE_TEXTURE2D(")
                    .Append(prop).Append(", sampler").Append(prop)
                    .Append(", (").Append(Value(sample.Operand1)).Append(") * ")
                    .Append(prop).Append("_ST.xy + ").Append(prop).AppendLine("_ST.zw);");
                return;
            }

            string resVar = $"vivid_slab_res_{sampleIndex}";
            string bindVar = $"vivid_slab_bind_{sampleIndex}";
            string ctxVar = $"vivid_slab_sctx_{sampleIndex}";
            string sampleFn = declaration.SampleClass switch
            {
                MaterialTextureSampleClass.Raw => "VividSampleRawGrad",
                MaterialTextureSampleClass.Mask => "VividSampleMaskGrad",
                MaterialTextureSampleClass.Normal => "VividSampleNormalGrad",
                _ => "VividSampleBaseColorGrad",
            };

            sb.Append("    const VividMaterialResourceData ").Append(resVar)
                .Append(" = PullMaterialResourceData(ctx.resourceAddress + ")
                .Append(genericBinding.Slot.ToString(CultureInfo.InvariantCulture))
                .AppendLine("u);");
            sb.Append("    VividSurfaceBindingData ").Append(bindVar).AppendLine(";");
            sb.Append("    ").Append(bindVar).Append(".BaseColorResource = ").Append(resVar).AppendLine(".BaseColorResource;");
            sb.Append("    ").Append(bindVar).Append(".NormalResource = ").Append(resVar).AppendLine(".NormalResource;");
            sb.Append("    ").Append(bindVar).Append(".MaskResource = ").Append(resVar).AppendLine(".MaskResource;");
            sb.Append("    ").Append(bindVar).Append(".Flags = ").Append(resVar).AppendLine(".SurfaceBindingFlags;");
            sb.Append("    ").Append(bindVar).Append(".UVScaleBias = ").Append(resVar).AppendLine(".UVScaleBias;");
            sb.Append("    const VividSurfaceSampleContext ").Append(ctxVar)
                .Append(" = VividCreateSurfaceSampleContextGrad(").Append(bindVar).Append(", ")
                .Append(Value(sample.Operand1)).Append(" * ").Append(resVar).Append(".TextureTilingOffset.xy + ")
                .Append(resVar).Append(".TextureTilingOffset.zw, ")
                .Append(Value(sample.Operand2)).Append(" * ").Append(resVar).Append(".TextureTilingOffset.xy, ")
                .Append(Value(sample.Operand3)).Append(" * ").Append(resVar).Append(".TextureTilingOffset.xy, ctx.positionCS);")
                .AppendLine();
            sb.Append("    const float4 ").Append(Value(sampleIndex)).Append(" = ")
                .Append(sampleFn).Append('(').Append(bindVar).Append(", ").Append(ctxVar).AppendLine(");");
        }

        private static string EmitNodeExpression(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            MaterialGenericLayout genericLayout,
            bool isCompute)
        {
            switch (node.Opcode)
            {
                case MaterialStageLIROpcode.StageInput:
                    return (MaterialStageInput)node.Semantic switch
                    {
                        MaterialStageInput.UV0 => "ctx.uv0",
                        MaterialStageInput.UV0Ddx => "ctx.uv0Ddx",
                        MaterialStageInput.UV0Ddy => "ctx.uv0Ddy",
                        MaterialStageInput.GeometryNormalWS => "ctx.geometryNormalWS",
                        MaterialStageInput.GeometryTangentWS => "ctx.geometryTangentWS",
                        _ => throw new NotSupportedException($"Unsupported StageInput '{node.Semantic}'."),
                    };
                case MaterialStageLIROpcode.Constant:
                    return FormatConstant(node.Type, node.Constant);
                case MaterialStageLIROpcode.Parameter:
                    return EmitParameterExpression(stageLIR, node.Semantic, genericLayout, isCompute);
                case MaterialStageLIROpcode.Add: return $"({Value(node.Operand0)} + {Value(node.Operand1)})";
                case MaterialStageLIROpcode.Multiply: return $"({Value(node.Operand0)} * {Value(node.Operand1)})";
                case MaterialStageLIROpcode.Subtract: return $"({Value(node.Operand0)} - {Value(node.Operand1)})";
                case MaterialStageLIROpcode.Divide: return $"({Value(node.Operand0)} / {Value(node.Operand1)})";
                case MaterialStageLIROpcode.Lerp: return $"lerp({Value(node.Operand0)}, {Value(node.Operand1)}, {Value(node.Operand2)})";
                case MaterialStageLIROpcode.Select: return $"({Value(node.Operand0)} ? {Value(node.Operand1)} : {Value(node.Operand2)})";
                case MaterialStageLIROpcode.Swizzle: return EmitSwizzle(node);
                case MaterialStageLIROpcode.Compose: return EmitCompose(node);
                case MaterialStageLIROpcode.Min: return $"min({Value(node.Operand0)}, {Value(node.Operand1)})";
                case MaterialStageLIROpcode.Max: return $"max({Value(node.Operand0)}, {Value(node.Operand1)})";
                case MaterialStageLIROpcode.Saturate: return $"saturate({Value(node.Operand0)})";
                case MaterialStageLIROpcode.OneMinus: return $"(1.0f - {Value(node.Operand0)})";
                case MaterialStageLIROpcode.Dot: return $"dot({Value(node.Operand0)}, {Value(node.Operand1)})";
                case MaterialStageLIROpcode.Normalize: return $"normalize({Value(node.Operand0)})";
                case MaterialStageLIROpcode.Compare: return EmitCompare(node);
                case MaterialStageLIROpcode.Abs: return $"abs({Value(node.Operand0)})";
                case MaterialStageLIROpcode.Sin: return $"sin({Value(node.Operand0)})";
                case MaterialStageLIROpcode.Cos: return $"cos({Value(node.Operand0)})";
                case MaterialStageLIROpcode.Floor: return $"floor({Value(node.Operand0)})";
                case MaterialStageLIROpcode.Frac: return $"frac({Value(node.Operand0)})";
                case MaterialStageLIROpcode.Sqrt: return $"sqrt(max({Value(node.Operand0)}, 0.0f))";
                case MaterialStageLIROpcode.Pow: return $"pow(max({Value(node.Operand0)}, 1e-6f), {Value(node.Operand1)})";
                case MaterialStageLIROpcode.Clamp: return $"clamp({Value(node.Operand0)}, {Value(node.Operand1)}, {Value(node.Operand2)})";
                case MaterialStageLIROpcode.Smoothstep: return $"smoothstep({Value(node.Operand0)}, {Value(node.Operand1)}, {Value(node.Operand2)})";
                case MaterialStageLIROpcode.UnpackNormal: return $"VividAOTUnpackNormalTS({Value(node.Operand0)}, {Value(node.Operand1)})";
                case MaterialStageLIROpcode.ValueNoise2D: return $"VividAOTValueNoise2D({Value(node.Operand0)}, {Value(node.Operand1)})";
                case MaterialStageLIROpcode.GradientNoise2D: return $"VividAOTGradientNoise2D({Value(node.Operand0)}, {Value(node.Operand1)})";
                default:
                    throw new NotSupportedException($"Slab HLSL backend does not support opcode '{node.Opcode}'.");
            }
        }

        private static string EmitParameterExpression(
            MaterialStageLIR stageLIR,
            int semantic,
            MaterialGenericLayout genericLayout,
            bool isCompute)
        {
            if (!stageLIR.Values.TryGetParameterDeclaration(semantic, out MaterialParameterDeclaration decl)
                || !genericLayout.TryGetParameterBinding(decl, out MaterialGenericParameterBinding binding))
            {
                throw new NotSupportedException($"Slab parameter declaration #{semantic} has no generic binding.");
            }

            if (!isCompute)
            {
                string prop = ToShaderPropertyName(decl.Symbol);
                return decl.Type switch
                {
                    MaterialValueType.Bool => $"({prop} != 0.0f)",
                    MaterialValueType.Float => prop,
                    MaterialValueType.Float2 => $"({prop}).xy",
                    MaterialValueType.Float3 => $"({prop}).xyz",
                    MaterialValueType.Float4 => prop,
                    _ => prop,
                };
            }

            string wordOffset = binding.WordOffset.ToString(CultureInfo.InvariantCulture) + "u";
            return decl.Type switch
            {
                MaterialValueType.Bool => $"(VividLoadMaterialFloat(ctx.parameterAddress, {wordOffset}) != 0.0f)",
                MaterialValueType.Float => $"VividLoadMaterialFloat(ctx.parameterAddress, {wordOffset})",
                MaterialValueType.Float2 => $"VividLoadMaterialFloat2(ctx.parameterAddress, {wordOffset})",
                MaterialValueType.Float3 => $"VividLoadMaterialFloat3(ctx.parameterAddress, {wordOffset})",
                MaterialValueType.Float4 => $"VividLoadMaterialFloat4(ctx.parameterAddress, {wordOffset})",
                _ => $"VividLoadMaterialFloat4(ctx.parameterAddress, {wordOffset})",
            };
        }

        private static string EmitSwizzle(in MaterialStageLIRNode node)
        {
            if (!MaterialSwizzleMask.TryDecode(node.Semantic, out MaterialSwizzleMask mask))
                throw new InvalidOperationException("Verified Surface LIR contains an invalid swizzle.");

            const string channels = "xyzw";
            var sb = new StringBuilder(16);
            sb.Append(Value(node.Operand0)).Append('.');
            for (int i = 0; i < mask.ComponentCount; i++)
                sb.Append(channels[mask.GetComponent(i)]);
            return sb.ToString();
        }

        private static string EmitCompose(in MaterialStageLIRNode node)
        {
            return node.Type switch
            {
                MaterialValueType.Float2 => $"float2({Value(node.Operand0)}, {Value(node.Operand1)})",
                MaterialValueType.Float3 => $"float3({Value(node.Operand0)}, {Value(node.Operand1)}, {Value(node.Operand2)})",
                MaterialValueType.Float4 => $"float4({Value(node.Operand0)}, {Value(node.Operand1)}, {Value(node.Operand2)}, {Value(node.Operand3)})",
                _ => Value(node.Operand0),
            };
        }

        private static string EmitCompare(in MaterialStageLIRNode node)
        {
            string op = (MaterialComparison)node.Semantic switch
            {
                MaterialComparison.Less => "<",
                MaterialComparison.LessOrEqual => "<=",
                MaterialComparison.Greater => ">",
                MaterialComparison.GreaterOrEqual => ">=",
                MaterialComparison.Equal => "==",
                MaterialComparison.NotEqual => "!=",
                _ => "==",
            };
            return $"({Value(node.Operand0)} {op} {Value(node.Operand1)})";
        }

        private static string Mapped(MaterialStageLIR stageLIR, MaterialValue value)
        {
            return Value(stageLIR.GetValue(value).Index);
        }

        private static string Value(int nodeIndex)
        {
            return $"vivid_s{nodeIndex:D4}";
        }

        private static string GetHlslType(MaterialValueType type)
        {
            return type switch
            {
                MaterialValueType.Bool => "bool",
                MaterialValueType.Float => "float",
                MaterialValueType.Float2 => "float2",
                MaterialValueType.Float3 => "float3",
                MaterialValueType.Float4 => "float4",
                _ => "float4",
            };
        }

        private static string FormatConstant(MaterialValueType type, float4 value)
        {
            return type switch
            {
                MaterialValueType.Bool => value.x != 0.0f ? "true" : "false",
                MaterialValueType.Float => FormatFloat(value.x),
                MaterialValueType.Float2 => $"float2({FormatFloat(value.x)}, {FormatFloat(value.y)})",
                MaterialValueType.Float3 => $"float3({FormatFloat(value.x)}, {FormatFloat(value.y)}, {FormatFloat(value.z)})",
                MaterialValueType.Float4 => $"float4({FormatFloat(value.x)}, {FormatFloat(value.y)}, {FormatFloat(value.z)}, {FormatFloat(value.w)})",
                _ => FormatFloat(value.x),
            };
        }

        private static string FormatFloat(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
                return "0.0f";
            string s = v.ToString("R", CultureInfo.InvariantCulture);
            if (!s.Contains(".") && !s.Contains("e") && !s.Contains("E"))
                s += ".0";
            return s + "f";
        }

        private static string FormatShaderLabFloat(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
                return "0";
            return v.ToString("G6", CultureInfo.InvariantCulture);
        }
    }
}
