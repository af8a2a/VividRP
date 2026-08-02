using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Wizard
{
    internal static class VividWizardConfiguration
    {
        private const string BuildProfileGraphicsSettingsTypeName =
            "UnityEditor.Build.Profile.BuildProfileGraphicsSettings";

        private const string ShaderBuildSettingsPropertyName = "m_ShaderBuildSettings";
        private const string CompilerSettingsPropertyName = "compilerSettings";
        private const string GraphicsApiPropertyName = "graphicsAPI";
        private const string CompilerToolchainPropertyName = "compilerToolchainOverride";
        private const string OptimizationLevelPropertyName = "optimizationLevel";
        private const string EnableDebugSymbolsPropertyName = "enableDebugSymbols";

        internal const int DefaultCompilerToolchainValue = 0;
        internal const int DxcCompilerToolchainValue = 2;
        internal const int DefaultOptimizationLevelValue = 0;

        internal static bool IsDirect3D12Configured()
        {
            const BuildTarget target = BuildTarget.StandaloneWindows64;
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(target))
                return false;

            var graphicsApis = PlayerSettings.GetGraphicsAPIs(target);
            return graphicsApis != null
                && graphicsApis.Length > 0
                && graphicsApis[0] == GraphicsDeviceType.Direct3D12;
        }

        internal static bool EnsureDirect3D12IsConfigured()
        {
            const BuildTarget target = BuildTarget.StandaloneWindows64;
            var graphicsApis = PlayerSettings.GetGraphicsAPIs(target);
            var configuredGraphicsApis = BuildDirect3D12FirstApiList(graphicsApis);
            var changed = PlayerSettings.GetUseDefaultGraphicsAPIs(target)
                || !AreGraphicsApiListsEqual(graphicsApis, configuredGraphicsApis);

            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            if (!AreGraphicsApiListsEqual(graphicsApis, configuredGraphicsApis))
                PlayerSettings.SetGraphicsAPIs(target, configuredGraphicsApis);

            return changed;
        }

        internal static GraphicsDeviceType[] BuildDirect3D12FirstApiList(
            IReadOnlyList<GraphicsDeviceType> graphicsApis)
        {
            var result = new List<GraphicsDeviceType> { GraphicsDeviceType.Direct3D12 };
            if (graphicsApis == null)
                return result.ToArray();

            for (var index = 0; index < graphicsApis.Count; index++)
            {
                var graphicsApi = graphicsApis[index];
                if (graphicsApi != GraphicsDeviceType.Direct3D12 && !result.Contains(graphicsApi))
                    result.Add(graphicsApi);
            }

            return result.ToArray();
        }

        internal static bool TryGetActiveBuildProfileGraphicsSettings(
            out BuildProfile buildProfile,
            out Object graphicsSettings,
            out string error)
        {
            buildProfile = BuildProfile.GetActiveBuildProfile();
            graphicsSettings = null;

            if (buildProfile == null)
            {
                error = "No active Build Profile. Create or activate a Windows Build Profile first.";
                return false;
            }

            using (var serializedBuildProfile = new SerializedObject(buildProfile))
            {
                var buildTargetProperty = serializedBuildProfile.FindProperty("m_BuildTarget");
                if (buildTargetProperty == null)
                {
                    error = "Could not determine the active Build Profile platform.";
                    return false;
                }

                if ((BuildTarget)buildTargetProperty.intValue != BuildTarget.StandaloneWindows64)
                {
                    error = $"Build Profile '{buildProfile.name}' is not a Standalone Windows 64 profile.";
                    return false;
                }
            }

            var graphicsSettingsType = typeof(BuildProfile).Assembly.GetType(BuildProfileGraphicsSettingsTypeName);
            if (graphicsSettingsType == null)
            {
                error = "The Build Profile graphics settings API is unavailable in this Unity version.";
                return false;
            }

            var getComponentMethod = FindBuildProfileGetComponentMethod();
            if (getComponentMethod == null)
            {
                error = "Could not access the active Build Profile graphics settings.";
                return false;
            }

            try
            {
                graphicsSettings = getComponentMethod
                    .MakeGenericMethod(graphicsSettingsType)
                    .Invoke(buildProfile, null) as Object;
            }
            catch (Exception exception)
            {
                error = $"Could not read Build Profile graphics settings: {exception.GetBaseException().Message}";
                return false;
            }

            if (graphicsSettings == null)
            {
                error = $"Build Profile '{buildProfile.name}' does not contain Graphics Settings.";
                return false;
            }

            error = null;
            return true;
        }

        internal static bool IsDxcConfigured(Object graphicsSettings, out string error)
        {
            if (!TryGetCompilerSettingsProperty(graphicsSettings, out var serializedObject,
                    out var compilerSettings, out error))
            {
                return false;
            }

            try
            {
                var foundDirect3D12 = false;
                for (var index = 0; index < compilerSettings.arraySize; index++)
                {
                    var entry = compilerSettings.GetArrayElementAtIndex(index);
                    var graphicsApi = entry.FindPropertyRelative(GraphicsApiPropertyName);
                    var compilerToolchain = entry.FindPropertyRelative(CompilerToolchainPropertyName);
                    if (graphicsApi == null || compilerToolchain == null)
                    {
                        error = "The Build Profile shader compiler settings format is unsupported.";
                        return false;
                    }

                    if (graphicsApi.intValue != (int)GraphicsDeviceType.Direct3D12)
                        continue;

                    foundDirect3D12 = true;
                    if (compilerToolchain.intValue != DxcCompilerToolchainValue)
                    {
                        error = null;
                        return false;
                    }
                }

                error = null;
                return foundDirect3D12;
            }
            finally
            {
                serializedObject.Dispose();
            }
        }

        internal static bool TryEnsureDxcIsConfigured(
            Object graphicsSettings,
            out bool changed,
            out string error)
        {
            changed = false;
            if (!TryGetCompilerSettingsProperty(graphicsSettings, out var serializedObject,
                    out var compilerSettings, out error))
            {
                return false;
            }

            try
            {
                var matchingEntries = new List<SerializedProperty>();
                for (var index = 0; index < compilerSettings.arraySize; index++)
                {
                    var entry = compilerSettings.GetArrayElementAtIndex(index);
                    var graphicsApi = entry.FindPropertyRelative(GraphicsApiPropertyName);
                    var compilerToolchain = entry.FindPropertyRelative(CompilerToolchainPropertyName);
                    if (graphicsApi == null || compilerToolchain == null)
                    {
                        error = "The Build Profile shader compiler settings format is unsupported.";
                        return false;
                    }

                    if (graphicsApi.intValue == (int)GraphicsDeviceType.Direct3D12)
                        matchingEntries.Add(entry.Copy());
                }

                for (var index = 0; index < matchingEntries.Count; index++)
                {
                    var compilerToolchain = matchingEntries[index]
                        .FindPropertyRelative(CompilerToolchainPropertyName);
                    if (compilerToolchain.intValue != DxcCompilerToolchainValue)
                        changed = true;
                }

                if (matchingEntries.Count == 0)
                    changed = true;

                if (!changed)
                {
                    error = null;
                    return true;
                }

                Undo.RecordObject(graphicsSettings, "Configure VividRP DXC shader compiler");
                if (matchingEntries.Count == 0)
                {
                    var newIndex = compilerSettings.arraySize;
                    compilerSettings.arraySize++;
                    var entry = compilerSettings.GetArrayElementAtIndex(newIndex);
                    entry.FindPropertyRelative(GraphicsApiPropertyName).intValue =
                        (int)GraphicsDeviceType.Direct3D12;
                    entry.FindPropertyRelative(CompilerToolchainPropertyName).intValue =
                        DxcCompilerToolchainValue;
                    entry.FindPropertyRelative(OptimizationLevelPropertyName).intValue =
                        DefaultOptimizationLevelValue;
                    entry.FindPropertyRelative(EnableDebugSymbolsPropertyName).boolValue = false;
                }
                else
                {
                    for (var index = 0; index < matchingEntries.Count; index++)
                    {
                        matchingEntries[index].FindPropertyRelative(CompilerToolchainPropertyName).intValue =
                            DxcCompilerToolchainValue;
                    }
                }

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(graphicsSettings);
                if (EditorUtility.IsPersistent(graphicsSettings))
                    AssetDatabase.SaveAssetIfDirty(graphicsSettings);

                error = null;
                return true;
            }
            finally
            {
                serializedObject.Dispose();
            }
        }

        private static bool TryGetCompilerSettingsProperty(
            Object graphicsSettings,
            out SerializedObject serializedObject,
            out SerializedProperty compilerSettings,
            out string error)
        {
            serializedObject = null;
            compilerSettings = null;

            if (graphicsSettings == null)
            {
                error = "Build Profile Graphics Settings are unavailable.";
                return false;
            }

            serializedObject = new SerializedObject(graphicsSettings);
            serializedObject.Update();
            var shaderBuildSettings = serializedObject.FindProperty(ShaderBuildSettingsPropertyName);
            compilerSettings = shaderBuildSettings?.FindPropertyRelative(CompilerSettingsPropertyName);
            if (compilerSettings != null && compilerSettings.isArray)
            {
                error = null;
                return true;
            }

            serializedObject.Dispose();
            serializedObject = null;
            error = "The Build Profile shader compiler settings format is unsupported.";
            return false;
        }

        private static MethodInfo FindBuildProfileGetComponentMethod()
        {
            var methods = typeof(BuildProfile).GetMethods(BindingFlags.Instance | BindingFlags.Public);
            for (var index = 0; index < methods.Length; index++)
            {
                var method = methods[index];
                if (method.Name == nameof(BuildProfile.GetComponent)
                    && method.IsGenericMethodDefinition
                    && method.GetGenericArguments().Length == 1
                    && method.GetParameters().Length == 0)
                {
                    return method;
                }
            }

            return null;
        }

        private static bool AreGraphicsApiListsEqual(
            IReadOnlyList<GraphicsDeviceType> first,
            IReadOnlyList<GraphicsDeviceType> second)
        {
            if (ReferenceEquals(first, second))
                return true;
            if (first == null || second == null || first.Count != second.Count)
                return false;

            for (var index = 0; index < first.Count; index++)
            {
                if (first[index] != second[index])
                    return false;
            }

            return true;
        }
    }
}
