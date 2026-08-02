#ifndef VIVIDRP_SURFACE_SAMPLING_INCLUDED
#define VIVIDRP_SURFACE_SAMPLING_INCLUDED

#if defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS) && defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
    #error "VividSurfaceSampling requires exactly one GPUDriven texture backend."
#elif defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS)
    #include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/BindlessSurfaceSampling.hlsl"
#elif defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
    #error "The Virtual Texture GPUDriven sampling backend is not implemented."
#else
    #error "VividSurfaceSampling requires a GPUDriven texture backend macro."
#endif

#endif // VIVIDRP_SURFACE_SAMPLING_INCLUDED
