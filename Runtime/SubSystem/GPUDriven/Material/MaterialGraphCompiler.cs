using System;
using System.Collections.Generic;

namespace VividRP.Runtime.GPUDriven
{
    internal static class MaterialGraphDiagnosticCodes
    {
        internal const string MissingOutput = "MATG1001";
        internal const string MultipleOutputs = "MATG1002";
        internal const string MissingNode = "MATG1003";
        internal const string ForeignReference = "MATG1004";
        internal const string Cycle = "MATG1005";
        internal const string InvalidNode = "MATG1006";
        internal const string BackendRejected = "MATG2001";
    }

    internal readonly struct MaterialGraphDiagnostic
    {
        internal MaterialGraphDiagnostic(
            MaterialIRDiagnosticSeverity severity,
            string code,
            string message,
            string sourceNodeId,
            string sourcePort)
        {
            Severity = severity;
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            SourceNodeId = sourceNodeId ?? string.Empty;
            SourcePort = sourcePort ?? string.Empty;
        }

        internal MaterialIRDiagnosticSeverity Severity { get; }

        internal string Code { get; }

        internal string Message { get; }

        internal string SourceNodeId { get; }

        internal string SourcePort { get; }
    }

    internal sealed class MaterialGraphProvenance
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<int>>
            m_CanonicalValueNodes;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<int>>
            m_CanonicalClosureNodes;

        internal MaterialGraphProvenance(
            Dictionary<string, HashSet<int>> canonicalValueNodes,
            Dictionary<string, HashSet<int>> canonicalClosureNodes)
        {
            m_CanonicalValueNodes = Freeze(canonicalValueNodes);
            m_CanonicalClosureNodes = Freeze(canonicalClosureNodes);
        }

        internal bool TryGetCanonicalValueNodes(
            string sourceNodeId,
            out IReadOnlyList<int> nodeIndices)
        {
            return m_CanonicalValueNodes.TryGetValue(sourceNodeId, out nodeIndices);
        }

        internal bool TryGetCanonicalClosureNodes(
            string sourceNodeId,
            out IReadOnlyList<int> nodeIndices)
        {
            return m_CanonicalClosureNodes.TryGetValue(sourceNodeId, out nodeIndices);
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<int>> Freeze(
            Dictionary<string, HashSet<int>> source)
        {
            var result = new Dictionary<string, IReadOnlyList<int>>(
                source.Count,
                StringComparer.Ordinal);
            foreach (KeyValuePair<string, HashSet<int>> pair in source)
            {
                var indices = new int[pair.Value.Count];
                pair.Value.CopyTo(indices);
                Array.Sort(indices);
                result.Add(pair.Key, Array.AsReadOnly(indices));
            }
            return result;
        }
    }

    internal sealed class MaterialGraphCompilationResult
    {
        private readonly IReadOnlyList<MaterialGraphDiagnostic> m_Diagnostics;

        internal MaterialGraphCompilationResult(
            CompiledMaterialProgram program,
            MaterialIRModule module,
            MaterialGraphProvenance provenance,
            MaterialGraphDiagnostic[] diagnostics)
        {
            Program = program;
            Module = module;
            Provenance = provenance;
            m_Diagnostics = Array.AsReadOnly(
                diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)));
        }

        internal CompiledMaterialProgram Program { get; }

        internal MaterialIRModule Module { get; }

        internal MaterialGraphProvenance Provenance { get; }

        internal IReadOnlyList<MaterialGraphDiagnostic> Diagnostics => m_Diagnostics;

        internal bool Succeeded => Program != null;
    }

    internal static class MaterialGraphCompiler
    {
        private const string OutputPort = "Out";

        internal static MaterialGraphCompilationResult Compile(
            MaterialGraph graph,
            uint programVersion)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            return new Compilation(graph, programVersion).Run();
        }

        private enum VisitState
        {
            Visiting,
            Complete,
            Failed,
        }

        private readonly struct SourceOrigin : IEquatable<SourceOrigin>
        {
            internal SourceOrigin(string nodeId, string port)
            {
                NodeId = nodeId ?? string.Empty;
                Port = port ?? string.Empty;
            }

            internal string NodeId { get; }

            internal string Port { get; }

            public bool Equals(SourceOrigin other)
            {
                return string.Equals(NodeId, other.NodeId, StringComparison.Ordinal)
                    && string.Equals(Port, other.Port, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is SourceOrigin other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (StringComparer.Ordinal.GetHashCode(NodeId) * 397)
                        ^ StringComparer.Ordinal.GetHashCode(Port);
                }
            }
        }

        private sealed class Compilation
        {
            private readonly MaterialGraph m_Graph;
            private readonly uint m_ProgramVersion;
            private readonly MaterialValueIR m_Values = new();
            private readonly ClosureExpressionGraph m_Closures;
            private readonly List<MaterialGraphDiagnostic> m_Diagnostics = new();
            private readonly Dictionary<string, VisitState> m_ValueStates =
                new(StringComparer.Ordinal);
            private readonly Dictionary<string, VisitState> m_ClosureStates =
                new(StringComparer.Ordinal);
            private readonly Dictionary<string, MaterialValue> m_CompiledValues =
                new(StringComparer.Ordinal);
            private readonly Dictionary<string, MaterialClosure> m_CompiledClosures =
                new(StringComparer.Ordinal);
            private readonly Dictionary<int, List<SourceOrigin>> m_ValueOrigins = new();
            private readonly Dictionary<int, SourceOrigin> m_ClosureOrigins = new();

            internal Compilation(MaterialGraph graph, uint programVersion)
            {
                m_Graph = graph;
                m_ProgramVersion = programVersion;
                m_Closures = new ClosureExpressionGraph(m_Values);
            }

            internal MaterialGraphCompilationResult Run()
            {
                if (m_Graph.OutputNodes.Count == 0)
                {
                    AddError(
                        MaterialGraphDiagnosticCodes.MissingOutput,
                        "Material graph requires exactly one Material Output node.",
                        string.Empty,
                        string.Empty);
                    return CreateResult(null, null, null);
                }
                if (m_Graph.OutputNodes.Count != 1)
                {
                    AddError(
                        MaterialGraphDiagnosticCodes.MultipleOutputs,
                        "Material graph contains more than one Material Output node.",
                        m_Graph.OutputNodes[1].NodeId,
                        string.Empty);
                    return CreateResult(null, null, null);
                }

                MaterialGraphOutputNode output = m_Graph.OutputNodes[0];
                bool hasSurface = TryCompileClosure(
                    output.Surface,
                    output.NodeId,
                    "Surface",
                    out MaterialClosure surface);
                bool hasCoverage = TryCompileValue(
                    output.Coverage,
                    output.NodeId,
                    "Coverage",
                    out MaterialValue coverage);
                bool hasThreshold = TryCompileValue(
                    output.AlphaClipThreshold,
                    output.NodeId,
                    "AlphaClipThreshold",
                    out MaterialValue threshold);
                bool hasEmission = TryCompileValue(
                    output.Emission,
                    output.NodeId,
                    "Emission",
                    out MaterialValue emission);
                if (!hasSurface || !hasCoverage || !hasThreshold || !hasEmission)
                    return CreateResult(null, null, null);

                MaterialIRModule module;
                try
                {
                    module = new MaterialIRModule(
                        m_Values,
                        new MaterialOutputRoots(coverage, threshold, emission),
                        m_Closures,
                        surface,
                        ClosureTopologyBudget.Prototype,
                        output.MaterialFeatures,
                        output.ShadingModels);
                }
                catch (MaterialIRVerificationException exception)
                {
                    AppendSourceDiagnostics(exception.Diagnostics, output.NodeId);
                    return CreateResult(null, null, null);
                }
                catch (InvalidOperationException exception)
                {
                    AddBackendError(exception.Message, output.NodeId);
                    return CreateResult(null, null, null);
                }
                catch (NotSupportedException exception)
                {
                    AddBackendError(exception.Message, output.NodeId);
                    return CreateResult(null, null, null);
                }

                MaterialGraphProvenance provenance = BuildProvenance(module);
                try
                {
                    CompiledMaterialProgram program = CompiledMaterialProgram.Compile(
                        module,
                        m_ProgramVersion);
                    return CreateResult(program, module, provenance);
                }
                catch (MaterialIRVerificationException exception)
                {
                    AppendCanonicalDiagnostics(
                        exception.Diagnostics,
                        module,
                        output.NodeId);
                }
                catch (ArgumentException exception)
                {
                    AddBackendError(exception.Message, output.NodeId);
                }
                catch (InvalidOperationException exception)
                {
                    AddBackendError(exception.Message, output.NodeId);
                }
                catch (NotSupportedException exception)
                {
                    AddBackendError(exception.Message, output.NodeId);
                }
                return CreateResult(null, module, provenance);
            }

            private bool TryCompileValue(
                MaterialGraphValue reference,
                string consumerNodeId,
                string consumerPort,
                out MaterialValue value)
            {
                value = default;
                if (!ValidateReference(
                        reference.Owner,
                        reference.NodeId,
                        consumerNodeId,
                        consumerPort,
                        "value"))
                {
                    return false;
                }
                if (!m_Graph.ValueNodes.TryGetValue(
                        reference.NodeId,
                        out MaterialGraphValueNode node))
                {
                    AddError(
                        MaterialGraphDiagnosticCodes.MissingNode,
                        $"Value node '{reference.NodeId}' does not exist.",
                        consumerNodeId,
                        consumerPort);
                    return false;
                }
                if (m_ValueStates.TryGetValue(node.NodeId, out VisitState state))
                {
                    if (state == VisitState.Complete)
                    {
                        value = m_CompiledValues[node.NodeId];
                        return true;
                    }
                    if (state == VisitState.Visiting)
                    {
                        AddError(
                            MaterialGraphDiagnosticCodes.Cycle,
                            $"Value graph cycle reaches node '{node.NodeId}'.",
                            consumerNodeId,
                            consumerPort);
                    }
                    return false;
                }

                m_ValueStates.Add(node.NodeId, VisitState.Visiting);
                var operands = new MaterialValue[node.Operands.Count];
                bool operandsValid = true;
                for (int operandIndex = 0;
                     operandIndex < node.Operands.Count;
                     operandIndex++)
                {
                    operandsValid &= TryCompileValue(
                        node.Operands[operandIndex],
                        node.NodeId,
                        GetValueInputPort(node.Opcode, operandIndex),
                        out operands[operandIndex]);
                }
                if (!operandsValid)
                {
                    m_ValueStates[node.NodeId] = VisitState.Failed;
                    return false;
                }

                try
                {
                    value = EmitValue(node, operands);
                    RecordValueOrigin(value, node.NodeId);
                    m_CompiledValues.Add(node.NodeId, value);
                    m_ValueStates[node.NodeId] = VisitState.Complete;
                    return true;
                }
                catch (MaterialIRVerificationException exception)
                {
                    AppendNodeDiagnostics(exception.Diagnostics, node.NodeId, OutputPort);
                }
                catch (ArgumentException exception)
                {
                    AddInvalidNodeError(node.NodeId, exception.Message);
                }
                catch (InvalidOperationException exception)
                {
                    AddInvalidNodeError(node.NodeId, exception.Message);
                }
                catch (NotSupportedException exception)
                {
                    AddInvalidNodeError(node.NodeId, exception.Message);
                }
                m_ValueStates[node.NodeId] = VisitState.Failed;
                return false;
            }

            private MaterialValue EmitValue(
                MaterialGraphValueNode node,
                MaterialValue[] operands)
            {
                switch (node.Opcode)
                {
                    case MaterialGraphValueOpcode.Constant:
                        return EmitConstant(node);
                    case MaterialGraphValueOpcode.ExternalInput:
                        return m_Values.ExternalInput((MaterialExternalInput) node.Semantic);
                    case MaterialGraphValueOpcode.Parameter:
                        return m_Values.Parameter(node.ParameterDeclaration);
                    case MaterialGraphValueOpcode.TextureResource:
                        return m_Values.TextureResource(node.ResourceDeclaration);
                    case MaterialGraphValueOpcode.TextureSample:
                    {
                        MaterialValue ddx = m_Values.Ddx(operands[1]);
                        RecordValueOrigin(ddx, node.NodeId);
                        MaterialValue ddy = m_Values.Ddy(operands[1]);
                        RecordValueOrigin(ddy, node.NodeId);
                        return m_Values.TextureSampleGrad(
                            operands[0],
                            operands[1],
                            ddx,
                            ddy);
                    }
                    case MaterialGraphValueOpcode.TextureSampleGrad:
                        return m_Values.TextureSampleGrad(
                            operands[0],
                            operands[1],
                            operands[2],
                            operands[3]);
                    case MaterialGraphValueOpcode.Ddx:
                        return m_Values.Ddx(operands[0]);
                    case MaterialGraphValueOpcode.Ddy:
                        return m_Values.Ddy(operands[0]);
                    case MaterialGraphValueOpcode.Add:
                        return m_Values.Add(operands[0], operands[1]);
                    case MaterialGraphValueOpcode.Multiply:
                        return m_Values.Multiply(operands[0], operands[1]);
                    case MaterialGraphValueOpcode.Subtract:
                        return m_Values.Subtract(operands[0], operands[1]);
                    case MaterialGraphValueOpcode.Divide:
                        return m_Values.Divide(operands[0], operands[1]);
                    case MaterialGraphValueOpcode.Min:
                        return m_Values.Min(operands[0], operands[1]);
                    case MaterialGraphValueOpcode.Max:
                        return m_Values.Max(operands[0], operands[1]);
                    case MaterialGraphValueOpcode.Saturate:
                        return m_Values.Saturate(operands[0]);
                    case MaterialGraphValueOpcode.OneMinus:
                        return m_Values.OneMinus(operands[0]);
                    case MaterialGraphValueOpcode.Dot:
                        return m_Values.Dot(operands[0], operands[1]);
                    case MaterialGraphValueOpcode.Normalize:
                        return m_Values.Normalize(operands[0]);
                    case MaterialGraphValueOpcode.Lerp:
                        return m_Values.Lerp(operands[0], operands[1], operands[2]);
                    case MaterialGraphValueOpcode.Select:
                        return m_Values.Select(operands[0], operands[1], operands[2]);
                    case MaterialGraphValueOpcode.Compare:
                        return m_Values.Compare(
                            operands[0],
                            operands[1],
                            (MaterialComparison) node.Semantic);
                    case MaterialGraphValueOpcode.Swizzle:
                        if (!MaterialSwizzleMask.TryDecode(
                                node.Semantic,
                                out MaterialSwizzleMask mask))
                        {
                            throw new ArgumentException("Material graph swizzle mask is invalid.");
                        }
                        return m_Values.Swizzle(operands[0], mask);
                    case MaterialGraphValueOpcode.Compose:
                        return EmitCompose(operands);
                    default:
                        throw new NotSupportedException(
                            $"Material graph value opcode '{node.Opcode}' is not supported.");
                }
            }

            private MaterialValue EmitConstant(MaterialGraphValueNode node)
            {
                switch (node.ConstantType)
                {
                    case MaterialValueType.Bool:
                        return m_Values.Constant(node.Constant.x != 0.0f);
                    case MaterialValueType.Float:
                        return m_Values.Constant(node.Constant.x);
                    case MaterialValueType.Float2:
                        return m_Values.Constant(node.Constant.xy);
                    case MaterialValueType.Float3:
                        return m_Values.Constant(node.Constant.xyz);
                    case MaterialValueType.Float4:
                        return m_Values.Constant(node.Constant);
                    default:
                        throw new NotSupportedException(
                            $"Material graph constant type '{node.ConstantType}' is not supported.");
                }
            }

            private MaterialValue EmitCompose(MaterialValue[] operands)
            {
                switch (operands.Length)
                {
                    case 2:
                        return m_Values.Compose(operands[0], operands[1]);
                    case 3:
                        return m_Values.Compose(operands[0], operands[1], operands[2]);
                    case 4:
                        return m_Values.Compose(
                            operands[0],
                            operands[1],
                            operands[2],
                            operands[3]);
                    default:
                        throw new ArgumentOutOfRangeException(nameof(operands));
                }
            }

            private bool TryCompileClosure(
                MaterialGraphClosure reference,
                string consumerNodeId,
                string consumerPort,
                out MaterialClosure closure)
            {
                closure = default;
                if (!ValidateReference(
                        reference.Owner,
                        reference.NodeId,
                        consumerNodeId,
                        consumerPort,
                        "closure"))
                {
                    return false;
                }
                if (!m_Graph.ClosureNodes.TryGetValue(
                        reference.NodeId,
                        out MaterialGraphClosureNode node))
                {
                    AddError(
                        MaterialGraphDiagnosticCodes.MissingNode,
                        $"Closure node '{reference.NodeId}' does not exist.",
                        consumerNodeId,
                        consumerPort);
                    return false;
                }
                if (m_ClosureStates.TryGetValue(node.NodeId, out VisitState state))
                {
                    if (state == VisitState.Complete)
                    {
                        closure = m_CompiledClosures[node.NodeId];
                        return true;
                    }
                    if (state == VisitState.Visiting)
                    {
                        AddError(
                            MaterialGraphDiagnosticCodes.Cycle,
                            $"Closure graph cycle reaches node '{node.NodeId}'.",
                            consumerNodeId,
                            consumerPort);
                    }
                    return false;
                }

                m_ClosureStates.Add(node.NodeId, VisitState.Visiting);
                var values = new MaterialValue[node.Values.Count];
                var closures = new MaterialClosure[node.Closures.Count];
                bool operandsValid = true;
                for (int valueIndex = 0; valueIndex < node.Values.Count; valueIndex++)
                {
                    operandsValid &= TryCompileValue(
                        node.Values[valueIndex],
                        node.NodeId,
                        GetClosureValueInputPort(node.Opcode, valueIndex),
                        out values[valueIndex]);
                }
                for (int closureIndex = 0;
                     closureIndex < node.Closures.Count;
                     closureIndex++)
                {
                    operandsValid &= TryCompileClosure(
                        node.Closures[closureIndex],
                        node.NodeId,
                        GetClosureInputPort(node.Opcode, closureIndex),
                        out closures[closureIndex]);
                }
                if (!operandsValid)
                {
                    m_ClosureStates[node.NodeId] = VisitState.Failed;
                    return false;
                }

                try
                {
                    switch (node.Opcode)
                    {
                        case MaterialGraphClosureOpcode.Slab:
                            closure = m_Closures.Slab(
                                values[0],
                                values[1],
                                values[2],
                                values[3],
                                values[4],
                                node.Features);
                            break;
                        case MaterialGraphClosureOpcode.HorizontalMix:
                            closure = m_Closures.HorizontalMix(
                                closures[0],
                                closures[1],
                                values[0]);
                            break;
                        case MaterialGraphClosureOpcode.VerticalLayer:
                            closure = m_Closures.VerticalLayer(
                                closures[0],
                                closures[1],
                                values[0]);
                            break;
                        default:
                            throw new NotSupportedException(
                                $"Material graph closure opcode '{node.Opcode}' is not supported.");
                    }
                    m_ClosureOrigins.Add(
                        closure.Index,
                        new SourceOrigin(node.NodeId, OutputPort));
                    m_CompiledClosures.Add(node.NodeId, closure);
                    m_ClosureStates[node.NodeId] = VisitState.Complete;
                    return true;
                }
                catch (MaterialIRVerificationException exception)
                {
                    AppendNodeDiagnostics(exception.Diagnostics, node.NodeId, OutputPort);
                }
                catch (ArgumentException exception)
                {
                    AddInvalidNodeError(node.NodeId, exception.Message);
                }
                catch (InvalidOperationException exception)
                {
                    AddInvalidNodeError(node.NodeId, exception.Message);
                }
                catch (NotSupportedException exception)
                {
                    AddInvalidNodeError(node.NodeId, exception.Message);
                }
                m_ClosureStates[node.NodeId] = VisitState.Failed;
                return false;
            }

            private bool ValidateReference(
                MaterialGraph owner,
                string nodeId,
                string consumerNodeId,
                string consumerPort,
                string kind)
            {
                if (owner == null || string.IsNullOrEmpty(nodeId))
                {
                    AddError(
                        MaterialGraphDiagnosticCodes.MissingNode,
                        $"Material graph {kind} input is not connected.",
                        consumerNodeId,
                        consumerPort);
                    return false;
                }
                if (!ReferenceEquals(owner, m_Graph))
                {
                    AddError(
                        MaterialGraphDiagnosticCodes.ForeignReference,
                        $"Material graph {kind} input references another graph.",
                        consumerNodeId,
                        consumerPort);
                    return false;
                }
                return true;
            }

            private void RecordValueOrigin(MaterialValue value, string nodeId)
            {
                if (!m_ValueOrigins.TryGetValue(
                        value.Index,
                        out List<SourceOrigin> origins))
                {
                    origins = new List<SourceOrigin>();
                    m_ValueOrigins.Add(value.Index, origins);
                }
                var origin = new SourceOrigin(nodeId, OutputPort);
                if (!origins.Contains(origin))
                    origins.Add(origin);
            }

            private MaterialGraphProvenance BuildProvenance(MaterialIRModule module)
            {
                var canonicalValues = new Dictionary<string, HashSet<int>>(
                    StringComparer.Ordinal);
                foreach (KeyValuePair<int, List<SourceOrigin>> pair in m_ValueOrigins)
                {
                    int canonicalIndex = module.CanonicalIR.GetCanonicalValueNodeIndex(
                        pair.Key);
                    if (canonicalIndex < 0)
                        continue;
                    for (int originIndex = 0;
                         originIndex < pair.Value.Count;
                         originIndex++)
                    {
                        AddCanonicalOrigin(
                            canonicalValues,
                            pair.Value[originIndex].NodeId,
                            canonicalIndex);
                    }
                }

                var canonicalClosures = new Dictionary<string, HashSet<int>>(
                    StringComparer.Ordinal);
                foreach (KeyValuePair<int, SourceOrigin> pair in m_ClosureOrigins)
                {
                    int canonicalIndex = module.CanonicalIR.GetCanonicalClosureNodeIndex(
                        pair.Key);
                    if (canonicalIndex >= 0)
                    {
                        AddCanonicalOrigin(
                            canonicalClosures,
                            pair.Value.NodeId,
                            canonicalIndex);
                    }
                }
                return new MaterialGraphProvenance(
                    canonicalValues,
                    canonicalClosures);
            }

            private static void AddCanonicalOrigin(
                Dictionary<string, HashSet<int>> origins,
                string nodeId,
                int canonicalIndex)
            {
                if (!origins.TryGetValue(nodeId, out HashSet<int> indices))
                {
                    indices = new HashSet<int>();
                    origins.Add(nodeId, indices);
                }
                indices.Add(canonicalIndex);
            }

            private void AppendSourceDiagnostics(
                IReadOnlyList<MaterialIRDiagnostic> diagnostics,
                string fallbackNodeId)
            {
                for (int diagnosticIndex = 0;
                     diagnosticIndex < diagnostics.Count;
                     diagnosticIndex++)
                {
                    MaterialIRDiagnostic diagnostic = diagnostics[diagnosticIndex];
                    bool mapped = diagnostic.NodeIndex >= 0
                        && (diagnostic.Code.StartsWith("MIR4", StringComparison.Ordinal)
                            ? AddClosureDiagnostic(diagnostic, diagnostic.NodeIndex)
                            : AddValueDiagnostic(diagnostic, diagnostic.NodeIndex));
                    if (!mapped)
                    {
                        AddDiagnostic(
                            diagnostic,
                            new SourceOrigin(fallbackNodeId, OutputPort));
                    }
                }
            }

            private void AppendCanonicalDiagnostics(
                IReadOnlyList<MaterialIRDiagnostic> diagnostics,
                MaterialIRModule module,
                string fallbackNodeId)
            {
                for (int diagnosticIndex = 0;
                     diagnosticIndex < diagnostics.Count;
                     diagnosticIndex++)
                {
                    MaterialIRDiagnostic diagnostic = diagnostics[diagnosticIndex];
                    bool mapped = false;
                    if (diagnostic.NodeIndex >= 0)
                    {
                        foreach (KeyValuePair<int, List<SourceOrigin>> pair in m_ValueOrigins)
                        {
                            if (module.CanonicalIR.GetCanonicalValueNodeIndex(pair.Key)
                                != diagnostic.NodeIndex)
                            {
                                continue;
                            }
                            for (int originIndex = 0;
                                 originIndex < pair.Value.Count;
                                 originIndex++)
                            {
                                AddDiagnostic(diagnostic, pair.Value[originIndex]);
                                mapped = true;
                            }
                        }
                    }
                    if (!mapped)
                    {
                        AddDiagnostic(
                            diagnostic,
                            new SourceOrigin(fallbackNodeId, OutputPort));
                    }
                }
            }

            private bool AddValueDiagnostic(
                in MaterialIRDiagnostic diagnostic,
                int sourceNodeIndex)
            {
                if (!m_ValueOrigins.TryGetValue(
                        sourceNodeIndex,
                        out List<SourceOrigin> origins))
                {
                    return false;
                }
                for (int originIndex = 0; originIndex < origins.Count; originIndex++)
                    AddDiagnostic(diagnostic, origins[originIndex]);
                return origins.Count > 0;
            }

            private bool AddClosureDiagnostic(
                in MaterialIRDiagnostic diagnostic,
                int sourceNodeIndex)
            {
                if (!m_ClosureOrigins.TryGetValue(
                        sourceNodeIndex,
                        out SourceOrigin origin))
                {
                    return false;
                }
                AddDiagnostic(diagnostic, origin);
                return true;
            }

            private void AppendNodeDiagnostics(
                IReadOnlyList<MaterialIRDiagnostic> diagnostics,
                string sourceNodeId,
                string sourcePort)
            {
                for (int diagnosticIndex = 0;
                     diagnosticIndex < diagnostics.Count;
                     diagnosticIndex++)
                {
                    AddDiagnostic(
                        diagnostics[diagnosticIndex],
                        new SourceOrigin(sourceNodeId, sourcePort));
                }
            }

            private void AddDiagnostic(
                in MaterialIRDiagnostic diagnostic,
                in SourceOrigin origin)
            {
                m_Diagnostics.Add(new MaterialGraphDiagnostic(
                    diagnostic.Severity,
                    diagnostic.Code,
                    diagnostic.Message,
                    origin.NodeId,
                    origin.Port));
            }

            private void AddInvalidNodeError(string nodeId, string message)
            {
                AddError(
                    MaterialGraphDiagnosticCodes.InvalidNode,
                    message,
                    nodeId,
                    OutputPort);
            }

            private void AddBackendError(string message, string nodeId)
            {
                AddError(
                    MaterialGraphDiagnosticCodes.BackendRejected,
                    message,
                    nodeId,
                    OutputPort);
            }

            private void AddError(
                string code,
                string message,
                string sourceNodeId,
                string sourcePort)
            {
                m_Diagnostics.Add(new MaterialGraphDiagnostic(
                    MaterialIRDiagnosticSeverity.Error,
                    code,
                    message,
                    sourceNodeId,
                    sourcePort));
            }

            private MaterialGraphCompilationResult CreateResult(
                CompiledMaterialProgram program,
                MaterialIRModule module,
                MaterialGraphProvenance provenance)
            {
                return new MaterialGraphCompilationResult(
                    program,
                    module,
                    provenance,
                    m_Diagnostics.ToArray());
            }
        }

        private static string GetValueInputPort(
            MaterialGraphValueOpcode opcode,
            int operandIndex)
        {
            switch (opcode)
            {
                case MaterialGraphValueOpcode.TextureSample:
                    return operandIndex == 0 ? "Texture" : "UV";
                case MaterialGraphValueOpcode.TextureSampleGrad:
                    return new[] { "Texture", "UV", "Ddx", "Ddy" }[operandIndex];
                case MaterialGraphValueOpcode.Ddx:
                case MaterialGraphValueOpcode.Ddy:
                case MaterialGraphValueOpcode.Saturate:
                case MaterialGraphValueOpcode.OneMinus:
                case MaterialGraphValueOpcode.Normalize:
                case MaterialGraphValueOpcode.Swizzle:
                    return "In";
                case MaterialGraphValueOpcode.Lerp:
                    return new[] { "A", "B", "T" }[operandIndex];
                case MaterialGraphValueOpcode.Select:
                    return new[] { "Condition", "True", "False" }[operandIndex];
                case MaterialGraphValueOpcode.Compose:
                    return new[] { "X", "Y", "Z", "W" }[operandIndex];
                default:
                    return operandIndex == 0 ? "A" : "B";
            }
        }

        private static string GetClosureValueInputPort(
            MaterialGraphClosureOpcode opcode,
            int valueIndex)
        {
            if (opcode == MaterialGraphClosureOpcode.Slab)
            {
                return new[]
                {
                    "BaseColor",
                    "Roughness",
                    "Metallic",
                    "Normal",
                    "Tangent",
                }[valueIndex];
            }
            return "Weight";
        }

        private static string GetClosureInputPort(
            MaterialGraphClosureOpcode opcode,
            int closureIndex)
        {
            if (opcode == MaterialGraphClosureOpcode.VerticalLayer)
                return closureIndex == 0 ? "Bottom" : "Top";
            return closureIndex == 0 ? "Background" : "Foreground";
        }
    }
}
