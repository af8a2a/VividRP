using System;

namespace VividRP.Runtime
{
    public interface VTProducer
    {
        string Name { get; }
    }

    internal sealed class VTNullProducer : VTProducer
    {
        internal static readonly VTNullProducer Instance = new();

        private VTNullProducer()
        {
        }

        public string Name => nameof(VTNullProducer);
    }
}
