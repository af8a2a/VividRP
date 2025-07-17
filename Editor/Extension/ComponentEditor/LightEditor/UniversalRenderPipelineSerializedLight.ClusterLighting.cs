using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    internal partial class UniversalRenderPipelineSerializedLight
    {
        // Shape Directional
        public SerializedProperty angularDiameter { get; }

        // Shape Puntual
        public SerializedProperty shapeRadius { get; }

        
        public SerializedProperty baseContributionProp { get; }

    }
}