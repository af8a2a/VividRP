namespace UnityEngine.Rendering.Universal
{
    // Global Constant Buffers - b registers. Unity supports a maximum of 16 global constant buffers.
    enum ConstantRegister
    {
        Global = 0,
        XR = 1,
        PBRSky = 2,
        RayTracing = 3,
        RayTracingLightLoop = 4,
        WorldEnvLightReflectionData = 5,
        APV = APVConstantBufferRegister.GlobalRegister,
    }

    //// TODO:
    //[GenerateHLSL(needAccessors = false, generateCBuffer = true, constantRegister = (int)ConstantRegister.Global)]
    //unsafe struct ShaderVariablesGlobal
    //{

    //}
}

