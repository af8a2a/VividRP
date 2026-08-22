using System;
using UnityEngine;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;

namespace VividRP.Runtime
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class VirtualTextureDemoController : MonoBehaviour
    {
        [SerializeField]
        private MeshletRenderer m_MeshletRenderer;

        [SerializeField]
        private GPUDrivenMaterialProxy m_MaterialProxy;

        [SerializeField]
        private VividVirtualTextureAsset m_VirtualTextureAsset;

        [SerializeField, Min(0)]
        private int m_SubMeshIndex;

        [NonSerialized]
        private string m_LastValidationMessage;

        // Kept for source compatibility with the former standalone-space demo.
        [Obsolete("VirtualTextureDemo no longer owns an address space. Use the GPUDriven VT allocation instead.")]
        public int SpaceId => 0;

        private void Reset()
        {
            m_MeshletRenderer = GetComponent<MeshletRenderer>();
            ResolveExistingBindings();
        }

        private void OnEnable()
        {
            ValidateConfiguration();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
                return;

            ValidateConfiguration();
        }

        public bool TryValidateVisibilityBufferDemo(out string validationMessage)
        {
            if (m_MeshletRenderer == null)
                m_MeshletRenderer = GetComponent<MeshletRenderer>();
            if (m_MeshletRenderer == null)
            {
                validationMessage =
                    "Assign a MeshletRenderer. VirtualTextureDemo now uses the GPUDriven VisibilityBuffer path and no longer creates a MeshRenderer surface.";
                return false;
            }

            if (!m_MeshletRenderer.takeOverSourceRenderer)
            {
                validationMessage =
                    "The MeshletRenderer must use the Take Over Source Renderer workflow before it can serve as the VT demo.";
                return false;
            }

            if (m_MeshletRenderer.TryGetComponent(out Renderer sourceRenderer) && sourceRenderer.enabled)
            {
                validationMessage =
                    $"Disable or remove the source {sourceRenderer.GetType().Name}. The VT demo must render only through the GPUDriven VisibilityBuffer path.";
                return false;
            }

            int subMeshCount = m_MeshletRenderer.subMeshCount;
            if (subMeshCount <= 0)
            {
                validationMessage = "The assigned MeshletRenderer has no captured source mesh.";
                return false;
            }

            if (m_SubMeshIndex < 0 || m_SubMeshIndex >= subMeshCount)
            {
                validationMessage =
                    $"Submesh index {m_SubMeshIndex} is outside the MeshletRenderer range [0, {subMeshCount - 1}].";
                return false;
            }

            GPUDrivenMaterialProxy boundMaterialProxy = m_MeshletRenderer.GetMaterialProxy(m_SubMeshIndex);
            if (m_MaterialProxy == null)
                m_MaterialProxy = boundMaterialProxy;
            if (m_MaterialProxy == null)
            {
                validationMessage =
                    "Assign a GPUDrivenMaterialProxy. The VisibilityBuffer path consumes material proxies instead of the legacy VirtualTextureDemo material.";
                return false;
            }

            if (boundMaterialProxy != m_MaterialProxy)
            {
                validationMessage =
                    $"Bind the assigned GPUDrivenMaterialProxy to MeshletRenderer submesh {m_SubMeshIndex}.";
                return false;
            }

            if (m_VirtualTextureAsset == null)
                m_VirtualTextureAsset = m_MaterialProxy.StreamedVirtualTexture;
            if (m_MaterialProxy.StreamedVirtualTexture != m_VirtualTextureAsset)
            {
                validationMessage =
                    "Build and bind the assigned streamed VT asset through the GPUDriven Material Proxy editor.";
                return false;
            }

            if (!VirtualTextureGPUDrivenTextureBackend.IsCompatibleStreamedAsset(
                    m_VirtualTextureAsset,
                    VirtualTextureGPUDrivenTextureBackend.ResolveActivePhysicalPoolQuality(),
                    out validationMessage))
            {
                return false;
            }

            if (!m_MeshletRenderer.TryValidate(out string rendererValidationMessage))
            {
                validationMessage = $"MeshletRenderer is not ready for VisibilityBuffer rendering: {rendererValidationMessage}";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        private void ValidateConfiguration()
        {
            if (TryValidateVisibilityBufferDemo(out string validationMessage)
                && TryValidateActivePipeline(out validationMessage))
            {
                m_LastValidationMessage = string.Empty;
                return;
            }

            if (string.Equals(m_LastValidationMessage, validationMessage, StringComparison.Ordinal))
                return;

            m_LastValidationMessage = validationMessage;
            Debug.LogWarning($"[VividRP] VirtualTextureDemo configuration is incomplete: {validationMessage}", this);
        }

        private static bool TryValidateActivePipeline(out string validationMessage)
        {
            VividRenderPipelineAsset pipelineAsset = VividRenderPipelineAsset.GetActiveAsset();
            if (pipelineAsset == null)
            {
                validationMessage = string.Empty;
                return true;
            }

            if (!pipelineAsset.EnableGPUDriven)
            {
                validationMessage = "Enable GPUDriven rendering on the active Vivid render pipeline asset.";
                return false;
            }

            if (pipelineAsset.GPUDrivenTextureBackend != GPUDrivenTextureBackendMode.VirtualTexture)
            {
                validationMessage =
                    "Select the VirtualTexture GPUDriven texture backend on the active Vivid render pipeline asset.";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        private void ResolveExistingBindings()
        {
            if (m_MeshletRenderer == null || m_MeshletRenderer.subMeshCount <= 0)
                return;

            int subMeshIndex = Mathf.Clamp(m_SubMeshIndex, 0, m_MeshletRenderer.subMeshCount - 1);
            if (m_MaterialProxy == null)
                m_MaterialProxy = m_MeshletRenderer.GetMaterialProxy(subMeshIndex);
            if (m_VirtualTextureAsset == null && m_MaterialProxy != null)
                m_VirtualTextureAsset = m_MaterialProxy.StreamedVirtualTexture;
        }
    }
}
