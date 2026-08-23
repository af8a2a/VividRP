#ifndef VIVIDRP_GPU_DRIVEN_MATERIAL_SURFACE_INCLUDED
#define VIVIDRP_GPU_DRIVEN_MATERIAL_SURFACE_INCLUDED

bool VividIsStandardSingleSlabSurfaceProgramCompatible(
    const VividMaterialProgramData programData,
    const uint requiredCapabilities)
{
    return programData.Version == VIVID_MATERIAL_PROGRAM_VERSION
        && programData.SurfaceProgramID
            == VIVIDMATERIALSURFACEPROGRAMID_STANDARD_SINGLE_SLAB
        && programData.ParameterLayoutID
            == VIVIDMATERIALPARAMETERLAYOUTID_LEGACY_MATERIAL_DATA
        && programData.ResourceLayoutID
            == VIVIDMATERIALRESOURCELAYOUTID_LEGACY_SURFACE_BINDING
        && (programData.CapabilityFlags & requiredCapabilities)
            == requiredCapabilities
        && programData.ExecutionClass
            == VIVIDMATERIALEXECUTIONCLASS_VISIBILITY_DEFERRED;
}

bool VividTryLoadStandardSingleSlabSurfaceProgram(
    const uint materialIndex,
    const uint requiredCapabilities,
    out VividMaterialData materialData,
    out VividSurfaceBindingData surfaceBindingData)
{
    materialData = (VividMaterialData) 0;
    surfaceBindingData = (VividSurfaceBindingData) 0;
    if (materialIndex >= _MaterialRuntimeHeaderCount)
        return false;

    const VividMaterialRuntimeHeader runtimeHeader =
        PullMaterialRuntimeHeader(materialIndex);
    if (runtimeHeader.ProgramID == VIVIDMATERIALPROGRAMID_INVALID
        || runtimeHeader.ProgramID >= _MaterialProgramCount)
    {
        return false;
    }

    const VividMaterialProgramData programData =
        PullMaterialProgramData(runtimeHeader.ProgramID);
    if (!VividIsStandardSingleSlabSurfaceProgramCompatible(
            programData,
            requiredCapabilities)
        || runtimeHeader.ParameterAddress >= _MaterialDataCount
        || runtimeHeader.ResourceBindingAddress >= _SurfaceBindingDataCount)
    {
        return false;
    }

    materialData = PullMaterialData(runtimeHeader.ParameterAddress);
    surfaceBindingData = PullSurfaceBindingData(
        runtimeHeader.ResourceBindingAddress);
    return true;
}

#endif // VIVIDRP_GPU_DRIVEN_MATERIAL_SURFACE_INCLUDED
