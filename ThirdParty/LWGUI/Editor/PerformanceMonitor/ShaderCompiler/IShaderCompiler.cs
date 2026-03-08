// Copyright (c) Jason Ma

using UnityEditor.Rendering;
using UnityEngine.Rendering;
using UnityEditor;

namespace LWGUI.PerformanceMonitor.ShaderCompiler
{
    public interface IShaderCompiler
    {
        public static IShaderCompiler instance { get; }

        public static bool isSupportCurrentPlatform => true;

        public static int priority => 0;

        public string compilerName { get; }

        public ShaderCompilerPlatform api    { get; set; }
        public BuildTarget            target { get; set; }
        public GraphicsTier           tier   { get; set; }

        /// <summary>
        /// The path to the Shader compilation result stored in text.
        /// </summary>
        public string GetCompiledShaderPath(ShaderPerfData shaderPerfData, string compiledShaderDirectory, string shaderTypeName);

        /// <summary>
        /// Try to compile a pass variant. Returns true and outputs string on success.
        /// </summary>
        public bool CompilePass(ShaderPerfData shaderPerfData, ShaderData.Pass pass, ShaderType shaderType, string[] keywords,
                                out string     compiledShader);

        /// <summary>
        /// Analyze a compiled shader (or readable asm) and return a compiler-specific stats object.
        /// The returned object is opaque to the caller and will be stored in ShaderPerfData.stats.
        /// Return null on failure.
        /// </summary>
        public object AnalyzeShaderPerformance(ShaderPerfData shaderPerfData, string compiledShader);

        public void DrawShaderPerformanceStatsHeader(LWGUIMetaDatas metaDatas) { }

        /// <summary>
        /// Draw a single line (pass) of shader performance UI inside the toolbar area.
        /// Compiler-specific UI (Find/Open buttons, label contents) should be implemented here.
        /// </summary>
        public void DrawShaderPerformanceStatsLine(LWGUIMetaDatas metaDatas, ShaderPerfData shaderPerfData);

        public void DrawShaderPerformanceStatsFooter(LWGUIMetaDatas metaDatas) { }
    }
}