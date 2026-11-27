using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    internal sealed class VividSlabShaderDirectives
    {
        internal string BaseTemplatePath { get; set; } = string.Empty;

        internal string LinkedGraphPath { get; set; } = string.Empty;

        internal string ComputeSurfaceMode { get; set; } = string.Empty;

        internal string ShaderName { get; set; } = string.Empty;
    }

    internal sealed class VividSlabShaderAssemblyResult
    {
        internal bool Succeeded { get; set; }

        internal string ShaderName { get; set; } = string.Empty;

        internal string BaseTemplatePath { get; set; } = string.Empty;

        internal string LinkedGraphPath { get; set; } = string.Empty;

        internal string RasterShaderSource { get; set; } = string.Empty;

        internal string ComputeResolveSource { get; set; } = string.Empty;

        internal string ResolveKernelName { get; set; } =
            VividSlabProgramDescriptor.DefaultResolveKernelName;

        internal int EstimatedSubstrateBytesPerPixel { get; set; } =
            VividSubstrateContract.OpenPBROpaqueSingleTotalSlices * VividSubstrateContract.BytesPerSlice;

        internal int ParameterStrideInWords { get; set; }

        internal VividSlabParameterSlot[] ParameterSlots { get; set; } = Array.Empty<VividSlabParameterSlot>();

        internal VividSlabResourceSlot[] ResourceSlots { get; set; } = Array.Empty<VividSlabResourceSlot>();

        internal bool HasLinkedGraphSurface { get; set; }

        internal bool HasCustomSurfaceHook { get; set; }

        internal bool IsMultiClosure { get; set; }

        internal int ClosureCount { get; set; } = 1;

        internal bool HasCustomCoverage { get; set; }

        internal bool HasVertexOffset { get; set; }

        internal uint SourceContentHash { get; set; }

        internal string[] Diagnostics { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Dual-target (Raster Shader + standalone GPUDriven ComputeShader) preprocessor and
    /// assembler for <c>*.slab.shader</c> source files (Phase S1 + S2).
    /// </summary>
    internal static class VividSlabShaderAssembler
    {
        internal const string GraphPropertiesToken = "VIVID_SLAB_GRAPH_PROPERTIES";
        internal const string GraphCBufferToken = "VIVID_SLAB_GRAPH_CBUFFER";
        internal const string GraphTexturesToken = "VIVID_SLAB_GRAPH_TEXTURES";
        internal const string GraphEvaluateToken = "VIVID_SLAB_GRAPH_EVALUATE";
        internal const string UserCBufferToken = "VIVID_SLAB_USER_CBUFFER";
        internal const string UserTexturesToken = "VIVID_SLAB_USER_TEXTURES";
        internal const string UserSurfaceHookToken = "VIVID_SLAB_USER_SURFACE_HOOK";
        internal const string UserVertexHookToken = "VIVID_SLAB_USER_VERTEX_HOOK";
        internal const string UserCoverageHookToken = "VIVID_SLAB_USER_COVERAGE_HOOK";
        internal const string EarlyDepthAttrToken = "VIVID_SLAB_EARLY_DEPTH_ATTR";
        internal const string UseBaseSubShaderToken = "VIVID_SLAB_USE_BASE_SUBSHADER";

        private static readonly Regex s_BaseDirectiveRegex = new(
            @"^\s*//\s*@vivid-base\s+""([^""]+)""\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_GraphDirectiveRegex = new(
            @"^\s*//\s*@vivid-graph\s+""([^""]+)""\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_ComputeSurfaceDirectiveRegex = new(
            @"^\s*//\s*@vivid-compute-surface\s+""([^""]+)""\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_ShaderNameRegex = new(
            @"Shader\s+""([^""]+)""",
            RegexOptions.Compiled);

        private static readonly Regex s_SubShaderBlockRegex = new(
            @"SubShader\s*\{[\s\S]*\}",
            RegexOptions.Compiled);

        private static readonly Regex s_SurfaceHookRegex = new(
            @"^[ \t]*//[ \t]*@vivid-surface-begin[ \t]*\n([\s\S]*?)^[ \t]*//[ \t]*@vivid-surface-end[ \t]*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_VertexHookRegex = new(
            @"^[ \t]*//[ \t]*@vivid-vertex-begin[ \t]*\n([\s\S]*?)^[ \t]*//[ \t]*@vivid-vertex-end[ \t]*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_CoverageHookRegex = new(
            @"^[ \t]*//[ \t]*@vivid-coverage-begin[ \t]*\n([\s\S]*?)^[ \t]*//[ \t]*@vivid-coverage-end[ \t]*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_UserCBufferRegex = new(
            @"^[ \t]*//[ \t]*@vivid-cbuffer-begin[ \t]*\n([\s\S]*?)^[ \t]*//[ \t]*@vivid-cbuffer-end[ \t]*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_UserTexturesRegex = new(
            @"^[ \t]*//[ \t]*@vivid-textures-begin[ \t]*\n([\s\S]*?)^[ \t]*//[ \t]*@vivid-textures-end[ \t]*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        internal static VividSlabShaderDirectives ParseDirectives(string sourceText)
        {
            var directives = new VividSlabShaderDirectives();
            if (string.IsNullOrEmpty(sourceText))
                return directives;

            Match baseMatch = s_BaseDirectiveRegex.Match(sourceText);
            if (baseMatch.Success)
                directives.BaseTemplatePath = baseMatch.Groups[1].Value.Trim();

            Match graphMatch = s_GraphDirectiveRegex.Match(sourceText);
            if (graphMatch.Success)
                directives.LinkedGraphPath = graphMatch.Groups[1].Value.Trim();

            Match computeSurfaceMatch = s_ComputeSurfaceDirectiveRegex.Match(sourceText);
            if (computeSurfaceMatch.Success)
                directives.ComputeSurfaceMode = computeSurfaceMatch.Groups[1].Value.Trim();

            Match shaderNameMatch = s_ShaderNameRegex.Match(sourceText);
            if (shaderNameMatch.Success)
                directives.ShaderName = shaderNameMatch.Groups[1].Value.Trim();

            return directives;
        }

        internal static VividSlabShaderAssemblyResult Assemble(
            string sourceAssetPath,
            string sourceText,
            string resolvedBaseTemplatePath,
            string baseTemplateText,
            string resolvedLinkedGraphPath,
            CompiledMaterialProgram compiledGraphProgram,
            IReadOnlyList<string> externalDiagnostics = null)
        {
            var diagnostics = new List<string>();
            if (externalDiagnostics != null)
            {
                for (int i = 0; i < externalDiagnostics.Count; i++)
                {
                    if (!string.IsNullOrEmpty(externalDiagnostics[i]))
                        diagnostics.Add(externalDiagnostics[i]);
                }
            }

            VividSlabShaderDirectives directives = ParseDirectives(sourceText);
            string normalizedSourcePath = (sourceAssetPath ?? "Unnamed.slab.shader").Replace('\\', '/');
            string shaderName = !string.IsNullOrEmpty(directives.ShaderName)
                ? directives.ShaderName
                : $"VividRP/Slab/{System.IO.Path.GetFileNameWithoutExtension(normalizedSourcePath)}";

            if (string.IsNullOrWhiteSpace(sourceText))
            {
                diagnostics.Add("SLAB-E001: Source .slab.shader file is empty.");
                return CreateFailureResult(
                    shaderName,
                    resolvedBaseTemplatePath,
                    resolvedLinkedGraphPath,
                    diagnostics);
            }

            MaterialSlabHlslPayload graphPayload = null;
            if (compiledGraphProgram != null)
            {
                try
                {
                    graphPayload = MaterialSlabHlslBackend.Compile(compiledGraphProgram);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(
                        $"SLAB-E009: Dual-target StageLIR lowering failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            string normalizedSource = NormalizeLineEndings(sourceText);

            // Extract optional user HLSL hook blocks (and remove them from ShaderLab outer scope
            // so the same hook code can be injected into BOTH Raster HLSLPROGRAM and GPUDriven Compute).
            string userSurfaceHook = ExtractAndStripBlock(
                ref normalizedSource,
                s_SurfaceHookRegex,
                normalizedSourcePath,
                out bool hasCustomSurfaceHook);
            string userVertexHook = ExtractAndStripBlock(
                ref normalizedSource,
                s_VertexHookRegex,
                normalizedSourcePath,
                out bool hasVertexOffset);
            string userCoverageHook = ExtractAndStripBlock(
                ref normalizedSource,
                s_CoverageHookRegex,
                normalizedSourcePath,
                out bool hasCustomCoverage);
            string userCBuffer = ExtractAndStripBlock(
                ref normalizedSource,
                s_UserCBufferRegex,
                normalizedSourcePath,
                out _);
            string userTextures = ExtractAndStripBlock(
                ref normalizedSource,
                s_UserTexturesRegex,
                normalizedSourcePath,
                out _);

            string propertiesBlock = graphPayload != null
                ? graphPayload.PropertiesBlock
                : "// [VividSlab] No linked MaterialGraph properties.";
            if (normalizedSource.Contains(UseBaseSubShaderToken)
                && normalizedSource.IndexOf("_Cull", StringComparison.Ordinal) < 0
                && propertiesBlock.IndexOf("_Cull", StringComparison.Ordinal) < 0)
            {
                propertiesBlock += "\n        [HideInInspector] _Cull(\"Cull\", Float) = 2";
            }
            string rasterCBufferBlock = graphPayload != null
                ? graphPayload.RasterCBufferBlock
                : "// [VividSlab] No linked MaterialGraph CBUFFER fields.";
            string rasterTexturesBlock = graphPayload != null
                ? graphPayload.RasterTexturesBlock
                : "// [VividSlab] No linked MaterialGraph texture declarations.";
            string computeSurfaceMode = directives.ComputeSurfaceMode;
            if (string.IsNullOrEmpty(computeSurfaceMode) && !string.IsNullOrEmpty(baseTemplateText))
            {
                Match templateComputeSurface = s_ComputeSurfaceDirectiveRegex.Match(baseTemplateText);
                if (templateComputeSurface.Success)
                    computeSurfaceMode = templateComputeSurface.Groups[1].Value.Trim();
            }
            bool useStandardLitComputeSurface = graphPayload == null
                && string.Equals(computeSurfaceMode, "StandardLit", StringComparison.OrdinalIgnoreCase);

            string rasterEvaluateBlock = graphPayload != null
                ? graphPayload.RasterEvaluateFunction
                : BuildDefaultRasterEvaluateFunction();
            string computeEvaluateBlock = graphPayload != null
                ? graphPayload.ComputeEvaluateFunction
                : useStandardLitComputeSurface
                    ? BuildStandardLitComputeEvaluateFunction()
                    : BuildDefaultComputeEvaluateFunction();
            string earlyDepthAttr = hasCustomCoverage
                ? "// [VividSlab] [earlydepthstencil] omitted when custom coverage clip is active."
                : "[earlydepthstencil]";

            string rasterText = normalizedSource;

            if (rasterText.Contains(UseBaseSubShaderToken))
            {
                if (string.IsNullOrEmpty(baseTemplateText))
                {
                    diagnostics.Add(
                        $"SLAB-E002: '{UseBaseSubShaderToken}' was used, but no readable @vivid-base template was provided.");
                }
                else
                {
                    string baseSubShader = ExtractSubShaderBlock(baseTemplateText);
                    if (string.IsNullOrEmpty(baseSubShader))
                    {
                        diagnostics.Add(
                            $"SLAB-E003: Base template '{resolvedBaseTemplatePath}' does not contain a SubShader block.");
                    }
                    else
                    {
                        // ShaderLab blocks do not accept HLSL #line directives.
                        rasterText = rasterText.Replace(UseBaseSubShaderToken, baseSubShader);
                    }
                }
            }

            // ShaderLab Properties block does not accept HLSL #line directives.
            rasterText = rasterText.Replace(GraphPropertiesToken, propertiesBlock);
            rasterText = ReplaceTokenWithLineAnchor(rasterText, GraphCBufferToken, rasterCBufferBlock, normalizedSourcePath);
            rasterText = ReplaceTokenWithLineAnchor(rasterText, UserCBufferToken, userCBuffer, normalizedSourcePath);
            rasterText = ReplaceTokenWithLineAnchor(rasterText, GraphTexturesToken, rasterTexturesBlock, normalizedSourcePath);
            rasterText = ReplaceTokenWithLineAnchor(rasterText, UserTexturesToken, userTextures, normalizedSourcePath);
            rasterText = ReplaceTokenWithLineAnchor(rasterText, GraphEvaluateToken, rasterEvaluateBlock, normalizedSourcePath);
            rasterText = ReplaceTokenWithLineAnchor(rasterText, UserSurfaceHookToken, userSurfaceHook, normalizedSourcePath);
            rasterText = ReplaceTokenWithLineAnchor(rasterText, UserVertexHookToken, userVertexHook, normalizedSourcePath);
            rasterText = ReplaceTokenWithLineAnchor(rasterText, UserCoverageHookToken, userCoverageHook, normalizedSourcePath);
            rasterText = rasterText.Replace(EarlyDepthAttrToken, earlyDepthAttr);

            bool isMultiClosure = graphPayload != null && graphPayload.IsMultiClosure;
            int closureCount = graphPayload != null ? graphPayload.ClosureCount : 1;

            bool hasErrors = HasErrorDiagnostics(diagnostics);
            string computeSource = BuildStandaloneGPUDrivenComputeSource(
                normalizedSourcePath,
                shaderName,
                resolvedLinkedGraphPath,
                compiledGraphProgram,
                computeEvaluateBlock,
                userSurfaceHook,
                userVertexHook,
                VividSlabProgramDescriptor.DefaultResolveKernelName);

            uint contentHash = ComputeFnv1a32(rasterText, computeSource);

            int estimatedBytes = graphPayload != null
                ? graphPayload.EstimatedSubstrateBytesPerPixel
                : useStandardLitComputeSurface
                    ? VividSubstrateContract.ComputeSingleClosureTotalSlices(
                        VividSubstrateFeatureMask.OpenPBROpaque | VividSubstrateFeatureMask.Coat)
                      * VividSubstrateContract.BytesPerSlice
                    : VividSubstrateContract.OpenPBROpaqueSingleTotalSlices * VividSubstrateContract.BytesPerSlice;
            if (!isMultiClosure && hasCustomSurfaceHook && userSurfaceHook.IndexOf("coatWeight", StringComparison.Ordinal) >= 0)
            {
                estimatedBytes = Math.Max(
                    estimatedBytes,
                    VividSubstrateContract.ComputeSingleClosureTotalSlices(
                        VividSubstrateFeatureMask.OpenPBROpaque | VividSubstrateFeatureMask.Coat)
                    * VividSubstrateContract.BytesPerSlice);
            }

            return new VividSlabShaderAssemblyResult
            {
                Succeeded = !hasErrors,
                ShaderName = shaderName,
                BaseTemplatePath = resolvedBaseTemplatePath ?? string.Empty,
                LinkedGraphPath = resolvedLinkedGraphPath ?? string.Empty,
                RasterShaderSource = rasterText,
                ComputeResolveSource = computeSource,
                ResolveKernelName = VividSlabProgramDescriptor.DefaultResolveKernelName,
                EstimatedSubstrateBytesPerPixel = estimatedBytes,
                ParameterStrideInWords = graphPayload != null ? graphPayload.ParameterStrideInWords : 0,
                ParameterSlots = graphPayload != null ? graphPayload.ParameterSlots : Array.Empty<VividSlabParameterSlot>(),
                ResourceSlots = graphPayload != null ? graphPayload.ResourceSlots : Array.Empty<VividSlabResourceSlot>(),
                HasLinkedGraphSurface = graphPayload != null,
                HasCustomSurfaceHook = hasCustomSurfaceHook,
                IsMultiClosure = isMultiClosure,
                ClosureCount = closureCount,
                HasCustomCoverage = hasCustomCoverage,
                HasVertexOffset = hasVertexOffset,
                SourceContentHash = contentHash,
                Diagnostics = diagnostics.ToArray(),
            };
        }

        internal static string BuildErrorShaderSource(string shaderName)
        {
            string safeName = string.IsNullOrEmpty(shaderName)
                ? "Hidden/VividRP/SlabShaderError"
                : shaderName;
            return
                $"Shader \"{safeName}\"\n"
                + "{\n"
                + "    SubShader\n"
                + "    {\n"
                + "        Pass\n"
                + "        {\n"
                + "            CGPROGRAM\n"
                + "            #pragma vertex vert\n"
                + "            #pragma fragment frag\n"
                + "            float4 vert(float4 pos : POSITION) : SV_POSITION { return UnityObjectToClipPos(pos); }\n"
                + "            fixed4 frag() : SV_Target { return fixed4(1, 0, 1, 1); }\n"
                + "            ENDCG\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }

        private static string ExtractAndStripBlock(
            ref string text,
            Regex blockRegex,
            string sourcePath,
            out bool found)
        {
            Match match = blockRegex.Match(text);
            if (!match.Success)
            {
                found = false;
                return "// [VividSlab] No custom block.";
            }

            found = true;
            int bodyStartLine = CountLines(text, 0, match.Groups[1].Index) + 1;
            string body = match.Groups[1].Value.TrimEnd();
            text = text.Remove(match.Index, match.Length);
            return $"#line {bodyStartLine.ToString(CultureInfo.InvariantCulture)} \"{sourcePath}\"\n{body}";
        }

        private static string BuildDefaultRasterEvaluateFunction()
        {
            return
                "void VividSlabEvaluateGraphSurface(\n"
                + "    const VividSlabEvalContext ctx,\n"
                + "    out VividOpenPBRV2Inputs v2Inputs,\n"
                + "    out float ambientOcclusion)\n"
                + "{\n"
                + "    VividStandardLitSurfaceParams p = VividCreateDefaultStandardLitSurfaceParams();\n"
                + "    p.normalWS = ctx.geometryNormalWS;\n"
                + "    p.coatNormalWS = ctx.geometryNormalWS;\n"
                + "    p.tangentWS = dot(ctx.geometryTangentWS.xyz, ctx.geometryTangentWS.xyz) > 1e-8f ? normalize(ctx.geometryTangentWS.xyz) : float3(1.0f, 0.0f, 0.0f);\n"
                + "    p.tangentHandedness = ctx.geometryTangentWS.w < 0.0f ? -1.0f : 1.0f;\n"
                + "    v2Inputs = VividStandardLitToOpenPBRV2Inputs(p);\n"
                + "    ambientOcclusion = 1.0f;\n"
                + "}";
        }

        private static string BuildDefaultComputeEvaluateFunction()
        {
            return
                "void VividSlabEvaluateGraphSurface(\n"
                + "    const VividSlabEvalContext ctx,\n"
                + "    out VividOpenPBRV2Inputs v2Inputs,\n"
                + "    out float ambientOcclusion)\n"
                + "{\n"
                + "    VividStandardLitSurfaceParams p = VividCreateDefaultStandardLitSurfaceParams();\n"
                + "    p.normalWS = ctx.geometryNormalWS;\n"
                + "    p.coatNormalWS = ctx.geometryNormalWS;\n"
                + "    p.tangentWS = dot(ctx.geometryTangentWS.xyz, ctx.geometryTangentWS.xyz) > 1e-8f ? normalize(ctx.geometryTangentWS.xyz) : float3(1.0f, 0.0f, 0.0f);\n"
                + "    p.tangentHandedness = ctx.geometryTangentWS.w < 0.0f ? -1.0f : 1.0f;\n"
                + "    v2Inputs = VividStandardLitToOpenPBRV2Inputs(p);\n"
                + "    ambientOcclusion = 1.0f;\n"
                + "}";
        }

        private static string BuildStandardLitComputeEvaluateFunction()
        {
            return
                "void VividSlabEvaluateGraphSurface(\n"
                + "    const VividSlabEvalContext ctx,\n"
                + "    out VividOpenPBRV2Inputs v2Inputs,\n"
                + "    out float ambientOcclusion)\n"
                + "{\n"
                + "    VividSlabEvaluateStandardLitFromMaterialData(ctx, v2Inputs, ambientOcclusion);\n"
                + "}";
        }

        private static VividSlabShaderAssemblyResult CreateFailureResult(
            string shaderName,
            string baseTemplatePath,
            string linkedGraphPath,
            List<string> diagnostics)
        {
            return new VividSlabShaderAssemblyResult
            {
                Succeeded = false,
                ShaderName = shaderName,
                BaseTemplatePath = baseTemplatePath ?? string.Empty,
                LinkedGraphPath = linkedGraphPath ?? string.Empty,
                RasterShaderSource = BuildErrorShaderSource(shaderName),
                ComputeResolveSource = string.Empty,
                ResolveKernelName = VividSlabProgramDescriptor.DefaultResolveKernelName,
                EstimatedSubstrateBytesPerPixel =
                    VividSubstrateContract.OpenPBROpaqueSingleTotalSlices * VividSubstrateContract.BytesPerSlice,
                SourceContentHash = 0u,
                Diagnostics = diagnostics.ToArray(),
            };
        }

        private static string ExtractSubShaderBlock(string templateText)
        {
            Match match = s_SubShaderBlockRegex.Match(templateText);
            return match.Success ? NormalizeLineEndings(match.Value) : string.Empty;
        }

        /// <summary>
        /// Generates a standalone, per-material GPUDriven VisibilityBuffer Binned Resolve ComputeShader
        /// with zero switch(ProgramID) divergence, VT-first texture backend (+ experimental Bindless),
        /// optional WPO vertex replay for exact barycentrics, StageLIR Single/Multi-Closure evaluation
        /// + custom post-hook, and direct Substrate Closure Buffer output (Tier 1/2/3).
        /// </summary>
        private static string BuildStandaloneGPUDrivenComputeSource(
            string sourceAssetPath,
            string shaderName,
            string linkedGraphPath,
            CompiledMaterialProgram compiledProgram,
            string computeEvaluateFunction,
            string userSurfaceHook,
            string userVertexHook,
            string kernelName)
        {
            string graphSummary = compiledProgram != null
                ? $"// Linked Graph: {linkedGraphPath} (Hash: {compiledProgram.CompiledHash})"
                : "// Linked Graph: <none> (standalone Slab compute)";

            var sb = new StringBuilder(8192);
            sb.AppendLine("// <auto-generated by VividSlabShaderImporter (Phase S2)>");
            sb.Append("// Source: ").AppendLine(sourceAssetPath);
            sb.Append("// Shader: ").AppendLine(shaderName);
            sb.AppendLine(graphSummary);
            sb.AppendLine("// Standalone per-material GPUDriven VisibilityBuffer Binned Resolve ComputeShader (zero switch divergence).");
            sb.AppendLine("#pragma use_dxc");
            sb.Append("#pragma kernel ").AppendLine(kernelName);
            sb.AppendLine("#pragma multi_compile_local _ VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE");
            sb.AppendLine();
            sb.AppendLine("#define VIVID_SLAB_TARGET_GPUDRIVEN_COMPUTE 1");
            sb.AppendLine("#include \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl\"");
            sb.AppendLine("#include \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/SurfaceSummaryGBuffer.hlsl\"");
            sb.AppendLine("#include \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividOpenPBROpaqueGBuffer.hlsl\"");
            sb.AppendLine("#include \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividSubstrateBuffer.hlsl\"");
            sb.AppendLine("#define VIVIDRP_STANDARD_LIT_TO_OPENPBR_SUBSTRATE_ONLY 1");
            sb.AppendLine("#include \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividStandardLitToOpenPBR.hlsl\"");
            sb.AppendLine("#include \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl\"");
            sb.AppendLine("#if defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)");
            sb.AppendLine("    #define VIVID_VT_ENABLE_FEEDBACK_RW_COMPUTE 1");
            sb.AppendLine("#else");
            sb.AppendLine("    #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS 1");
            sb.AppendLine("#endif");
            sb.AppendLine("#include_with_pragmas \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividSurfaceSampling.hlsl\"");
            sb.AppendLine("#define VIVID_SLAB_NO_AOT_SURFACE_DISPATCH 1");
            sb.AppendLine("#include \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialSurface.hlsl\"");
            sb.AppendLine("#include \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividSlabSurfaceContract.hlsl\"");
            sb.AppendLine("#include \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl\"");
            sb.AppendLine("#include \"Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividBarycentric.hlsl\"");
            sb.AppendLine();
            sb.AppendLine("#define VIVID_SLAB_RESOLVE_GROUP_SIZE 8u");
            sb.AppendLine();
            sb.AppendLine("uint _MaterialBinTileStride;");
            sb.AppendLine("uint _TargetMaterialBin;");
            sb.AppendLine("StructuredBuffer<uint> _MaterialBinTileListSRV;");
            sb.AppendLine();
            sb.AppendLine("TYPED_TEXTURE2D(float2, _VisibilityBuffer);");
            sb.AppendLine("StructuredBuffer<VividMeshletVertex> _SharedVertexBuffer;");
            sb.AppendLine("ByteAddressBuffer _SharedIndexBuffer;");
            sb.AppendLine();
            sb.AppendLine("float4 _VisibilityBufferScaleBias;");
            sb.AppendLine("float4 _ResolveOutputSize;");
            sb.AppendLine("uint _WriteSubstrateClosureBuffer;");
            sb.AppendLine();
            sb.AppendLine("RWTexture2D<float4> _OutGBuffer0;");
            sb.AppendLine("RWTexture2D<float4> _OutGBuffer1;");
            sb.AppendLine("RWTexture2D<float4> _OutGBuffer2;");
            sb.AppendLine("RWTexture2D<float4> _OutGBuffer3;");
            sb.AppendLine("RWTexture2D<float4> _OutGBuffer4;");
            sb.AppendLine("RWTexture2DArray<uint> _OutSubstrateClosureBuffer;");
            sb.AppendLine();
            sb.AppendLine(userVertexHook);
            sb.AppendLine();
            sb.AppendLine(computeEvaluateFunction);
            sb.AppendLine();
            sb.AppendLine(userSurfaceHook);
            sb.AppendLine();
            sb.AppendLine("uint VividSlabPullIndex(const VividDecodedMeshlet meshlet, const uint indexID)");
            sb.AppendLine("{");
            sb.AppendLine("    const uint absoluteIndexID = meshlet.TriangleOffset + indexID;");
            sb.AppendLine("    const uint packedIndices = _SharedIndexBuffer.Load((absoluteIndexID / 4u) * 4u);");
            sb.AppendLine("    const uint shiftAmount = (absoluteIndexID % 4u) * 8u;");
            sb.AppendLine("    return (packedIndices >> shiftAmount) & 0xFFu;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("[numthreads(VIVID_SLAB_RESOLVE_GROUP_SIZE, VIVID_SLAB_RESOLVE_GROUP_SIZE, 1)]");
            sb.Append("void ").Append(kernelName).AppendLine("(");
            sb.AppendLine("    uint3 groupId : SV_GroupID,");
            sb.AppendLine("    uint3 groupThreadId : SV_GroupThreadID)");
            sb.AppendLine("{");
            sb.AppendLine("    uint packedTile = _MaterialBinTileListSRV[_TargetMaterialBin * _MaterialBinTileStride + groupId.x];");
            sb.AppendLine("    uint2 tileCoord = uint2(packedTile & 0xFFFFu, packedTile >> 16u);");
            sb.AppendLine("    uint2 pixelCoord = tileCoord * VIVID_SLAB_RESOLVE_GROUP_SIZE + groupThreadId.xy;");
            sb.AppendLine("    if (pixelCoord.x >= (uint)_ResolveOutputSize.x || pixelCoord.y >= (uint)_ResolveOutputSize.y)");
            sb.AppendLine("        return;");
            sb.AppendLine();
            sb.AppendLine("    float2 screenUV = (float2(pixelCoord) + 0.5f) * _ResolveOutputSize.zw;");
            sb.AppendLine("    float2 visibilityUv = screenUV * _VisibilityBufferScaleBias.xy + _VisibilityBufferScaleBias.zw;");
            sb.AppendLine("    uint2 packedVis = asuint(SAMPLE_TEXTURE2D_LOD(_VisibilityBuffer, sampler_PointClamp, visibilityUv, 0).xy);");
            sb.AppendLine("    if (!IsPackedVisibilityBufferValueValid(packedVis))");
            sb.AppendLine("        return;");
            sb.AppendLine();
            sb.AppendLine("    VividVisibilityBufferValue vis = UnpackVisibilityBufferValue(packedVis);");
            sb.AppendLine("    VividInstanceData instanceData = PullInstanceData(vis.InstanceID);");
            sb.AppendLine("    VividMaterialRuntimeHeader runtimeHeader;");
            sb.AppendLine("    VividMaterialProgramData programData;");
            sb.AppendLine("    const uint programStatus = VividGetMaterialProgramStatus(instanceData.MaterialIndex, runtimeHeader, programData);");
            sb.AppendLine("    if (programStatus != VIVID_MATERIAL_PROGRAM_KNOWN || runtimeHeader.ProgramID != _TargetMaterialBin)");
            sb.AppendLine("        return;");
            sb.AppendLine();
            sb.AppendLine("    VividDecodedMeshlet meshlet = PullMeshletData(vis.MeshletID);");
            sb.AppendLine("    uint i0 = VividSlabPullIndex(meshlet, vis.IndexID + 0u);");
            sb.AppendLine("    uint i1 = VividSlabPullIndex(meshlet, vis.IndexID + 1u);");
            sb.AppendLine("    uint i2 = VividSlabPullIndex(meshlet, vis.IndexID + 2u);");
            sb.AppendLine("    VividMeshletVertex v0 = _SharedVertexBuffer[meshlet.VertexOffset + i0];");
            sb.AppendLine("    VividMeshletVertex v1 = _SharedVertexBuffer[meshlet.VertexOffset + i1];");
            sb.AppendLine("    VividMeshletVertex v2 = _SharedVertexBuffer[meshlet.VertexOffset + i2];");
            sb.AppendLine();
            sb.AppendLine("    float3 posOS0 = DecodeVividMeshletPosition(v0);");
            sb.AppendLine("    float3 posOS1 = DecodeVividMeshletPosition(v1);");
            sb.AppendLine("    float3 posOS2 = DecodeVividMeshletPosition(v2);");
            sb.AppendLine("    float3 posWS0 = TransformPosition(instanceData.ObjectToWorldMatrix, posOS0);");
            sb.AppendLine("    float3 posWS1 = TransformPosition(instanceData.ObjectToWorldMatrix, posOS1);");
            sb.AppendLine("    float3 posWS2 = TransformPosition(instanceData.ObjectToWorldMatrix, posOS2);");
            sb.AppendLine("    float3 nOS0 = SafeNormalize(DecodeVividMeshletNormal(v0));");
            sb.AppendLine("    float3 nOS1 = SafeNormalize(DecodeVividMeshletNormal(v1));");
            sb.AppendLine("    float3 nOS2 = SafeNormalize(DecodeVividMeshletNormal(v2));");
            sb.AppendLine("    float3 n0 = SafeNormalize(mul(nOS0, (float3x3)instanceData.WorldToObjectMatrix));");
            sb.AppendLine("    float3 n1 = SafeNormalize(mul(nOS1, (float3x3)instanceData.WorldToObjectMatrix));");
            sb.AppendLine("    float3 n2 = SafeNormalize(mul(nOS2, (float3x3)instanceData.WorldToObjectMatrix));");
            sb.AppendLine("    #if defined(VIVID_SLAB_HAS_VERTEX_OFFSET)");
            sb.AppendLine("    {");
            sb.AppendLine("        VividSlabVertexContext vctx0 = { posOS0, posWS0, nOS0, n0, DecodeVividMeshletUV(v0), runtimeHeader.ParameterAddress, runtimeHeader.ResourceBindingAddress, instanceData.MaterialIndex };");
            sb.AppendLine("        VividSlabVertexContext vctx1 = { posOS1, posWS1, nOS1, n1, DecodeVividMeshletUV(v1), runtimeHeader.ParameterAddress, runtimeHeader.ResourceBindingAddress, instanceData.MaterialIndex };");
            sb.AppendLine("        VividSlabVertexContext vctx2 = { posOS2, posWS2, nOS2, n2, DecodeVividMeshletUV(v2), runtimeHeader.ParameterAddress, runtimeHeader.ResourceBindingAddress, instanceData.MaterialIndex };");
            sb.AppendLine("        posWS0 += VividSlabEvaluateVertexOffset(vctx0);");
            sb.AppendLine("        posWS1 += VividSlabEvaluateVertexOffset(vctx1);");
            sb.AppendLine("        posWS2 += VividSlabEvaluateVertexOffset(vctx2);");
            sb.AppendLine("    }");
            sb.AppendLine("    #endif");
            sb.AppendLine("    float normalFlipSign = 1.0f;");
            sb.AppendLine("    {");
            sb.AppendLine("        const VividMaterialData cullMat = PullMaterialData(instanceData.MaterialIndex);");
            sb.AppendLine("        if ((GetRendererListID(instanceData, cullMat) & VIVIDRENDERERLISTID_CULL_OFF) != 0u)");
            sb.AppendLine("        {");
            sb.AppendLine("            float3 autoNormalWS = cross(SafeNormalize(posWS0 - posWS1), SafeNormalize(posWS2 - posWS0));");
            sb.AppendLine("            float3 viewDirWS = GetViewForwardDir(UNITY_MATRIX_V);");
            sb.AppendLine("            if (unity_OrthoParams.w == 0.0f)");
            sb.AppendLine("            {");
            sb.AppendLine("                float3 triCenterWS = (posWS0 + posWS1 + posWS2) / 3.0f;");
            sb.AppendLine("                float3 camToTri = triCenterWS - _WorldSpaceCameraPos;");
            sb.AppendLine("                if (dot(camToTri, camToTri) > 1e-8f) viewDirWS = camToTri;");
            sb.AppendLine("            }");
            sb.AppendLine("            normalFlipSign = dot(autoNormalWS, viewDirWS) < 0.0f ? -1.0f : 1.0f;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    float4 posCS = float4(float2(pixelCoord) + 0.5f, 0.0f, 1.0f);");
            sb.AppendLine("    float2 pixelNdc = posCS.xy * _ResolveOutputSize.zw * 2.0f - 1.0f;");
            sb.AppendLine("    #ifdef UNITY_UV_STARTS_AT_TOP");
            sb.AppendLine("    pixelNdc.y *= -1.0f;");
            sb.AppendLine("    #endif");
            sb.AppendLine("    VividBarycentricDerivatives bary = CalculateFullBarycentric(");
            sb.AppendLine("        TransformWorldToHClip(posWS0),");
            sb.AppendLine("        TransformWorldToHClip(posWS1),");
            sb.AppendLine("        TransformWorldToHClip(posWS2),");
            sb.AppendLine("        pixelNdc,");
            sb.AppendLine("        _ResolveOutputSize.zw);");
            sb.AppendLine();
            sb.AppendLine("    float3 uInterp = InterpolateWithBarycentric(bary, DecodeVividMeshletUV(v0).x, DecodeVividMeshletUV(v1).x, DecodeVividMeshletUV(v2).x);");
            sb.AppendLine("    float3 vInterp = InterpolateWithBarycentric(bary, DecodeVividMeshletUV(v0).y, DecodeVividMeshletUV(v1).y, DecodeVividMeshletUV(v2).y);");
            sb.AppendLine("    float3 geometryNormalWS = SafeNormalize(InterpolateWithBarycentricNoDerivatives(bary, n0, n1, n2) * normalFlipSign);");
            sb.AppendLine("    float4 tangentOS = InterpolateWithBarycentricNoDerivatives(bary, DecodeVividMeshletTangent(v0), DecodeVividMeshletTangent(v1), DecodeVividMeshletTangent(v2));");
            sb.AppendLine("    float3 tangentWS = mul((float3x3)instanceData.ObjectToWorldMatrix, tangentOS.xyz);");
            sb.AppendLine("    float tangentSign = ((instanceData.Flags & VIVIDINSTANCEFLAGS_FLIP_WINDING_ORDER) != 0u ? -tangentOS.w : tangentOS.w) * normalFlipSign;");
            sb.AppendLine("    float4 geometryTangentWS = dot(tangentWS, tangentWS) > 1e-8f ? float4(normalize(tangentWS), tangentSign) : float4(1.0f, 0.0f, 0.0f, 1.0f);");
            sb.AppendLine();
            sb.AppendLine("    VividSlabEvalContext evalCtx;");
            sb.AppendLine("    evalCtx.uv0 = float2(uInterp.x, vInterp.x);");
            sb.AppendLine("    evalCtx.uv0Ddx = float2(uInterp.y, vInterp.y);");
            sb.AppendLine("    evalCtx.uv0Ddy = float2(uInterp.z, vInterp.z);");
            sb.AppendLine("    evalCtx.geometryNormalWS = geometryNormalWS;");
            sb.AppendLine("    evalCtx.geometryTangentWS = geometryTangentWS;");
            sb.AppendLine("    evalCtx.positionWS = InterpolateWithBarycentricNoDerivatives(bary, posWS0, posWS1, posWS2);");
            sb.AppendLine("    evalCtx.positionCS = posCS;");
            sb.AppendLine("    evalCtx.parameterAddress = runtimeHeader.ParameterAddress;");
            sb.AppendLine("    evalCtx.resourceAddress = runtimeHeader.ResourceBindingAddress;");
            sb.AppendLine("    evalCtx.materialIndex = instanceData.MaterialIndex;");
            sb.AppendLine();
            sb.AppendLine("    #if defined(VIVID_SLAB_MULTI_CLOSURE_OUTPUT)");
            sb.AppendLine("    {");
            sb.AppendLine("        VividSlabMultiClosureOutput mcOutput;");
            sb.AppendLine("        VividSlabEvaluateMultiClosureGraphSurface(evalCtx, mcOutput);");
            sb.AppendLine("        #if defined(VIVID_SLAB_HAS_CUSTOM_POST_MULTI_CLOSURE)");
            sb.AppendLine("            VividSlabPostEvaluateMultiClosure(evalCtx, mcOutput);");
            sb.AppendLine("        #elif defined(VIVID_SLAB_HAS_CUSTOM_POST_SURFACE)");
            sb.AppendLine("            VividSlabPostEvaluateSurface(evalCtx, mcOutput.closures[0], mcOutput.ambientOcclusion);");
            sb.AppendLine("        #endif");
            sb.AppendLine();
            sb.AppendLine("        VividSurfaceSummaryData summary = VividBuildMultiClosureTopLayerSummary(mcOutput, true, true);");
            sb.AppendLine("        VividSurfaceSummaryGBufferOutput packedSummary = VividPackSurfaceSummaryGBuffer(summary);");
            sb.AppendLine("        _OutGBuffer0[pixelCoord] = packedSummary.rt0;");
            sb.AppendLine("        _OutGBuffer1[pixelCoord] = packedSummary.rt1;");
            sb.AppendLine("        _OutGBuffer2[pixelCoord] = packedSummary.rt2;");
            sb.AppendLine("        _OutGBuffer3[pixelCoord] = packedSummary.rt3;");
            sb.AppendLine("        _OutGBuffer4[pixelCoord] = packedSummary.rt4;");
            sb.AppendLine();
            sb.AppendLine("        if (_WriteSubstrateClosureBuffer != 0u)");
            sb.AppendLine("        {");
            sb.AppendLine("            uint headerFlags = VIVID_SUBSTRATE_FLAG_RECEIVE_DECALS;");
            sb.AppendLine("            [unroll]");
            sb.AppendLine("            for (uint ci = 0u; ci < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++ci)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (ci < mcOutput.closureCount");
            sb.AppendLine("                    && any(mcOutput.closures[ci].core.emissionLuminance * mcOutput.closures[ci].core.emissionColor > 0.0f))");
            sb.AppendLine("                {");
            sb.AppendLine("                    headerFlags |= VIVID_SUBSTRATE_FLAG_HAS_EMISSION;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            VividWriteSubstrateMultiClosure(");
            sb.AppendLine("                _OutSubstrateClosureBuffer,");
            sb.AppendLine("                pixelCoord,");
            sb.AppendLine("                mcOutput.closureCount,");
            sb.AppendLine("                mcOutput.closures,");
            sb.AppendLine("                mcOutput.operators,");
            sb.AppendLine("                headerFlags,");
            sb.AppendLine("                VIVID_SUBSTRATE_MAX_SLICE_COUNT);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    #else");
            sb.AppendLine("    {");
            sb.AppendLine("        VividOpenPBRV2Inputs v2Inputs;");
            sb.AppendLine("        float ambientOcclusion = 1.0f;");
            sb.AppendLine("        VividSlabEvaluateGraphSurface(evalCtx, v2Inputs, ambientOcclusion);");
            sb.AppendLine("        #if defined(VIVID_SLAB_HAS_CUSTOM_POST_SURFACE)");
            sb.AppendLine("            VividSlabPostEvaluateSurface(evalCtx, v2Inputs, ambientOcclusion);");
            sb.AppendLine("        #endif");
            sb.AppendLine();
            sb.AppendLine("        VividSurfaceSummaryData summary = VividBuildStandardLitSubstrateTopLayerSummary(v2Inputs, ambientOcclusion, true, true);");
            sb.AppendLine("        VividSurfaceSummaryGBufferOutput packedSummary = VividPackSurfaceSummaryGBuffer(summary);");
            sb.AppendLine("        _OutGBuffer0[pixelCoord] = packedSummary.rt0;");
            sb.AppendLine("        _OutGBuffer1[pixelCoord] = packedSummary.rt1;");
            sb.AppendLine("        _OutGBuffer2[pixelCoord] = packedSummary.rt2;");
            sb.AppendLine("        _OutGBuffer3[pixelCoord] = packedSummary.rt3;");
            sb.AppendLine("        _OutGBuffer4[pixelCoord] = packedSummary.rt4;");
            sb.AppendLine();
            sb.AppendLine("        if (_WriteSubstrateClosureBuffer != 0u)");
            sb.AppendLine("        {");
            sb.AppendLine("            uint headerFlags = VIVID_SUBSTRATE_FLAG_RECEIVE_DECALS;");
            sb.AppendLine("            if (any(v2Inputs.core.emissionLuminance * v2Inputs.core.emissionColor > 0.0f))");
            sb.AppendLine("                headerFlags |= VIVID_SUBSTRATE_FLAG_HAS_EMISSION;");
            sb.AppendLine("            VividWriteSubstrateOpenPBRV2Single(_OutSubstrateClosureBuffer, pixelCoord, v2Inputs, headerFlags);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    #endif");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ReplaceTokenWithLineAnchor(
            string text,
            string token,
            string replacement,
            string sourcePath)
        {
            int index = text.IndexOf(token, StringComparison.Ordinal);
            if (index < 0)
                return text;

            var sb = new StringBuilder(text.Length + replacement.Length + 64);
            int searchStart = 0;
            while (index >= 0)
            {
                sb.Append(text, searchStart, index - searchStart);
                int lineAfterToken = CountLines(text, 0, index + token.Length) + 1;
                sb.AppendLine(replacement);
                sb.Append("#line ")
                    .Append(lineAfterToken.ToString(CultureInfo.InvariantCulture))
                    .Append(" \"")
                    .Append(sourcePath)
                    .AppendLine("\"");
                searchStart = index + token.Length;
                index = text.IndexOf(token, searchStart, StringComparison.Ordinal);
            }

            sb.Append(text, searchStart, text.Length - searchStart);
            return sb.ToString();
        }

        private static int CountLines(string text, int start, int length)
        {
            int count = 0;
            int end = Math.Min(text.Length, start + length);
            for (int i = start; i < end; i++)
            {
                if (text[i] == '\n')
                    count++;
            }
            return count;
        }

        private static bool HasErrorDiagnostics(List<string> diagnostics)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].StartsWith("SLAB-E", StringComparison.Ordinal)
                    || diagnostics[i].StartsWith("MAT-", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static string NormalizeLineEndings(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static uint ComputeFnv1a32(string a, string b)
        {
            unchecked
            {
                uint hash = 2166136261u;
                HashString(ref hash, a);
                hash ^= 0xFFu;
                hash *= 16777619u;
                HashString(ref hash, b);
                return hash == 0u ? 1u : hash;
            }
        }

        private static void HashString(ref uint hash, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            unchecked
            {
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }
            }
        }
    }
}
