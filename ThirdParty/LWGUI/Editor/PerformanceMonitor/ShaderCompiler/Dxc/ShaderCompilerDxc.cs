// Copyright (c) Jason Ma

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LWGUI.PerformanceMonitor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace LWGUI.PerformanceMonitor.ShaderCompiler
{
    public class ShaderCompilerDxc : IShaderCompiler
    {
        public struct ShaderPerfStats
        {
            public float estimatedAluCost;
            public int textureOperationCount;
            public int instructionCount;
            public int temporaryValueCount;
            public int flowControlCount;
            public bool isValid;
        }

        private static ShaderCompilerDxc _instance;
        public static ShaderCompilerDxc instance => _instance ??= new ShaderCompilerDxc();

        public ShaderCompilerPlatform api    { get; set; } = ShaderCompilerPlatform.D3D12;
        public BuildTarget            target { get; set; } = BuildTarget.StandaloneWindows64;
        public GraphicsTier           tier   { get; set; } = (GraphicsTier)(-1);

        public string compilerName => "DXC (DXIL)";

        private static int _isSupportCurrentPlatform = -1;

        public static bool isSupportCurrentPlatform
        {
            get
            {
#if UNITY_EDITOR_WIN
                if (_isSupportCurrentPlatform == -1)
                {
                    _cachedDxcPath = FindDxcExecutable();
                    _isSupportCurrentPlatform = !string.IsNullOrEmpty(_cachedDxcPath) && File.Exists(_cachedDxcPath) ? 1 : 0;
                }

                return _isSupportCurrentPlatform == 1;
#else
                return false;
#endif
            }
        }

        // Prefer DXC over the legacy FXC backend, while preserving Malioc as the
        // highest-priority opt-in offline compiler when it is installed.
        public static int priority => 5;

        public string GetCompiledShaderPath(ShaderPerfData shaderPerfData, string compiledShaderDirectory, string shaderTypeName)
            => Path.Combine(compiledShaderDirectory, $"Dxc_{api}_{target}_{shaderTypeName}.txt");

        public string GetPreprocessedShaderPath(ShaderPerfData shaderPerfData)
            => Path.Combine(shaderPerfData.compiledShaderDirectory, $"Dxc_{api}_{target}_{shaderPerfData.shaderTypeName}.hlsl");

        public string GetCompiledDxilPath(ShaderPerfData shaderPerfData)
            => Path.Combine(shaderPerfData.compiledShaderDirectory, $"Dxc_{api}_{target}_{shaderPerfData.shaderTypeName}.dxil");

        public bool CompilePass(ShaderPerfData shaderPerfData, ShaderData.Pass pass, ShaderType shaderType, string[] keywords,
                                out string compiledShader)
        {
            compiledShader = string.Empty;

            if (shaderPerfData == null || pass == null || keywords == null)
                return false;

            if (api != ShaderCompilerPlatform.D3D12)
            {
                Debug.LogError($"LWGUI: DXC only supports the D3D shader compiler platform, not {api}.");
                return false;
            }

            if (string.IsNullOrEmpty(_dxcAbsPath) || !File.Exists(_dxcAbsPath))
                return false;

            if (!TryGetTargetProfile(shaderType, out var targetProfile))
                return false;

            var entryPoint = GetEntryPoint(pass.SourceCode, shaderType);
            if (shaderType != ShaderType.RayTracing && string.IsNullOrEmpty(entryPoint))
            {
                Debug.LogError($"LWGUI: DXC could not find the {shaderType} entry point in pass '{pass.Name}'.");
                return false;
            }

            var preprocessed = pass.PreprocessVariant(shaderType, keywords, api, target, tier, false);
            if (!preprocessed.Success || string.IsNullOrWhiteSpace(preprocessed.PreprocessedCode))
            {
                var messages = preprocessed.Messages == null
                    ? string.Empty
                    : string.Join("\n", preprocessed.Messages.Select(message => message.message));
                Debug.LogError($"LWGUI: DXC failed to preprocess pass '{pass.Name}' ({shaderType}).\n{messages}");
                return false;
            }

            var hlslPath = GetPreprocessedShaderPath(shaderPerfData);
            var dxilPath = GetCompiledDxilPath(shaderPerfData);
            IOHelper.WriteTextFile(hlslPath, preprocessed.PreprocessedCode);

            var entryPointArgument = string.IsNullOrEmpty(entryPoint) ? string.Empty : $"-E \"{entryPoint}\" ";
            var arguments = $"-T {targetProfile} {entryPointArgument}-HV 2021 -O3 -Fo \"{dxilPath}\" \"{hlslPath}\"";
            if (!IOHelper.RunProcess(_dxcAbsPath, arguments, out var diagnostics, false))
                return false;

            if (!string.IsNullOrWhiteSpace(diagnostics))
                Debug.LogWarning($"LWGUI: DXC diagnostics for pass '{pass.Name}' ({shaderType}):\n{diagnostics}");

            return IOHelper.RunProcess(_dxcAbsPath, $"-dumpbin \"{dxilPath}\"", out compiledShader)
                   && !string.IsNullOrWhiteSpace(compiledShader);
        }

        public object AnalyzeShaderPerformance(ShaderPerfData shaderPerfData, string compiledShader)
            => ParseDxilStats(compiledShader);

        public void DrawShaderPerformanceStatsHeader(LWGUIMetaDatas metaDatas)
        {
            EditorGUILayout.LabelField(" ", " ALU Cost   Texture   DXIL Inst   SSA Values");
        }

        public void DrawShaderPerformanceStatsLine(LWGUIMetaDatas metaDatas, ShaderPerfData shaderPerfData)
        {
            EditorGUILayout.BeginHorizontal();

            if (shaderPerfData.stats is ShaderPerfStats { isValid: true } stats)
            {
                var statsStr = $"{stats.estimatedAluCost,8:0.0} {stats.textureOperationCount,9:0} {stats.instructionCount,11:0} {stats.temporaryValueCount,12:0}";
                EditorGUILayout.LabelField($"{shaderPerfData.passName} | {shaderPerfData.shaderTypeName}", statsStr, GUIStyles.label_monospace);
                ToolbarHelper.DrawShaderPerformanceStatsLineButtons(shaderPerfData);
            }
            else
            {
                var status = shaderPerfData.isCompiledSuccessful ? "ANALYSIS FAILED" : "COMPILATION FAILED";
                EditorGUILayout.LabelField($"{shaderPerfData.passName} | {shaderPerfData.shaderTypeName}", status);
            }

            EditorGUILayout.EndHorizontal();
        }

        public void DrawShaderPerformanceStatsFooter(LWGUIMetaDatas metaDatas)
        {
            EditorGUILayout.HelpBox(
                "DXC statistics are estimated from optimized DXIL IR. SSA Values are an IR-level pressure indicator, not physical GPU registers; final register allocation is driver and GPU dependent.",
                MessageType.Info);
        }

        private static readonly Dictionary<string, float> IrOpcodeWeights = new(StringComparer.Ordinal)
        {
            { "add", 1.0f },
            { "fadd", 1.0f },
            { "sub", 1.0f },
            { "fsub", 1.0f },
            { "mul", 1.0f },
            { "fmul", 1.0f },
            { "udiv", 8.0f },
            { "sdiv", 8.0f },
            { "fdiv", 8.0f },
            { "urem", 8.0f },
            { "srem", 8.0f },
            { "frem", 8.0f },
            { "shl", 1.0f },
            { "lshr", 1.0f },
            { "ashr", 1.0f },
            { "and", 1.0f },
            { "or", 1.0f },
            { "xor", 1.0f },
            { "icmp", 1.0f },
            { "fcmp", 1.0f },
            { "select", 1.0f },
            { "trunc", 1.0f },
            { "zext", 1.0f },
            { "sext", 1.0f },
            { "fptoui", 1.0f },
            { "fptosi", 1.0f },
            { "uitofp", 1.0f },
            { "sitofp", 1.0f },
            { "fptrunc", 1.0f },
            { "fpext", 1.0f },
            { "bitcast", 1.0f },
        };

        private static readonly Dictionary<string, float> DxOperationWeights = new(StringComparer.Ordinal)
        {
            { "Dot2", 2.0f },
            { "Dot3", 3.0f },
            { "Dot4", 4.0f },
            { "FAbs", 1.0f },
            { "FMax", 1.0f },
            { "FMin", 1.0f },
            { "FMad", 1.0f },
            { "IMad", 1.0f },
            { "UMad", 1.0f },
            { "Round_ne", 1.0f },
            { "Round_ni", 1.0f },
            { "Round_pi", 1.0f },
            { "Round_z", 1.0f },
            { "Saturate", 1.0f },
            { "Cos", 4.0f },
            { "Exp", 4.0f },
            { "Log", 4.0f },
            { "Rsqrt", 4.0f },
            { "Sin", 4.0f },
            { "Sqrt", 4.0f },
        };

        private static ShaderPerfStats ParseDxilStats(string dxilText)
        {
            if (string.IsNullOrWhiteSpace(dxilText))
                return new ShaderPerfStats();

            var estimatedAluCost = 0f;
            var textureOperationCount = 0;
            var instructionCount = 0;
            var temporaryValueCount = 0;
            var flowControlCount = 0;
            var sawFunction = false;
            var inFunction = false;

            using var reader = new StringReader(dxilText);
            while (reader.ReadLine() is { } line)
            {
                line = line.Trim();

                if (line.StartsWith("define ", StringComparison.Ordinal))
                {
                    sawFunction = true;
                    inFunction = true;
                    continue;
                }

                if (!inFunction)
                    continue;

                if (line == "}")
                {
                    inFunction = false;
                    continue;
                }

                if (string.IsNullOrEmpty(line) || line.StartsWith(";", StringComparison.Ordinal) || line.EndsWith(":", StringComparison.Ordinal))
                    continue;

                instructionCount++;

                var equalsIndex = line.IndexOf(" = ", StringComparison.Ordinal);
                var instruction = line;
                if (equalsIndex >= 0)
                {
                    temporaryValueCount++;
                    instruction = line.Substring(equalsIndex + 3);
                }

                var opcodeEnd = instruction.IndexOfAny(new[] { ' ', '\t' });
                var opcode = opcodeEnd > 0 ? instruction.Substring(0, opcodeEnd) : instruction;
                if (IrOpcodeWeights.TryGetValue(opcode, out var irWeight))
                    estimatedAluCost += irWeight;

                if (opcode is "br" or "switch" or "indirectbr")
                    flowControlCount++;

                var operationName = GetDxOperationName(line);
                if (string.IsNullOrEmpty(operationName))
                    continue;

                if (IsTextureOperation(operationName))
                    textureOperationCount++;

                if (DxOperationWeights.TryGetValue(operationName, out var dxWeight))
                    estimatedAluCost += dxWeight;
            }

            return new ShaderPerfStats
            {
                estimatedAluCost = estimatedAluCost,
                textureOperationCount = textureOperationCount,
                instructionCount = instructionCount,
                temporaryValueCount = temporaryValueCount,
                flowControlCount = flowControlCount,
                isValid = sawFunction && instructionCount > 0,
            };
        }

        private static string GetDxOperationName(string line)
        {
            if (!line.Contains("@dx.op.", StringComparison.Ordinal))
                return string.Empty;

            var commentIndex = line.IndexOf(';');
            if (commentIndex < 0)
                return string.Empty;

            var operationStart = commentIndex + 1;
            while (operationStart < line.Length && char.IsWhiteSpace(line[operationStart]))
                operationStart++;

            var operationEnd = line.IndexOf('(', operationStart);
            return operationEnd > operationStart
                ? line.Substring(operationStart, operationEnd - operationStart)
                : string.Empty;
        }

        private static bool IsTextureOperation(string operationName)
        {
            return operationName.StartsWith("Sample", StringComparison.Ordinal)
                   || operationName.StartsWith("Gather", StringComparison.Ordinal)
                   || operationName is "TextureLoad" or "TextureStore";
        }

        private static bool TryGetTargetProfile(ShaderType shaderType, out string targetProfile)
        {
            targetProfile = shaderType switch
            {
                ShaderType.Vertex => "vs_6_0",
                ShaderType.Fragment => "ps_6_0",
                ShaderType.Geometry => "gs_6_0",
                ShaderType.Hull => "hs_6_0",
                ShaderType.Domain => "ds_6_0",
                ShaderType.RayTracing => "lib_6_3",
                _ => string.Empty,
            };

            return !string.IsNullOrEmpty(targetProfile);
        }

        private static string GetEntryPoint(string passSourceCode, ShaderType shaderType)
        {
            if (shaderType == ShaderType.RayTracing || string.IsNullOrEmpty(passSourceCode))
                return string.Empty;

            var pragmaName = shaderType switch
            {
                ShaderType.Vertex => "vertex",
                ShaderType.Fragment => "fragment",
                ShaderType.Geometry => "geometry",
                ShaderType.Hull => "hull",
                ShaderType.Domain => "domain",
                _ => string.Empty,
            };

            if (string.IsNullOrEmpty(pragmaName))
                return string.Empty;

            var match = Regex.Match(
                passSourceCode,
                $@"^\s*#pragma\s+{Regex.Escape(pragmaName)}\s+([A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string _cachedDxcPath;

        private static string _dxcAbsPath
        {
            get
            {
                if (string.IsNullOrEmpty(_cachedDxcPath))
                    _cachedDxcPath = FindDxcExecutable();
                return _cachedDxcPath;
            }
        }

        private static string FindDxcExecutable()
        {
#if UNITY_EDITOR_WIN
            var explicitPath = Environment.GetEnvironmentVariable("DXC_PATH");
            var resolvedExplicitPath = ResolveDxcCandidate(explicitPath);
            if (!string.IsNullOrEmpty(resolvedExplicitPath))
                return resolvedExplicitPath;

            var vulkanSdkPath = Environment.GetEnvironmentVariable("VULKAN_SDK");
            var vulkanDxcPath = ResolveDxcCandidate(string.IsNullOrEmpty(vulkanSdkPath) ? null : Path.Combine(vulkanSdkPath, "Bin"));
            if (!string.IsNullOrEmpty(vulkanDxcPath))
                return vulkanDxcPath;

            var pathEnvironment = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnvironment))
            {
                foreach (var directory in pathEnvironment.Split(Path.PathSeparator))
                {
                    var pathDxc = ResolveDxcCandidate(directory.Trim().Trim('"'));
                    if (!string.IsNullOrEmpty(pathDxc))
                        return pathDxc;
                }
            }

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var windowsSdkBinPath = Path.Combine(programFilesX86, "Windows Kits", "10", "bin");
            if (Directory.Exists(windowsSdkBinPath))
            {
                try
                {
                    foreach (var versionDirectory in Directory.GetDirectories(windowsSdkBinPath).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        var x64Dxc = ResolveDxcCandidate(Path.Combine(versionDirectory, "x64"));
                        if (!string.IsNullOrEmpty(x64Dxc))
                            return x64Dxc;

                        var x86Dxc = ResolveDxcCandidate(Path.Combine(versionDirectory, "x86"));
                        if (!string.IsNullOrEmpty(x86Dxc))
                            return x86Dxc;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"LWGUI: Failed to search the Windows SDK for dxc.exe: {exception.Message}");
                }
            }
#endif
            return null;
        }

        private static string ResolveDxcCandidate(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (File.Exists(path) && string.Equals(Path.GetFileName(path), "dxc.exe", StringComparison.OrdinalIgnoreCase))
                return path;

            var candidate = Path.Combine(path, "dxc.exe");
            return File.Exists(candidate) ? candidate : null;
        }
    }
}
