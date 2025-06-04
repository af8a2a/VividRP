// using System;
// using UnityEngine;
// using UnityEngine.Rendering;
// using UnityEngine.Rendering.Universal;
//
// namespace Features.Sky.ProceduralToonSky
// {
//     [Serializable, VolumeComponentMenu("Sky/ProceduralToon Sky")]
//     [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
//     [SkyUniqueID((int)SkyType.ProceduralToon)]
//     public sealed class ProceduralToonSkySetting : SkySettings
//     {
//         internal static Material defaultMaterial { get; set; }
//         private static Material s_SkyMaterial;
//         
//         protected override void OnEnable()
//         {
//             base.OnEnable();
//         
//             if (s_SkyMaterial == null)
//             {
//                 s_SkyMaterial = defaultMaterial;
//             }
//         }
//         
//         
//         public MaterialParameter material = new MaterialParameter(s_SkyMaterial);
//
//         /// <summary>
//         /// Returns the hash code of the Procedural Toon sky parameters.
//         /// </summary>
//         /// <returns>The hash code of the Procedural Toon sky parameters.</returns>
//         public override int GetHashCode()
//         {
//             int hash = base.GetHashCode();
//
//             unchecked
//             {
//                 //hash = hash * 23 + material.GetHashCode();
//             }
//
//             return hash;
//         }
//
//         /// <summary>
//         /// Returns ProceduralToonSky Renderer type.
//         /// </summary>
//         /// <returns></returns>
//         public override Type GetSkyRendererType()
//         {
//             return typeof(ProceduralToonSkyRenderer);
//         }
//     }
// }