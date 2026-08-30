using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class MaterialCoverageHlslArtifact
    {
        internal MaterialCoverageHlslArtifact(
            string entryPoint,
            string source,
            MaterialSurfaceHlslPhysicalContract physicalContract,
            ulong bindingHash,
            ulong codeHash)
        {
            if (string.IsNullOrEmpty(entryPoint))
                throw new ArgumentException("A Coverage HLSL entry point is required.", nameof(entryPoint));
            if (string.IsNullOrEmpty(source))
                throw new ArgumentException("Coverage HLSL source is required.", nameof(source));

            EntryPoint = entryPoint;
            Source = NormalizeLineEndings(source);
            PhysicalContract = physicalContract;
            BindingHash = bindingHash;
            CodeHash = codeHash;
            PayloadHash = ComputePayloadHash();
        }

        internal uint Version => MaterialProgramContract.CoverageHlslArtifactVersion;

        internal uint BackendVersion => MaterialProgramContract.CoverageHlslBackendVersion;

        internal string EntryPoint { get; }

        internal string Source { get; }

        internal MaterialSurfaceHlslPhysicalContract PhysicalContract { get; }

        internal ulong BindingHash { get; }

        internal ulong CodeHash { get; }

        internal ulong PayloadHash { get; }

        internal bool PayloadEquals(MaterialCoverageHlslArtifact other)
        {
            return ReferenceEquals(this, other)
                || other != null
                && Version == other.Version
                && BackendVersion == other.BackendVersion
                && PhysicalContract == other.PhysicalContract
                && BindingHash == other.BindingHash
                && CodeHash == other.CodeHash
                && string.Equals(EntryPoint, other.EntryPoint, StringComparison.Ordinal)
                && string.Equals(Source, other.Source, StringComparison.Ordinal);
        }

        private ulong ComputePayloadHash()
        {
            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(ref hash, Version);
            MaterialProgramHashUtility.Add(ref hash, BackendVersion);
            MaterialProgramHashUtility.Add(ref hash, (int) PhysicalContract);
            MaterialProgramHashUtility.Add(ref hash, BindingHash);
            MaterialProgramHashUtility.Add(ref hash, CodeHash);
            MaterialProgramHashUtility.Add(ref hash, EntryPoint);
            MaterialProgramHashUtility.Add(ref hash, Source);
            return hash;
        }

        internal static string NormalizeLineEndings(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }

    internal static class MaterialCoverageHlslBackend
    {
        internal static MaterialCoverageHlslArtifact Compile(
            MaterialIRModule module,
            MaterialProgramLoweringResult lowering)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));
            if (lowering == null)
                throw new ArgumentNullException(nameof(lowering));
            if (!ReferenceEquals(module.Values, lowering.CoverageProgram.StageLIR.Values))
            {
                throw new ArgumentException(
                    "Coverage Stage LIR does not belong to the material module.",
                    nameof(lowering));
            }
            if (lowering.SelectionKey.BackendKind != MaterialProgramBackendKind.NativeTemplate)
            {
                throw new NotSupportedException(
                    $"Coverage HLSL backend does not support '{lowering.SelectionKey.BackendKind}'.");
            }

            MaterialStageLIR stageLIR = lowering.CoverageProgram.StageLIR;
            MaterialIRVerifier.VerifyStageLIR(stageLIR).ThrowIfInvalid();
            if (stageLIR.Stage != MaterialEvaluationStage.Coverage
                || stageLIR.ExecutionModel != MaterialStageExecutionModel.RasterFragment
                || stageLIR.DerivativeProvider != MaterialStageDerivativeProvider.NativeQuad)
            {
                throw new NotSupportedException(
                    "Coverage AOT HLSL requires RasterFragment Stage LIR with NativeQuad derivatives.");
            }
            if (stageLIR.Roots.Count != 2
                || stageLIR.Roots[0].Type != MaterialValueType.Float
                || stageLIR.Roots[1].Type != MaterialValueType.Float)
            {
                throw new NotSupportedException(
                    "Coverage AOT HLSL requires Float coverage and alpha-clip-threshold roots.");
            }

            MaterialNativeTemplateLayoutSchema schema =
                lowering.Template.LayoutSchema;
            MaterialGenericLayout genericLayout = lowering.GenericLayout;
            MaterialSurfaceHlslPhysicalContract physicalContract =
                MaterialSurfaceHlslPhysicalContract.GenericRuntime;
            ValidateResourceUses(stageLIR);

            var bodyBuilder = new StringBuilder(Math.Max(1024, stageLIR.NodeCount * 96));
            MaterialSurfaceHlslBackend.AppendStageNodes(
                bodyBuilder,
                stageLIR,
                schema,
                genericLayout,
                includePositionCS: false);
            bodyBuilder.AppendLine("    VividMaterialCoverageEvaluation output;");
            bodyBuilder.Append("    output.Coverage = ")
                .Append(GetValueName(stageLIR.Roots[0].Index))
                .AppendLine(";");
            bodyBuilder.Append("    output.AlphaClipThreshold = ")
                .Append(GetValueName(stageLIR.Roots[1].Index))
                .AppendLine(";");
            bodyBuilder.AppendLine("    return output;");

            string bodySource = MaterialCoverageHlslArtifact.NormalizeLineEndings(
                bodyBuilder.ToString());
            ulong bindingHash = MaterialSurfaceHlslBackend.ComputeGenericBindingHash(
                genericLayout,
                schema);
            ulong codeHash = ComputeCodeHash(
                bodySource,
                physicalContract,
                bindingHash);
            string entryPoint = string.Format(
                CultureInfo.InvariantCulture,
                "VividEvaluateAOTCoverage_{0:X16}",
                codeHash);

            var sourceBuilder = new StringBuilder(bodySource.Length + 384);
            sourceBuilder.Append("// Coverage AOT HLSL artifact v")
                .Append(MaterialProgramContract.CoverageHlslArtifactVersion)
                .Append(", backend v")
                .Append(MaterialProgramContract.CoverageHlslBackendVersion)
                .AppendLine(".");
            AppendFunctionSignature(
                sourceBuilder,
                entryPoint);
            sourceBuilder.AppendLine("{");
            sourceBuilder.Append(bodySource);
            sourceBuilder.AppendLine("}");

            return new MaterialCoverageHlslArtifact(
                entryPoint,
                sourceBuilder.ToString(),
                physicalContract,
                bindingHash,
                codeHash);
        }

        private static void AppendFunctionSignature(
            StringBuilder builder,
            string entryPoint)
        {
            builder.Append("VividMaterialCoverageEvaluation ")
                .Append(entryPoint)
                .AppendLine("(");
            builder.AppendLine("    const uint parameterAddress,");
            builder.AppendLine("    const uint resourceAddress,");
            builder.AppendLine("    const VividAOTCoverageContext context)");
        }

        private static ulong ComputeCodeHash(
            string bodySource,
            MaterialSurfaceHlslPhysicalContract physicalContract,
            ulong bindingHash)
        {
            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(
                ref hash,
                MaterialProgramContract.CoverageHlslArtifactVersion);
            MaterialProgramHashUtility.Add(
                ref hash,
                MaterialProgramContract.CoverageHlslBackendVersion);
            MaterialProgramHashUtility.Add(ref hash, (int) physicalContract);
            MaterialProgramHashUtility.Add(ref hash, bindingHash);
            MaterialProgramHashUtility.Add(ref hash, bodySource);
            return hash;
        }

        private static string GetValueName(int nodeIndex)
        {
            return "vivid_v" + nodeIndex.ToString("D4", CultureInfo.InvariantCulture);
        }

        private static void ValidateResourceUses(MaterialStageLIR stageLIR)
        {
            for (int resourceIndex = 0;
                 resourceIndex < stageLIR.Nodes.Count;
                 resourceIndex++)
            {
                if (stageLIR.Nodes[resourceIndex].Opcode
                    != MaterialStageLIROpcode.TextureResource)
                {
                    continue;
                }

                for (int nodeIndex = 0;
                     nodeIndex < stageLIR.Nodes.Count;
                     nodeIndex++)
                {
                    MaterialStageLIRNode node = stageLIR.Nodes[nodeIndex];
                    for (int operandIndex = 0;
                         operandIndex < node.OperandCount;
                         operandIndex++)
                    {
                        if (node.GetOperand(operandIndex) != resourceIndex)
                            continue;
                        if (node.Opcode == MaterialStageLIROpcode.TextureSampleGrad
                            && operandIndex == 0)
                        {
                            continue;
                        }
                        throw new NotSupportedException(
                            "Coverage Texture2D values may only be used as TextureSampleGrad operand 0.");
                    }
                }
            }
        }
    }

    internal static class MaterialCoverageHlslSourceBuilder
    {
        internal static string BuildSource(
            MaterialProgramCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            var sortedEntries = new List<MaterialProgramCatalog.ManifestEntry>(
                catalog.Entries.Count);
            var programIDs = new HashSet<VividMaterialProgramID>();
            for (int entryIndex = 0;
                 entryIndex < catalog.Entries.Count;
                 entryIndex++)
            {
                MaterialProgramCatalog.ManifestEntry entry = catalog.Entries[entryIndex]
                    ?? throw new ArgumentException(
                        $"Material catalog contains null at entry {entryIndex}.",
                        nameof(catalog));
                if (!programIDs.Add(entry.ProgramID))
                {
                    throw new ArgumentException(
                        $"Material program ID '{entry.ProgramID}' is emitted more than once.",
                        nameof(catalog));
                }
                sortedEntries.Add(entry);
            }
            sortedEntries.Sort((left, right) =>
                ((uint) left.ProgramID).CompareTo((uint) right.ProgramID));

            var builder = new StringBuilder(Math.Max(4096, sortedEntries.Count * 2048));
            builder.AppendLine("// <auto-generated by MaterialCoverageHlslGenerator>");
            builder.AppendLine("// Generated from canonical Coverage Stage LIR; do not edit.");
            builder.AppendLine("#ifndef VIVID_MATERIAL_COVERAGE_AOT_GENERATED_INCLUDED");
            builder.AppendLine("#define VIVID_MATERIAL_COVERAGE_AOT_GENERATED_INCLUDED");
            builder.AppendLine();
            builder.Append("#define VIVID_MATERIAL_COVERAGE_HLSL_BACKEND_VERSION ")
                .Append(MaterialProgramContract.CoverageHlslBackendVersion)
                .AppendLine("u");
            builder.AppendLine();
            MaterialProgramCatalogHlslContract.Append(builder, catalog);
            builder.AppendLine();
            AppendAbi(builder);

            var artifacts = new Dictionary<string, MaterialCoverageHlslArtifact>(
                StringComparer.Ordinal);
            for (int entryIndex = 0;
                 entryIndex < sortedEntries.Count;
                 entryIndex++)
            {
                MaterialCoverageHlslArtifact artifact =
                    sortedEntries[entryIndex].Program.CoverageHlsl;
                if (artifacts.TryGetValue(
                        artifact.EntryPoint,
                        out MaterialCoverageHlslArtifact existing))
                {
                    if (!existing.PayloadEquals(artifact))
                    {
                        throw new InvalidOperationException(
                            $"Coverage HLSL entry point '{artifact.EntryPoint}' has conflicting artifacts.");
                    }
                    continue;
                }
                artifacts.Add(artifact.EntryPoint, artifact);
                builder.Append(artifact.Source);
                if (!artifact.Source.EndsWith("\n", StringComparison.Ordinal))
                    builder.AppendLine();
                builder.AppendLine();
            }

            AppendDispatcher(builder, sortedEntries);
            builder.AppendLine();
            builder.AppendLine("#endif // VIVID_MATERIAL_COVERAGE_AOT_GENERATED_INCLUDED");
            return MaterialCoverageHlslArtifact.NormalizeLineEndings(builder.ToString());
        }

        private static void AppendAbi(StringBuilder builder)
        {
            builder.AppendLine("struct VividAOTCoverageContext");
            builder.AppendLine("{");
            builder.AppendLine("    float2 UV0;");
            builder.AppendLine("    float2 UV0Ddx;");
            builder.AppendLine("    float2 UV0Ddy;");
            builder.AppendLine("};");
            builder.AppendLine();
        }

        private static void AppendDispatcher(
            StringBuilder builder,
            IReadOnlyList<MaterialProgramCatalog.ManifestEntry> entries)
        {
            builder.AppendLine("bool VividTryEvaluateAOTCoverageProgram(");
            builder.AppendLine("    const VividMaterialRuntimeHeader runtimeHeader,");
            builder.AppendLine("    const VividMaterialProgramData programData,");
            builder.AppendLine("    const VividAOTCoverageContext context,");
            builder.AppendLine("    out VividMaterialCoverageEvaluation output)");
            builder.AppendLine("{");
            builder.AppendLine("    output = (VividMaterialCoverageEvaluation) 0;");
            builder.AppendLine("    switch (runtimeHeader.ProgramID)");
            builder.AppendLine("    {");
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                MaterialProgramCatalog.ManifestEntry entry = entries[entryIndex];
                MaterialCoverageHlslArtifact artifact = entry.Program.CoverageHlsl;
                builder.Append("        case ")
                    .Append(((uint) entry.ProgramID).ToString(CultureInfo.InvariantCulture))
                    .AppendLine("u:");
                builder.AppendLine("        {");
                AppendDispatchCase(builder, entry);
                builder.AppendLine("        }");
            }
            builder.AppendLine("        default:");
            builder.AppendLine("            return false;");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private static void AppendDispatchCase(
            StringBuilder builder,
            MaterialProgramCatalog.ManifestEntry entry)
        {
            MaterialCoverageHlslArtifact artifact = entry.Program.CoverageHlsl;
            MaterialGenericLayout layout = entry.Program.Lowering.GenericLayout;
            builder.Append("            const uint parameterLaneCount = ")
                .Append((layout.ParameterStrideInWords / 4).ToString(
                    CultureInfo.InvariantCulture))
                .AppendLine("u;");
            builder.Append("            const uint resourceCount = ")
                .Append(layout.ResourceCount.ToString(CultureInfo.InvariantCulture))
                .AppendLine("u;");
            builder.AppendLine("            if (programData.ParameterLayoutID");
            builder.AppendLine("                    != VIVIDMATERIALPARAMETERLAYOUTID_GENERIC_PARAMETER_LANES");
            builder.AppendLine("                || programData.ResourceLayoutID");
            builder.AppendLine("                    != VIVIDMATERIALRESOURCELAYOUTID_GENERIC_RESOURCE_RECORDS");
            builder.AppendLine("                || runtimeHeader.ParameterAddress > _MaterialParameterDataCount");
            builder.AppendLine("                || parameterLaneCount > _MaterialParameterDataCount - runtimeHeader.ParameterAddress");
            builder.AppendLine("                || runtimeHeader.ResourceBindingAddress > _MaterialResourceDataCount");
            builder.AppendLine("                || resourceCount > _MaterialResourceDataCount - runtimeHeader.ResourceBindingAddress)");
            builder.AppendLine("                return false;");
            builder.Append("            output = ")
                .Append(artifact.EntryPoint)
                .AppendLine("(");
            builder.AppendLine("                runtimeHeader.ParameterAddress,");
            builder.AppendLine("                runtimeHeader.ResourceBindingAddress,");
            builder.AppendLine("                context);");
            builder.AppendLine("            return true;");
        }
    }

    internal static class MaterialProgramCatalogHlslContract
    {
        internal static void Append(
            StringBuilder builder,
            MaterialProgramCatalog catalog)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            MaterialProgramCatalogManifestHash manifestHash = catalog.ManifestHash;
            uint hashLo = unchecked((uint) manifestHash.Value);
            uint hashHi = unchecked((uint) (manifestHash.Value >> 32));
            string expectedContract = string.Format(
                CultureInfo.InvariantCulture,
                "VIVID_MATERIAL_CATALOG_MANIFEST_VERSION != {0}u || VIVID_MATERIAL_CATALOG_MANIFEST_HASH_LO != 0x{1:X8}u || VIVID_MATERIAL_CATALOG_MANIFEST_HASH_HI != 0x{2:X8}u || VIVID_MATERIAL_CATALOG_PROGRAM_TABLE_LENGTH != {3}u",
                manifestHash.Version,
                hashLo,
                hashHi,
                catalog.RuntimeTableLength);

            builder.AppendLine("#ifndef VIVID_MATERIAL_CATALOG_MANIFEST_INCLUDED");
            builder.AppendLine("#define VIVID_MATERIAL_CATALOG_MANIFEST_INCLUDED");
            builder.Append("#define VIVID_MATERIAL_CATALOG_MANIFEST_VERSION ")
                .Append(manifestHash.Version.ToString(CultureInfo.InvariantCulture))
                .AppendLine("u");
            builder.Append("#define VIVID_MATERIAL_CATALOG_MANIFEST_HASH_LO 0x")
                .Append(hashLo.ToString("X8", CultureInfo.InvariantCulture))
                .AppendLine("u");
            builder.Append("#define VIVID_MATERIAL_CATALOG_MANIFEST_HASH_HI 0x")
                .Append(hashHi.ToString("X8", CultureInfo.InvariantCulture))
                .AppendLine("u");
            builder.Append("#define VIVID_MATERIAL_CATALOG_PROGRAM_TABLE_LENGTH ")
                .Append(catalog.RuntimeTableLength.ToString(CultureInfo.InvariantCulture))
                .AppendLine("u");
            builder.Append("#elif ").AppendLine(expectedContract);
            builder.AppendLine("#error Material dispatchers use different frozen catalog manifests.");
            builder.AppendLine("#endif");
        }
    }
}
