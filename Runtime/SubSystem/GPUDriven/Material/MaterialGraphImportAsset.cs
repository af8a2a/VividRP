using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    public sealed class MaterialGraphImportAsset : ScriptableObject
    {
        [SerializeField]
        private bool m_Succeeded;

        [SerializeField]
        private uint m_ProgramVersion;

        [SerializeField]
        private string m_SemanticHash;

        [SerializeField]
        private string m_CompiledHash;

        [SerializeField]
        private bool m_IsCataloged;

        [SerializeField]
        private uint m_ProgramID = (uint) VividMaterialProgramID.Invalid;

        [SerializeField]
        private uint m_CatalogManifestHashVersion;

        [SerializeField]
        private ulong m_CatalogManifestHash;

        [SerializeField]
        private uint m_CompiledHashVersion;

        [SerializeField]
        private ulong m_CompiledHashValue;

        [SerializeField]
        private uint m_LayoutFingerprintVersion;

        [SerializeField]
        private ulong m_LayoutFingerprint;

        [SerializeField]
        private uint m_ArtifactSetHashVersion;

        [SerializeField]
        private ulong m_ArtifactSetHash;

        [SerializeField]
        private string[] m_Diagnostics = Array.Empty<string>();

        [SerializeField]
        private uint m_ContentVersion;

        internal bool Succeeded => m_Succeeded;

        internal uint ProgramVersion => m_ProgramVersion;

        internal string SemanticHash => m_SemanticHash;

        internal string CompiledHash => m_CompiledHash;

        internal bool IsCataloged => m_IsCataloged;

        internal VividMaterialProgramID ProgramID =>
            (VividMaterialProgramID) m_ProgramID;

        internal MaterialProgramCatalogManifestHash CatalogManifestHash =>
            new MaterialProgramCatalogManifestHash(
                m_CatalogManifestHashVersion,
                m_CatalogManifestHash);

        internal CompiledMaterialProgramHash CompiledProgramHash =>
            new CompiledMaterialProgramHash(
                m_CompiledHashVersion,
                m_CompiledHashValue);

        internal MaterialProgramLayoutFingerprint LayoutFingerprint =>
            new MaterialProgramLayoutFingerprint(
                m_LayoutFingerprintVersion,
                m_LayoutFingerprint);

        internal MaterialProgramArtifactSetHash ArtifactSetHash =>
            new MaterialProgramArtifactSetHash(
                m_ArtifactSetHashVersion,
                m_ArtifactSetHash);

        internal IReadOnlyList<string> Diagnostics => m_Diagnostics;

        internal uint ContentVersion => m_ContentVersion;

        internal void Apply(
            MaterialGraphCompilationResult result,
            uint programVersion,
            MaterialProgramCatalog catalog)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            MaterialProgramCatalog.ManifestEntry catalogEntry = null;
            bool isCataloged = result.Program != null
                && catalog.TryGetCatalogedProgram(result.Program, out catalogEntry);
            Apply(
                result,
                programVersion,
                catalog.ManifestHash,
                MaterialProgramArtifactSetHashBuilder.Compute(
                    catalog.ManifestHash),
                isCataloged,
                catalogEntry?.ProgramID ?? VividMaterialProgramID.Invalid);
        }

        internal void Apply(
            MaterialGraphCompilationResult result,
            uint programVersion,
            MaterialProgramCatalogAsset frozenCatalog)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (frozenCatalog == null)
                throw new ArgumentNullException(nameof(frozenCatalog));
            if (!frozenCatalog.IsCommitted
                || !frozenCatalog.ArtifactSetHash.IsValid)
            {
                throw new InvalidOperationException(
                    "Frozen Material Program Catalog has not committed a valid artifact set.");
            }

            MaterialProgramCatalogAsset.Slot match = null;
            if (result.Program != null)
            {
                for (int slotIndex = 0;
                     slotIndex < frozenCatalog.Slots.Count;
                     slotIndex++)
                {
                    MaterialProgramCatalogAsset.Slot candidate =
                        frozenCatalog.Slots[slotIndex];
                    if (candidate == null
                        || !candidate.MatchesCompiledProgram(result.Program))
                    {
                        continue;
                    }
                    if (match != null)
                    {
                        throw new InvalidOperationException(
                            "Frozen Material Program Catalog contains duplicate compiled payloads.");
                    }
                    match = candidate;
                }
            }
            Apply(
                result,
                programVersion,
                frozenCatalog.ManifestHash,
                frozenCatalog.ArtifactSetHash,
                match != null,
                match?.ProgramID ?? VividMaterialProgramID.Invalid);
        }

        internal void ApplyFailure(uint programVersion, string diagnostic)
        {
            if (string.IsNullOrEmpty(diagnostic))
                throw new ArgumentException(
                    "A failure diagnostic is required.",
                    nameof(diagnostic));

            m_Succeeded = false;
            m_ProgramVersion = programVersion;
            m_SemanticHash = string.Empty;
            m_CompiledHash = string.Empty;
            m_IsCataloged = false;
            m_ProgramID = (uint) VividMaterialProgramID.Invalid;
            m_CatalogManifestHashVersion = 0u;
            m_CatalogManifestHash = 0ul;
            m_CompiledHashVersion = 0u;
            m_CompiledHashValue = 0ul;
            m_LayoutFingerprintVersion = 0u;
            m_LayoutFingerprint = 0ul;
            m_ArtifactSetHashVersion = 0u;
            m_ArtifactSetHash = 0ul;
            m_Diagnostics = new[] { diagnostic };
            RecomputeContentVersion();
        }

        private void Apply(
            MaterialGraphCompilationResult result,
            uint programVersion,
            in MaterialProgramCatalogManifestHash manifestHash,
            in MaterialProgramArtifactSetHash artifactSetHash,
            bool isCataloged,
            VividMaterialProgramID programID)
        {
            m_Succeeded = result.Succeeded;
            m_ProgramVersion = programVersion;
            m_SemanticHash = result.Program != null
                ? result.Program.SemanticHash.ToString()
                : string.Empty;
            m_CompiledHash = result.Program != null
                ? result.Program.CompiledHash.ToString()
                : string.Empty;

            MaterialProgramCatalogManifestHash effectiveManifestHash =
                result.Succeeded ? manifestHash : default;
            m_CatalogManifestHashVersion = effectiveManifestHash.Version;
            m_CatalogManifestHash = effectiveManifestHash.Value;
            m_IsCataloged = result.Program != null && isCataloged;
            m_ProgramID = m_IsCataloged
                ? (uint) programID
                : (uint) VividMaterialProgramID.Invalid;

            CompiledMaterialProgramHash compiledHash = result.Program != null
                ? result.Program.CompiledHash
                : default;
            m_CompiledHashVersion = compiledHash.Version;
            m_CompiledHashValue = compiledHash.Value;
            MaterialProgramLayoutFingerprint layoutFingerprint =
                result.Program != null
                    ? result.Program.Lowering.LayoutFingerprint
                    : default;
            m_LayoutFingerprintVersion = layoutFingerprint.Version;
            m_LayoutFingerprint = layoutFingerprint.Value;
            MaterialProgramArtifactSetHash effectiveArtifactSetHash =
                result.Succeeded ? artifactSetHash : default;
            m_ArtifactSetHashVersion = effectiveArtifactSetHash.Version;
            m_ArtifactSetHash = effectiveArtifactSetHash.Value;

            int diagnosticCount = result.Diagnostics.Count
                + (result.Succeeded && !m_IsCataloged ? 1 : 0);
            m_Diagnostics = new string[diagnosticCount];
            for (int diagnosticIndex = 0;
                 diagnosticIndex < result.Diagnostics.Count;
                 diagnosticIndex++)
            {
                MaterialGraphDiagnostic diagnostic = result.Diagnostics[diagnosticIndex];
                string location = string.IsNullOrEmpty(diagnostic.SourceNodeId)
                    ? string.Empty
                    : $" {diagnostic.SourceNodeId}.{diagnostic.SourcePort}";
                m_Diagnostics[diagnosticIndex] =
                    $"{diagnostic.Code}{location}: {diagnostic.Message}";
            }
            if (result.Succeeded && !m_IsCataloged)
            {
                m_Diagnostics[diagnosticCount - 1] =
                    "MAT-CATALOG: Compiled program is not present in the frozen Material Program Catalog.";
            }

            RecomputeContentVersion();
        }

        private void RecomputeContentVersion()
        {
            ulong contentHash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(ref contentHash, m_Succeeded);
            MaterialProgramHashUtility.Add(ref contentHash, m_ProgramVersion);
            MaterialProgramHashUtility.Add(ref contentHash, m_IsCataloged);
            MaterialProgramHashUtility.Add(ref contentHash, m_ProgramID);
            MaterialProgramHashUtility.Add(
                ref contentHash,
                m_CatalogManifestHashVersion);
            MaterialProgramHashUtility.Add(ref contentHash, m_CatalogManifestHash);
            MaterialProgramHashUtility.Add(ref contentHash, m_CompiledHashVersion);
            MaterialProgramHashUtility.Add(ref contentHash, m_CompiledHashValue);
            MaterialProgramHashUtility.Add(
                ref contentHash,
                m_LayoutFingerprintVersion);
            MaterialProgramHashUtility.Add(ref contentHash, m_LayoutFingerprint);
            MaterialProgramHashUtility.Add(
                ref contentHash,
                m_ArtifactSetHashVersion);
            MaterialProgramHashUtility.Add(ref contentHash, m_ArtifactSetHash);
            int diagnosticCount = m_Diagnostics?.Length ?? 0;
            MaterialProgramHashUtility.Add(ref contentHash, diagnosticCount);
            for (int diagnosticIndex = 0;
                 diagnosticIndex < diagnosticCount;
                 diagnosticIndex++)
            {
                MaterialProgramHashUtility.Add(
                    ref contentHash,
                    m_Diagnostics[diagnosticIndex] ?? string.Empty);
            }
            m_ContentVersion = (uint) (contentHash ^ (contentHash >> 32));
            if (m_ContentVersion == 0u)
                m_ContentVersion = 1u;
        }
    }
}
