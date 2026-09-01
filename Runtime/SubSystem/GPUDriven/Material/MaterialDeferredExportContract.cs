using System;
using System.Globalization;

namespace VividRP.Runtime.GPUDriven
{
    internal enum MaterialDeferredExportSurfaceSummaryAbi : uint
    {
        SurfaceSummaryV1 = 1u,
    }

    internal enum MaterialDeferredExportSidecarAbi : uint
    {
        None = 0u,
        DualSlabV1 = 1u,
    }

    internal enum MaterialDeferredExportLitClass : uint
    {
        None = 0u,
        FastSlab = 2u,
        DualSlab = 4u,
    }

    internal enum MaterialDeferredExportTopology : uint
    {
        None = 0u,
        HorizontalMix = 1u,
        VerticalLayer = 2u,
    }

    [Flags]
    internal enum MaterialDeferredExportPayloadFlags : uint
    {
        None = 0u,
        SurfaceSummary = 1u << 0,
        DiffuseIrradiance = 1u << 1,
        DualSlabSidecar = 1u << 2,
        SharedNormalAndAmbientOcclusion = 1u << 3,
    }

    [Flags]
    internal enum MaterialDeferredExportPolicyFlags : uint
    {
        None = 0u,
        DynamicDiffuseIrradiance = 1u << 0,
        ReceiveSsrOnFastSlab = 1u << 1,
        ReceiveDecals = 1u << 2,
        FastSlabWhenSidecarEmpty = 1u << 3,
    }

    internal readonly struct MaterialDeferredExportContractFingerprint :
        IEquatable<MaterialDeferredExportContractFingerprint>
    {
        internal MaterialDeferredExportContractFingerprint(uint version, ulong value)
        {
            Version = version;
            Value = value;
        }

        internal uint Version { get; }

        internal ulong Value { get; }

        public bool Equals(MaterialDeferredExportContractFingerprint other)
        {
            return Version == other.Version && Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialDeferredExportContractFingerprint other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int) Version * 397) ^ (int) Value ^ (int) (Value >> 32);
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "deferred_export_v={0} 0x{1:X16}",
                Version,
                Value);
        }

        public static bool operator ==(
            MaterialDeferredExportContractFingerprint left,
            MaterialDeferredExportContractFingerprint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            MaterialDeferredExportContractFingerprint left,
            MaterialDeferredExportContractFingerprint right)
        {
            return !left.Equals(right);
        }
    }

    internal sealed class MaterialDeferredExportContract
    {
        internal MaterialDeferredExportContract(
            MaterialDeferredExportSurfaceSummaryAbi surfaceSummaryAbi,
            MaterialDeferredExportSidecarAbi dualSlabSidecarAbi,
            MaterialShadingModelMask shadingModels,
            MaterialDeferredExportLitClass litClass,
            uint expectedClosureCount,
            MaterialDeferredExportTopology topology,
            MaterialDeferredExportPayloadFlags payloadFlags,
            MaterialDeferredExportPolicyFlags policyFlags)
        {
            Version = MaterialProgramContract.DeferredExportContractVersion;
            SurfaceSummaryAbi = surfaceSummaryAbi;
            DualSlabSidecarAbi = dualSlabSidecarAbi;
            ShadingModels = shadingModels;
            LitClass = litClass;
            ExpectedClosureCount = expectedClosureCount;
            Topology = topology;
            PayloadFlags = payloadFlags;
            PolicyFlags = policyFlags;
            Validate();
            Fingerprint = MaterialDeferredExportContractHashBuilder.Compute(this);
        }

        internal uint Version { get; }

        internal MaterialDeferredExportSurfaceSummaryAbi SurfaceSummaryAbi { get; }

        internal MaterialDeferredExportSidecarAbi DualSlabSidecarAbi { get; }

        internal MaterialShadingModelMask ShadingModels { get; }

        internal MaterialDeferredExportLitClass LitClass { get; }

        internal uint ExpectedClosureCount { get; }

        internal MaterialDeferredExportTopology Topology { get; }

        internal MaterialDeferredExportPayloadFlags PayloadFlags { get; }

        internal MaterialDeferredExportPolicyFlags PolicyFlags { get; }

        internal MaterialDeferredExportContractFingerprint Fingerprint { get; }

        internal bool PayloadEquals(MaterialDeferredExportContract other)
        {
            return ReferenceEquals(this, other)
                || other != null
                && Version == other.Version
                && SurfaceSummaryAbi == other.SurfaceSummaryAbi
                && DualSlabSidecarAbi == other.DualSlabSidecarAbi
                && ShadingModels == other.ShadingModels
                && LitClass == other.LitClass
                && ExpectedClosureCount == other.ExpectedClosureCount
                && Topology == other.Topology
                && PayloadFlags == other.PayloadFlags
                && PolicyFlags == other.PolicyFlags;
        }

        private void Validate()
        {
            if (SurfaceSummaryAbi
                != MaterialDeferredExportSurfaceSummaryAbi.SurfaceSummaryV1)
            {
                throw new ArgumentOutOfRangeException(nameof(SurfaceSummaryAbi));
            }

            const MaterialShadingModelMask knownShadingModels =
                MaterialShadingModelMask.StandardLit
                | MaterialShadingModelMask.Unlit;
            if (ShadingModels == MaterialShadingModelMask.None
                || (ShadingModels & ~knownShadingModels) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ShadingModels));
            }

            if (Topology != MaterialDeferredExportTopology.None
                && Topology != MaterialDeferredExportTopology.HorizontalMix
                && Topology != MaterialDeferredExportTopology.VerticalLayer)
            {
                throw new ArgumentOutOfRangeException(nameof(Topology));
            }
            if (DualSlabSidecarAbi != MaterialDeferredExportSidecarAbi.None
                && DualSlabSidecarAbi
                    != MaterialDeferredExportSidecarAbi.DualSlabV1)
            {
                throw new ArgumentOutOfRangeException(nameof(DualSlabSidecarAbi));
            }

            bool isDualTopology = Topology != MaterialDeferredExportTopology.None;
            if (ExpectedClosureCount != (isDualTopology ? 2u : 1u))
                throw new ArgumentOutOfRangeException(nameof(ExpectedClosureCount));

            const MaterialDeferredExportPayloadFlags corePayload =
                MaterialDeferredExportPayloadFlags.SurfaceSummary
                | MaterialDeferredExportPayloadFlags.DiffuseIrradiance;
            const MaterialDeferredExportPayloadFlags knownPayload =
                corePayload
                | MaterialDeferredExportPayloadFlags.DualSlabSidecar
                | MaterialDeferredExportPayloadFlags.SharedNormalAndAmbientOcclusion;
            if ((PayloadFlags & corePayload) != corePayload
                || (PayloadFlags & ~knownPayload) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(PayloadFlags));
            }

            const MaterialDeferredExportPolicyFlags knownPolicy =
                MaterialDeferredExportPolicyFlags.DynamicDiffuseIrradiance
                | MaterialDeferredExportPolicyFlags.ReceiveSsrOnFastSlab
                | MaterialDeferredExportPolicyFlags.ReceiveDecals
                | MaterialDeferredExportPolicyFlags.FastSlabWhenSidecarEmpty;
            if ((PolicyFlags & ~knownPolicy) != 0
                || (PolicyFlags & MaterialDeferredExportPolicyFlags.ReceiveDecals) == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(PolicyFlags));
            }

            bool supportsLit =
                (ShadingModels & MaterialShadingModelMask.StandardLit) != 0;
            MaterialDeferredExportLitClass expectedLitClass = supportsLit
                ? isDualTopology
                    ? MaterialDeferredExportLitClass.DualSlab
                    : MaterialDeferredExportLitClass.FastSlab
                : MaterialDeferredExportLitClass.None;
            if (LitClass != expectedLitClass)
                throw new ArgumentException("Deferred Export lit class does not match its shading models and topology.");

            bool hasDualSidecar =
                (PayloadFlags & MaterialDeferredExportPayloadFlags.DualSlabSidecar) != 0;
            bool sharesNormalAndAmbientOcclusion =
                (PayloadFlags
                    & MaterialDeferredExportPayloadFlags.SharedNormalAndAmbientOcclusion) != 0;
            bool expectsDualSidecar = supportsLit && isDualTopology;
            if (hasDualSidecar != expectsDualSidecar
                || sharesNormalAndAmbientOcclusion != isDualTopology
                || (DualSlabSidecarAbi
                        == MaterialDeferredExportSidecarAbi.DualSlabV1)
                    != expectsDualSidecar)
            {
                throw new ArgumentException("Deferred Export sidecar ABI does not match its payload.");
            }

            const MaterialDeferredExportPolicyFlags litPolicies =
                MaterialDeferredExportPolicyFlags.DynamicDiffuseIrradiance
                | MaterialDeferredExportPolicyFlags.ReceiveSsrOnFastSlab;
            MaterialDeferredExportPolicyFlags activeLitPolicies =
                PolicyFlags & litPolicies;
            bool hasFastFallback =
                (PolicyFlags
                    & MaterialDeferredExportPolicyFlags.FastSlabWhenSidecarEmpty) != 0;
            if (activeLitPolicies != (supportsLit
                    ? litPolicies
                    : MaterialDeferredExportPolicyFlags.None)
                || hasFastFallback != expectsDualSidecar)
            {
                throw new ArgumentException("Deferred Export policies do not match its lit payload.");
            }
        }
    }

    internal static class MaterialDeferredExportContractLowerer
    {
        internal static MaterialDeferredExportContract Compile(
            MaterialIRModule module,
            MaterialProgramTopologySpecialization topology)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            MaterialDeferredExportTopology exportTopology;
            uint expectedClosureCount;
            switch (topology)
            {
                case MaterialProgramTopologySpecialization.SingleSlab:
                    exportTopology = MaterialDeferredExportTopology.None;
                    expectedClosureCount = 1u;
                    break;
                case MaterialProgramTopologySpecialization.HorizontalMix:
                    exportTopology = MaterialDeferredExportTopology.HorizontalMix;
                    expectedClosureCount = 2u;
                    break;
                case MaterialProgramTopologySpecialization.VerticalLayer:
                    exportTopology = MaterialDeferredExportTopology.VerticalLayer;
                    expectedClosureCount = 2u;
                    break;
                default:
                    throw new NotSupportedException(
                        $"Topology '{topology}' cannot be exported to deferred shading.");
            }

            if (module.Topology.ClosureCount != expectedClosureCount)
            {
                throw new NotSupportedException(
                    "Deferred Export topology does not match the compiled closure count.");
            }

            bool supportsLit =
                (module.ShadingModels & MaterialShadingModelMask.StandardLit) != 0;
            bool exportsDualSlab = supportsLit && expectedClosureCount == 2u;
            MaterialDeferredExportPayloadFlags payloadFlags =
                MaterialDeferredExportPayloadFlags.SurfaceSummary
                | MaterialDeferredExportPayloadFlags.DiffuseIrradiance;
            MaterialDeferredExportPolicyFlags policyFlags =
                MaterialDeferredExportPolicyFlags.ReceiveDecals;
            if (supportsLit)
            {
                policyFlags |=
                    MaterialDeferredExportPolicyFlags.DynamicDiffuseIrradiance
                    | MaterialDeferredExportPolicyFlags.ReceiveSsrOnFastSlab;
            }
            if (expectedClosureCount == 2u)
            {
                payloadFlags |=
                    MaterialDeferredExportPayloadFlags.SharedNormalAndAmbientOcclusion;
            }
            if (exportsDualSlab)
            {
                payloadFlags |=
                    MaterialDeferredExportPayloadFlags.DualSlabSidecar;
                policyFlags |=
                    MaterialDeferredExportPolicyFlags.FastSlabWhenSidecarEmpty;
            }

            return new MaterialDeferredExportContract(
                MaterialDeferredExportSurfaceSummaryAbi.SurfaceSummaryV1,
                exportsDualSlab
                    ? MaterialDeferredExportSidecarAbi.DualSlabV1
                    : MaterialDeferredExportSidecarAbi.None,
                module.ShadingModels,
                supportsLit
                    ? exportsDualSlab
                        ? MaterialDeferredExportLitClass.DualSlab
                        : MaterialDeferredExportLitClass.FastSlab
                    : MaterialDeferredExportLitClass.None,
                expectedClosureCount,
                exportTopology,
                payloadFlags,
                policyFlags);
        }
    }

    internal static class MaterialDeferredExportContractHashBuilder
    {
        internal static MaterialDeferredExportContractFingerprint Compute(
            MaterialDeferredExportContract contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));

            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(
                ref hash,
                MaterialProgramContract.DeferredExportFingerprintVersion);
            AddPayload(ref hash, contract);
            return new MaterialDeferredExportContractFingerprint(
                MaterialProgramContract.DeferredExportFingerprintVersion,
                hash);
        }

        internal static void AddPayload(
            ref ulong hash,
            MaterialDeferredExportContract contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));

            MaterialProgramHashUtility.Add(ref hash, contract.Version);
            MaterialProgramHashUtility.Add(ref hash, (uint) contract.SurfaceSummaryAbi);
            MaterialProgramHashUtility.Add(ref hash, (uint) contract.DualSlabSidecarAbi);
            MaterialProgramHashUtility.Add(ref hash, (uint) contract.ShadingModels);
            MaterialProgramHashUtility.Add(ref hash, (uint) contract.LitClass);
            MaterialProgramHashUtility.Add(ref hash, contract.ExpectedClosureCount);
            MaterialProgramHashUtility.Add(ref hash, (uint) contract.Topology);
            MaterialProgramHashUtility.Add(ref hash, (uint) contract.PayloadFlags);
            MaterialProgramHashUtility.Add(ref hash, (uint) contract.PolicyFlags);
        }
    }
}
