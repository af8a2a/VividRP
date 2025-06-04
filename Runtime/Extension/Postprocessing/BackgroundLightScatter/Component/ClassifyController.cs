using System;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    [ExecuteInEditMode]
    public class ClassifyController : MonoBehaviour
    {
        MaterialPropertyBlock materialPropertyBlock;

        Renderer[] renderers;

        private void OnEnable()
        {
            materialPropertyBlock = new MaterialPropertyBlock();
            renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.GetPropertyBlock(materialPropertyBlock);
                materialPropertyBlock.SetFloat("_Enable", 1);
                renderer.SetPropertyBlock(materialPropertyBlock);

            }
        }


        private void OnDisable()
        {
            foreach (var renderer in renderers)
            {
                renderer.GetPropertyBlock(materialPropertyBlock);
                materialPropertyBlock.SetFloat("_Enable", 0);
                renderer.SetPropertyBlock(materialPropertyBlock);
            }

            materialPropertyBlock = null;
            renderers = null;
        }
    }
}