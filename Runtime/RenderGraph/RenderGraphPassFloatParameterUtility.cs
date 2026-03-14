using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VividRP.Runtime
{
    internal static class RenderGraphPassFloatParameterUtility
    {
        private const string FloatParameterOptionPrefix = "FloatParameter:";

        internal static IEnumerable<FieldInfo> EnumerateSerializableFloatFields(Type passType)
        {
            if (passType == null)
                yield break;

            foreach (var field in RenderGraphPassReflectionUtility.EnumerateInstanceFields(passType))
            {
                if (!IsSerializableFloatField(field))
                    continue;

                yield return field;
            }
        }

        internal static string GetOptionName(string fieldName)
        {
            return string.IsNullOrEmpty(fieldName)
                ? FloatParameterOptionPrefix
                : $"{FloatParameterOptionPrefix}{fieldName}";
        }

        internal static float GetDefaultValue(Type passType, FieldInfo field)
        {
            if (passType == null || field == null || field.FieldType != typeof(float))
                return 0f;

            try
            {
                var passInstance = Activator.CreateInstance(passType);
                if (passInstance == null)
                    return 0f;

                return field.GetValue(passInstance) is float value
                    ? value
                    : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        internal static void ApplyFloatParameters(object pass, Type passType, IReadOnlyList<RenderGraphPassFloatParameter> floatParameters)
        {
            if (pass == null || passType == null || floatParameters == null || floatParameters.Count == 0)
                return;

            for (var i = 0; i < floatParameters.Count; i++)
            {
                var parameter = floatParameters[i];
                if (parameter == null || string.IsNullOrEmpty(parameter.FieldName))
                    continue;

                var field = RenderGraphPassReflectionUtility.GetInstanceField(passType, parameter.FieldName);
                if (!IsSerializableFloatField(field))
                    continue;

                var value = parameter.Value;
                var range = field.GetCustomAttribute<RangeAttribute>();
                if (range != null)
                    value = Mathf.Clamp(value, range.min, range.max);

                field.SetValue(pass, value);
            }
        }

        private static bool IsSerializableFloatField(FieldInfo field)
        {
            if (field == null
                || field.IsStatic
                || field.IsInitOnly
                || field.IsLiteral
                || field.IsNotSerialized
                || field.FieldType != typeof(float)
                || field.GetCustomAttribute<RenderGraphResource>() != null)
            {
                return false;
            }

            return field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
        }
    }
}
