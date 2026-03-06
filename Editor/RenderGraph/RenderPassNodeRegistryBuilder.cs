using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    internal readonly struct RenderPassNodeRegistration
    {
        internal RenderPassNodeRegistration(string nodeClassName, string passTypeName)
        {
            NodeClassName = nodeClassName;
            PassTypeName = passTypeName;
        }

        internal string NodeClassName { get; }

        internal string PassTypeName { get; }
    }

    internal static class RenderPassNodeRegistryBuilder
    {
        internal const string GeneratedNamespace = "VividRP.Editor.RenderGraph.Generated";

        private static readonly Regex s_ExistingRegistrationPattern = new Regex(
            "internal sealed class\\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)\\s*:\\s*RenderPassNodeData\\s*\\{\\s*protected override string RegisteredPassTypeName => \\\"(?<type>(?:\\\\.|[^\\\"])*)\\\";",
            RegexOptions.Compiled | RegexOptions.Singleline);

        internal static IReadOnlyList<RenderPassNodeRegistration> ParseExistingRegistrations(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return Array.Empty<RenderPassNodeRegistration>();

            var registrations = new List<RenderPassNodeRegistration>();
            var seenPassTypes = new HashSet<string>(StringComparer.Ordinal);
            var seenClassNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in s_ExistingRegistrationPattern.Matches(source))
            {
                var nodeClassName = match.Groups["class"].Value;
                var passTypeName = Regex.Unescape(match.Groups["type"].Value);
                if (string.IsNullOrEmpty(nodeClassName) || string.IsNullOrEmpty(passTypeName))
                    continue;

                if (!seenClassNames.Add(nodeClassName) || !seenPassTypes.Add(passTypeName))
                    continue;

                registrations.Add(new RenderPassNodeRegistration(nodeClassName, passTypeName));
            }

            return registrations;
        }

        internal static IReadOnlyList<RenderPassNodeRegistration> BuildRegistrations(
            IEnumerable<Type> passTypes,
            IEnumerable<RenderPassNodeRegistration> existingRegistrations = null,
            bool includeTestAssemblies = false)
        {
            var registrations = new Dictionary<string, RenderPassNodeRegistration>(StringComparer.Ordinal);
            var usedClassNames = new HashSet<string>(StringComparer.Ordinal);

            if (existingRegistrations != null)
            {
                foreach (var existingRegistration in existingRegistrations)
                {
                    if (string.IsNullOrEmpty(existingRegistration.NodeClassName) ||
                        string.IsNullOrEmpty(existingRegistration.PassTypeName))
                    {
                        continue;
                    }

                    if (!usedClassNames.Add(existingRegistration.NodeClassName))
                        continue;

                    registrations[existingRegistration.PassTypeName] = existingRegistration;
                }
            }

            foreach (var passType in passTypes
                         .Where(type => IsAutoRegistrablePassType(type, includeTestAssemblies))
                         .OrderBy(type => type.FullName, StringComparer.Ordinal)
                         .ThenBy(type => type.Assembly.GetName().Name, StringComparer.Ordinal))
            {
                var passTypeName = BuildTypeName(passType);
                if (string.IsNullOrEmpty(passTypeName) || registrations.ContainsKey(passTypeName))
                    continue;

                var nodeClassName = AllocateNodeClassName(passType, usedClassNames);
                registrations.Add(passTypeName, new RenderPassNodeRegistration(nodeClassName, passTypeName));
            }

            return registrations.Values
                .OrderBy(registration => registration.NodeClassName, StringComparer.Ordinal)
                .ToArray();
        }

        internal static string BuildSource(IEnumerable<RenderPassNodeRegistration> registrations)
        {
            var builder = new StringBuilder();
            builder.AppendLine("using System;");
            builder.AppendLine();
            builder.AppendLine($"namespace {GeneratedNamespace}");
            builder.AppendLine("{");

            foreach (var registration in registrations.OrderBy(item => item.NodeClassName, StringComparer.Ordinal))
            {
                builder.AppendLine("    [Serializable]");
                builder.AppendLine($"    internal sealed class {registration.NodeClassName} : RenderPassNodeData");
                builder.AppendLine("    {");
                builder.AppendLine($"        protected override string RegisteredPassTypeName => \"{EscapeStringLiteral(registration.PassTypeName)}\";");
                builder.AppendLine("    }");
                builder.AppendLine();
            }

            builder.AppendLine("}");
            return builder.ToString();
        }

        private static bool IsAutoRegistrablePassType(Type passType, bool includeTestAssemblies)
        {
            if (passType == null || !passType.IsClass || passType.IsAbstract || passType.ContainsGenericParameters)
                return false;

            if (!typeof(IRenderPass).IsAssignableFrom(passType))
                return false;

            var assemblyName = passType.Assembly.GetName().Name;
            if (!includeTestAssemblies &&
                !string.IsNullOrEmpty(assemblyName) &&
                (assemblyName.EndsWith(".Tests", StringComparison.Ordinal) ||
                 assemblyName.Contains(".Tests.", StringComparison.Ordinal)))
            {
                return false;
            }

            return passType.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null) != null;
        }

        private static string BuildTypeName(Type passType)
        {
            var assemblyName = passType.Assembly.GetName().Name;
            return string.IsNullOrEmpty(assemblyName)
                ? passType.FullName
                : $"{passType.FullName}, {assemblyName}";
        }

        private static string AllocateNodeClassName(Type passType, HashSet<string> usedClassNames)
        {
            var simpleName = SanitizeIdentifier(passType.Name);
            if (usedClassNames.Add(simpleName))
                return simpleName;

            var namespaceSuffix = BuildNamespaceSuffix(passType.Namespace);
            if (!string.IsNullOrEmpty(namespaceSuffix))
            {
                var namespaceQualifiedName = $"{namespaceSuffix}_{simpleName}";
                if (usedClassNames.Add(namespaceQualifiedName))
                    return namespaceQualifiedName;
            }

            var assemblyName = SanitizeIdentifier(passType.Assembly.GetName().Name);
            if (!string.IsNullOrEmpty(assemblyName))
            {
                var assemblyQualifiedName = $"{assemblyName}_{simpleName}";
                if (usedClassNames.Add(assemblyQualifiedName))
                    return assemblyQualifiedName;
            }

            var suffix = 2;
            while (true)
            {
                var fallbackName = $"{simpleName}_{suffix}";
                if (usedClassNames.Add(fallbackName))
                    return fallbackName;

                suffix++;
            }
        }

        private static string BuildNamespaceSuffix(string namespaceName)
        {
            if (string.IsNullOrEmpty(namespaceName))
                return string.Empty;

            var segments = namespaceName
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(segment =>
                    !segment.Equals("VividRP", StringComparison.Ordinal) &&
                    !segment.Equals("Runtime", StringComparison.Ordinal) &&
                    !segment.Equals("Editor", StringComparison.Ordinal) &&
                    !segment.Equals("RenderPass", StringComparison.Ordinal) &&
                    !segment.Equals("RenderPasses", StringComparison.Ordinal) &&
                    !segment.Equals("Passes", StringComparison.Ordinal))
                .Select(SanitizeIdentifier)
                .Where(segment => !string.IsNullOrEmpty(segment))
                .ToArray();

            if (segments.Length == 0)
                return string.Empty;

            var startIndex = Math.Max(0, segments.Length - 2);
            return string.Join("_", segments, startIndex, segments.Length - startIndex);
        }

        private static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Pass";

            var builder = new StringBuilder(value.Length + 4);
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (char.IsLetterOrDigit(current) || current == '_')
                {
                    builder.Append(current);
                }
                else
                {
                    builder.Append('_');
                }
            }

            if (!char.IsLetter(builder[0]) && builder[0] != '_')
                builder.Insert(0, '_');

            return builder.ToString();
        }

        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
