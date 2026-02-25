using System;

namespace VividRP.Editor.RenderGraph
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class NodeEditorAttribute : Attribute
    {
        public Type DataType { get; }

        public NodeEditorAttribute(Type dataType)
        {
            DataType = dataType;
        }
    }
}
