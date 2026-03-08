// Copyright (c) Jason Ma

using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.Rendering;
using UnityEngine.Rendering;
using UnityEngine;
using UnityEditor;
using LWGUI.PerformanceMonitor.ShaderCompiler.Mali;

namespace LWGUI.PerformanceMonitor.ShaderCompiler
{
    public class ShaderCompilerMali : IShaderCompiler
    {
        private static ShaderCompilerMali _instance;
        public static  ShaderCompilerMali instance => _instance ??= new ShaderCompilerMali();

        public ShaderCompilerPlatform api    { get; set; } = ShaderCompilerPlatform.GLES3x;
        public BuildTarget            target { get; set; } = BuildTarget.Android;
        public GraphicsTier           tier   { get; set; } = (GraphicsTier)(-1);

        public string compilerName => "Malioc";

        private static int _isSupportCurrentPlatform = -1;

        public static bool isSupportCurrentPlatform
        {
            get
            {
                if (_isSupportCurrentPlatform == -1)
                {
                    if (!IOHelper.RunCMD("malioc --list", out var output) || string.IsNullOrWhiteSpace(output) || !output.Contains("Compiler"))
                        _isSupportCurrentPlatform = 0;
                    else
                        _isSupportCurrentPlatform = 1;
                }

                return _isSupportCurrentPlatform == 1;
            }
        }
        
        public static int priority => 10;

        public string GetCompiledShaderPath(ShaderPerfData shaderPerfData, string compiledShaderDirectory, string shaderTypeName)
        {
            string ext;
            switch (shaderPerfData.shaderType)
            {
                // https://developer.arm.com/documentation/101863/8-8/Using-Mali-Offline-Compiler/Compiling-OpenGL-ES-shaders
                case ShaderType.Vertex:   ext = ".vert"; break;
                case ShaderType.Fragment: ext = ".frag"; break;
                case ShaderType.Geometry: ext = ".geom"; break;
                default:                  return null;
            }

            return Path.Combine(compiledShaderDirectory, $"Mali_{api}_{target}_{shaderTypeName}{ext}");
        }

        public string GetMaliJsonOutputPath(ShaderPerfData shaderPerfData)
            => Path.Combine(shaderPerfData.compiledShaderDirectory, $"Mali_{api}_{target}_{shaderPerfData.shaderTypeName}.json");


        public bool CompilePass(ShaderPerfData shaderPerfData, ShaderData.Pass pass, ShaderType shaderType, string[] keywords,
                                out string     compiledShader)
        {
            compiledShader = string.Empty;

            if (shaderPerfData == null || pass == null || keywords == null)
                return false;

            var compileInfo = pass.CompileVariant(shaderType, keywords, api, target, tier, true);
            if (!compileInfo.Success)
                return false;

            compiledShader = Encoding.UTF8.GetString(compileInfo.ShaderData);

            // Fix Mali Compiler Errors
            compiledShader = compiledShader.Replace("#version 300 es", "#version 320 es");
            compiledShader = compiledShader.Replace("#version 310 es", "#version 320 es");
            IOHelper.WriteTextFile(shaderPerfData.compiledShaderPath, compiledShader);

            return !string.IsNullOrWhiteSpace(compiledShader);
        }


        public object AnalyzeShaderPerformance(ShaderPerfData shaderPerfData, string compiledShader)
        {
            var jsonPath = GetMaliJsonOutputPath(shaderPerfData);
            string jsonString;

            if (!File.Exists(jsonPath))
            {
                // https://developer.arm.com/documentation/101863/8-8/Using-Mali-Offline-Compiler/Compiling-OpenGL-ES-shaders
                IOHelper.RunCMD($"malioc --core Mali-G76 --format json \"{shaderPerfData.compiledShaderPath}\"", out jsonString);
                IOHelper.WriteTextFile(jsonPath, jsonString);
            }
            else
            {
                jsonString = IOHelper.ReadTextFile(jsonPath);
            }

            if (IOHelper.ExistAndNotEmpty(jsonPath))
            {
                return MaliocOutputParser.Parse(jsonString);
            }

            return null;
        }

        public void DrawShaderPerformanceStatsHeader(LWGUIMetaDatas metaDatas)
        {
            EditorGUILayout.LabelField(" ", "Arithmetic   Load/Store   Varying   Texture");
        }

        // https://developer.arm.com/documentation/101863/8-8/Using-Mali-Offline-Compiler/Performance-analysis
        public void DrawShaderPerformanceStatsLine(LWGUIMetaDatas metaDatas, ShaderPerfData shaderPerfData)
        {
            EditorGUILayout.BeginHorizontal();

            if (shaderPerfData.stats is RuntimeMaliocShader stats)
            {
                if (stats.Variants is { Count: > 0 } && stats.Variants[0].Pipelines is { Count: > 0 })
                {
                    var variant = stats.Variants[0];
                    var cycles = Enumerable.Repeat(0.0f, variant.Pipelines.Count).ToList();

                    for (int i = 0; i < variant.Pipelines.Count; i++)
                    {
                        cycles[i] = Mathf.Max(Mathf.Max(variant.ShortestPathCycles.PipelineCycles[i],
                                variant.LongestPathCycles.PipelineCycles[i]),
                            variant.TotalCycles.PipelineCycles[i]);
                    }

                    // https://developer.arm.com/documentation/101863/8-8/Using-Mali-Offline-Compiler/Performance-analysis/Performance-table
                    float arithmeticCycle = 0;
                    float loadStoreCycle = 0;
                    float varyingCycle = 0;
                    float textureCycle = 0;
                    for (int i = 0; i < variant.Pipelines.Count; i++)
                    {
                        switch (variant.Pipelines[i])
                        {
                            case RuntimeMaliocShader.ShaderVariantPipelineType.Arithmetic: arithmeticCycle = cycles[i]; break;
                            case RuntimeMaliocShader.ShaderVariantPipelineType.LoadStore:  loadStoreCycle = cycles[i]; break;
                            case RuntimeMaliocShader.ShaderVariantPipelineType.Varying:    varyingCycle = cycles[i]; break;
                            case RuntimeMaliocShader.ShaderVariantPipelineType.Texture:    textureCycle = cycles[i]; break;
                        }
                    }

                    var statsStr = $"{arithmeticCycle,8:0.0} {loadStoreCycle,9:0.0} {varyingCycle,7:0.0} {textureCycle,6:0.0}";
                    EditorGUILayout.LabelField($"{shaderPerfData.passName} | {shaderPerfData.shaderTypeName}", statsStr, GUIStyles.label_monospace);

                    ToolbarHelper.DrawShaderPerformanceStatsLineButtons(shaderPerfData);
                    if (GUILayout.Button("Json", GUILayout.MaxWidth(40)))
                        IOHelper.OpenFile(GetMaliJsonOutputPath(shaderPerfData));
                }
                else
                {
                    EditorGUILayout.LabelField($"{shaderPerfData.passName} | {shaderPerfData.shaderTypeName}", "ANALYSIS FAILED");
                }
                
                if (stats.HasErrors)
                {
					var errorMsg = stats.Errors.Count > 0 ? stats.Errors[0] : "Unknown Error";
					Debug.LogError($"LWGUI: {shaderPerfData.passName} | {shaderPerfData.shaderTypeName} Error:\n{errorMsg}");
                }
            }
            else
            {
                var status = shaderPerfData.isCompiledSuccessful ? "ANALYSIS FAILED" : "COMPILATION FAILED";
                EditorGUILayout.LabelField($"{shaderPerfData.passName} | {shaderPerfData.shaderTypeName}", status);
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}