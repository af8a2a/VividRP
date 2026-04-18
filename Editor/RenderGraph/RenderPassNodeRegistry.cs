using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [InitializeOnLoad]
    internal static class RenderPassNodeRegistry
    {
        private static Dictionary<Type, Type> s_NodeToPass;
        private static Dictionary<Type, Type> s_PassToNode;

        static RenderPassNodeRegistry()
        {
            Rebuild();
        }

        internal static Type GetPassType(Type nodeType)
        {
            if (nodeType == null)
                return null;

            EnsureBuilt();
            s_NodeToPass.TryGetValue(nodeType, out var passType);
            return passType;
        }

        internal static Type GetNodeType(Type passType)
        {
            if (passType == null)
                return null;

            EnsureBuilt();
            s_PassToNode.TryGetValue(passType, out var nodeType);
            return nodeType;
        }

        internal static void Rebuild()
        {
            var passTypes = TypeCache.GetTypesDerivedFrom<IRenderPass>();
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(passTypes);

            var nodeTypes = TypeCache.GetTypesDerivedFrom<RenderPassNodeData>();
            var nodeTypesByName = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var nodeType in nodeTypes)
            {
                if (nodeType.IsAbstract || nodeType.ContainsGenericParameters)
                    continue;

                nodeTypesByName[nodeType.Name] = nodeType;
            }

            var nodeToPass = new Dictionary<Type, Type>();
            var passToNode = new Dictionary<Type, Type>();

            foreach (var registration in registrations)
            {
                var passType = registration.PassType;
                if (passType == null)
                    continue;

                if (!nodeTypesByName.TryGetValue(registration.NodeClassName, out var nodeType))
                    continue;

                nodeToPass[nodeType] = passType;
                passToNode[passType] = nodeType;
            }

            s_NodeToPass = nodeToPass;
            s_PassToNode = passToNode;
        }

        private static void EnsureBuilt()
        {
            if (s_NodeToPass == null)
                Rebuild();
        }
    }
}
