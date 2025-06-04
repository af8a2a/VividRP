using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityEngine.Rendering.Universal
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class UIScratch : MonoBehaviour, IDragHandler, IPointerEnterHandler, IPointerExitHandler, IMaterialModifier
    {
        #region Property

        private Vector3 position = Vector3.zero;
        private Vector3 lastPosition = Vector3.zero;
        public Texture2D traceTex = null;

        [Range(0f, 1f)] public float traceSize = 0.1f;
        [Range(0f, 1f)] public float finishRate = 0.8f;
        private float lastFinishRate = 0f;

        Texture2D lastTraceTex;
        float lastTraScale;

        RawImage _rawImage;
        private bool _entered = false;

        public Vector2 scale = Vector2.one;
        private Material _material;
        private Graphic m_Graphic;
        private Image _image;
        int rendererId = -1;
        private bool isForceUpdate = false;

        public Graphic graphic
        {
            get { return m_Graphic ??= GetComponent<Graphic>(); }
        }


        Image image
        {
            get { return _image ??= GetComponent<Image>(); }
        }

        #endregion

        #region LifeCycle

        void OnEnable()
        {
            if (graphic) graphic.SetMaterialDirty();
            rendererId = -1;
            IsDirty();
        }


        void OnDisable()
        {
            if (graphic) graphic.SetMaterialDirty();
            UIScratchEffectSystem.instance.UnRegist(rendererId);
        }

        bool IsDirty()
        {
            if (lastTraceTex != traceTex ||
                lastFinishRate != finishRate ||
                lastPosition != position)
            {
                lastFinishRate = finishRate;
                lastPosition = position;
                lastTraceTex = traceTex;
                return true;
            }


            return false;
        }

        void LateUpdate()
        {
            if (!_material || _material.shader.name != "UI_Scratch")
                return;

            if (rendererId == -1)
            {
                rendererId = UIScratchEffectSystem.instance.Regist(this);
                IsDirty();
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(image.rectTransform, position, null,
                    out var localPos) && _entered)
            {
                Vector2 size = image.rectTransform.rect.size;
                localPos = new Vector2(localPos.x / size.x + 0.5f, localPos.y / size.y + 0.5f);
                UIScratchEffectSystem.instance.UpdateTracePos(rendererId, localPos);
            }

            if (IsDirty())
            {
                UIScratchEffectSystem.instance.UpdateData(rendererId, this, true);
                _material.SetTexture("_TraceTexture", UIScratchEffectSystem.instance.GetRenderTextureByID(rendererId));
                _material.SetFloat("_FinishRate", finishRate);
            }

            isForceUpdate = false;
        }

        public Material GetModifiedMaterial(Material baseMaterial)
        {
            if (!enabled) return baseMaterial;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                _material = new Material(baseMaterial);

                _material.hideFlags = HideFlags.NotEditable;
                isForceUpdate = true;
            }
            else if (_material == null || _material.shader != baseMaterial.shader)
            {
                _material = new Material(baseMaterial);
                isForceUpdate = true;
            }
#else
        if (_material == null || _material.shader != baseMaterial.shader)
        {
            _material = new Material(baseMaterial);
            isForceUpdate = true;
        }
#endif

            return _material;
        }

        #endregion

        #region InputCallback

        public void OnDrag(PointerEventData eventData)
        {
            position = eventData.position;
        }


        public void OnPointerEnter(PointerEventData eventData)
        {
            _entered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _entered = false;
        }

        #endregion


        //debug only
        public void Clear()
        {
            UIScratchEffectSystem.instance.Clear(rendererId);
        }
    }
}
