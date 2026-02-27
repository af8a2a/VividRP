using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Runtime.RenderGraph.Passes.DataDriven
{
    public sealed class RasterAttachmentField
    {
        public string FieldName { get; }
        public string DisplayName { get; }
        public ResourceIntent Intent { get; }
        public int MrtIndex { get; }

        public RasterAttachmentField(string fieldName, string displayName, ResourceIntent intent, int mrtIndex)
        {
            FieldName = fieldName;
            DisplayName = displayName;
            Intent = intent;
            MrtIndex = mrtIndex;
        }
    }

    public sealed class RasterDepthField
    {
        public string FieldName { get; }
        public string DisplayName { get; }
        public ResourceIntent Intent { get; }

        public RasterDepthField(string fieldName, string displayName, ResourceIntent intent)
        {
            FieldName = fieldName;
            DisplayName = displayName;
            Intent = intent;
        }
    }

    public sealed class RasterReadField
    {
        public string FieldName { get; }
        public string DisplayName { get; }
        public PortType PortType { get; }

        public RasterReadField(string fieldName, string displayName, PortType portType)
        {
            FieldName = fieldName;
            DisplayName = displayName;
            PortType = portType;
        }
    }

    public sealed class RasterRendererListField
    {
        public string FieldName { get; }
        public string DisplayName { get; }

        public RasterRendererListField(string fieldName, string displayName)
        {
            FieldName = fieldName;
            DisplayName = displayName;
        }
    }

    public sealed class RasterPassLayout
    {
        public Type PassLogicType { get; }
        public RasterAttachmentField[] ColorAttachments { get; }
        public RasterDepthField DepthAttachment { get; }
        public RasterReadField[] ReadResources { get; }
        public RasterRendererListField[] RendererLists { get; }

        public bool HasDepthAttachment => DepthAttachment != null;

        public RasterPassLayout(
            Type passLogicType,
            RasterAttachmentField[] colorAttachments,
            RasterDepthField depthAttachment,
            RasterReadField[] readResources,
            RasterRendererListField[] rendererLists)
        {
            PassLogicType = passLogicType;
            ColorAttachments = colorAttachments;
            DepthAttachment = depthAttachment;
            ReadResources = readResources;
            RendererLists = rendererLists;
        }
    }

    public static class RasterPassReflectionCompiler
    {
        private const int MaxMrtCount = 8;

        private sealed class CacheEntry
        {
            public bool IsValid;
            public RasterPassLayout Layout;
            public string[] Errors;
        }

        private static readonly Dictionary<Type, CacheEntry> s_Cache = new();

        public static bool TryCompile(Type passLogicType, out RasterPassLayout layout, out string[] errors)
        {
            if (passLogicType == null)
            {
                layout = null;
                errors = new[] { "Pass logic type is null." };
                return false;
            }

            if (!typeof(DataDrivenRasterPassLogic).IsAssignableFrom(passLogicType))
            {
                layout = null;
                errors = new[]
                {
                    $"Pass logic type '{passLogicType.FullName}' must inherit {nameof(DataDrivenRasterPassLogic)}."
                };
                return false;
            }

            if (!s_Cache.TryGetValue(passLogicType, out var entry))
            {
                entry = Compile(passLogicType);
                s_Cache[passLogicType] = entry;
            }

            layout = entry.Layout;
            errors = entry.Errors;
            return entry.IsValid;
        }

        private static CacheEntry Compile(Type passLogicType)
        {
            var errors = new List<string>();
            var colorAttachments = new List<RasterAttachmentField>();
            var readResources = new List<RasterReadField>();
            var rendererLists = new List<RasterRendererListField>();
            RasterDepthField depthAttachment = null;

            var fields = GetOrderedFields(passLogicType);
            int mrtIndex = 0;

            foreach (var field in fields)
            {
                var resourceAttr = field.GetCustomAttribute<PassResourceAttribute>(false);
                if (resourceAttr == null)
                    continue;

                bool isDepth = field.IsDefined(typeof(PassDepthAttribute), false);
                string displayName = NicifyFieldName(field.Name);

                if (field.FieldType == typeof(TextureHandle))
                {
                    if (isDepth)
                    {
                        if (depthAttachment != null)
                        {
                            errors.Add($"Pass logic '{passLogicType.Name}' defines multiple [PassDepth] fields.");
                            continue;
                        }

                        if (resourceAttr.Intent == ResourceIntent.Read)
                        {
                            errors.Add($"Depth field '{field.Name}' must be [PassWrite] or [PassReadWrite].");
                            continue;
                        }

                        depthAttachment = new RasterDepthField(field.Name, displayName, resourceAttr.Intent);
                        continue;
                    }

                    if (resourceAttr.Intent == ResourceIntent.Read)
                    {
                        readResources.Add(new RasterReadField(field.Name, displayName, PortType.Texture));
                    }
                    else
                    {
                        colorAttachments.Add(new RasterAttachmentField(
                            field.Name, displayName, resourceAttr.Intent, mrtIndex));
                        mrtIndex++;
                    }

                    continue;
                }

                if (field.FieldType == typeof(BufferHandle))
                {
                    if (isDepth)
                    {
                        errors.Add($"Field '{field.Name}' is marked [PassDepth] but is not a TextureHandle.");
                        continue;
                    }

                    if (resourceAttr.Intent != ResourceIntent.Read)
                    {
                        errors.Add($"Buffer field '{field.Name}' only supports [PassRead].");
                        continue;
                    }

                    readResources.Add(new RasterReadField(field.Name, displayName, PortType.Buffer));
                    continue;
                }

                if (field.FieldType == typeof(RendererListHandle))
                {
                    if (isDepth)
                    {
                        errors.Add($"Field '{field.Name}' is marked [PassDepth] but is a RendererListHandle.");
                        continue;
                    }

                    if (resourceAttr.Intent != ResourceIntent.Read)
                    {
                        errors.Add($"Renderer list field '{field.Name}' only supports [PassRead].");
                        continue;
                    }

                    rendererLists.Add(new RasterRendererListField(field.Name, displayName));
                    continue;
                }

                errors.Add(
                    $"Field '{field.Name}' has a pass intent attribute but unsupported type '{field.FieldType.Name}'.");
            }

            if (colorAttachments.Count > MaxMrtCount)
            {
                errors.Add($"MRT overflow in '{passLogicType.Name}': color attachment count {colorAttachments.Count} exceeds {MaxMrtCount}.");
            }

            if (colorAttachments.Count == 0 && depthAttachment == null)
            {
                errors.Add($"Pass logic '{passLogicType.Name}' does not declare any writable attachments.");
            }

            return new CacheEntry
            {
                IsValid = errors.Count == 0,
                Layout = errors.Count == 0
                    ? new RasterPassLayout(
                        passLogicType,
                        colorAttachments.ToArray(),
                        depthAttachment,
                        readResources.ToArray(),
                        rendererLists.ToArray())
                    : null,
                Errors = errors.ToArray()
            };
        }

        private static FieldInfo[] GetOrderedFields(Type passLogicType)
        {
            var orderedFields = new List<FieldInfo>();
            var hierarchy = new Stack<Type>();
            var current = passLogicType;

            while (current != null && typeof(DataDrivenRasterPassLogic).IsAssignableFrom(current))
            {
                hierarchy.Push(current);
                current = current.BaseType;
            }

            while (hierarchy.Count > 0)
            {
                var type = hierarchy.Pop();
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                            BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                orderedFields.AddRange(fields);
            }

            return orderedFields.ToArray();
        }

        private static string NicifyFieldName(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return "Resource";

            var chars = new List<char>(fieldName.Length + 4);
            for (int i = 0; i < fieldName.Length; i++)
            {
                char c = fieldName[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(fieldName[i - 1]))
                    chars.Add(' ');

                chars.Add(i == 0 ? char.ToUpperInvariant(c) : c);
            }

            return new string(chars.ToArray());
        }
    }
}
