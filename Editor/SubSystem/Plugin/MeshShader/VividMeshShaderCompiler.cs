using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VividRP.Editor.MeshShader
{
    internal static class VividMeshShaderCompiler
    {
        internal const uint AbiVersion = 1;

        private const string NativeLibrary = "VividMeshShaderCompiler";
        private const int NativeIncludeRootSize = 16;
        private const int NativeCompileDescSize = 64;

        [Flags]
        internal enum CompileFlags : uint
        {
            None = 0,
            Debug = 1u << 0,
            DisableOptimizations = 1u << 1,
        }

        internal readonly struct IncludeRoot
        {
            internal IncludeRoot(string logicalPrefix, string physicalPath)
            {
                LogicalPrefix = logicalPrefix ?? string.Empty;
                PhysicalPath = physicalPath ?? string.Empty;
            }

            internal string LogicalPrefix { get; }
            internal string PhysicalPath { get; }
        }

        internal readonly struct CompilationResult
        {
            internal CompilationResult(bool success, byte[] dxil, string diagnostics)
            {
                Success = success;
                Dxil = dxil ?? Array.Empty<byte>();
                Diagnostics = diagnostics ?? string.Empty;
            }

            internal bool Success { get; }
            internal byte[] Dxil { get; }
            internal string Diagnostics { get; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeIncludeRoot
        {
            internal IntPtr LogicalPrefixUtf8;
            internal IntPtr PhysicalPathUtf8;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeCompileDesc
        {
            internal uint StructSize;
            internal uint AbiVersion;
            internal IntPtr SourceUtf8;
            internal ulong SourceLength;
            internal IntPtr SourceNameUtf8;
            internal IntPtr EntryPointUtf8;
            internal IntPtr TargetProfileUtf8;
            internal IntPtr IncludeRoots;
            internal uint IncludeRootCount;
            internal CompileFlags Flags;
        }

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VMSC_GetAbiVersion")]
        private static extern uint GetAbiVersion();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VMSC_GetCompilerVersion")]
        private static extern IntPtr GetCompilerVersionUtf8();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VMSC_Compile")]
        private static extern ulong CompileNative(ref NativeCompileDesc desc);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VMSC_GetResultSuccess")]
        private static extern uint GetResultSuccess(ulong result);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VMSC_GetResultData")]
        private static extern IntPtr GetResultData(ulong result);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VMSC_GetResultSize")]
        private static extern ulong GetResultSize(ulong result);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VMSC_GetResultDiagnostics")]
        private static extern IntPtr GetResultDiagnostics(ulong result);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VMSC_GetResultDiagnosticsSize")]
        private static extern ulong GetResultDiagnosticsSize(ulong result);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, EntryPoint = "VMSC_DestroyResult")]
        private static extern void DestroyResult(ulong result);

        internal static bool TryGetCompilerVersion(out string compilerVersion, out string error)
        {
            compilerVersion = string.Empty;
            error = null;
            if (!HasExpectedAbiLayout())
            {
                error = "The managed VividMeshShaderCompiler ABI layout is incompatible with the x64 native compiler.";
                return false;
            }

            try
            {
                uint nativeAbiVersion = GetAbiVersion();
                if (nativeAbiVersion != AbiVersion)
                {
                    error = $"VividMeshShaderCompiler ABI mismatch: managed={AbiVersion}, native={nativeAbiVersion}.";
                    return false;
                }

                IntPtr versionUtf8 = GetCompilerVersionUtf8();
                compilerVersion = versionUtf8 != IntPtr.Zero
                    ? Marshal.PtrToStringUTF8(versionUtf8) ?? string.Empty
                    : string.Empty;
                return true;
            }
            catch (Exception exception) when (IsPluginLoadException(exception))
            {
                error = exception.Message;
                return false;
            }
        }

        internal static CompilationResult Compile(
            string source,
            string sourceName,
            string entryPoint,
            string targetProfile,
            IncludeRoot[] includeRoots,
            CompileFlags flags)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(entryPoint))
                throw new ArgumentException("A shader entry point is required.", nameof(entryPoint));
            if (string.IsNullOrWhiteSpace(targetProfile))
                throw new ArgumentException("A shader target profile is required.", nameof(targetProfile));
            if (!HasExpectedAbiLayout())
                throw new InvalidOperationException(
                    "The managed VividMeshShaderCompiler ABI layout is incompatible with the x64 native compiler.");

            includeRoots ??= Array.Empty<IncludeRoot>();
            var nativeRoots = new NativeIncludeRoot[includeRoots.Length];
            var nativeStrings = new NativeUtf8String[includeRoots.Length * 2 + 4];
            GCHandle includeRootsHandle = default;
            ulong resultHandle = 0;

            try
            {
                var stringIndex = 0;
                NativeUtf8String sourceUtf8 = nativeStrings[stringIndex++] = new NativeUtf8String(source);
                NativeUtf8String sourceNameUtf8 = nativeStrings[stringIndex++] = new NativeUtf8String(sourceName);
                NativeUtf8String entryPointUtf8 = nativeStrings[stringIndex++] = new NativeUtf8String(entryPoint);
                NativeUtf8String targetProfileUtf8 = nativeStrings[stringIndex++] = new NativeUtf8String(targetProfile);

                for (var includeRootIndex = 0; includeRootIndex < includeRoots.Length; includeRootIndex++)
                {
                    NativeUtf8String logicalPrefixUtf8 =
                        nativeStrings[stringIndex++] = new NativeUtf8String(includeRoots[includeRootIndex].LogicalPrefix);
                    NativeUtf8String physicalPathUtf8 =
                        nativeStrings[stringIndex++] = new NativeUtf8String(includeRoots[includeRootIndex].PhysicalPath);
                    nativeRoots[includeRootIndex] = new NativeIncludeRoot
                    {
                        LogicalPrefixUtf8 = logicalPrefixUtf8.Pointer,
                        PhysicalPathUtf8 = physicalPathUtf8.Pointer,
                    };
                }

                IntPtr nativeRootsPointer = IntPtr.Zero;
                if (nativeRoots.Length > 0)
                {
                    includeRootsHandle = GCHandle.Alloc(nativeRoots, GCHandleType.Pinned);
                    nativeRootsPointer = includeRootsHandle.AddrOfPinnedObject();
                }

                var desc = new NativeCompileDesc
                {
                    StructSize = (uint)Marshal.SizeOf<NativeCompileDesc>(),
                    AbiVersion = AbiVersion,
                    SourceUtf8 = sourceUtf8.Pointer,
                    SourceLength = sourceUtf8.ByteLength,
                    SourceNameUtf8 = sourceNameUtf8.Pointer,
                    EntryPointUtf8 = entryPointUtf8.Pointer,
                    TargetProfileUtf8 = targetProfileUtf8.Pointer,
                    IncludeRoots = nativeRootsPointer,
                    IncludeRootCount = (uint)nativeRoots.Length,
                    Flags = flags,
                };

                resultHandle = CompileNative(ref desc);
                if (resultHandle == 0)
                    return new CompilationResult(false, null, "The native compiler returned no result.");

                bool success = GetResultSuccess(resultHandle) != 0;
                string diagnostics = CopyUtf8(
                    GetResultDiagnostics(resultHandle),
                    GetResultDiagnosticsSize(resultHandle));
                byte[] dxil = success
                    ? CopyBytes(GetResultData(resultHandle), GetResultSize(resultHandle))
                    : Array.Empty<byte>();
                return new CompilationResult(success, dxil, diagnostics);
            }
            finally
            {
                if (resultHandle != 0)
                    DestroyResult(resultHandle);
                if (includeRootsHandle.IsAllocated)
                    includeRootsHandle.Free();
                for (var stringIndex = 0; stringIndex < nativeStrings.Length; stringIndex++)
                    nativeStrings[stringIndex]?.Dispose();
            }
        }

        private static byte[] CopyBytes(IntPtr source, ulong size)
        {
            if (source == IntPtr.Zero || size == 0)
                return Array.Empty<byte>();
            if (size > int.MaxValue)
                throw new InvalidOperationException("Compiled DXIL exceeds the managed array size limit.");

            var bytes = new byte[(int)size];
            Marshal.Copy(source, bytes, 0, bytes.Length);
            return bytes;
        }

        private static string CopyUtf8(IntPtr source, ulong size)
        {
            byte[] bytes = CopyBytes(source, size);
            if (bytes.Length == 0)
                return string.Empty;

            int length = bytes.Length;
            while (length > 0 && bytes[length - 1] == 0)
                length--;
            return Encoding.UTF8.GetString(bytes, 0, length);
        }

        internal static bool HasExpectedAbiLayout()
        {
            return IntPtr.Size == 8
                   && Marshal.SizeOf<NativeIncludeRoot>() == NativeIncludeRootSize
                   && Marshal.SizeOf<NativeCompileDesc>() == NativeCompileDescSize
                   && Marshal.OffsetOf<NativeCompileDesc>(nameof(NativeCompileDesc.SourceUtf8)).ToInt64() == 8
                   && Marshal.OffsetOf<NativeCompileDesc>(nameof(NativeCompileDesc.SourceLength)).ToInt64() == 16
                   && Marshal.OffsetOf<NativeCompileDesc>(nameof(NativeCompileDesc.IncludeRoots)).ToInt64() == 48
                   && Marshal.OffsetOf<NativeCompileDesc>(nameof(NativeCompileDesc.Flags)).ToInt64() == 60;
        }

        private static bool IsPluginLoadException(Exception exception)
        {
            return exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException
                or MarshalDirectiveException;
        }

        private sealed class NativeUtf8String : IDisposable
        {
            internal NativeUtf8String(string value)
            {
                value ??= string.Empty;
                Pointer = Marshal.StringToCoTaskMemUTF8(value);
                ByteLength = (ulong)Encoding.UTF8.GetByteCount(value);
            }

            internal IntPtr Pointer { get; private set; }
            internal ulong ByteLength { get; }

            public void Dispose()
            {
                IntPtr pointer = Pointer;
                Pointer = IntPtr.Zero;
                if (pointer != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pointer);
            }
        }
    }
}
