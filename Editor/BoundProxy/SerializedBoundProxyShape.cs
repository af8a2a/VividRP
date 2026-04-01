using UnityEditor;

namespace VividRP.Editor
{
    internal sealed class SerializedBoundProxyShape
    {
        internal SerializedProperty root;
        internal SerializedProperty shape;
        internal SerializedProperty center;
        internal SerializedProperty size;
        internal SerializedProperty radius;

        internal SerializedBoundProxyShape(SerializedProperty property)
        {
            root = property;
            shape = property.FindPropertyRelative("shape");
            center = property.FindPropertyRelative("center");
            size = property.FindPropertyRelative("size");
            radius = property.FindPropertyRelative("radius");
        }
    }
}
