using System;
using System.Collections.Generic;
using System.Text;

namespace VividRP.Runtime.GPUDriven
{
    internal readonly struct MaterialGenericParameterBinding :
        IEquatable<MaterialGenericParameterBinding>
    {
        internal MaterialGenericParameterBinding(
            in MaterialParameterDeclaration declaration,
            int wordOffset,
            int wordCount)
        {
            Declaration = declaration;
            WordOffset = wordOffset;
            WordCount = wordCount;
        }

        internal MaterialParameterDeclaration Declaration { get; }

        internal int WordOffset { get; }

        internal int WordCount { get; }

        internal int ByteOffset => WordOffset * sizeof(uint);

        internal int ByteSize => WordCount * sizeof(uint);

        public bool Equals(MaterialGenericParameterBinding other)
        {
            return Declaration == other.Declaration
                && WordOffset == other.WordOffset
                && WordCount == other.WordCount;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialGenericParameterBinding other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Declaration.GetHashCode();
                hashCode = (hashCode * 397) ^ WordOffset;
                hashCode = (hashCode * 397) ^ WordCount;
                return hashCode;
            }
        }
    }

    internal readonly struct MaterialGenericResourceBinding :
        IEquatable<MaterialGenericResourceBinding>
    {
        internal MaterialGenericResourceBinding(
            in MaterialResourceDeclaration declaration,
            int slot)
        {
            Declaration = declaration;
            Slot = slot;
        }

        internal MaterialResourceDeclaration Declaration { get; }

        internal int Slot { get; }

        public bool Equals(MaterialGenericResourceBinding other)
        {
            return Declaration == other.Declaration && Slot == other.Slot;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialGenericResourceBinding other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Declaration.GetHashCode() * 397) ^ Slot;
            }
        }
    }

    internal sealed class MaterialGenericLayout
    {
        private const int WordsPerLane = 4;

        private readonly IReadOnlyList<MaterialGenericParameterBinding>
            m_ParameterBindings;
        private readonly IReadOnlyList<MaterialGenericResourceBinding>
            m_ResourceBindings;
        private readonly string m_DebugDump;

        internal MaterialGenericLayout(MaterialValueRequirements requirements)
            : this(
                requirements?.ParameterDeclarations
                    ?? throw new ArgumentNullException(nameof(requirements)),
                requirements.ResourceDeclarations)
        {
        }

        internal MaterialGenericLayout(
            IReadOnlyList<MaterialParameterDeclaration> parameterDeclarations,
            IReadOnlyList<MaterialResourceDeclaration> resourceDeclarations)
        {
            if (parameterDeclarations == null)
                throw new ArgumentNullException(nameof(parameterDeclarations));
            if (resourceDeclarations == null)
                throw new ArgumentNullException(nameof(resourceDeclarations));

            MaterialParameterDeclaration[] parameters =
                CopyParameters(parameterDeclarations);
            MaterialResourceDeclaration[] resources =
                CopyResources(resourceDeclarations);
            Array.Sort(parameters, CompareParameterDeclarations);
            Array.Sort(resources, CompareResourceDeclarations);
            ValidateParameters(parameters);
            ValidateResources(resources);

            var parameterBindings =
                new MaterialGenericParameterBinding[parameters.Length];
            int wordOffset = 0;
            for (int parameterIndex = 0;
                 parameterIndex < parameters.Length;
                 parameterIndex++)
            {
                MaterialParameterDeclaration declaration =
                    parameters[parameterIndex];
                int wordCount = GetParameterWordCount(declaration.Type);
                int laneWordOffset = wordOffset % WordsPerLane;
                if (laneWordOffset + wordCount > WordsPerLane)
                    wordOffset = AlignToLane(wordOffset);

                parameterBindings[parameterIndex] =
                    new MaterialGenericParameterBinding(
                        declaration,
                        wordOffset,
                        wordCount);
                wordOffset += wordCount;
            }

            ParameterStrideInWords = AlignToLane(wordOffset);
            var resourceBindings =
                new MaterialGenericResourceBinding[resources.Length];
            for (int resourceIndex = 0;
                 resourceIndex < resources.Length;
                 resourceIndex++)
            {
                resourceBindings[resourceIndex] =
                    new MaterialGenericResourceBinding(
                        resources[resourceIndex],
                        resourceIndex);
            }

            m_ParameterBindings = Array.AsReadOnly(parameterBindings);
            m_ResourceBindings = Array.AsReadOnly(resourceBindings);
            Fingerprint = ComputeFingerprint();
            m_DebugDump = BuildDebugDump();
        }

        internal uint Version => MaterialProgramContract.GenericLayoutVersion;

        internal int ParameterStrideInWords { get; }

        internal int ParameterStride => ParameterStrideInWords * sizeof(uint);

        internal int ResourceCount => m_ResourceBindings.Count;

        internal IReadOnlyList<MaterialGenericParameterBinding> ParameterBindings =>
            m_ParameterBindings;

        internal IReadOnlyList<MaterialGenericResourceBinding> ResourceBindings =>
            m_ResourceBindings;

        internal ulong Fingerprint { get; }

        internal bool TryGetParameterBinding(
            in MaterialParameterDeclaration declaration,
            out MaterialGenericParameterBinding binding)
        {
            for (int bindingIndex = 0;
                 bindingIndex < m_ParameterBindings.Count;
                 bindingIndex++)
            {
                if (m_ParameterBindings[bindingIndex].Declaration != declaration)
                    continue;

                binding = m_ParameterBindings[bindingIndex];
                return true;
            }

            binding = default;
            return false;
        }

        internal bool TryGetResourceBinding(
            in MaterialResourceDeclaration declaration,
            out MaterialGenericResourceBinding binding)
        {
            for (int bindingIndex = 0;
                 bindingIndex < m_ResourceBindings.Count;
                 bindingIndex++)
            {
                if (m_ResourceBindings[bindingIndex].Declaration != declaration)
                    continue;

                binding = m_ResourceBindings[bindingIndex];
                return true;
            }

            binding = default;
            return false;
        }

        internal bool PayloadEquals(MaterialGenericLayout other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other == null
                || Version != other.Version
                || ParameterStrideInWords != other.ParameterStrideInWords
                || m_ParameterBindings.Count != other.m_ParameterBindings.Count
                || m_ResourceBindings.Count != other.m_ResourceBindings.Count)
            {
                return false;
            }

            for (int bindingIndex = 0;
                 bindingIndex < m_ParameterBindings.Count;
                 bindingIndex++)
            {
                if (!m_ParameterBindings[bindingIndex].Equals(
                        other.m_ParameterBindings[bindingIndex]))
                {
                    return false;
                }
            }

            for (int bindingIndex = 0;
                 bindingIndex < m_ResourceBindings.Count;
                 bindingIndex++)
            {
                if (!m_ResourceBindings[bindingIndex].Equals(
                        other.m_ResourceBindings[bindingIndex]))
                {
                    return false;
                }
            }

            return true;
        }

        internal string GetDebugDump()
        {
            return m_DebugDump;
        }

        internal static int CompareParameterDeclarations(
            MaterialParameterDeclaration left,
            MaterialParameterDeclaration right)
        {
            int result = string.CompareOrdinal(left.Symbol, right.Symbol);
            return result != 0
                ? result
                : ((int) left.Type).CompareTo((int) right.Type);
        }

        internal static int CompareResourceDeclarations(
            MaterialResourceDeclaration left,
            MaterialResourceDeclaration right)
        {
            int result = string.CompareOrdinal(left.Symbol, right.Symbol);
            if (result == 0)
                result = ((int) left.Type).CompareTo((int) right.Type);
            return result != 0
                ? result
                : ((int) left.SampleClass).CompareTo(
                    (int) right.SampleClass);
        }

        private static MaterialParameterDeclaration[] CopyParameters(
            IReadOnlyList<MaterialParameterDeclaration> declarations)
        {
            var copy = new MaterialParameterDeclaration[declarations.Count];
            for (int declarationIndex = 0;
                 declarationIndex < declarations.Count;
                 declarationIndex++)
            {
                copy[declarationIndex] = declarations[declarationIndex];
            }
            return copy;
        }

        private static MaterialResourceDeclaration[] CopyResources(
            IReadOnlyList<MaterialResourceDeclaration> declarations)
        {
            var copy = new MaterialResourceDeclaration[declarations.Count];
            for (int declarationIndex = 0;
                 declarationIndex < declarations.Count;
                 declarationIndex++)
            {
                copy[declarationIndex] = declarations[declarationIndex];
            }
            return copy;
        }

        private static void ValidateParameters(
            MaterialParameterDeclaration[] declarations)
        {
            for (int declarationIndex = 0;
                 declarationIndex < declarations.Length;
                 declarationIndex++)
            {
                MaterialParameterDeclaration declaration =
                    declarations[declarationIndex];
                if (string.IsNullOrEmpty(declaration.Symbol))
                {
                    throw new ArgumentException(
                        "Generic material parameter symbols cannot be null or empty.",
                        nameof(declarations));
                }

                GetParameterWordCount(declaration.Type);
                if (declarationIndex > 0
                    && string.Equals(
                        declarations[declarationIndex - 1].Symbol,
                        declaration.Symbol,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Generic material parameter '{declaration.Symbol}' is declared more than once.",
                        nameof(declarations));
                }
            }
        }

        private static void ValidateResources(
            MaterialResourceDeclaration[] declarations)
        {
            for (int declarationIndex = 0;
                 declarationIndex < declarations.Length;
                 declarationIndex++)
            {
                MaterialResourceDeclaration declaration =
                    declarations[declarationIndex];
                if (string.IsNullOrEmpty(declaration.Symbol))
                {
                    throw new ArgumentException(
                        "Generic material resource symbols cannot be null or empty.",
                        nameof(declarations));
                }
                if (declaration.Type != MaterialValueType.Texture2D)
                {
                    throw new ArgumentException(
                        $"Generic material resource '{declaration.Symbol}' has unsupported type '{declaration.Type}'.",
                        nameof(declarations));
                }
                if ((uint) declaration.SampleClass
                    > (uint) MaterialTextureSampleClass.Mask)
                {
                    throw new ArgumentException(
                        $"Generic material resource '{declaration.Symbol}' has unsupported sample class '{declaration.SampleClass}'.",
                        nameof(declarations));
                }
                if (declarationIndex > 0
                    && string.Equals(
                        declarations[declarationIndex - 1].Symbol,
                        declaration.Symbol,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Generic material resource '{declaration.Symbol}' is declared more than once.",
                        nameof(declarations));
                }
            }
        }

        private static int GetParameterWordCount(MaterialValueType type)
        {
            switch (type)
            {
                case MaterialValueType.Bool:
                case MaterialValueType.Float:
                    return 1;
                case MaterialValueType.Float2:
                    return 2;
                case MaterialValueType.Float3:
                    return 3;
                case MaterialValueType.Float4:
                    return 4;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(type),
                        type,
                        "Generic material parameters must be scalar or vector values.");
            }
        }

        private static int AlignToLane(int wordOffset)
        {
            return (wordOffset + WordsPerLane - 1)
                / WordsPerLane
                * WordsPerLane;
        }

        private ulong ComputeFingerprint()
        {
            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(ref hash, Version);
            MaterialProgramHashUtility.Add(ref hash, ParameterStrideInWords);
            MaterialProgramHashUtility.Add(ref hash, m_ParameterBindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < m_ParameterBindings.Count;
                 bindingIndex++)
            {
                MaterialGenericParameterBinding binding =
                    m_ParameterBindings[bindingIndex];
                MaterialProgramHashUtility.Add(
                    ref hash,
                    binding.Declaration.Symbol);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Declaration.Type);
                MaterialProgramHashUtility.Add(ref hash, binding.WordOffset);
                MaterialProgramHashUtility.Add(ref hash, binding.WordCount);
            }

            MaterialProgramHashUtility.Add(ref hash, m_ResourceBindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < m_ResourceBindings.Count;
                 bindingIndex++)
            {
                MaterialGenericResourceBinding binding =
                    m_ResourceBindings[bindingIndex];
                MaterialProgramHashUtility.Add(
                    ref hash,
                    binding.Declaration.Symbol);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Declaration.Type);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Declaration.SampleClass);
                MaterialProgramHashUtility.Add(ref hash, binding.Slot);
            }
            return hash;
        }

        private string BuildDebugDump()
        {
            var builder = new StringBuilder();
            builder.Append("material_generic_layout v=")
                .Append(Version)
                .Append(" fingerprint=0x")
                .Append(Fingerprint.ToString("X16"))
                .Append(" parameter_stride_words=")
                .Append(ParameterStrideInWords)
                .Append(" resource_count=")
                .Append(ResourceCount)
                .AppendLine();

            for (int bindingIndex = 0;
                 bindingIndex < m_ParameterBindings.Count;
                 bindingIndex++)
            {
                MaterialGenericParameterBinding binding =
                    m_ParameterBindings[bindingIndex];
                builder.Append("  parameter ")
                    .Append(binding.Declaration.Symbol)
                    .Append(':')
                    .Append(binding.Declaration.Type)
                    .Append(" word_offset=")
                    .Append(binding.WordOffset)
                    .Append(" word_count=")
                    .Append(binding.WordCount)
                    .AppendLine();
            }

            for (int bindingIndex = 0;
                 bindingIndex < m_ResourceBindings.Count;
                 bindingIndex++)
            {
                MaterialGenericResourceBinding binding =
                    m_ResourceBindings[bindingIndex];
                builder.Append("  resource ")
                    .Append(binding.Declaration.Symbol)
                    .Append(':')
                    .Append(binding.Declaration.Type)
                    .Append('/')
                    .Append(binding.Declaration.SampleClass)
                    .Append(" slot=")
                    .Append(binding.Slot)
                    .AppendLine();
            }
            return builder.ToString();
        }
    }

    internal enum MaterialParameterStorageConversion
    {
        None = 0,
        Float3ToFloat4 = 1,
    }

    internal readonly struct MaterialNativeParameterBinding
    {
        internal MaterialNativeParameterBinding(
            in MaterialParameterDeclaration declaration,
            MaterialRuntimeParameter target,
            MaterialParameterStorageConversion conversion =
                MaterialParameterStorageConversion.None)
        {
            Declaration = declaration;
            Target = target;
            Conversion = conversion;
        }

        internal MaterialParameterDeclaration Declaration { get; }

        internal MaterialRuntimeParameter Target { get; }

        internal MaterialParameterStorageConversion Conversion { get; }
    }

    internal readonly struct MaterialNativeResourceBinding
    {
        internal MaterialNativeResourceBinding(
            in MaterialResourceDeclaration declaration,
            MaterialTextureResource target)
        {
            Declaration = declaration;
            Target = target;
        }

        internal MaterialResourceDeclaration Declaration { get; }

        internal MaterialTextureResource Target { get; }
    }

    internal sealed class MaterialNativeTemplateLayoutSchema
    {
        private readonly IReadOnlyList<MaterialNativeParameterBinding>
            m_ParameterBindings;
        private readonly IReadOnlyList<MaterialNativeResourceBinding>
            m_ResourceBindings;

        internal MaterialNativeTemplateLayoutSchema(
            CompiledParameterLayout parameterLayout,
            CompiledResourceLayout resourceLayout,
            MaterialNativeParameterBinding[] parameterBindings,
            MaterialNativeResourceBinding[] resourceBindings)
        {
            ParameterLayout = parameterLayout
                ?? throw new ArgumentNullException(nameof(parameterLayout));
            ResourceLayout = resourceLayout
                ?? throw new ArgumentNullException(nameof(resourceLayout));
            if (parameterBindings == null)
                throw new ArgumentNullException(nameof(parameterBindings));
            if (resourceBindings == null)
                throw new ArgumentNullException(nameof(resourceBindings));

            var parameterCopy =
                (MaterialNativeParameterBinding[]) parameterBindings.Clone();
            var resourceCopy =
                (MaterialNativeResourceBinding[]) resourceBindings.Clone();
            Array.Sort(parameterCopy, CompareParameterBindings);
            Array.Sort(resourceCopy, CompareResourceBindings);
            ValidateParameterBindings(parameterCopy, ParameterLayout);
            ValidateResourceBindings(resourceCopy, ResourceLayout);

            m_ParameterBindings = Array.AsReadOnly(parameterCopy);
            m_ResourceBindings = Array.AsReadOnly(resourceCopy);
            LiveLayout = new MaterialGenericLayout(
                GetParameterDeclarations(parameterCopy),
                GetResourceDeclarations(resourceCopy));
            if (LiveLayout.ParameterBindings.Count != parameterCopy.Length
                || LiveLayout.ResourceBindings.Count != resourceCopy.Length)
            {
                throw new InvalidOperationException(
                    "Native template layout schema does not completely map its live declarations.");
            }
        }

        internal CompiledParameterLayout ParameterLayout { get; }

        internal CompiledResourceLayout ResourceLayout { get; }

        internal MaterialGenericLayout LiveLayout { get; }

        internal IReadOnlyList<MaterialNativeParameterBinding> ParameterBindings =>
            m_ParameterBindings;

        internal IReadOnlyList<MaterialNativeResourceBinding> ResourceBindings =>
            m_ResourceBindings;

        internal bool Matches(MaterialValueRequirements requirements)
        {
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));
            if (requirements.ParameterDeclarations.Count
                    != m_ParameterBindings.Count
                || requirements.ResourceDeclarations.Count
                    != m_ResourceBindings.Count)
            {
                return false;
            }

            for (int declarationIndex = 0;
                 declarationIndex < requirements.ParameterDeclarations.Count;
                 declarationIndex++)
            {
                if (!TryGetParameterBinding(
                        requirements.ParameterDeclarations[declarationIndex],
                        out _))
                {
                    return false;
                }
            }

            for (int declarationIndex = 0;
                 declarationIndex < requirements.ResourceDeclarations.Count;
                 declarationIndex++)
            {
                if (!TryGetResourceBinding(
                        requirements.ResourceDeclarations[declarationIndex],
                        out _))
                {
                    return false;
                }
            }

            return true;
        }

        internal bool MappingPayloadEquals(MaterialNativeTemplateLayoutSchema other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other == null
                || !LiveLayout.PayloadEquals(other.LiveLayout)
                || m_ParameterBindings.Count != other.m_ParameterBindings.Count
                || m_ResourceBindings.Count != other.m_ResourceBindings.Count)
            {
                return false;
            }

            for (int bindingIndex = 0;
                 bindingIndex < m_ParameterBindings.Count;
                 bindingIndex++)
            {
                MaterialNativeParameterBinding left =
                    m_ParameterBindings[bindingIndex];
                MaterialNativeParameterBinding right =
                    other.m_ParameterBindings[bindingIndex];
                if (left.Declaration != right.Declaration
                    || left.Target != right.Target
                    || left.Conversion != right.Conversion)
                {
                    return false;
                }
            }

            for (int bindingIndex = 0;
                 bindingIndex < m_ResourceBindings.Count;
                 bindingIndex++)
            {
                MaterialNativeResourceBinding left =
                    m_ResourceBindings[bindingIndex];
                MaterialNativeResourceBinding right =
                    other.m_ResourceBindings[bindingIndex];
                if (left.Declaration != right.Declaration
                    || left.Target != right.Target)
                {
                    return false;
                }
            }

            return true;
        }

        internal bool TryGetParameterBinding(
            in MaterialParameterDeclaration declaration,
            out MaterialNativeParameterBinding binding)
        {
            for (int bindingIndex = 0;
                 bindingIndex < m_ParameterBindings.Count;
                 bindingIndex++)
            {
                if (m_ParameterBindings[bindingIndex].Declaration != declaration)
                    continue;

                binding = m_ParameterBindings[bindingIndex];
                return true;
            }

            binding = default;
            return false;
        }

        internal bool TryGetResourceBinding(
            in MaterialResourceDeclaration declaration,
            out MaterialNativeResourceBinding binding)
        {
            for (int bindingIndex = 0;
                 bindingIndex < m_ResourceBindings.Count;
                 bindingIndex++)
            {
                if (m_ResourceBindings[bindingIndex].Declaration != declaration)
                    continue;

                binding = m_ResourceBindings[bindingIndex];
                return true;
            }

            binding = default;
            return false;
        }

        private static int CompareParameterBindings(
            MaterialNativeParameterBinding left,
            MaterialNativeParameterBinding right)
        {
            return MaterialGenericLayout.CompareParameterDeclarations(
                left.Declaration,
                right.Declaration);
        }

        private static int CompareResourceBindings(
            MaterialNativeResourceBinding left,
            MaterialNativeResourceBinding right)
        {
            return MaterialGenericLayout.CompareResourceDeclarations(
                left.Declaration,
                right.Declaration);
        }

        private static void ValidateParameterBindings(
            MaterialNativeParameterBinding[] bindings,
            CompiledParameterLayout parameterLayout)
        {
            for (int bindingIndex = 0;
                 bindingIndex < bindings.Length;
                 bindingIndex++)
            {
                MaterialNativeParameterBinding binding = bindings[bindingIndex];
                if (bindingIndex > 0
                    && string.Equals(
                        bindings[bindingIndex - 1].Declaration.Symbol,
                        binding.Declaration.Symbol,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Native template parameter '{binding.Declaration.Symbol}' is mapped more than once.",
                        nameof(bindings));
                }
                for (int previousIndex = 0;
                     previousIndex < bindingIndex;
                     previousIndex++)
                {
                    if (bindings[previousIndex].Target != binding.Target)
                        continue;

                    throw new ArgumentException(
                        $"Native template parameter target '{binding.Target}' is mapped more than once.",
                        nameof(bindings));
                }

                if (!parameterLayout.TryGetBinding(
                        binding.Target,
                        out MaterialParameterLayoutBinding physicalBinding))
                {
                    throw new ArgumentException(
                        $"Native template parameter target '{binding.Target}' is not present in physical layout '{parameterLayout.LayoutID}'.",
                        nameof(bindings));
                }
                if (!IsValidStorageConversion(
                        binding.Declaration.Type,
                        physicalBinding.Type,
                        binding.Conversion))
                {
                    throw new ArgumentException(
                        $"Native template parameter '{binding.Declaration.Symbol}:{binding.Declaration.Type}' cannot map to '{binding.Target}:{physicalBinding.Type}' using conversion '{binding.Conversion}'.",
                        nameof(bindings));
                }
            }
        }

        private static void ValidateResourceBindings(
            MaterialNativeResourceBinding[] bindings,
            CompiledResourceLayout resourceLayout)
        {
            for (int bindingIndex = 0;
                 bindingIndex < bindings.Length;
                 bindingIndex++)
            {
                MaterialNativeResourceBinding binding = bindings[bindingIndex];
                if (binding.Declaration.Type != MaterialValueType.Texture2D)
                {
                    throw new ArgumentException(
                        $"Native template resource '{binding.Declaration.Symbol}' must be Texture2D, not '{binding.Declaration.Type}'.",
                        nameof(bindings));
                }
                if (bindingIndex > 0
                    && string.Equals(
                        bindings[bindingIndex - 1].Declaration.Symbol,
                        binding.Declaration.Symbol,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Native template resource '{binding.Declaration.Symbol}' is mapped more than once.",
                        nameof(bindings));
                }
                for (int previousIndex = 0;
                     previousIndex < bindingIndex;
                     previousIndex++)
                {
                    if (bindings[previousIndex].Target != binding.Target)
                        continue;

                    throw new ArgumentException(
                        $"Native template resource target '{binding.Target}' is mapped more than once.",
                        nameof(bindings));
                }

                if (!resourceLayout.TryGetBinding(binding.Target, out _))
                {
                    throw new ArgumentException(
                        $"Native template resource target '{binding.Target}' is not present in physical layout '{resourceLayout.LayoutID}'.",
                        nameof(bindings));
                }
                MaterialTextureSampleClass targetSampleClass =
                    MaterialNativeTemplateDeclarationAdapter
                        .GetTexture(binding.Target)
                        .SampleClass;
                if (binding.Declaration.SampleClass != targetSampleClass)
                {
                    throw new ArgumentException(
                        $"Native template resource '{binding.Declaration.Symbol}' "
                        + $"uses sample class '{binding.Declaration.SampleClass}', "
                        + $"but target '{binding.Target}' requires "
                        + $"'{targetSampleClass}'.",
                        nameof(bindings));
                }
            }
        }

        private static bool IsValidStorageConversion(
            MaterialValueType logicalType,
            MaterialLayoutValueType storageType,
            MaterialParameterStorageConversion conversion)
        {
            switch (conversion)
            {
                case MaterialParameterStorageConversion.None:
                    return logicalType == MaterialValueType.Bool
                            && storageType == MaterialLayoutValueType.UInt
                        || logicalType == MaterialValueType.Float
                            && storageType == MaterialLayoutValueType.Float
                        || logicalType == MaterialValueType.Float4
                            && storageType == MaterialLayoutValueType.Float4;
                case MaterialParameterStorageConversion.Float3ToFloat4:
                    return logicalType == MaterialValueType.Float3
                        && storageType == MaterialLayoutValueType.Float4;
                default:
                    return false;
            }
        }

        private static MaterialParameterDeclaration[] GetParameterDeclarations(
            MaterialNativeParameterBinding[] bindings)
        {
            var declarations =
                new MaterialParameterDeclaration[bindings.Length];
            for (int bindingIndex = 0;
                 bindingIndex < bindings.Length;
                 bindingIndex++)
            {
                declarations[bindingIndex] = bindings[bindingIndex].Declaration;
            }
            return declarations;
        }

        private static MaterialResourceDeclaration[] GetResourceDeclarations(
            MaterialNativeResourceBinding[] bindings)
        {
            var declarations =
                new MaterialResourceDeclaration[bindings.Length];
            for (int bindingIndex = 0;
                 bindingIndex < bindings.Length;
                 bindingIndex++)
            {
                declarations[bindingIndex] = bindings[bindingIndex].Declaration;
            }
            return declarations;
        }
    }

    internal readonly struct MaterialProgramLayoutFingerprint :
        IEquatable<MaterialProgramLayoutFingerprint>
    {
        internal MaterialProgramLayoutFingerprint(uint version, ulong value)
        {
            Version = version;
            Value = value;
        }

        internal uint Version { get; }

        internal ulong Value { get; }

        public bool Equals(MaterialProgramLayoutFingerprint other)
        {
            return Version == other.Version && Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialProgramLayoutFingerprint other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int) Version;
                hashCode = (hashCode * 397) ^ (int) Value;
                hashCode = (hashCode * 397) ^ (int) (Value >> 32);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return $"layout_v={Version} 0x{Value:X16}";
        }

        public static bool operator ==(
            MaterialProgramLayoutFingerprint left,
            MaterialProgramLayoutFingerprint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            MaterialProgramLayoutFingerprint left,
            MaterialProgramLayoutFingerprint right)
        {
            return !left.Equals(right);
        }
    }

    internal static class MaterialProgramLayoutFingerprintBuilder
    {
        internal static MaterialProgramLayoutFingerprint Compute(
            MaterialGenericLayout genericLayout)
        {
            if (genericLayout == null)
                throw new ArgumentNullException(nameof(genericLayout));

            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(
                ref hash,
                MaterialProgramContract.LayoutFingerprintVersion);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) VividMaterialParameterLayoutID.GenericParameterLanes);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) VividMaterialResourceLayoutID.GenericResourceRecords);
            AddGenericLayout(ref hash, genericLayout);
            return new MaterialProgramLayoutFingerprint(
                MaterialProgramContract.LayoutFingerprintVersion,
                hash);
        }

        private static void AddGenericLayout(
            ref ulong hash,
            MaterialGenericLayout layout)
        {
            MaterialProgramHashUtility.Add(ref hash, layout.Version);
            MaterialProgramHashUtility.Add(ref hash, layout.ParameterStrideInWords);
            MaterialProgramHashUtility.Add(ref hash, layout.ParameterBindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < layout.ParameterBindings.Count;
                 bindingIndex++)
            {
                MaterialGenericParameterBinding binding =
                    layout.ParameterBindings[bindingIndex];
                MaterialProgramHashUtility.Add(
                    ref hash,
                    binding.Declaration.Symbol);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Declaration.Type);
                MaterialProgramHashUtility.Add(ref hash, binding.WordOffset);
                MaterialProgramHashUtility.Add(ref hash, binding.WordCount);
            }

            MaterialProgramHashUtility.Add(ref hash, layout.ResourceBindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < layout.ResourceBindings.Count;
                 bindingIndex++)
            {
                MaterialGenericResourceBinding binding =
                    layout.ResourceBindings[bindingIndex];
                MaterialProgramHashUtility.Add(
                    ref hash,
                    binding.Declaration.Symbol);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Declaration.Type);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Declaration.SampleClass);
                MaterialProgramHashUtility.Add(ref hash, binding.Slot);
            }
        }

    }
}
