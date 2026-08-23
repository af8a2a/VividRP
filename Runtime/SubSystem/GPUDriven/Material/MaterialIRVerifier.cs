using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven
{
    [Flags]
    internal enum MaterialOpcodeFlags
    {
        None = 0,
        Arithmetic = 1 << 0,
        Derivative = 1 << 1,
        TextureSample = 1 << 2,
        Parameter = 1 << 3,
        TextureResource = 1 << 4,
        ExternalInput = 1 << 5,
        // Canonical semantic contract: operands may be reordered. Backends must
        // not preserve operand-order-specific NaN payload selection for these ops.
        Commutative = 1 << 6,
    }

    internal enum MaterialOpcodeSignatureKind
    {
        Constant = 0,
        ExternalInput = 1,
        Parameter = 2,
        TextureResource = 3,
        TextureSampleGrad = 4,
        UnarySameNumeric = 5,
        BinarySameNumeric = 6,
        Lerp = 7,
        Select = 8,
        Swizzle = 9,
        Compose = 10,
        Dot = 11,
        Normalize = 12,
        Compare = 13,
    }

    internal enum MaterialOpcodePayloadKind
    {
        None = 0,
        Constant = 1,
        ExternalInput = 2,
        ParameterDeclaration = 3,
        ResourceDeclaration = 4,
        SwizzleMask = 5,
        Comparison = 6,
    }

    [Flags]
    internal enum MaterialEvaluationStageMask
    {
        None = 0,
        Coverage = 1 << 0,
        Surface = 1 << 1,
        All = Coverage | Surface,
    }

    internal enum MaterialDerivativePolicy
    {
        None = 0,
        ProducesDerivative = 1,
        RequiresExplicitGradients = 2,
    }

    internal readonly struct MaterialOpcodeInfo
    {
        internal MaterialOpcodeInfo(
            MaterialValueOpcode opcode,
            string name,
            int minOperandCount,
            int maxOperandCount,
            MaterialOpcodeSignatureKind signatureKind,
            MaterialOpcodePayloadKind payloadKind,
            MaterialOpcodeFlags flags,
            MaterialEvaluationStageMask evaluationStages =
                MaterialEvaluationStageMask.All,
            MaterialDerivativePolicy derivativePolicy =
                MaterialDerivativePolicy.None)
        {
            Opcode = opcode;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            MinOperandCount = minOperandCount;
            MaxOperandCount = maxOperandCount;
            SignatureKind = signatureKind;
            PayloadKind = payloadKind;
            Flags = flags;
            EvaluationStages = evaluationStages;
            DerivativePolicy = derivativePolicy;
        }

        internal MaterialValueOpcode Opcode { get; }

        internal string Name { get; }

        internal int MinOperandCount { get; }

        internal int MaxOperandCount { get; }

        internal MaterialOpcodeSignatureKind SignatureKind { get; }

        internal MaterialOpcodePayloadKind PayloadKind { get; }

        internal MaterialOpcodeFlags Flags { get; }

        internal MaterialEvaluationStageMask EvaluationStages { get; }

        internal MaterialDerivativePolicy DerivativePolicy { get; }
    }

    internal static class MaterialOpcodeTable
    {
        internal static bool TryGetInfo(
            MaterialValueOpcode opcode,
            out MaterialOpcodeInfo info)
        {
            switch (opcode)
            {
                case MaterialValueOpcode.Constant:
                    info = Create(
                        opcode,
                        "constant",
                        0,
                        MaterialOpcodeSignatureKind.Constant,
                        MaterialOpcodePayloadKind.Constant);
                    return true;
                case MaterialValueOpcode.ExternalInput:
                    info = Create(
                        opcode,
                        "external_input",
                        0,
                        MaterialOpcodeSignatureKind.ExternalInput,
                        MaterialOpcodePayloadKind.ExternalInput,
                        MaterialOpcodeFlags.ExternalInput);
                    return true;
                case MaterialValueOpcode.Parameter:
                    info = Create(
                        opcode,
                        "parameter",
                        0,
                        MaterialOpcodeSignatureKind.Parameter,
                        MaterialOpcodePayloadKind.ParameterDeclaration,
                        MaterialOpcodeFlags.Parameter);
                    return true;
                case MaterialValueOpcode.TextureResource:
                    info = Create(
                        opcode,
                        "texture_resource",
                        0,
                        MaterialOpcodeSignatureKind.TextureResource,
                        MaterialOpcodePayloadKind.ResourceDeclaration,
                        MaterialOpcodeFlags.TextureResource);
                    return true;
                case MaterialValueOpcode.TextureSampleGrad:
                    info = Create(
                        opcode,
                        "texture_sample_grad",
                        4,
                        MaterialOpcodeSignatureKind.TextureSampleGrad,
                        MaterialOpcodePayloadKind.None,
                        MaterialOpcodeFlags.TextureSample,
                        MaterialDerivativePolicy.RequiresExplicitGradients);
                    return true;
                case MaterialValueOpcode.Ddx:
                    info = Create(
                        opcode,
                        "ddx",
                        1,
                        MaterialOpcodeSignatureKind.UnarySameNumeric,
                        MaterialOpcodePayloadKind.None,
                        MaterialOpcodeFlags.Derivative,
                        MaterialDerivativePolicy.ProducesDerivative);
                    return true;
                case MaterialValueOpcode.Ddy:
                    info = Create(
                        opcode,
                        "ddy",
                        1,
                        MaterialOpcodeSignatureKind.UnarySameNumeric,
                        MaterialOpcodePayloadKind.None,
                        MaterialOpcodeFlags.Derivative,
                        MaterialDerivativePolicy.ProducesDerivative);
                    return true;
                case MaterialValueOpcode.Add:
                    info = CreateBinaryArithmetic(opcode, "add", commutative: true);
                    return true;
                case MaterialValueOpcode.Multiply:
                    info = CreateBinaryArithmetic(opcode, "multiply", commutative: true);
                    return true;
                case MaterialValueOpcode.Lerp:
                    info = Create(
                        opcode,
                        "lerp",
                        3,
                        MaterialOpcodeSignatureKind.Lerp,
                        MaterialOpcodePayloadKind.None,
                        MaterialOpcodeFlags.Arithmetic);
                    return true;
                case MaterialValueOpcode.Select:
                    info = Create(
                        opcode,
                        "select",
                        3,
                        MaterialOpcodeSignatureKind.Select,
                        MaterialOpcodePayloadKind.None,
                        MaterialOpcodeFlags.Arithmetic);
                    return true;
                case MaterialValueOpcode.Swizzle:
                    info = Create(
                        opcode,
                        "swizzle",
                        1,
                        MaterialOpcodeSignatureKind.Swizzle,
                        MaterialOpcodePayloadKind.SwizzleMask,
                        MaterialOpcodeFlags.Arithmetic);
                    return true;
                case MaterialValueOpcode.Compose:
                    info = new MaterialOpcodeInfo(
                        opcode,
                        "compose",
                        2,
                        4,
                        MaterialOpcodeSignatureKind.Compose,
                        MaterialOpcodePayloadKind.None,
                        MaterialOpcodeFlags.Arithmetic);
                    return true;
                case MaterialValueOpcode.Subtract:
                    info = CreateBinaryArithmetic(opcode, "subtract", commutative: false);
                    return true;
                case MaterialValueOpcode.Divide:
                    info = CreateBinaryArithmetic(opcode, "divide", commutative: false);
                    return true;
                case MaterialValueOpcode.Min:
                    info = CreateBinaryArithmetic(opcode, "min", commutative: true);
                    return true;
                case MaterialValueOpcode.Max:
                    info = CreateBinaryArithmetic(opcode, "max", commutative: true);
                    return true;
                case MaterialValueOpcode.Saturate:
                    info = CreateUnaryArithmetic(opcode, "saturate");
                    return true;
                case MaterialValueOpcode.OneMinus:
                    info = CreateUnaryArithmetic(opcode, "one_minus");
                    return true;
                case MaterialValueOpcode.Dot:
                    info = Create(
                        opcode,
                        "dot",
                        2,
                        MaterialOpcodeSignatureKind.Dot,
                        MaterialOpcodePayloadKind.None,
                        MaterialOpcodeFlags.Arithmetic
                        | MaterialOpcodeFlags.Commutative);
                    return true;
                case MaterialValueOpcode.Normalize:
                    info = Create(
                        opcode,
                        "normalize",
                        1,
                        MaterialOpcodeSignatureKind.Normalize,
                        MaterialOpcodePayloadKind.None,
                        MaterialOpcodeFlags.Arithmetic);
                    return true;
                case MaterialValueOpcode.Compare:
                    info = Create(
                        opcode,
                        "compare",
                        2,
                        MaterialOpcodeSignatureKind.Compare,
                        MaterialOpcodePayloadKind.Comparison,
                        MaterialOpcodeFlags.Arithmetic);
                    return true;
                default:
                    info = default;
                    return false;
            }
        }

        private static MaterialOpcodeInfo Create(
            MaterialValueOpcode opcode,
            string name,
            int operandCount,
            MaterialOpcodeSignatureKind signatureKind,
            MaterialOpcodePayloadKind payloadKind,
            MaterialOpcodeFlags flags = MaterialOpcodeFlags.None,
            MaterialDerivativePolicy derivativePolicy =
                MaterialDerivativePolicy.None)
        {
            return new MaterialOpcodeInfo(
                opcode,
                name,
                operandCount,
                operandCount,
                signatureKind,
                payloadKind,
                flags,
                MaterialEvaluationStageMask.All,
                derivativePolicy);
        }

        private static MaterialOpcodeInfo CreateUnaryArithmetic(
            MaterialValueOpcode opcode,
            string name)
        {
            return Create(
                opcode,
                name,
                1,
                MaterialOpcodeSignatureKind.UnarySameNumeric,
                MaterialOpcodePayloadKind.None,
                MaterialOpcodeFlags.Arithmetic);
        }

        private static MaterialOpcodeInfo CreateBinaryArithmetic(
            MaterialValueOpcode opcode,
            string name,
            bool commutative)
        {
            MaterialOpcodeFlags flags = MaterialOpcodeFlags.Arithmetic;
            if (commutative)
                flags |= MaterialOpcodeFlags.Commutative;
            return Create(
                opcode,
                name,
                2,
                MaterialOpcodeSignatureKind.BinarySameNumeric,
                MaterialOpcodePayloadKind.None,
                flags);
        }
    }

    internal enum MaterialIRDiagnosticSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    internal static class MaterialIRDiagnosticCodes
    {
        internal const string UnknownOpcode = "MIR1001";
        internal const string UnknownValueType = "MIR1002";
        internal const string InvalidOperandEncoding = "MIR1003";
        internal const string OperandOutOfRange = "MIR1004";
        internal const string NonTopologicalOperand = "MIR1005";
        internal const string OperandTypeMismatch = "MIR1006";
        internal const string ResultTypeMismatch = "MIR1007";
        internal const string InvalidSemantic = "MIR1008";
        internal const string NonCanonicalPayload = "MIR1009";

        internal const string OutputNotOwned = "MIR2001";
        internal const string OutputTypeMismatch = "MIR2002";
        internal const string UnknownMaterialFeature = "MIR2003";
        internal const string InvalidShadingModel = "MIR2004";

        internal const string TopologyOwnerMismatch = "MIR3001";
        internal const string TopologyBudgetExceeded = "MIR3002";
        internal const string InvalidTopologyShape = "MIR3003";
        internal const string InvalidTopologyValue = "MIR3004";
        internal const string InvalidTopologyIndex = "MIR3005";
        internal const string InvalidTopologySemantic = "MIR3006";

        internal const string ClosureGraphOwnerMismatch = "MIR4001";
        internal const string UnknownClosureOpcode = "MIR4002";
        internal const string InvalidClosureOperandEncoding = "MIR4003";
        internal const string ClosureOperandOutOfRange = "MIR4004";
        internal const string NonTopologicalClosureOperand = "MIR4005";
        internal const string InvalidClosureValue = "MIR4006";
        internal const string InvalidClosureFeature = "MIR4007";
        internal const string ClosureRootNotOwned = "MIR4008";
        internal const string InvalidClosureGraphShape = "MIR4009";
        internal const string ClosureGraphFanOut = "MIR4010";
        internal const string ClosureGraphBudgetExceeded = "MIR4011";

        internal const string UnsupportedStageOpcode = "MIR5001";
        internal const string StageInputUnavailable = "MIR5002";
        internal const string DerivativeSourceCannotBeLegalized = "MIR5003";
        internal const string InvalidStageLIR = "MIR5004";
    }

    internal readonly struct MaterialIRDiagnostic
    {
        internal MaterialIRDiagnostic(
            MaterialIRDiagnosticSeverity severity,
            string code,
            string message,
            int nodeIndex = -1)
        {
            Severity = severity;
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            NodeIndex = nodeIndex;
        }

        internal MaterialIRDiagnosticSeverity Severity { get; }

        internal string Code { get; }

        internal string Message { get; }

        internal int NodeIndex { get; }
    }

    internal sealed class MaterialIRVerificationResult
    {
        private readonly IReadOnlyList<MaterialIRDiagnostic> m_Diagnostics;

        internal MaterialIRVerificationResult(MaterialIRDiagnostic[] diagnostics)
        {
            if (diagnostics == null)
                throw new ArgumentNullException(nameof(diagnostics));

            m_Diagnostics = Array.AsReadOnly(
                (MaterialIRDiagnostic[]) diagnostics.Clone());
            HasErrors = false;
            for (int i = 0; i < m_Diagnostics.Count; i++)
            {
                if (m_Diagnostics[i].Severity != MaterialIRDiagnosticSeverity.Error)
                    continue;

                HasErrors = true;
                break;
            }
        }

        internal IReadOnlyList<MaterialIRDiagnostic> Diagnostics => m_Diagnostics;

        internal bool HasErrors { get; }

        internal bool IsValid => !HasErrors;

        internal void ThrowIfInvalid()
        {
            if (HasErrors)
                throw new MaterialIRVerificationException(this);
        }
    }

    internal sealed class MaterialIRVerificationException : InvalidOperationException
    {
        internal MaterialIRVerificationException(MaterialIRVerificationResult result)
            : base(BuildMessage(result))
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            Diagnostics = result.Diagnostics;
        }

        internal IReadOnlyList<MaterialIRDiagnostic> Diagnostics { get; }

        private static string BuildMessage(MaterialIRVerificationResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            int errorCount = 0;
            MaterialIRDiagnostic firstError = default;
            bool hasFirstError = false;
            for (int i = 0; i < result.Diagnostics.Count; i++)
            {
                MaterialIRDiagnostic diagnostic = result.Diagnostics[i];
                if (diagnostic.Severity != MaterialIRDiagnosticSeverity.Error)
                    continue;

                errorCount++;
                if (hasFirstError)
                    continue;

                firstError = diagnostic;
                hasFirstError = true;
            }

            if (!hasFirstError)
                return "Material IR verification failed.";

            string location = firstError.NodeIndex >= 0
                ? $" at node %{firstError.NodeIndex}"
                : string.Empty;
            return $"Material IR verification failed with {errorCount} error(s). "
                + $"First error {firstError.Code}{location}: {firstError.Message}";
        }
    }

    internal static class MaterialIRVerifier
    {
        private const int InvalidOperand = -1;
        private const int KnownMaterialFeatureBits =
            (int) MaterialFeatureMask.AlphaClip;
        private const int KnownShadingModelBits =
            (int) MaterialShadingModelMask.StandardLit
            | (int) MaterialShadingModelMask.Unlit;
        private const int KnownClosureFeatureBits =
            (int) ClosureFeatureMask.BaseColorTexture
            | (int) ClosureFeatureMask.NormalTexture
            | (int) ClosureFeatureMask.MaskTexture;

        internal static MaterialIRVerificationResult VerifyCandidateNode(
            MaterialValueIR values,
            in MaterialValueNode node)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            var diagnostics = new List<MaterialIRDiagnostic>();
            AppendNodeDiagnostics(
                values,
                node,
                values.NodeCount,
                values.NodeCount + 1,
                diagnostics);
            return CreateResult(diagnostics);
        }

        internal static MaterialIRVerificationResult VerifyCandidateClosureNode(
            ClosureExpressionGraph graph,
            in ClosureExpressionNode node)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            var diagnostics = new List<MaterialIRDiagnostic>();
            AppendClosureNodeDiagnostics(
                graph,
                node,
                graph.NodeCount,
                graph.NodeCount + 1,
                diagnostics);
            return CreateResult(diagnostics);
        }

        internal static MaterialIRVerificationResult VerifyModule(
            MaterialValueIR values,
            in MaterialOutputRoots outputs,
            ClosureExpressionGraph closureGraph,
            MaterialClosure closureRoot,
            ClosureTopologyBudget closureBudget,
            MaterialFeatureMask materialFeatures,
            MaterialShadingModelMask shadingModels)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (closureGraph == null)
                throw new ArgumentNullException(nameof(closureGraph));

            var diagnostics = new List<MaterialIRDiagnostic>();
            AppendDeclarationDiagnostics(values, diagnostics);
            AppendValueGraphDiagnostics(values, diagnostics);
            AppendOutputDiagnostics(
                values,
                outputs.CoverageValue,
                MaterialValueType.Float,
                nameof(MaterialOutputRoots.CoverageValue),
                diagnostics);
            AppendOutputDiagnostics(
                values,
                outputs.AlphaClipThreshold,
                MaterialValueType.Float,
                nameof(MaterialOutputRoots.AlphaClipThreshold),
                diagnostics);
            AppendOutputDiagnostics(
                values,
                outputs.Emission,
                MaterialValueType.Float3,
                nameof(MaterialOutputRoots.Emission),
                diagnostics);

            int unknownMaterialFeatures =
                (int) materialFeatures & ~KnownMaterialFeatureBits;
            if (unknownMaterialFeatures != 0)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.UnknownMaterialFeature,
                    $"Material feature mask contains unknown bits 0x{unknownMaterialFeatures:X}.");
            }

            int shadingModelBits = (int) shadingModels;
            int unknownShadingModels = shadingModelBits & ~KnownShadingModelBits;
            if (shadingModelBits == 0 || unknownShadingModels != 0)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidShadingModel,
                    shadingModelBits == 0
                        ? "Material shading model mask must not be empty."
                        : $"Material shading model mask contains unknown bits 0x{unknownShadingModels:X}.");
            }

            if (!ReferenceEquals(values, closureGraph.ValueIR))
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.ClosureGraphOwnerMismatch,
                    "Closure expression graph must reference the module value IR.");
            }
            AppendClosureGraphDiagnostics(
                closureGraph,
                closureRoot,
                closureBudget,
                diagnostics);
            return CreateResult(diagnostics);
        }

        internal static MaterialIRVerificationResult VerifyStageSlice(
            MaterialValueSlice sourceSlice,
            MaterialEvaluationStage stage)
        {
            if (sourceSlice == null)
                throw new ArgumentNullException(nameof(sourceSlice));
            if (stage != MaterialEvaluationStage.Coverage
                && stage != MaterialEvaluationStage.Surface)
            {
                throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }

            var diagnostics = new List<MaterialIRDiagnostic>();
            MaterialValueIR values = sourceSlice.Values;
            var derivativeStates = new StageDerivativeAvailability[values.NodeCount];
            var uniformityStates = new MaterialStageUniformity[values.NodeCount];
            MaterialEvaluationStageMask stageMask = stage
                == MaterialEvaluationStage.Coverage
                ? MaterialEvaluationStageMask.Coverage
                : MaterialEvaluationStageMask.Surface;

            for (int sliceIndex = 0;
                 sliceIndex < sourceSlice.NodeIndices.Count;
                 sliceIndex++)
            {
                int nodeIndex = sourceSlice.NodeIndices[sliceIndex];
                MaterialValueNode node = values.Nodes[nodeIndex];
                if (!MaterialOpcodeTable.TryGetInfo(
                        node.Opcode,
                        out MaterialOpcodeInfo info)
                    || (info.EvaluationStages & stageMask) == 0)
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.UnsupportedStageOpcode,
                        nodeIndex,
                        $"Opcode {node.Opcode} is not available in {stage} evaluation.");
                    continue;
                }

                if (node.Opcode == MaterialValueOpcode.ExternalInput
                    && !IsExternalInputAvailable(
                        stage,
                        (MaterialExternalInput) node.Semantic))
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.StageInputUnavailable,
                        nodeIndex,
                        $"External input {(MaterialExternalInput) node.Semantic} is not available in {stage} evaluation.");
                }

                if (info.DerivativePolicy
                    != MaterialDerivativePolicy.ProducesDerivative)
                {
                    continue;
                }

                if (AnalyzeStageDerivative(
                        values,
                        node.Operand0,
                        derivativeStates,
                        uniformityStates)
                    == StageDerivativeAvailability.Unavailable)
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.DerivativeSourceCannotBeLegalized,
                        nodeIndex,
                        $"{node.Opcode} source %{node.Operand0} cannot be legalized for {stage} evaluation.");
                }
            }

            return CreateResult(diagnostics);
        }

        internal static MaterialIRVerificationResult VerifyStageLIR(
            MaterialStageLIR stageLIR)
        {
            return VerifyStageLIR(stageLIR, verifyCanonicalLowering: true);
        }

        internal static MaterialIRVerificationResult VerifyStageLIRStructure(
            MaterialStageLIR stageLIR)
        {
            return VerifyStageLIR(stageLIR, verifyCanonicalLowering: false);
        }

        private static MaterialIRVerificationResult VerifyStageLIR(
            MaterialStageLIR stageLIR,
            bool verifyCanonicalLowering)
        {
            if (stageLIR == null)
                throw new ArgumentNullException(nameof(stageLIR));

            var diagnostics = new List<MaterialIRDiagnostic>();
            bool hasKnownStage = stageLIR.Stage == MaterialEvaluationStage.Coverage
                || stageLIR.Stage == MaterialEvaluationStage.Surface;
            if (!hasKnownStage)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidStageLIR,
                    $"Stage LIR evaluation stage value {(int) stageLIR.Stage} is not defined.");
            }
            else
            {
                MaterialStageExecutionModel expectedExecution = stageLIR.Stage
                    == MaterialEvaluationStage.Coverage
                    ? MaterialStageExecutionModel.RasterFragment
                    : MaterialStageExecutionModel.VisibilityResolve;
                MaterialStageDerivativeProvider expectedProvider = stageLIR.Stage
                    == MaterialEvaluationStage.Coverage
                    ? MaterialStageDerivativeProvider.NativeQuad
                    : MaterialStageDerivativeProvider.VisibilityBuffer;
                if (stageLIR.ExecutionModel != expectedExecution
                    || stageLIR.DerivativeProvider != expectedProvider)
                {
                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidStageLIR,
                        $"{stageLIR.Stage} LIR has an invalid execution or derivative provider profile.");
                }
            }

            if (stageLIR.SourceValueMapCount != stageLIR.Values.NodeCount)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidStageLIR,
                    "Stage LIR source map size does not match its source value IR.");
            }

            for (int nodeIndex = 0; nodeIndex < stageLIR.Nodes.Count; nodeIndex++)
            {
                MaterialStageLIRNode node = stageLIR.Nodes[nodeIndex];
                bool hasValidSource = (uint) node.SourceNodeIndex
                    < (uint) stageLIR.Values.NodeCount
                    && ContainsSourceNode(stageLIR.SourceSlice, node.SourceNodeIndex);
                if (!hasValidSource)
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidStageLIR,
                        node.SourceNodeIndex,
                        $"Stage LIR node %{nodeIndex} has invalid source provenance %{node.SourceNodeIndex}.");
                }

                if ((uint) node.Opcode > (uint) MaterialStageLIROpcode.Compare
                    || !IsKnownType(node.Type))
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidStageLIR,
                        node.SourceNodeIndex,
                        $"Stage LIR node %{nodeIndex} has an unknown opcode or result type.");
                    continue;
                }

                GetStageLIROperandRange(
                    node.Opcode,
                    out int minimumOperands,
                    out int maximumOperands);
                bool hasValidOperandCount = node.OperandCount >= minimumOperands
                    && node.OperandCount <= maximumOperands
                    && node.OperandCount <= 4;
                if (!hasValidOperandCount)
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidStageLIR,
                        node.SourceNodeIndex,
                        $"Stage LIR node %{nodeIndex} has {node.OperandCount} operands; expected {minimumOperands} to {maximumOperands}.");
                }

                bool hasValidOperands = true;
                for (int operandIndex = 0; operandIndex < 4; operandIndex++)
                {
                    int operand = node.GetOperand(operandIndex);
                    if (operandIndex >= node.OperandCount)
                    {
                        if (operand != InvalidOperand)
                        {
                            hasValidOperands = false;
                            AddNodeError(
                                diagnostics,
                                MaterialIRDiagnosticCodes.InvalidStageLIR,
                                node.SourceNodeIndex,
                                $"Stage LIR node %{nodeIndex} inactive operand {operandIndex} must be -1.");
                        }
                        continue;
                    }

                    if (operand < 0 || operand >= nodeIndex)
                    {
                        hasValidOperands = false;
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidStageLIR,
                            node.SourceNodeIndex,
                            $"Stage LIR node %{nodeIndex} operand {operandIndex} is not topological.");
                    }
                }

                if (hasValidOperandCount && hasValidOperands)
                {
                    AppendStageLIRNodeDiagnostics(
                        stageLIR,
                        node,
                        nodeIndex,
                        hasKnownStage,
                        diagnostics);
                }
            }

            AppendStageLIRSourceMapDiagnostics(stageLIR, diagnostics);
            AppendStageLIRReachabilityDiagnostics(stageLIR, diagnostics);

            if (stageLIR.Roots.Count != stageLIR.SourceSlice.Roots.Count)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidStageLIR,
                    "Stage LIR root count does not match its source slice.");
            }
            else
            {
                for (int rootIndex = 0; rootIndex < stageLIR.Roots.Count; rootIndex++)
                {
                    MaterialStageValue root = stageLIR.Roots[rootIndex];
                    if (!stageLIR.Owns(root)
                        || !stageLIR.TryGetValue(
                            stageLIR.SourceSlice.Roots[rootIndex],
                            out MaterialStageValue mappedRoot)
                        || !root.Equals(mappedRoot))
                    {
                        AddError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidStageLIR,
                            $"Stage LIR root {rootIndex} is not the lowered source root.");
                    }
                }
            }

            if (hasKnownStage && verifyCanonicalLowering)
            {
                MaterialIRVerificationResult stageSliceVerification =
                    VerifyStageSlice(stageLIR.SourceSlice, stageLIR.Stage);
                if (!stageSliceVerification.IsValid)
                {
                    diagnostics.AddRange(stageSliceVerification.Diagnostics);
                }
                else
                {
                    MaterialStageLIR canonical =
                        MaterialStageLIRLowerer.BuildCanonicalUnchecked(
                            stageLIR.SourceSlice,
                            stageLIR.Stage);
                    if (!IsCanonicalStageLIR(stageLIR, canonical))
                    {
                        AddError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidStageLIR,
                            "Stage LIR does not match the canonical stage lowering of its source slice.");
                    }
                }
            }

            return CreateResult(diagnostics);
        }

        private static bool IsCanonicalStageLIR(
            MaterialStageLIR stageLIR,
            MaterialStageLIR canonical)
        {
            if (stageLIR.ExecutionModel != canonical.ExecutionModel
                || stageLIR.DerivativeProvider != canonical.DerivativeProvider
                || stageLIR.NodeCount != canonical.NodeCount
                || stageLIR.Roots.Count != canonical.Roots.Count
                || stageLIR.SourceValueMapCount != canonical.SourceValueMapCount)
            {
                return false;
            }

            for (int nodeIndex = 0; nodeIndex < stageLIR.NodeCount; nodeIndex++)
            {
                MaterialStageLIRNode node = stageLIR.Nodes[nodeIndex];
                MaterialStageLIRNode expected = canonical.Nodes[nodeIndex];
                uint4 constantBits = math.asuint(node.Constant);
                uint4 expectedConstantBits = math.asuint(expected.Constant);
                if (node.Opcode != expected.Opcode
                    || node.Type != expected.Type
                    || node.Semantic != expected.Semantic
                    || node.SourceNodeIndex != expected.SourceNodeIndex
                    || node.OperandCount != expected.OperandCount
                    || node.Operand0 != expected.Operand0
                    || node.Operand1 != expected.Operand1
                    || node.Operand2 != expected.Operand2
                    || node.Operand3 != expected.Operand3
                    || constantBits.x != expectedConstantBits.x
                    || constantBits.y != expectedConstantBits.y
                    || constantBits.z != expectedConstantBits.z
                    || constantBits.w != expectedConstantBits.w)
                {
                    return false;
                }
            }

            for (int rootIndex = 0; rootIndex < stageLIR.Roots.Count; rootIndex++)
            {
                if (stageLIR.Roots[rootIndex].Index
                    != canonical.Roots[rootIndex].Index)
                {
                    return false;
                }
            }

            for (int sourceIndex = 0;
                 sourceIndex < stageLIR.SourceValueMapCount;
                 sourceIndex++)
            {
                if (stageLIR.GetMappedNodeIndex(sourceIndex)
                    != canonical.GetMappedNodeIndex(sourceIndex))
                {
                    return false;
                }
            }
            return true;
        }

        private static void AppendStageLIRNodeDiagnostics(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            int nodeIndex,
            bool hasKnownStage,
            List<MaterialIRDiagnostic> diagnostics)
        {
            if (node.Opcode != MaterialStageLIROpcode.Constant
                && !IsDefaultBits(node.Constant))
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    "must not carry a constant payload.");
            }

            switch (node.Opcode)
            {
                case MaterialStageLIROpcode.StageInput:
                    var input = (MaterialStageInput) node.Semantic;
                    MaterialValueType inputType = GetStageInputType(input);
                    if (!IsKnownType(inputType)
                        || (hasKnownStage
                            && !IsStageInputAvailable(stageLIR.Stage, input)))
                    {
                        AddStageLIRNodeError(
                            diagnostics,
                            node,
                            nodeIndex,
                            $"has invalid input semantic {node.Semantic} for {stageLIR.Stage} evaluation.");
                    }
                    else
                    {
                        RequireStageLIRResultType(
                            node,
                            nodeIndex,
                            inputType,
                            diagnostics);
                    }
                    break;
                case MaterialStageLIROpcode.Constant:
                    if (node.Semantic != 0)
                    {
                        AddStageLIRNodeError(
                            diagnostics,
                            node,
                            nodeIndex,
                            "constant semantic payload must be 0.");
                    }
                    VerifyStageLIRConstant(node, nodeIndex, diagnostics);
                    break;
                case MaterialStageLIROpcode.Parameter:
                    if (!stageLIR.Values.TryGetParameterDeclaration(
                            node.Semantic,
                            out MaterialParameterDeclaration parameter)
                        || !IsDataType(parameter.Type))
                    {
                        AddStageLIRNodeError(
                            diagnostics,
                            node,
                            nodeIndex,
                            $"references invalid parameter declaration {node.Semantic}.");
                    }
                    else
                    {
                        RequireStageLIRResultType(
                            node,
                            nodeIndex,
                            parameter.Type,
                            diagnostics);
                    }
                    break;
                case MaterialStageLIROpcode.TextureResource:
                    if (!stageLIR.Values.TryGetResourceDeclaration(
                            node.Semantic,
                            out MaterialResourceDeclaration resource)
                        || resource.Type != MaterialValueType.Texture2D)
                    {
                        AddStageLIRNodeError(
                            diagnostics,
                            node,
                            nodeIndex,
                            $"references invalid resource declaration {node.Semantic}.");
                    }
                    else
                    {
                        RequireStageLIRResultType(
                            node,
                            nodeIndex,
                            resource.Type,
                            diagnostics);
                    }
                    break;
                case MaterialStageLIROpcode.Swizzle:
                    if (!MaterialSwizzleMask.TryDecode(
                            node.Semantic,
                            out MaterialSwizzleMask mask))
                    {
                        AddStageLIRNodeError(
                            diagnostics,
                            node,
                            nodeIndex,
                            $"has non-canonical swizzle payload 0x{node.Semantic:X8}.");
                    }
                    else
                    {
                        VerifyStageLIRSwizzle(
                            stageLIR,
                            node,
                            nodeIndex,
                            mask,
                            diagnostics);
                    }
                    break;
                case MaterialStageLIROpcode.Compare:
                    if (node.Semantic < (int) MaterialComparison.Equal
                        || node.Semantic > (int) MaterialComparison.GreaterOrEqual)
                    {
                        AddStageLIRNodeError(
                            diagnostics,
                            node,
                            nodeIndex,
                            $"has undefined comparison semantic {node.Semantic}.");
                    }
                    VerifyStageLIRCompare(stageLIR, node, nodeIndex, diagnostics);
                    break;
                default:
                    if (node.Semantic != 0)
                    {
                        AddStageLIRNodeError(
                            diagnostics,
                            node,
                            nodeIndex,
                            "semantic payload must be 0.");
                    }
                    AppendStageLIRSignatureDiagnostics(
                        stageLIR,
                        node,
                        nodeIndex,
                        diagnostics);
                    break;
            }
        }

        private static void AppendStageLIRSignatureDiagnostics(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            switch (node.Opcode)
            {
                case MaterialStageLIROpcode.TextureSampleGrad:
                    RequireStageLIROperandType(
                        stageLIR,
                        node,
                        nodeIndex,
                        0,
                        MaterialValueType.Texture2D,
                        diagnostics);
                    RequireStageLIROperandType(
                        stageLIR,
                        node,
                        nodeIndex,
                        1,
                        MaterialValueType.Float2,
                        diagnostics);
                    RequireStageLIROperandType(
                        stageLIR,
                        node,
                        nodeIndex,
                        2,
                        MaterialValueType.Float2,
                        diagnostics);
                    RequireStageLIROperandType(
                        stageLIR,
                        node,
                        nodeIndex,
                        3,
                        MaterialValueType.Float2,
                        diagnostics);
                    RequireStageLIRResultType(
                        node,
                        nodeIndex,
                        MaterialValueType.Float4,
                        diagnostics);
                    break;
                case MaterialStageLIROpcode.Add:
                case MaterialStageLIROpcode.Multiply:
                case MaterialStageLIROpcode.Subtract:
                case MaterialStageLIROpcode.Divide:
                case MaterialStageLIROpcode.Min:
                case MaterialStageLIROpcode.Max:
                    VerifyStageLIRBinarySameNumeric(
                        stageLIR,
                        node,
                        nodeIndex,
                        diagnostics);
                    break;
                case MaterialStageLIROpcode.Lerp:
                    VerifyStageLIRLerp(stageLIR, node, nodeIndex, diagnostics);
                    break;
                case MaterialStageLIROpcode.Select:
                    VerifyStageLIRSelect(stageLIR, node, nodeIndex, diagnostics);
                    break;
                case MaterialStageLIROpcode.Compose:
                    for (int operandIndex = 0;
                         operandIndex < node.OperandCount;
                         operandIndex++)
                    {
                        RequireStageLIROperandType(
                            stageLIR,
                            node,
                            nodeIndex,
                            operandIndex,
                            MaterialValueType.Float,
                            diagnostics);
                    }
                    RequireStageLIRResultType(
                        node,
                        nodeIndex,
                        GetFloatType(node.OperandCount),
                        diagnostics);
                    break;
                case MaterialStageLIROpcode.Saturate:
                case MaterialStageLIROpcode.OneMinus:
                    VerifyStageLIRUnarySameNumeric(
                        stageLIR,
                        node,
                        nodeIndex,
                        diagnostics);
                    break;
                case MaterialStageLIROpcode.Dot:
                    VerifyStageLIRDot(stageLIR, node, nodeIndex, diagnostics);
                    break;
                case MaterialStageLIROpcode.Normalize:
                    MaterialValueType operandType = GetStageLIROperandType(
                        stageLIR,
                        node,
                        0);
                    if (!IsVectorType(operandType))
                    {
                        AddStageLIRNodeError(
                            diagnostics,
                            node,
                            nodeIndex,
                            $"normalize operand must be a vector, got {operandType}.");
                    }
                    else
                    {
                        RequireStageLIRResultType(
                            node,
                            nodeIndex,
                            operandType,
                            diagnostics);
                    }
                    break;
            }
        }

        private static void VerifyStageLIRConstant(
            in MaterialStageLIRNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            if (!IsDataType(node.Type))
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    $"constant result must be Bool or numeric, got {node.Type}.");
                return;
            }

            uint4 bits = math.asuint(node.Constant);
            if (node.Type == MaterialValueType.Bool
                && bits.x != 0u
                && bits.x != math.asuint(1.0f))
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    "Bool constant must be exactly 0 or 1.");
            }
            for (int componentIndex = GetComponentCount(node.Type);
                 componentIndex < 4;
                 componentIndex++)
            {
                if (bits[componentIndex] != 0u)
                {
                    AddStageLIRNodeError(
                        diagnostics,
                        node,
                        nodeIndex,
                        $"unused constant component {componentIndex} must be positive zero.");
                }
            }
        }

        private static void VerifyStageLIRUnarySameNumeric(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType operandType = GetStageLIROperandType(stageLIR, node, 0);
            if (!IsNumericType(operandType))
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    $"operand must be numeric, got {operandType}.");
                return;
            }
            RequireStageLIRResultType(node, nodeIndex, operandType, diagnostics);
        }

        private static void VerifyStageLIRBinarySameNumeric(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType leftType = GetStageLIROperandType(stageLIR, node, 0);
            MaterialValueType rightType = GetStageLIROperandType(stageLIR, node, 1);
            if (!IsNumericType(leftType)
                || !IsNumericType(rightType)
                || leftType != rightType)
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    $"binary operands must have the same numeric type, got {leftType} and {rightType}.");
                return;
            }
            RequireStageLIRResultType(node, nodeIndex, leftType, diagnostics);
        }

        private static void VerifyStageLIRLerp(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType leftType = GetStageLIROperandType(stageLIR, node, 0);
            MaterialValueType rightType = GetStageLIROperandType(stageLIR, node, 1);
            if (!IsNumericType(leftType) || leftType != rightType)
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    $"lerp values must have the same numeric type, got {leftType} and {rightType}.");
            }
            RequireStageLIROperandType(
                stageLIR,
                node,
                nodeIndex,
                2,
                MaterialValueType.Float,
                diagnostics);
            if (IsNumericType(leftType) && leftType == rightType)
                RequireStageLIRResultType(node, nodeIndex, leftType, diagnostics);
        }

        private static void VerifyStageLIRSelect(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            RequireStageLIROperandType(
                stageLIR,
                node,
                nodeIndex,
                0,
                MaterialValueType.Bool,
                diagnostics);
            MaterialValueType trueType = GetStageLIROperandType(stageLIR, node, 1);
            MaterialValueType falseType = GetStageLIROperandType(stageLIR, node, 2);
            if (!IsDataType(trueType) || trueType != falseType)
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    $"select values must have the same data type, got {trueType} and {falseType}.");
                return;
            }
            RequireStageLIRResultType(node, nodeIndex, trueType, diagnostics);
        }

        private static void VerifyStageLIRSwizzle(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            int nodeIndex,
            in MaterialSwizzleMask mask,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType sourceType = GetStageLIROperandType(stageLIR, node, 0);
            if (!IsNumericType(sourceType))
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    $"swizzle source must be numeric, got {sourceType}.");
                return;
            }

            int sourceComponentCount = GetComponentCount(sourceType);
            for (int componentIndex = 0;
                 componentIndex < mask.ComponentCount;
                 componentIndex++)
            {
                if (mask.GetComponent(componentIndex) >= sourceComponentCount)
                {
                    AddStageLIRNodeError(
                        diagnostics,
                        node,
                        nodeIndex,
                        $"swizzle component {mask.GetComponent(componentIndex)} is unavailable on {sourceType}.");
                }
            }
            RequireStageLIRResultType(node, nodeIndex, mask.ResultType, diagnostics);
        }

        private static void VerifyStageLIRDot(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType leftType = GetStageLIROperandType(stageLIR, node, 0);
            MaterialValueType rightType = GetStageLIROperandType(stageLIR, node, 1);
            if (!IsVectorType(leftType) || leftType != rightType)
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    $"dot operands must have the same vector type, got {leftType} and {rightType}.");
            }
            RequireStageLIRResultType(
                node,
                nodeIndex,
                MaterialValueType.Float,
                diagnostics);
        }

        private static void VerifyStageLIRCompare(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType leftType = GetStageLIROperandType(stageLIR, node, 0);
            MaterialValueType rightType = GetStageLIROperandType(stageLIR, node, 1);
            if (!IsNumericType(leftType) || leftType != rightType)
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    $"compare operands must have the same numeric type, got {leftType} and {rightType}.");
            }
            RequireStageLIRResultType(
                node,
                nodeIndex,
                MaterialValueType.Bool,
                diagnostics);
        }

        private static void AppendStageLIRSourceMapDiagnostics(
            MaterialStageLIR stageLIR,
            List<MaterialIRDiagnostic> diagnostics)
        {
            var uniformityStates = new MaterialStageUniformity[
                stageLIR.Values.NodeCount];
            int sourceCount = Math.Min(
                stageLIR.SourceValueMapCount,
                stageLIR.Values.NodeCount);
            for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
            {
                int mappedNodeIndex = stageLIR.GetMappedNodeIndex(sourceIndex);
                if (mappedNodeIndex == InvalidOperand)
                    continue;
                if (mappedNodeIndex < 0
                    || mappedNodeIndex >= stageLIR.NodeCount
                    || !ContainsSourceNode(stageLIR.SourceSlice, sourceIndex))
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidStageLIR,
                        sourceIndex,
                        $"Stage LIR source map entry %{sourceIndex} -> %{mappedNodeIndex} is invalid.");
                    continue;
                }

                MaterialStageLIRNode mappedNode = stageLIR.Nodes[mappedNodeIndex];
                if (mappedNode.Type != stageLIR.Values.Nodes[sourceIndex].Type)
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidStageLIR,
                        sourceIndex,
                        $"Stage LIR source map entry %{sourceIndex} has an incompatible type.");
                    continue;
                }
                AppendDirectStageLIRMappingDiagnostics(
                    stageLIR,
                    sourceIndex,
                    mappedNodeIndex,
                    uniformityStates,
                    diagnostics);
            }
        }

        private static void AppendDirectStageLIRMappingDiagnostics(
            MaterialStageLIR stageLIR,
            int sourceIndex,
            int mappedNodeIndex,
            MaterialStageUniformity[] uniformityStates,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueNode source = stageLIR.Values.Nodes[sourceIndex];
            if (source.Opcode == MaterialValueOpcode.Ddx
                || source.Opcode == MaterialValueOpcode.Ddy)
            {
                AppendDerivativeStageLIRMappingDiagnostics(
                    stageLIR,
                    source,
                    sourceIndex,
                    mappedNodeIndex,
                    uniformityStates,
                    diagnostics);
                return;
            }

            MaterialStageLIRNode mapped = stageLIR.Nodes[mappedNodeIndex];
            MaterialStageLIROpcode expectedOpcode;
            int expectedSemantic;
            if (source.Opcode == MaterialValueOpcode.ExternalInput)
            {
                expectedOpcode = MaterialStageLIROpcode.StageInput;
                expectedSemantic = (int) MaterialStageLIRLowerer.MapStageInput(
                    (MaterialExternalInput) source.Semantic);
            }
            else
            {
                expectedOpcode = MaterialStageLIRLowerer.MapOpcode(source.Opcode);
                expectedSemantic = source.Semantic;
            }

            if (mapped.Opcode != expectedOpcode
                || mapped.Semantic != expectedSemantic)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidStageLIR,
                    sourceIndex,
                    $"Stage LIR source map entry %{sourceIndex} does not preserve opcode or semantic payload.");
            }

            uint4 sourceBits = math.asuint(source.Constant);
            uint4 mappedBits = math.asuint(mapped.Constant);
            if (sourceBits.x != mappedBits.x
                || sourceBits.y != mappedBits.y
                || sourceBits.z != mappedBits.z
                || sourceBits.w != mappedBits.w)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidStageLIR,
                    sourceIndex,
                    $"Stage LIR source map entry %{sourceIndex} does not preserve constant payload.");
            }

            int sourceOperandCount = 0;
            while (sourceOperandCount < 4
                   && GetOperand(source, sourceOperandCount) >= 0)
            {
                sourceOperandCount++;
            }
            if (mapped.OperandCount != sourceOperandCount)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidStageLIR,
                    sourceIndex,
                    $"Stage LIR source map entry %{sourceIndex} does not preserve operand count.");
                return;
            }

            for (int operandIndex = 0;
                 operandIndex < sourceOperandCount;
                 operandIndex++)
            {
                int sourceOperand = GetOperand(source, operandIndex);
                if ((uint) sourceOperand >= (uint) stageLIR.SourceValueMapCount
                    || stageLIR.GetMappedNodeIndex(sourceOperand)
                        != mapped.GetOperand(operandIndex))
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidStageLIR,
                        sourceIndex,
                        $"Stage LIR source map entry %{sourceIndex} does not preserve operand {operandIndex}.");
                }
            }
        }

        private static void AppendDerivativeStageLIRMappingDiagnostics(
            MaterialStageLIR stageLIR,
            in MaterialValueNode source,
            int sourceIndex,
            int mappedNodeIndex,
            MaterialStageUniformity[] uniformityStates,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialStageLIRNode mapped = stageLIR.Nodes[mappedNodeIndex];
            bool isDdx = source.Opcode == MaterialValueOpcode.Ddx;
            if (MaterialStageUniformityAnalyzer.Analyze(
                    stageLIR.Values,
                    source.Operand0,
                    uniformityStates)
                == MaterialStageUniformity.Uniform)
            {
                if (mapped.Opcode != MaterialStageLIROpcode.Constant
                    || !IsDefaultBits(mapped.Constant))
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidStageLIR,
                        sourceIndex,
                        $"Uniform derivative source map entry %{sourceIndex} must lower to typed zero.");
                }
                return;
            }

            MaterialValueNode operand = stageLIR.Values.Nodes[source.Operand0];
            if (operand.Opcode == MaterialValueOpcode.ExternalInput
                && operand.Semantic == (int) MaterialExternalInput.UV0)
            {
                MaterialStageInput expectedInput = isDdx
                    ? MaterialStageInput.UV0Ddx
                    : MaterialStageInput.UV0Ddy;
                if (mapped.Opcode != MaterialStageLIROpcode.StageInput
                    || mapped.Semantic != (int) expectedInput)
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidStageLIR,
                        sourceIndex,
                        $"Direct UV derivative source map entry %{sourceIndex} must use {expectedInput}.");
                }
                return;
            }

            if (mapped.Opcode == MaterialStageLIROpcode.StageInput)
            {
                MaterialStageInput expectedInput = isDdx
                    ? MaterialStageInput.UV0Ddx
                    : MaterialStageInput.UV0Ddy;
                if (mapped.Semantic != (int) expectedInput)
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidStageLIR,
                        sourceIndex,
                        $"Derivative source map entry %{sourceIndex} uses the wrong gradient axis.");
                }
            }

            if ((uint) mapped.SourceNodeIndex
                >= (uint) stageLIR.Values.NodeCount)
            {
                return;
            }
            MaterialValueOpcode provenanceOpcode =
                stageLIR.Values.Nodes[mapped.SourceNodeIndex].Opcode;
            if (provenanceOpcode != source.Opcode)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidStageLIR,
                    sourceIndex,
                    $"Derivative source map entry %{sourceIndex} has incompatible axis provenance.");
            }
        }

        private static void AppendStageLIRReachabilityDiagnostics(
            MaterialStageLIR stageLIR,
            List<MaterialIRDiagnostic> diagnostics)
        {
            var reachable = new bool[stageLIR.NodeCount];
            var pending = new Stack<int>();
            for (int rootIndex = 0; rootIndex < stageLIR.Roots.Count; rootIndex++)
            {
                MaterialStageValue root = stageLIR.Roots[rootIndex];
                if (stageLIR.Owns(root))
                    pending.Push(root.Index);
            }

            while (pending.Count > 0)
            {
                int nodeIndex = pending.Pop();
                if ((uint) nodeIndex >= (uint) stageLIR.NodeCount
                    || reachable[nodeIndex])
                {
                    continue;
                }

                reachable[nodeIndex] = true;
                MaterialStageLIRNode node = stageLIR.Nodes[nodeIndex];
                int operandCount = Math.Min(Math.Max(node.OperandCount, 0), 4);
                for (int operandIndex = 0;
                     operandIndex < operandCount;
                     operandIndex++)
                {
                    int operand = node.GetOperand(operandIndex);
                    if ((uint) operand < (uint) stageLIR.NodeCount)
                        pending.Push(operand);
                }
            }

            for (int nodeIndex = 0; nodeIndex < reachable.Length; nodeIndex++)
            {
                if (!reachable[nodeIndex])
                {
                    AddStageLIRNodeError(
                        diagnostics,
                        stageLIR.Nodes[nodeIndex],
                        nodeIndex,
                        "is not reachable from a stage root.");
                }
            }
        }

        private static bool ContainsSourceNode(
            MaterialValueSlice sourceSlice,
            int sourceNodeIndex)
        {
            for (int i = 0; i < sourceSlice.NodeIndices.Count; i++)
            {
                if (sourceSlice.NodeIndices[i] == sourceNodeIndex)
                    return true;
            }
            return false;
        }

        private static MaterialValueType GetStageLIROperandType(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            int operandIndex)
        {
            return stageLIR.Nodes[node.GetOperand(operandIndex)].Type;
        }

        private static void RequireStageLIROperandType(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            int nodeIndex,
            int operandIndex,
            MaterialValueType expectedType,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType actualType = GetStageLIROperandType(
                stageLIR,
                node,
                operandIndex);
            if (actualType != expectedType)
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    $"operand {operandIndex} must be {expectedType}, got {actualType}.");
            }
        }

        private static void RequireStageLIRResultType(
            in MaterialStageLIRNode node,
            int nodeIndex,
            MaterialValueType expectedType,
            List<MaterialIRDiagnostic> diagnostics)
        {
            if (node.Type != expectedType)
            {
                AddStageLIRNodeError(
                    diagnostics,
                    node,
                    nodeIndex,
                    $"result must be {expectedType}, got {node.Type}.");
            }
        }

        private static void AddStageLIRNodeError(
            List<MaterialIRDiagnostic> diagnostics,
            in MaterialStageLIRNode node,
            int nodeIndex,
            string message)
        {
            AddNodeError(
                diagnostics,
                MaterialIRDiagnosticCodes.InvalidStageLIR,
                node.SourceNodeIndex,
                $"Stage LIR node %{nodeIndex} {message}");
        }

        internal static MaterialIRVerificationResult VerifyClosureGraph(
            ClosureExpressionGraph graph,
            MaterialClosure root,
            ClosureTopologyBudget budget)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            var diagnostics = new List<MaterialIRDiagnostic>();
            AppendClosureGraphDiagnostics(graph, root, budget, diagnostics);
            return CreateResult(diagnostics);
        }

        internal static MaterialIRVerificationResult VerifyTopology(
            ClosureTopology topology)
        {
            if (topology == null)
                throw new ArgumentNullException(nameof(topology));

            var diagnostics = new List<MaterialIRDiagnostic>();
            AppendTopologyDiagnostics(topology, diagnostics);
            return CreateResult(diagnostics);
        }

        private static void AppendValueGraphDiagnostics(
            MaterialValueIR values,
            List<MaterialIRDiagnostic> diagnostics)
        {
            for (int nodeIndex = 0; nodeIndex < values.Nodes.Count; nodeIndex++)
            {
                AppendNodeDiagnostics(
                    values,
                    values.Nodes[nodeIndex],
                    nodeIndex,
                    values.Nodes.Count,
                    diagnostics);
            }
        }

        private static void AppendDeclarationDiagnostics(
            MaterialValueIR values,
            List<MaterialIRDiagnostic> diagnostics)
        {
            for (int declarationIndex = 0;
                 declarationIndex < values.ParameterDeclarations.Count;
                 declarationIndex++)
            {
                MaterialParameterDeclaration declaration =
                    values.ParameterDeclarations[declarationIndex];
                if (string.IsNullOrEmpty(declaration.Symbol))
                {
                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidSemantic,
                        $"Parameter declaration {declarationIndex} has an empty symbol.");
                }
                if (!IsDataType(declaration.Type))
                {
                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidSemantic,
                        $"Parameter declaration {declarationIndex} has invalid type {declaration.Type}.");
                }

                for (int previousIndex = 0;
                     previousIndex < declarationIndex;
                     previousIndex++)
                {
                    if (!string.Equals(
                            values.ParameterDeclarations[previousIndex].Symbol,
                            declaration.Symbol,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidSemantic,
                        $"Parameter declarations {previousIndex} and {declarationIndex} "
                        + $"use the same symbol '{declaration.Symbol}'.");
                    break;
                }
            }

            for (int declarationIndex = 0;
                 declarationIndex < values.ResourceDeclarations.Count;
                 declarationIndex++)
            {
                MaterialResourceDeclaration declaration =
                    values.ResourceDeclarations[declarationIndex];
                if (string.IsNullOrEmpty(declaration.Symbol))
                {
                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidSemantic,
                        $"Resource declaration {declarationIndex} has an empty symbol.");
                }
                if (declaration.Type != MaterialValueType.Texture2D)
                {
                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidSemantic,
                        $"Resource declaration {declarationIndex} has invalid type {declaration.Type}.");
                }

                for (int previousIndex = 0;
                     previousIndex < declarationIndex;
                     previousIndex++)
                {
                    if (!string.Equals(
                            values.ResourceDeclarations[previousIndex].Symbol,
                            declaration.Symbol,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidSemantic,
                        $"Resource declarations {previousIndex} and {declarationIndex} "
                        + $"use the same symbol '{declaration.Symbol}'.");
                    break;
                }
            }
        }

        private static void AppendNodeDiagnostics(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            int totalNodeCount,
            List<MaterialIRDiagnostic> diagnostics)
        {
            if (!MaterialOpcodeTable.TryGetInfo(node.Opcode, out MaterialOpcodeInfo info))
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.UnknownOpcode,
                    nodeIndex,
                    $"Opcode value {(int) node.Opcode} is not defined by the current Material IR schema.");
                return;
            }

            bool hasKnownResultType = IsKnownType(node.Type);
            if (!hasKnownResultType)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.UnknownValueType,
                    nodeIndex,
                    $"Result type value {(int) node.Type} is not defined by the current Material IR schema.");
            }

            int activeOperandCount = 0;
            bool sawInactiveOperand = false;
            bool hasValidOperandEncoding = true;
            bool hasValidOperandIndices = true;
            for (int operandIndex = 0; operandIndex < 4; operandIndex++)
            {
                int operand = GetOperand(node, operandIndex);
                if (operand == InvalidOperand)
                {
                    sawInactiveOperand = true;
                    continue;
                }

                if (operand < InvalidOperand || sawInactiveOperand)
                {
                    hasValidOperandEncoding = false;
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidOperandEncoding,
                        nodeIndex,
                        operand < InvalidOperand
                            ? $"Operand {operandIndex} uses invalid sentinel {operand}; only -1 is valid."
                            : $"Operand {operandIndex} is active after an inactive operand.");
                }
                if (operand < 0)
                    continue;

                activeOperandCount++;
                if (operand >= totalNodeCount)
                {
                    hasValidOperandIndices = false;
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.OperandOutOfRange,
                        nodeIndex,
                        $"Operand {operandIndex} references node %{operand}, but the graph has {totalNodeCount} nodes.");
                }
                else if (operand >= nodeIndex)
                {
                    hasValidOperandIndices = false;
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.NonTopologicalOperand,
                        nodeIndex,
                        $"Operand {operandIndex} references non-predecessor node %{operand}.");
                }
            }

            if (activeOperandCount < info.MinOperandCount
                || activeOperandCount > info.MaxOperandCount)
            {
                hasValidOperandEncoding = false;
                string expectedCount = info.MinOperandCount == info.MaxOperandCount
                    ? info.MinOperandCount.ToString()
                    : $"{info.MinOperandCount} to {info.MaxOperandCount}";
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidOperandEncoding,
                    nodeIndex,
                    $"Opcode {info.Name} requires {expectedCount} operands, got {activeOperandCount}.");
            }

            bool hasValidPayload = AppendPayloadDiagnostics(
                values,
                node,
                nodeIndex,
                info,
                diagnostics);
            if (!hasKnownResultType
                || !hasValidOperandEncoding
                || !hasValidOperandIndices
                || !hasValidPayload)
            {
                return;
            }

            AppendSignatureDiagnostics(
                values,
                node,
                nodeIndex,
                activeOperandCount,
                info,
                diagnostics);
        }

        private static bool AppendPayloadDiagnostics(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            in MaterialOpcodeInfo info,
            List<MaterialIRDiagnostic> diagnostics)
        {
            bool isValid = true;
            if (info.PayloadKind != MaterialOpcodePayloadKind.Constant
                && !IsDefaultBits(node.Constant))
            {
                isValid = false;
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.NonCanonicalPayload,
                    nodeIndex,
                    $"Opcode {info.Name} must not carry a constant payload.");
            }

            switch (info.PayloadKind)
            {
                case MaterialOpcodePayloadKind.None:
                case MaterialOpcodePayloadKind.Constant:
                    if (node.Semantic != 0)
                    {
                        isValid = false;
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.NonCanonicalPayload,
                            nodeIndex,
                            $"Opcode {info.Name} must use semantic payload 0.");
                    }
                    break;
                case MaterialOpcodePayloadKind.ExternalInput:
                    if (!TryGetExternalInputType(node.Semantic, out _))
                    {
                        isValid = false;
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidSemantic,
                            nodeIndex,
                            $"External input semantic {node.Semantic} is not defined.");
                    }
                    break;
                case MaterialOpcodePayloadKind.ParameterDeclaration:
                    if (!values.TryGetParameterDeclaration(
                            node.Semantic,
                            out MaterialParameterDeclaration parameterDeclaration))
                    {
                        isValid = false;
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidSemantic,
                            nodeIndex,
                            $"Parameter declaration index {node.Semantic} is not defined.");
                    }
                    else if (!IsDataType(parameterDeclaration.Type))
                    {
                        isValid = false;
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidSemantic,
                            nodeIndex,
                            $"Parameter '{parameterDeclaration.Symbol}' has invalid type {parameterDeclaration.Type}.");
                    }
                    break;
                case MaterialOpcodePayloadKind.ResourceDeclaration:
                    if (!values.TryGetResourceDeclaration(
                            node.Semantic,
                            out MaterialResourceDeclaration resourceDeclaration))
                    {
                        isValid = false;
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidSemantic,
                            nodeIndex,
                            $"Resource declaration index {node.Semantic} is not defined.");
                    }
                    else if (resourceDeclaration.Type != MaterialValueType.Texture2D)
                    {
                        isValid = false;
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidSemantic,
                            nodeIndex,
                            $"Resource '{resourceDeclaration.Symbol}' has invalid type {resourceDeclaration.Type}.");
                    }
                    break;
                case MaterialOpcodePayloadKind.SwizzleMask:
                    if (!MaterialSwizzleMask.TryDecode(
                            node.Semantic,
                            out MaterialSwizzleMask _))
                    {
                        isValid = false;
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidSemantic,
                            nodeIndex,
                            $"Swizzle payload 0x{node.Semantic:X8} is not canonical.");
                    }
                    break;
                case MaterialOpcodePayloadKind.Comparison:
                    if (node.Semantic < 0 || node.Semantic > 5)
                    {
                        isValid = false;
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidSemantic,
                            nodeIndex,
                            $"Comparison semantic {node.Semantic} is not defined.");
                    }
                    break;
                default:
                    isValid = false;
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidSemantic,
                        nodeIndex,
                        $"Payload kind {(int) info.PayloadKind} is not defined.");
                    break;
            }
            return isValid;
        }

        private static void AppendSignatureDiagnostics(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            int operandCount,
            in MaterialOpcodeInfo info,
            List<MaterialIRDiagnostic> diagnostics)
        {
            switch (info.SignatureKind)
            {
                case MaterialOpcodeSignatureKind.Constant:
                    VerifyConstant(node, nodeIndex, diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.ExternalInput:
                    TryGetExternalInputType(node.Semantic, out MaterialValueType externalType);
                    RequireResultType(node, nodeIndex, externalType, diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.Parameter:
                    values.TryGetParameterDeclaration(
                        node.Semantic,
                        out MaterialParameterDeclaration parameterDeclaration);
                    RequireResultType(
                        node,
                        nodeIndex,
                        parameterDeclaration.Type,
                        diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.TextureResource:
                    values.TryGetResourceDeclaration(
                        node.Semantic,
                        out MaterialResourceDeclaration resourceDeclaration);
                    RequireResultType(
                        node,
                        nodeIndex,
                        resourceDeclaration.Type,
                        diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.TextureSampleGrad:
                    RequireOperandType(
                        values,
                        node,
                        nodeIndex,
                        0,
                        MaterialValueType.Texture2D,
                        diagnostics);
                    RequireOperandType(
                        values,
                        node,
                        nodeIndex,
                        1,
                        MaterialValueType.Float2,
                        diagnostics);
                    RequireOperandType(
                        values,
                        node,
                        nodeIndex,
                        2,
                        MaterialValueType.Float2,
                        diagnostics);
                    RequireOperandType(
                        values,
                        node,
                        nodeIndex,
                        3,
                        MaterialValueType.Float2,
                        diagnostics);
                    RequireResultType(
                        node,
                        nodeIndex,
                        MaterialValueType.Float4,
                        diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.UnarySameNumeric:
                    VerifyUnarySameNumeric(values, node, nodeIndex, diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.BinarySameNumeric:
                    VerifyBinarySameNumeric(values, node, nodeIndex, diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.Lerp:
                    VerifyLerp(values, node, nodeIndex, diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.Select:
                    VerifySelect(values, node, nodeIndex, diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.Swizzle:
                    VerifySwizzle(values, node, nodeIndex, diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.Compose:
                    VerifyCompose(
                        values,
                        node,
                        nodeIndex,
                        operandCount,
                        diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.Dot:
                    VerifyDot(values, node, nodeIndex, diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.Normalize:
                    VerifyNormalize(values, node, nodeIndex, diagnostics);
                    break;
                case MaterialOpcodeSignatureKind.Compare:
                    VerifyCompare(values, node, nodeIndex, diagnostics);
                    break;
                default:
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.ResultTypeMismatch,
                        nodeIndex,
                        $"Signature kind {(int) info.SignatureKind} is not defined.");
                    break;
            }
        }

        private static void VerifyConstant(
            in MaterialValueNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            if (!IsDataType(node.Type))
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.ResultTypeMismatch,
                    nodeIndex,
                    $"Constant result must be Bool or numeric, got {node.Type}.");
                return;
            }

            uint4 bits = math.asuint(node.Constant);
            if (node.Type == MaterialValueType.Bool
                && bits.x != 0u
                && bits.x != math.asuint(1.0f))
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.NonCanonicalPayload,
                    nodeIndex,
                    "Bool constant payload must be exactly 0 or 1.");
            }

            int componentCount = GetComponentCount(node.Type);
            for (int componentIndex = componentCount; componentIndex < 4; componentIndex++)
            {
                if (bits[componentIndex] == 0u)
                    continue;

                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.NonCanonicalPayload,
                    nodeIndex,
                    $"Unused constant component {componentIndex} must be positive zero.");
            }
        }

        private static void VerifyUnarySameNumeric(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType operandType = GetOperandType(values, node, 0);
            if (!IsNumericType(operandType))
            {
                AddOperandTypeError(
                    diagnostics,
                    nodeIndex,
                    0,
                    "numeric",
                    operandType);
                return;
            }
            RequireResultType(node, nodeIndex, operandType, diagnostics);
        }

        private static void VerifyBinarySameNumeric(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType leftType = GetOperandType(values, node, 0);
            MaterialValueType rightType = GetOperandType(values, node, 1);
            if (!IsNumericType(leftType))
                AddOperandTypeError(diagnostics, nodeIndex, 0, "numeric", leftType);
            if (!IsNumericType(rightType))
                AddOperandTypeError(diagnostics, nodeIndex, 1, "numeric", rightType);
            if (!IsNumericType(leftType) || !IsNumericType(rightType))
                return;
            if (leftType != rightType)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.OperandTypeMismatch,
                    nodeIndex,
                    $"Binary operands must have matching types, got {leftType} and {rightType}.");
                return;
            }
            RequireResultType(node, nodeIndex, leftType, diagnostics);
        }

        private static void VerifyLerp(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType leftType = GetOperandType(values, node, 0);
            MaterialValueType rightType = GetOperandType(values, node, 1);
            if (!IsNumericType(leftType))
                AddOperandTypeError(diagnostics, nodeIndex, 0, "numeric", leftType);
            if (!IsNumericType(rightType))
                AddOperandTypeError(diagnostics, nodeIndex, 1, "numeric", rightType);
            if (leftType != rightType)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.OperandTypeMismatch,
                    nodeIndex,
                    $"Lerp value operands must match, got {leftType} and {rightType}.");
            }
            RequireOperandType(
                values,
                node,
                nodeIndex,
                2,
                MaterialValueType.Float,
                diagnostics);
            if (IsNumericType(leftType) && leftType == rightType)
                RequireResultType(node, nodeIndex, leftType, diagnostics);
        }

        private static void VerifySelect(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            RequireOperandType(
                values,
                node,
                nodeIndex,
                0,
                MaterialValueType.Bool,
                diagnostics);
            MaterialValueType trueType = GetOperandType(values, node, 1);
            MaterialValueType falseType = GetOperandType(values, node, 2);
            if (!IsDataType(trueType))
                AddOperandTypeError(diagnostics, nodeIndex, 1, "Bool or numeric", trueType);
            if (!IsDataType(falseType))
                AddOperandTypeError(diagnostics, nodeIndex, 2, "Bool or numeric", falseType);
            if (trueType != falseType)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.OperandTypeMismatch,
                    nodeIndex,
                    $"Select values must match, got {trueType} and {falseType}.");
                return;
            }
            if (IsDataType(trueType))
                RequireResultType(node, nodeIndex, trueType, diagnostics);
        }

        private static void VerifySwizzle(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType sourceType = GetOperandType(values, node, 0);
            if (!IsNumericType(sourceType))
            {
                AddOperandTypeError(diagnostics, nodeIndex, 0, "numeric", sourceType);
                return;
            }

            MaterialSwizzleMask.TryDecode(
                node.Semantic,
                out MaterialSwizzleMask mask);
            int sourceComponentCount = GetComponentCount(sourceType);
            for (int componentIndex = 0;
                 componentIndex < mask.ComponentCount;
                 componentIndex++)
            {
                int sourceComponent = mask.GetComponent(componentIndex);
                if ((uint) sourceComponent < (uint) sourceComponentCount)
                    continue;

                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.OperandTypeMismatch,
                    nodeIndex,
                    $"Swizzle component {sourceComponent} is unavailable on {sourceType}.");
            }
            RequireResultType(node, nodeIndex, mask.ResultType, diagnostics);
        }

        private static void VerifyCompose(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            int operandCount,
            List<MaterialIRDiagnostic> diagnostics)
        {
            for (int operandIndex = 0; operandIndex < operandCount; operandIndex++)
            {
                RequireOperandType(
                    values,
                    node,
                    nodeIndex,
                    operandIndex,
                    MaterialValueType.Float,
                    diagnostics);
            }
            RequireResultType(
                node,
                nodeIndex,
                GetFloatType(operandCount),
                diagnostics);
        }

        private static void VerifyDot(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType leftType = GetOperandType(values, node, 0);
            MaterialValueType rightType = GetOperandType(values, node, 1);
            if (!IsVectorType(leftType))
                AddOperandTypeError(diagnostics, nodeIndex, 0, "Float2, Float3, or Float4", leftType);
            if (!IsVectorType(rightType))
                AddOperandTypeError(diagnostics, nodeIndex, 1, "Float2, Float3, or Float4", rightType);
            if (leftType != rightType)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.OperandTypeMismatch,
                    nodeIndex,
                    $"Dot operands must match, got {leftType} and {rightType}.");
            }
            RequireResultType(
                node,
                nodeIndex,
                MaterialValueType.Float,
                diagnostics);
        }

        private static void VerifyNormalize(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType operandType = GetOperandType(values, node, 0);
            if (!IsVectorType(operandType))
            {
                AddOperandTypeError(
                    diagnostics,
                    nodeIndex,
                    0,
                    "Float2, Float3, or Float4",
                    operandType);
                return;
            }
            RequireResultType(node, nodeIndex, operandType, diagnostics);
        }

        private static void VerifyCompare(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType leftType = GetOperandType(values, node, 0);
            MaterialValueType rightType = GetOperandType(values, node, 1);
            if (!IsNumericType(leftType))
                AddOperandTypeError(diagnostics, nodeIndex, 0, "numeric", leftType);
            if (!IsNumericType(rightType))
                AddOperandTypeError(diagnostics, nodeIndex, 1, "numeric", rightType);
            if (IsNumericType(leftType)
                && IsNumericType(rightType)
                && leftType != rightType)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.OperandTypeMismatch,
                    nodeIndex,
                    $"Compare operands must have matching types, got {leftType} and {rightType}.");
            }
            RequireResultType(
                node,
                nodeIndex,
                MaterialValueType.Bool,
                diagnostics);
        }

        private static void AppendOutputDiagnostics(
            MaterialValueIR values,
            MaterialValue output,
            MaterialValueType expectedType,
            string outputName,
            List<MaterialIRDiagnostic> diagnostics)
        {
            if (!values.Owns(output))
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.OutputNotOwned,
                    $"Material output '{outputName}' is not owned by the module value IR.");
                return;
            }
            if (output.Type == expectedType)
                return;

            AddNodeError(
                diagnostics,
                MaterialIRDiagnosticCodes.OutputTypeMismatch,
                output.Index,
                $"Material output '{outputName}' must be {expectedType}, got {output.Type}.");
        }

        private static void AppendClosureGraphDiagnostics(
            ClosureExpressionGraph graph,
            MaterialClosure root,
            ClosureTopologyBudget budget,
            List<MaterialIRDiagnostic> diagnostics)
        {
            for (int nodeIndex = 0; nodeIndex < graph.NodeCount; nodeIndex++)
            {
                AppendClosureNodeDiagnostics(
                    graph,
                    graph.Nodes[nodeIndex],
                    nodeIndex,
                    graph.NodeCount,
                    diagnostics);
            }

            if (!graph.Owns(root))
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.ClosureRootNotOwned,
                    "Closure root is not owned by the closure expression graph.");
                return;
            }

            int nodeCount = graph.NodeCount;
            var reachable = new bool[nodeCount];
            var incomingEdges = new int[nodeCount];
            var pending = new Stack<int>();
            pending.Push(root.Index);

            int closureCount = 0;
            int operatorCount = 0;
            while (pending.Count > 0)
            {
                int nodeIndex = pending.Pop();
                if (reachable[nodeIndex])
                    continue;

                reachable[nodeIndex] = true;
                ClosureExpressionNode node = graph.Nodes[nodeIndex];
                switch (node.Opcode)
                {
                    case ClosureExpressionOpcode.Slab:
                        closureCount++;
                        break;
                    case ClosureExpressionOpcode.HorizontalMix:
                    case ClosureExpressionOpcode.VerticalLayer:
                        operatorCount++;
                        AppendReachableClosureEdge(
                            node.Operand0,
                            nodeIndex,
                            nodeCount,
                            incomingEdges,
                            pending);
                        AppendReachableClosureEdge(
                            node.Operand1,
                            nodeIndex,
                            nodeCount,
                            incomingEdges,
                            pending);
                        break;
                }
            }

            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                if (!reachable[nodeIndex] || nodeIndex == root.Index)
                    continue;
                if (incomingEdges[nodeIndex] == 1)
                    continue;

                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.ClosureGraphFanOut,
                    nodeIndex,
                    incomingEdges[nodeIndex] > 1
                        ? $"Reachable closure node has {incomingEdges[nodeIndex]} parents; closure occurrences cannot fan out."
                        : "Reachable non-root closure node must have exactly one parent.");
            }

            AppendClosurePrototypeShapeDiagnostics(
                graph,
                root,
                closureCount,
                operatorCount,
                diagnostics);

            if (!budget.Allows(closureCount, operatorCount))
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.ClosureGraphBudgetExceeded,
                    $"Reachable closure graph requires {closureCount} closures and "
                    + $"{operatorCount} operators, but its budget allows "
                    + $"{budget.MaxClosureCount} closures and "
                    + $"{budget.MaxOperatorCount} operators.");
            }
        }

        private static void AppendClosureNodeDiagnostics(
            ClosureExpressionGraph graph,
            in ClosureExpressionNode node,
            int nodeIndex,
            int nodeCount,
            List<MaterialIRDiagnostic> diagnostics)
        {
            switch (node.Opcode)
            {
                case ClosureExpressionOpcode.Slab:
                    if (node.Operand0 != InvalidOperand
                        || node.Operand1 != InvalidOperand
                        || node.Weight != default)
                    {
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidClosureOperandEncoding,
                            nodeIndex,
                            "Slab closure nodes cannot contain operator operands or weight.");
                    }

                    AppendClosureValueDiagnostics(
                        graph.ValueIR,
                        node.Slab.BaseColor,
                        MaterialValueType.Float4,
                        "slab base color",
                        nodeIndex,
                        diagnostics);
                    AppendClosureValueDiagnostics(
                        graph.ValueIR,
                        node.Slab.Roughness,
                        MaterialValueType.Float,
                        "slab roughness",
                        nodeIndex,
                        diagnostics);
                    AppendClosureValueDiagnostics(
                        graph.ValueIR,
                        node.Slab.Metallic,
                        MaterialValueType.Float,
                        "slab metallic",
                        nodeIndex,
                        diagnostics);
                    AppendClosureValueDiagnostics(
                        graph.ValueIR,
                        node.Slab.Normal,
                        MaterialValueType.Float3,
                        "slab normal",
                        nodeIndex,
                        diagnostics);
                    AppendClosureValueDiagnostics(
                        graph.ValueIR,
                        node.Slab.Tangent,
                        MaterialValueType.Float4,
                        "slab tangent",
                        nodeIndex,
                        diagnostics);

                    int unknownFeatures =
                        (int) node.Slab.Features & ~KnownClosureFeatureBits;
                    if (unknownFeatures != 0)
                    {
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidClosureFeature,
                            nodeIndex,
                            $"Closure slab contains unknown feature bits 0x{unknownFeatures:X}.");
                    }
                    break;

                case ClosureExpressionOpcode.HorizontalMix:
                case ClosureExpressionOpcode.VerticalLayer:
                    if (HasSlabPayload(node.Slab))
                    {
                        AddNodeError(
                            diagnostics,
                            MaterialIRDiagnosticCodes.InvalidClosureOperandEncoding,
                            nodeIndex,
                            "Closure operator nodes cannot contain a slab payload.");
                    }
                    AppendClosureOperandDiagnostics(
                        node.Operand0,
                        0,
                        nodeIndex,
                        nodeCount,
                        diagnostics);
                    AppendClosureOperandDiagnostics(
                        node.Operand1,
                        1,
                        nodeIndex,
                        nodeCount,
                        diagnostics);
                    AppendClosureValueDiagnostics(
                        graph.ValueIR,
                        node.Weight,
                        MaterialValueType.Float,
                        "closure operator weight",
                        nodeIndex,
                        diagnostics);
                    break;

                default:
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.UnknownClosureOpcode,
                        nodeIndex,
                        $"Closure opcode value {(int) node.Opcode} is not defined.");
                    break;
            }
        }

        private static bool HasSlabPayload(in ClosureSlabExpression slab)
        {
            return slab.BaseColor != default
                || slab.Roughness != default
                || slab.Metallic != default
                || slab.Normal != default
                || slab.Tangent != default
                || slab.Features != ClosureFeatureMask.None;
        }

        private static void AppendClosureOperandDiagnostics(
            int operand,
            int operandIndex,
            int nodeIndex,
            int nodeCount,
            List<MaterialIRDiagnostic> diagnostics)
        {
            if (operand == InvalidOperand)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidClosureOperandEncoding,
                    nodeIndex,
                    $"Closure operand {operandIndex} is required.");
                return;
            }
            if ((uint) operand >= (uint) nodeCount)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.ClosureOperandOutOfRange,
                    nodeIndex,
                    $"Closure operand {operandIndex} references node {operand}, "
                    + $"outside [0, {nodeCount}).");
                return;
            }
            if (operand < nodeIndex)
                return;

            AddNodeError(
                diagnostics,
                MaterialIRDiagnosticCodes.NonTopologicalClosureOperand,
                nodeIndex,
                $"Closure operand {operandIndex} references node {operand}; "
                + "closure operands must precede their user.");
        }

        private static void AppendClosureValueDiagnostics(
            MaterialValueIR values,
            MaterialValue value,
            MaterialValueType expectedType,
            string description,
            int closureNodeIndex,
            List<MaterialIRDiagnostic> diagnostics)
        {
            if (!values.Owns(value))
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidClosureValue,
                    closureNodeIndex,
                    $"The {description} is not owned by the closure graph value IR.");
                return;
            }
            if (value.Type == expectedType)
                return;

            AddNodeError(
                diagnostics,
                MaterialIRDiagnosticCodes.InvalidClosureValue,
                closureNodeIndex,
                $"The {description} must be {expectedType}, got {value.Type}.");
        }

        private static void AppendReachableClosureEdge(
            int childIndex,
            int parentIndex,
            int nodeCount,
            int[] incomingEdges,
            Stack<int> pending)
        {
            if ((uint) childIndex >= (uint) nodeCount
                || childIndex >= parentIndex)
            {
                return;
            }

            incomingEdges[childIndex]++;
            pending.Push(childIndex);
        }

        private static void AppendClosurePrototypeShapeDiagnostics(
            ClosureExpressionGraph graph,
            MaterialClosure root,
            int closureCount,
            int operatorCount,
            List<MaterialIRDiagnostic> diagnostics)
        {
            ClosureExpressionNode rootNode = graph.Nodes[root.Index];
            if (rootNode.Opcode == ClosureExpressionOpcode.Slab)
            {
                if (closureCount != 1 || operatorCount != 0)
                {
                    AddNodeError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidClosureGraphShape,
                        root.Index,
                        "A slab root must resolve to exactly one slab and no operators.");
                }
                return;
            }

            if (rootNode.Opcode != ClosureExpressionOpcode.HorizontalMix
                && rootNode.Opcode != ClosureExpressionOpcode.VerticalLayer)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidClosureGraphShape,
                    root.Index,
                    "Closure graph root must be a slab or a supported closure operator.");
                return;
            }

            bool operandsAreValid =
                IsValidClosureChild(rootNode.Operand0, root.Index, graph.NodeCount)
                && IsValidClosureChild(rootNode.Operand1, root.Index, graph.NodeCount);
            if (operandsAreValid
                && (graph.Nodes[rootNode.Operand0].Opcode
                        != ClosureExpressionOpcode.Slab
                    || graph.Nodes[rootNode.Operand1].Opcode
                        != ClosureExpressionOpcode.Slab))
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidClosureGraphShape,
                    root.Index,
                    "Prototype closure operators must reference two direct slab operands.");
            }

            if (closureCount == 2 && operatorCount == 1)
                return;

            AddNodeError(
                diagnostics,
                MaterialIRDiagnosticCodes.InvalidClosureGraphShape,
                root.Index,
                "A prototype operator root must resolve to exactly two slabs "
                + "and one operator at depth one.");
        }

        private static bool IsValidClosureChild(
            int childIndex,
            int parentIndex,
            int nodeCount)
        {
            return (uint) childIndex < (uint) nodeCount
                && childIndex < parentIndex;
        }

        private static void AppendTopologyDiagnostics(
            ClosureTopology topology,
            List<MaterialIRDiagnostic> diagnostics)
        {
            if (topology.ClosureCount == 0)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidTopologyShape,
                    "Closure topology must contain at least one slab.");
            }
            if (!topology.IsWithinBudget)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.TopologyBudgetExceeded,
                    $"Closure topology requires {topology.ClosureCount} closures and "
                    + $"{topology.OperatorCount} operators, but its budget allows "
                    + $"{topology.Budget.MaxClosureCount} closures and "
                    + $"{topology.Budget.MaxOperatorCount} operators.");
            }

            for (int basisIndex = 0; basisIndex < topology.NormalBases.Count; basisIndex++)
            {
                ClosureNormalBasis basis = topology.NormalBases[basisIndex];
                AppendTopologyValueDiagnostics(
                    topology.ValueIR,
                    basis.Normal,
                    MaterialValueType.Float3,
                    $"normal basis {basisIndex} normal",
                    diagnostics);
                AppendTopologyValueDiagnostics(
                    topology.ValueIR,
                    basis.Tangent,
                    MaterialValueType.Float4,
                    $"normal basis {basisIndex} tangent",
                    diagnostics);
            }

            for (int slabIndex = 0; slabIndex < topology.Slabs.Count; slabIndex++)
            {
                ClosureSlab slab = topology.Slabs[slabIndex];
                AppendTopologyValueDiagnostics(
                    topology.ValueIR,
                    slab.BaseColor,
                    MaterialValueType.Float4,
                    $"slab {slabIndex} base color",
                    diagnostics);
                AppendTopologyValueDiagnostics(
                    topology.ValueIR,
                    slab.Roughness,
                    MaterialValueType.Float,
                    $"slab {slabIndex} roughness",
                    diagnostics);
                AppendTopologyValueDiagnostics(
                    topology.ValueIR,
                    slab.Metallic,
                    MaterialValueType.Float,
                    $"slab {slabIndex} metallic",
                    diagnostics);

                if ((uint) slab.NormalBasisIndex >= (uint) topology.NormalBases.Count)
                {
                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidTopologyIndex,
                        $"Slab {slabIndex} references invalid normal basis {slab.NormalBasisIndex}.");
                }

                int unknownFeatures = (int) slab.Features & ~KnownClosureFeatureBits;
                if (unknownFeatures != 0)
                {
                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidTopologySemantic,
                        $"Slab {slabIndex} contains unknown feature bits 0x{unknownFeatures:X}.");
                }
            }

            if (topology.ClosureCount == 1)
            {
                if (topology.OperatorCount != 0)
                {
                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidTopologyShape,
                        "A single-slab topology cannot contain an operator.");
                }
                if (!topology.Slabs[0].IsTop || !topology.Slabs[0].IsBottom)
                {
                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidTopologyShape,
                        "A single slab must be both top and bottom.");
                }
                return;
            }

            if (topology.ClosureCount != 2 || topology.OperatorCount != 1)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidTopologyShape,
                    "The closure topology projection supports one slab or two slabs connected by one operator.");
                return;
            }

            ClosureOperator closureOperator = topology.Operators[0];
            if (closureOperator.Kind != ClosureOperatorKind.HorizontalMix
                && closureOperator.Kind != ClosureOperatorKind.VerticalLayer)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidTopologySemantic,
                    $"Closure operator kind value {(int) closureOperator.Kind} is not defined.");
            }
            if (closureOperator.BackgroundSlabIndex != 0
                || closureOperator.ForegroundSlabIndex != 1)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidTopologyIndex,
                    "Dual-slab topology requires background slab 0 and foreground slab 1.");
            }
            AppendTopologyValueDiagnostics(
                topology.ValueIR,
                closureOperator.Weight,
                MaterialValueType.Float,
                "closure operator weight",
                diagnostics);
            if (closureOperator.Kind == ClosureOperatorKind.HorizontalMix)
            {
                for (int slabIndex = 0; slabIndex < topology.Slabs.Count; slabIndex++)
                {
                    if (topology.Slabs[slabIndex].IsTop
                        && topology.Slabs[slabIndex].IsBottom)
                    {
                        continue;
                    }

                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidTopologyShape,
                        $"Horizontal slab {slabIndex} must be marked as both top and bottom.");
                }
            }
            else if (closureOperator.Kind == ClosureOperatorKind.VerticalLayer)
            {
                if (topology.Slabs[0].IsTop || !topology.Slabs[0].IsBottom)
                {
                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidTopologyShape,
                        "The vertical bottom slab must be marked as bottom only.");
                }
                if (!topology.Slabs[1].IsTop || topology.Slabs[1].IsBottom)
                {
                    AddError(
                        diagnostics,
                        MaterialIRDiagnosticCodes.InvalidTopologyShape,
                        "The vertical top slab must be marked as top only.");
                }
            }
        }

        private static void AppendTopologyValueDiagnostics(
            MaterialValueIR values,
            MaterialValue value,
            MaterialValueType expectedType,
            string description,
            List<MaterialIRDiagnostic> diagnostics)
        {
            if (!values.Owns(value))
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidTopologyValue,
                    $"The {description} is not owned by the topology value IR.");
                return;
            }
            if (value.Type == expectedType)
                return;

            AddNodeError(
                diagnostics,
                MaterialIRDiagnosticCodes.InvalidTopologyValue,
                value.Index,
                $"The {description} must be {expectedType}, got {value.Type}.");
        }

        private static void RequireOperandType(
            MaterialValueIR values,
            in MaterialValueNode node,
            int nodeIndex,
            int operandIndex,
            MaterialValueType expectedType,
            List<MaterialIRDiagnostic> diagnostics)
        {
            MaterialValueType actualType = GetOperandType(values, node, operandIndex);
            if (actualType == expectedType)
                return;
            AddOperandTypeError(
                diagnostics,
                nodeIndex,
                operandIndex,
                expectedType.ToString(),
                actualType);
        }

        private static void RequireResultType(
            in MaterialValueNode node,
            int nodeIndex,
            MaterialValueType expectedType,
            List<MaterialIRDiagnostic> diagnostics)
        {
            if (node.Type == expectedType)
                return;
            AddNodeError(
                diagnostics,
                MaterialIRDiagnosticCodes.ResultTypeMismatch,
                nodeIndex,
                $"Opcode {node.Opcode} must return {expectedType}, got {node.Type}.");
        }

        private static void AddOperandTypeError(
            List<MaterialIRDiagnostic> diagnostics,
            int nodeIndex,
            int operandIndex,
            string expected,
            MaterialValueType actual)
        {
            AddNodeError(
                diagnostics,
                MaterialIRDiagnosticCodes.OperandTypeMismatch,
                nodeIndex,
                $"Operand {operandIndex} must be {expected}, got {actual}.");
        }

        private static MaterialValueType GetOperandType(
            MaterialValueIR values,
            in MaterialValueNode node,
            int operandIndex)
        {
            return values.Nodes[GetOperand(node, operandIndex)].Type;
        }

        private static int GetOperand(in MaterialValueNode node, int operandIndex)
        {
            switch (operandIndex)
            {
                case 0:
                    return node.Operand0;
                case 1:
                    return node.Operand1;
                case 2:
                    return node.Operand2;
                case 3:
                    return node.Operand3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operandIndex));
            }
        }

        private static bool TryGetExternalInputType(
            int semantic,
            out MaterialValueType type)
        {
            switch ((MaterialExternalInput) semantic)
            {
                case MaterialExternalInput.UV0:
                    type = MaterialValueType.Float2;
                    return true;
                case MaterialExternalInput.GeometryNormalWS:
                    type = MaterialValueType.Float3;
                    return true;
                case MaterialExternalInput.GeometryTangentWS:
                    type = MaterialValueType.Float4;
                    return true;
                default:
                    type = default;
                    return false;
            }
        }

        private static MaterialValueType GetFloatType(int componentCount)
        {
            switch (componentCount)
            {
                case 2:
                    return MaterialValueType.Float2;
                case 3:
                    return MaterialValueType.Float3;
                case 4:
                    return MaterialValueType.Float4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(componentCount));
            }
        }

        private static int GetComponentCount(MaterialValueType type)
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
                    return 0;
            }
        }

        private enum StageDerivativeAvailability
        {
            Unknown = 0,
            Zero = 1,
            Valid = 2,
            Unavailable = 3,
        }

        private static StageDerivativeAvailability AnalyzeStageDerivative(
            MaterialValueIR values,
            int nodeIndex,
            StageDerivativeAvailability[] states,
            MaterialStageUniformity[] uniformityStates)
        {
            StageDerivativeAvailability cached = states[nodeIndex];
            if (cached != StageDerivativeAvailability.Unknown)
                return cached;

            if (MaterialStageUniformityAnalyzer.Analyze(
                    values,
                    nodeIndex,
                    uniformityStates)
                == MaterialStageUniformity.Uniform)
            {
                states[nodeIndex] = StageDerivativeAvailability.Zero;
                return StageDerivativeAvailability.Zero;
            }

            MaterialValueNode node = values.Nodes[nodeIndex];
            StageDerivativeAvailability result;
            switch (MaterialDerivativeLegalizationRules.GetRule(node.Opcode))
            {
                case MaterialDerivativeLegalizationRule.Zero:
                    result = StageDerivativeAvailability.Zero;
                    break;
                case MaterialDerivativeLegalizationRule.StageInput:
                    result = node.Semantic == (int) MaterialExternalInput.UV0
                        ? StageDerivativeAvailability.Valid
                        : StageDerivativeAvailability.Unavailable;
                    break;
                case MaterialDerivativeLegalizationRule.Add:
                case MaterialDerivativeLegalizationRule.Subtract:
                    result = MergeDerivativeAvailability(
                        AnalyzeStageDerivative(
                            values,
                            node.Operand0,
                            states,
                            uniformityStates),
                        AnalyzeStageDerivative(
                            values,
                            node.Operand1,
                            states,
                            uniformityStates));
                    break;
                case MaterialDerivativeLegalizationRule.Multiply:
                    StageDerivativeAvailability left = AnalyzeStageDerivative(
                        values,
                        node.Operand0,
                        states,
                        uniformityStates);
                    StageDerivativeAvailability right = AnalyzeStageDerivative(
                        values,
                        node.Operand1,
                        states,
                        uniformityStates);
                    result = left == StageDerivativeAvailability.Valid
                        && right == StageDerivativeAvailability.Valid
                            ? StageDerivativeAvailability.Unavailable
                            : MergeDerivativeAvailability(left, right);
                    break;
                case MaterialDerivativeLegalizationRule.Divide:
                    result = MaterialStageUniformityAnalyzer.Analyze(
                            values,
                            node.Operand1,
                            uniformityStates)
                        == MaterialStageUniformity.Uniform
                            ? AnalyzeStageDerivative(
                                values,
                                node.Operand0,
                                states,
                                uniformityStates)
                            : StageDerivativeAvailability.Unavailable;
                    break;
                case MaterialDerivativeLegalizationRule.Lerp:
                    result = AnalyzeLerpDerivative(
                        values,
                        node,
                        states,
                        uniformityStates);
                    break;
                case MaterialDerivativeLegalizationRule.Select:
                    result = MaterialStageUniformityAnalyzer.Analyze(
                            values,
                            node.Operand0,
                            uniformityStates)
                        == MaterialStageUniformity.Uniform
                            ? MergeDerivativeAvailability(
                                AnalyzeStageDerivative(
                                    values,
                                    node.Operand1,
                                    states,
                                    uniformityStates),
                                AnalyzeStageDerivative(
                                    values,
                                    node.Operand2,
                                    states,
                                    uniformityStates))
                            : StageDerivativeAvailability.Unavailable;
                    break;
                case MaterialDerivativeLegalizationRule.Swizzle:
                case MaterialDerivativeLegalizationRule.OneMinus:
                    result = AnalyzeStageDerivative(
                        values,
                        node.Operand0,
                        states,
                        uniformityStates);
                    break;
                case MaterialDerivativeLegalizationRule.Compose:
                    result = AnalyzeComposeDerivative(
                        values,
                        node,
                        states,
                        uniformityStates);
                    break;
                case MaterialDerivativeLegalizationRule.Dot:
                    StageDerivativeAvailability dotLeft = AnalyzeStageDerivative(
                        values,
                        node.Operand0,
                        states,
                        uniformityStates);
                    StageDerivativeAvailability dotRight = AnalyzeStageDerivative(
                        values,
                        node.Operand1,
                        states,
                        uniformityStates);
                    result = dotLeft == StageDerivativeAvailability.Valid
                        && dotRight == StageDerivativeAvailability.Valid
                            ? StageDerivativeAvailability.Unavailable
                            : MergeDerivativeAvailability(dotLeft, dotRight);
                    break;
                default:
                    result = StageDerivativeAvailability.Unavailable;
                    break;
            }

            states[nodeIndex] = result;
            return result;
        }

        private static StageDerivativeAvailability AnalyzeLerpDerivative(
            MaterialValueIR values,
            in MaterialValueNode node,
            StageDerivativeAvailability[] states,
            MaterialStageUniformity[] uniformityStates)
        {
            if (MaterialStageUniformityAnalyzer.Analyze(
                    values,
                    node.Operand2,
                    uniformityStates)
                == MaterialStageUniformity.Uniform)
            {
                return MergeDerivativeAvailability(
                    AnalyzeStageDerivative(
                        values,
                        node.Operand0,
                        states,
                        uniformityStates),
                    AnalyzeStageDerivative(
                        values,
                        node.Operand1,
                        states,
                        uniformityStates));
            }

            bool hasUniformEndpoints = MaterialStageUniformityAnalyzer.Analyze(
                    values,
                    node.Operand0,
                    uniformityStates)
                == MaterialStageUniformity.Uniform
                && MaterialStageUniformityAnalyzer.Analyze(
                    values,
                    node.Operand1,
                    uniformityStates)
                == MaterialStageUniformity.Uniform;
            return hasUniformEndpoints
                ? AnalyzeStageDerivative(
                    values,
                    node.Operand2,
                    states,
                    uniformityStates)
                : StageDerivativeAvailability.Unavailable;
        }

        private static StageDerivativeAvailability AnalyzeComposeDerivative(
            MaterialValueIR values,
            in MaterialValueNode node,
            StageDerivativeAvailability[] states,
            MaterialStageUniformity[] uniformityStates)
        {
            var operands = new[]
            {
                node.Operand0,
                node.Operand1,
                node.Operand2,
                node.Operand3,
            };
            StageDerivativeAvailability result = StageDerivativeAvailability.Zero;
            for (int operandIndex = 0;
                 operandIndex < operands.Length && operands[operandIndex] >= 0;
                 operandIndex++)
            {
                result = MergeDerivativeAvailability(
                    result,
                    AnalyzeStageDerivative(
                        values,
                        operands[operandIndex],
                        states,
                        uniformityStates));
                if (result == StageDerivativeAvailability.Unavailable)
                    return result;
            }
            return result;
        }

        private static StageDerivativeAvailability MergeDerivativeAvailability(
            StageDerivativeAvailability left,
            StageDerivativeAvailability right)
        {
            if (left == StageDerivativeAvailability.Unavailable
                || right == StageDerivativeAvailability.Unavailable)
            {
                return StageDerivativeAvailability.Unavailable;
            }
            if (left == StageDerivativeAvailability.Valid
                || right == StageDerivativeAvailability.Valid)
            {
                return StageDerivativeAvailability.Valid;
            }
            return StageDerivativeAvailability.Zero;
        }

        private static bool IsExternalInputAvailable(
            MaterialEvaluationStage stage,
            MaterialExternalInput input)
        {
            if (input == MaterialExternalInput.UV0)
                return true;
            return stage == MaterialEvaluationStage.Surface
                && (input == MaterialExternalInput.GeometryNormalWS
                    || input == MaterialExternalInput.GeometryTangentWS);
        }

        private static bool IsStageInputAvailable(
            MaterialEvaluationStage stage,
            MaterialStageInput input)
        {
            switch (input)
            {
                case MaterialStageInput.UV0:
                case MaterialStageInput.UV0Ddx:
                case MaterialStageInput.UV0Ddy:
                    return true;
                case MaterialStageInput.GeometryNormalWS:
                case MaterialStageInput.GeometryTangentWS:
                    return stage == MaterialEvaluationStage.Surface;
                default:
                    return false;
            }
        }

        private static MaterialValueType GetStageInputType(MaterialStageInput input)
        {
            switch (input)
            {
                case MaterialStageInput.UV0:
                case MaterialStageInput.UV0Ddx:
                case MaterialStageInput.UV0Ddy:
                    return MaterialValueType.Float2;
                case MaterialStageInput.GeometryNormalWS:
                    return MaterialValueType.Float3;
                case MaterialStageInput.GeometryTangentWS:
                    return MaterialValueType.Float4;
                default:
                    return (MaterialValueType) (-1);
            }
        }

        private static void GetStageLIROperandRange(
            MaterialStageLIROpcode opcode,
            out int minimum,
            out int maximum)
        {
            switch (opcode)
            {
                case MaterialStageLIROpcode.StageInput:
                case MaterialStageLIROpcode.Constant:
                case MaterialStageLIROpcode.Parameter:
                case MaterialStageLIROpcode.TextureResource:
                    minimum = maximum = 0;
                    return;
                case MaterialStageLIROpcode.TextureSampleGrad:
                    minimum = maximum = 4;
                    return;
                case MaterialStageLIROpcode.Add:
                case MaterialStageLIROpcode.Multiply:
                case MaterialStageLIROpcode.Subtract:
                case MaterialStageLIROpcode.Divide:
                case MaterialStageLIROpcode.Min:
                case MaterialStageLIROpcode.Max:
                case MaterialStageLIROpcode.Dot:
                case MaterialStageLIROpcode.Compare:
                    minimum = maximum = 2;
                    return;
                case MaterialStageLIROpcode.Lerp:
                case MaterialStageLIROpcode.Select:
                    minimum = maximum = 3;
                    return;
                case MaterialStageLIROpcode.Swizzle:
                case MaterialStageLIROpcode.Saturate:
                case MaterialStageLIROpcode.OneMinus:
                case MaterialStageLIROpcode.Normalize:
                    minimum = maximum = 1;
                    return;
                case MaterialStageLIROpcode.Compose:
                    minimum = 2;
                    maximum = 4;
                    return;
                default:
                    minimum = maximum = 0;
                    return;
            }
        }

        private static bool IsKnownType(MaterialValueType type)
        {
            return type == MaterialValueType.Bool
                || type == MaterialValueType.Float
                || type == MaterialValueType.Float2
                || type == MaterialValueType.Float3
                || type == MaterialValueType.Float4
                || type == MaterialValueType.Texture2D;
        }

        private static bool IsDataType(MaterialValueType type)
        {
            return type == MaterialValueType.Bool || IsNumericType(type);
        }

        private static bool IsNumericType(MaterialValueType type)
        {
            return type == MaterialValueType.Float
                || type == MaterialValueType.Float2
                || type == MaterialValueType.Float3
                || type == MaterialValueType.Float4;
        }

        private static bool IsVectorType(MaterialValueType type)
        {
            return type == MaterialValueType.Float2
                || type == MaterialValueType.Float3
                || type == MaterialValueType.Float4;
        }

        private static bool IsDefaultBits(float4 value)
        {
            uint4 bits = math.asuint(value);
            return bits.x == 0u
                && bits.y == 0u
                && bits.z == 0u
                && bits.w == 0u;
        }

        private static MaterialIRVerificationResult CreateResult(
            List<MaterialIRDiagnostic> diagnostics)
        {
            return new MaterialIRVerificationResult(diagnostics.ToArray());
        }

        private static void AddNodeError(
            List<MaterialIRDiagnostic> diagnostics,
            string code,
            int nodeIndex,
            string message)
        {
            diagnostics.Add(new MaterialIRDiagnostic(
                MaterialIRDiagnosticSeverity.Error,
                code,
                message,
                nodeIndex));
        }

        private static void AddError(
            List<MaterialIRDiagnostic> diagnostics,
            string code,
            string message)
        {
            diagnostics.Add(new MaterialIRDiagnostic(
                MaterialIRDiagnosticSeverity.Error,
                code,
                message));
        }
    }
}
