Shader "GuateGeeks/Shaft"
{
    // Projection light from the ceiling emitter: a cone (uv.y = 0 at the lens) that is brightest at the lens and gone
    // by mid-room, so it never washes over the table. Silhouette edges fade by view angle; soft bands descend slowly.
    // Additive. Animation uses the shared reduced-motion clock.
    Properties { _Color ("Light", Color) = (0.05, 0.4, 0.45, 1) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-55" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            float _LabAnimationTime;
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS); o.uv = a.uv; return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float facing = abs(dot(normalize(i.normalWS), GetWorldSpaceNormalizeViewDir(i.positionWS)));
                float fall = pow(saturate(1 - i.uv.y * 1.6), 3);
                float bands = .7 + .3 * sin(i.uv.y * 38 - _LabAnimationTime * 1.4);
                return half4(_Color.rgb * fall * facing * facing * bands, 1);
            }
            ENDHLSL
        }
    }
}
