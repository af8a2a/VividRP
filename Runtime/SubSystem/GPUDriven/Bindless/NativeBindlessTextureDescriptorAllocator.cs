using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.Bindless
{
    public sealed class NativeBindlessTextureDescriptorAllocator : IBindlessTextureDescriptorAllocator
    {
        private readonly Func<Texture, uint, bool> m_CreateTextureDescriptor;
        private readonly Func<uint> m_GetDescriptorHeapCount;
        private readonly Func<uint> m_GetDescriptorStartIndex;
        private readonly Func<uint> m_GetDescriptorCapacity;
        private readonly Func<ulong> m_GetCompletedFrameFenceValue;
        private readonly Func<ulong> m_GetPendingFrameFenceValue;
        private readonly Func<GraphicsDeviceType> m_GetGraphicsDeviceType;
        private readonly Func<Texture> m_GetWhiteTexture;
        private uint m_DescriptorHeapCount;
        private uint m_DescriptorStartIndex;
        private uint m_DescriptorCapacity;
        private uint m_CreateSRVDescriptorCallCountThisFrame;
        private bool m_IsAvailable;
        private bool m_IsPermanentlyUnavailable;
        private string m_UnavailableReason = "Bindless allocator has not been initialized.";

        public NativeBindlessTextureDescriptorAllocator()
            : this(
                BindlessPluginBindings.GetSRVDescriptorHeapCount,
                BindlessPluginBindings.GetBindlessDescriptorStartIndex,
                BindlessPluginBindings.GetBindlessDescriptorCount,
                BindlessPluginBindings.GetCompletedFrameFenceValue,
                BindlessPluginBindings.GetPendingFrameFenceValue,
                static (texture, index) => BindlessPluginBindings.CreateSRVDescriptor(texture.GetNativeTexturePtr(), index),
                static () => SystemInfo.graphicsDeviceType,
                static () => Texture2D.whiteTexture)
        {
        }

        internal NativeBindlessTextureDescriptorAllocator(
            Func<uint> getDescriptorHeapCount,
            Func<uint> getDescriptorStartIndex,
            Func<uint> getDescriptorCapacity,
            Func<ulong> getCompletedFrameFenceValue,
            Func<ulong> getPendingFrameFenceValue,
            Func<Texture, uint, bool> createTextureDescriptor,
            Func<GraphicsDeviceType> getGraphicsDeviceType,
            Func<Texture> getWhiteTexture)
        {
            BindlessPluginBindings.EnsureLoaded();
            m_GetDescriptorHeapCount = getDescriptorHeapCount ?? throw new ArgumentNullException(nameof(getDescriptorHeapCount));
            m_GetDescriptorStartIndex = getDescriptorStartIndex ?? throw new ArgumentNullException(nameof(getDescriptorStartIndex));
            m_GetDescriptorCapacity = getDescriptorCapacity ?? throw new ArgumentNullException(nameof(getDescriptorCapacity));
            m_GetCompletedFrameFenceValue = getCompletedFrameFenceValue ?? throw new ArgumentNullException(nameof(getCompletedFrameFenceValue));
            m_GetPendingFrameFenceValue = getPendingFrameFenceValue ?? throw new ArgumentNullException(nameof(getPendingFrameFenceValue));
            m_CreateTextureDescriptor = createTextureDescriptor ?? throw new ArgumentNullException(nameof(createTextureDescriptor));
            m_GetGraphicsDeviceType = getGraphicsDeviceType ?? throw new ArgumentNullException(nameof(getGraphicsDeviceType));
            m_GetWhiteTexture = getWhiteTexture ?? throw new ArgumentNullException(nameof(getWhiteTexture));

            TryInitializeIfNeeded();
        }

        public bool IsAvailable
        {
            get
            {
                TryInitializeIfNeeded();
                return m_IsAvailable;
            }
        }

        public uint DescriptorHeapCount
        {
            get
            {
                TryInitializeIfNeeded();
                return m_DescriptorHeapCount;
            }
        }

        public uint DescriptorStartIndex
        {
            get
            {
                TryInitializeIfNeeded();
                return m_DescriptorStartIndex;
            }
        }

        public uint DescriptorCapacity
        {
            get
            {
                TryInitializeIfNeeded();
                return m_DescriptorCapacity;
            }
        }

        public ulong CompletedFrameFenceValue => GetFrameFenceValue(m_GetCompletedFrameFenceValue);

        public ulong PendingFrameFenceValue => GetFrameFenceValue(m_GetPendingFrameFenceValue);

        public string UnavailableReason
        {
            get
            {
                TryInitializeIfNeeded();
                return m_UnavailableReason;
            }
        }

        public uint CreateSRVDescriptorCallCountThisFrame => m_CreateSRVDescriptorCallCountThisFrame;

        public void ResetPerFrameStats()
        {
            m_CreateSRVDescriptorCallCountThisFrame = 0;
        }

        public bool TryCreateTextureDescriptor(Texture texture, uint index)
        {
            if (!TryInitializeIfNeeded())
            {
                return false;
            }

            Texture effectiveTexture = texture != null ? texture : m_GetWhiteTexture();
            if (effectiveTexture == null)
            {
                SetPermanentlyUnavailable("The fallback white texture is unavailable.");
                return false;
            }

            try
            {
                m_CreateSRVDescriptorCallCountThisFrame++;
                if (!m_CreateTextureDescriptor(effectiveTexture, index))
                {
                    m_UnavailableReason = "Bindless plugin failed to create a descriptor.";
                    return false;
                }

                m_UnavailableReason = string.Empty;
                return true;
            }
            catch (Exception exception) when (IsNativePluginException(exception))
            {
                SetPermanentlyUnavailable($"Bindless plugin invocation failed: {exception.GetType().Name}.");
                return false;
            }
        }

        private ulong GetFrameFenceValue(Func<ulong> getFrameFenceValue)
        {
            if (!TryInitializeIfNeeded())
            {
                return 0ul;
            }

            try
            {
                return getFrameFenceValue();
            }
            catch (Exception exception) when (IsNativePluginException(exception))
            {
                SetPermanentlyUnavailable($"Bindless plugin invocation failed: {exception.GetType().Name}.");
                return 0ul;
            }
        }

        private bool TryInitializeIfNeeded()
        {
            if (m_IsAvailable && m_DescriptorCapacity > 0)
            {
                return true;
            }

            if (m_IsPermanentlyUnavailable)
            {
                return false;
            }

            return Initialize();
        }

        private bool Initialize()
        {
            if (m_GetGraphicsDeviceType() != GraphicsDeviceType.Direct3D12)
            {
                SetPermanentlyUnavailable("Bindless descriptors require the Direct3D12 graphics backend.");
                return false;
            }

            try
            {
                BindlessPluginBindings.EnsureLoaded();
                m_DescriptorHeapCount = m_GetDescriptorHeapCount();
                m_DescriptorStartIndex = m_GetDescriptorStartIndex();
                m_DescriptorCapacity = m_GetDescriptorCapacity();
            }
            catch (Exception exception) when (IsNativePluginException(exception))
            {
                SetPermanentlyUnavailable($"Bindless plugin is unavailable: {exception.GetType().Name}.");
                return false;
            }

            if (m_DescriptorHeapCount == 0)
            {
                SetTemporarilyUnavailable("Bindless descriptor heap has not been captured yet.");
                return false;
            }

            if (m_DescriptorCapacity == 0)
            {
                SetTemporarilyUnavailable(
                    "Bindless descriptor heap was captured without a plugin-owned range. " +
                    "In the Unity Editor on Windows this usually means UnityBindless.dll loaded too late. " +
                    "Run Packages/VividRP/Setup-Bindless.ps1 to install Assets/Plugins/VividRP/x86_64/UnityBindless.dll, then restart the editor.");
                return false;
            }

            if ((ulong) m_DescriptorStartIndex + m_DescriptorCapacity > m_DescriptorHeapCount)
            {
                SetPermanentlyUnavailable("Bindless plugin returned an invalid descriptor range.");
                return false;
            }

            m_IsAvailable = true;
            m_UnavailableReason = string.Empty;
            return true;
        }

        private void SetTemporarilyUnavailable(string reason)
        {
            m_IsAvailable = false;
            m_DescriptorHeapCount = 0;
            m_DescriptorStartIndex = 0;
            m_DescriptorCapacity = 0;
            m_UnavailableReason = reason;
        }

        private void SetPermanentlyUnavailable(string reason)
        {
            m_IsPermanentlyUnavailable = true;
            SetTemporarilyUnavailable(reason);
        }

        private static bool IsNativePluginException(Exception exception)
        {
            return exception is DllNotFoundException
                || exception is EntryPointNotFoundException
                || exception is BadImageFormatException;
        }
    }
}
