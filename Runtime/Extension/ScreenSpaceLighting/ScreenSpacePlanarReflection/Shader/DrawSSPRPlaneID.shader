Shader "Hidden/DrawSSPRPlaneID"
{
    SubShader
    {
        LOD 100

        Pass
        {
            Tags { "LightMode" = "DrawSSPRPlaneID"}
            name "DrawSSPRPlaneID"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _PlaneID;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert (Attributes input)
            {
                Varyings output;

                output.positionCS = TransformObjectToHClip(input.positionOS);

                return output;
            }

            half frag (Varyings input) : SV_Target
            {
                return _PlaneID;
            }
            ENDHLSL
        }
    }
}
