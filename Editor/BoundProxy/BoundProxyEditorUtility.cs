using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal static class BoundProxyEditorUtility
    {
        private static readonly Color GizmoColor = new(0.23f, 0.73f, 0.67f, 0.08f);
        private static readonly Color CenterHandleColor = new(0.98f, 0.92f, 0.44f, 1.0f);
        private static readonly Color[] BoxHandleColors =
        {
            new Color(0.95f, 0.48f, 0.34f, 1.0f),
            new Color(0.31f, 0.78f, 0.41f, 1.0f),
            new Color(0.28f, 0.58f, 0.96f, 1.0f),
            new Color(0.95f, 0.48f, 0.34f, 1.0f),
            new Color(0.31f, 0.78f, 0.41f, 1.0f),
            new Color(0.28f, 0.58f, 0.96f, 1.0f),
        };

        private static HierarchicalBox s_ShapeBox;
        private static HierarchicalSphere s_ShapeSphere;

        private static HierarchicalBox ShapeBox => s_ShapeBox ??= new HierarchicalBox(GizmoColor, BoxHandleColors);

        private static HierarchicalSphere ShapeSphere => s_ShapeSphere ??= new HierarchicalSphere(GizmoColor);

        internal static void DrawInspector(SerializedBoundProxyShape serializedShape, bool showCenter = true)
        {
            if (serializedShape == null)
            {
                return;
            }

            EditorGUILayout.PropertyField(serializedShape.shape);
            if (showCenter)
            {
                EditorGUILayout.PropertyField(serializedShape.center);
            }

            EditorGUILayout.PropertyField(GetPrimaryShapeProperty(serializedShape));
        }

        internal static SerializedProperty GetPrimaryShapeProperty(SerializedBoundProxyShape serializedShape)
        {
            return GetShapeType(serializedShape) == BoundProxyShapeType.Sphere
                ? serializedShape.radius
                : serializedShape.size;
        }

        internal static BoundProxyShape GetShapeValue(SerializedBoundProxyShape serializedShape)
        {
            var shape = new BoundProxyShape
            {
                shape = GetShapeType(serializedShape),
                center = serializedShape.center.vector3Value,
                size = serializedShape.size.vector3Value,
                radius = serializedShape.radius.floatValue,
            };
            shape.Sanitize();
            return shape;
        }

        internal static Bounds GetLocalBounds(BoundProxyShape shape)
        {
            shape.Sanitize();
            return shape.GetLocalBounds();
        }

        internal static Bounds GetWorldBounds(BoundProxyShape shape, Transform ownerTransform)
        {
            return BoundProxyUtility.CalculateWorldAabb(ownerTransform, shape);
        }

        internal static void DrawGizmo(Transform ownerTransform, BoundProxyShape shape, bool filled, Color? baseColor = null)
        {
            DrawGizmo(ownerTransform, shape, filled, baseColor, applyOwnerRotation: true);
        }

        internal static void DrawGizmo(
            Transform ownerTransform,
            BoundProxyShape shape,
            bool filled,
            Color? baseColor,
            bool applyOwnerRotation)
        {
            if (ownerTransform == null)
            {
                return;
            }

            shape.Sanitize();
            Quaternion rotation = applyOwnerRotation ? ownerTransform.rotation : Quaternion.identity;
            using (new Handles.DrawingScope(Matrix4x4.TRS(ownerTransform.position, rotation, Vector3.one)))
            {
                if (shape.shape == BoundProxyShapeType.Sphere)
                {
                    ShapeSphere.center = shape.center;
                    ShapeSphere.radius = shape.GetSanitizedRadius();
                    ShapeSphere.baseColor = baseColor ?? GizmoColor;
                    ShapeSphere.DrawHull(filled);
                    return;
                }

                ShapeBox.center = shape.center;
                ShapeBox.size = shape.GetSanitizedSize();
                ShapeBox.SetBaseColor(baseColor ?? GizmoColor);
                ShapeBox.DrawHull(filled);
            }
        }

        internal static void DrawSceneHandles(
            SerializedObject serializedObject,
            SerializedBoundProxyShape serializedShape,
            Transform ownerTransform,
            string undoLabel = "Edit Bound Proxy",
            bool allowCenterHandle = false,
            bool applyOwnerRotation = true)
        {
            if (serializedObject == null || serializedShape == null || ownerTransform == null)
            {
                return;
            }

            serializedObject.Update();
            BoundProxyShape currentShape = GetShapeValue(serializedShape);
            if (!TryDrawSceneHandles(
                    currentShape,
                    ownerTransform,
                    out BoundProxyShape updatedShape,
                    allowCenterHandle,
                    applyOwnerRotation))
            {
                return;
            }

            serializedObject.Update();
            Undo.RecordObjects(serializedObject.targetObjects, undoLabel);
            serializedShape.center.vector3Value = updatedShape.center;
            serializedShape.size.vector3Value = updatedShape.size;
            serializedShape.radius.floatValue = updatedShape.radius;
            serializedObject.ApplyModifiedProperties();
        }

        internal static bool TryDrawSceneHandles(
            BoundProxyShape currentShape,
            Transform ownerTransform,
            out BoundProxyShape updatedShape,
            bool allowCenterHandle = false,
            bool applyOwnerRotation = true)
        {
            updatedShape = currentShape;
            if (ownerTransform == null)
            {
                return false;
            }

            currentShape.Sanitize();
            Vector3 newCenter = currentShape.center;
            Vector3 newSize = currentShape.GetSanitizedSize();
            float newRadius = currentShape.GetSanitizedRadius();
            bool hasChanges = false;

            Quaternion rotation = applyOwnerRotation ? ownerTransform.rotation : Quaternion.identity;
            using (new Handles.DrawingScope(Matrix4x4.TRS(ownerTransform.position, rotation, Vector3.one)))
            {
                if (allowCenterHandle)
                {
                    EditorGUI.BeginChangeCheck();
                    float handleSize = HandleUtility.GetHandleSize(newCenter) * 0.08f;
                    using (new Handles.DrawingScope(CenterHandleColor))
                    {
                        Vector3 movedCenter = Handles.FreeMoveHandle(
                            newCenter,
                            handleSize,
                            Vector3.zero,
                            Handles.DotHandleCap);
                        if (EditorGUI.EndChangeCheck())
                        {
                            newCenter = movedCenter;
                            hasChanges = true;
                        }
                    }
                }

                if (currentShape.shape == BoundProxyShapeType.Sphere)
                {
                    ShapeSphere.center = newCenter;
                    ShapeSphere.radius = newRadius;
                    ShapeSphere.baseColor = GizmoColor;
                    ShapeSphere.DrawHull(true);

                    EditorGUI.BeginChangeCheck();
                    ShapeSphere.DrawHandle();
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (allowCenterHandle)
                        {
                            newCenter = ShapeSphere.center;
                        }

                        newRadius = Mathf.Max(ShapeSphere.radius, 0.0f);
                        hasChanges = true;
                    }
                }
                else
                {
                    ShapeBox.center = newCenter;
                    ShapeBox.size = newSize;
                    ShapeBox.SetBaseColor(GizmoColor);
                    ShapeBox.DrawHull(true);
                    ShapeBox.monoHandle = false;

                    EditorGUI.BeginChangeCheck();
                    ShapeBox.DrawHandle();
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (allowCenterHandle)
                        {
                            newCenter = ShapeBox.center;
                        }

                        newSize = SanitizeSize(ShapeBox.size);
                        hasChanges = true;
                    }
                }
            }

            if (!hasChanges)
            {
                return false;
            }

            updatedShape = new BoundProxyShape
            {
                shape = currentShape.shape,
                center = newCenter,
                size = newSize,
                radius = newRadius,
            };
            updatedShape.Sanitize();
            return true;
        }

        private static BoundProxyShapeType GetShapeType(SerializedBoundProxyShape serializedShape)
        {
            return (BoundProxyShapeType)serializedShape.shape.intValue;
        }

        private static Vector3 SanitizeSize(Vector3 size)
        {
            return new Vector3(
                Mathf.Max(size.x, 0.0f),
                Mathf.Max(size.y, 0.0f),
                Mathf.Max(size.z, 0.0f));
        }
    }
}
