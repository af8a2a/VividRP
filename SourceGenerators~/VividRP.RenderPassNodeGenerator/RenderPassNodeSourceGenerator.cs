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

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.CompilationProvider,
                static (productionContext, compilation) => Execute(productionContext, compilation));
        }

        private static void Execute(SourceProductionContext context, Compilation compilation)
        {
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

            var candidates = EnumerateTypes(runtimeAssembly.GlobalNamespace, context.CancellationToken)
                .Where(type => IsCandidate(type, renderPassInterface, compilation))
                .Select(type => new PassCandidate(
                    type.Name,
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    type.ToDisplayString()))
                .OrderBy(candidate => candidate.NodeName, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.PassTypeName, StringComparer.Ordinal)
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

            context.AddSource(NodesHintName, SourceText.From(BuildNodesSource(candidates), Encoding.UTF8));
            context.AddSource(RegistryHintName, SourceText.From(BuildRegistrySource(candidates), Encoding.UTF8));
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
