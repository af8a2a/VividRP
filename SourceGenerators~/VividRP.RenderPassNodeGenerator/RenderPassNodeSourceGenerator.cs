using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace VividRP.RenderPassNodeGenerator
{
    [Generator]
    public sealed class RenderPassNodeSourceGenerator : IIncrementalGenerator
    {
        internal const string NodesHintName = "GeneratedRenderPassNodes.g.cs";
        internal const string RegistryHintName = "GeneratedRenderPassNodeRegistry.g.cs";

        private const string TargetAssemblyName = "VividRP.Editor";
        private const string RuntimeAssemblyName = "VividRP.Runtime";
        private const string RenderPassInterfaceMetadataName = "VividRP.Runtime.IRenderPass";
        private const string RenderGraphResourceMetadataName = "VividRP.Runtime.RenderGraphResource";
        private const string TransientResourceMetadataName = "VividRP.Runtime.TransientResourceAttribute";
        private const string PassBypassMetadataName = "VividRP.Runtime.PassBypassAttribute";
        private const string FrameResourceSlotMetadataName = "VividRP.Runtime.FrameResourceSlotAttribute";
        private const string CameraHistoryAccessMetadataName = "VividRP.Runtime.CameraHistoryAccessAttribute";
        private const string RenderGraphTextureMetadataName = "VividRP.Runtime.RenderGraphTexture";
        private const string RenderGraphBufferMetadataName = "VividRP.Runtime.RenderGraphBuffer";

        private const int AccessFlagRead = 1;
        private const int AccessFlagWrite = 2;

        private static readonly DiagnosticDescriptor s_MissingRuntimeAssembly = new DiagnosticDescriptor(
            id: "VRPG001",
            title: "VividRP runtime assembly is not referenced",
            messageFormat: "Compilation '{0}' must reference '{1}' for render pass node generation",
            category: "VividRP.SourceGeneration",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_MissingRenderPassInterface = new DiagnosticDescriptor(
            id: "VRPG002",
            title: "IRenderPass was not found",
            messageFormat: "Assembly '{0}' does not contain '{1}'",
            category: "VividRP.SourceGeneration",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_DuplicateNodeName = new DiagnosticDescriptor(
            id: "VRPG003",
            title: "Render pass node name collision",
            messageFormat: "Render pass types {0} all map to node name '{1}'; render pass simple names must be unique",
            category: "VividRP.SourceGeneration",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_InvalidTransientResource = new DiagnosticDescriptor(
            id: "VRPG004",
            title: "Invalid TransientResource declaration",
            messageFormat: "Invalid [TransientResource] on '{0}.{1}': {2}",
            category: "VividRP.SourceGeneration",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_InvalidPassBypass = new DiagnosticDescriptor(
            id: "VRPG005",
            title: "Invalid PassBypass declaration",
            messageFormat: "Invalid [PassBypass] on '{0}.{1}': {2}",
            category: "VividRP.SourceGeneration",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_EmptySlotOrHistoryName = new DiagnosticDescriptor(
            id: "VRPG006",
            title: "Empty FrameResourceSlot or CameraHistoryAccess name",
            messageFormat: "Render pass '{0}' has an empty [{1}] name",
            category: "VividRP.SourceGeneration",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.CompilationProvider,
                static (productionContext, compilation) => Execute(productionContext, compilation));
        }

        private static void Execute(SourceProductionContext context, Compilation compilation)
        {
            if (string.Equals(compilation.AssemblyName, RuntimeAssemblyName, StringComparison.Ordinal))
            {
                ValidateRuntimeAssembly(context, compilation, compilation.Assembly);
                return;
            }

            if (!string.Equals(compilation.AssemblyName, TargetAssemblyName, StringComparison.Ordinal))
                return;

            var runtimeAssembly = compilation.SourceModule.ReferencedAssemblySymbols
                .FirstOrDefault(assembly =>
                    string.Equals(assembly.Identity.Name, RuntimeAssemblyName, StringComparison.Ordinal));
            if (runtimeAssembly == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_MissingRuntimeAssembly,
                    Location.None,
                    TargetAssemblyName,
                    RuntimeAssemblyName));
                return;
            }

            var renderPassInterface = runtimeAssembly.GetTypeByMetadataName(RenderPassInterfaceMetadataName);
            if (renderPassInterface == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_MissingRenderPassInterface,
                    Location.None,
                    RuntimeAssemblyName,
                    RenderPassInterfaceMetadataName));
                return;
            }

            var candidateTypes = EnumerateTypes(runtimeAssembly.GlobalNamespace, context.CancellationToken)
                .Where(type => IsCandidate(type, renderPassInterface, compilation))
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ThenBy(type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .ToArray();

            var candidates = candidateTypes
                .Select(type => new PassCandidate(
                    type.Name,
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    type.ToDisplayString()))
                .ToArray();

            var collisions = candidates
                .GroupBy(candidate => candidate.NodeName, StringComparer.Ordinal)
                .Where(group => group.Skip(1).Any())
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            if (collisions.Length != 0)
            {
                foreach (var collision in collisions)
                {
                    var passTypes = string.Join(", ", collision.Select(candidate => $"'{candidate.DiagnosticTypeName}'"));
                    context.ReportDiagnostic(Diagnostic.Create(
                        s_DuplicateNodeName,
                        Location.None,
                        passTypes,
                        collision.Key));
                }

                return;
            }

            var inspectionAssembly = ResolveFullMetadataInspectionAssembly(compilation, runtimeAssembly);
            var validationSymbols = ValidationSymbols.Resolve(inspectionAssembly);
            foreach (var candidateType in candidateTypes)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var inspectedType = ReferenceEquals(inspectionAssembly, runtimeAssembly)
                    ? candidateType
                    : inspectionAssembly.GetTypeByMetadataName(GetClrMetadataName(candidateType)) ?? candidateType;
                ValidatePassContract(context, inspectedType, validationSymbols);
            }

            context.AddSource(NodesHintName, SourceText.From(BuildNodesSource(candidates), Encoding.UTF8));
            context.AddSource(RegistryHintName, SourceText.From(BuildRegistrySource(candidates), Encoding.UTF8));
        }

        private static void ValidateRuntimeAssembly(
            SourceProductionContext context,
            Compilation compilation,
            IAssemblySymbol runtimeAssembly)
        {
            var renderPassInterface = runtimeAssembly.GetTypeByMetadataName(RenderPassInterfaceMetadataName);
            if (renderPassInterface == null)
                return;

            var validationSymbols = ValidationSymbols.Resolve(runtimeAssembly);
            var candidateTypes = EnumerateTypes(runtimeAssembly.GlobalNamespace, context.CancellationToken)
                .Where(type => IsCandidate(type, renderPassInterface, compilation))
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ThenBy(type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal);

            foreach (var candidateType in candidateTypes)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                ValidatePassContract(context, candidateType, validationSymbols);
            }
        }

        private static IAssemblySymbol ResolveFullMetadataInspectionAssembly(
            Compilation compilation,
            IAssemblySymbol runtimeAssembly)
        {
            // When VividRP.Editor references VividRP.Runtime as a PE metadata reference, default
            // MetadataImportOptions.Public omits private fields on pass types. Re-bind existing
            // metadata references with MetadataImportOptions.All (no disk reload) so private
            // [RenderGraphResource]/[TransientResource]/[PassBypass] fields are validated too.
            if (compilation.GetMetadataReference(runtimeAssembly) is not PortableExecutableReference)
                return runtimeAssembly;

            var fullMetadataCompilation = CSharpCompilation.Create(
                assemblyName: "__VividRPRuntimeInspection",
                references: compilation.References,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    metadataImportOptions: MetadataImportOptions.All));

            var fullRuntimeAssembly = fullMetadataCompilation.SourceModule.ReferencedAssemblySymbols
                .FirstOrDefault(assembly =>
                    string.Equals(assembly.Identity.Name, RuntimeAssemblyName, StringComparison.Ordinal));

            return fullRuntimeAssembly ?? runtimeAssembly;
        }

        private static string GetClrMetadataName(INamedTypeSymbol type)
        {
            if (type.ContainingType != null)
                return GetClrMetadataName(type.ContainingType) + "+" + type.MetadataName;

            if (type.ContainingNamespace != null && !type.ContainingNamespace.IsGlobalNamespace)
                return type.ContainingNamespace.ToDisplayString() + "." + type.MetadataName;

            return type.MetadataName;
        }

        private static void ValidatePassContract(
            SourceProductionContext context,
            INamedTypeSymbol passType,
            ValidationSymbols symbols)
        {
            var passTypeName = passType.ToDisplayString();

            // Validate class-level [FrameResourceSlot] and [CameraHistoryAccess] across the hierarchy.
            for (INamedTypeSymbol? current = passType;
                 current != null && current.SpecialType != SpecialType.System_Object;
                 current = current.BaseType)
            {
                foreach (var attribute in current.GetAttributes())
                {
                    if (symbols.FrameResourceSlotAttribute != null
                        && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.FrameResourceSlotAttribute))
                    {
                        var slotName = GetFirstStringConstructorArg(attribute);
                        if (string.IsNullOrWhiteSpace(slotName))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                s_EmptySlotOrHistoryName,
                                Location.None,
                                passTypeName,
                                "FrameResourceSlot"));
                        }
                    }
                    else if (symbols.CameraHistoryAccessAttribute != null
                             && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.CameraHistoryAccessAttribute))
                    {
                        var historyName = GetFirstStringConstructorArg(attribute);
                        if (string.IsNullOrWhiteSpace(historyName))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                s_EmptySlotOrHistoryName,
                                Location.None,
                                passTypeName,
                                "CameraHistoryAccess"));
                        }
                    }
                }
            }

            if (symbols.RenderGraphResourceAttribute == null)
                return;

            // Collect instance fields derived-first so lookup mirrors runtime GetInstanceField().
            var fieldsByName = new Dictionary<string, IFieldSymbol>(StringComparer.Ordinal);
            var orderedFields = new List<IFieldSymbol>();
            for (INamedTypeSymbol? current = passType;
                 current != null && current.SpecialType != SpecialType.System_Object;
                 current = current.BaseType)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member is IFieldSymbol field && !field.IsStatic && !field.IsImplicitlyDeclared)
                    {
                        orderedFields.Add(field);
                        if (!fieldsByName.ContainsKey(field.Name))
                            fieldsByName[field.Name] = field;
                    }
                }
            }

            foreach (var field in orderedFields)
            {
                var resourceAttr = FindAttribute(field, symbols.RenderGraphResourceAttribute);
                var hasTransient = symbols.TransientResourceAttribute != null
                    && FindAttribute(field, symbols.TransientResourceAttribute) != null;
                var bypassAttr = symbols.PassBypassAttribute != null
                    ? FindAttribute(field, symbols.PassBypassAttribute)
                    : null;

                if (hasTransient)
                {
                    if (resourceAttr == null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            s_InvalidTransientResource,
                            Location.None,
                            passTypeName,
                            field.Name,
                            "field must also be marked with [RenderGraphResource]"));
                    }
                    else if (!IsSupportedTransientOrBypassType(field.Type, symbols))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            s_InvalidTransientResource,
                            Location.None,
                            passTypeName,
                            field.Name,
                            "only RenderGraphTexture and RenderGraphBuffer fields are supported"));
                    }
                }

                if (bypassAttr != null)
                {
                    ValidatePassBypass(
                        context,
                        passTypeName,
                        field,
                        resourceAttr,
                        hasTransient,
                        bypassAttr,
                        fieldsByName,
                        symbols);
                }
            }
        }

        private static void ValidatePassBypass(
            SourceProductionContext context,
            string passTypeName,
            IFieldSymbol outputField,
            AttributeData? outputResourceAttr,
            bool outputIsTransient,
            AttributeData bypassAttr,
            Dictionary<string, IFieldSymbol> fieldsByName,
            ValidationSymbols symbols)
        {
            if (outputResourceAttr == null)
            {
                ReportInvalidBypass(context, passTypeName, outputField.Name, "the output field must be marked with [RenderGraphResource]");
                return;
            }

            if (outputIsTransient)
            {
                ReportInvalidBypass(context, passTypeName, outputField.Name, "transient resources cannot be bypass outputs");
                return;
            }

            if ((GetAccessFlags(outputResourceAttr) & AccessFlagWrite) == 0)
            {
                ReportInvalidBypass(context, passTypeName, outputField.Name, "the bypass output must be writable");
                return;
            }

            if (!IsSupportedTransientOrBypassType(outputField.Type, symbols))
            {
                ReportInvalidBypass(context, passTypeName, outputField.Name, "only RenderGraphTexture and RenderGraphBuffer fields are supported");
                return;
            }

            var sourceFieldName = GetFirstStringConstructorArg(bypassAttr);
            if (string.IsNullOrWhiteSpace(sourceFieldName))
            {
                ReportInvalidBypass(context, passTypeName, outputField.Name, "source field name is empty");
                return;
            }

            if (!fieldsByName.TryGetValue(sourceFieldName!, out var sourceField))
            {
                ReportInvalidBypass(context, passTypeName, outputField.Name, $"source field '{sourceFieldName}' was not found");
                return;
            }

            var sourceResourceAttr = FindAttribute(sourceField, symbols.RenderGraphResourceAttribute);
            if (sourceResourceAttr == null)
            {
                ReportInvalidBypass(context, passTypeName, outputField.Name, $"source field '{sourceField.Name}' must be marked with [RenderGraphResource]");
                return;
            }

            if (symbols.TransientResourceAttribute != null
                && FindAttribute(sourceField, symbols.TransientResourceAttribute) != null)
            {
                ReportInvalidBypass(context, passTypeName, outputField.Name, $"source field '{sourceField.Name}' cannot be transient");
                return;
            }

            if (!SymbolEqualityComparer.Default.Equals(sourceField.Type, outputField.Type))
            {
                ReportInvalidBypass(context, passTypeName, outputField.Name, $"source field '{sourceField.Name}' must use the same resource type as the output");
                return;
            }

            if ((GetAccessFlags(sourceResourceAttr) & AccessFlagRead) == 0)
            {
                ReportInvalidBypass(context, passTypeName, outputField.Name, $"source field '{sourceField.Name}' must be readable");
            }
        }

        private static void ReportInvalidBypass(
            SourceProductionContext context,
            string passTypeName,
            string fieldName,
            string reason)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_InvalidPassBypass,
                Location.None,
                passTypeName,
                fieldName,
                reason));
        }

        private static bool IsSupportedTransientOrBypassType(ITypeSymbol fieldType, ValidationSymbols symbols)
        {
            if (symbols.RenderGraphTexture != null
                && SymbolEqualityComparer.Default.Equals(fieldType, symbols.RenderGraphTexture))
            {
                return true;
            }

            if (symbols.RenderGraphBuffer != null
                && SymbolEqualityComparer.Default.Equals(fieldType, symbols.RenderGraphBuffer))
            {
                return true;
            }

            return false;
        }

        private static AttributeData? FindAttribute(ISymbol symbol, INamedTypeSymbol? attributeType)
        {
            if (attributeType == null)
                return null;

            foreach (var attr in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeType))
                    return attr;
            }

            return null;
        }

        private static string? GetFirstStringConstructorArg(AttributeData attribute)
        {
            if (attribute.ConstructorArguments.Length == 0)
                return null;

            return attribute.ConstructorArguments[0].Value as string;
        }

        private static int GetAccessFlags(AttributeData resourceAttribute)
        {
            foreach (var namedArg in resourceAttribute.NamedArguments)
            {
                if (string.Equals(namedArg.Key, "Access", StringComparison.Ordinal)
                    && namedArg.Value.Value != null)
                {
                    return Convert.ToInt32(namedArg.Value.Value);
                }
            }

            return 0;
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateTypes(
            INamespaceOrTypeSymbol container,
            CancellationToken cancellationToken)
        {
            foreach (var member in container.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (member is INamespaceSymbol namespaceSymbol)
                {
                    foreach (var nestedType in EnumerateTypes(namespaceSymbol, cancellationToken))
                        yield return nestedType;
                }
                else if (member is INamedTypeSymbol typeSymbol)
                {
                    yield return typeSymbol;
                    foreach (var nestedType in EnumerateTypes(typeSymbol, cancellationToken))
                        yield return nestedType;
                }
            }
        }

        private static bool IsCandidate(
            INamedTypeSymbol type,
            INamedTypeSymbol renderPassInterface,
            Compilation compilation)
        {
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || HasGenericContext(type))
                return false;

            if (!compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
                return false;

            if (type.GetAttributes().Any(attribute =>
                    string.Equals(
                        attribute.AttributeClass?.ToDisplayString(),
                        typeof(ObsoleteAttribute).FullName,
                        StringComparison.Ordinal)))
            {
                return false;
            }

            if (!type.InstanceConstructors.Any(constructor =>
                    constructor.DeclaredAccessibility == Accessibility.Public &&
                    constructor.Parameters.Length == 0))
            {
                return false;
            }

            return type.AllInterfaces.Any(@interface =>
                SymbolEqualityComparer.Default.Equals(@interface, renderPassInterface));
        }

        private static bool HasGenericContext(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
            {
                if (current.Arity != 0)
                    return true;
            }

            return false;
        }

        private static string BuildNodesSource(IReadOnlyList<PassCandidate> candidates)
        {
            var builder = new StringBuilder();
            AppendLine(builder, "// <auto-generated/>");
            AppendLine(builder, "namespace VividRP.Editor.RenderGraph.Generated");
            AppendLine(builder, "{");

            foreach (var candidate in candidates)
            {
                var nodeIdentifier = EscapeIdentifier(candidate.NodeName);
                AppendLine(builder, "    [global::System.Serializable]");
                AppendLine(
                    builder,
                    $"    internal sealed class {nodeIdentifier} : global::VividRP.Editor.RenderGraph.RenderPassNodeData");
                AppendLine(builder, "    {");
                AppendLine(builder, "        internal override global::System.Type GetRegisteredPassType()");
                AppendLine(builder, "        {");
                AppendLine(builder, $"            return typeof({candidate.PassTypeName});");
                AppendLine(builder, "        }");
                AppendLine(builder, "    }");
                AppendLine(builder);
            }

            AppendLine(builder, "}");
            return builder.ToString();
        }

        private static string BuildRegistrySource(IReadOnlyList<PassCandidate> candidates)
        {
            var builder = new StringBuilder();
            AppendLine(builder, "// <auto-generated/>");
            AppendLine(builder, "namespace VividRP.Editor.RenderGraph");
            AppendLine(builder, "{");
            AppendLine(builder, "    internal static class GeneratedRenderPassNodeRegistry");
            AppendLine(builder, "    {");
            AppendLine(builder, "        internal static void Populate(");
            AppendLine(
                builder,
                "            global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Type> nodeToPass,");
            AppendLine(
                builder,
                "            global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Type> passToNode)");
            AppendLine(builder, "        {");

            foreach (var candidate in candidates)
            {
                var generatedNodeType =
                    $"global::VividRP.Editor.RenderGraph.Generated.{EscapeIdentifier(candidate.NodeName)}";
                AppendLine(
                    builder,
                    $"            nodeToPass[typeof({generatedNodeType})] = typeof({candidate.PassTypeName});");
                AppendLine(
                    builder,
                    $"            passToNode[typeof({candidate.PassTypeName})] = typeof({generatedNodeType});");
            }

            AppendLine(builder, "        }");
            AppendLine(builder, "    }");
            AppendLine(builder, "}");
            return builder.ToString();
        }

        private static string EscapeIdentifier(string identifier)
        {
            return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ||
                   SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
                ? $"@{identifier}"
                : identifier;
        }

        private static void AppendLine(StringBuilder builder)
        {
            builder.Append('\n');
        }

        private static void AppendLine(StringBuilder builder, string value)
        {
            builder.Append(value).Append('\n');
        }

        private readonly struct ValidationSymbols
        {
            private ValidationSymbols(
                INamedTypeSymbol? renderGraphResourceAttribute,
                INamedTypeSymbol? transientResourceAttribute,
                INamedTypeSymbol? passBypassAttribute,
                INamedTypeSymbol? frameResourceSlotAttribute,
                INamedTypeSymbol? cameraHistoryAccessAttribute,
                INamedTypeSymbol? renderGraphTexture,
                INamedTypeSymbol? renderGraphBuffer)
            {
                RenderGraphResourceAttribute = renderGraphResourceAttribute;
                TransientResourceAttribute = transientResourceAttribute;
                PassBypassAttribute = passBypassAttribute;
                FrameResourceSlotAttribute = frameResourceSlotAttribute;
                CameraHistoryAccessAttribute = cameraHistoryAccessAttribute;
                RenderGraphTexture = renderGraphTexture;
                RenderGraphBuffer = renderGraphBuffer;
            }

            internal INamedTypeSymbol? RenderGraphResourceAttribute { get; }
            internal INamedTypeSymbol? TransientResourceAttribute { get; }
            internal INamedTypeSymbol? PassBypassAttribute { get; }
            internal INamedTypeSymbol? FrameResourceSlotAttribute { get; }
            internal INamedTypeSymbol? CameraHistoryAccessAttribute { get; }
            internal INamedTypeSymbol? RenderGraphTexture { get; }
            internal INamedTypeSymbol? RenderGraphBuffer { get; }

            internal static ValidationSymbols Resolve(IAssemblySymbol runtimeAssembly)
            {
                return new ValidationSymbols(
                    runtimeAssembly.GetTypeByMetadataName(RenderGraphResourceMetadataName),
                    runtimeAssembly.GetTypeByMetadataName(TransientResourceMetadataName),
                    runtimeAssembly.GetTypeByMetadataName(PassBypassMetadataName),
                    runtimeAssembly.GetTypeByMetadataName(FrameResourceSlotMetadataName),
                    runtimeAssembly.GetTypeByMetadataName(CameraHistoryAccessMetadataName),
                    runtimeAssembly.GetTypeByMetadataName(RenderGraphTextureMetadataName),
                    runtimeAssembly.GetTypeByMetadataName(RenderGraphBufferMetadataName));
            }
        }

        private sealed class PassCandidate
        {
            internal PassCandidate(string nodeName, string passTypeName, string diagnosticTypeName)
            {
                NodeName = nodeName;
                PassTypeName = passTypeName;
                DiagnosticTypeName = diagnosticTypeName;
            }

            internal string NodeName { get; }

            internal string PassTypeName { get; }

            internal string DiagnosticTypeName { get; }
        }
    }
}
