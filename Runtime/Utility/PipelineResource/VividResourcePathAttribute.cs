using System;

namespace VividRP.Runtime
{
    [AttributeUsage(AttributeTargets.Class)]
    public class PipelineResourceAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class VividResourcePathAttribute : Attribute
    {
        public string Path { get; }
        public VividResourcePathAttribute(string path) { Path = path; }
    }
}