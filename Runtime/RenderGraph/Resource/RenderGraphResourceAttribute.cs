using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    [AttributeUsage(AttributeTargets.Field)]
    public class RenderGraphResource : Attribute
    {
        public string Name;
        public string Label;
        public AccessFlags Flags;
    }
}