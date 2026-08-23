using System;
using System.Collections.Generic;

namespace VividRP.Runtime.GPUDriven
{
    [Flags]
    internal enum ClosureFeatureMask
    {
        None = 0,
        BaseColorTexture = 1 << 0,
        NormalTexture = 1 << 1,
        MaskTexture = 1 << 2,
        AlphaClip = 1 << 3,
        Emission = 1 << 4,
        Unlit = 1 << 5,
    }

    internal enum ClosureOperatorKind
    {
        None,
        HorizontalMix,
        VerticalLayer,
    }

    internal readonly struct ClosureTopologyBudget
    {
        internal static ClosureTopologyBudget Prototype => new(2, 1);

        internal ClosureTopologyBudget(int maxClosureCount, int maxOperatorCount)
        {
            if (maxClosureCount < 1)
                throw new ArgumentOutOfRangeException(nameof(maxClosureCount));
            if (maxOperatorCount < 0)
                throw new ArgumentOutOfRangeException(nameof(maxOperatorCount));

            MaxClosureCount = maxClosureCount;
            MaxOperatorCount = maxOperatorCount;
        }

        internal int MaxClosureCount { get; }

        internal int MaxOperatorCount { get; }

        internal bool Allows(int closureCount, int operatorCount)
        {
            return closureCount >= 0
                && operatorCount >= 0
                && closureCount <= MaxClosureCount
                && operatorCount <= MaxOperatorCount;
        }
    }

    internal readonly struct ClosureNormalBasis
    {
        internal ClosureNormalBasis(MaterialValue normal, MaterialValue tangent)
        {
            Normal = normal;
            Tangent = tangent;
        }

        internal MaterialValue Normal { get; }

        internal MaterialValue Tangent { get; }
    }

    internal readonly struct ClosureSlab
    {
        internal ClosureSlab(
            MaterialValue baseColor,
            MaterialValue roughness,
            MaterialValue metallic,
            int normalBasisIndex,
            ClosureFeatureMask features,
            bool isTop,
            bool isBottom)
        {
            BaseColor = baseColor;
            Roughness = roughness;
            Metallic = metallic;
            NormalBasisIndex = normalBasisIndex;
            Features = features;
            IsTop = isTop;
            IsBottom = isBottom;
        }

        internal MaterialValue BaseColor { get; }

        internal MaterialValue Roughness { get; }

        internal MaterialValue Metallic { get; }

        internal int NormalBasisIndex { get; }

        internal ClosureFeatureMask Features { get; }

        internal bool IsTop { get; }

        internal bool IsBottom { get; }
    }

    internal readonly struct ClosureOperator
    {
        internal ClosureOperator(
            ClosureOperatorKind kind,
            int backgroundSlabIndex,
            int foregroundSlabIndex,
            MaterialValue weight)
        {
            Kind = kind;
            BackgroundSlabIndex = backgroundSlabIndex;
            ForegroundSlabIndex = foregroundSlabIndex;
            Weight = weight;
        }

        internal ClosureOperatorKind Kind { get; }

        internal int BackgroundSlabIndex { get; }

        internal int ForegroundSlabIndex { get; }

        internal MaterialValue Weight { get; }
    }

    internal sealed class ClosureTopology
    {
        private readonly ClosureNormalBasis[] m_NormalBases;
        private readonly ClosureSlab[] m_Slabs;
        private readonly ClosureOperator[] m_Operators;
        private readonly IReadOnlyList<ClosureNormalBasis> m_NormalBasesView;
        private readonly IReadOnlyList<ClosureSlab> m_SlabsView;
        private readonly IReadOnlyList<ClosureOperator> m_OperatorsView;

        internal ClosureTopology(
            MaterialValueIR valueIR,
            ClosureNormalBasis[] normalBases,
            ClosureSlab[] slabs,
            ClosureOperator[] operators,
            ClosureTopologyBudget budget)
        {
            ValueIR = valueIR ?? throw new ArgumentNullException(nameof(valueIR));
            if (normalBases == null)
                throw new ArgumentNullException(nameof(normalBases));
            if (slabs == null)
                throw new ArgumentNullException(nameof(slabs));
            if (operators == null)
                throw new ArgumentNullException(nameof(operators));

            m_NormalBases = (ClosureNormalBasis[]) normalBases.Clone();
            m_Slabs = (ClosureSlab[]) slabs.Clone();
            m_Operators = (ClosureOperator[]) operators.Clone();
            m_NormalBasesView = Array.AsReadOnly(m_NormalBases);
            m_SlabsView = Array.AsReadOnly(m_Slabs);
            m_OperatorsView = Array.AsReadOnly(m_Operators);
            Budget = budget;

            Validate();
        }

        internal MaterialValueIR ValueIR { get; }

        internal IReadOnlyList<ClosureNormalBasis> NormalBases => m_NormalBasesView;

        internal IReadOnlyList<ClosureSlab> Slabs => m_SlabsView;

        internal IReadOnlyList<ClosureOperator> Operators => m_OperatorsView;

        internal ClosureTopologyBudget Budget { get; }

        internal int ClosureCount => m_Slabs.Length;

        internal int OperatorCount => m_Operators.Length;

        internal ClosureFeatureMask FeatureMask
        {
            get
            {
                ClosureFeatureMask features = ClosureFeatureMask.None;
                foreach (ClosureSlab slab in m_Slabs)
                    features |= slab.Features;
                return features;
            }
        }

        internal bool IsWithinBudget => Budget.Allows(ClosureCount, OperatorCount);

        private void Validate()
        {
            if (m_Slabs.Length == 0)
                throw new ArgumentException("Closure topology must contain at least one slab.", nameof(m_Slabs));
            if (!IsWithinBudget)
            {
                throw new InvalidOperationException(
                    $"Closure topology requires {ClosureCount} closures and {OperatorCount} operators, "
                    + $"but the prototype budget allows {Budget.MaxClosureCount} closures and "
                    + $"{Budget.MaxOperatorCount} operators.");
            }

            for (int i = 0; i < m_NormalBases.Length; i++)
            {
                ClosureNormalBasis basis = m_NormalBases[i];
                RequireValueType(basis.Normal, MaterialValueType.Float3, "normal basis normal");
                RequireValueType(basis.Tangent, MaterialValueType.Float4, "normal basis tangent");
            }

            for (int i = 0; i < m_Slabs.Length; i++)
            {
                ClosureSlab slab = m_Slabs[i];
                RequireValueType(slab.BaseColor, MaterialValueType.Float4, "slab base color");
                RequireValueType(slab.Roughness, MaterialValueType.Float, "slab roughness");
                RequireValueType(slab.Metallic, MaterialValueType.Float, "slab metallic");
                if ((uint) slab.NormalBasisIndex >= (uint) m_NormalBases.Length)
                    throw new ArgumentOutOfRangeException(nameof(m_Slabs), "Slab normal basis is invalid.");
            }

            if (m_Slabs.Length == 1)
            {
                if (m_Operators.Length != 0)
                    throw new ArgumentException("A single-slab topology cannot contain an operator.", nameof(m_Operators));
                if (!m_Slabs[0].IsTop || !m_Slabs[0].IsBottom)
                    throw new ArgumentException("A single slab must be both top and bottom.", nameof(m_Slabs));
                return;
            }

            if (m_Slabs.Length != 2 || m_Operators.Length != 1)
            {
                throw new NotSupportedException(
                    "The prototype supports one slab or two slabs connected by one operator.");
            }

            ClosureOperator closureOperator = m_Operators[0];
            if (closureOperator.Kind == ClosureOperatorKind.None)
                throw new ArgumentException("A dual-slab topology requires an operator.", nameof(m_Operators));
            if (closureOperator.BackgroundSlabIndex != 0 || closureOperator.ForegroundSlabIndex != 1)
            {
                throw new ArgumentException(
                    "The prototype requires the base slab at index 0 and top slab at index 1.",
                    nameof(m_Operators));
            }
            RequireValueType(closureOperator.Weight, MaterialValueType.Float, "operator weight");
            if (m_Slabs[0].IsTop || !m_Slabs[0].IsBottom)
                throw new ArgumentException("The base slab must be marked as bottom only.", nameof(m_Slabs));
            if (!m_Slabs[1].IsTop || m_Slabs[1].IsBottom)
                throw new ArgumentException("The top slab must be marked as top only.", nameof(m_Slabs));
        }

        private void RequireValueType(
            MaterialValue value,
            MaterialValueType expectedType,
            string description)
        {
            if (!ValueIR.Owns(value))
                throw new ArgumentException($"The {description} is not owned by the topology value IR.");
            if (value.Type != expectedType)
            {
                throw new ArgumentException(
                    $"The {description} must be {expectedType}, got {value.Type}.");
            }
        }
    }
}
