Shader "GuateGeeks/Hologram"
{
    Properties { _Color ("Projection color", Color) = (0.2,0.8,1,1) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            float _LabAnimationTime;
            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float rim = pow(1 - saturate(abs(dot(normalize(i.normalWS), GetWorldSpaceNormalizeViewDir(i.positionWS)))), 2.2);
                float phase = i.positionWS.y * 42 - _LabAnimationTime * .65;
                float band = abs(frac(phase) - .5);
                float lines = 1 - smoothstep(.10, .10 + max(fwidth(phase), .035), band);
                float scan = pow(saturate(1 - abs(frac(i.positionWS.y * .75 - _LabAnimationTime * .13) - .5) * 15), 2);
                half3 color = _Color.rgb * (.48 + .55 * rim + .28 * lines) + scan * .16;
                return half4(color, saturate(.07 + rim * .50 + lines * .12 + scan * .10) * _Color.a);
            }
            ENDHLSL
        }
    }
}
