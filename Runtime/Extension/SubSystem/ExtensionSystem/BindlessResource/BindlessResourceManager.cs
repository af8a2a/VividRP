using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class BindlessResourceManager : Singleton<BindlessResourceManager>
    {
        Dictionary<UInt64, uint> m_PresisentResources = new Dictionary<UInt64, uint>();


        UInt64 ComputeTextureHash(Texture tex)
        {
            UInt64 w = (UInt64)tex.width;
            UInt64 h = (UInt64)tex.height;
            UInt64 d = (UInt64)(tex is Texture3D ? ((Texture3D)tex).depth : 1);
            UInt64 fmt = (UInt64)(tex is Texture2D t2d ? (int)t2d.format : 0);

            return (w << 48) ^ (h << 32) ^ (d << 16) ^ fmt;
        }


        uint GetOrRegisterResource(Texture texture, bool isUAV = false)
        {
            var textureHash = ComputeTextureHash(texture);

            if (m_PresisentResources.TryGetValue(textureHash, out var resource))
            {
                return resource;
            }

            var currentIndex = Bindless.GetSRVDescriptorHeapCount();
            bool success = true;
            if (isUAV)
            {
                success &= Bindless.CreateUAVDescriptor(texture.GetNativeTexturePtr(), currentIndex);
            }

            else
            {
                success &= Bindless.CreateSRVDescriptor(texture.GetNativeTexturePtr(), currentIndex);
            }

            if (!success)
            {
                Debug.LogError("Failed to register resource: " + texture.name);
            }

            return currentIndex;
        }

        
    }
}