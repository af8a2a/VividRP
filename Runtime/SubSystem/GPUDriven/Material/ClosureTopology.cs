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
    }

    internal enum ClosureOperatorKind
    {
        None = 0,
        HorizontalMix = 1,
        VerticalLayer = 2,
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
            MaterialIRVerifier.VerifyTopology(this).ThrowIfInvalid();
        }
    }
}
