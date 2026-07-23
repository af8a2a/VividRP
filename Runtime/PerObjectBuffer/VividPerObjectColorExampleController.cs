using UnityEngine;

namespace VividRP.Runtime.Examples
{
    /// <summary>
    /// Pushes an animated color to the shared per-object buffer without using a
    /// MaterialPropertyBlock. Add this component to a MeshRenderer or
    /// SkinnedMeshRenderer that uses VividRP/Examples/Per-Object Color.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class VividPerObjectColorExampleController : MonoBehaviour
    {
        public enum PropertyAccessMode
        {
            CachedHandle,
            PropertyId,
            PropertyName,
        }

        [SerializeField]
        private Color m_Color = Color.white;

        [SerializeField]
        private bool m_AnimateColor = true;

        [SerializeField, Min(0.0f)]
        private float m_AnimationSpeed = 0.2f;

        [SerializeField]
        private PropertyAccessMode m_PropertyAccess = PropertyAccessMode.CachedHandle;

        private Renderer m_Renderer;
        private VividPerObjectBlock m_Block;
        private VividPerObjectPropertyHandle m_ColorProperty;
        private bool m_HasWarnedAboutRenderer;

        public Color Color
        {
            get => m_Color;
            set => SetColor(value);
        }

        public PropertyAccessMode AccessMode
        {
            get => m_PropertyAccess;
            set
            {
                m_PropertyAccess = value;
                PushCurrentColor();
            }
        }

        private void OnEnable()
        {
            Bind();
            PushCurrentColor();
        }

        private void Update()
        {
            if (!EnsureBound())
                return;

            Color color = m_AnimateColor ? EvaluateAnimatedColor() : m_Color;
            PushColor(color);
        }

        private void OnValidate()
        {
            m_AnimationSpeed = Mathf.Max(0.0f, m_AnimationSpeed);
            if (!isActiveAndEnabled)
                return;

            Bind();
            PushCurrentColor();
        }

        private void OnDisable()
        {
            // Only release the record if this controller still owns the active
            // block. A different system may have rebound the Renderer meanwhile.
            if (m_Renderer != null && m_Block.IsValid)
                VividPerObjectBuffer.Unbind(m_Renderer);

            m_Block = default;
        }

        [ContextMenu("Rebind Per-Object Color")]
        public void Bind()
        {
            if (!TryResolveSupportedRenderer())
                return;

            m_Block = VividPerObjectBuffer.Bind<VividPerObjectColorExampleLayout>(m_Renderer);
            m_ColorProperty = VividPerObjectColorExampleLayout.ColorProperty;
        }

        [ContextMenu("Unbind Per-Object Color")]
        public void Unbind()
        {
            if (m_Renderer != null && m_Block.IsValid)
                VividPerObjectBuffer.Unbind(m_Renderer);

            m_Block = default;
        }

        public void SetColor(Color color)
        {
            m_Color = color;
            PushCurrentColor();
        }

        public void PushCurrentColor()
        {
            if (!isActiveAndEnabled)
                return;

            if (!EnsureBound())
                return;

            PushColor(m_Color);
        }

        private bool EnsureBound()
        {
            if (m_Block.IsValid)
                return true;

            Bind();
            return m_Block.IsValid;
        }

        private bool TryResolveSupportedRenderer()
        {
            if (m_Renderer == null)
                m_Renderer = GetComponent<Renderer>();

            if (m_Renderer is MeshRenderer or SkinnedMeshRenderer)
            {
                m_HasWarnedAboutRenderer = false;
                return true;
            }

            if (!m_HasWarnedAboutRenderer)
            {
                Debug.LogWarning(
                    $"[{nameof(VividPerObjectColorExampleController)}] " +
                    "A MeshRenderer or SkinnedMeshRenderer is required.",
                    this);
                m_HasWarnedAboutRenderer = true;
            }

            return false;
        }

        private void PushColor(Color color)
        {
            switch (m_PropertyAccess)
            {
                case PropertyAccessMode.CachedHandle:
                    m_Block.SetColor(m_ColorProperty, color);
                    break;
                case PropertyAccessMode.PropertyId:
                    m_Block.SetColor(VividPerObjectColorExampleLayout.ColorPropertyId, color);
                    break;
                case PropertyAccessMode.PropertyName:
                    m_Block.SetColor(VividPerObjectColorExampleLayout.ColorPropertyName, color);
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException();
            }
        }

        private Color EvaluateAnimatedColor()
        {
            float hue = Mathf.Repeat(Time.realtimeSinceStartup * m_AnimationSpeed, 1.0f);
            Color animatedColor = UnityEngine.Color.HSVToRGB(hue, 0.8f, 1.0f);
            animatedColor.a = m_Color.a;
            return animatedColor;
        }
    }
}
