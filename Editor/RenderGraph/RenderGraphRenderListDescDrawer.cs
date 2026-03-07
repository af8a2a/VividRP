using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [CustomPropertyDrawer(typeof(RenderGraphRenderListDesc))]
    internal sealed class RenderGraphRenderListDescDrawer : PropertyDrawer
    {
        private const float VerticalSpacing = 2f;

        private static readonly Dictionary<string, bool> s_SectionExpandedStates = new();

        private static readonly SectionDefinition[] s_Sections =
        {
            new("Shader Tags", false, "ShaderTagNames"),
            new("Filtering", false, "RenderQueueRange", "LayerMask", "RenderingLayerMask", "ExcludeObjectMotionVectors"),
            new("Sorting", false, "SortingCriteria", "RendererConfiguration"),
            new("Overrides", false, "OverrideMaterial", "OverrideMaterialPassIndex", "OverrideShader", "OverrideShaderPassIndex"),
        };

        public override bool CanCacheInspectorGUI(SerializedProperty property)
        {
            return false;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return height;

            height += VerticalSpacing;
            foreach (var section in s_Sections)
            {
                height += EditorGUIUtility.singleLineHeight;
                height += VerticalSpacing;

                if (!IsSectionExpanded(property, section))
                    continue;

                foreach (var fieldName in section.FieldNames)
                {
                    var childProperty = property.FindPropertyRelative(fieldName);
                    if (childProperty == null)
                        continue;

                    height += EditorGUI.GetPropertyHeight(childProperty, includeChildren: true);
                    height += VerticalSpacing;
                }
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
            if (!property.isExpanded)
                return;

            var currentY = foldoutRect.yMax + VerticalSpacing;
            foreach (var section in s_Sections)
            {
                var sectionRect = new Rect(position.x, currentY, position.width, EditorGUIUtility.singleLineHeight);
                var expanded = IsSectionExpanded(property, section);
                var nextExpanded = EditorGUI.Foldout(sectionRect, expanded, section.Title, true);
                if (nextExpanded != expanded)
                    SetSectionExpanded(property, section, nextExpanded);

                currentY = sectionRect.yMax + VerticalSpacing;
                if (!nextExpanded)
                    continue;

                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (var fieldName in section.FieldNames)
                    {
                        var childProperty = property.FindPropertyRelative(fieldName);
                        if (childProperty == null)
                            continue;

                        var childHeight = EditorGUI.GetPropertyHeight(childProperty, includeChildren: true);
                        var childRect = new Rect(position.x, currentY, position.width, childHeight);
                        EditorGUI.PropertyField(childRect, childProperty, includeChildren: true);
                        currentY = childRect.yMax + VerticalSpacing;
                    }
                }
            }
        }

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new Foldout
            {
                name = "vivid-renderlist-desc-root",
                text = string.IsNullOrEmpty(property.displayName) ? "Render List Descriptor" : property.displayName,
                value = property.isExpanded,
            };
            root.RegisterValueChangedCallback(evt => property.isExpanded = evt.newValue);

            foreach (var section in s_Sections)
            {
                root.Add(CreateSectionFoldout(property, section));
            }

            return root;
        }

        private static Foldout CreateSectionFoldout(SerializedProperty property, SectionDefinition section)
        {
            var foldout = new Foldout
            {
                name = $"vivid-renderlist-desc-section-{SanitizeName(section.Title)}",
                text = section.Title,
                value = IsSectionExpanded(property, section),
            };
            foldout.style.marginTop = 2f;
            foldout.RegisterValueChangedCallback(evt => SetSectionExpanded(property, section, evt.newValue));

            foreach (var fieldName in section.FieldNames)
            {
                var childProperty = property.FindPropertyRelative(fieldName);
                if (childProperty == null)
                    continue;

                var field = new PropertyField(childProperty)
                {
                    name = $"vivid-renderlist-desc-field-{fieldName}",
                };
                field.style.marginBottom = 2f;
                foldout.Add(field);
            }

            return foldout;
        }

        private static bool IsSectionExpanded(SerializedProperty property, SectionDefinition section)
        {
            if (property == null)
                return section.ExpandedByDefault;

            return s_SectionExpandedStates.TryGetValue(BuildSectionStateKey(property, section), out var expanded)
                ? expanded
                : section.ExpandedByDefault;
        }

        private static void SetSectionExpanded(SerializedProperty property, SectionDefinition section, bool expanded)
        {
            if (property == null)
                return;

            s_SectionExpandedStates[BuildSectionStateKey(property, section)] = expanded;
        }

        private static string BuildSectionStateKey(SerializedProperty property, SectionDefinition section)
        {
            var targetObject = property.serializedObject?.targetObject;
            var targetId = targetObject != null ? targetObject.GetInstanceID().ToString() : "null";
            return $"{targetId}:{property.propertyPath}:{section.Title}";
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "section";

            return value.Replace(" ", string.Empty).ToLowerInvariant();
        }

        private readonly struct SectionDefinition
        {
            internal SectionDefinition(string title, bool expandedByDefault, params string[] fieldNames)
            {
                Title = title;
                ExpandedByDefault = expandedByDefault;
                FieldNames = fieldNames ?? Array.Empty<string>();
            }

            internal string Title { get; }

            internal bool ExpandedByDefault { get; }

            internal IReadOnlyList<string> FieldNames { get; }
        }
    }
}
