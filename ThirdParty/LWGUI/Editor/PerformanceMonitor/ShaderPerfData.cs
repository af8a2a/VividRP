// Copyright (c) Jason Ma

using System;
using System.Collections.Generic;
using UnityEditor.Rendering;

namespace LWGUI.PerformanceMonitor
{
    public class ShaderPerfData
    {
        public int        subshaderIndex = -1;
        public int        passIndex      = -1;
        public string     passName       = string.Empty;
        public ShaderType shaderType;
        public string     hash                 = string.Empty;
        public bool       isCompiledSuccessful = false;

        // Path
        public string shaderTypeName          = string.Empty;
        public string compiledShaderDirectory = string.Empty;
        public string compiledShaderPath      = string.Empty;

        // Opaque compiler-specific stats.
        // Compiler implementations should populate this with their own stats object (or null if unavailable).
        public object stats;
    }
}