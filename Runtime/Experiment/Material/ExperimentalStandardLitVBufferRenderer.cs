using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.Experimental.Material
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class ExperimentalStandardLitVBufferRenderer : MonoBehaviour
    {
        private readonly List<MeshRenderer> m_RegisteredRenderers = new();
        private readonly List<MeshRenderer> m_ScannedRenderers = new();

        private void OnEnable()
        {
            RefreshRenderers();
        }

        private void OnDisable()
        {
            UnregisterAll();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
                return;

            RefreshRenderers();
            ExperimentalStandardLitVBufferMaterialRegistry.MarkDirty();
        }

        private void OnTransformChildrenChanged()
        {
            if (isActiveAndEnabled)
                RefreshRenderers();
        }

        private void RefreshRenderers()
        {
            m_ScannedRenderers.Clear();
            GetComponentsInChildren(includeInactive: true, m_ScannedRenderers);

            for (int index = m_RegisteredRenderers.Count - 1; index >= 0; index--)
            {
                MeshRenderer renderer = m_RegisteredRenderers[index];
                if (m_ScannedRenderers.Contains(renderer))
                    continue;

                ExperimentalStandardLitVBufferMaterialRegistry.Unregister(renderer);
                m_RegisteredRenderers.RemoveAt(index);
            }

            for (int index = 0; index < m_ScannedRenderers.Count; index++)
            {
                MeshRenderer renderer = m_ScannedRenderers[index];
                if (renderer == null || m_RegisteredRenderers.Contains(renderer))
                    continue;

                ExperimentalStandardLitVBufferMaterialRegistry.Register(renderer);
                m_RegisteredRenderers.Add(renderer);
            }
        }

        private void UnregisterAll()
        {
            for (int index = 0; index < m_RegisteredRenderers.Count; index++)
            {
                ExperimentalStandardLitVBufferMaterialRegistry.Unregister(
                    m_RegisteredRenderers[index]);
            }

            m_RegisteredRenderers.Clear();
            m_ScannedRenderers.Clear();
        }
    }
}
