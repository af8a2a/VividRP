using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven
{
    internal enum MaterialValueType
    {
        Bool = 0,
        Float = 1,
        Float2 = 2,
        Float3 = 3,
        Float4 = 4,
        Texture2D = 5,
    }

    internal enum MaterialValueOpcode
    {
        Constant = 0,
        ExternalInput = 1,
        Parameter = 2,
        TextureResource = 3,
        TextureSampleGrad = 4,
        Ddx = 5,
        Ddy = 6,
        Add = 7,
        Multiply = 8,
        Lerp = 9,
        Select = 10,
        Swizzle = 11,
        Compose = 12,
        Subtract = 13,
        Divide = 14,
        Min = 15,
        Max = 16,
        Saturate = 17,
        OneMinus = 18,
        Dot = 19,
        Normalize = 20,
        Compare = 21,
    }

    internal enum MaterialExternalInput
    {
        UV0 = 0,
        GeometryNormalWS = 1,
        GeometryTangentWS = 2,
    }

    internal enum MaterialParameter
    {
        BaseColor = 0,
        TopBaseColor = 1,
        Roughness = 2,
        TopRoughness = 3,
        Metallic = 4,
        TopMetallic = 5,
        LayerWeight = 6,
        AlphaClipThreshold = 7,
        Emission = 8,
    }

    internal enum MaterialTextureResource
    {
        BaseColor = 0,
        TopBaseColor = 1,
        BaseNormal = 2,
        BaseMask = 3,
        TopNormal = 4,
        TopMask = 5,
    }

    internal enum MaterialComparison
    {
        Equal = 0,
        NotEqual = 1,
        Less = 2,
        LessOrEqual = 3,
        Greater = 4,
        GreaterOrEqual = 5,
    }

    internal readonly struct MaterialSwizzleMask : IEquatable<MaterialSwizzleMask>
    {
        private const int ComponentCountMask = 0x7;
        private const int FirstComponentShift = 3;
        private const int ComponentBitCount = 2;
        private const int EncodedBitCount = FirstComponentShift + ComponentBitCount * 4;
        private const int EncodedMask = (1 << EncodedBitCount) - 1;

        private MaterialSwizzleMask(int packedValue)
        {
            PackedValue = packedValue;
        }

        internal static MaterialSwizzleMask X => Create(0);

        internal static MaterialSwizzleMask Y => Create(1);

        internal static MaterialSwizzleMask Z => Create(2);

        internal static MaterialSwizzleMask W => Create(3);

        internal static MaterialSwizzleMask XYZ => Create(0, 1, 2);

        internal int PackedValue { get; }

        internal int ComponentCount => PackedValue & ComponentCountMask;

        internal MaterialValueType ResultType
        {
            get
            {
                switch (ComponentCount)
                {
                    case 1:
                        return MaterialValueType.Float;
                    case 2:
                        return MaterialValueType.Float2;
                    case 3:
                        return MaterialValueType.Float3;
                    case 4:
                        return MaterialValueType.Float4;
                    default:
                        throw new InvalidOperationException("Invalid material swizzle mask.");
                }
            }
        }

        internal int GetComponent(int componentIndex)
        {
            if ((uint) componentIndex >= (uint) ComponentCount)
                throw new ArgumentOutOfRangeException(nameof(componentIndex));
            return (PackedValue >> (FirstComponentShift + componentIndex * ComponentBitCount)) & 0x3;
        }

        internal static MaterialSwizzleMask Create(params int[] components)
        {
            if (components == null)
                throw new ArgumentNullException(nameof(components));
            if (components.Length < 1 || components.Length > 4)
                throw new ArgumentOutOfRangeException(nameof(components));

            int packedValue = components.Length;
            for (int i = 0; i < components.Length; i++)
            {
                if ((uint) components[i] > 3u)
                    throw new ArgumentOutOfRangeException(nameof(components));
                packedValue |= components[i]
                    << (FirstComponentShift + i * ComponentBitCount);
            }
            return new MaterialSwizzleMask(packedValue);
        }

        internal static bool TryDecode(
            int packedValue,
            out MaterialSwizzleMask mask)
        {
            int componentCount = packedValue & ComponentCountMask;
            if (componentCount < 1
                || componentCount > 4
                || (packedValue & ~EncodedMask) != 0)
            {
                mask = default;
                return false;
            }

            int usedBitCount = FirstComponentShift + componentCount * ComponentBitCount;
            int usedMask = (1 << usedBitCount) - 1;
            if ((packedValue & ~usedMask) != 0)
            {
                mask = default;
                return false;
            }

            mask = new MaterialSwizzleMask(packedValue);
            return true;
        }

        public bool Equals(MaterialSwizzleMask other)
        {
            return PackedValue == other.PackedValue;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialSwizzleMask other && Equals(other);
        }

        public override int GetHashCode()
        {
            return PackedValue;
        }
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
            uint4 constantBits = math.asuint(Constant);
            uint4 otherConstantBits = math.asuint(other.Constant);
            return Opcode == other.Opcode
                && Type == other.Type
                && Semantic == other.Semantic
                && constantBits.x == otherConstantBits.x
                && constantBits.y == otherConstantBits.y
                && constantBits.z == otherConstantBits.z
                && constantBits.w == otherConstantBits.w
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
                uint4 constantBits = math.asuint(Constant);
                int hashCode = (int) Opcode;
                hashCode = (hashCode * 397) ^ (int) Type;
                hashCode = (hashCode * 397) ^ Semantic;
                hashCode = (hashCode * 397) ^ (int) constantBits.x;
                hashCode = (hashCode * 397) ^ (int) constantBits.y;
                hashCode = (hashCode * 397) ^ (int) constantBits.z;
                hashCode = (hashCode * 397) ^ (int) constantBits.w;
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
        private readonly List<MaterialParameterDeclaration> m_ParameterDeclarations = new();
        private readonly List<MaterialResourceDeclaration> m_ResourceDeclarations = new();
        private readonly IReadOnlyList<MaterialValueNode> m_NodesView;
        private readonly IReadOnlyList<MaterialParameterDeclaration> m_ParameterDeclarationsView;
        private readonly IReadOnlyList<MaterialResourceDeclaration> m_ResourceDeclarationsView;

        internal MaterialValueIR()
        {
            m_NodesView = m_Nodes.AsReadOnly();
            m_ParameterDeclarationsView = m_ParameterDeclarations.AsReadOnly();
            m_ResourceDeclarationsView = m_ResourceDeclarations.AsReadOnly();
        }

        internal MaterialValueIR(
            IReadOnlyList<MaterialParameterDeclaration> parameterDeclarations,
            IReadOnlyList<MaterialResourceDeclaration> resourceDeclarations)
            : this()
        {
            if (parameterDeclarations == null)
                throw new ArgumentNullException(nameof(parameterDeclarations));
            if (resourceDeclarations == null)
                throw new ArgumentNullException(nameof(resourceDeclarations));

            for (int i = 0; i < parameterDeclarations.Count; i++)
                AddPredeclaredParameter(parameterDeclarations[i]);
            for (int i = 0; i < resourceDeclarations.Count; i++)
                AddPredeclaredResource(resourceDeclarations[i]);
        }

        internal IReadOnlyList<MaterialValueNode> Nodes => m_NodesView;

        internal IReadOnlyList<MaterialParameterDeclaration> ParameterDeclarations =>
            m_ParameterDeclarationsView;

        internal IReadOnlyList<MaterialResourceDeclaration> ResourceDeclarations =>
            m_ResourceDeclarationsView;

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
            return Parameter(
                MaterialNativeTemplateDeclarationAdapter.GetParameter(parameter));
        }

        internal MaterialValue Parameter(in MaterialParameterDeclaration declaration)
        {
            int declarationIndex = GetOrAddParameterDeclaration(declaration);
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Parameter,
                declaration.Type,
                declarationIndex,
                default,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand));
        }

        internal MaterialValue TextureResource(MaterialTextureResource resource)
        {
            return TextureResource(
                MaterialNativeTemplateDeclarationAdapter.GetTexture(resource));
        }

        internal MaterialValue TextureResource(in MaterialResourceDeclaration declaration)
        {
            int declarationIndex = GetOrAddResourceDeclaration(declaration);
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.TextureResource,
                declaration.Type,
                declarationIndex,
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
            ValidateValue(texture, nameof(texture));
            ValidateValue(coordinates, nameof(coordinates));
            ValidateValue(ddx, nameof(ddx));
            ValidateValue(ddy, nameof(ddy));
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
            ValidateValue(value, nameof(value));
            return EmitUnary(MaterialValueOpcode.Ddx, value);
        }

        internal MaterialValue Ddy(MaterialValue value)
        {
            ValidateValue(value, nameof(value));
            return EmitUnary(MaterialValueOpcode.Ddy, value);
        }

        internal MaterialValue Add(MaterialValue left, MaterialValue right)
        {
            ValidateBinaryOperands(left, right);
            return EmitBinary(MaterialValueOpcode.Add, left, right);
        }

        internal MaterialValue Multiply(MaterialValue left, MaterialValue right)
        {
            ValidateBinaryOperands(left, right);
            return EmitBinary(MaterialValueOpcode.Multiply, left, right);
        }

        internal MaterialValue Subtract(MaterialValue left, MaterialValue right)
        {
            ValidateBinaryOperands(left, right);
            return EmitBinary(MaterialValueOpcode.Subtract, left, right);
        }

        internal MaterialValue Divide(MaterialValue left, MaterialValue right)
        {
            ValidateBinaryOperands(left, right);
            return EmitBinary(MaterialValueOpcode.Divide, left, right);
        }

        internal MaterialValue Min(MaterialValue left, MaterialValue right)
        {
            ValidateBinaryOperands(left, right);
            return EmitBinary(MaterialValueOpcode.Min, left, right);
        }

        internal MaterialValue Max(MaterialValue left, MaterialValue right)
        {
            ValidateBinaryOperands(left, right);
            return EmitBinary(MaterialValueOpcode.Max, left, right);
        }

        internal MaterialValue Saturate(MaterialValue value)
        {
            ValidateValue(value, nameof(value));
            return EmitUnary(MaterialValueOpcode.Saturate, value);
        }

        internal MaterialValue OneMinus(MaterialValue value)
        {
            ValidateValue(value, nameof(value));
            return EmitUnary(MaterialValueOpcode.OneMinus, value);
        }

        internal MaterialValue Normalize(MaterialValue value)
        {
            ValidateValue(value, nameof(value));
            return EmitUnary(MaterialValueOpcode.Normalize, value);
        }

        internal MaterialValue Dot(MaterialValue left, MaterialValue right)
        {
            ValidateBinaryOperands(left, right);
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Dot,
                MaterialValueType.Float,
                default,
                default,
                left.Index,
                right.Index,
                InvalidOperand,
                InvalidOperand));
        }

        internal MaterialValue Compare(
            MaterialValue left,
            MaterialValue right,
            MaterialComparison comparison)
        {
            ValidateBinaryOperands(left, right);
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Compare,
                MaterialValueType.Bool,
                (int) comparison,
                default,
                left.Index,
                right.Index,
                InvalidOperand,
                InvalidOperand));
        }

        internal MaterialValue Swizzle(
            MaterialValue value,
            in MaterialSwizzleMask mask)
        {
            ValidateValue(value, nameof(value));
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Swizzle,
                mask.ResultType,
                mask.PackedValue,
                default,
                value.Index,
                InvalidOperand,
                InvalidOperand,
                InvalidOperand));
        }

        internal MaterialValue Compose(MaterialValue x, MaterialValue y)
        {
            ValidateBinaryOperands(x, y);
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Compose,
                MaterialValueType.Float2,
                default,
                default,
                x.Index,
                y.Index,
                InvalidOperand,
                InvalidOperand));
        }

        internal MaterialValue Compose(
            MaterialValue x,
            MaterialValue y,
            MaterialValue z)
        {
            ValidateValue(x, nameof(x));
            ValidateValue(y, nameof(y));
            ValidateValue(z, nameof(z));
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Compose,
                MaterialValueType.Float3,
                default,
                default,
                x.Index,
                y.Index,
                z.Index,
                InvalidOperand));
        }

        internal MaterialValue Compose(
            MaterialValue x,
            MaterialValue y,
            MaterialValue z,
            MaterialValue w)
        {
            ValidateValue(x, nameof(x));
            ValidateValue(y, nameof(y));
            ValidateValue(z, nameof(z));
            ValidateValue(w, nameof(w));
            return Emit(new MaterialValueNode(
                MaterialValueOpcode.Compose,
                MaterialValueType.Float4,
                default,
                default,
                x.Index,
                y.Index,
                z.Index,
                w.Index));
        }

        internal MaterialValue Lerp(
            MaterialValue left,
            MaterialValue right,
            MaterialValue weight)
        {
            ValidateBinaryOperands(left, right);
            ValidateValue(weight, nameof(weight));
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
            ValidateValue(condition, nameof(condition));
            ValidateValue(whenTrue, nameof(whenTrue));
            ValidateValue(whenFalse, nameof(whenFalse));
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

        internal bool TryGetParameterDeclaration(
            int declarationIndex,
            out MaterialParameterDeclaration declaration)
        {
            if ((uint) declarationIndex < (uint) m_ParameterDeclarations.Count)
            {
                declaration = m_ParameterDeclarations[declarationIndex];
                return true;
            }

            declaration = default;
            return false;
        }

        internal bool TryGetResourceDeclaration(
            int declarationIndex,
            out MaterialResourceDeclaration declaration)
        {
            if ((uint) declarationIndex < (uint) m_ResourceDeclarations.Count)
            {
                declaration = m_ResourceDeclarations[declarationIndex];
                return true;
            }

            declaration = default;
            return false;
        }

        internal void Freeze()
        {
            IsFrozen = true;
        }

        internal MaterialValue AppendVerifiedNode(in MaterialValueNode node)
        {
            return Emit(node);
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

            MaterialIRVerifier.VerifyCandidateNode(this, node).ThrowIfInvalid();

            if (m_ValueSet.TryGetValue(node, out int existingIndex))
                return new MaterialValue(this, existingIndex, node.Type);

            int index = m_Nodes.Count;
            m_Nodes.Add(node);
            m_ValueSet.Add(node, index);
            return new MaterialValue(this, index, node.Type);
        }

        private int GetOrAddParameterDeclaration(
            in MaterialParameterDeclaration declaration)
        {
            RequireMutable();
            ValidateDeclaration(declaration.Symbol, declaration.Type, isResource: false);
            for (int i = 0; i < m_ParameterDeclarations.Count; i++)
            {
                MaterialParameterDeclaration existing = m_ParameterDeclarations[i];
                if (existing == declaration)
                    return i;
                if (string.Equals(existing.Symbol, declaration.Symbol, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Material parameter '{declaration.Symbol}' is already declared as {existing.Type}.",
                        nameof(declaration));
                }
            }

            int index = m_ParameterDeclarations.Count;
            m_ParameterDeclarations.Add(declaration);
            return index;
        }

        private int GetOrAddResourceDeclaration(
            in MaterialResourceDeclaration declaration)
        {
            RequireMutable();
            ValidateDeclaration(declaration.Symbol, declaration.Type, isResource: true);
            for (int i = 0; i < m_ResourceDeclarations.Count; i++)
            {
                MaterialResourceDeclaration existing = m_ResourceDeclarations[i];
                if (existing == declaration)
                    return i;
                if (string.Equals(existing.Symbol, declaration.Symbol, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Material resource '{declaration.Symbol}' is already declared as {existing.Type}.",
                        nameof(declaration));
                }
            }

            int index = m_ResourceDeclarations.Count;
            m_ResourceDeclarations.Add(declaration);
            return index;
        }

        private void AddPredeclaredParameter(
            in MaterialParameterDeclaration declaration)
        {
            ValidateDeclaration(declaration.Symbol, declaration.Type, isResource: false);
            for (int i = 0; i < m_ParameterDeclarations.Count; i++)
            {
                if (string.Equals(
                        m_ParameterDeclarations[i].Symbol,
                        declaration.Symbol,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Material parameter '{declaration.Symbol}' is already declared.",
                        nameof(declaration));
                }
            }
            m_ParameterDeclarations.Add(declaration);
        }

        private void AddPredeclaredResource(
            in MaterialResourceDeclaration declaration)
        {
            ValidateDeclaration(declaration.Symbol, declaration.Type, isResource: true);
            for (int i = 0; i < m_ResourceDeclarations.Count; i++)
            {
                if (string.Equals(
                        m_ResourceDeclarations[i].Symbol,
                        declaration.Symbol,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Material resource '{declaration.Symbol}' is already declared.",
                        nameof(declaration));
                }
            }
            m_ResourceDeclarations.Add(declaration);
        }

        private static void ValidateDeclaration(
            string symbol,
            MaterialValueType type,
            bool isResource)
        {
            if (string.IsNullOrEmpty(symbol))
                throw new ArgumentException("Material declaration symbol cannot be empty.", nameof(symbol));
            if (isResource)
            {
                if (type != MaterialValueType.Texture2D)
                {
                    throw new ArgumentException(
                        $"Material resource '{symbol}' has unsupported type {type}.",
                        nameof(type));
                }
                return;
            }

            if (type == MaterialValueType.Texture2D
                || (uint) type > (uint) MaterialValueType.Texture2D)
            {
                throw new ArgumentException(
                    $"Material parameter '{symbol}' has unsupported type {type}.",
                    nameof(type));
            }
        }

        private void ValidateBinaryOperands(MaterialValue left, MaterialValue right)
        {
            ValidateValue(left, nameof(left));
            ValidateValue(right, nameof(right));
        }

        private void ValidateValue(MaterialValue value, string parameterName)
        {
            if (!Owns(value))
                throw new ArgumentException("Material value is not owned by this IR.", parameterName);
        }

        private void RequireMutable()
        {
            if (IsFrozen)
                throw new InvalidOperationException("Cannot modify a frozen material value IR.");
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

    }
}
