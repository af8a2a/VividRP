using System;

namespace VividRP.Runtime.Utility
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class ResourcePathAttribute : Attribute
    {
        public string Path { get; }
        public bool Required { get; set; } = true;

        public ResourcePathAttribute(string path)
        {
            Path = path;
        }
    }
}
