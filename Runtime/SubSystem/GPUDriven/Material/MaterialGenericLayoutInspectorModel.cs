using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    internal readonly struct MaterialInstanceParameterField
    {
        internal MaterialInstanceParameterField(
            string symbol,
            MaterialValueType valueType,
            int wordOffset,
            int wordCount,
            bool isOverriddenLocally,
            bool isInheritedFromParent,
            Vector4 effectiveValue)
        {
            Symbol = symbol;
            ValueType = valueType;
            WordOffset = wordOffset;
            WordCount = wordCount;
            IsOverriddenLocally = isOverriddenLocally;
            IsInheritedFromParent = isInheritedFromParent;
            EffectiveValue = effectiveValue;
        }

        internal string Symbol { get; }
        internal MaterialValueType ValueType { get; }
        internal int WordOffset { get; }
        internal int WordCount { get; }
        internal bool IsOverriddenLocally { get; }
        internal bool IsInheritedFromParent { get; }
        internal Vector4 EffectiveValue { get; }
    }

    internal readonly struct MaterialInstanceTextureField
    {
        internal MaterialInstanceTextureField(
            string symbol,
            MaterialTextureSampleClass sampleClass,
            int slot,
            bool isOverriddenLocally,
            bool isInheritedFromParent,
            Texture2D effectiveTexture,
            Vector4 effectiveTilingOffset)
        {
            Symbol = symbol;
            SampleClass = sampleClass;
            Slot = slot;
            IsOverriddenLocally = isOverriddenLocally;
            IsInheritedFromParent = isInheritedFromParent;
            EffectiveTexture = effectiveTexture;
            EffectiveTilingOffset = effectiveTilingOffset;
        }

        internal string Symbol { get; }
        internal MaterialTextureSampleClass SampleClass { get; }
        internal int Slot { get; }
        internal bool IsOverriddenLocally { get; }
        internal bool IsInheritedFromParent { get; }
        internal Texture2D EffectiveTexture { get; }
        internal Vector4 EffectiveTilingOffset { get; }
    }

    /// <summary>
    /// Layout-driven view model for first-class Material Instance inspection (Phase 3.2).
    /// Driven directly by a compiled MaterialGenericLayout so Inspector controls stay
    /// 100% synchronized with declared graph parameters and texture bindings.
    /// </summary>
    internal sealed class MaterialGenericLayoutInspectorModel
    {
        internal delegate bool ParameterLookupDelegate(
            in MaterialParameterDeclaration declaration,
            out Vector4 value,
            out bool isInherited);

        internal delegate bool TextureLookupDelegate(
            in MaterialResourceDeclaration declaration,
            out GPUDrivenMaterialTextureOverride value,
            out bool isInherited);

        internal MaterialGenericLayoutInspectorModel(
            MaterialInstanceParameterField[] parameters,
            MaterialInstanceTextureField[] textures,
            int parameterByteStride)
        {
            Parameters = Array.AsReadOnly(parameters ?? Array.Empty<MaterialInstanceParameterField>());
            Textures = Array.AsReadOnly(textures ?? Array.Empty<MaterialInstanceTextureField>());
            ParameterByteStride = parameterByteStride;
        }

        internal IReadOnlyList<MaterialInstanceParameterField> Parameters { get; }

        internal IReadOnlyList<MaterialInstanceTextureField> Textures { get; }

        internal int ParameterByteStride { get; }

        internal static MaterialGenericLayoutInspectorModel Build(
            MaterialGenericLayout layout,
            GPUDrivenMaterialProxy proxy)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));

            ParameterLookupDelegate paramLookup = null;
            TextureLookupDelegate texLookup = null;
            if (proxy != null)
            {
                paramLookup = proxy.TryGetParameterOverrideWithSource;
                texLookup = proxy.TryGetTextureOverrideWithSource;
            }
            return Build(layout, paramLookup, texLookup);
        }

        internal static MaterialGenericLayoutInspectorModel Build(
            MaterialGenericLayout layout,
            ParameterLookupDelegate parameterLookup,
            TextureLookupDelegate textureLookup)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));

            var parameters = new MaterialInstanceParameterField[layout.ParameterBindings.Count];
            for (int i = 0; i < layout.ParameterBindings.Count; i++)
            {
                MaterialGenericParameterBinding binding = layout.ParameterBindings[i];
                MaterialParameterDeclaration decl = binding.Declaration;
                bool hasValue = false;
                bool isInherited = false;
                Vector4 value = GetDefaultParameterValue(decl);
                if (parameterLookup != null && parameterLookup(in decl, out Vector4 resolved, out bool inherited))
                {
                    hasValue = true;
                    isInherited = inherited;
                    value = resolved;
                }
                parameters[i] = new MaterialInstanceParameterField(
                    decl.Symbol,
                    decl.Type,
                    binding.WordOffset,
                    binding.WordCount,
                    isOverriddenLocally: hasValue && !isInherited,
                    isInheritedFromParent: hasValue && isInherited,
                    effectiveValue: value);
            }

            var textures = new MaterialInstanceTextureField[layout.ResourceBindings.Count];
            for (int i = 0; i < layout.ResourceBindings.Count; i++)
            {
                MaterialGenericResourceBinding binding = layout.ResourceBindings[i];
                MaterialResourceDeclaration decl = binding.Declaration;
                bool hasValue = false;
                bool isInherited = false;
                Texture2D tex = null;
                Vector4 tilingOffset = new Vector4(1.0f, 1.0f, 0.0f, 0.0f);
                if (textureLookup != null && textureLookup(in decl, out GPUDrivenMaterialTextureOverride resolved, out bool inherited))
                {
                    hasValue = true;
                    isInherited = inherited;
                    tex = resolved.Texture;
                    tilingOffset = resolved.TilingOffset;
                }
                textures[i] = new MaterialInstanceTextureField(
                    decl.Symbol,
                    decl.SampleClass,
                    binding.Slot,
                    isOverriddenLocally: hasValue && !isInherited,
                    isInheritedFromParent: hasValue && isInherited,
                    effectiveTexture: tex,
                    effectiveTilingOffset: tilingOffset);
            }

            return new MaterialGenericLayoutInspectorModel(
                parameters,
                textures,
                layout.ParameterStrideInWords * sizeof(uint));
        }

        internal static GPUDrivenMaterialParameterType ToOverrideParameterType(MaterialValueType valueType)
        {
            switch (valueType)
            {
                case MaterialValueType.Bool: return GPUDrivenMaterialParameterType.Bool;
                case MaterialValueType.Float: return GPUDrivenMaterialParameterType.Float;
                case MaterialValueType.Float2: return GPUDrivenMaterialParameterType.Float2;
                case MaterialValueType.Float3: return GPUDrivenMaterialParameterType.Float3;
                case MaterialValueType.Float4: return GPUDrivenMaterialParameterType.Float4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(valueType), $"Unsupported parameter value type '{valueType}'.");
            }
        }

        internal static bool IsLikelyColorSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return false;
            return symbol.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0
                || symbol.IndexOf("Tint", StringComparison.OrdinalIgnoreCase) >= 0
                || symbol.IndexOf("Albedo", StringComparison.OrdinalIgnoreCase) >= 0
                || symbol.IndexOf("Emission", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static Vector4 GetDefaultParameterValue(in MaterialParameterDeclaration decl)
        {
            if (IsLikelyColorSymbol(decl.Symbol))
                return new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
            return Vector4.zero;
        }
    }
}
