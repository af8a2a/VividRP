using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven
{
    internal enum MaterialValueType
    {
        Bool,
        Float,
        Float2,
        Float3,
        Float4,
        Texture2D,
    }

    internal enum MaterialValueOpcode
    {
        Constant,
        ExternalInput,
        Parameter,
        TextureResource,
        TextureSampleGrad,
        Ddx,
        Ddy,
        Add,
        Multiply,
        Lerp,
        Select,
    }

    internal enum MaterialExternalInput
    {
        UV0,
        GeometryNormalWS,
        GeometryTangentWS,
    }

    internal enum MaterialParameter
    {
        BaseColor,
        TopBaseColor,
        Roughness,
        TopRoughness,
        Metallic,
        TopMetallic,
        LayerWeight,
        AlphaClipThreshold,
    }

    internal enum MaterialTextureResource
    {
        BaseColor,
        TopBaseColor,
    }

    internal readonly struct MaterialValue : IEquatable<MaterialValue>
    {
        internal MaterialValue(MaterialValueIR owner, int index, MaterialValueType type)
        {
            Owner = owner;
            Index = index;
            Type = type;
        }

        internal MaterialValueIR Owner { get; }

        internal int Index { get; }

        internal MaterialValueType Type { get; }

        internal bool IsValid => Index >= 0;

        public bool Equals(MaterialValue other)
        {
            return ReferenceEquals(Owner, other.Owner)
                && Index == other.Index
                && Type == other.Type;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Owner != null ? Owner.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ Index;
                hashCode = (hashCode * 397) ^ (int) Type;
                return hashCode;
            }
        }

        public static bool operator ==(MaterialValue left, MaterialValue right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MaterialValue left, MaterialValue right)
        {
            return !left.Equals(right);
        }
    }

    internal readonly struct MaterialValueNode : IEquatable<MaterialValueNode>
    {
        internal MaterialValueNode(
            MaterialValueOpcode opcode,
            MaterialValueType type,
            int semantic,
            float4 constant,
            int operand0,
            int operand1,
            int operand2,
            int operand3)
        {
            Opcode = opcode;
            Type = type;
            Semantic = semantic;
            Constant = constant;
            Operand0 = operand0;
            Operand1 = operand1;
            Operand2 = operand2;
            Operand3 = operand3;
        }

        internal MaterialValueOpcode Opcode { get; }

        internal MaterialValueType Type { get; }

        internal int Semantic { get; }

        internal float4 Constant { get; }

        internal int Operand0 { get; }

        internal int Operand1 { get; }

        internal int Operand2 { get; }

        internal int Operand3 { get; }

        public bool Equals(MaterialValueNode other)
        {
            return Opcode == other.Opcode
                && Type == other.Type
                && Semantic == other.Semantic
                && Constant.Equals(other.Constant)
                && Operand0 == other.Operand0
                && Operand1 == other.Operand1
                && Operand2 == other.Operand2
                && Operand3 == other.Operand3;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialValueNode other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int) Opcode;
                hashCode = (hashCode * 397) ^ (int) Type;
                hashCode = (hashCode * 397) ^ Semantic;
                hashCode = (hashCode * 397) ^ Constant.GetHashCode();
                hashCode = (hashCode * 397) ^ Operand0;
                hashCode = (hashCode * 397) ^ Operand1;
                hashCode = (hashCode * 397) ^ Operand2;
                hashCode = (hashCode * 397) ^ Operand3;
                return hashCode;
            }
        }
    }

    internal sealed class MaterialValueIR
    {
        private const int InvalidOperand = -1;

        private readonly List<MaterialValueNode> m_Nodes = new();
        private readonly Dictionary<MaterialValueNode, int> m_ValueSet = new();
        private readonly IReadOnlyList<MaterialValueNode> m_NodesView;

        internal MaterialValueIR()
        {
            m_NodesView = m_Nodes.AsReadOnly();
        }

        internal IReadOnlyList<MaterialValueNode> Nodes => m_NodesView;

        internal int NodeCount => m_Nodes.Count;

        internal bool IsFrozen { get; private set; }

        internal MaterialValue ExternalInput(MaterialExternalInput input)
        {
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.ExternalInput,
                GetExternalInputType(input),
                (int) input,
                default,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand));
        }

        internal MaterialValue Parameter(MaterialParameter parameter)
        {
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Parameter,
                GetParameterType(parameter),
                (int) parameter,
                default,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand));
        }

        internal MaterialValue TextureResource(MaterialTextureResource resource)
        {
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.TextureResource,
                MaterialValueType.Texture2D,
                (int) resource,
                default,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand));
        }

        internal MaterialValue Constant(float value)
        {
            return EmitConstant(
                MaterialValueType.Float,
                new float4(value, 0.0f, 0.0f, 0.0f));
        }

        internal MaterialValue Constant(float2 value)
        {
            return EmitConstant(
                MaterialValueType.Float2,
                new float4(value, 0.0f, 0.0f));
        }

        internal MaterialValue Constant(float3 value)
        {
            return EmitConstant(MaterialValueType.Float3, new float4(value, 0.0f));
        }

        internal MaterialValue Constant(float4 value)
        {
            return EmitConstant(MaterialValueType.Float4, value);
        }

        internal MaterialValue Constant(bool value)
        {
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Constant,
                MaterialValueType.Bool,
                default,
                new float4(value ? 1.0f : 0.0f, 0.0f, 0.0f, 0.0f),
                InvalidOperand,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand));
        }

        internal MaterialValue TextureSampleGrad(
            MaterialValue texture,
            MaterialValue coordinates,
            MaterialValue ddx,
            MaterialValue ddy)
        {
            RequireType(texture, MaterialValueType.Texture2D, nameof(texture));
            RequireType(coordinates, MaterialValueType.Float2, nameof(coordinates));
            RequireType(ddx, MaterialValueType.Float2, nameof(ddx));
            RequireType(ddy, MaterialValueType.Float2, nameof(ddy));
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.TextureSampleGrad,
                MaterialValueType.Float4,
                default,
                default,
                texture.Index,
                coordinates.Index,
                ddx.Index,
                ddy.Index));
        }

        internal MaterialValue Ddx(MaterialValue value)
        {
            RequireNumeric(value, nameof(value));
            return EmitUnary(MaterialValueOpcode.Ddx, value);
        }

        internal MaterialValue Ddy(MaterialValue value)
        {
            RequireNumeric(value, nameof(value));
            return EmitUnary(MaterialValueOpcode.Ddy, value);
        }

        internal MaterialValue Add(MaterialValue left, MaterialValue right)
        {
            RequireMatchingNumericTypes(left, right);
            return EmitBinary(MaterialValueOpcode.Add, left, right);
        }

        internal MaterialValue Multiply(MaterialValue left, MaterialValue right)
        {
            RequireMatchingNumericTypes(left, right);
            return EmitBinary(MaterialValueOpcode.Multiply, left, right);
        }

        internal MaterialValue Lerp(
            MaterialValue left,
            MaterialValue right,
            MaterialValue weight)
        {
            RequireMatchingNumericTypes(left, right);
            RequireType(weight, MaterialValueType.Float, nameof(weight));
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Lerp,
                left.Type,
                default,
                default,
                left.Index,
                right.Index,
                weight.Index,
                InvalidOperand));
        }

        internal MaterialValue Select(
            MaterialValue condition,
            MaterialValue whenTrue,
            MaterialValue whenFalse)
        {
            RequireType(condition, MaterialValueType.Bool, nameof(condition));
            RequireSameType(whenTrue, whenFalse);
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Select,
                whenTrue.Type,
                default,
                default,
                condition.Index,
                whenTrue.Index,
                whenFalse.Index,
                InvalidOperand));
        }

        internal MaterialValueNode GetNode(MaterialValue value)
        {
            ValidateValue(value, nameof(value));
            MaterialValueNode node = m_Nodes[value.Index];
            if (node.Type != value.Type)
                throw new ArgumentException("The material value type does not match its IR node.", nameof(value));
            return node;
        }

        internal bool Owns(MaterialValue value)
        {
            return value.IsValid
                && ReferenceEquals(value.Owner, this)
                && value.Index < m_Nodes.Count
                && m_Nodes[value.Index].Type == value.Type;
        }

        internal void Freeze()
        {
            IsFrozen = true;
        }

        private MaterialValue EmitUnary(MaterialValueOpcode opcode, MaterialValue value)
        {
            return Emit(new MaterialValueNode(
                opcode,
                value.Type,
                default,
                default,
                value.Index,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand));
        }

        private MaterialValue EmitConstant(MaterialValueType type, float4 value)
        {
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Constant,
                type,
                default,
                value,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand));
        }

        private MaterialValue EmitBinary(
            MaterialValueOpcode opcode,
            MaterialValue left,
            MaterialValue right)
        {
            return Emit(new MaterialValueNode(
                opcode,
                left.Type,
                default,
                default,
                left.Index,
                right.Index,
                InvalidOperand,
                InvalidOperand));
        }

        private MaterialValue Emit(in MaterialValueNode node)
        {
            if (IsFrozen)
                throw new InvalidOperationException("Cannot modify a frozen material value IR.");

            if (m_ValueSet.TryGetValue(node, out int existingIndex))
                return new MaterialValue(this, existingIndex, node.Type);

            int index = m_Nodes.Count;
            m_Nodes.Add(node);
            m_ValueSet.Add(node, index);
            return new MaterialValue(this, index, node.Type);
        }

        private void RequireMatchingNumericTypes(MaterialValue left, MaterialValue right)
        {
            RequireNumeric(left, nameof(left));
            RequireNumeric(right, nameof(right));
            RequireSameType(left, right);
        }

        private void RequireSameType(MaterialValue left, MaterialValue right)
        {
            ValidateValue(left, nameof(left));
            ValidateValue(right, nameof(right));
            if (left.Type != right.Type)
            {
                throw new ArgumentException(
                    $"Material values must have matching types, got {left.Type} and {right.Type}.");
            }
        }

        private void RequireNumeric(MaterialValue value, string parameterName)
        {
            ValidateValue(value, parameterName);
            if (value.Type == MaterialValueType.Bool || value.Type == MaterialValueType.Texture2D)
            {
                throw new ArgumentException(
                    $"Material value must be numeric, got {value.Type}.",
                    parameterName);
            }
        }

        private void RequireType(
            MaterialValue value,
            MaterialValueType expectedType,
            string parameterName)
        {
            ValidateValue(value, parameterName);
            if (value.Type != expectedType)
            {
                throw new ArgumentException(
                    $"Material value must be {expectedType}, got {value.Type}.",
                    parameterName);
            }
        }

        private void ValidateValue(MaterialValue value, string parameterName)
        {
            if (!Owns(value))
                throw new ArgumentException("Material value is not owned by this IR.", parameterName);
        }

        private static MaterialValueType GetExternalInputType(MaterialExternalInput input)
        {
            switch (input)
            {
                case MaterialExternalInput.UV0:
                    return MaterialValueType.Float2;
                case MaterialExternalInput.GeometryNormalWS:
                    return MaterialValueType.Float3;
                case MaterialExternalInput.GeometryTangentWS:
                    return MaterialValueType.Float4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(input), input, null);
            }
        }

        private static MaterialValueType GetParameterType(MaterialParameter parameter)
        {
            switch (parameter)
            {
                case MaterialParameter.BaseColor:
                case MaterialParameter.TopBaseColor:
                    return MaterialValueType.Float4;
                case MaterialParameter.Roughness:
                case MaterialParameter.TopRoughness:
                case MaterialParameter.Metallic:
                case MaterialParameter.TopMetallic:
                case MaterialParameter.LayerWeight:
                case MaterialParameter.AlphaClipThreshold:
                    return MaterialValueType.Float;
                default:
                    throw new ArgumentOutOfRangeException(nameof(parameter), parameter, null);
            }
        }
    }
}
