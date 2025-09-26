

//--------------------------------------------------------------------------------------------------
// Global fetch data
//--------------------------------------------------------------------------------------------------

StructuredBuffer<GPULightData> g_GPULightDatas;



GPULightData FetchLight(uint index)
{
    return g_GPULightDatas[index];
}




//--------------------------------------------------------------------------------------------------
// Directional light data
//--------------------------------------------------------------------------------------------------
uint _DirectionalLightCount;
StructuredBuffer<DirectionalLightData> g_DirectionalLightDatas;
