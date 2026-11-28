using System;
using System.Collections.Generic;
using System.IO;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    [InitializeOnLoad]
    internal static class VividSlabProgramRegistryEditorBootstrap
    {
        static VividSlabProgramRegistryEditorBootstrap()
        {
            VividSlabProgramRegistry.SetEditorResolver(ResolveDescriptorFromAssetDatabase);
        }

        private static VividSlabProgramDescriptor ResolveDescriptorFromAssetDatabase(Shader shader)
        {
            if (shader == null)
                return null;

            string assetPath = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(assetPath)
                || !assetPath.EndsWith("." + VividSlabShaderImporter.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is VividSlabProgramDescriptor descriptor)
                    return descriptor;
            }
            return null;
        }
    }

    /// <summary>
    /// Composite-extension ScriptedImporter (<c>*.slab.shader</c>) that compiles a single
    /// Substrate material source into:
    /// <list type="bullet">
    ///   <item>Main Asset: Raster <see cref="Shader"/> (via <see cref="ShaderUtil.CreateShaderAsset"/>)</item>
    ///   <item>SubAsset: Standalone GPUDriven <see cref="ComputeShader"/> (via <see cref="ShaderUtil.CreateComputeShaderAsset"/>)</item>
    ///   <item>SubAsset: <see cref="VividSlabProgramDescriptor"/> runtime metadata & layout</item>
    /// </list>
    /// Automatically reimports when linked <c>.vmatg</c> MaterialGraphs or base templates change.
    /// </summary>
    [ScriptedImporter(13, Extension)]
    internal sealed class VividSlabShaderImporter : ScriptedImporter
    {
        internal const string Extension = "slab.shader";
        internal const string MainShaderObjectId = "MainAsset";
        internal const string ResolveComputeObjectId = "GPUDrivenResolveCompute";
        internal const string DescriptorObjectId = "SlabProgramDescriptor";

        private static readonly string[] s_SharedResolveIncludeRelativePaths =
        {
            "Runtime/SubSystem/GPUDriven/VividGPUDrivenStructs.cs.hlsl",
            "Runtime/SubSystem/PrimitiveScene/VividPrimitiveSceneTypes.cs.hlsl",
            "Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl",
            "Shaders/Core/Public/GPUDriven/VividSurfaceSampling.hlsl",
            "Shaders/Core/Public/GPUDriven/VividMaterialSurface.hlsl",
            "Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl",
            "Shaders/Core/Public/GPUDriven/VividBarycentric.hlsl",
            "Shaders/Core/Public/VividSlabSurfaceContract.hlsl",
            "Shaders/Core/Public/VividSubstrateBuffer.hlsl",
            "Shaders/Core/Public/VividHairFurBSDF.hlsl",
        };

        public override void OnImportAsset(AssetImportContext ctx)
        {
            RegisterSharedIncludeDependencies(ctx);
            string sourceText = File.ReadAllText(ctx.assetPath);
            VividSlabShaderAssemblyResult assembled = AssembleFromAssetPath(
                ctx.assetPath,
                sourceText,
                ctx);

            for (int i = 0; i < assembled.Diagnostics.Length; i++)
            {
                string diag = assembled.Diagnostics[i];
                if (diag.StartsWith("SLAB-W", StringComparison.Ordinal))
                    ctx.LogImportWarning(diag);
                else
                    ctx.LogImportError(diag);
            }

            string rasterToCompile = assembled.Succeeded
                ? assembled.RasterShaderSource
                : VividSlabShaderAssembler.BuildErrorShaderSource(assembled.ShaderName);

            Shader rasterShader = ShaderUtil.CreateShaderAsset(ctx, rasterToCompile, false);
            if (rasterShader == null)
            {
                ctx.LogImportError(
                    $"SLAB-E010: ShaderUtil.CreateShaderAsset returned null for '{ctx.assetPath}'.");
                rasterShader = ShaderUtil.CreateShaderAsset(
                    ctx,
                    VividSlabShaderAssembler.BuildErrorShaderSource(assembled.ShaderName),
                    false);
            }

            ComputeShader resolveCompute = null;
            if (assembled.Succeeded && !string.IsNullOrWhiteSpace(assembled.ComputeResolveSource))
            {
                resolveCompute = ShaderUtil.CreateComputeShaderAsset(
                    ctx,
                    assembled.ComputeResolveSource);
                if (resolveCompute != null)
                {
                    resolveCompute.name = $"{Path.GetFileNameWithoutExtension(ctx.assetPath)}_Resolve";
                    ctx.AddObjectToAsset(ResolveComputeObjectId, resolveCompute);
                }
                else
                {
                    ctx.LogImportError(
                        $"SLAB-E011: ShaderUtil.CreateComputeShaderAsset returned null for '{ctx.assetPath}'.");
                }
            }

            var descriptor = ScriptableObject.CreateInstance<VividSlabProgramDescriptor>();
            descriptor.name = $"{Path.GetFileNameWithoutExtension(ctx.assetPath)}_Descriptor";
            descriptor.Apply(
                rasterShader,
                resolveCompute,
                assembled.ResolveKernelName,
                assembled.ShaderName,
                assembled.BaseTemplatePath,
                assembled.LinkedGraphPath,
                assembled.EstimatedSubstrateBytesPerPixel,
                assembled.ParameterStrideInWords,
                assembled.ParameterSlots,
                assembled.ResourceSlots,
                assembled.HasLinkedGraphSurface,
                assembled.HasCustomSurfaceHook,
                assembled.SourceContentHash,
                !assembled.Succeeded || resolveCompute == null,
                assembled.Diagnostics,
                assembled.IsMultiClosure,
                assembled.ClosureCount,
                assembled.HasCustomCoverage,
                assembled.HasVertexOffset,
                assembled.WorldPositionOffsetPadding,
                assembled.WorldPositionOffsetPaddingProperties,
                assembled.ShellLayerCount,
                assembled.ShadowShellLayerCount);

            ctx.AddObjectToAsset(MainShaderObjectId, rasterShader);
            ctx.AddObjectToAsset(DescriptorObjectId, descriptor);
            ctx.SetMainObject(rasterShader);
        }

        internal static VividSlabShaderAssemblyResult AssembleFromAssetPath(
            string assetPath,
            string sourceText,
            AssetImportContext ctx = null)
        {
            var diagnostics = new List<string>();
            VividSlabShaderDirectives directives = VividSlabShaderAssembler.ParseDirectives(sourceText);

            string resolvedBasePath = ResolveAssetPath(directives.BaseTemplatePath);
            string baseTemplateText = string.Empty;
            if (!string.IsNullOrEmpty(directives.BaseTemplatePath))
            {
                if (string.IsNullOrEmpty(resolvedBasePath) || !File.Exists(resolvedBasePath))
                {
                    diagnostics.Add(
                        $"SLAB-E004: Linked @vivid-base template not found: '{directives.BaseTemplatePath}'.");
                }
                else
                {
                    ctx?.DependsOnSourceAsset(resolvedBasePath);
                    baseTemplateText = File.ReadAllText(resolvedBasePath);
                }
            }

            string resolvedGraphPath = ResolveAssetPath(directives.LinkedGraphPath);
            CompiledMaterialProgram compiledGraphProgram = null;
            if (!string.IsNullOrEmpty(directives.LinkedGraphPath))
            {
                if (string.IsNullOrEmpty(resolvedGraphPath) || !File.Exists(resolvedGraphPath))
                {
                    diagnostics.Add(
                        $"SLAB-E005: Linked @vivid-graph asset not found: '{directives.LinkedGraphPath}'.");
                }
                else
                {
                    // Depend directly on the .vmatg source asset so saving the graph
                    // automatically reimports this .slab.shader without requiring a Catalog bake.
                    ctx?.DependsOnSourceAsset(resolvedGraphPath);
                    try
                    {
                        MaterialGraphEditorGraph graph = ctx != null
                            ? GraphDatabase.LoadGraphForImporter<MaterialGraphEditorGraph>(resolvedGraphPath)
                            : GraphDatabase.LoadGraph<MaterialGraphEditorGraph>(resolvedGraphPath);
                        if (graph == null)
                        {
                            diagnostics.Add(
                                $"SLAB-E006: Failed to load MaterialGraph '{resolvedGraphPath}'.");
                        }
                        else
                        {
                            MaterialGraphCompilationResult compileResult =
                                MaterialGraphEditorCompiler.Compile(graph);
                            if (!compileResult.Succeeded || compileResult.Program == null)
                            {
                                for (int i = 0; i < compileResult.Diagnostics.Count; i++)
                                {
                                    diagnostics.Add(
                                        $"MAT-GRAPH: {compileResult.Diagnostics[i].Message}");
                                }
                                if (compileResult.Diagnostics.Count == 0)
                                {
                                    diagnostics.Add(
                                        $"SLAB-E007: MaterialGraph '{resolvedGraphPath}' failed to compile.");
                                }
                            }
                            else
                            {
                                compiledGraphProgram = compileResult.Program;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add(
                            $"SLAB-E008: Exception compiling MaterialGraph '{resolvedGraphPath}': {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            return VividSlabShaderAssembler.Assemble(
                assetPath,
                sourceText,
                resolvedBasePath,
                baseTemplateText,
                resolvedGraphPath,
                compiledGraphProgram,
                diagnostics);
        }

        internal static string ResolveAssetPath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return string.Empty;

            string normalized = rawPath.Trim().Replace('\\', '/');
            if (File.Exists(normalized))
                return normalized;

            string[] prefixes =
            {
                "Packages/com.vivid.render-pipelines/",
                "Packages/com.af8a2a.vividrp/",
                "Packages/VividRP/",
            };
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (normalized.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    string relative = normalized.Substring(prefixes[i].Length);
                    string preferred = VividPackagePathUtility.GetPreferredAssetPath(relative);
                    if (File.Exists(preferred))
                        return preferred;

                    string[] candidates = VividPackagePathUtility.GetCandidateAssetPaths(relative);
                    for (int c = 0; c < candidates.Length; c++)
                    {
                        if (File.Exists(candidates[c]))
                            return candidates[c];
                    }
                }
            }

            return normalized;
        }

        private static void RegisterSharedIncludeDependencies(AssetImportContext ctx)
        {
            if (ctx == null)
                return;

            for (int i = 0; i < s_SharedResolveIncludeRelativePaths.Length; i++)
            {
                string preferred = VividPackagePathUtility.GetPreferredAssetPath(
                    s_SharedResolveIncludeRelativePaths[i]);
                if (File.Exists(preferred))
                {
                    ctx.DependsOnSourceAsset(preferred);
                    continue;
                }

                string[] candidates = VividPackagePathUtility.GetCandidateAssetPaths(
                    s_SharedResolveIncludeRelativePaths[i]);
                for (int c = 0; c < candidates.Length; c++)
                {
                    if (File.Exists(candidates[c]))
                    {
                        ctx.DependsOnSourceAsset(candidates[c]);
                        break;
                    }
                }
            }
        }
    }
}
