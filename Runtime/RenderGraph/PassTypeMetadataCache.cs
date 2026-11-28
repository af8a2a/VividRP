using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Serialization;

namespace VividRP.Runtime
{
    internal sealed class PassResourceFieldMetadata
    {
        internal PassResourceFieldMetadata(
            FieldInfo field,
            RenderGraphResource resourceAttribute,
            PassBypassAttribute bypassAttribute,
            bool isDeclaredTransient,
            bool isTransient,
            string displayName,
            Func<object, object> getter,
            Action<object, object> setter)
        {
            Field = field;
            ResourceAttribute = resourceAttribute;
            BypassAttribute = bypassAttribute;
            IsDeclaredTransient = isDeclaredTransient;
            IsTransient = isTransient;
            DisplayName = displayName;
            Getter = getter;
            Setter = setter;
        }

        internal FieldInfo Field { get; }
        internal RenderGraphResource ResourceAttribute { get; }
        internal PassBypassAttribute BypassAttribute { get; }
        internal bool IsDeclaredTransient { get; }
        internal bool IsTransient { get; }
        internal string DisplayName { get; }
        internal Func<object, object> Getter { get; }
        internal Action<object, object> Setter { get; }
    }

    internal sealed class PassTypeMetadata
    {
        internal PassTypeMetadata(
            Type passType,
            PassResourceFieldMetadata[] resourceFields,
            Dictionary<string, PassResourceFieldMetadata> resourceFieldsByName,
            FrameResourceSlotAttribute[] slotAccesses,
            CameraHistoryAccessAttribute[] historyAccesses,
            bool hasCameraHistoryWrite)
        {
            PassType = passType;
            ResourceFields = resourceFields;
            ResourceFieldsByName = resourceFieldsByName;
            SlotAccesses = slotAccesses;
            HistoryAccesses = historyAccesses;
            HasCameraHistoryWrite = hasCameraHistoryWrite;
        }

        internal Type PassType { get; }
        internal PassResourceFieldMetadata[] ResourceFields { get; }
        internal Dictionary<string, PassResourceFieldMetadata> ResourceFieldsByName { get; }
        internal FrameResourceSlotAttribute[] SlotAccesses { get; }
        internal CameraHistoryAccessAttribute[] HistoryAccesses { get; }
        internal bool HasCameraHistoryWrite { get; }

        internal bool TryGetResourceField(string fieldName, out PassResourceFieldMetadata fieldMetadata)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                fieldMetadata = null;
                return false;
            }

            return ResourceFieldsByName.TryGetValue(fieldName, out fieldMetadata);
        }
    }

    /// <summary>
    /// Per-type cache of RenderGraph pass reflection metadata and compiled field accessors.
    /// Eliminates repeated attribute reflection and FieldInfo.GetValue/SetValue overhead on hot and compile paths.
    /// </summary>
    internal static class PassTypeMetadataCache
    {
        private static readonly Dictionary<Type, PassTypeMetadata> s_Cache = new();
        private static readonly object s_Lock = new();

        internal static PassTypeMetadata GetOrCreate(Type passType)
        {
            if (passType == null)
                return null;

            lock (s_Lock)
            {
                if (s_Cache.TryGetValue(passType, out var cached))
                    return cached;

                cached = Build(passType);
                s_Cache[passType] = cached;
                return cached;
            }
        }

        internal static void Clear()
        {
            lock (s_Lock)
            {
                s_Cache.Clear();
            }
        }

        private static PassTypeMetadata Build(Type passType)
        {
            var resourceFields = new List<PassResourceFieldMetadata>();
            var byName = new Dictionary<string, PassResourceFieldMetadata>(StringComparer.Ordinal);

            foreach (var field in passType.EnumerateInstanceFields())
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (attr == null)
                    continue;

                var hasTransientAttr = field.GetCustomAttribute<TransientResourceAttribute>() != null;
                var isTransient = hasTransientAttr && field.FieldType.IsSupportedTransientResourceFieldType();
                var bypassAttr = field.GetCustomAttribute<PassBypassAttribute>();
                var displayName = field.GetRenderGraphResourceName(attr);
                var getter = CompileGetter(field);
                var setter = field.IsInitOnly ? null : CompileSetter(field);

                var metadata = new PassResourceFieldMetadata(
                    field,
                    attr,
                    bypassAttr,
                    hasTransientAttr,
                    isTransient,
                    displayName,
                    getter,
                    setter);

                resourceFields.Add(metadata);
                byName[field.Name] = metadata;
            }

            // Register [FormerlySerializedAs] aliases without overwriting exact field names.
            for (var i = 0; i < resourceFields.Count; i++)
            {
                var metadata = resourceFields[i];
                foreach (var formerlySerializedAs in metadata.Field.GetCustomAttributes<FormerlySerializedAsAttribute>())
                {
                    if (!string.IsNullOrEmpty(formerlySerializedAs.oldName)
                        && !byName.ContainsKey(formerlySerializedAs.oldName))
                    {
                        byName[formerlySerializedAs.oldName] = metadata;
                    }
                }
            }

            var slotList = new List<FrameResourceSlotAttribute>();
            foreach (var attr in passType.GetCustomAttributes<FrameResourceSlotAttribute>(inherit: true))
            {
                if (attr != null)
                    slotList.Add(attr);
            }

            var historyList = new List<CameraHistoryAccessAttribute>();
            var hasHistoryWrite = false;
            foreach (var attr in passType.GetCustomAttributes<CameraHistoryAccessAttribute>(inherit: true))
            {
                if (attr == null)
                    continue;

                historyList.Add(attr);
                if ((attr.Access & AccessFlags.Write) != 0)
                    hasHistoryWrite = true;
            }

            return new PassTypeMetadata(
                passType,
                resourceFields.Count > 0 ? resourceFields.ToArray() : Array.Empty<PassResourceFieldMetadata>(),
                byName,
                slotList.Count > 0 ? slotList.ToArray() : Array.Empty<FrameResourceSlotAttribute>(),
                historyList.Count > 0 ? historyList.ToArray() : Array.Empty<CameraHistoryAccessAttribute>(),
                hasHistoryWrite);
        }

        private static Func<object, object> CompileGetter(FieldInfo field)
        {
            var declaringType = field.DeclaringType;
            if (declaringType == null)
                return field.GetValue;

            try
            {
                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var typedInstance = Expression.Convert(instanceParam, declaringType);
                var fieldAccess = Expression.Field(typedInstance, field);
                var boxed = Expression.Convert(fieldAccess, typeof(object));
                return Expression.Lambda<Func<object, object>>(boxed, instanceParam).Compile();
            }
            catch
            {
                return field.GetValue;
            }
        }

        private static Action<object, object> CompileSetter(FieldInfo field)
        {
            var declaringType = field.DeclaringType;
            if (declaringType == null || field.IsInitOnly)
                return field.SetValue;

            try
            {
                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var valueParam = Expression.Parameter(typeof(object), "value");
                var typedInstance = Expression.Convert(instanceParam, declaringType);
                var typedValue = Expression.Convert(valueParam, field.FieldType);
                var assign = Expression.Assign(Expression.Field(typedInstance, field), typedValue);
                return Expression.Lambda<Action<object, object>>(assign, instanceParam, valueParam).Compile();
            }
            catch
            {
                return field.SetValue;
            }
        }
    }
}
