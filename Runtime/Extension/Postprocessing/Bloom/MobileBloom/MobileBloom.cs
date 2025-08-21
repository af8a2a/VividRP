using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    
    public enum BloomMode
    {
        None,
        URP,
        Moblie,
    }

    
    [Serializable]
    public sealed class BloomModeParameter : VolumeParameter<BloomMode>
    {
        public BloomModeParameter(BloomMode value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    public partial class MobileBloom : VolumeComponent
    {
        /// <summary>
        /// Set the level of brightness to filter out pixels under this level.
        /// This value is expressed in gamma-space.
        /// A value above 0 will disregard energy conservation rules.
        /// </summary>
        [Header("Bloom")] [Tooltip("Filters out pixels under this level of brightness. Value is in gamma-space.")]
        public MinFloatParameter threshold = new MinFloatParameter(0.7f, 0f);

        /// <summary>
        /// Controls the strength of the bloom filter.
        /// </summary>
        [Tooltip("Strength of the bloom filter.")]
        public MinFloatParameter intensity = new MinFloatParameter(0.75f, 0f);

        [Tooltip("lumRangeScale of the bloom filter. We need this to anti-flicker.")]
        public ClampedFloatParameter lumRangeScale = new ClampedFloatParameter(0.2f, 0f, 1f);

        [Tooltip("preFilterScale of the bloom filter.")]
        public ClampedFloatParameter preFilterScale = new ClampedFloatParameter(2.5f, 0f, 5.0f);

        /// <summary>
        /// Specifies the tint of the bloom filter.
        /// </summary>
        [Tooltip("Use the color picker to select a color for the Bloom effect to tint to.")]
        public ColorParameter tint = new ColorParameter(new Color(1f, 1f, 1f, 0f), false, true, true);

        [Tooltip("preFilterScale of the bloom filter.")]
        public Vector4Parameter blurCompositeWeight = new Vector4Parameter(new Vector4(0.3f, 0.3f, 0.26f, 0.15f));


        public BoolParameter enable = new BoolParameter(false);

        [Tooltip("Select a Bloom Mode.")]
        public BloomModeParameter mode = new BloomModeParameter(BloomMode.None);

    }
}