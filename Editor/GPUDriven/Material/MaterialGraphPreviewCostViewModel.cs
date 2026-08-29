using System;
using System.Collections.Generic;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    internal enum MaterialGraphPreviewStatus
    {
        CompileError,
        OverBudget,
        CatalogMiss,
        Ready,
    }

    internal readonly struct MaterialGraphCostMetric
    {
        internal MaterialGraphCostMetric(
            string name,
            int actual,
            int maximum)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Actual = actual;
            Maximum = maximum;
        }

        internal string Name { get; }

        internal int Actual { get; }

        internal int Maximum { get; }

        internal bool IsExceeded => Actual > Maximum;
    }

    internal readonly struct MaterialGraphStageCostSummary
    {
        internal MaterialGraphStageCostSummary(
            string name,
            in MaterialStageCost cost)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            NodeCount = cost.ValueNodeCount;
            TextureSampleCount = cost.TextureSampleCount;
            DerivativeCount = cost.DerivativeCount;
            ArithmeticNodeCount = cost.ArithmeticNodeCount;
        }

        internal string Name { get; }

        internal int NodeCount { get; }

        internal int TextureSampleCount { get; }

        internal int DerivativeCount { get; }

        internal int ArithmeticNodeCount { get; }
    }

    internal sealed class MaterialGraphPreviewCostViewModel
    {
        private readonly IReadOnlyList<MaterialGraphCostMetric> m_Metrics;
        private readonly IReadOnlyList<MaterialGraphStageCostSummary> m_Stages;
        private readonly IReadOnlyList<string> m_Diagnostics;

        private MaterialGraphPreviewCostViewModel(
            MaterialGraphPreviewStatus status,
            VividMaterialProgramID programID,
            string semanticHash,
            string compiledHash,
            int closureCount,
            int operatorCount,
            MaterialGraphCostMetric[] metrics,
            MaterialGraphStageCostSummary[] stages,
            string[] diagnostics)
        {
            Status = status;
            ProgramID = programID;
            SemanticHash = semanticHash ?? string.Empty;
            CompiledHash = compiledHash ?? string.Empty;
            ClosureCount = closureCount;
            OperatorCount = operatorCount;
            m_Metrics = Array.AsReadOnly(metrics ?? Array.Empty<MaterialGraphCostMetric>());
            m_Stages = Array.AsReadOnly(stages ?? Array.Empty<MaterialGraphStageCostSummary>());
            m_Diagnostics = Array.AsReadOnly(diagnostics ?? Array.Empty<string>());
        }

        internal MaterialGraphPreviewStatus Status { get; }

        internal VividMaterialProgramID ProgramID { get; }

        internal string SemanticHash { get; }

        internal string CompiledHash { get; }

        internal int ClosureCount { get; }

        internal int OperatorCount { get; }

        internal IReadOnlyList<MaterialGraphCostMetric> Metrics => m_Metrics;

        internal IReadOnlyList<MaterialGraphStageCostSummary> Stages => m_Stages;

        internal IReadOnlyList<string> Diagnostics => m_Diagnostics;

        internal bool CanPreview => Status == MaterialGraphPreviewStatus.Ready;

        internal string Signature
        {
            get
            {
                string metricSignature = string.Empty;
                for (int metricIndex = 0; metricIndex < m_Metrics.Count; metricIndex++)
                {
                    MaterialGraphCostMetric metric = m_Metrics[metricIndex];
                    metricSignature += $"|{metric.Actual}/{metric.Maximum}";
                }

                string diagnosticSignature = string.Join("|", m_Diagnostics);
                return $"{Status}:{(uint) ProgramID}:{CompiledHash}:{metricSignature}:{diagnosticSignature}";
            }
        }

        internal static MaterialGraphPreviewCostViewModel Build(
            MaterialGraphEditorGraph graph)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            return Build(
                MaterialGraphEditorCompiler.Compile(graph),
                GPUDrivenMaterialCompiler.ProgramCatalog,
                GPUDrivenMaterialCompiler.ProgramVersion);
        }

        internal static MaterialGraphPreviewCostViewModel Build(
            MaterialGraphCompilationResult result,
            MaterialProgramCatalog catalog,
            uint programVersion)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            MaterialProgramDiagnostics programDiagnostics =
                TryGetProgramDiagnostics(result, programVersion);
            MaterialGraphPreviewStatus status;
            MaterialProgramCatalog.ManifestEntry catalogEntry = null;
            if (!result.Succeeded)
            {
                status = programDiagnostics != null
                    && !programDiagnostics.IsWithinBudget
                        ? MaterialGraphPreviewStatus.OverBudget
                        : MaterialGraphPreviewStatus.CompileError;
            }
            else if (!catalog.TryGetCatalogedProgram(result.Program, out catalogEntry))
            {
                status = MaterialGraphPreviewStatus.CatalogMiss;
            }
            else
            {
                status = MaterialGraphPreviewStatus.Ready;
            }

            MaterialGraphCostMetric[] metrics = programDiagnostics != null
                ? CreateMetrics(programDiagnostics)
                : Array.Empty<MaterialGraphCostMetric>();
            MaterialGraphStageCostSummary[] stages = programDiagnostics != null
                ? CreateStageSummaries(programDiagnostics.Cost)
                : Array.Empty<MaterialGraphStageCostSummary>();

            var diagnostics = new List<string>();
            for (int diagnosticIndex = 0;
                 diagnosticIndex < result.Diagnostics.Count;
                 diagnosticIndex++)
            {
                MaterialGraphDiagnostic diagnostic = result.Diagnostics[diagnosticIndex];
                string location = string.IsNullOrEmpty(diagnostic.SourceNodeId)
                    ? string.Empty
                    : $" {diagnostic.SourceNodeId}.{diagnostic.SourcePort}";
                diagnostics.Add(
                    $"{diagnostic.Severity}: {diagnostic.Code}{location}: {diagnostic.Message}");
            }
            if (status == MaterialGraphPreviewStatus.CatalogMiss)
            {
                diagnostics.Add(
                    "Compiled program is not present in the Frozen Catalog; preview dispatch is unavailable until the catalog is baked.");
            }

            MaterialIRModule module = result.Program?.Module ?? result.Module;
            return new MaterialGraphPreviewCostViewModel(
                status,
                catalogEntry?.ProgramID ?? VividMaterialProgramID.Invalid,
                result.Program?.SemanticHash.ToString(),
                result.Program?.CompiledHash.ToString(),
                module?.Topology.ClosureCount ?? 0,
                module?.Topology.OperatorCount ?? 0,
                metrics,
                stages,
                diagnostics.ToArray());
        }

        private static MaterialProgramDiagnostics TryGetProgramDiagnostics(
            MaterialGraphCompilationResult result,
            uint programVersion)
        {
            if (result.Program != null)
                return result.Program.Diagnostics;
            if (result.Module == null)
                return null;

            try
            {
                MaterialProgramLoweringResult lowering = MaterialProgramLowerer.Lower(
                    result.Module,
                    programVersion,
                    MaterialProgramBuiltinCatalog.Templates);
                return MaterialProgramDiagnosticsBuilder.Build(
                    result.Module,
                    lowering.CoverageProgram,
                    lowering.SurfaceProgram,
                    lowering.MaterialLayout,
                    MaterialProgramCostBudget.Prototype);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static MaterialGraphCostMetric[] CreateMetrics(
            MaterialProgramDiagnostics diagnostics)
        {
            MaterialProgramCost cost = diagnostics.Cost;
            MaterialProgramCostBudget budget = diagnostics.Budget;
            return new[]
            {
                new MaterialGraphCostMetric(
                    "Combined IR nodes",
                    cost.Combined.ValueNodeCount,
                    budget.MaxCombinedValueNodes),
                new MaterialGraphCostMetric(
                    "Coverage texture samples",
                    cost.WorstCaseCoverageTextureSamples,
                    budget.MaxCoverageTextureSamples),
                new MaterialGraphCostMetric(
                    "Surface texture samples",
                    cost.WorstCaseSurfaceTextureSamples,
                    budget.MaxSurfaceTextureSamples),
                new MaterialGraphCostMetric(
                    "Total texture samples",
                    cost.WorstCaseTotalTextureSamples,
                    budget.MaxTotalTextureSamples),
                new MaterialGraphCostMetric(
                    "Parameter bindings",
                    cost.ParameterBindingCount,
                    budget.MaxParameterBindings),
                new MaterialGraphCostMetric(
                    "Resource bindings",
                    cost.ResourceBindingCount,
                    budget.MaxResourceBindings),
                new MaterialGraphCostMetric(
                    "Closures",
                    cost.ClosureCount,
                    budget.MaxClosures),
                new MaterialGraphCostMetric(
                    "Closure operators",
                    cost.OperatorCount,
                    budget.MaxOperators),
                new MaterialGraphCostMetric(
                    "Parameter bytes",
                    cost.ParameterBytes,
                    budget.MaxParameterBytes),
                new MaterialGraphCostMetric(
                    "Resource records",
                    cost.ResourceBindingRecords,
                    budget.MaxResourceBindingRecords),
            };
        }

        private static MaterialGraphStageCostSummary[] CreateStageSummaries(
            in MaterialProgramCost cost)
        {
            return new[]
            {
                new MaterialGraphStageCostSummary("Coverage", cost.Coverage),
                new MaterialGraphStageCostSummary("Surface", cost.Surface),
            };
        }
    }
}
