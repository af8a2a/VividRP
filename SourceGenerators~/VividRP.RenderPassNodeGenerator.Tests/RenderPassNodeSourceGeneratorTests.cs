using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VividRP.RenderPassNodeGenerator.Tests
{
    [TestClass]
    public sealed class RenderPassNodeSourceGeneratorTests
    {
        private const string NodesHintName = "GeneratedRenderPassNodes.g.cs";
        private const string RegistryHintName = "GeneratedRenderPassNodeRegistry.g.cs";

        private const string EditorScaffolding = @"
namespace VividRP.Editor.RenderGraph
{
    internal class RenderPassNodeData
    {
        internal virtual System.Type GetRegisteredPassType()
        {
            return null;
        }
    }
}
";

        private static readonly CSharpParseOptions s_ParseOptions =
            new CSharpParseOptions(LanguageVersion.CSharp9);

        private static readonly ImmutableArray<MetadataReference> s_PlatformReferences =
            CreatePlatformReferences();

        [TestMethod]
        public void GeneratesStronglyTypedNodesAndBidirectionalRegistry()
        {
            const string runtimeSource = @"
namespace VividRP.Runtime
{
    public interface IRenderPass { }

    public sealed class ZuluPass : IRenderPass { }

    public sealed class AlphaPass : IRenderPass { }
}
";

            var run = RunGenerator(runtimeSource);

            AssertNoErrors(run.OutputCompilation);
            Assert.AreEqual(2, run.GeneratedSources.Count);

            var nodes = run.GeneratedSources[NodesHintName];
            StringAssert.Contains(nodes, "[global::System.Serializable]");
            StringAssert.Contains(
                nodes,
                "internal sealed class AlphaPass : global::VividRP.Editor.RenderGraph.RenderPassNodeData");
            StringAssert.Contains(nodes, "internal override global::System.Type GetRegisteredPassType()");
            StringAssert.Contains(nodes, "return typeof(global::VividRP.Runtime.AlphaPass);");
            Assert.IsTrue(
                nodes.IndexOf("class AlphaPass", StringComparison.Ordinal) <
                nodes.IndexOf("class ZuluPass", StringComparison.Ordinal));

            var registry = run.GeneratedSources[RegistryHintName];
            StringAssert.Contains(registry, "internal static class GeneratedRenderPassNodeRegistry");
            StringAssert.Contains(registry, "internal static void Populate(");
            StringAssert.Contains(
                registry,
                "nodeToPass[typeof(global::VividRP.Editor.RenderGraph.Generated.AlphaPass)] = " +
                "typeof(global::VividRP.Runtime.AlphaPass);");
            StringAssert.Contains(
                registry,
                "passToNode[typeof(global::VividRP.Runtime.AlphaPass)] = " +
                "typeof(global::VividRP.Editor.RenderGraph.Generated.AlphaPass);");
        }

        [TestMethod]
        public void FrameworkAndBaseTypeNamesCannotShadowGeneratedReferences()
        {
            const string runtimeSource = @"
namespace VividRP.Runtime
{
    public interface IRenderPass { }

    public sealed class Type : IRenderPass { }

    public sealed class RenderPassNodeData : IRenderPass { }

    public sealed class SerializableAttribute : IRenderPass { }
}
";

            var run = RunGenerator(runtimeSource);

            AssertNoErrors(run.OutputCompilation);
            var nodes = run.GeneratedSources[NodesHintName];
            StringAssert.Contains(nodes, "[global::System.Serializable]");
            StringAssert.Contains(
                nodes,
                "internal sealed class RenderPassNodeData : global::VividRP.Editor.RenderGraph.RenderPassNodeData");
            StringAssert.Contains(nodes, "internal override global::System.Type GetRegisteredPassType()");
        }

        [TestMethod]
        public void FiltersUnsupportedPassTypesAndOnlyHonorsDirectObsoleteAttribute()
        {
            const string runtimeSource = @"
using System;

namespace VividRP.Runtime
{
    public interface IRenderPass { }

    public sealed class ValidPass : IRenderPass { }

    public abstract class AbstractPass : IRenderPass { }

    public sealed class GenericPass<T> : IRenderPass { }

    [Obsolete]
    public sealed class ObsoletePass : IRenderPass { }

    public sealed class NoPublicConstructorPass : IRenderPass
    {
        private NoPublicConstructorPass() { }
    }

    public sealed class NotARenderPass { }

    [Obsolete]
    public abstract class ObsoleteBasePass : IRenderPass { }

    public sealed class InheritedObsoletePass : ObsoleteBasePass { }

    public sealed class GenericContainer<T>
    {
        public sealed class NestedGenericContextPass : IRenderPass { }
    }

    public sealed class AccessibilityContainer
    {
        private sealed class PrivateNestedPass : IRenderPass
        {
            public PrivateNestedPass() { }
        }
    }

    internal sealed class InternalPass : IRenderPass
    {
        public InternalPass() { }
    }
}
";

            var run = RunGenerator(runtimeSource);

            AssertNoErrors(run.OutputCompilation);
            var allGeneratedSource = string.Join("\n", run.GeneratedSources.Values);
            StringAssert.Contains(allGeneratedSource, "class ValidPass");
            StringAssert.Contains(allGeneratedSource, "class InheritedObsoletePass");
            AssertDoesNotContain(allGeneratedSource, "class AbstractPass");
            AssertDoesNotContain(allGeneratedSource, "class GenericPass");
            AssertDoesNotContain(allGeneratedSource, "class ObsoletePass");
            AssertDoesNotContain(allGeneratedSource, "class NoPublicConstructorPass");
            AssertDoesNotContain(allGeneratedSource, "class NotARenderPass");
            AssertDoesNotContain(allGeneratedSource, "class NestedGenericContextPass");
            AssertDoesNotContain(allGeneratedSource, "class PrivateNestedPass");
            AssertDoesNotContain(allGeneratedSource, "class InternalPass");
        }

        [TestMethod]
        public void ReportsErrorAndEmitsNothingForSimpleNameCollision()
        {
            const string runtimeSource = @"
namespace VividRP.Runtime
{
    public interface IRenderPass { }
}

namespace First
{
    public sealed class DuplicatePass : VividRP.Runtime.IRenderPass { }
}

namespace Second
{
    public sealed class DuplicatePass : VividRP.Runtime.IRenderPass { }
}
";

            var run = RunGenerator(runtimeSource);

            Assert.AreEqual(0, run.GeneratedSources.Count);
            var collision = run.GeneratorDiagnostics.Single(diagnostic => diagnostic.Id == "VRPG003");
            Assert.AreEqual(DiagnosticSeverity.Error, collision.Severity);
            StringAssert.Contains(collision.GetMessage(), "First.DuplicatePass");
            StringAssert.Contains(collision.GetMessage(), "Second.DuplicatePass");
        }

        [TestMethod]
        public void EmitsNothingOutsideVividRPEditorCompilation()
        {
            const string runtimeSource = @"
namespace VividRP.Runtime
{
    public interface IRenderPass { }
    public sealed class ValidPass : IRenderPass { }
}
";

            var run = RunGenerator(runtimeSource, editorAssemblyName: "Consumer.Editor");

            AssertNoErrors(run.OutputCompilation);
            Assert.AreEqual(0, run.GeneratedSources.Count);
            Assert.AreEqual(0, run.GeneratorDiagnostics.Length);
        }

        [TestMethod]
        public void OutputIsDeterministicAcrossDeclarationOrder()
        {
            const string firstRuntimeSource = @"
namespace VividRP.Runtime
{
    public interface IRenderPass { }
    public sealed class ZuluPass : IRenderPass { }
    public sealed class AlphaPass : IRenderPass { }
    public sealed class MiddlePass : IRenderPass { }
}
";
            const string secondRuntimeSource = @"
namespace VividRP.Runtime
{
    public sealed class MiddlePass : IRenderPass { }
    public sealed class AlphaPass : IRenderPass { }
    public sealed class ZuluPass : IRenderPass { }
    public interface IRenderPass { }
}
";

            var firstRun = RunGenerator(firstRuntimeSource);
            var secondRun = RunGenerator(secondRuntimeSource);

            AssertNoErrors(firstRun.OutputCompilation);
            AssertNoErrors(secondRun.OutputCompilation);
            CollectionAssert.AreEquivalent(
                firstRun.GeneratedSources.Keys.ToArray(),
                secondRun.GeneratedSources.Keys.ToArray());
            foreach (var hintName in firstRun.GeneratedSources.Keys)
            {
                Assert.AreEqual(
                    firstRun.GeneratedSources[hintName],
                    secondRun.GeneratedSources[hintName],
                    $"Generated source '{hintName}' changed with declaration order.");
            }
        }

        private static GeneratorRun RunGenerator(
            string runtimeSource,
            string editorAssemblyName = "VividRP.Editor")
        {
            var runtimeReference = CompileRuntimeReference(runtimeSource);
            var editorCompilation = CSharpCompilation.Create(
                editorAssemblyName,
                new[] { CSharpSyntaxTree.ParseText(EditorScaffolding, s_ParseOptions) },
                s_PlatformReferences.Add(runtimeReference),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithDeterministic(true));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators: new[] { new RenderPassNodeSourceGenerator().AsSourceGenerator() },
                parseOptions: s_ParseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                editorCompilation,
                out var outputCompilation,
                out _);

            var generatorResult = driver.GetRunResult().Results.Single();
            var generatedSources = generatorResult.GeneratedSources.ToDictionary(
                source => source.HintName,
                source => source.SourceText.ToString(),
                StringComparer.Ordinal);
            return new GeneratorRun(outputCompilation, generatedSources, generatorResult.Diagnostics);
        }

        private static MetadataReference CompileRuntimeReference(string source)
        {
            var compilation = CSharpCompilation.Create(
                "VividRP.Runtime",
                new[] { CSharpSyntaxTree.ParseText(source, s_ParseOptions) },
                s_PlatformReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithDeterministic(true));

            using var stream = new MemoryStream();
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
            {
                throw new AssertFailedException(
                    "Synthetic VividRP.Runtime compilation failed:\n" +
                    string.Join("\n", emitResult.Diagnostics));
            }

            return MetadataReference.CreateFromImage(stream.ToArray());
        }

        private static ImmutableArray<MetadataReference> CreatePlatformReferences()
        {
            var trustedPlatformAssemblies =
                AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrEmpty(trustedPlatformAssemblies))
                throw new InvalidOperationException("Trusted platform assemblies are unavailable.");

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToImmutableArray<MetadataReference>();
        }

        private static void AssertNoErrors(Compilation compilation)
        {
            var errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.AreEqual(
                0,
                errors.Length,
                "Compilation errors:\n" + string.Join("\n", errors.AsEnumerable()));
        }

        private static void AssertDoesNotContain(string value, string unexpectedSubstring)
        {
            Assert.IsFalse(
                value.Contains(unexpectedSubstring, StringComparison.Ordinal),
                $"Generated source unexpectedly contained '{unexpectedSubstring}'.");
        }

        private sealed class GeneratorRun
        {
            internal GeneratorRun(
                Compilation outputCompilation,
                IReadOnlyDictionary<string, string> generatedSources,
                ImmutableArray<Diagnostic> generatorDiagnostics)
            {
                OutputCompilation = outputCompilation;
                GeneratedSources = generatedSources;
                GeneratorDiagnostics = generatorDiagnostics;
            }

            internal Compilation OutputCompilation { get; }

            internal IReadOnlyDictionary<string, string> GeneratedSources { get; }

            internal ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }
        }
    }
}
