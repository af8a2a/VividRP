using UnityEngine;

namespace VividRP.Runtime.Examples
{
    /// <summary>
    /// Minimal code-declared layout used by the per-object color example.
    /// </summary>
    public sealed class VividPerObjectColorExampleLayout
        : VividPerObjectLayout<VividPerObjectColorExampleLayout>
    {
        public const string ColorPropertyName = "_PerObjectColor";

        public static readonly int ColorPropertyId = Shader.PropertyToID(ColorPropertyName);

        public VividPerObjectColorExampleLayout()
        {
        }

        public override string ShaderIdentifier => "PerObjectColorExample";

        public static VividPerObjectPropertyHandle ColorProperty =>
            Instance.GetProperty(ColorPropertyId);

        protected override void Define(VividPerObjectLayoutBuilder builder)
        {
            builder.AddColor(ColorPropertyName, Color.white);
        }
    }
}
