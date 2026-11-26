using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven
{
    internal enum MaterialGraphValueOpcode
    {
        Constant = 0,
        ExternalInput = 1,
        Parameter = 2,
        TextureResource = 3,
        TextureSample = 4,
        TextureSampleGrad = 5,
        Ddx = 6,
        Ddy = 7,
        Add = 8,
        Multiply = 9,
        Lerp = 10,
        Select = 11,
        Swizzle = 12,
        Compose = 13,
        Subtract = 14,
        Divide = 15,
        Min = 16,
        Max = 17,
        Saturate = 18,
        OneMinus = 19,
        Dot = 20,
        Normalize = 21,
        Compare = 22,
        Abs = 23,
        Sin = 24,
        Cos = 25,
        Floor = 26,
        Frac = 27,
        Sqrt = 28,
        Pow = 29,
        Clamp = 30,
        Smoothstep = 31,
        UnpackNormal = 32,
        ValueNoise2D = 33,
        GradientNoise2D = 34,
    }

    internal enum MaterialGraphClosureOpcode
    {
        Slab = 0,
        HorizontalMix = 1,
        VerticalLayer = 2,
        OpenPBROpaque = 3,
    }

    internal readonly struct MaterialGraphValue : IEquatable<MaterialGraphValue>
    {
        internal MaterialGraphValue(MaterialGraph owner, string nodeId)
        {
            Owner = owner;
            NodeId = nodeId;
        }

        internal MaterialGraph Owner { get; }

        internal string NodeId { get; }

        internal bool IsValid => Owner != null && !string.IsNullOrEmpty(NodeId);

        public bool Equals(MaterialGraphValue other)
        {
            return ReferenceEquals(Owner, other.Owner)
                && string.Equals(NodeId, other.NodeId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialGraphValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Owner != null ? Owner.GetHashCode() : 0;
                return (hashCode * 397)
                    ^ (NodeId != null ? StringComparer.Ordinal.GetHashCode(NodeId) : 0);
            }
        }
    }

    internal readonly struct MaterialGraphClosure : IEquatable<MaterialGraphClosure>
    {
        internal MaterialGraphClosure(MaterialGraph owner, string nodeId)
        {
            Owner = owner;
            NodeId = nodeId;
        }

        internal MaterialGraph Owner { get; }

        internal string NodeId { get; }

        internal bool IsValid => Owner != null && !string.IsNullOrEmpty(NodeId);

        public bool Equals(MaterialGraphClosure other)
        {
            return ReferenceEquals(Owner, other.Owner)
                && string.Equals(NodeId, other.NodeId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialGraphClosure other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Owner != null ? Owner.GetHashCode() : 0;
                return (hashCode * 397)
                    ^ (NodeId != null ? StringComparer.Ordinal.GetHashCode(NodeId) : 0);
            }
        }
    }

    internal sealed class MaterialGraphValueNode
    {
        internal MaterialGraphValueNode(
            string nodeId,
            MaterialGraphValueOpcode opcode,
            MaterialValueType constantType,
            float4 constant,
            int semantic,
            in MaterialParameterDeclaration parameterDeclaration,
            in MaterialResourceDeclaration resourceDeclaration,
            MaterialGraphValue[] operands)
        {
            NodeId = nodeId;
            Opcode = opcode;
            ConstantType = constantType;
            Constant = constant;
            Semantic = semantic;
            ParameterDeclaration = parameterDeclaration;
            ResourceDeclaration = resourceDeclaration;
            Operands = operands ?? Array.Empty<MaterialGraphValue>();
        }

        internal string NodeId { get; }

        internal MaterialGraphValueOpcode Opcode { get; }

        internal MaterialValueType ConstantType { get; }

        internal float4 Constant { get; }

        internal int Semantic { get; }

        internal MaterialParameterDeclaration ParameterDeclaration { get; }

        internal MaterialResourceDeclaration ResourceDeclaration { get; }

        internal IReadOnlyList<MaterialGraphValue> Operands { get; }
    }

    internal sealed class MaterialGraphClosureNode
    {
        internal MaterialGraphClosureNode(
            string nodeId,
            MaterialGraphClosureOpcode opcode,
            MaterialGraphValue[] values,
            MaterialGraphClosure[] closures,
            ClosureFeatureMask features)
        {
            NodeId = nodeId;
            Opcode = opcode;
            Values = values ?? Array.Empty<MaterialGraphValue>();
            Closures = closures ?? Array.Empty<MaterialGraphClosure>();
            Features = features;
        }

        internal string NodeId { get; }

        internal MaterialGraphClosureOpcode Opcode { get; }

        internal IReadOnlyList<MaterialGraphValue> Values { get; }

        internal IReadOnlyList<MaterialGraphClosure> Closures { get; }

        internal ClosureFeatureMask Features { get; }
    }

    internal sealed class MaterialGraphOutputNode
    {
        internal MaterialGraphOutputNode(
            string nodeId,
            MaterialGraphClosure surface,
            MaterialGraphValue coverage,
            MaterialGraphValue alphaClipThreshold,
            MaterialGraphValue emission,
            MaterialFeatureMask materialFeatures,
            MaterialShadingModelMask shadingModels)
        {
            NodeId = nodeId;
            Surface = surface;
            Coverage = coverage;
            AlphaClipThreshold = alphaClipThreshold;
            Emission = emission;
            MaterialFeatures = materialFeatures;
            ShadingModels = shadingModels;
        }

        internal string NodeId { get; }

        internal MaterialGraphClosure Surface { get; }

        internal MaterialGraphValue Coverage { get; }

        internal MaterialGraphValue AlphaClipThreshold { get; }

        internal MaterialGraphValue Emission { get; }

        internal MaterialFeatureMask MaterialFeatures { get; }

        internal MaterialShadingModelMask ShadingModels { get; }
    }

    internal static class MaterialGraphDefaults
    {
        internal const ClosureFeatureMask StandardSlabFeatures =
            ClosureFeatureMask.BaseColorTexture
            | ClosureFeatureMask.NormalTexture
            | ClosureFeatureMask.MaskTexture;

        internal const MaterialFeatureMask StandardMaterialFeatures =
            MaterialFeatureMask.AlphaClip;

        internal const MaterialShadingModelMask StandardShadingModels =
            MaterialShadingModelMask.StandardLit
            | MaterialShadingModelMask.Unlit;
    }

    // UI-independent source model. A future GraphToolkit asset adapts its stable
    // node GUIDs and typed ports to this model; IR node indices are never authored.
    internal sealed class MaterialGraph
    {
        private readonly Dictionary<string, MaterialGraphValueNode> m_ValueNodes =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, MaterialGraphClosureNode> m_ClosureNodes =
            new(StringComparer.Ordinal);
        private readonly List<MaterialGraphOutputNode> m_OutputNodes = new();
        private readonly HashSet<string> m_NodeIds = new(StringComparer.Ordinal);

        internal IReadOnlyDictionary<string, MaterialGraphValueNode> ValueNodes =>
            m_ValueNodes;

        internal IReadOnlyDictionary<string, MaterialGraphClosureNode> ClosureNodes =>
            m_ClosureNodes;

        internal IReadOnlyList<MaterialGraphOutputNode> OutputNodes => m_OutputNodes;

        internal MaterialGraphValue Value(string nodeId)
        {
            return new MaterialGraphValue(this, RequireNodeId(nodeId));
        }

        internal MaterialGraphClosure Closure(string nodeId)
        {
            return new MaterialGraphClosure(this, RequireNodeId(nodeId));
        }

        internal MaterialGraphValue Constant(string nodeId, bool value)
        {
            return AddConstant(
                nodeId,
                MaterialValueType.Bool,
                new float4(value ? 1.0f : 0.0f, 0.0f, 0.0f, 0.0f));
        }

        internal MaterialGraphValue Constant(string nodeId, float value)
        {
            return AddConstant(
                nodeId,
                MaterialValueType.Float,
                new float4(value, 0.0f, 0.0f, 0.0f));
        }

        internal MaterialGraphValue Constant(string nodeId, float2 value)
        {
            return AddConstant(
                nodeId,
                MaterialValueType.Float2,
                new float4(value, 0.0f, 0.0f));
        }

        internal MaterialGraphValue Constant(string nodeId, float3 value)
        {
            return AddConstant(
                nodeId,
                MaterialValueType.Float3,
                new float4(value, 0.0f));
        }

        internal MaterialGraphValue Constant(string nodeId, float4 value)
        {
            return AddConstant(nodeId, MaterialValueType.Float4, value);
        }

        internal MaterialGraphValue ExternalInput(
            string nodeId,
            MaterialExternalInput input)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.ExternalInput,
                semantic: (int) input);
        }

        internal MaterialGraphValue Parameter(
            string nodeId,
            MaterialParameter parameter)
        {
            MaterialParameterDeclaration declaration =
                MaterialNativeTemplateDeclarationAdapter.GetParameter(parameter);
            return Parameter(nodeId, declaration);
        }

        internal MaterialGraphValue Parameter(
            string nodeId,
            string symbol,
            MaterialValueType type)
        {
            return Parameter(
                nodeId,
                new MaterialParameterDeclaration(symbol, type));
        }

        internal MaterialGraphValue Parameter(
            string nodeId,
            in MaterialParameterDeclaration declaration)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.Parameter,
                parameterDeclaration: declaration);
        }

        internal MaterialGraphValue TextureResource(
            string nodeId,
            MaterialTextureResource resource)
        {
            MaterialResourceDeclaration declaration =
                MaterialNativeTemplateDeclarationAdapter.GetTexture(resource);
            return TextureResource(nodeId, declaration);
        }

        internal MaterialGraphValue TextureResource(
            string nodeId,
            string symbol,
            MaterialValueType type,
            MaterialTextureSampleClass sampleClass = MaterialTextureSampleClass.Raw)
        {
            return TextureResource(
                nodeId,
                new MaterialResourceDeclaration(symbol, type, sampleClass));
        }

        internal MaterialGraphValue TextureResource(
            string nodeId,
            in MaterialResourceDeclaration declaration)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.TextureResource,
                resourceDeclaration: declaration);
        }

        internal MaterialGraphValue TextureSample(
            string nodeId,
            MaterialGraphValue texture,
            MaterialGraphValue uv)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.TextureSample,
                operands: new[] { texture, uv });
        }

        internal MaterialGraphValue TextureSampleGrad(
            string nodeId,
            MaterialGraphValue texture,
            MaterialGraphValue uv,
            MaterialGraphValue ddx,
            MaterialGraphValue ddy)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.TextureSampleGrad,
                operands: new[] { texture, uv, ddx, ddy });
        }

        internal MaterialGraphValue Ddx(string nodeId, MaterialGraphValue value)
        {
            return AddUnary(nodeId, MaterialGraphValueOpcode.Ddx, value);
        }

        internal MaterialGraphValue Ddy(string nodeId, MaterialGraphValue value)
        {
            return AddUnary(nodeId, MaterialGraphValueOpcode.Ddy, value);
        }

        internal MaterialGraphValue Add(
            string nodeId,
            MaterialGraphValue left,
            MaterialGraphValue right)
        {
            return AddBinary(nodeId, MaterialGraphValueOpcode.Add, left, right);
        }

        internal MaterialGraphValue Multiply(
            string nodeId,
            MaterialGraphValue left,
            MaterialGraphValue right)
        {
            return AddBinary(nodeId, MaterialGraphValueOpcode.Multiply, left, right);
        }

        internal MaterialGraphValue Subtract(
            string nodeId,
            MaterialGraphValue left,
            MaterialGraphValue right)
        {
            return AddBinary(nodeId, MaterialGraphValueOpcode.Subtract, left, right);
        }

        internal MaterialGraphValue Divide(
            string nodeId,
            MaterialGraphValue left,
            MaterialGraphValue right)
        {
            return AddBinary(nodeId, MaterialGraphValueOpcode.Divide, left, right);
        }

        internal MaterialGraphValue Min(
            string nodeId,
            MaterialGraphValue left,
            MaterialGraphValue right)
        {
            return AddBinary(nodeId, MaterialGraphValueOpcode.Min, left, right);
        }

        internal MaterialGraphValue Max(
            string nodeId,
            MaterialGraphValue left,
            MaterialGraphValue right)
        {
            return AddBinary(nodeId, MaterialGraphValueOpcode.Max, left, right);
        }

        internal MaterialGraphValue Dot(
            string nodeId,
            MaterialGraphValue left,
            MaterialGraphValue right)
        {
            return AddBinary(nodeId, MaterialGraphValueOpcode.Dot, left, right);
        }

        internal MaterialGraphValue Saturate(
            string nodeId,
            MaterialGraphValue value)
        {
            return AddUnary(nodeId, MaterialGraphValueOpcode.Saturate, value);
        }

        internal MaterialGraphValue OneMinus(
            string nodeId,
            MaterialGraphValue value)
        {
            return AddUnary(nodeId, MaterialGraphValueOpcode.OneMinus, value);
        }

        internal MaterialGraphValue Normalize(
            string nodeId,
            MaterialGraphValue value)
        {
            return AddUnary(nodeId, MaterialGraphValueOpcode.Normalize, value);
        }

        internal MaterialGraphValue Abs(string nodeId, MaterialGraphValue value)
            => AddUnary(nodeId, MaterialGraphValueOpcode.Abs, value);

        internal MaterialGraphValue Sin(string nodeId, MaterialGraphValue value)
            => AddUnary(nodeId, MaterialGraphValueOpcode.Sin, value);

        internal MaterialGraphValue Cos(string nodeId, MaterialGraphValue value)
            => AddUnary(nodeId, MaterialGraphValueOpcode.Cos, value);

        internal MaterialGraphValue Floor(string nodeId, MaterialGraphValue value)
            => AddUnary(nodeId, MaterialGraphValueOpcode.Floor, value);

        internal MaterialGraphValue Frac(string nodeId, MaterialGraphValue value)
            => AddUnary(nodeId, MaterialGraphValueOpcode.Frac, value);

        internal MaterialGraphValue Sqrt(string nodeId, MaterialGraphValue value)
            => AddUnary(nodeId, MaterialGraphValueOpcode.Sqrt, value);

        internal MaterialGraphValue Pow(string nodeId, MaterialGraphValue left, MaterialGraphValue right)
            => AddBinary(nodeId, MaterialGraphValueOpcode.Pow, left, right);

        internal MaterialGraphValue Clamp(
            string nodeId,
            MaterialGraphValue value,
            MaterialGraphValue minValue,
            MaterialGraphValue maxValue)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.Clamp,
                operands: new[] { value, minValue, maxValue });
        }

        internal MaterialGraphValue Smoothstep(
            string nodeId,
            MaterialGraphValue edge0,
            MaterialGraphValue edge1,
            MaterialGraphValue value)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.Smoothstep,
                operands: new[] { edge0, edge1, value });
        }

        internal MaterialGraphValue UnpackNormal(
            string nodeId,
            MaterialGraphValue packedNormal,
            MaterialGraphValue strength)
        {
            return AddBinary(nodeId, MaterialGraphValueOpcode.UnpackNormal, packedNormal, strength);
        }

        internal MaterialGraphValue ValueNoise2D(
            string nodeId,
            MaterialGraphValue uv,
            MaterialGraphValue scale)
        {
            return AddBinary(nodeId, MaterialGraphValueOpcode.ValueNoise2D, uv, scale);
        }

        internal MaterialGraphValue GradientNoise2D(
            string nodeId,
            MaterialGraphValue uv,
            MaterialGraphValue scale)
        {
            return AddBinary(nodeId, MaterialGraphValueOpcode.GradientNoise2D, uv, scale);
        }

        internal MaterialGraphValue Remap(
            string nodeId,
            MaterialGraphValue value,
            MaterialGraphValue inMin,
            MaterialGraphValue inMax,
            MaterialGraphValue outMin,
            MaterialGraphValue outMax)
        {
            MaterialGraphValue numerator = Subtract(nodeId + ".Num", value, inMin);
            MaterialGraphValue denominator = Subtract(nodeId + ".Den", inMax, inMin);
            MaterialGraphValue t = Saturate(nodeId + ".T", Divide(nodeId + ".Div", numerator, denominator));
            return Lerp(nodeId, outMin, outMax, t);
        }

        /// <summary>
        /// Phase 3.3 compile-time Subgraph flattening: recursively inlines a value subgraph
        /// rooted at <paramref name="outputValueNodeId"/> into this MaterialGraph, substituting
        /// any named Parameter nodes matching <paramref name="parameterBindings"/> with caller values
        /// and detecting cyclic references cleanly.
        /// </summary>
        internal MaterialGraphValue InlineValueSubgraph(
            string instanceNodeId,
            MaterialGraph subgraph,
            string outputValueNodeId,
            IReadOnlyDictionary<string, MaterialGraphValue> parameterBindings = null)
        {
            if (string.IsNullOrEmpty(instanceNodeId))
                throw new ArgumentException("Subgraph instance node ID is required.", nameof(instanceNodeId));
            if (subgraph == null)
                throw new ArgumentNullException(nameof(subgraph));
            if (ReferenceEquals(this, subgraph))
                throw new InvalidOperationException($"Subgraph '{instanceNodeId}' cannot recursively reference its own host graph.");
            if (string.IsNullOrEmpty(outputValueNodeId)
                || !subgraph.m_ValueNodes.TryGetValue(outputValueNodeId, out _))
            {
                throw new ArgumentException($"Subgraph output value node '{outputValueNodeId}' was not found.", nameof(outputValueNodeId));
            }

            var memo = new Dictionary<string, MaterialGraphValue>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            return InlineNode(outputValueNodeId, isRoot: true);

            MaterialGraphValue InlineNode(string subNodeId, bool isRoot)
            {
                if (memo.TryGetValue(subNodeId, out MaterialGraphValue cached))
                    return cached;
                if (!visiting.Add(subNodeId))
                    throw new InvalidOperationException($"Subgraph '{instanceNodeId}' contains a cycle at node '{subNodeId}'.");
                if (!subgraph.m_ValueNodes.TryGetValue(subNodeId, out MaterialGraphValueNode subNode))
                    throw new ArgumentException($"Subgraph node '{subNodeId}' is missing.", nameof(subNodeId));

                if (subNode.Opcode == MaterialGraphValueOpcode.Parameter
                    && parameterBindings != null
                    && !string.IsNullOrEmpty(subNode.ParameterDeclaration.Symbol)
                    && parameterBindings.TryGetValue(subNode.ParameterDeclaration.Symbol, out MaterialGraphValue bound))
                {
                    visiting.Remove(subNodeId);
                    memo[subNodeId] = bound;
                    return bound;
                }

                var mappedOperands = new MaterialGraphValue[subNode.Operands.Count];
                for (int i = 0; i < subNode.Operands.Count; i++)
                    mappedOperands[i] = InlineNode(subNode.Operands[i].NodeId, isRoot: false);

                string targetId = isRoot ? instanceNodeId : $"{instanceNodeId}::{subNodeId}";
                MaterialGraphValue emitted = AddValue(
                    targetId,
                    subNode.Opcode,
                    subNode.ConstantType,
                    subNode.Constant,
                    subNode.Semantic,
                    subNode.ParameterDeclaration,
                    subNode.ResourceDeclaration,
                    mappedOperands);
                visiting.Remove(subNodeId);
                memo[subNodeId] = emitted;
                return emitted;
            }
        }

        internal MaterialGraphValue Lerp(
            string nodeId,
            MaterialGraphValue left,
            MaterialGraphValue right,
            MaterialGraphValue weight)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.Lerp,
                operands: new[] { left, right, weight });
        }

        internal MaterialGraphValue Select(
            string nodeId,
            MaterialGraphValue condition,
            MaterialGraphValue whenTrue,
            MaterialGraphValue whenFalse)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.Select,
                operands: new[] { condition, whenTrue, whenFalse });
        }

        internal MaterialGraphValue Compare(
            string nodeId,
            MaterialGraphValue left,
            MaterialGraphValue right,
            MaterialComparison comparison)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.Compare,
                semantic: (int) comparison,
                operands: new[] { left, right });
        }

        internal MaterialGraphValue Swizzle(
            string nodeId,
            MaterialGraphValue value,
            in MaterialSwizzleMask mask)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.Swizzle,
                semantic: mask.PackedValue,
                operands: new[] { value });
        }

        internal MaterialGraphValue Compose(
            string nodeId,
            params MaterialGraphValue[] components)
        {
            if (components == null)
                throw new ArgumentNullException(nameof(components));
            if (components.Length < 2 || components.Length > 4)
                throw new ArgumentOutOfRangeException(nameof(components));
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.Compose,
                operands: (MaterialGraphValue[]) components.Clone());
        }

        internal MaterialGraphClosure Slab(
            string nodeId,
            MaterialGraphValue baseColor,
            MaterialGraphValue roughness,
            MaterialGraphValue metallic,
            MaterialGraphValue normal,
            MaterialGraphValue tangent,
            ClosureFeatureMask features =
                MaterialGraphDefaults.StandardSlabFeatures)
        {
            return AddClosure(
                nodeId,
                MaterialGraphClosureOpcode.Slab,
                new[] { baseColor, roughness, metallic, normal, tangent },
                null,
                features);
        }

        internal MaterialGraphClosure OpenPBROpaque(
            string nodeId,
            MaterialGraphValue baseWeight,
            MaterialGraphValue baseColor,
            MaterialGraphValue baseDiffuseRoughness,
            MaterialGraphValue baseMetalness,
            MaterialGraphValue specularWeight,
            MaterialGraphValue specularColor,
            MaterialGraphValue specularRoughness,
            MaterialGraphValue specularIor,
            MaterialGraphValue normalWS,
            MaterialGraphValue emissionLuminance,
            MaterialGraphValue emissionColor)
        {
            return AddClosure(
                nodeId,
                MaterialGraphClosureOpcode.OpenPBROpaque,
                new[]
                {
                    baseWeight, baseColor, baseDiffuseRoughness, baseMetalness,
                    specularWeight, specularColor, specularRoughness, specularIor,
                    normalWS, emissionLuminance, emissionColor,
                },
                null,
                ClosureFeatureMask.None);
        }

        internal MaterialGraphClosure OpenPBROpaqueDefault(
            string nodeId,
            MaterialGraphValue normalWS)
        {
            return OpenPBROpaque(
                nodeId,
                Constant(nodeId + ".BaseWeight", OpenPBROpaqueContract.DefaultBaseWeight),
                Constant(nodeId + ".BaseColor", new float3(OpenPBROpaqueContract.DefaultBaseColor)),
                Constant(nodeId + ".BaseDiffuseRoughness", OpenPBROpaqueContract.DefaultBaseDiffuseRoughness),
                Constant(nodeId + ".BaseMetalness", OpenPBROpaqueContract.DefaultBaseMetalness),
                Constant(nodeId + ".SpecularWeight", OpenPBROpaqueContract.DefaultSpecularWeight),
                Constant(nodeId + ".SpecularColor", new float3(OpenPBROpaqueContract.DefaultSpecularColor)),
                Constant(nodeId + ".SpecularRoughness", OpenPBROpaqueContract.DefaultSpecularRoughness),
                Constant(nodeId + ".SpecularIor", OpenPBROpaqueContract.DefaultSpecularIor),
                normalWS,
                Constant(nodeId + ".EmissionLuminance", OpenPBROpaqueContract.DefaultEmissionLuminance),
                Constant(nodeId + ".EmissionColor", new float3(OpenPBROpaqueContract.DefaultEmissionColor)));
        }

        internal MaterialGraphClosure HorizontalMix(
            string nodeId,
            MaterialGraphClosure background,
            MaterialGraphClosure foreground,
            MaterialGraphValue weight)
        {
            return AddClosure(
                nodeId,
                MaterialGraphClosureOpcode.HorizontalMix,
                new[] { weight },
                new[] { background, foreground },
                ClosureFeatureMask.None);
        }

        internal MaterialGraphClosure VerticalLayer(
            string nodeId,
            MaterialGraphClosure bottom,
            MaterialGraphClosure top,
            MaterialGraphValue weight)
        {
            return AddClosure(
                nodeId,
                MaterialGraphClosureOpcode.VerticalLayer,
                new[] { weight },
                new[] { bottom, top },
                ClosureFeatureMask.None);
        }

        internal void Output(
            string nodeId,
            MaterialGraphClosure surface,
            MaterialGraphValue coverage,
            MaterialGraphValue alphaClipThreshold,
            MaterialGraphValue emission,
            MaterialFeatureMask materialFeatures =
                MaterialGraphDefaults.StandardMaterialFeatures,
            MaterialShadingModelMask shadingModels =
                MaterialGraphDefaults.StandardShadingModels)
        {
            string validatedId = ReserveNodeId(nodeId);
            m_OutputNodes.Add(new MaterialGraphOutputNode(
                validatedId,
                surface,
                coverage,
                alphaClipThreshold,
                emission,
                materialFeatures,
                shadingModels));
        }

        private MaterialGraphValue AddConstant(
            string nodeId,
            MaterialValueType type,
            float4 value)
        {
            return AddValue(
                nodeId,
                MaterialGraphValueOpcode.Constant,
                type,
                value);
        }

        private MaterialGraphValue AddUnary(
            string nodeId,
            MaterialGraphValueOpcode opcode,
            MaterialGraphValue value)
        {
            return AddValue(nodeId, opcode, operands: new[] { value });
        }

        private MaterialGraphValue AddBinary(
            string nodeId,
            MaterialGraphValueOpcode opcode,
            MaterialGraphValue left,
            MaterialGraphValue right)
        {
            return AddValue(nodeId, opcode, operands: new[] { left, right });
        }

        private MaterialGraphValue AddValue(
            string nodeId,
            MaterialGraphValueOpcode opcode,
            MaterialValueType constantType = default,
            float4 constant = default,
            int semantic = default,
            MaterialParameterDeclaration parameterDeclaration = default,
            MaterialResourceDeclaration resourceDeclaration = default,
            MaterialGraphValue[] operands = null)
        {
            string validatedId = ReserveNodeId(nodeId);
            m_ValueNodes.Add(
                validatedId,
                new MaterialGraphValueNode(
                    validatedId,
                    opcode,
                    constantType,
                    constant,
                    semantic,
                    parameterDeclaration,
                    resourceDeclaration,
                    operands));
            return new MaterialGraphValue(this, validatedId);
        }

        private MaterialGraphClosure AddClosure(
            string nodeId,
            MaterialGraphClosureOpcode opcode,
            MaterialGraphValue[] values,
            MaterialGraphClosure[] closures,
            ClosureFeatureMask features)
        {
            string validatedId = ReserveNodeId(nodeId);
            m_ClosureNodes.Add(
                validatedId,
                new MaterialGraphClosureNode(
                    validatedId,
                    opcode,
                    values,
                    closures,
                    features));
            return new MaterialGraphClosure(this, validatedId);
        }

        private string ReserveNodeId(string nodeId)
        {
            string validatedId = RequireNodeId(nodeId);
            if (!m_NodeIds.Add(validatedId))
            {
                throw new ArgumentException(
                    $"Material graph node ID '{validatedId}' is already in use.",
                    nameof(nodeId));
            }
            return validatedId;
        }

        private static string RequireNodeId(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                throw new ArgumentException("Material graph nodes require a stable ID.", nameof(nodeId));
            return nodeId;
        }
    }
}
