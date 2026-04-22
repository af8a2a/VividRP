using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace VividRP.Editor.RenderGraph
{
    [CustomPropertyDrawer(typeof(TexturePreviewValue))]
    internal sealed class TexturePreviewValueDrawer : PropertyDrawer
    {
        internal const string RemovedMessage =
            "Preview Node has been removed from VividRP RenderGraph. Delete this node and use camera-aware debugging tools instead.";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            return new HelpBox(RemovedMessage, HelpBoxMessageType.Warning)
            {
                name = "vivid-preview-removed-help",
            };
        }
    }
}
