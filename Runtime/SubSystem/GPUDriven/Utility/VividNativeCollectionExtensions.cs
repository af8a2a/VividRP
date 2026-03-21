using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace VividRP.Runtime.GPUDriven.Utility
{
    public static class VividNativeCollectionExtensions
    {
        public static unsafe ref T ElementAtRef<T>(this NativeArray<T> array, int index) where T : struct
        {
            if ((uint) index >= (uint) array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of range.");
            }

            return ref UnsafeUtility.ArrayElementAsRef<T>(array.GetUnsafePtr(), index);
        }

        public static unsafe ref readonly T ElementAtRefReadonly<T>(this NativeArray<T> array, int index) where T : struct
        {
            if ((uint) index >= (uint) array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of range.");
            }

            return ref UnsafeUtility.ArrayElementAsRef<T>(array.GetUnsafeReadOnlyPtr(), index);
        }

        public static unsafe T* ElementPtr<T>(this NativeArray<T> array, int index) where T : unmanaged
        {
            if ((uint) index >= (uint) array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of range.");
            }

            return (T*) array.GetUnsafePtr() + index;
        }

        public static unsafe T* ElementPtrReadonly<T>(this NativeArray<T> array, int index) where T : unmanaged
        {
            if ((uint) index >= (uint) array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of range.");
            }

            return (T*) array.GetUnsafeReadOnlyPtr() + index;
        }

        public static unsafe ref T ElementAtRef<T>(this NativeList<T> list, int index) where T : unmanaged
        {
            if ((uint) index >= (uint) list.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of range.");
            }

            return ref UnsafeUtility.ArrayElementAsRef<T>(list.GetUnsafePtr(), index);
        }

        public static unsafe ref readonly T ElementAtRefReadonly<T>(this NativeList<T> list, int index) where T : unmanaged
        {
            if ((uint) index >= (uint) list.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of range.");
            }

            return ref UnsafeUtility.ArrayElementAsRef<T>(list.GetUnsafeReadOnlyPtr(), index);
        }
    }
}
