using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime.Particle;

namespace VividRP.Editor
{
    internal enum VividParticlePreviewCommand
    {
        Play,
        Pause,
        StopAndClear,
        Restart,
    }

    [Overlay(
        typeof(SceneView),
        OverlayId,
        "Vivid Particles",
        true,
        defaultDockZone = DockZone.RightColumn,
        defaultDockPosition = DockPosition.Top,
        defaultLayout = Layout.Panel,
        defaultWidth = 280.0f,
        defaultHeight = 132.0f,
        minWidth = 240.0f,
        minHeight = 112.0f,
        group = "VividRP")]
    internal sealed class VividParticleSystemSceneOverlay : Overlay, ITransientOverlay
    {
        internal const string OverlayId = "vividrp-particle-system-preview";
        private const string ShowBoundsSessionKey = "VividRP.Particles.SceneOverlay.ShowBounds";

        public bool visible => ResolveSelectedSystem() != null;

        internal static bool showBounds
        {
            get => SessionState.GetBool(ShowBoundsSessionKey, false);
            set => SessionState.SetBool(ShowBoundsSessionKey, value);
        }

        public override VisualElement CreatePanelContent()
        {
            return new VividParticleSystemSceneOverlayContent();
        }

        internal static VividParticleSystem ResolveSelectedSystem()
        {
            GameObject selectedGameObject = Selection.activeGameObject;
            return selectedGameObject != null
                ? selectedGameObject.GetComponent<VividParticleSystem>()
                : null;
        }

        internal static bool ExecutePreviewCommand(
            VividParticleSystem system,
            VividParticlePreviewCommand command)
        {
            if (system == null)
                return false;

            switch (command)
            {
                case VividParticlePreviewCommand.Play:
                    system.Play(withChildren: false);
                    break;
                case VividParticlePreviewCommand.Pause:
                    system.Pause(withChildren: false);
                    break;
                case VividParticlePreviewCommand.StopAndClear:
                    system.Stop(
                        withChildren: false,
                        VividParticleSystemStopBehavior.StopEmittingAndClear);
                    break;
                case VividParticlePreviewCommand.Restart:
                    VividParticleSystemEditorUtility.RestartPreview(system, play: true);
                    break;
                default:
                    return false;
            }

            RequestEditorRefresh();
            return true;
        }

        internal static bool ScrubPreview(VividParticleSystem system, float time)
        {
            if (!VividParticleSystemEditorUtility.ScrubPreview(system, time))
                return false;

            RequestEditorRefresh();
            return true;
        }

        private static void RequestEditorRefresh()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
    }

    internal sealed class VividParticleSystemSceneOverlayContent : VisualElement
    {
        internal const string PlayButtonName = "vivid-particle-play";
        internal const string PauseButtonName = "vivid-particle-pause";
        internal const string RestartButtonName = "vivid-particle-restart";
        internal const string StopButtonName = "vivid-particle-stop";
        internal const string TimeSliderName = "vivid-particle-time";
        internal const string ShowBoundsToggleName = "vivid-particle-show-bounds";

        private readonly Label m_SystemLabel;
        private readonly Label m_StatusLabel;
        private readonly Label m_TimeLabel;
        private readonly Button m_PlayButton;
        private readonly Button m_PauseButton;
        private readonly Slider m_TimeSlider;
        private bool m_IsRefreshing;

        internal VividParticleSystemSceneOverlayContent()
        {
            style.minWidth = 240.0f;
            style.paddingLeft = 6.0f;
            style.paddingRight = 6.0f;
            style.paddingTop = 5.0f;
            style.paddingBottom = 5.0f;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            Add(header);

            m_SystemLabel = new Label();
            m_SystemLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_SystemLabel.style.flexGrow = 1.0f;
            header.Add(m_SystemLabel);

            m_StatusLabel = new Label();
            m_StatusLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            header.Add(m_StatusLabel);

            var controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.alignItems = Align.Center;
            controls.style.marginTop = 3.0f;
            controls.style.marginBottom = 3.0f;
            Add(controls);

            m_PlayButton = CreateIconButton(
                PlayButtonName,
                "PlayButton",
                "Play particle preview",
                "Play",
                () => Execute(VividParticlePreviewCommand.Play));
            controls.Add(m_PlayButton);

            m_PauseButton = CreateIconButton(
                PauseButtonName,
                "PauseButton",
                "Pause particle preview",
                "Pause",
                () => Execute(VividParticlePreviewCommand.Pause));
            controls.Add(m_PauseButton);

            controls.Add(CreateIconButton(
                RestartButtonName,
                "Refresh",
                "Restart particle preview",
                "Restart",
                () => Execute(VividParticlePreviewCommand.Restart)));
            controls.Add(CreateIconButton(
                StopButtonName,
                "TreeEditor.Trash",
                "Stop and clear particle preview",
                "Clear",
                () => Execute(VividParticlePreviewCommand.StopAndClear)));

            m_TimeLabel = new Label();
            m_TimeLabel.style.flexGrow = 1.0f;
            m_TimeLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            controls.Add(m_TimeLabel);

            m_TimeSlider = new Slider("Time", 0.0f, 1.0f)
            {
                name = TimeSliderName,
            };
            m_TimeSlider.RegisterValueChangedCallback(OnTimeChanged);
            Add(m_TimeSlider);

            var showBoundsToggle = new Toggle("Show Bounds")
            {
                name = ShowBoundsToggleName,
            };
            showBoundsToggle.SetValueWithoutNotify(VividParticleSystemSceneOverlay.showBounds);
            showBoundsToggle.RegisterValueChangedCallback(evt =>
            {
                VividParticleSystemSceneOverlay.showBounds = evt.newValue;
                SceneView.RepaintAll();
            });
            Add(showBoundsToggle);

            schedule.Execute(Refresh).Every(100);
            Refresh();
        }

        private static Button CreateIconButton(
            string name,
            string iconName,
            string tooltip,
            string fallbackText,
            System.Action action)
        {
            var button = new Button(action)
            {
                name = name,
                tooltip = tooltip,
                focusable = false,
            };
            button.style.width = 28.0f;
            button.style.height = 22.0f;
            button.style.marginRight = 2.0f;

            Texture icon = EditorGUIUtility.IconContent(iconName).image;
            if (icon != null)
            {
                button.Add(new Image
                {
                    image = icon,
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore,
                });
            }
            else
            {
                button.text = fallbackText;
                button.style.width = 58.0f;
            }

            return button;
        }

        private void Execute(VividParticlePreviewCommand command)
        {
            VividParticleSystemSceneOverlay.ExecutePreviewCommand(
                VividParticleSystemSceneOverlay.ResolveSelectedSystem(),
                command);
            Refresh();
        }

        private void OnTimeChanged(ChangeEvent<float> evt)
        {
            if (m_IsRefreshing)
                return;

            VividParticleSystemSceneOverlay.ScrubPreview(
                VividParticleSystemSceneOverlay.ResolveSelectedSystem(),
                evt.newValue);
            Refresh();
        }

        private void Refresh()
        {
            VividParticleSystem system = VividParticleSystemSceneOverlay.ResolveSelectedSystem();
            bool available = system != null && system.isActiveAndEnabled;
            SetEnabled(available);
            if (!available)
            {
                m_SystemLabel.text = "Vivid Particle System";
                m_StatusLabel.text = "Unavailable";
                m_TimeLabel.text = string.Empty;
                return;
            }

            m_IsRefreshing = true;
            float duration = Mathf.Max(0.01f, system.main.duration);
            float currentTime = Mathf.Clamp(system.time, 0.0f, duration);
            m_SystemLabel.text = system.name;
            m_StatusLabel.text = ResolveStatus(system);
            m_TimeLabel.text = $"{system.particleCount} particles  {currentTime:0.00}/{duration:0.00}s";
            m_PlayButton.style.display = system.isPlaying ? DisplayStyle.None : DisplayStyle.Flex;
            m_PauseButton.style.display = system.isPlaying ? DisplayStyle.Flex : DisplayStyle.None;
            m_TimeSlider.lowValue = 0.0f;
            m_TimeSlider.highValue = duration;
            m_TimeSlider.SetValueWithoutNotify(currentTime);
            m_IsRefreshing = false;
        }

        private static string ResolveStatus(VividParticleSystem system)
        {
            if (system.isPlaying)
                return "Playing";
            if (system.isPaused)
                return "Paused";
            return "Stopped";
        }
    }
}
