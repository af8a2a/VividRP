// UNITY_SHADER_NO_UPGRADE

#ifndef VIVID_PER_OBJECT_BUFFER_INCLUDED
#define VIVID_PER_OBJECT_BUFFER_INCLUDED

ByteAddressBuffer _VividPerObjectBuffer;

#define VIVID_PER_OBJECT_USER_VALUE_MAGIC 0xA0000000u
#define VIVID_PER_OBJECT_USER_VALUE_MAGIC_MASK 0xF0000000u
#define VIVID_PER_OBJECT_USER_VALUE_ADDRESS_MASK 0x0FFFFFFFu

struct VividPerObjectContext
{
    uint BaseAddress;
    uint CapacityBytes;
    uint IsValid;
};

uint VividPerObjectGetRendererUserValue()
{
#if defined(DOTS_INSTANCING_ON)
    return 0u;
#elif defined(UNITY_INSTANCING_ENABLED)
    return unity_RendererUserValue;
#else
    return asuint(unity_RenderingLayer.y);
#endif
}

bool VividPerObjectRangeIsValid(VividPerObjectContext context, uint relativeOffset, uint byteCount)
{
    if (context.IsValid == 0u || relativeOffset > context.CapacityBytes)
        return false;

    const uint remainingAfterOffset = context.CapacityBytes - relativeOffset;
    if (byteCount > remainingAfterOffset)
        return false;

    return context.BaseAddress <= remainingAfterOffset - byteCount;
}

VividPerObjectContext VividPerObjectCreateContextFromUserValue(
    uint rendererUserValue,
    uint layoutSignature)
{
    VividPerObjectContext context;
    context.BaseAddress = 0u;
    _VividPerObjectBuffer.GetDimensions(context.CapacityBytes);
    context.IsValid = 0u;

    if ((rendererUserValue & VIVID_PER_OBJECT_USER_VALUE_MAGIC_MASK) != VIVID_PER_OBJECT_USER_VALUE_MAGIC)
        return context;

    const uint baseAddress = (rendererUserValue & VIVID_PER_OBJECT_USER_VALUE_ADDRESS_MASK) << 4u;
    if (baseAddress < 16u || baseAddress > context.CapacityBytes)
        return context;
    if (context.CapacityBytes - baseAddress < 4u)
        return context;
    if (_VividPerObjectBuffer.Load(baseAddress) != layoutSignature)
        return context;

    context.BaseAddress = baseAddress;
    context.IsValid = 1u;
    return context;
}

VividPerObjectContext VividPerObjectCreateContext(uint layoutSignature)
{
    return VividPerObjectCreateContextFromUserValue(
        VividPerObjectGetRendererUserValue(),
        layoutSignature);
}

int VividPerObjectLoadInt(VividPerObjectContext context, uint relativeOffset, int defaultValue)
{
    return VividPerObjectRangeIsValid(context, relativeOffset, 4u)
        ? asint(_VividPerObjectBuffer.Load(context.BaseAddress + relativeOffset))
        : defaultValue;
}

float VividPerObjectLoadFloat(VividPerObjectContext context, uint relativeOffset, float defaultValue)
{
    return VividPerObjectRangeIsValid(context, relativeOffset, 4u)
        ? asfloat(_VividPerObjectBuffer.Load(context.BaseAddress + relativeOffset))
        : defaultValue;
}

float4 VividPerObjectLoadFloat4(VividPerObjectContext context, uint relativeOffset, float4 defaultValue)
{
    return VividPerObjectRangeIsValid(context, relativeOffset, 16u)
        ? asfloat(_VividPerObjectBuffer.Load4(context.BaseAddress + relativeOffset))
        : defaultValue;
}

float4x4 VividPerObjectLoadFloat4x4(
    VividPerObjectContext context,
    uint relativeOffset,
    float4x4 defaultValue)
{
    if (!VividPerObjectRangeIsValid(context, relativeOffset, 64u))
        return defaultValue;

    const uint address = context.BaseAddress + relativeOffset;
    const float4 column0 = asfloat(_VividPerObjectBuffer.Load4(address));
    const float4 column1 = asfloat(_VividPerObjectBuffer.Load4(address + 16u));
    const float4 column2 = asfloat(_VividPerObjectBuffer.Load4(address + 32u));
    const float4 column3 = asfloat(_VividPerObjectBuffer.Load4(address + 48u));
    return transpose(float4x4(column0, column1, column2, column3));
}

#endif
