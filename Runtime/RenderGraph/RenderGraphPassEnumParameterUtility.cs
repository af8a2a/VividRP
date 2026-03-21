using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VividRP.Runtime
{
    internal static class RenderGraphPassEnumParameterUtility
    {
        private const string EnumParameterOptionPrefix = "EnumParameter:";

        internal static IEnumerable<FieldInfo> EnumerateSerializableEnumFields(Type passType)
        {
            if (passType == null)
                yield break;

            foreach (var field in RenderGraphPassReflectionUtility.EnumerateInstanceFields(passType))
            {
                if (!IsSerializableEnumField(field))
                    continue;

                yield return field;
            }
        }

        internal static string GetOptionName(string fieldName)
        {
            return string.IsNullOrEmpty(fieldName)
                ? EnumParameterOptionPrefix
                : $"{EnumParameterOptionPrefix}{fieldName}";
        }

        internal static object GetDefaultValue(Type passType, FieldInfo field)
        {
            if (field == null || !IsSerializableEnumField(field))
                return null;

            try
            {
                var passInstance = Activator.CreateInstance(passType);
                if (passInstance == null)
                    return Activator.CreateInstance(field.FieldType);

                return field.GetValue(passInstance) ?? Activator.CreateInstance(field.FieldType);
            }
            catch
            {
                return Activator.CreateInstance(field.FieldType);
            }
        }

        internal static void ApplyEnumParameters(object pass, Type passType, IReadOnlyList<RenderGraphPassEnumParameter> enumParameters)
        {
            if (pass == null || passType == null || enumParameters == null || enumParameters.Count == 0)
                return;

            for (var i = 0; i < enumParameters.Count; i++)
            {
                var parameter = enumParameters[i];
                if (parameter == null || string.IsNullOrEmpty(parameter.FieldName))
                    continue;

                var field = RenderGraphPassReflectionUtility.GetInstanceField(passType, parameter.FieldName);
                if (!IsSerializableEnumField(field))
                    continue;

                field.SetValue(pass, Enum.ToObject(field.FieldType, parameter.Value));
            }
        }

        private static bool IsSerializableEnumField(FieldInfo field)
        {
            if (field == null
                || field.IsStatic
                || field.IsInitOnly
                || field.IsLiteral
                || field.IsNotSerialized
                || !field.FieldType.IsEnum
                || field.GetCustomAttribute<RenderGraphResource>() != null)
            {
                return false;
            }

            return field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
        }
    }
}
