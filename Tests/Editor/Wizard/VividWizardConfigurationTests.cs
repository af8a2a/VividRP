using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Editor.Wizard;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class VividWizardConfigurationTests
    {
        private const string BuildProfileGraphicsSettingsTypeName =
            "UnityEditor.Build.Profile.BuildProfileGraphicsSettings";

        private Object m_GraphicsSettings;

        [SetUp]
        public void SetUp()
        {
            var graphicsSettingsType = typeof(BuildProfile).Assembly.GetType(BuildProfileGraphicsSettingsTypeName);
            Assert.That(graphicsSettingsType, Is.Not.Null);
            m_GraphicsSettings = ScriptableObject.CreateInstance(graphicsSettingsType);
        }

        [TearDown]
        public void TearDown()
        {
            if (m_GraphicsSettings != null)
                Object.DestroyImmediate(m_GraphicsSettings);
        }

        [Test]
        public void BuildDirect3D12FirstApiList_AddsDirect3D12FirstAndPreservesOtherApis()
        {
            var result = VividWizardConfiguration.BuildDirect3D12FirstApiList(new[]
            {
                GraphicsDeviceType.Direct3D11,
                GraphicsDeviceType.Vulkan,
                GraphicsDeviceType.Direct3D12,
                GraphicsDeviceType.Direct3D11,
            });

            Assert.That(result, Is.EqualTo(new[]
            {
                GraphicsDeviceType.Direct3D12,
                GraphicsDeviceType.Direct3D11,
                GraphicsDeviceType.Vulkan,
            }));
        }

        [Test]
        public void TryEnsureDxcIsConfigured_AddsDirect3D12DxcEntry()
        {
            var success = VividWizardConfiguration.TryEnsureDxcIsConfigured(
                m_GraphicsSettings, out var changed, out var error);

            Assert.That(success, Is.True, error);
            Assert.That(changed, Is.True);
            Assert.That(VividWizardConfiguration.IsDxcConfigured(m_GraphicsSettings, out error), Is.True, error);

            using (var serializedObject = new SerializedObject(m_GraphicsSettings))
            {
                var compilerSettings = GetCompilerSettings(serializedObject);
                Assert.That(compilerSettings.arraySize, Is.EqualTo(1));
                var entry = compilerSettings.GetArrayElementAtIndex(0);
                Assert.That(entry.FindPropertyRelative("graphicsAPI").intValue,
                    Is.EqualTo((int)GraphicsDeviceType.Direct3D12));
                Assert.That(entry.FindPropertyRelative("compilerToolchainOverride").intValue,
                    Is.EqualTo(VividWizardConfiguration.DxcCompilerToolchainValue));
                Assert.That(entry.FindPropertyRelative("optimizationLevel").intValue,
                    Is.EqualTo(VividWizardConfiguration.DefaultOptimizationLevelValue));
                Assert.That(entry.FindPropertyRelative("enableDebugSymbols").boolValue, Is.False);
            }
        }

        [Test]
        public void TryEnsureDxcIsConfigured_PreservesExistingCompilerEntryOptions()
        {
            using (var serializedObject = new SerializedObject(m_GraphicsSettings))
            {
                var compilerSettings = GetCompilerSettings(serializedObject);
                compilerSettings.arraySize = 2;

                var direct3D12Entry = compilerSettings.GetArrayElementAtIndex(0);
                direct3D12Entry.FindPropertyRelative("graphicsAPI").intValue =
                    (int)GraphicsDeviceType.Direct3D12;
                direct3D12Entry.FindPropertyRelative("compilerToolchainOverride").intValue =
                    VividWizardConfiguration.DefaultCompilerToolchainValue;
                direct3D12Entry.FindPropertyRelative("optimizationLevel").intValue = 4;
                direct3D12Entry.FindPropertyRelative("enableDebugSymbols").boolValue = true;

                var vulkanEntry = compilerSettings.GetArrayElementAtIndex(1);
                vulkanEntry.FindPropertyRelative("graphicsAPI").intValue = (int)GraphicsDeviceType.Vulkan;
                vulkanEntry.FindPropertyRelative("compilerToolchainOverride").intValue =
                    VividWizardConfiguration.DefaultCompilerToolchainValue;
                vulkanEntry.FindPropertyRelative("optimizationLevel").intValue = 3;
                vulkanEntry.FindPropertyRelative("enableDebugSymbols").boolValue = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            var success = VividWizardConfiguration.TryEnsureDxcIsConfigured(
                m_GraphicsSettings, out var changed, out var error);

            Assert.That(success, Is.True, error);
            Assert.That(changed, Is.True);

            using (var serializedObject = new SerializedObject(m_GraphicsSettings))
            {
                var compilerSettings = GetCompilerSettings(serializedObject);
                Assert.That(compilerSettings.arraySize, Is.EqualTo(2));

                var direct3D12Entry = compilerSettings.GetArrayElementAtIndex(0);
                Assert.That(direct3D12Entry.FindPropertyRelative("compilerToolchainOverride").intValue,
                    Is.EqualTo(VividWizardConfiguration.DxcCompilerToolchainValue));
                Assert.That(direct3D12Entry.FindPropertyRelative("optimizationLevel").intValue, Is.EqualTo(4));
                Assert.That(direct3D12Entry.FindPropertyRelative("enableDebugSymbols").boolValue, Is.True);

                var vulkanEntry = compilerSettings.GetArrayElementAtIndex(1);
                Assert.That(vulkanEntry.FindPropertyRelative("compilerToolchainOverride").intValue,
                    Is.EqualTo(VividWizardConfiguration.DefaultCompilerToolchainValue));
                Assert.That(vulkanEntry.FindPropertyRelative("optimizationLevel").intValue, Is.EqualTo(3));
                Assert.That(vulkanEntry.FindPropertyRelative("enableDebugSymbols").boolValue, Is.True);
            }
        }

        [Test]
        public void TryEnsureDxcIsConfigured_IsIdempotent()
        {
            Assert.That(VividWizardConfiguration.TryEnsureDxcIsConfigured(
                m_GraphicsSettings, out var firstChanged, out var firstError), Is.True, firstError);
            Assert.That(firstChanged, Is.True);

            Assert.That(VividWizardConfiguration.TryEnsureDxcIsConfigured(
                m_GraphicsSettings, out var secondChanged, out var secondError), Is.True, secondError);
            Assert.That(secondChanged, Is.False);
        }

        private static SerializedProperty GetCompilerSettings(SerializedObject serializedObject)
        {
            serializedObject.Update();
            var shaderBuildSettings = serializedObject.FindProperty("m_ShaderBuildSettings");
            Assert.That(shaderBuildSettings, Is.Not.Null);
            var compilerSettings = shaderBuildSettings.FindPropertyRelative("compilerSettings");
            Assert.That(compilerSettings, Is.Not.Null);
            return compilerSettings;
        }
    }
}
