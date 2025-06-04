using System;
using Features.Shadow.ScreenSpaceShadow.PCSSShadow;
using UnityEngine;
using UnityEngine.Rendering;

namespace Features.Shadow.ShadowCommon
{
    public enum ShadowFilter
    {
        PCF = 0, //default URP CSM
        PCSS,
    }


    public enum ShadowScatterMode
    {
        None = 0,
        RampTexture = 1,
        SubSurface = 2,
    }

    [Serializable]
    public sealed class ShadowScatterModeParameter : VolumeParameter<ShadowScatterMode>
    {
        public ShadowScatterModeParameter(ShadowScatterMode value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }


    [Serializable]
    public sealed class ShadowAlgoParameter : VolumeParameter<ShadowFilter>
    {
        public ShadowAlgoParameter(ShadowFilter value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }


    [Serializable]
    public class CascadePartitionSplitParameter : VolumeParameter<float>
    {
        [NonSerialized] NoInterpMinFloatParameter maxDistance;
        internal bool normalized;
        [NonSerialized] CascadePartitionSplitParameter previous;
        [NonSerialized] CascadePartitionSplitParameter next;
        [NonSerialized] NoInterpClampedIntParameter cascadeCounts;
        int minCascadeToAppears;

        internal float min => previous?.value ?? 0f;
        internal float max => (cascadeCounts.value > minCascadeToAppears && next != null) ? next.value : 1f;

        internal float representationDistance => maxDistance.value;

        /// <summary>
        /// Size of the split.
        /// </summary>
        public override float value
        {
            get => m_Value;
            set => m_Value = Mathf.Clamp(value, min, max);
        }

        /// <summary>
        /// Cascade Partition split parameter constructor.
        /// </summary>
        /// <param name="value">Initial value.</param>
        /// <param name="normalized">Partition is normalized.</param>
        /// <param name="overrideState">Initial override state.</param>
        public CascadePartitionSplitParameter(float value, bool normalized = false, bool overrideState = false)
            : base(value, overrideState)
            => this.normalized = normalized;

        internal void Init(NoInterpClampedIntParameter cascadeCounts, int minCascadeToAppears, NoInterpMinFloatParameter maxDistance,
            CascadePartitionSplitParameter previous, CascadePartitionSplitParameter next)
        {
            this.maxDistance = maxDistance;
            this.previous = previous;
            this.next = next;
            this.cascadeCounts = cascadeCounts;
            this.minCascadeToAppears = minCascadeToAppears;
        }
    }


    public sealed class Shadows : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter enable = new BoolParameter(false, BoolParameter.DisplayType.EnumPopup);

        public ShadowAlgoParameter shadowAlgo = new ShadowAlgoParameter(ShadowFilter.PCF, true);

        [Tooltip("Shadow intensity.")] public ClampedFloatParameter intensity = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);

        #region Cascade Shadow

        public NoInterpMinFloatParameter maxShadowDistance = new NoInterpMinFloatParameter(150.0f, 0.0f);
        public NoInterpClampedIntParameter cascadeShadowSplitCount = new NoInterpClampedIntParameter(4, 1, 8);
        public CascadePartitionSplitParameter cascadeShadowSplit0 = new CascadePartitionSplitParameter(0.05f);
        public CascadePartitionSplitParameter cascadeShadowSplit1 = new CascadePartitionSplitParameter(0.1f);
        public CascadePartitionSplitParameter cascadeShadowSplit2 = new CascadePartitionSplitParameter(0.15f);
        public CascadePartitionSplitParameter cascadeShadowSplit3 = new CascadePartitionSplitParameter(0.2f);
        public CascadePartitionSplitParameter cascadeShadowSplit4 = new CascadePartitionSplitParameter(0.4f);
        public CascadePartitionSplitParameter cascadeShadowSplit5 = new CascadePartitionSplitParameter(0.6f);
        public CascadePartitionSplitParameter cascadeShadowSplit6 = new CascadePartitionSplitParameter(0.8f);

        public NoInterpMinFloatParameter cascadeBorder = new NoInterpMinFloatParameter(0.2f, 0.0f);

        #endregion


        #region PCSS

        [Tooltip("Penumbra controls shadows soften width.")]
        public ClampedFloatParameter penumbra = new ClampedFloatParameter(1.0f, 0.001f, 3.0f);

        [Tooltip("Shadow ramp texture.")] public NoInterpTextureParameter shadowRampTex = new NoInterpTextureParameter(s_DefaultShadowRampTex);

        [Tooltip("Shadow subsurface R channel.")]
        public ClampedFloatParameter scatterR = new ClampedFloatParameter(0.3f, 0.01f, 1.0f);

        [Tooltip("Shadow subsurface G channel.")]
        public ClampedFloatParameter scatterG = new ClampedFloatParameter(0.1f, 0.01f, 1.0f);

        [Tooltip("Shadow subsurface B channel.")]
        public ClampedFloatParameter scatterB = new ClampedFloatParameter(0.07f, 0.01f, 1.0f);

        [Tooltip("Penumbra controls shadows scatter occlusion soften width.")]
        public ClampedFloatParameter occlusionPenumbra = new ClampedFloatParameter(1.0f, 0.001f, 3.0f);


        [Tooltip("Shadow scatter mode.")] public ShadowScatterModeParameter shadowScatterMode = new ShadowScatterModeParameter(ShadowScatterMode.SubSurface);

        [Header("PerObjectShadow")] [Tooltip("Penumbra controls shadows soften width. (For Per Object Shadow)")]
        public ClampedFloatParameter perObjectShadowPenumbra = new ClampedFloatParameter(1.0f, 0.001f, 3.0f);

        #endregion
       


        /// <inheritdoc/>
        public bool IsActive() => enable.value; // Always enable screenSpaceShadows.


        #region Private

        protected override void OnEnable()
        {
            if (s_DefaultShadowRampTex == null)
            {
                var runtimeTextures = GraphicsSettings.GetRenderPipelineSettings<ShadowRuntimeResource>();
                s_DefaultShadowRampTex = runtimeTextures?.defaultDirShadowRampTex;
            }
            base.OnEnable();
        }


        private static Texture2D s_DefaultShadowRampTex;


        internal void InitNormalized(bool normalized)
        {
            cascadeShadowSplit0.normalized = normalized;
            cascadeShadowSplit1.normalized = normalized;
            cascadeShadowSplit2.normalized = normalized;
            cascadeShadowSplit3.normalized = normalized;
            cascadeShadowSplit4.normalized = normalized;
            cascadeShadowSplit5.normalized = normalized;
            cascadeShadowSplit6.normalized = normalized;
        }


        public Shadows()
        {
            cascadeShadowSplit0.Init(cascadeShadowSplitCount, 2, maxShadowDistance, null, cascadeShadowSplit1);
            cascadeShadowSplit1.Init(cascadeShadowSplitCount, 3, maxShadowDistance, cascadeShadowSplit0, cascadeShadowSplit2);
            cascadeShadowSplit2.Init(cascadeShadowSplitCount, 4, maxShadowDistance, cascadeShadowSplit1, cascadeShadowSplit3);
            cascadeShadowSplit3.Init(cascadeShadowSplitCount, 5, maxShadowDistance, cascadeShadowSplit2, cascadeShadowSplit4);
            cascadeShadowSplit4.Init(cascadeShadowSplitCount, 6, maxShadowDistance, cascadeShadowSplit3, cascadeShadowSplit5);
            cascadeShadowSplit5.Init(cascadeShadowSplitCount, 7, maxShadowDistance, cascadeShadowSplit4, cascadeShadowSplit6);
            cascadeShadowSplit6.Init(cascadeShadowSplitCount, 8, maxShadowDistance, cascadeShadowSplit5, null);
        }

        #endregion
    }
}