using System;
using Unity.Collections;

namespace UnityEngine.Rendering.Universal
{
    public enum BindlessResourceType : int
    {
        SinglePassDownSample,
        BindlessResourceCount
    }


    public struct BindlessResource
    {
        public uint Index;
        public IntPtr Resource;
    }


    public class BindlessResourceDesc
    {
        
    }
    
    
    public class BindlessResourceManager : Singleton<BindlessResourceManager>
    {
        NativeArray<BindlessResource> m_ResourcePool = new NativeArray<BindlessResource>((int)BindlessResourceType.BindlessResourceCount, Allocator.Persistent);
    }
}