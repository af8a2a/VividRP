using System;
using System.Collections.Generic;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    internal sealed class MaterialGraphImportAsset : ScriptableObject
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
        private string[] m_Diagnostics = Array.Empty<string>();

        [SerializeField]
        private long m_ImportVersion;

        internal bool Succeeded => m_Succeeded;

        internal uint ProgramVersion => m_ProgramVersion;

        internal string SemanticHash => m_SemanticHash;

        internal string CompiledHash => m_CompiledHash;

        internal IReadOnlyList<string> Diagnostics => m_Diagnostics;

        internal void Apply(MaterialGraphCompilationResult result, uint programVersion)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            m_Succeeded = result.Succeeded;
            m_ProgramVersion = programVersion;
            m_SemanticHash = result.Program != null
                ? result.Program.SemanticHash.ToString()
                : string.Empty;
            m_CompiledHash = result.Program != null
                ? result.Program.CompiledHash.ToString()
                : string.Empty;
            m_Diagnostics = new string[result.Diagnostics.Count];
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
            m_ImportVersion = DateTime.UtcNow.Ticks;
        }
    }
}
