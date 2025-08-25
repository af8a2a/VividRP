using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Sky/GradientSky Sky")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [SkyUniqueID((int)SkyType.Gradient)]
    public sealed class GradientSkySetting : SkySettings
    {
        /// <summary>Top color of the gradient sky.</summary>
        [Tooltip("Specifies the color of the upper hemisphere of the sky.")]
        public ColorParameter top = new ColorParameter(new Color(0.747f, 0.0f, 1.0f), true, false, true);
        /// <summary>Middle color of the gradient sky.</summary>
        [Tooltip("Specifies the color at the horizon.")]
        public ColorParameter middle = new ColorParameter(new Color(1.0f, 0.667f, 0.676f), true, false, true);
        /// <summary>Bottom color of the gradient sky.</summary>
        [Tooltip("Specifies the color of the lower hemisphere of the sky. This is below the horizon.")]
        public ColorParameter bottom = new ColorParameter(Color.white, true, false, true);
        /// <summary>Size of the horizon (middle color.</summary>
        [Tooltip("Sets the size of the horizon (Middle color).")]
        public MinFloatParameter gradientDiffusion = new MinFloatParameter(1, 0.0f);

        /// <summary>
        /// Returns the hash code of the gradient sky parameters.
        /// </summary>
        /// <returns>The hash code of the gradient sky parameters.</returns>
        public override int GetHashCode()
        {
            int hash = base.GetHashCode();

            unchecked
            {
#if UNITY_2019_3 // In 2019.3, when we call GetHashCode on a VolumeParameter it generate garbage (due to the boxing of the generic parameter)
                hash = hash * 23 + bottom.value.GetHashCode();
                hash = hash * 23 + top.value.GetHashCode();
                hash = hash * 23 + middle.value.GetHashCode();
                hash = hash * 23 + gradientDiffusion.value.GetHashCode();

                hash = hash * 23 + bottom.overrideState.GetHashCode();
                hash = hash * 23 + top.overrideState.GetHashCode();
                hash = hash * 23 + middle.overrideState.GetHashCode();
                hash = hash * 23 + gradientDiffusion.overrideState.GetHashCode();
#else
                hash = hash * 23 + bottom.GetHashCode();
                hash = hash * 23 + top.GetHashCode();
                hash = hash * 23 + middle.GetHashCode();
                hash = hash * 23 + gradientDiffusion.GetHashCode();
#endif
            }

            return hash;
        }

        /// <summary>
        /// Returns GradientSky Renderer type.
        /// </summary>
        /// <returns></returns>
        public override Type GetSkyRendererType()
        {
            return typeof(GradientSkyRenderer);
        }
    }
}