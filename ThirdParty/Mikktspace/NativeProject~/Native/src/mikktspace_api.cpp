#include "mikktspace_api.h"

DLL_EXPORT tbool DLL_API genMikktTangSpace(const SMikkTSpaceContext * pContext, const float fAngularThreshold)
{
    return genTangSpace(pContext, fAngularThreshold);
}