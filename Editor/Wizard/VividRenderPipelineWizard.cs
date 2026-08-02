using System;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Editor.Wizard
{
    internal sealed class VividRenderPipelineWizard : EditorWindow
    {
        private const string MenuPath = "Window/Rendering/VividRP Wizard";

        private Vector2 m_ScrollPosition;

        private readonly struct ConfigurationState
        {
            internal readonly bool Available;
            internal readonly bool Configured;
            internal readonly string Detail;

            internal ConfigurationState(bool available, bool configured, string detail)
            {
                Available = available;
                Configured = configured;
                Detail = detail;
            }
        }

        [MenuItem(MenuPath)]
        private static void Open()
        {
            var window = GetWindow<VividRenderPipelineWizard>();
            window.titleContent = new GUIContent("VividRP Wizard");
            window.minSize = new Vector2(520.0f, 260.0f);
            window.Show();
        }

        private void OnEnable()
        {
            BuildProfile.activeProfileChanged += OnActiveBuildProfileChanged;
            EditorApplication.projectChanged += Repaint;
        }

        private void OnDisable()
        {
            BuildProfile.activeProfileChanged -= OnActiveBuildProfileChanged;
            EditorApplication.projectChanged -= Repaint;
        }

        private void OnGUI()
        {
            var direct3D12State = GetDirect3D12State();
            var dxcState = GetDxcState();

            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
            EditorGUILayout.Space(12.0f);
            EditorGUILayout.LabelField("Vivid Render Pipeline Wizard", EditorStyles.largeLabel);
            EditorGUILayout.LabelField(
                "Configure the Windows graphics API and the active Build Profile shader compiler required by VividRP.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8.0f);

            DrawConfiguration(
                "DirectX 12 Graphics API",
                direct3D12State,
                EnsureDirect3D12IsConfigured);
            DrawConfiguration(
                "Shader Compiler Backend Selection",
                dxcState,
                EnsureDxcIsConfigured);

            if (direct3D12State.Configured
                && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                EditorGUILayout.HelpBox(
                    $"Direct3D12 is configured, but this Editor is currently using {SystemInfo.graphicsDeviceType}. " +
                    "Restart the Editor to activate Direct3D12.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8.0f);
            using (new EditorGUI.DisabledScope(direct3D12State.Configured
                       && (!dxcState.Available || dxcState.Configured)))
            {
                if (GUILayout.Button("Fix All", GUILayout.Height(26.0f)))
                    FixAll();
            }

            EditorGUILayout.EndScrollView();
        }

        private static ConfigurationState GetDirect3D12State()
        {
            if (VividWizardConfiguration.IsDirect3D12Configured())
            {
                return new ConfigurationState(
                    true,
                    true,
                    "Direct3D12 is the first graphics API for Standalone Windows 64.");
            }

            return new ConfigurationState(
                true,
                false,
                "Standalone Windows 64 must disable automatic graphics APIs and use Direct3D12 first.");
        }

        private static ConfigurationState GetDxcState()
        {
            if (!VividWizardConfiguration.TryGetActiveBuildProfileGraphicsSettings(
                    out var buildProfile, out var graphicsSettings, out var error))
            {
                return new ConfigurationState(false, false, error);
            }

            var configured = VividWizardConfiguration.IsDxcConfigured(graphicsSettings, out error);
            if (error != null)
                return new ConfigurationState(false, false, error);

            return new ConfigurationState(
                true,
                configured,
                configured
                    ? $"Build Profile '{buildProfile.name}' uses DXC for Direct3D12."
                    : $"Build Profile '{buildProfile.name}' must use DXC for Direct3D12.");
        }

        private static void DrawConfiguration(
            string title,
            ConfigurationState state,
            Action fix)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(GetStatusLabel(state), EditorStyles.miniBoldLabel, GUILayout.Width(82.0f));

                    using (new EditorGUI.DisabledScope(!state.Available || state.Configured))
                    {
                        if (GUILayout.Button("Fix", GUILayout.Width(64.0f)))
                            fix();
                    }
                }

                EditorGUILayout.LabelField(state.Detail, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static string GetStatusLabel(ConfigurationState state)
        {
            if (!state.Available)
                return "Unavailable";
            return state.Configured ? "Ready" : "Fix required";
        }

        private void EnsureDirect3D12IsConfigured()
        {
            try
            {
                if (VividWizardConfiguration.EnsureDirect3D12IsConfigured())
                    Debug.Log("VividRP Wizard configured Direct3D12 as the first Standalone Windows 64 graphics API.");
                Repaint();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowNotification(new GUIContent("Failed to configure Direct3D12. See the Console for details."));
            }
        }

        private void EnsureDxcIsConfigured()
        {
            if (!VividWizardConfiguration.TryGetActiveBuildProfileGraphicsSettings(
                    out var buildProfile, out var graphicsSettings, out var error))
            {
                ShowNotification(new GUIContent(error));
                return;
            }

            if (!VividWizardConfiguration.TryEnsureDxcIsConfigured(
                    graphicsSettings, out var changed, out error))
            {
                Debug.LogError($"VividRP Wizard could not configure DXC: {error}");
                ShowNotification(new GUIContent(error));
                return;
            }

            if (changed)
                Debug.Log($"VividRP Wizard configured DXC for Direct3D12 in Build Profile '{buildProfile.name}'.");
            Repaint();
        }

        private void FixAll()
        {
            EnsureDirect3D12IsConfigured();
            EnsureDxcIsConfigured();
        }

        private void OnActiveBuildProfileChanged(BuildProfile previous, BuildProfile current)
        {
            Repaint();
        }
    }
}
