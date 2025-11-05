#ifndef BINDLESS_INCLUDED
#define BINDLESS_INCLUDED

//reference from https://github.com/Delt06/aaaa-rp
//Shader Model 6.6
#pragma require Int64BufferAtomics

//usage from D3D12 Spec
//https://microsoft.github.io/DirectX-Specs/d3d/HLSL_ShaderModel6_6.html
Texture2D GetBindlessTexture2D(const uint index)
{
    Texture2D texture = ResourceDescriptorHeap[index];
    return texture;
}

#endif 