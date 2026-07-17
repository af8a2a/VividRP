using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace VividRP.Runtime.ECS
{
    internal interface IVividEcsColumn : IDisposable
    {
        VividEcsTypeIndex typeIndex { get; }

        bool isCreated { get; }

        int capacity { get; }

        void EnsureCapacity(int requestedCapacity);

        void CopyEntry(int sourceIndex, int destinationIndex);

        void ClearEntry(int index);
    }

    internal sealed class VividEcsComponentColumn<T> : IVividEcsColumn
        where T : struct, IVividEcsComponentData
    {
        private NativeArray<T> m_Data;

        public VividEcsComponentColumn(VividEcsTypeIndex typeIndex)
        {
            this.typeIndex = typeIndex;
        }

        public VividEcsTypeIndex typeIndex { get; }

        public bool isCreated => m_Data.IsCreated;

        public int capacity => m_Data.IsCreated ? m_Data.Length : 0;

        public NativeArray<T> data => m_Data;

        public T this[int index]
        {
            get => m_Data[index];
            set => m_Data[index] = value;
        }

        public void EnsureCapacity(int requestedCapacity)
        {
            if (requestedCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(requestedCapacity));

            if (capacity == requestedCapacity)
                return;

            NativeArray<T> newData = default;
            if (requestedCapacity > 0)
                newData = new NativeArray<T>(requestedCapacity, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            if (m_Data.IsCreated && newData.IsCreated)
            {
                int copyCount = Math.Min(m_Data.Length, newData.Length);
                if (copyCount > 0)
                    NativeArray<T>.Copy(m_Data, newData, copyCount);
            }

            Dispose();
            m_Data = newData;
        }

        public void CopyEntry(int sourceIndex, int destinationIndex)
        {
            m_Data[destinationIndex] = m_Data[sourceIndex];
        }

        public void ClearEntry(int index)
        {
            m_Data[index] = default;
        }

        public void Dispose()
        {
            if (m_Data.IsCreated)
                m_Data.Dispose();

            m_Data = default;
        }
    }

    internal sealed class VividEcsSoaColumn<TComponent> : IVividEcsColumn
        where TComponent : struct, IVividEcsSoaComponentData
    {
        private NativeArray<byte>[] m_Fields = Array.Empty<NativeArray<byte>>();
        private VividEcsTypeInfo m_TypeInfo;
        private int m_Capacity;
        private int m_Version = 1;

        public VividEcsSoaColumn(VividEcsTypeIndex typeIndex)
        {
            this.typeIndex = typeIndex;
            m_TypeInfo = VividEcsTypeManager.GetTypeInfo(typeIndex);
            m_Fields = new NativeArray<byte>[m_TypeInfo.SoaFieldCount];
        }

        public VividEcsTypeIndex typeIndex { get; }

        public bool isCreated => m_Fields.Length > 0 && m_Fields[0].IsCreated;

        public int capacity => m_Capacity;

        public int version => m_Version;

        public void EnsureCapacity(int requestedCapacity)
        {
            if (requestedCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(requestedCapacity));

            if (m_Capacity == requestedCapacity)
                return;

            var newFields = new NativeArray<byte>[m_TypeInfo.SoaFieldCount];
            for (int fieldIndex = 0; fieldIndex < newFields.Length; fieldIndex++)
            {
                VividEcsSoaFieldInfo fieldInfo = m_TypeInfo.GetSoaFieldInfo(fieldIndex);
                int requestedLength = requestedCapacity * fieldInfo.ElementSize;
                if (requestedLength > 0)
                    newFields[fieldIndex] = new NativeArray<byte>(requestedLength, Allocator.Persistent, NativeArrayOptions.ClearMemory);

                if (fieldIndex < m_Fields.Length && m_Fields[fieldIndex].IsCreated && newFields[fieldIndex].IsCreated)
                {
                    int copyCount = Math.Min(m_Fields[fieldIndex].Length, newFields[fieldIndex].Length);
                    if (copyCount > 0)
                        NativeArray<byte>.Copy(m_Fields[fieldIndex], newFields[fieldIndex], copyCount);
                }
            }

            DisposeFields();
            m_Fields = newFields;
            m_Capacity = requestedCapacity;
            IncrementVersion();
        }

        public NativeArray<TField> GetFieldArray<TField>(int fieldIndex)
            where TField : struct
        {
            VividEcsSoaFieldInfo fieldInfo = m_TypeInfo.GetSoaFieldInfo(fieldIndex);
            if (UnsafeUtility.SizeOf<TField>() != fieldInfo.ElementSize)
                throw new InvalidOperationException($"Field {fieldIndex} expects {fieldInfo.ElementSize} bytes but {typeof(TField).Name} is {UnsafeUtility.SizeOf<TField>()} bytes.");

            return m_Fields[fieldIndex].Reinterpret<TField>(sizeof(byte));
        }

        public TField GetFieldValue<TField>(int fieldIndex, int index)
            where TField : struct
        {
            return GetFieldArray<TField>(fieldIndex)[index];
        }

        public void SetFieldValue<TField>(int fieldIndex, int index, TField value)
            where TField : struct
        {
            NativeArray<TField> field = GetFieldArray<TField>(fieldIndex);
            field[index] = value;
        }

        public void CopyEntry(int sourceIndex, int destinationIndex)
        {
            for (int fieldIndex = 0; fieldIndex < m_Fields.Length; fieldIndex++)
            {
                VividEcsSoaFieldInfo fieldInfo = m_TypeInfo.GetSoaFieldInfo(fieldIndex);
                int sourceOffset = sourceIndex * fieldInfo.ElementSize;
                int destinationOffset = destinationIndex * fieldInfo.ElementSize;
                unsafe
                {
                    byte* source = (byte*)m_Fields[fieldIndex].GetUnsafePtr() + sourceOffset;
                    byte* destination = (byte*)m_Fields[fieldIndex].GetUnsafePtr() + destinationOffset;
                    UnsafeUtility.MemCpy(destination, source, fieldInfo.ElementSize);
                }
            }
        }

        public void ClearEntry(int index)
        {
            for (int fieldIndex = 0; fieldIndex < m_Fields.Length; fieldIndex++)
            {
                VividEcsSoaFieldInfo fieldInfo = m_TypeInfo.GetSoaFieldInfo(fieldIndex);
                int offset = index * fieldInfo.ElementSize;
                unsafe
                {
                    byte* destination = (byte*)m_Fields[fieldIndex].GetUnsafePtr() + offset;
                    UnsafeUtility.MemClear(destination, fieldInfo.ElementSize);
                }
            }
        }

        public void Dispose()
        {
            bool hadStorage = m_Capacity != 0 || isCreated;
            DisposeFields();
            m_Capacity = 0;
            if (hadStorage)
                IncrementVersion();
        }

        private void DisposeFields()
        {
            for (int fieldIndex = 0; fieldIndex < m_Fields.Length; fieldIndex++)
            {
                if (m_Fields[fieldIndex].IsCreated)
                    m_Fields[fieldIndex].Dispose();
            }

            m_Fields = new NativeArray<byte>[m_TypeInfo.SoaFieldCount];
        }

        private void IncrementVersion()
        {
            m_Version = m_Version == int.MaxValue ? 1 : m_Version + 1;
        }
    }

    internal sealed class VividEcsBitColumn<TComponent> : IVividEcsColumn
        where TComponent : struct, IVividEcsBitComponentData
    {
        public const int WordsPerPage = VividEcsConstants.PageEntryCount / 64;

        private NativeArray<ulong> m_Words;
        private int m_Capacity;

        public VividEcsBitColumn(VividEcsTypeIndex typeIndex)
        {
            this.typeIndex = typeIndex;
        }

        public VividEcsTypeIndex typeIndex { get; }

        public bool isCreated => m_Words.IsCreated;

        public int capacity => m_Capacity;

        public NativeArray<ulong> words => m_Words;

        public void EnsureCapacity(int requestedCapacity)
        {
            if (requestedCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(requestedCapacity));

            if (m_Capacity == requestedCapacity)
                return;

            int requestedPageCount = requestedCapacity <= 0 ? 0 : VividEcsConstants.AlignToPage(requestedCapacity) / VividEcsConstants.PageEntryCount;
            var newWords = requestedPageCount > 0
                ? new NativeArray<ulong>(requestedPageCount * WordsPerPage, Allocator.Persistent, NativeArrayOptions.ClearMemory)
                : default;

            if (m_Words.IsCreated && newWords.IsCreated)
            {
                int copyCount = Math.Min(m_Words.Length, newWords.Length);
                if (copyCount > 0)
                    NativeArray<ulong>.Copy(m_Words, newWords, copyCount);
            }

            Dispose();
            m_Words = newWords;
            m_Capacity = requestedCapacity;
        }

        public bool Get(int index)
        {
            GetWordAndBit(index, out int wordIndex, out int bitIndex);
            return (m_Words[wordIndex] & (1UL << bitIndex)) != 0UL;
        }

        public void Set(int index, bool value)
        {
            GetWordAndBit(index, out int wordIndex, out int bitIndex);
            ulong mask = 1UL << bitIndex;
            m_Words[wordIndex] = value
                ? m_Words[wordIndex] | mask
                : m_Words[wordIndex] & ~mask;
        }

        public void CopyEntry(int sourceIndex, int destinationIndex)
        {
            Set(destinationIndex, Get(sourceIndex));
        }

        public void ClearEntry(int index)
        {
            Set(index, false);
        }

        public void Dispose()
        {
            if (m_Words.IsCreated)
                m_Words.Dispose();

            m_Words = default;
            m_Capacity = 0;
        }

        private static void GetWordAndBit(int index, out int wordIndex, out int bitIndex)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            wordIndex = index >> 6;
            bitIndex = index & 63;
        }
    }
}
