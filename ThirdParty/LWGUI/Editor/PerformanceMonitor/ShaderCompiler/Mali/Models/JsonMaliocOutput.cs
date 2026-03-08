// Copyright (c) Jason Ma
// ReSharper disable InconsistentNaming

using System;

namespace LWGUI.PerformanceMonitor.ShaderCompiler.Mali
{
    [Serializable]
    internal class JsonMaliocOutput
    {
        public Schema schema;
        public Shader[] shaders;

        [Serializable]
        public class Schema
        {
            public string name;
            public int version;
        }

        [Serializable]
        public class Shader
        {
            // Normal output fields
            public ShaderProperty[] properties;
            public ShaderVariant[] variants;

            // Error output fields
            public string[] errors;
            public string[] warnings;
            public string filename;
        }

        [Serializable]
        public class ShaderProperty
        {
            public string display_name;
            public string name;
            public string value;
        }

        [Serializable]
        public class ShaderVariant
        {
            public string name;
            public ShaderVariantPerformance performance;
            public ShaderProperty[] properties;
        }

        [Serializable]
        public class ShaderVariantPerformance
        {
            public string[] pipelines;
            public ShaderVariantCycles longest_path_cycles;
            public ShaderVariantCycles shortest_path_cycles;
            public ShaderVariantCycles total_cycles;
        }

        [Serializable]
        public class ShaderVariantCycles
        {
            public string[] bound_pipelines;
            public float[] cycle_count;
        }
    }
}