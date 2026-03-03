using System;

namespace VividRP.Runtime
{
    [AttributeUsage(AttributeTargets.Class)]
    public class PipelineResourceAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class ResourcePathAttribute : Attribute
    {
        public string Path { get; }
        public ResourcePathAttribute(string path) { Path = path; }
    }
}