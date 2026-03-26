using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.GraphToolkit.Editor;

namespace VividRP.Editor.RenderGraph
{
    internal static class RenderGraphSubSystemReflectionUtility
    {
        private const string InputPortMappingPropertyName = "InputPortToVariableDeclarationDictionary";
        private const string OutputPortMappingPropertyName = "OutputPortToVariableDeclarationDictionary";

        private static readonly BindingFlags s_InstanceBindings = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal static bool TryGetVariableForInputPort(ISubgraphNode subgraphNode, IPort port, out IVariable variable)
        {
            variable = null;
            if (!TryGetPortMappings(subgraphNode, out var mappings))
                return false;

            return port != null && mappings.InputPorts.TryGetValue(port, out variable);
        }

        internal static bool TryGetVariableForOutputPort(ISubgraphNode subgraphNode, IPort port, out IVariable variable)
        {
            variable = null;
            if (!TryGetPortMappings(subgraphNode, out var mappings))
                return false;

            return port != null && mappings.OutputPorts.TryGetValue(port, out variable);
        }

        internal static bool TryGetInputPortForVariable(ISubgraphNode subgraphNode, IVariable variable, out IPort port)
        {
            port = null;
            if (!TryGetPortMappings(subgraphNode, out var mappings))
                return false;

            return variable != null && mappings.InputVariables.TryGetValue(variable, out port);
        }

        internal static bool TryGetOutputPortForVariable(ISubgraphNode subgraphNode, IVariable variable, out IPort port)
        {
            port = null;
            if (!TryGetPortMappings(subgraphNode, out var mappings))
                return false;

            return variable != null && mappings.OutputVariables.TryGetValue(variable, out port);
        }

        private static bool TryGetPortMappings(ISubgraphNode subgraphNode, out SubgraphPortMappings mappings)
        {
            mappings = null;
            if (subgraphNode == null)
                return false;

            if (!TryGetNodeModel(subgraphNode, out var nodeModel))
                return false;

            mappings = new SubgraphPortMappings(
                BuildPortToVariableMap(nodeModel, InputPortMappingPropertyName),
                BuildPortToVariableMap(nodeModel, OutputPortMappingPropertyName));
            return true;
        }

        private static Dictionary<IPort, IVariable> BuildPortToVariableMap(object nodeModel, string propertyName)
        {
            var result = new Dictionary<IPort, IVariable>(ReferenceEqualityComparer<IPort>.Instance);
            if (nodeModel == null || string.IsNullOrEmpty(propertyName))
                return result;

            var property = nodeModel.GetType().GetProperty(propertyName, s_InstanceBindings);
            if (property?.GetValue(nodeModel) is not IDictionary dictionary)
                return result;

            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not IPort port || entry.Value is not IVariable variable)
                    continue;

                result[port] = variable;
            }

            return result;
        }

        private static bool TryGetNodeModel(INode node, out object nodeModel)
        {
            nodeModel = null;
            if (node == null)
                return false;

            var nodeType = node.GetType();
            if (nodeType.GetProperty(InputPortMappingPropertyName, s_InstanceBindings) != null
                || nodeType.GetProperty(OutputPortMappingPropertyName, s_InstanceBindings) != null)
            {
                nodeModel = node;
                return true;
            }

            var method = node.GetType().GetMethod("GetImplementation", s_InstanceBindings);
            if (method == null)
                return false;

            nodeModel = method.Invoke(node, null);
            return nodeModel != null;
        }

        private sealed class SubgraphPortMappings
        {
            internal SubgraphPortMappings(
                Dictionary<IPort, IVariable> inputPorts,
                Dictionary<IPort, IVariable> outputPorts)
            {
                InputPorts = inputPorts ?? new Dictionary<IPort, IVariable>(ReferenceEqualityComparer<IPort>.Instance);
                OutputPorts = outputPorts ?? new Dictionary<IPort, IVariable>(ReferenceEqualityComparer<IPort>.Instance);
                InputVariables = BuildVariableToPortMap(InputPorts);
                OutputVariables = BuildVariableToPortMap(OutputPorts);
            }

            internal Dictionary<IPort, IVariable> InputPorts { get; }
            internal Dictionary<IPort, IVariable> OutputPorts { get; }
            internal Dictionary<IVariable, IPort> InputVariables { get; }
            internal Dictionary<IVariable, IPort> OutputVariables { get; }

            private static Dictionary<IVariable, IPort> BuildVariableToPortMap(Dictionary<IPort, IVariable> portMappings)
            {
                var result = new Dictionary<IVariable, IPort>(ReferenceEqualityComparer<IVariable>.Instance);
                if (portMappings == null)
                    return result;

                foreach (var pair in portMappings)
                {
                    if (pair.Value == null || pair.Key == null)
                        continue;

                    result[pair.Value] = pair.Key;
                }

                return result;
            }
        }
    }

    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        internal static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

        public bool Equals(T x, T y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
        }
    }
}
