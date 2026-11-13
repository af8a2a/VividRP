#ifndef VIVID_SDF_MATERIAL_SIDEDNESS_INCLUDED
#define VIVID_SDF_MATERIAL_SIDEDNESS_INCLUDED

// Unity CullMode: Off=0, Front=1, Back=2. Use the original material normal,
// not the artificial SDF shell gradient or the face-oriented lighting normal.
bool VividSdfMaterialSideVisible(float cullMode, float3 originalNormal, float3 outgoingDirection)
{
    float facing = dot(originalNormal, outgoingDirection);
    return cullMode == 0.0 || (cullMode == 1.0 ? facing < 0.0 : facing > 0.0);
}

#endif
