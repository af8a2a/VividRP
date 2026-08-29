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

            m_Succeeded = result.Succeeded;
            m_ProgramVersion = programVersion;
            m_SemanticHash = result.Program != null
                ? result.Program.SemanticHash.ToString()
                : string.Empty;
            m_CompiledHash = result.Program != null
                ? result.Program.CompiledHash.ToString()
                : string.Empty;

            MaterialProgramCatalogManifestHash manifestHash = catalog.ManifestHash;
            m_CatalogManifestHashVersion = manifestHash.Version;
            m_CatalogManifestHash = manifestHash.Value;
            MaterialProgramCatalog.ManifestEntry catalogEntry = null;
            m_IsCataloged = result.Program != null
                && catalog.TryGetCatalogedProgram(result.Program, out catalogEntry);
            m_ProgramID = catalogEntry != null
                ? (uint) catalogEntry.ProgramID
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
            m_ContentVersion = (uint) (contentHash ^ (contentHash >> 32));
            if (m_ContentVersion == 0u)
                m_ContentVersion = 1u;
        }
    }
}
