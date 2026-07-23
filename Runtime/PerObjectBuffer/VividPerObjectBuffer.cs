using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public readonly struct VividPerObjectBufferStats
    {
        internal VividPerObjectBufferStats(
            int activeRendererCount,
            int usedBytes,
            int capacityBytes,
            int largestFreeBlockBytes,
            int dirtyRangeCount,
            int lastUploadBytes,
            int lastUploadRangeCount)
        {
            ActiveRendererCount = activeRendererCount;
            UsedBytes = usedBytes;
            CapacityBytes = capacityBytes;
            LargestFreeBlockBytes = largestFreeBlockBytes;
            DirtyRangeCount = dirtyRangeCount;
            LastUploadBytes = lastUploadBytes;
            LastUploadRangeCount = lastUploadRangeCount;
        }

        public int ActiveRendererCount { get; }

        public int UsedBytes { get; }

        public int CapacityBytes { get; }

        public int LargestFreeBlockBytes { get; }

        public int DirtyRangeCount { get; }

        public int LastUploadBytes { get; }

        public int LastUploadRangeCount { get; }
    }

    public readonly struct VividPerObjectBlock : IEquatable<VividPerObjectBlock>
    {
        internal VividPerObjectBlock(EntityId rendererEntityId, uint generation)
        {
            RendererEntityId = rendererEntityId;
            Generation = generation;
        }

        internal EntityId RendererEntityId { get; }

        internal uint Generation { get; }

        public bool IsValid => VividPerObjectBufferSystem.IsBlockValid(this);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetInt(string propertyName, int value)
        {
            SetInt(Shader.PropertyToID(ValidatePropertyName(propertyName)), value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetInt(int propertyNameId, int value)
        {
            VividPerObjectBufferSystem.SetValue(this, propertyNameId, VividPerObjectPropertyType.Int, value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetInt(VividPerObjectPropertyHandle property, int value)
        {
            VividPerObjectBufferSystem.SetValue(this, property, VividPerObjectPropertyType.Int, value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFloat(string propertyName, float value)
        {
            SetFloat(Shader.PropertyToID(ValidatePropertyName(propertyName)), value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFloat(int propertyNameId, float value)
        {
            VividPerObjectBufferSystem.SetValue(this, propertyNameId, VividPerObjectPropertyType.Float, value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFloat(VividPerObjectPropertyHandle property, float value)
        {
            VividPerObjectBufferSystem.SetValue(this, property, VividPerObjectPropertyType.Float, value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVector(string propertyName, Vector4 value)
        {
            SetVector(Shader.PropertyToID(ValidatePropertyName(propertyName)), value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVector(int propertyNameId, Vector4 value)
        {
            VividPerObjectBufferSystem.SetValue(this, propertyNameId, VividPerObjectPropertyType.Vector, value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVector(VividPerObjectPropertyHandle property, Vector4 value)
        {
            VividPerObjectBufferSystem.SetValue(this, property, VividPerObjectPropertyType.Vector, value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetColor(string propertyName, Color value)
        {
            SetColor(Shader.PropertyToID(ValidatePropertyName(propertyName)), value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetColor(int propertyNameId, Color value)
        {
            VividPerObjectBufferSystem.SetValue(
                this,
                propertyNameId,
                VividPerObjectPropertyType.Color,
                new Vector4(value.r, value.g, value.b, value.a));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetColor(VividPerObjectPropertyHandle property, Color value)
        {
            VividPerObjectBufferSystem.SetValue(
                this,
                property,
                VividPerObjectPropertyType.Color,
                new Vector4(value.r, value.g, value.b, value.a));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetMatrix(string propertyName, Matrix4x4 value)
        {
            SetMatrix(Shader.PropertyToID(ValidatePropertyName(propertyName)), value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetMatrix(int propertyNameId, Matrix4x4 value)
        {
            VividPerObjectBufferSystem.SetValue(this, propertyNameId, VividPerObjectPropertyType.Matrix, value);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetMatrix(VividPerObjectPropertyHandle property, Matrix4x4 value)
        {
            VividPerObjectBufferSystem.SetValue(this, property, VividPerObjectPropertyType.Matrix, value);
        }

        public bool Equals(VividPerObjectBlock other)
        {
            return RendererEntityId.Equals(other.RendererEntityId) && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is VividPerObjectBlock other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (RendererEntityId.GetHashCode() * 397) ^ (int)Generation;
            }
        }

        public static bool operator ==(VividPerObjectBlock left, VividPerObjectBlock right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VividPerObjectBlock left, VividPerObjectBlock right)
        {
            return !left.Equals(right);
        }

        private static string ValidatePropertyName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                throw new ArgumentException("A per-object property name is required.", nameof(propertyName));
            return propertyName;
        }
    }

    public static class VividPerObjectBuffer
    {
        public static VividPerObjectBlock Bind<TLayout>(Renderer renderer)
            where TLayout : VividPerObjectLayout<TLayout>, new()
        {
            return Bind(renderer, VividPerObjectLayout<TLayout>.Instance);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VividPerObjectBlock Bind(Renderer renderer, VividPerObjectLayout layout)
        {
            return VividPerObjectBufferSystem.Bind(renderer, layout);
        }

        public static void Unbind(Renderer renderer)
        {
            VividPerObjectBufferSystem.Unbind(renderer);
        }

        public static bool IsBound(Renderer renderer)
        {
            return VividPerObjectBufferSystem.IsBound(renderer);
        }

        public static VividPerObjectBufferStats GetStats()
        {
            return VividPerObjectBufferSystem.GetStats();
        }

        internal static void PrepareAndBind(CommandBuffer cmd)
        {
            VividPerObjectBufferSystem.PrepareAndBind(cmd);
        }

        internal static void DisposeAll()
        {
            VividPerObjectBufferSystem.DisposeAll();
        }
    }
}
