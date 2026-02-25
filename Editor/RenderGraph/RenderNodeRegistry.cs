using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using VividRP.Runtime.RenderGraph.Data;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Editor.RenderGraph
{
    public struct NodeRegistryEntry
    {
        public Type DataType;
        public string DisplayName;
    }

    public static class RenderNodeRegistry
    {
        private static readonly List<NodeRegistryEntry> s_PassTypes = new();
        private static readonly List<NodeRegistryEntry> s_ResourceTypes = new();
        private static readonly Dictionary<Type, Type> s_ViewTypes = new();
        private static bool s_Initialized;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            s_PassTypes.Clear();
            s_ResourceTypes.Clear();
            s_ViewTypes.Clear();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract) continue;

                    var renderPass = type.GetCustomAttribute<RenderPassAttribute>();
                    if (renderPass != null)
                    {
                        s_PassTypes.Add(new NodeRegistryEntry
                        {
                            DataType = type,
                            DisplayName = renderPass.DisplayName
                        });
                    }

                    var resourceNode = type.GetCustomAttribute<ResourceNodeAttribute>();
                    if (resourceNode != null)
                    {
                        s_ResourceTypes.Add(new NodeRegistryEntry
                        {
                            DataType = type,
                            DisplayName = resourceNode.DisplayName
                        });
                    }

                    var nodeEditor = type.GetCustomAttribute<NodeEditorAttribute>();
                    if (nodeEditor != null)
                    {
                        s_ViewTypes[nodeEditor.DataType] = type;
                    }
                }
            }

            s_Initialized = true;
        }

        public static List<NodeRegistryEntry> GetAllPassTypes()
        {
            if (!s_Initialized) Initialize();
            return s_PassTypes;
        }

        public static List<NodeRegistryEntry> GetAllResourceTypes()
        {
            if (!s_Initialized) Initialize();
            return s_ResourceTypes;
        }

        public static bool TryGetViewType(Type dataType, out Type viewType)
        {
            if (!s_Initialized) Initialize();
            return s_ViewTypes.TryGetValue(dataType, out viewType);
        }
    }
}
