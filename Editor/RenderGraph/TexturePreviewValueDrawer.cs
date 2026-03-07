using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [CustomPropertyDrawer(typeof(TexturePreviewValue))]
    internal sealed class TexturePreviewValueDrawer : PropertyDrawer
    {
        internal const float PreviewElementWidth = 240f;
        private const float PreviewHeight = 120f;
        private const float VerticalSpacing = 4f;


        
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var state = BuildPreviewState(property);
            var height = EditorGUIUtility.singleLineHeight;
            height += VerticalSpacing;
            height += GetHelpBoxHeight(state.Message, PreviewElementWidth);

            if (state.DisplayTexture != null)
            {
                height += VerticalSpacing;
                height += PreviewHeight;
            }

            return height;
        }


        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement
            {
                name = "vivid-texture-preview-root",
            };
            ConfigureFixedWidthElement(root);
            root.style.minWidth = PreviewElementWidth;
            root.style.alignSelf = Align.FlexStart;
            root.style.flexShrink = 0f;

            var textureProperty = property.FindPropertyRelative("m_Texture");
            if (textureProperty == null)
                return root;

            var liveInfo = new HelpBox(string.Empty, HelpBoxMessageType.Info)
            {
                name = "vivid-texture-preview-info",
            };
            ConfigureFixedWidthElement(liveInfo);
            liveInfo.style.marginBottom = VerticalSpacing;
            liveInfo.style.whiteSpace = WhiteSpace.Normal;
            root.Add(liveInfo);

            var textureField = new ObjectField("Fallback")
            {
                name = "vivid-texture-preview-fallback",
                objectType = typeof(Texture),
                allowSceneObjects = false,
            };
            ConfigureFixedWidthElement(textureField);
            textureField.BindProperty(textureProperty);
            root.Add(textureField);

            var previewContainer = new VisualElement
            {
                name = "vivid-texture-preview-container",
            };
            ConfigureFixedWidthElement(previewContainer);
            previewContainer.style.height = PreviewHeight;
            previewContainer.style.minWidth = PreviewElementWidth;
            previewContainer.style.marginTop = VerticalSpacing;
            previewContainer.style.alignSelf = Align.Center;
            previewContainer.style.justifyContent = Justify.Center;
            previewContainer.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            previewContainer.style.overflow = Overflow.Hidden;
            root.Add(previewContainer);

            var previewImage = new Image
            {
                name = "vivid-texture-preview-image",
                scaleMode = ScaleMode.ScaleToFit,
            };
            ConfigureFixedWidthElement(previewImage);
            previewImage.style.height = PreviewHeight;
            previewImage.style.minWidth = PreviewElementWidth;
            previewImage.style.alignSelf = Align.Center;
            previewContainer.Add(previewImage);

            void RefreshPreview()
            {
                var state = BuildPreviewState(property);
                liveInfo.text = state.Message;
                previewImage.image =  state.DisplayTexture;
                var hasPreview = state.DisplayTexture != null;
                previewContainer.style.display = hasPreview ? DisplayStyle.Flex : DisplayStyle.None;
                previewImage.style.display = hasPreview ? DisplayStyle.Flex : DisplayStyle.None;
            }
            
            RefreshPreview();
            textureField.RegisterValueChangedCallback(_ => RefreshPreview());
            root.schedule.Execute(RefreshPreview).Every(250);
            return root;
        }

        private static float GetHelpBoxHeight(string message, float width)
        {
            var content = EditorGUIUtility.TrTextContent(string.IsNullOrEmpty(message) ? " " : message);
            return Mathf.Max(
                EditorGUIUtility.singleLineHeight * 2f,
                EditorStyles.helpBox.CalcHeight(content, Mathf.Max(width, 1f)));
        }

        private static void ConfigureFixedWidthElement(VisualElement element)
        {
            element.style.width = PreviewElementWidth;
            element.style.maxWidth = PreviewElementWidth;
            element.style.minWidth = 0f;
            element.style.flexGrow = 0f;
            element.style.flexShrink = 1f;
            element.style.alignSelf = Align.Stretch;
        }


        private static PreviewState BuildPreviewState(SerializedProperty property)
        {
            var textureProperty = property?.FindPropertyRelative("m_Texture");
            var fallbackTexture = textureProperty?.objectReferenceValue as Texture;
            var displayTexture = fallbackTexture;
            var message = "Connect a texture output to preview it.";
            var hasConnectedPassOutput = false;
            var hasConnectedTextureInput = false;
            Type passType = null;
            string fieldName = null;

            var previewValue = TryGetPreviewValue(property);
            var previewNode = ResolvePreviewNode(property);
            if (previewNode != null)
            {
                previewNode.RefreshPreviewConnectionMetadata();
                previewValue = previewNode.GetPreviewValue();
                hasConnectedPassOutput = previewNode.TryGetConnectedPassOutput(out passType, out fieldName);
                hasConnectedTextureInput = previewNode.HasConnectedTextureInput();
            }
            else if (previewValue != null)
            {
                hasConnectedPassOutput = previewValue.TryGetConnectedPassOutput(out passType, out fieldName);
                hasConnectedTextureInput = previewValue.HasConnectedTextureInput;
            }

            if (hasConnectedPassOutput)
            {
                if (RenderGraphPreviewRegistry.TryGetPreview(passType, fieldName, out var runtimeTexture))
                {
                    displayTexture = runtimeTexture;
                    previewValue.Texture = runtimeTexture;
                    message = $"Live preview from {passType.Name}.{fieldName}.";
                }
                else if (fallbackTexture != null)
                {
                    message = $"Connected to {passType.Name}.{fieldName}. Runtime preview is not ready yet, showing fallback texture.";
                }
                else
                {
                    message = $"Connected to {passType.Name}.{fieldName}. Enter Play Mode and let a camera render to populate the preview.";
                }
            }
            else if (hasConnectedTextureInput)
            {
                message = fallbackTexture != null
                    ? "Connected texture has no live preview provider yet. Showing fallback texture."
                    : "Connected texture has no live preview provider yet.";
            }
            else if (RenderGraphPreviewRegistry.TryGetSinglePreview(out var singlePassType, out var singleFieldName, out var singleTexture))
            {
                displayTexture = singleTexture;
                message = $"Live preview from {singlePassType.Name}.{singleFieldName}.";
            }
            else if (fallbackTexture != null)
            {
                message = "Showing fallback texture.";
            }

            return new PreviewState(displayTexture, message);
        }

        private static TexturePreviewValue TryGetPreviewValue(SerializedProperty property)
        {
            return TryResolvePropertyValue(property, out var value) ? value as TexturePreviewValue : null;
        }

        private static PreviewNodeData ResolvePreviewNode(SerializedProperty property)
        {
            if (property?.serializedObject?.targetObject == null)
                return null;

            if (TryResolvePropertyValue(property, out var propertyValue) && propertyValue is TexturePreviewValue previewValue)
            {

                var owner = FindPreviewNodeByValue(property.serializedObject.targetObject, previewValue);
                if (owner != null)
                    return owner;
            }

            object current = property.serializedObject.targetObject;
            if (current is PreviewNodeData previewNode)
                return previewNode;

            var path = property.propertyPath.Replace(".Array.data[", "[");
            var elements = path.Split('.');
            foreach (var element in elements)
            {
                if (current == null)
                    return null;

                if (current is PreviewNodeData nestedPreviewNode)
                    return nestedPreviewNode;

                current = GetPathElementValue(current, element);
            }

            return current as PreviewNodeData;
        }

        private static PreviewNodeData FindPreviewNodeByValue(object root, TexturePreviewValue previewValue)
        {
            if (root == null || previewValue == null)
                return null;

            var visited = new HashSet<object>(ReferenceComparer.Instance);
            var queue = new Queue<object>();
            Enqueue(root, queue, visited);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current is PreviewNodeData previewNode && ReferenceEquals(previewNode.GetPreviewValue(), previewValue))
                    return previewNode;

                if (ShouldSkipTraversal(current))
                    continue;

                EnumerateChildren(current, queue, visited);
            }

            return null;
        }

        private static bool ShouldSkipTraversal(object value)
        {
            if (value == null)
                return true;

            var type = value.GetType();
            return type.IsPrimitive
                || type.IsEnum
                || value is string
                || value is Type
                || value is TexturePreviewValue
                || IsNonGraphUnityObject(value);
        }

        private static bool IsNonGraphUnityObject(object value)
        {
            if (value is not UnityEngine.Object unityObject)
                return false;

            var type = unityObject.GetType();
            var ns = type.Namespace;
            if (!string.IsNullOrEmpty(ns) &&
                (ns.StartsWith("Unity.GraphToolkit", StringComparison.Ordinal) || ns.StartsWith("VividRP", StringComparison.Ordinal)))
                return false;

            return true;
        }

        private static void EnumerateChildren(object source, Queue<object> queue, ISet<object> visited)
        {
            if (source is IEnumerable enumerable && source is not string)
            {
                foreach (var item in enumerable)
                {
                    Enqueue(item, queue, visited);
                }
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var sourceType = source.GetType();
            while (sourceType != null)
            {
                foreach (var field in sourceType.GetFields(flags))
                {
                    if (field.IsStatic)
                        continue;

                    Enqueue(field.GetValue(source), queue, visited);
                }

                foreach (var property in sourceType.GetProperties(flags))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0)
                        continue;

                    try
                    {
                        Enqueue(property.GetValue(source), queue, visited);
                    }
                    catch
                    {
                    }
                }

                sourceType = sourceType.BaseType;
            }
        }

        private static void Enqueue(object value, Queue<object> queue, ISet<object> visited)
        {
            if (value == null)
                return;

            if (!visited.Add(value))
                return;

            queue.Enqueue(value);
        }

        private static bool TryResolvePropertyValue(SerializedProperty property, out object value)
        {
            value = null;
            if (property?.serializedObject?.targetObject == null)
                return false;

            object current = property.serializedObject.targetObject;
            var path = property.propertyPath.Replace(".Array.data[", "[");
            var elements = path.Split('.');
            foreach (var element in elements)
            {
                if (current == null)
                    return false;

                current = GetPathElementValue(current, element);
            }

            value = current;
            return value != null;
        }

        private static object GetPathElementValue(object source, string pathElement)
        {
            if (source == null || string.IsNullOrEmpty(pathElement))
                return null;

            var bracketIndex = pathElement.IndexOf('[');
            if (bracketIndex < 0)
                return GetMemberValue(source, pathElement);

            var memberName = pathElement.Substring(0, bracketIndex);
            var memberValue = GetMemberValue(source, memberName);
            var indexText = pathElement.Substring(bracketIndex + 1, pathElement.Length - bracketIndex - 2);
            return int.TryParse(indexText, out var index)
                ? GetIndexedValue(memberValue, index)
                : null;
        }

        private static object GetMemberValue(object source, string memberName)
        {
            if (source == null || string.IsNullOrEmpty(memberName))
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var sourceType = source.GetType();
            while (sourceType != null)
            {
                var field = sourceType.GetField(memberName, flags);
                if (field != null)
                    return field.GetValue(source);

                var property = sourceType.GetProperty(memberName, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(source);

                sourceType = sourceType.BaseType;
            }

            return null;
        }

        private static object GetIndexedValue(object source, int index)
        {
            if (source is IList list)
            {
                return index >= 0 && index < list.Count ? list[index] : null;
            }

            if (source is IEnumerable enumerable)
            {
                var currentIndex = 0;
                foreach (var item in enumerable)
                {
                    if (currentIndex == index)
                        return item;

                    currentIndex++;
                }
            }

            return null;
        }

        private readonly struct PreviewState
        {
            internal PreviewState(Texture displayTexture, string message)
            {
                DisplayTexture = displayTexture;
                Message = message;
            }

            internal Texture DisplayTexture { get; }
            internal string Message { get; }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
