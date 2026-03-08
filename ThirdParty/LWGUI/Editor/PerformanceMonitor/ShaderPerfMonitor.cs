// Copyright (c) Jason Ma

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LWGUI.PerformanceMonitor.ShaderCompiler;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace LWGUI.PerformanceMonitor
{
    public static class ShaderPerfMonitor
    {
        #region Global Settings

        public static GraphicsTier           graphicsTier           = (GraphicsTier)(-1);
        public static ShaderCompilerPlatform shaderCompilerPlatform = ShaderCompilerPlatform.D3D;
        public static BuildTarget            buildTarget            = BuildTarget.StandaloneWindows64;

        #endregion

        public static List<string> GetMaterialAndGlobalAndUserOverrideActiveKeywords(Material material, string shaderUID)
        {
            var output = new List<string>();

            foreach (var localKeyword in material.enabledKeywords)
            {
                if (ToolbarHelper.IsUserKeywordOverride(shaderUID, localKeyword.name))
                {
                    if (ToolbarHelper.IsUserKeywordEnabled(shaderUID, localKeyword.name))
                        output.Add(localKeyword.name);
                }
                else
                {
                    output.Add(localKeyword.name);
                }
            }

            foreach (var keyword in material.shader.keywordSpace.keywords)
            {
                if (ToolbarHelper.IsUserKeywordOverride(shaderUID, keyword.name))
                {
                    if (ToolbarHelper.IsUserKeywordEnabled(shaderUID, keyword.name))
                        output.Add(keyword.name);
                }
                else
                {
                    if (!keyword.isValid)
                        continue;
                    
                    // Is a global keyword?
                    if (keyword.isOverridable && Shader.IsKeywordEnabled(keyword.name))
                        output.Add(keyword.name);
                }
            }

            return output.Distinct().ToList();
        }

        public static string ComputeShaderVariantHash(List<string> keywords, BuildTarget target, ShaderCompilerPlatform platform, GraphicsTier tier)
        {
            var sorted = keywords?.OrderBy(k => k) ?? Enumerable.Empty<string>();
            var key = string.Join(";", sorted) + $"|{target}|{platform}|{tier}";
            using var md5 = MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(key);
            var hash = md5.ComputeHash(bytes);
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }

        public static List<ShaderPerfData> GetShaderVariantPerfDatas(Shader shader, List<string> keywords)
        {
            var output = new List<ShaderPerfData>();
            var shaderData = ShaderUtil.GetShaderData(shader);
            var subshader = shaderData.ActiveSubshader;

            IShaderCompiler compiler = GetActiveCompiler();

            for (int i = 0; i < subshader.PassCount; i++)
            {
                var pass = subshader.GetPass(i);
                for (int j = 0; j < (int)ShaderType.Count; j++)
                {
                    var shaderType = (ShaderType)j;
                    if (!pass.HasShaderStage(shaderType))
                        continue;

                    // Collect input data
                    var hash = ComputeShaderVariantHash(keywords, buildTarget, shaderCompilerPlatform, graphicsTier);
                    var shaderPerfData = new ShaderPerfData
                    {
                        subshaderIndex = shaderData.ActiveSubshaderIndex,
                        passIndex = i,
                        passName = IOHelper.GetValidFileName(pass.Name),
                        shaderType = shaderType,
                        hash = hash,
                    };

                    shaderPerfData.shaderTypeName = shaderPerfData.shaderType.ToString();
                    shaderPerfData.compiledShaderDirectory = IOHelper.GetCompiledShaderVariantCacheDirectory(shader, shaderPerfData);

                    if (compiler != null)
                    {
                        shaderPerfData.compiledShaderPath = compiler.GetCompiledShaderPath(shaderPerfData, shaderPerfData.compiledShaderDirectory, shaderPerfData.shaderTypeName);

                        if (!string.IsNullOrEmpty(shaderPerfData.compiledShaderPath))
                        {
                            // Compile and create cache
                            shaderPerfData.isCompiledSuccessful = true;
                            string compiledShader;
                            if (!File.Exists(shaderPerfData.compiledShaderPath))
                            {
                                shaderPerfData.isCompiledSuccessful = compiler.CompilePass(shaderPerfData, pass, shaderType, keywords.ToArray(),
                                    out compiledShader);
                                IOHelper.WriteTextFile(shaderPerfData.compiledShaderPath, compiledShader);
                            }
                            else
                            {
                                compiledShader = IOHelper.ReadTextFile(shaderPerfData.compiledShaderPath);
                            }

                            shaderPerfData.isCompiledSuccessful &= IOHelper.ExistAndNotEmpty(shaderPerfData.compiledShaderPath);

                            // Analyze performance
                            if (shaderPerfData.isCompiledSuccessful)
                            {
                                shaderPerfData.stats = compiler.AnalyzeShaderPerformance(shaderPerfData, compiledShader);
                                
                                if (shaderPerfData.stats == null)
                                    Debug.LogError($"LWGUI: Failed to Analyze Shader: {shader.name} | Subshader: {shaderPerfData.subshaderIndex} | Pass: {shaderPerfData.passName} | Stage: {shaderType}\n" +
                                                   $"Keywords: \n{string.Join('\n', keywords)}");
                            }
                            else
                            {
                                Debug.LogError($"LWGUI: Failed to Compile Shader: {shader.name} | Subshader: {shaderPerfData.subshaderIndex} | Pass: {shaderPerfData.passName} | Stage: {shaderType}\n" +
                                               $"Keywords: \n{string.Join('\n', keywords)}");
                            }
                        }
                        else
                        {
                            Debug.LogError("LWGUI: Unable to get the compiled Shader path!");
                            break;
                        }
                    }

                    output.Add(shaderPerfData);
                }
            }

            return output;
        }

        public static void ClearShaderPerfCache(Shader shader)
        {
            IOHelper.ClearShaderPerfCache(shader);
        }

        private static IShaderCompiler _cachedActiveCompiler;
        private static readonly object _compilerLock = new object();
        
        public static IShaderCompiler GetActiveCompiler()
        {
            if (_cachedActiveCompiler != null)
                return _cachedActiveCompiler;
            
            lock (_compilerLock)
            {
                if (_cachedActiveCompiler != null)
                    return _cachedActiveCompiler;
                
                var assembly = Assembly.GetExecutingAssembly();
                var compilerTypes = assembly.GetTypes()
                    .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IShaderCompiler).IsAssignableFrom(t))
                    .ToList();

                Type bestType = null;
                int bestPriority = int.MinValue;

                foreach (var compilerType in compilerTypes)
                {
                    try
                    {
                        var isSupportProp = compilerType.GetProperty("isSupportCurrentPlatform",
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                        if (isSupportProp == null)
                            continue;

                        bool isSupported = (bool)isSupportProp.GetValue(null);
                        if (!isSupported)
                            continue;

                        var priorityProp = compilerType.GetProperty("priority",
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                        int priority = 0;
                        if (priorityProp != null)
                        {
                            try { priority = (int)priorityProp.GetValue(null); } catch { priority = 0; }
                        }

                        if (bestType == null || priority > bestPriority)
                        {
                            bestType = compilerType;
                            bestPriority = priority;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"LWGUI: Error inspecting compiler type {compilerType.Name}: {e.Message}");
                    }
                }

                if (bestType != null)
                {
                    try
                    {
                        var instanceProp = bestType.GetProperty("instance",
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                        if (instanceProp != null)
                        {
                            var instance = instanceProp.GetValue(null) as IShaderCompiler;
                            if (instance != null)
                            {
                                _cachedActiveCompiler = instance;
                                return instance;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"LWGUI: Error getting compiler instance of type {bestType.Name}: {e.Message}");
                    }
                }

                Debug.LogWarning("LWGUI: No supported shader compiler found!");
                return null;
            }
        }

        public static void ResetActiveCompiler()
        {
            lock (_compilerLock)
            {
                _cachedActiveCompiler = null;
            }
        }
    }
}