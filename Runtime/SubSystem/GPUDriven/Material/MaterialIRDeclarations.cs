using System;

namespace VividRP.Runtime.GPUDriven
{
    internal enum MaterialTextureSampleClass
    {
        Raw = 0,
        Color = 1,
        Normal = 2,
        Mask = 3,
    }

    internal readonly struct MaterialParameterDeclaration :
        IEquatable<MaterialParameterDeclaration>
    {
        internal MaterialParameterDeclaration(string symbol, MaterialValueType type)
        {
            Symbol = symbol;
            Type = type;
        }

        internal string Symbol { get; }

        internal MaterialValueType Type { get; }

        public bool Equals(MaterialParameterDeclaration other)
        {
            return Type == other.Type
                && string.Equals(Symbol, other.Symbol, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialParameterDeclaration other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Symbol != null
                    ? StringComparer.Ordinal.GetHashCode(Symbol)
                    : 0;
                return (hashCode * 397) ^ (int) Type;
            }
        }

        public static bool operator ==(
            MaterialParameterDeclaration left,
            MaterialParameterDeclaration right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            MaterialParameterDeclaration left,
            MaterialParameterDeclaration right)
        {
            return !left.Equals(right);
        }
    }

    internal readonly struct MaterialResourceDeclaration :
        IEquatable<MaterialResourceDeclaration>
    {
        internal MaterialResourceDeclaration(string symbol, MaterialValueType type)
            : this(symbol, type, MaterialTextureSampleClass.Raw)
        {
        }

        internal MaterialResourceDeclaration(
            string symbol,
            MaterialValueType type,
            MaterialTextureSampleClass sampleClass)
        {
            Symbol = symbol;
            Type = type;
            SampleClass = sampleClass;
        }

        internal string Symbol { get; }

        internal MaterialValueType Type { get; }

        internal MaterialTextureSampleClass SampleClass { get; }

        public bool Equals(MaterialResourceDeclaration other)
        {
            return Type == other.Type
                && SampleClass == other.SampleClass
                && string.Equals(Symbol, other.Symbol, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialResourceDeclaration other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Symbol != null
                    ? StringComparer.Ordinal.GetHashCode(Symbol)
                    : 0;
                hashCode = (hashCode * 397) ^ (int) Type;
                return (hashCode * 397) ^ (int) SampleClass;
            }
        }

        public static bool operator ==(
            MaterialResourceDeclaration left,
            MaterialResourceDeclaration right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            MaterialResourceDeclaration left,
            MaterialResourceDeclaration right)
        {
            return !left.Equals(right);
        }
    }

    // Compatibility boundary for the authored StandardLit/DualSlab fields. Generic
    // program identity and Frozen runtime descriptors use declarations directly.
    internal static class MaterialNativeTemplateDeclarationAdapter
    {
        private static readonly MaterialParameterDeclaration s_BaseColorParameter =
            new("BaseColor", MaterialValueType.Float4);
        private static readonly MaterialParameterDeclaration s_TopBaseColorParameter =
            new("TopBaseColor", MaterialValueType.Float4);
        private static readonly MaterialParameterDeclaration s_RoughnessParameter =
            new("Roughness", MaterialValueType.Float);
        private static readonly MaterialParameterDeclaration s_TopRoughnessParameter =
            new("TopRoughness", MaterialValueType.Float);
        private static readonly MaterialParameterDeclaration s_MetallicParameter =
            new("Metallic", MaterialValueType.Float);
        private static readonly MaterialParameterDeclaration s_TopMetallicParameter =
            new("TopMetallic", MaterialValueType.Float);
        private static readonly MaterialParameterDeclaration s_LayerWeightParameter =
            new("LayerWeight", MaterialValueType.Float);
        private static readonly MaterialParameterDeclaration s_AlphaClipThresholdParameter =
            new("AlphaClipThreshold", MaterialValueType.Float);
        private static readonly MaterialParameterDeclaration s_EmissionParameter =
            new("Emission", MaterialValueType.Float3);

        private static readonly MaterialResourceDeclaration s_BaseColorTexture =
            new(
                "BaseColor",
                MaterialValueType.Texture2D,
                MaterialTextureSampleClass.Color);
        private static readonly MaterialResourceDeclaration s_TopBaseColorTexture =
            new(
                "TopBaseColor",
                MaterialValueType.Texture2D,
                MaterialTextureSampleClass.Color);
        private static readonly MaterialResourceDeclaration s_BaseNormalTexture =
            new(
                "BaseNormal",
                MaterialValueType.Texture2D,
                MaterialTextureSampleClass.Normal);
        private static readonly MaterialResourceDeclaration s_BaseMaskTexture =
            new(
                "BaseMask",
                MaterialValueType.Texture2D,
                MaterialTextureSampleClass.Mask);
        private static readonly MaterialResourceDeclaration s_TopNormalTexture =
            new(
                "TopNormal",
                MaterialValueType.Texture2D,
                MaterialTextureSampleClass.Normal);
        private static readonly MaterialResourceDeclaration s_TopMaskTexture =
            new(
                "TopMask",
                MaterialValueType.Texture2D,
                MaterialTextureSampleClass.Mask);

        internal static MaterialParameterDeclaration GetParameter(
            MaterialParameter parameter)
        {
            switch (parameter)
            {
                case MaterialParameter.BaseColor:
                    return s_BaseColorParameter;
                case MaterialParameter.TopBaseColor:
                    return s_TopBaseColorParameter;
                case MaterialParameter.Roughness:
                    return s_RoughnessParameter;
                case MaterialParameter.TopRoughness:
                    return s_TopRoughnessParameter;
                case MaterialParameter.Metallic:
                    return s_MetallicParameter;
                case MaterialParameter.TopMetallic:
                    return s_TopMetallicParameter;
                case MaterialParameter.LayerWeight:
                    return s_LayerWeightParameter;
                case MaterialParameter.AlphaClipThreshold:
                    return s_AlphaClipThresholdParameter;
                case MaterialParameter.Emission:
                    return s_EmissionParameter;
                default:
                    throw new ArgumentOutOfRangeException(nameof(parameter), parameter, null);
            }
        }

        internal static bool TryGetParameter(
            in MaterialParameterDeclaration declaration,
            out MaterialParameter parameter)
        {
            if (declaration == s_BaseColorParameter)
                parameter = MaterialParameter.BaseColor;
            else if (declaration == s_TopBaseColorParameter)
                parameter = MaterialParameter.TopBaseColor;
            else if (declaration == s_RoughnessParameter)
                parameter = MaterialParameter.Roughness;
            else if (declaration == s_TopRoughnessParameter)
                parameter = MaterialParameter.TopRoughness;
            else if (declaration == s_MetallicParameter)
                parameter = MaterialParameter.Metallic;
            else if (declaration == s_TopMetallicParameter)
                parameter = MaterialParameter.TopMetallic;
            else if (declaration == s_LayerWeightParameter)
                parameter = MaterialParameter.LayerWeight;
            else if (declaration == s_AlphaClipThresholdParameter)
                parameter = MaterialParameter.AlphaClipThreshold;
            else if (declaration == s_EmissionParameter)
                parameter = MaterialParameter.Emission;
            else
            {
                parameter = default;
                return false;
            }

            return true;
        }

        internal static MaterialResourceDeclaration GetTexture(
            MaterialTextureResource resource)
        {
            switch (resource)
            {
                case MaterialTextureResource.BaseColor:
                    return s_BaseColorTexture;
                case MaterialTextureResource.TopBaseColor:
                    return s_TopBaseColorTexture;
                case MaterialTextureResource.BaseNormal:
                    return s_BaseNormalTexture;
                case MaterialTextureResource.BaseMask:
                    return s_BaseMaskTexture;
                case MaterialTextureResource.TopNormal:
                    return s_TopNormalTexture;
                case MaterialTextureResource.TopMask:
                    return s_TopMaskTexture;
                default:
                    throw new ArgumentOutOfRangeException(nameof(resource), resource, null);
            }
        }

        internal static bool TryGetTexture(
            in MaterialResourceDeclaration declaration,
            out MaterialTextureResource resource)
        {
            if (declaration == s_BaseColorTexture)
                resource = MaterialTextureResource.BaseColor;
            else if (declaration == s_TopBaseColorTexture)
                resource = MaterialTextureResource.TopBaseColor;
            else if (declaration == s_BaseNormalTexture)
                resource = MaterialTextureResource.BaseNormal;
            else if (declaration == s_BaseMaskTexture)
                resource = MaterialTextureResource.BaseMask;
            else if (declaration == s_TopNormalTexture)
                resource = MaterialTextureResource.TopNormal;
            else if (declaration == s_TopMaskTexture)
                resource = MaterialTextureResource.TopMask;
            else
            {
                resource = default;
                return false;
            }

            return true;
        }

        internal static bool IsTopSlabTexture(MaterialTextureResource resource)
        {
            return resource == MaterialTextureResource.TopBaseColor
                || resource == MaterialTextureResource.TopNormal
                || resource == MaterialTextureResource.TopMask;
        }
    }
}
