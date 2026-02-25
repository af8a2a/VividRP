using System;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Runtime.RenderGraph.Passes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class RenderPassAttribute : Attribute
    {
        public string DisplayName { get; }
        public PassType PassType { get; }

        public RenderPassAttribute(string displayName, PassType passType)
        {
            DisplayName = displayName;
            PassType = passType;
        }
    }
}
