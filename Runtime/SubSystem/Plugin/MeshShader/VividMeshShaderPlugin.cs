using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.MeshShader
{
    internal enum VividMeshShaderSupportStatus : uint
    {
        Unknown = 0,
        Supported = 1,
        NotDirect3D12 = 2,
        NoDevice = 3,
        ShaderModel65Unavailable = 4,
        MeshShaderUnavailable = 5,
        UnityD3D12InterfaceUnavailable = 6,
    }

    internal enum VividMeshShaderCullMode : uint
    {
        None = 1,
        Front = 2,
        Back = 3,
    }

    internal enum VividMeshShaderCompareFunction : uint
    {
        LessEqual = 4,
        GreaterEqual = 7,
    }

    internal readonly struct VividMeshShaderRenderState
    {
        internal VividMeshShaderRenderState(
            VividMeshShaderCullMode cullMode,
            VividMeshShaderCompareFunction depthCompare)
        {
            CullMode = cullMode;
            DepthCompare = depthCompare;
        }

        internal VividMeshShaderCullMode CullMode { get; }
        internal VividMeshShaderCompareFunction DepthCompare { get; }
    }

    /// <summary>
    /// Immutable raw-HLSL shader object owned by the native mesh-shader plugin.
    /// Frame resources are deliberately kept out of this object.
    /// </summary>
    internal sealed class VividMeshShaderObject : IDisposable
    {
        private ulong m_NativeHandle;

        private VividMeshShaderObject(
            string source,
            VividMeshShaderRenderState renderState,
            ulong nativeHandle)
        {
            Source = source;
            RenderState = renderState;
            m_NativeHandle = nativeHandle;
        }

        internal string Source { get; }
        internal VividMeshShaderRenderState RenderState { get; }
        internal ulong NativeHandle => m_NativeHandle;
        internal bool IsValid => m_NativeHandle != 0;

        internal static bool TryCreate(
            string source,
            in VividMeshShaderRenderState renderState,
            out VividMeshShaderObject shaderObject,
            out string error)
        {
            shaderObject = null;
            error = null;
            if (string.IsNullOrWhiteSpace(source))
            {
                error = "Mesh shader HLSL source is empty.";
                return false;
            }

            var desc = new VividMeshShaderPlugin.NativeShaderObjectDesc
            {
                StructSize = (uint)Marshal.SizeOf<VividMeshShaderPlugin.NativeShaderObjectDesc>(),
                AbiVersion = VividMeshShaderPlugin.AbiVersion,
                SourceUtf8 = source,
                SourceLength = (uint)Encoding.UTF8.GetByteCount(source),
                AmplificationEntryUtf8 = "AmplificationMain",
                MeshEntryUtf8 = "MeshMain",
                PixelEntryUtf8 = "PixelMain",
                RenderState = VividMeshShaderPlugin.CreateNativeRenderState(renderState),
            };

            try
            {
                ulong handle = VividMeshShaderPlugin.CreateShaderObject(ref desc);
                if (handle == 0)
                {
                    error = VividMeshShaderPlugin.GetLastErrorMessage(
                        "Native mesh ShaderObject creation failed.");
                    return false;
                }

                shaderObject = new VividMeshShaderObject(source, renderState, handle);
                return true;
            }
            catch (Exception exception) when (VividMeshShaderPlugin.IsPluginLoadException(exception))
            {
                error = exception.Message;
                return false;
            }
        }

        public void Dispose()
        {
            ulong handle = m_NativeHandle;
            m_NativeHandle = 0;
            if (handle == 0)
                return;

            try
            {
                VividMeshShaderPlugin.DestroyShaderObject(handle);
            }
            catch (Exception exception) when (VividMeshShaderPlugin.IsPluginLoadException(exception))
            {
                // The native library may already be unloaded during domain or player shutdown.
            }
        }
    }

    internal static class VividMeshShaderPlugin
    {
        internal const uint AbiVersion = 1;

        private const string NativeLibrary = "VividMeshShader";
        private const uint DxgiFormatR16G16B16A16Float = 10;
        private const uint DxgiFormatR32G32Uint = 17;
        private const uint DxgiFormatR16G16Float = 34;
        private const uint DxgiFormatD32Float = 40;
        private const int NativeRenderStateDescSize = 52;
        private const int NativeShaderObjectDescSize = 104;
        private const int NativeDispatchDescSize = 136;

        private static IntPtr s_RenderEvent;
        private static int s_DispatchEventId = -1;
        private static ulong s_DispatchFailureBaseline;

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRenderStateDesc
        {
            internal uint CullMode;
            internal uint FrontCounterClockwise;
            internal uint DepthEnable;
            internal uint DepthWrite;
            internal uint DepthCompare;
            internal uint RenderTargetCount;
            internal uint RenderTargetFormat0;
            internal uint RenderTargetFormat1;
            internal uint RenderTargetFormat2;
            internal uint RenderTargetFormat3;
            internal uint DepthStencilFormat;
            internal uint SampleCount;
            internal uint SampleQuality;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        internal struct NativeShaderObjectDesc
        {
            internal uint StructSize;
            internal uint AbiVersion;

            [MarshalAs(UnmanagedType.LPUTF8Str)]
            internal string SourceUtf8;

            internal uint SourceLength;
            internal uint Reserved0;

            [MarshalAs(UnmanagedType.LPUTF8Str)]
            internal string AmplificationEntryUtf8;

            [MarshalAs(UnmanagedType.LPUTF8Str)]
            internal string MeshEntryUtf8;

            [MarshalAs(UnmanagedType.LPUTF8Str)]
            internal string PixelEntryUtf8;

            internal NativeRenderStateDesc RenderState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMatrix4x4
        {
            internal float M00;
            internal float M10;
            internal float M20;
            internal float M30;
            internal float M01;
            internal float M11;
            internal float M21;
            internal float M31;
            internal float M02;
            internal float M12;
            internal float M22;
            internal float M32;
            internal float M03;
            internal float M13;
            internal float M23;
            internal float M33;

            internal static NativeMatrix4x4 FromUnityMatrix(in Matrix4x4 matrix)
            {
                return new NativeMatrix4x4
                {
                    M00 = matrix.m00,
                    M10 = matrix.m10,
                    M20 = matrix.m20,
                    M30 = matrix.m30,
                    M01 = matrix.m01,
                    M11 = matrix.m11,
                    M21 = matrix.m21,
                    M31 = matrix.m31,
                    M02 = matrix.m02,
                    M12 = matrix.m12,
                    M22 = matrix.m22,
                    M32 = matrix.m32,
                    M03 = matrix.m03,
                    M13 = matrix.m13,
                    M23 = matrix.m23,
                    M33 = matrix.m33,
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeDispatchDesc
        {
            internal uint StructSize;
            internal uint AbiVersion;
            internal ulong ShaderHandle;
            internal IntPtr VisibleRequests;
            internal IntPtr IndirectArgs;
            internal IntPtr Instances;
            internal IntPtr Meshlets;
            internal IntPtr Vertices;
            internal IntPtr Indices;
            internal uint RendererListIndex;
            internal uint MaxRequestCount;
            internal NativeMatrix4x4 ViewProjectionColumnMajor;
        }

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_GetAbiVersion")]
        private static extern uint GetAbiVersion();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_GetSupportStatus")]
        private static extern VividMeshShaderSupportStatus GetSupportStatus();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_GetDispatchFailureCount")]
        private static extern ulong GetDispatchFailureCount();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_GetLastError")]
        private static extern IntPtr GetLastError();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_CreateShaderObject")]
        internal static extern ulong CreateShaderObject(ref NativeShaderObjectDesc desc);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_DestroyShaderObject")]
        internal static extern void DestroyShaderObject(ulong handle);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_CreateDispatchRequest")]
        private static extern IntPtr CreateDispatchRequest(ref NativeDispatchDesc desc);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_DestroyDispatchRequest")]
        private static extern void DestroyDispatchRequest(IntPtr request);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_GetRenderEventFunc")]
        private static extern IntPtr GetRenderEventFunc();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_GetDispatchEventId")]
        private static extern int GetDispatchEventId();

        internal static NativeRenderStateDesc CreateNativeRenderState(
            in VividMeshShaderRenderState renderState)
        {
            return new NativeRenderStateDesc
            {
                CullMode = (uint)renderState.CullMode,
                FrontCounterClockwise = 0,
                DepthEnable = 1,
                DepthWrite = 1,
                DepthCompare = (uint)renderState.DepthCompare,
                RenderTargetCount = 4,
                RenderTargetFormat0 = DxgiFormatR32G32Uint,
                RenderTargetFormat1 = DxgiFormatR16G16B16A16Float,
                RenderTargetFormat2 = DxgiFormatR16G16B16A16Float,
                RenderTargetFormat3 = DxgiFormatR16G16Float,
                DepthStencilFormat = DxgiFormatD32Float,
                SampleCount = 1,
                SampleQuality = 0,
            };
        }

        internal static bool TryGetSupport(
            out VividMeshShaderSupportStatus supportStatus,
            out string error)
        {
            supportStatus = VividMeshShaderSupportStatus.Unknown;
            error = null;
            s_RenderEvent = IntPtr.Zero;
            s_DispatchEventId = -1;
            s_DispatchFailureBaseline = 0;

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                supportStatus = VividMeshShaderSupportStatus.NotDirect3D12;
                error = "The experimental mesh-shader path requires Direct3D 12.";
                return false;
            }

            if (!HasExpectedAbiLayout())
            {
                error = "The managed VividMeshShader ABI layout is incompatible with the x64 native plugin.";
                return false;
            }

            try
            {
                uint nativeAbiVersion = GetAbiVersion();
                if (nativeAbiVersion != AbiVersion)
                {
                    error = $"VividMeshShader ABI mismatch: managed={AbiVersion}, native={nativeAbiVersion}.";
                    return false;
                }

                supportStatus = GetSupportStatus();
                if (supportStatus != VividMeshShaderSupportStatus.Supported)
                {
                    error = $"Native mesh-shader support status is {supportStatus}.";
                    return false;
                }

                s_RenderEvent = GetRenderEventFunc();
                s_DispatchEventId = GetDispatchEventId();
                if (s_RenderEvent == IntPtr.Zero || s_DispatchEventId < 0)
                {
                    error = "The native mesh-shader render event is unavailable.";
                    return false;
                }

                s_DispatchFailureBaseline = GetDispatchFailureCount();
                return true;
            }
            catch (Exception exception) when (IsPluginLoadException(exception))
            {
                error = exception.Message;
                return false;
            }
        }

        internal static bool TryQueueDispatch(
            CommandBuffer cmd,
            VividMeshShaderObject shaderObject,
            GraphicsBuffer visibleRequests,
            GraphicsBuffer indirectArgs,
            GraphicsBuffer instances,
            GraphicsBuffer meshlets,
            GraphicsBuffer vertices,
            GraphicsBuffer indices,
            uint rendererListIndex,
            uint maxRequestCount,
            in Matrix4x4 viewProjection,
            out string error)
        {
            error = null;
            if (cmd == null
                || shaderObject?.IsValid != true
                || visibleRequests == null
                || indirectArgs == null
                || instances == null
                || meshlets == null
                || vertices == null
                || indices == null
                || maxRequestCount == 0)
            {
                error = "Mesh-shader dispatch resources are incomplete.";
                return false;
            }

            IntPtr request = IntPtr.Zero;
            try
            {
                if (GetDispatchFailureCount() != s_DispatchFailureBaseline)
                {
                    error = GetLastErrorMessage(
                        "A previously queued native mesh-shader dispatch failed.");
                    return false;
                }

                var desc = new NativeDispatchDesc
                {
                    StructSize = (uint)Marshal.SizeOf<NativeDispatchDesc>(),
                    AbiVersion = AbiVersion,
                    ShaderHandle = shaderObject.NativeHandle,
                    VisibleRequests = visibleRequests.GetNativeBufferPtr(),
                    IndirectArgs = indirectArgs.GetNativeBufferPtr(),
                    Instances = instances.GetNativeBufferPtr(),
                    Meshlets = meshlets.GetNativeBufferPtr(),
                    Vertices = vertices.GetNativeBufferPtr(),
                    Indices = indices.GetNativeBufferPtr(),
                    RendererListIndex = rendererListIndex,
                    MaxRequestCount = maxRequestCount,
                    ViewProjectionColumnMajor = NativeMatrix4x4.FromUnityMatrix(viewProjection),
                };

                request = CreateDispatchRequest(ref desc);
                if (request == IntPtr.Zero)
                {
                    error = GetLastErrorMessage("Could not allocate a native mesh-shader dispatch request.");
                    return false;
                }

                cmd.IssuePluginEventAndData(s_RenderEvent, s_DispatchEventId, request);
                request = IntPtr.Zero;
                return true;
            }
            catch (Exception exception) when (IsPluginLoadException(exception))
            {
                error = exception.Message;
                return false;
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                              or ArgumentException
                                              or UnityException)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (request != IntPtr.Zero)
                {
                    try
                    {
                        DestroyDispatchRequest(request);
                    }
                    catch (Exception exception) when (IsPluginLoadException(exception))
                    {
                    }
                }
            }
        }

        private static bool HasExpectedAbiLayout()
        {
            return Marshal.SizeOf<NativeRenderStateDesc>() == NativeRenderStateDescSize
                   && Marshal.SizeOf<NativeShaderObjectDesc>() == NativeShaderObjectDescSize
                   && Marshal.OffsetOf<NativeShaderObjectDesc>(nameof(NativeShaderObjectDesc.SourceUtf8)).ToInt64() == 8
                   && Marshal.OffsetOf<NativeShaderObjectDesc>(nameof(NativeShaderObjectDesc.RenderState)).ToInt64() == 48
                   && Marshal.SizeOf<NativeDispatchDesc>() == NativeDispatchDescSize
                   && Marshal.OffsetOf<NativeDispatchDesc>(nameof(NativeDispatchDesc.ShaderHandle)).ToInt64() == 8
                   && Marshal.OffsetOf<NativeDispatchDesc>(nameof(NativeDispatchDesc.VisibleRequests)).ToInt64() == 16
                   && Marshal.OffsetOf<NativeDispatchDesc>(nameof(NativeDispatchDesc.RendererListIndex)).ToInt64() == 64
                   && Marshal.OffsetOf<NativeDispatchDesc>(nameof(NativeDispatchDesc.ViewProjectionColumnMajor)).ToInt64() == 72;
        }

        internal static string GetLastErrorMessage(string fallback)
        {
            try
            {
                IntPtr error = GetLastError();
                return error != IntPtr.Zero
                    ? Marshal.PtrToStringUTF8(error) ?? fallback
                    : fallback;
            }
            catch (Exception exception) when (IsPluginLoadException(exception))
            {
                return fallback;
            }
        }

        internal static bool IsPluginLoadException(Exception exception)
        {
            return exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException
                or MarshalDirectiveException;
        }
    }
}
