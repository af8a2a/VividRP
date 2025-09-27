
namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Shading models used for GBuffer and Deferred shading.
    /// </summary>
    [GenerateHLSL]
    internal enum ShadingModels
    {
        // Bits [0,3] are reserved for users. For users stencil custom usage.
        UserMask        = 0b_0000_1111,
        UserMaskBits    = 4,

        // Bits [4,6] are used for shading models. So we can have different models up to 15.
        // Zero is none shading model default stencil value. So we need to remove Unlit.
        ModelsMask      = 0b_1111_0000,
        // Unlit           = 0b_0000_0000,
        Lit             = 0b_0010_0000,

        // !!!IMPORTANT!!! add/remove shading models must change tihs value.
        CurModelsNum    = 1,
        MaxModelsNum    = 15,
    }
}
