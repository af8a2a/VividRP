// using System;
// using UnityEngine.Serialization;
//
// namespace UnityEngine.Rendering.Universal
// {
//     public  sealed partial class GlobalIllumination
//     {
//         #region RayMarching
//
//         /// <summary>
//         /// The thickness of the depth buffer value used for the ray marching step
//         /// </summary>
//         [Tooltip("Controls the thickness of the depth buffer used for ray marching.")]
//         public ClampedFloatParameter depthBufferThickness = new ClampedFloatParameter(0.1f, 0.0f, 0.5f);
//
//         GlobalIllumination()
//         {
//             displayName = "Screen Space Global Illumination";
//         }
//
//         /// <summary>
//         /// Defines if the screen space global illumination should be evaluated at full resolution.
//         /// </summary>
//         public BoolParameter fullResolutionSS = new BoolParameter(true);
//
//         /// <summary>
//         /// The number of steps that should be used during the ray marching pass.
//         /// </summary>
//         public int maxRaySteps
//         {
//             get { return m_MaxRaySteps.value; }
//             set { m_MaxRaySteps.value = value; }
//         }
//
//         [SerializeField] [Tooltip("Controls the number of steps used for ray marching.")]
//         private ClampedIntParameter m_MaxRaySteps = new ClampedIntParameter(32, 0,128);
//
//         // Filtering
//         /// <summary>
//         /// Defines if the screen space global illumination should be denoised.
//         /// </summary>
//         public bool denoiseSS
//         {
//             get { return m_DenoiseSS.value; }
//             set { m_DenoiseSS.value = value; }
//         }
//
//         [SerializeField, FormerlySerializedAs("denoise")]
//         private BoolParameter m_DenoiseSS = new BoolParameter(true);
//
//         /// <summary>
//         /// Defines if the denoiser should be evaluated at half resolution.
//         /// </summary>
//         public bool halfResolutionDenoiserSS
//         {
//             get { return m_HalfResolutionDenoiserSS.value; }
//             set { m_HalfResolutionDenoiserSS.value = value; }
//         }
//
//         [SerializeField] [Tooltip("Use a half resolution denoiser.")]
//         private BoolParameter m_HalfResolutionDenoiserSS = new BoolParameter(false);
//
//         /// <summary>
//         /// Controls the radius of the global illumination denoiser (First Pass).
//         /// </summary>
//         public float denoiserRadiusSS
//         {
//             get { return m_DenoiserRadiusSS.value; }
//             set { m_DenoiserRadiusSS.value = value; }
//         }
//
//         [SerializeField] [Tooltip("Controls the radius of the GI denoiser (First Pass).")]
//         private ClampedFloatParameter m_DenoiserRadiusSS = new ClampedFloatParameter(0.6f, 0.001f, 1.0f);
//
//         /// <summary>
//         /// Defines if the second denoising pass should be enabled.
//         /// </summary>
//         public bool secondDenoiserPassSS
//         {
//             get { return m_SecondDenoiserPassSS.value; }
//             set { m_SecondDenoiserPassSS.value = value; }
//         }
//
//         [SerializeField] [Tooltip("Enable second denoising pass.")]
//         private BoolParameter m_SecondDenoiserPassSS = new BoolParameter(true);
//
//         #endregion
//     }
// }