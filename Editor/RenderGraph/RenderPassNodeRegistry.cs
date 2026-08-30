using System;
using System.Collections.Generic;
using UnityEditor;

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
            var nodeToPass = new Dictionary<Type, Type>();
            var passToNode = new Dictionary<Type, Type>();
            GeneratedRenderPassNodeRegistry.Populate(nodeToPass, passToNode);

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
