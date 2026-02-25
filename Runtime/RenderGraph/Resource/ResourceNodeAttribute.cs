using System;

namespace VividRP.Runtime.RenderGraph.Resource
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class ResourceNodeAttribute : Attribute
    {
        public string DisplayName { get; }

        public ResourceNodeAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }
}
