using System;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Runtime.RenderGraph.Passes.DataDriven
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public abstract class PassResourceAttribute : Attribute
    {
        public ResourceIntent Intent { get; }

        protected PassResourceAttribute(ResourceIntent intent)
        {
            Intent = intent;
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class PassWriteAttribute : PassResourceAttribute
    {
        public PassWriteAttribute() : base(ResourceIntent.Write)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class PassReadWriteAttribute : PassResourceAttribute
    {
        public PassReadWriteAttribute() : base(ResourceIntent.ReadWrite)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class PassReadAttribute : PassResourceAttribute
    {
        public PassReadAttribute() : base(ResourceIntent.Read)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class PassDepthAttribute : Attribute
    {
    }
}
