using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.Bindless
{
    public sealed class NativeBindlessTextureDescriptorAllocator : IBindlessTextureDescriptorAllocator
    {
        private uint m_DescriptorHeapCount;
        private bool m_IsAvailable;
        private string m_UnavailableReason = "Bindless allocator has not been initialized.";

        public NativeBindlessTextureDescriptorAllocator()
        {
            Initialize();
        }

        public bool IsAvailable => m_IsAvailable;

        public uint DescriptorHeapCount => m_DescriptorHeapCount;

        public string UnavailableReason => m_UnavailableReason;

        public bool TryCreateTextureDescriptor(Texture texture, uint index)
        {
            if (!m_IsAvailable)
            {
                return false;
            }

            Texture effectiveTexture = texture != null ? texture : Texture2D.whiteTexture;
            if (effectiveTexture == null)
            {
                SetUnavailable("The fallback white texture is unavailable.");
                return false;
            }

            try
            {
                int result = BindlessPluginBindings.CreateSRVDescriptor(effectiveTexture.GetNativeTexturePtr(), index);
                if (result != 0)
                {
                    m_UnavailableReason = $"Bindless plugin returned error code {result} while creating a descriptor.";
                    return false;
                }

                m_UnavailableReason = string.Empty;
                return true;
            }
            catch (Exception exception) when (IsNativePluginException(exception))
            {
                SetUnavailable($"Bindless plugin invocation failed: {exception.GetType().Name}.");
                return false;
            }
        }

        private void Initialize()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                SetUnavailable("Bindless descriptors require the Direct3D12 graphics backend.");
                return;
            }

            try
            {
                m_DescriptorHeapCount = BindlessPluginBindings.GetSRVDescriptorHeapCount();
            }
            catch (Exception exception) when (IsNativePluginException(exception))
            {
                SetUnavailable($"Bindless plugin is unavailable: {exception.GetType().Name}.");
                return;
            }

            if (m_DescriptorHeapCount == 0)
            {
                SetUnavailable("Bindless plugin reported an empty SRV descriptor heap.");
                return;
            }

            m_IsAvailable = true;
            m_UnavailableReason = string.Empty;
        }

        private void SetUnavailable(string reason)
        {
            m_IsAvailable = false;
            m_DescriptorHeapCount = 0;
            m_UnavailableReason = reason;
        }

        private static bool IsNativePluginException(Exception exception)
        {
            return exception is DllNotFoundException
                || exception is EntryPointNotFoundException
                || exception is BadImageFormatException;
        }
    }
}
