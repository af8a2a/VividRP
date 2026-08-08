using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;

namespace VividRP.Runtime
{
    internal readonly struct VTIOReadCommand
    {
        internal VTIOReadCommand(long fileOffset, int byteSize, bool highPriority)
        {
            if (fileOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(fileOffset));
            if (byteSize < 0)
                throw new ArgumentOutOfRangeException(nameof(byteSize));

            FileOffset = fileOffset;
            ByteSize = byteSize;
            HighPriority = highPriority;
        }

        internal long FileOffset { get; }

        internal int ByteSize { get; }

        internal bool HighPriority { get; }
    }

    internal interface IVTIOBackend : IDisposable
    {
        string Name { get; }

        bool IsAvailable { get; }

        IVTIOBatch CreateBatch(string path, IReadOnlyList<VTIOReadCommand> commands);
    }

    internal interface IVTIOBatch : IDisposable
    {
        int Count { get; }

        bool IsCompleted { get; }

        bool Failed { get; }

        string Error { get; }

        bool TryGetResult(int commandIndex, out byte[] data);

        void Cancel();
    }

    internal sealed unsafe class VTAsyncReadManagerBackend : IVTIOBackend
    {
        private readonly Dictionary<string, SharedFile> m_Files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<int[]> m_BatchIntBufferPool = new();
        private bool m_Disposed;

        private sealed class SharedFile
        {
            internal FileHandle Handle;
            internal int ReferenceCount;
        }

        public string Name => nameof(VividVirtualTextureIOBackendMode.AsyncReadManager);

        public bool IsAvailable => true;

        public IVTIOBatch CreateBatch(string path, IReadOnlyList<VTIOReadCommand> commands)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(VTAsyncReadManagerBackend));

            SharedFile file = AcquireFile(path);
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

            foreach (SharedFile file in m_Files.Values)
                CloseFile(file);
            m_Files.Clear();
            m_BatchIntBufferPool.Clear();
            m_Disposed = true;
        }

        private int[] RentBatchIntBuffer()
        {
            return m_BatchIntBufferPool.Count > 0
                ? m_BatchIntBufferPool.Pop()
                : new int[64];
        }

        private void ReturnBatchIntBuffer(int[] buffer)
        {
            if (!m_Disposed && buffer != null && buffer.Length >= 64)
                m_BatchIntBufferPool.Push(buffer);
        }

        private SharedFile AcquireFile(string path)
        {
            if (m_Files.TryGetValue(path, out SharedFile file))
            {
                file.ReferenceCount += 1;
                return file;
            }

            file = new SharedFile
            {
                Handle = AsyncReadManager.OpenFileAsync(path),
                ReferenceCount = 1,
            };
            m_Files.Add(path, file);
            return file;
        }

        private void ReleaseFile(string path, SharedFile file)
        {
            if (file == null || file.ReferenceCount <= 0)
                return;

            file.ReferenceCount -= 1;
            if (file.ReferenceCount == 0 && file.Handle.Status == FileStatus.OpenFailed && !m_Disposed)
            {
                CloseFile(file);
                m_Files.Remove(path);
            }
        }

        private static void CloseFile(SharedFile file)
        {
            if (file == null || !file.Handle.IsValid())
                return;

            if (file.Handle.Status == FileStatus.Pending)
                file.Handle.JobHandle.Complete();
            if (file.Handle.Status != FileStatus.Closed)
                file.Handle.Close(default).Complete();
        }

        private sealed unsafe class Batch : IVTIOBatch
        {
            private readonly int[] m_BufferOffsets;
            private readonly int[] m_ByteSizes;
            private readonly VTAsyncReadManagerBackend m_Owner;
            private readonly string m_Path;
            private SharedFile m_File;
            private NativeArray<byte> m_Buffer;
            private NativeArray<ReadCommand> m_ReadCommands;
            private ReadHandle m_ReadHandle;
            private bool m_HasReadHandle;
            private bool m_Disposed;

            internal Batch(
                VTAsyncReadManagerBackend owner,
                string path,
                SharedFile file,
                IReadOnlyList<VTIOReadCommand> commands)
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new ArgumentException("VT stream path must be non-empty.", nameof(path));
                if (commands == null || commands.Count == 0 || commands.Count > 64)
                    throw new ArgumentOutOfRangeException(nameof(commands));

                m_Owner = owner;
                m_Path = path;
                m_File = file;

                Count = commands.Count;
                m_BufferOffsets = owner.RentBatchIntBuffer();
                m_ByteSizes = owner.RentBatchIntBuffer();
                try
                {
                    int totalByteSize = 0;
                    for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                    {
                        m_BufferOffsets[commandIndex] = totalByteSize;
                        m_ByteSizes[commandIndex] = commands[commandIndex].ByteSize;
                        totalByteSize = checked(totalByteSize + commands[commandIndex].ByteSize);
                    }

                    m_Buffer = new NativeArray<byte>(
                        Math.Max(1, totalByteSize),
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory);
                    m_ReadCommands = new NativeArray<ReadCommand>(
                        commands.Count,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory);
                    byte* buffer = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(m_Buffer);
                    for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                    {
                        VTIOReadCommand command = commands[commandIndex];
                        m_ReadCommands[commandIndex] = new ReadCommand
                        {
                            Buffer = buffer + m_BufferOffsets[commandIndex],
                            Offset = command.FileOffset,
                            Size = command.ByteSize,
                        };
                    }
                }
                catch
                {
                    if (m_ReadCommands.IsCreated)
                        m_ReadCommands.Dispose();
                    if (m_Buffer.IsCreated)
                        m_Buffer.Dispose();
                    owner.ReturnBatchIntBuffer(m_BufferOffsets);
                    owner.ReturnBatchIntBuffer(m_ByteSizes);
                    throw;
                }
            }

            public int Count { get; }

            public bool IsCompleted
            {
                get
                {
                    EnsureSubmitted();
                    return m_Disposed
                           || m_File == null
                           || m_File.Handle.Status == FileStatus.OpenFailed
                           || (m_HasReadHandle && m_ReadHandle.Status != ReadStatus.InProgress);
                }
            }

            public bool Failed
            {
                get
                {
                    EnsureSubmitted();
                    return m_Disposed
                           || m_File == null
                           || m_File.Handle.Status == FileStatus.OpenFailed
                           || (m_HasReadHandle && m_ReadHandle.Status != ReadStatus.InProgress && m_ReadHandle.Status != ReadStatus.Complete);
                }
            }

            public string Error => Failed
                ? m_File != null && m_File.Handle.Status == FileStatus.OpenFailed
                    ? "AsyncReadManager failed to open the VT stream file."
                    : $"AsyncReadManager completed with status {(m_HasReadHandle ? m_ReadHandle.Status : ReadStatus.Failed)}."
                : null;

            public bool TryGetResult(int commandIndex, out byte[] data)
            {
                data = null;
                EnsureSubmitted();
                if (m_Disposed
                    || commandIndex < 0
                    || commandIndex >= Count
                    || m_ReadHandle.Status != ReadStatus.Complete
                    || m_ReadHandle.GetBytesRead((uint)commandIndex) != m_ByteSizes[commandIndex])
                {
                    return false;
                }

                int byteSize = m_ByteSizes[commandIndex];
                data = new byte[byteSize];
                if (byteSize > 0)
                    NativeArray<byte>.Copy(m_Buffer, m_BufferOffsets[commandIndex], data, 0, byteSize);
                return true;
            }

            public void Cancel()
            {
                if (!m_Disposed && m_HasReadHandle && m_ReadHandle.Status == ReadStatus.InProgress)
                    m_ReadHandle.Cancel();
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;

                if (m_HasReadHandle && m_ReadHandle.IsValid())
                {
                    if (m_ReadHandle.Status == ReadStatus.InProgress)
                        m_ReadHandle.Cancel();
                    m_ReadHandle.JobHandle.Complete();
                    m_ReadHandle.Dispose();
                }

                if (m_ReadCommands.IsCreated)
                    m_ReadCommands.Dispose();
                if (m_Buffer.IsCreated)
                    m_Buffer.Dispose();
                m_Owner.ReleaseFile(m_Path, m_File);
                m_Owner.ReturnBatchIntBuffer(m_BufferOffsets);
                m_Owner.ReturnBatchIntBuffer(m_ByteSizes);
                m_File = null;
                m_Disposed = true;
            }

            private void EnsureSubmitted()
            {
                if (m_Disposed || m_HasReadHandle || m_File == null)
                    return;
                if (m_File.Handle.Status == FileStatus.Pending)
                    return;
                if (m_File.Handle.Status != FileStatus.Open)
                    return;

                var commands = new ReadCommandArray
                {
                    ReadCommands = (ReadCommand*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(m_ReadCommands),
                    CommandCount = m_ReadCommands.Length,
                };
                m_ReadHandle = AsyncReadManager.Read(in m_File.Handle, commands);
                m_HasReadHandle = true;
            }
        }
    }
}
