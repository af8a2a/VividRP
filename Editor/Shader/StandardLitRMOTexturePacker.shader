Shader "Hidden/VividRP/Editor/StandardLit RMO Texture Packer"
{
    Properties
    {
        [HideInInspector] _RoughnessMap("Roughness", 2D) = "white" {}
        [HideInInspector] _MetallicMap("Metallic", 2D) = "white" {}
        [HideInInspector] _AmbientOcclusionMap("Ambient Occlusion", 2D) = "white" {}
        [HideInInspector] _RoughnessChannelMask("Roughness Channel", Vector) = (1, 0, 0, 0)
        [HideInInspector] _MetallicChannelMask("Metallic Channel", Vector) = (1, 0, 0, 0)
        [HideInInspector] _AmbientOcclusionChannelMask("AO Channel", Vector) = (1, 0, 0, 0)
        [HideInInspector] _RoughnessTransform("Roughness Transform", Vector) = (1, 0, 1, 0)
        [HideInInspector] _MetallicTransform("Metallic Transform", Vector) = (1, 0, 0, 0)
        [HideInInspector] _AmbientOcclusionTransform("AO Transform", Vector) = (1, 0, 1, 0)
    }

    CGINCLUDE

        #include "UnityCG.cginc"
        #pragma editor_sync_compilation
        #pragma target 3.0

        sampler2D _RoughnessMap;
        sampler2D _MetallicMap;
        sampler2D _AmbientOcclusionMap;

        float4 _RoughnessChannelMask;
        float4 _MetallicChannelMask;
        float4 _AmbientOcclusionChannelMask;

        // x: scale, y: invert, z: fallback, w: source assigned
        float4 _RoughnessTransform;
        float4 _MetallicTransform;
        float4 _AmbientOcclusionTransform;

        float ResolveChannel(float4 sampleValue, float4 channelMask, float4 transform)
        {
            float value = dot(sampleValue, channelMask) * transform.x;
            value = lerp(value, 1.0 - value, transform.y);
            return saturate(lerp(transform.z, value, transform.w));
        }

        float4 Frag(v2f_img input) : SV_Target
        {
            float roughness = ResolveChannel(
                tex2D(_RoughnessMap, input.uv),
                _RoughnessChannelMask,
                _RoughnessTransform);
            float metallic = ResolveChannel(
                tex2D(_MetallicMap, input.uv),
                _MetallicChannelMask,
                _MetallicTransform);
            float ambientOcclusion = ResolveChannel(
                tex2D(_AmbientOcclusionMap, input.uv),
                _AmbientOcclusionChannelMask,
                _AmbientOcclusionTransform);
            return float4(roughness, metallic, ambientOcclusion, 1.0);
        }

    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM

                #pragma vertex vert_img
                #pragma fragment Frag

            ENDCG
        }
    }

    Fallback Off
}
