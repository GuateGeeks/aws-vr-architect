Shader "GuateGeeks/Glow"
{
    // Additive radial light pool for flat quads (uv 0..1). Used for the floor glow under the projection console.
    Properties { _Color ("Glow", Color) = (0.05,0.5,0.5,1) _Falloff ("Falloff", Float) = 2.4 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }
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
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color; float _Falloff;
            CBUFFER_END
            float _LabAnimationTime;
            V vert(A a) { V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(a.positionOS.xyz); o.uv = a.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float r = length(i.uv - .5) * 2;
                float pool = pow(saturate(1 - r), _Falloff);
                float breathe = .9 + .1 * sin(_LabAnimationTime * .6);
                return half4(_Color.rgb * pool * breathe * _Color.a, 1);
            }
            ENDHLSL
        }
    }
}
