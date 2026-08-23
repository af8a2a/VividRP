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

        internal static MaterialIRVerificationResult VerifyModule(
            MaterialValueIR values,
            in MaterialOutputRoots outputs,
            ClosureTopology topology,
            MaterialFeatureMask materialFeatures,
            MaterialShadingModelMask shadingModels)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (topology == null)
                throw new ArgumentNullException(nameof(topology));

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

            if (!ReferenceEquals(values, topology.ValueIR))
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.TopologyOwnerMismatch,
                    "Closure topology must reference the module value IR.");
            }
            AppendTopologyDiagnostics(topology, diagnostics);
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
                    $"Opcode value {(int) node.Opcode} is not defined by Material IR V2.");
                return;
            }

            bool hasKnownResultType = IsKnownType(node.Type);
            if (!hasKnownResultType)
            {
                AddNodeError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.UnknownValueType,
                    nodeIndex,
                    $"Result type value {(int) node.Type} is not defined by Material IR V2.");
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
                    "Material IR V2 supports one slab or two slabs connected by one operator.");
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
            if (topology.Slabs[0].IsTop || !topology.Slabs[0].IsBottom)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidTopologyShape,
                    "The base slab must be marked as bottom only.");
            }
            if (!topology.Slabs[1].IsTop || topology.Slabs[1].IsBottom)
            {
                AddError(
                    diagnostics,
                    MaterialIRDiagnosticCodes.InvalidTopologyShape,
                    "The top slab must be marked as top only.");
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
