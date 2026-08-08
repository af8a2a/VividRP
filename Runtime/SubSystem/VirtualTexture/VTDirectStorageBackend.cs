using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VividRP.Runtime
{
    internal sealed class VTDirectStorageBackend : IVTIOBackend
    {
        private const string NativeLibrary = "VividVTStreamingNative";

        private readonly Dictionary<string, FileState> m_Files = new(StringComparer.OrdinalIgnoreCase);
        private readonly long[] m_BatchOffsets = new long[64];
        private readonly int[] m_BatchSizes = new int[64];
        private readonly byte[] m_BatchPriorities = new byte[64];
        private readonly bool m_IsAvailable;
        private bool m_Disposed;

        private sealed class FileState
        {
            internal IntPtr Handle;
            internal int ReferenceCount;
        }

        internal VTDirectStorageBackend()
        {
            try
            {
                m_IsAvailable = VividVT_DSIsAvailable() != 0;
            }
            catch (DllNotFoundException)
            {
                m_IsAvailable = false;
            }
            catch (EntryPointNotFoundException)
            {
                m_IsAvailable = false;
            }
            catch (BadImageFormatException)
            {
                m_IsAvailable = false;
            }
        }

        public string Name => nameof(VividVirtualTextureIOBackendMode.DirectStorage);

        public bool IsAvailable => !m_Disposed && m_IsAvailable;

        public IVTIOBatch CreateBatch(string path, IReadOnlyList<VTIOReadCommand> commands)
        {
            if (!IsAvailable)
                throw new InvalidOperationException("DirectStorage is unavailable.");
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("VT stream path must be non-empty.", nameof(path));
            if (commands == null || commands.Count == 0 || commands.Count > 64)
                throw new ArgumentOutOfRangeException(nameof(commands));

            FileState file = AcquireFile(path);
            try
            {
                return new Batch(this, path, file, commands);
            }
            catch
            {
                ReleaseFile(path, file);
                throw;
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            foreach (FileState file in m_Files.Values)
            {
                if (file.Handle != IntPtr.Zero)
                    VividVT_DSCloseFile(file.Handle);
            }

            m_Files.Clear();
            m_Disposed = true;
        }

        private FileState AcquireFile(string path)
        {
            if (m_Files.TryGetValue(path, out FileState file))
            {
                file.ReferenceCount += 1;
                return file;
            }

            IntPtr handle = VividVT_DSOpenFile(path);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException(GetLastError("DirectStorage failed to open the VT stream file."));

            file = new FileState
            {
                Handle = handle,
                ReferenceCount = 1,
            };
            m_Files.Add(path, file);
            return file;
        }

        private void ReleaseFile(string path, FileState file)
        {
            if (file == null || file.ReferenceCount <= 0)
                return;

            file.ReferenceCount -= 1;
        }

        private static string GetLastError(string fallback)
        {
            IntPtr error = VividVT_DSGetLastError();
            return error != IntPtr.Zero ? Marshal.PtrToStringAnsi(error) ?? fallback : fallback;
        }

        private sealed class Batch : IVTIOBatch
        {
            private readonly VTDirectStorageBackend m_Owner;
            private readonly string m_Path;
            private FileState m_File;
            private IntPtr m_Handle;
            private bool m_Disposed;

            internal Batch(
                VTDirectStorageBackend owner,
                string path,
                FileState file,
                IReadOnlyList<VTIOReadCommand> commands)
            {
                m_Owner = owner;
                m_Path = path;
                m_File = file;
                Count = commands.Count;
                for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                {
                    owner.m_BatchOffsets[commandIndex] = commands[commandIndex].FileOffset;
                    owner.m_BatchSizes[commandIndex] = commands[commandIndex].ByteSize;
                    owner.m_BatchPriorities[commandIndex] = commands[commandIndex].HighPriority ? (byte)1 : (byte)0;
                }

                m_Handle = VividVT_DSCreateMemoryBatch(
                    file.Handle,
                    owner.m_BatchOffsets,
                    owner.m_BatchSizes,
                    owner.m_BatchPriorities,
                    commands.Count);
                if (m_Handle == IntPtr.Zero)
                    throw new InvalidOperationException(GetLastError("DirectStorage failed to create a VT read batch."));
            }

            public int Count { get; }

            public bool IsCompleted => m_Disposed || VividVT_DSGetBatchStatus(m_Handle) != 0;

            public bool Failed => m_Disposed || VividVT_DSGetBatchStatus(m_Handle) < 0;

            public string Error => Failed ? GetLastError("DirectStorage VT read batch failed.") : null;

            public bool TryGetResult(int commandIndex, out byte[] data)
            {
                data = null;
                if (m_Disposed || commandIndex < 0 || commandIndex >= Count || VividVT_DSGetBatchStatus(m_Handle) != 1)
                    return false;

                int byteSize = VividVT_DSGetResultSize(m_Handle, commandIndex);
                if (byteSize < 0)
                    return false;
                data = new byte[byteSize];
                return VividVT_DSCopyResult(m_Handle, commandIndex, data, data.Length) != 0;
            }

            public void Cancel()
            {
                if (!m_Disposed)
                    VividVT_DSCancelBatch(m_Handle);
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;

                if (m_Handle != IntPtr.Zero)
                    VividVT_DSReleaseBatch(m_Handle);
                m_Handle = IntPtr.Zero;
                m_Owner.ReleaseFile(m_Path, m_File);
                m_File = null;
                m_Disposed = true;
            }
        }

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int VividVT_DSIsAvailable();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern IntPtr VividVT_DSOpenFile(string path);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void VividVT_DSCloseFile(IntPtr file);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr VividVT_DSCreateMemoryBatch(
            IntPtr file,
            long[] offsets,
            int[] sizes,
            byte[] priorities,
            int commandCount);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int VividVT_DSGetBatchStatus(IntPtr batch);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int VividVT_DSGetResultSize(IntPtr batch, int commandIndex);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int VividVT_DSCopyResult(IntPtr batch, int commandIndex, [Out] byte[] destination, int destinationSize);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void VividVT_DSCancelBatch(IntPtr batch);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void VividVT_DSReleaseBatch(IntPtr batch);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr VividVT_DSGetLastError();
    }
}
