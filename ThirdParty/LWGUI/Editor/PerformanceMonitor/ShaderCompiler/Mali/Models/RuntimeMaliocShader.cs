// Copyright (c) Jason Ma

using System;
using System.Collections.Generic;

namespace LWGUI.PerformanceMonitor.ShaderCompiler.Mali
{
    [Serializable]
    public class RuntimeMaliocShader
    {
        [Serializable]
        public enum ShaderVariantPipelineType
        {
            Null,
            Arithmetic,
            LoadStore,
            Varying,
            Texture,
        }

        public bool HasErrors;
        public List<string> Errors;
        public List<string> Warnings;
        public List<ShaderProperty> Properties;
        public List<ShaderVariant> Variants;

        [Serializable]
        public class ShaderProperty
        {
            public enum Unit
            {
                None,
                Percent,
            }

            public string Name;
            public DynamicValue Value;
            public Unit ValueUnit;
        }

        [Serializable]
        public class ShaderVariant
        {
            public string Name;
            public List<ShaderVariantPipelineType> Pipelines;
            public ShaderPipelineCycles LongestPathCycles;
            public ShaderPipelineCycles ShortestPathCycles;
            public ShaderPipelineCycles TotalCycles;
            public List<ShaderProperty> Properties;
        }

        [Serializable]
        public class ShaderPipelineCycles
        {
            public List<float> PipelineCycles;
            public ShaderVariantPipelineType BoundPipeline;
        }
    }
}