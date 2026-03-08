// Copyright (c) Jason Ma

using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace LWGUI.PerformanceMonitor.ShaderCompiler.Mali
{
    public static class MaliocOutputParser
    {
        public static RuntimeMaliocShader Parse(string json)
        {
            if (json == null)
                return null;

            JsonMaliocOutput jsonModel;
            try
            {
                jsonModel = JsonUtility.FromJson<JsonMaliocOutput>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"LWGUI: Failed to parse malioc json: {e.Message}");
                return null;
            }

            if (jsonModel == null || jsonModel.shaders == null || jsonModel.shaders.Length != 1)
            {
                Debug.LogError("LWGUI: malioc json missing shaders or unexpected format.");
                return null;
            }

            var shader = jsonModel.shaders[0];

            // Check if this is an error response
            bool isErrorResponse = jsonModel.schema?.name == "error";
            if (isErrorResponse || (shader.errors != null && shader.errors.Length > 0))
            {
                return new RuntimeMaliocShader
                {
                    HasErrors = true,
                    Errors = shader.errors?.ToList() ?? new System.Collections.Generic.List<string>(),
                    Warnings = shader.warnings?.ToList() ?? new System.Collections.Generic.List<string>(),
                    Properties = new System.Collections.Generic.List<RuntimeMaliocShader.ShaderProperty>(),
                    Variants = new System.Collections.Generic.List<RuntimeMaliocShader.ShaderVariant>(),
                };
            }

            return new RuntimeMaliocShader
            {
                HasErrors = false,
                Errors = new System.Collections.Generic.List<string>(),
                Warnings = shader.warnings?.ToList() ?? new System.Collections.Generic.List<string>(),
                Properties = shader.properties.Select(ConvertProperty).ToList(),
                Variants = shader.variants.Select(variant =>
                    {
                        var pipelines = variant.performance.pipelines.Select(ParsePipelineType).ToList();
                        return new RuntimeMaliocShader.ShaderVariant
                        {
                            Name = variant.name,
                            Properties = variant.properties.Select(ConvertVariantProperty).ToList(),
                            Pipelines = pipelines,
                            LongestPathCycles =
                                ParseShaderPipelineCycles(variant.performance.longest_path_cycles),
                            ShortestPathCycles =
                                ParseShaderPipelineCycles(variant.performance.shortest_path_cycles),
                            TotalCycles = ParseShaderPipelineCycles(variant.performance.total_cycles),
                        };
                    }
                ).ToList(),
            };
        }

        private static RuntimeMaliocShader.ShaderProperty ConvertProperty(JsonMaliocOutput.ShaderProperty property) =>
            new() { Name = property.display_name, Value = new DynamicValue(ParseValue(property.value)) };

        private static RuntimeMaliocShader.ShaderProperty
            ConvertVariantProperty(JsonMaliocOutput.ShaderProperty property) =>
            new()
            {
                Name = property.display_name, Value = new DynamicValue(ParseValue(property.value)),
                ValueUnit = ParseValueUnit(property.name),
            };

        private static RuntimeMaliocShader.ShaderProperty.Unit ParseValueUnit(string name) =>
            name switch
            {
                "thread_occupancy" => RuntimeMaliocShader.ShaderProperty.Unit.Percent,
                "fp16_arithmetic" => RuntimeMaliocShader.ShaderProperty.Unit.Percent,
                var _ => RuntimeMaliocShader.ShaderProperty.Unit.None,
            };

        private static RuntimeMaliocShader.ShaderVariantPipelineType ParsePipelineType(string text) =>
            text switch
            {
                "arithmetic" => RuntimeMaliocShader.ShaderVariantPipelineType.Arithmetic,
                "load_store" => RuntimeMaliocShader.ShaderVariantPipelineType.LoadStore,
                "varying" => RuntimeMaliocShader.ShaderVariantPipelineType.Varying,
                "texture" => RuntimeMaliocShader.ShaderVariantPipelineType.Texture,
                "" => RuntimeMaliocShader.ShaderVariantPipelineType.Null,
                null => RuntimeMaliocShader.ShaderVariantPipelineType.Null,
                var _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Invalid pipeline type."),
            };

        private static RuntimeMaliocShader.ShaderPipelineCycles ParseShaderPipelineCycles(
            JsonMaliocOutput.ShaderVariantCycles cycles) =>
            new()
            {
                PipelineCycles = (cycles.cycle_count ?? Array.Empty<float>()).Select(f => f).ToList(),
                BoundPipeline = ParsePipelineType(cycles.bound_pipelines?.First()),
            };

        private static object ParseValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            var s = value.Trim();
            if (s.Equals("null", StringComparison.OrdinalIgnoreCase))
                return null;

            // Try int
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return i;

            // Try float
            if (float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var f))
                return f;

            // Try bool
            if (bool.TryParse(s, out var b))
                return b;

            // Fallback: return string
            return s;
        }
    }
}