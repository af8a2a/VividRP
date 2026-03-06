using System;
using System.Collections;
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
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var textureProperty = property.FindPropertyRelative("m_Texture");
            var previewNode = ResolvePreviewNode(property);

            var liveInfo = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            liveInfo.style.marginBottom = 4;
            root.Add(liveInfo);

            var textureField = new ObjectField("Fallback")
            {
                objectType = typeof(Texture),
                allowSceneObjects = false,
            };
            textureField.BindProperty(textureProperty);
            root.Add(textureField);

            var previewHint = new HelpBox(
                "If no live runtime preview is available, this fallback texture is shown instead.",
                HelpBoxMessageType.Info);
            previewHint.style.marginTop = 4;
            root.Add(previewHint);

            var previewImage = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
            };
            previewImage.style.height = 120;
            previewImage.style.marginTop = 4;
            previewImage.style.unityBackgroundImageTintColor = Color.white;
            root.Add(previewImage);

            void RefreshPreview()
            {
                var fallbackTexture = textureProperty.objectReferenceValue as Texture;
                var displayTexture = fallbackTexture;
                var liveMessage = "Connect a texture output to preview it.";

                if (previewNode != null && previewNode.TryGetConnectedPassOutput(out var passType, out var fieldName))
                {
                    if (RenderGraphPreviewRegistry.TryGetPreview(passType, fieldName, out var runtimeTexture))
                    {
                        displayTexture = runtimeTexture;
                        liveMessage = $"Live preview from {passType.Name}.{fieldName}.";
                    }
                    else
                    {
                        liveMessage = $"Connected to {passType.Name}.{fieldName}. Enter Play Mode and let a camera render to populate the live preview.";
                    }
                }
                else if (previewNode != null && previewNode.HasConnectedTextureInput())
                {
                    liveMessage = "Connected texture has no live preview provider yet. Showing fallback texture if assigned.";
                }

                liveInfo.text = liveMessage;
                UpdatePreview(displayTexture, previewHint, previewImage);
            }

            RefreshPreview();
            textureField.RegisterValueChangedCallback(evt =>
            {
                RefreshPreview();
            });
            root.schedule.Execute(RefreshPreview).Every(250);

            return root;
        }

        private static PreviewNodeData ResolvePreviewNode(SerializedProperty property)
        {
            if (property?.serializedObject?.targetObject == null)
                return null;

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
                if (property != null)
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

        private static void UpdatePreview(Texture texture, VisualElement previewHint, Image previewImage)
        {
            previewImage.image = texture;
            previewImage.style.display = texture != null ? DisplayStyle.Flex : DisplayStyle.None;
            previewHint.style.display = texture == null ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
