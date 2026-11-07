Shader "Hidden/VividRP/Debug/MeshSdfViz"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "MeshSdfViz"
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

            Texture3D<float> _SdfDistanceField;
            float4 _SdfSampleOrigin;
            float4 _SdfGrid;
            float4 _SdfScreenRect;
            float4x4 _SdfLocalToClip;
            float4x4 _SdfClipToLocal;

            static const float2 QuadCorners[6] =
            {
                float2(0.0, 0.0), float2(1.0, 0.0), float2(0.0, 1.0),
                float2(0.0, 1.0), float2(1.0, 0.0), float2(1.0, 1.0)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 positionNdc : TEXCOORD0;
            };

            Varyings Vert(uint vertexId : SV_VertexID)
            {
                Varyings output;
                output.positionNdc = lerp(_SdfScreenRect.xy, _SdfScreenRect.zw, QuadCorners[vertexId]);
                output.positionCS = float4(output.positionNdc, 0.5, 1.0);
                return output;
            }

            float3 UnprojectLocal(float2 positionNdc, float clipDepth)
            {
                float4 local = mul(_SdfClipToLocal, float4(positionNdc, clipDepth, 1.0));
                return local.xyz / local.w;
            }

            bool IntersectGrid(float3 rayOrigin, float3 rayDirection, float3 gridExtent, out float t, out float tExit)
            {
                t = 0.0;
                tExit = FLT_MAX;
                [unroll]
                for (int axis = 0; axis < 3; axis++)
                {
                    float direction = rayDirection[axis];
                    if (abs(direction) < 1e-8)
                    {
                        if (rayOrigin[axis] < 0.0 || rayOrigin[axis] > gridExtent[axis])
                            return false;
                        continue;
                    }

                    float t0 = -rayOrigin[axis] / direction;
                    float t1 = (gridExtent[axis] - rayOrigin[axis]) / direction;
                    t = max(t, min(t0, t1));
                    tExit = min(tExit, max(t0, t1));
                }
                return t <= tExit;
            }

            float SampleDistance(float3 positionInGrid)
            {
                // SampleOrigin is the center of voxel zero, in mesh-local units.
                float3 uvw = (positionInGrid / _SdfGrid.w + 0.5) / _SdfGrid.xyz;
                return _SdfDistanceField.SampleLevel(sampler_LinearClamp, uvw, 0);
            }

            float3 EstimateGradient(float3 positionInGrid)
            {
                float2 stepSize = float2(_SdfGrid.w, 0.0);
                return float3(
                    SampleDistance(positionInGrid + stepSize.xyy) - SampleDistance(positionInGrid - stepSize.xyy),
                    SampleDistance(positionInGrid + stepSize.yxy) - SampleDistance(positionInGrid - stepSize.yxy),
                    SampleDistance(positionInGrid + stepSize.yyx) - SampleDistance(positionInGrid - stepSize.yyx));
            }

            float4 Frag(Varyings input, out float deviceDepth : SV_Depth) : SV_Target
            {
                float3 nearLocal = UnprojectLocal(input.positionNdc, UNITY_NEAR_CLIP_VALUE);
                // Use an interior clip depth so this also works with an infinite far plane.
                float midDepth = 0.5 * (UNITY_NEAR_CLIP_VALUE + UNITY_RAW_FAR_CLIP_VALUE);
                float3 rayDirection = normalize(UnprojectLocal(input.positionNdc, midDepth) - nearLocal);
                float3 rayOrigin = nearLocal - _SdfSampleOrigin.xyz;
                float3 gridExtent = (_SdfGrid.xyz - 1.0) * _SdfGrid.w;
                float t, tExit;
                if (!IntersectGrid(rayOrigin, rayDirection, gridExtent, t, tExit))
                    discard;

                float tolerance = 0.075 * _SdfGrid.w;
                float3 positionInGrid;
                bool hit = false;
                [loop]
                for (int step = 0; step < 192 && t <= tExit; step++)
                {
                    positionInGrid = rayOrigin + t * rayDirection;
                    float distance = abs(SampleDistance(positionInGrid));
                    if (distance <= tolerance)
                    {
                        hit = true;
                        break;
                    }
                    // Use a reduced step for the trilinear field, including when tracing from inside.
                    t += distance * 0.7;
                }
                if (!hit)
                    discard;

                float4 positionCS = mul(_SdfLocalToClip, float4(positionInGrid + _SdfSampleOrigin.xyz, 1.0));
                deviceDepth = positionCS.z / positionCS.w;
                #if !UNITY_REVERSED_Z
                    deviceDepth = (deviceDepth - UNITY_NEAR_CLIP_VALUE) / (1.0 - UNITY_NEAR_CLIP_VALUE);
                #endif
                if (deviceDepth < 0.0 || deviceDepth > 1.0)
                    discard;

                float3 gradient = EstimateGradient(positionInGrid);
                float facing = abs(dot(SafeNormalize(gradient), rayDirection));
                float3 tint = lerp(float3(0.12, 0.75, 1.0), float3(1.0, 0.48, 0.13), _SdfSampleOrigin.w);
                return float4(tint * (0.28 + 0.72 * facing), 1.0);
            }
            ENDHLSL
        }
    }
}
