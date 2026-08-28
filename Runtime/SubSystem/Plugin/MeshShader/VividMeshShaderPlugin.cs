using System;
using System.Runtime.InteropServices;
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

    internal readonly struct VividMeshShaderDispatch
    {
        internal VividMeshShaderDispatch(
            VividMeshShaderObject shaderObject,
            uint rendererListIndex)
        {
            ShaderObject = shaderObject;
            RendererListIndex = rendererListIndex;
        }

        internal VividMeshShaderObject ShaderObject { get; }
        internal uint RendererListIndex { get; }
    }

    /// <summary>
    /// Immutable precompiled-DXIL shader object owned by the native mesh-shader plugin.
    /// Frame resources are deliberately kept out of this object.
    /// </summary>
    internal sealed class VividMeshShaderObject : IDisposable
    {
        private ulong m_NativeHandle;

        private VividMeshShaderObject(
            VividMeshShaderProgramAsset programAsset,
            VividMeshShaderRenderState renderState,
            ulong nativeHandle)
        {
            ProgramAsset = programAsset;
            RenderState = renderState;
            m_NativeHandle = nativeHandle;
        }

        internal VividMeshShaderProgramAsset ProgramAsset { get; }
        internal VividMeshShaderRenderState RenderState { get; }
        internal ulong NativeHandle => m_NativeHandle;
        internal bool IsValid => m_NativeHandle != 0;

        internal static bool TryCreate(
            VividMeshShaderProgramAsset programAsset,
            in VividMeshShaderRenderState renderState,
            out VividMeshShaderObject shaderObject,
            out string error)
        {
            shaderObject = null;
            error = null;
            if (programAsset == null)
            {
                error = "The precompiled mesh-shader program asset is missing.";
                return false;
            }

            if (programAsset.RootLayoutVersion != VividMeshShaderProgramAsset.CurrentRootLayoutVersion)
            {
                error = $"Mesh-shader root-layout mismatch: "
                        + $"program={programAsset.RootLayoutVersion}, "
                        + $"runtime={VividMeshShaderProgramAsset.CurrentRootLayoutVersion}.";
                return false;
            }

            byte[] amplificationDxil = programAsset.AmplificationDxilBytes;
            byte[] meshDxil = programAsset.MeshDxilBytes;
            byte[] pixelDxil = programAsset.PixelDxilBytes;
            if (amplificationDxil == null || amplificationDxil.Length == 0
                || meshDxil == null || meshDxil.Length == 0
                || pixelDxil == null || pixelDxil.Length == 0)
            {
                error = "The precompiled mesh-shader program does not contain AS, MS, and PS DXIL.";
                return false;
            }

            GCHandle amplificationHandle = default;
            GCHandle meshHandle = default;
            GCHandle pixelHandle = default;
            try
            {
                amplificationHandle = GCHandle.Alloc(amplificationDxil, GCHandleType.Pinned);
                meshHandle = GCHandle.Alloc(meshDxil, GCHandleType.Pinned);
                pixelHandle = GCHandle.Alloc(pixelDxil, GCHandleType.Pinned);

                var desc = new VividMeshShaderPlugin.NativeShaderObjectDxilDesc
                {
                    StructSize = (uint)Marshal.SizeOf<VividMeshShaderPlugin.NativeShaderObjectDxilDesc>(),
                    AbiVersion = VividMeshShaderPlugin.AbiVersion,
                    AmplificationShader = new VividMeshShaderPlugin.NativeBytecode
                    {
                        Data = amplificationHandle.AddrOfPinnedObject(),
                        Size = (ulong)amplificationDxil.LongLength,
                    },
                    MeshShader = new VividMeshShaderPlugin.NativeBytecode
                    {
                        Data = meshHandle.AddrOfPinnedObject(),
                        Size = (ulong)meshDxil.LongLength,
                    },
                    PixelShader = new VividMeshShaderPlugin.NativeBytecode
                    {
                        Data = pixelHandle.AddrOfPinnedObject(),
                        Size = (ulong)pixelDxil.LongLength,
                    },
                    RenderState = VividMeshShaderPlugin.CreateNativeRenderState(renderState),
                };

                ulong handle = VividMeshShaderPlugin.CreateShaderObjectFromDxil(ref desc);
                if (handle == 0)
                {
                    error = VividMeshShaderPlugin.GetLastErrorMessage(
                        "Native mesh ShaderObject creation failed.");
                    return false;
                }

                shaderObject = new VividMeshShaderObject(programAsset, renderState, handle);
                return true;
            }
            catch (Exception exception) when (VividMeshShaderPlugin.IsPluginLoadException(exception))
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (pixelHandle.IsAllocated)
                    pixelHandle.Free();
                if (meshHandle.IsAllocated)
                    meshHandle.Free();
                if (amplificationHandle.IsAllocated)
                    amplificationHandle.Free();
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
        internal const uint AbiVersion = 2;

        private const string NativeLibrary = "VividMeshShader";
        private const uint DxgiFormatR16G16B16A16Float = 10;
        private const uint DxgiFormatR32G32Uint = 17;
        private const uint DxgiFormatR16G16Float = 34;
        private const uint DxgiFormatD32Float = 40;
        private const int NativeRenderStateDescSize = 52;
        private const int NativeShaderObjectDescSize = 104;
        private const int NativeBytecodeSize = 16;
        private const int NativeShaderObjectDxilDescSize = 112;
        private const int NativeDispatchDescSize = 136;
        private const int MaxDispatchBatchCount = 64;

        private static IntPtr s_RenderEvent;
        private static int s_DispatchEventId = -1;
        private static int s_StateBoundaryEventId = -1;
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
        internal struct NativeBytecode
        {
            internal IntPtr Data;
            internal ulong Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeShaderObjectDxilDesc
        {
            internal uint StructSize;
            internal uint AbiVersion;
            internal NativeBytecode AmplificationShader;
            internal NativeBytecode MeshShader;
            internal NativeBytecode PixelShader;
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

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_CreateShaderObjectFromDxil")]
        internal static extern ulong CreateShaderObjectFromDxil(ref NativeShaderObjectDxilDesc desc);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_DestroyShaderObject")]
        internal static extern void DestroyShaderObject(ulong handle);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_CreateDispatchBatchRequest")]
        private static extern IntPtr CreateDispatchBatchRequest(
            [In] NativeDispatchDesc[] descs,
            uint dispatchCount);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_DestroyDispatchRequest")]
        private static extern void DestroyDispatchRequest(IntPtr request);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_GetRenderEventFunc")]
        private static extern IntPtr GetRenderEventFunc();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_GetDispatchEventId")]
        private static extern int GetDispatchEventId();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall, EntryPoint = "VMS_GetStateBoundaryEventId")]
        private static extern int GetStateBoundaryEventId();

        internal static NativeRenderStateDesc CreateNativeRenderState(
            in VividMeshShaderRenderState renderState)
        {
            return new NativeRenderStateDesc
            {
                CullMode = (uint)renderState.CullMode,
                FrontCounterClockwise = 1,
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
            s_StateBoundaryEventId = -1;
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
                s_StateBoundaryEventId = GetStateBoundaryEventId();
                if (s_RenderEvent == IntPtr.Zero
                    || s_DispatchEventId < 0
                    || s_StateBoundaryEventId < 0)
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

        internal static bool TryQueueDispatchBatch(
            CommandBuffer cmd,
            VividMeshShaderDispatch[] dispatches,
            int dispatchCount,
            GraphicsBuffer visibleRequests,
            GraphicsBuffer indirectArgs,
            GraphicsBuffer instances,
            GraphicsBuffer meshlets,
            GraphicsBuffer vertices,
            GraphicsBuffer indices,
            uint maxRequestCount,
            in Matrix4x4 viewProjection,
            out string error)
        {
            error = null;
            if (cmd == null
                || s_RenderEvent == IntPtr.Zero
                || s_DispatchEventId < 0
                || s_StateBoundaryEventId < 0
                || dispatches == null
                || dispatchCount <= 0
                || dispatchCount > dispatches.Length
                || dispatchCount > MaxDispatchBatchCount
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

            for (int dispatchIndex = 0; dispatchIndex < dispatchCount; dispatchIndex++)
            {
                if (dispatches[dispatchIndex].ShaderObject?.IsValid != true)
                {
                    error = "A mesh-shader batch entry is invalid.";
                    return false;
                }
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

                // SetData/SetBufferData may rotate a buffer's backing D3D12 resource
                // while keeping the managed wrapper, so resolve every resource here.
                IntPtr visibleRequestsPtr = visibleRequests.GetNativeBufferPtr();
                IntPtr indirectArgsPtr = indirectArgs.GetNativeBufferPtr();
                IntPtr instancesPtr = instances.GetNativeBufferPtr();
                IntPtr meshletsPtr = meshlets.GetNativeBufferPtr();
                IntPtr verticesPtr = vertices.GetNativeBufferPtr();
                IntPtr indicesPtr = indices.GetNativeBufferPtr();
                if (visibleRequestsPtr == IntPtr.Zero
                    || indirectArgsPtr == IntPtr.Zero
                    || instancesPtr == IntPtr.Zero
                    || meshletsPtr == IntPtr.Zero
                    || verticesPtr == IntPtr.Zero
                    || indicesPtr == IntPtr.Zero)
                {
                    error = "A mesh-shader GraphicsBuffer has no native D3D12 resource.";
                    return false;
                }

                var nativeDispatches = new NativeDispatchDesc[dispatchCount];
                for (int dispatchIndex = 0; dispatchIndex < dispatchCount; dispatchIndex++)
                {
                    VividMeshShaderDispatch dispatch = dispatches[dispatchIndex];
                    nativeDispatches[dispatchIndex] = new NativeDispatchDesc
                    {
                        StructSize = (uint)Marshal.SizeOf<NativeDispatchDesc>(),
                        AbiVersion = AbiVersion,
                        ShaderHandle = dispatch.ShaderObject.NativeHandle,
                        VisibleRequests = visibleRequestsPtr,
                        IndirectArgs = indirectArgsPtr,
                        Instances = instancesPtr,
                        Meshlets = meshletsPtr,
                        Vertices = verticesPtr,
                        Indices = indicesPtr,
                        RendererListIndex = dispatch.RendererListIndex,
                        MaxRequestCount = maxRequestCount,
                        ViewProjectionColumnMajor = NativeMatrix4x4.FromUnityMatrix(viewProjection),
                    };
                }

                request = CreateDispatchBatchRequest(
                    nativeDispatches,
                    (uint)dispatchCount);
                if (request == IntPtr.Zero)
                {
                    error = GetLastErrorMessage(
                        "Could not allocate a native mesh-shader dispatch batch.");
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

        internal static void QueueStateBoundary(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException(nameof(cmd));
            if (s_RenderEvent == IntPtr.Zero || s_StateBoundaryEventId < 0)
                throw new InvalidOperationException(
                    "The mesh-shader command-list boundary is unavailable.");

            cmd.IssuePluginEventAndData(
                s_RenderEvent,
                s_StateBoundaryEventId,
                IntPtr.Zero);
        }

        private static bool HasExpectedAbiLayout()
        {
            return Marshal.SizeOf<NativeRenderStateDesc>() == NativeRenderStateDescSize
                   && Marshal.SizeOf<NativeShaderObjectDesc>() == NativeShaderObjectDescSize
                   && Marshal.OffsetOf<NativeShaderObjectDesc>(nameof(NativeShaderObjectDesc.SourceUtf8)).ToInt64() == 8
                   && Marshal.OffsetOf<NativeShaderObjectDesc>(nameof(NativeShaderObjectDesc.RenderState)).ToInt64() == 48
                   && Marshal.SizeOf<NativeBytecode>() == NativeBytecodeSize
                   && Marshal.OffsetOf<NativeBytecode>(nameof(NativeBytecode.Size)).ToInt64() == 8
                   && Marshal.SizeOf<NativeShaderObjectDxilDesc>() == NativeShaderObjectDxilDescSize
                   && Marshal.OffsetOf<NativeShaderObjectDxilDesc>(nameof(NativeShaderObjectDxilDesc.AmplificationShader)).ToInt64() == 8
                   && Marshal.OffsetOf<NativeShaderObjectDxilDesc>(nameof(NativeShaderObjectDxilDesc.MeshShader)).ToInt64() == 24
                   && Marshal.OffsetOf<NativeShaderObjectDxilDesc>(nameof(NativeShaderObjectDxilDesc.PixelShader)).ToInt64() == 40
                   && Marshal.OffsetOf<NativeShaderObjectDxilDesc>(nameof(NativeShaderObjectDxilDesc.RenderState)).ToInt64() == 56
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
