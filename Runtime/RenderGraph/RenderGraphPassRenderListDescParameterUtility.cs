using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VividRP.Runtime
{
    internal static class RenderGraphPassRenderListDescParameterUtility
    {
        private const string OptionPrefix = "RenderListDescParameter:";

        internal static IEnumerable<FieldInfo> EnumerateSerializableFields(Type passType)
        {
            if (passType == null)
                yield break;

            foreach (var field in RenderGraphPassReflectionUtility.EnumerateInstanceFields(passType))
            {
                if (IsSerializableField(field))
                    yield return field;
            }
        }

        internal static string GetOptionName(string fieldName)
        {
            return string.IsNullOrEmpty(fieldName)
                ? OptionPrefix
                : $"{OptionPrefix}{fieldName}";
        }

        internal static RenderGraphRenderListDesc GetDefaultValue(Type passType, FieldInfo field)
        {
            if (passType == null || !IsSerializableField(field))
                return new RenderGraphRenderListDesc();

            try
            {
                var passInstance = Activator.CreateInstance(passType);
                return field.GetValue(passInstance) is RenderGraphRenderListDesc value
                    ? value.Clone()
                    : new RenderGraphRenderListDesc();
            }
            catch
            {
                return new RenderGraphRenderListDesc();
            }
        }

        internal static void ApplyParameters(
            object pass,
            Type passType,
            IReadOnlyList<RenderGraphPassRenderListDescParameter> parameters)
        {
            if (pass == null || passType == null || parameters == null || parameters.Count == 0)
                return;

            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (parameter == null || string.IsNullOrEmpty(parameter.FieldName))
                    continue;

                var field = RenderGraphPassReflectionUtility.GetInstanceField(passType, parameter.FieldName);
                if (!IsSerializableField(field))
                    continue;

                field.SetValue(
                    pass,
                    parameter.Value != null
                        ? parameter.Value.Clone()
                        : new RenderGraphRenderListDesc());
            }
        }

        private static bool IsSerializableField(FieldInfo field)
        {
            if (field == null
                || field.IsStatic
                || field.IsInitOnly
                || field.IsLiteral
                || field.IsNotSerialized
                || field.FieldType != typeof(RenderGraphRenderListDesc)
                || field.GetCustomAttribute<RenderGraphResource>() != null)
            {
                return false;
            }

            return field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
        }
    }
}
