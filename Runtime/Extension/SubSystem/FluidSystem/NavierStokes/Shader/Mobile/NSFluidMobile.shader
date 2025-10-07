Shader "Hidden/NSFluid_Mobile"
{
    SubShader
    {
        ZWrite Off ZTest Always Blend Off Cull Off
        HLSLINCLUDE
        #pragma target 4.5
        // #pragma enable_d3d11_debug_symbols

        #include "NSFluidMobile.hlsl"
        ENDHLSL

        Pass//0 Advect
        {
            Name "Advect"
            HLSLPROGRAM
            #pragma vertex vert_common
            #pragma fragment frag_advect
            ENDHLSL
        }
        Pass//1 Diffusion
        {
            Name "Diffusion"
            HLSLPROGRAM
            #pragma vertex vert_neighbor
            #pragma fragment frag_diffusion
            ENDHLSL
        }
        Pass//2 Force
        {
            Name "Force"
            HLSLPROGRAM
            #pragma vertex vert_common
            #pragma fragment frag_force
            ENDHLSL
        }
        Pass//3 Divergence
        {
            Name "Divergence"
            HLSLPROGRAM
            #pragma vertex vert_neighbor
            #pragma fragment frag_divergence
            ENDHLSL
        }
        Pass//4 Presure
        {
            Name "Pressure"
            HLSLPROGRAM
            #pragma vertex vert_neighbor
            #pragma fragment frag_pressure
            ENDHLSL
        }
        Pass//5 Gradient
        {
            Name "Gradient"
            HLSLPROGRAM
            #pragma vertex vert_neighbor
            #pragma fragment frag_gradient
            ENDHLSL
        }
        //        Pass // 6 Generate Normal
        //        {
        //            Name "Normal"
        //            HLSLPROGRAM
        //            #pragma vertex vert_neighbor
        //            #pragma fragment frag_Normal
        //            ENDHLSL
        //        }
        // Pass//6 layInVectorSield
        // {
        //     Name "layInVectorSield"
        //     HLSLPROGRAM
        //     #pragma vertex vert_neighbor
        //     #pragma fragment frag_layInVectorSield
        //     ENDHLSL
        // }  
        // Pass//7 layInVectorSield
        // {
        //     Name "TestlayInVectorSield"
        //     HLSLPROGRAM
        //     #pragma vertex vert_neighbor
        //     #pragma fragment frag_TestlayInVectorSield
        //     ENDHLSL
        // }
        // Pass//8 ResetlayInVectorSield
        // {
        //     Name "ResetlayInVectorSield"
        //     HLSLPROGRAM
        //     #pragma vertex vert_neighbor
        //     #pragma fragment frag_ResetlayInVectorSield
        //     ENDHLSL
        // }
        // Pass//8 Fade
        // {
        //     Name "Fade"
        //     HLSLPROGRAM
        //     #pragma vertex vert_neighbor
        //     #pragma fragment frag_Fade
        //     ENDHLSL
        // }
    }
}