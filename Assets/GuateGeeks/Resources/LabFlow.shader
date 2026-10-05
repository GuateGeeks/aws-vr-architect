Shader "GuateGeeks/Flow"
{
    // Additive energy beam for LineRenderers: soft core across the width and dashes travelling along it.
    // Uses the shared reduced-motion clock, so dashes freeze when decorative motion is disabled.
    Properties { _Color ("Beam color", Color) = (0.36,0.88,0.95,1) _Dashes ("Dashes", Float) = 7 _Speed ("Speed", Float) = .55 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color; float _Dashes; float _Speed;
            CBUFFER_END
            float _LabAnimationTime;
            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; o.color = v.color; return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float across = saturate(1 - abs(i.uv.y - .5) * 2);
                float core = pow(across, 3), halo = across * across;
                float phase = frac(i.uv.x * _Dashes - _LabAnimationTime * _Speed);
                float dash = smoothstep(0, .12, phase) * (1 - smoothstep(.28, .42, phase));
                float ends = smoothstep(0, .04, i.uv.x) * smoothstep(1, .96, i.uv.x);
                half3 rgb = _Color.rgb * (.55 + core * 1.1 + dash * core * .9) + core * .18;
                half alpha = saturate(halo * .35 + core * .55 + dash * core * .45) * ends * _Color.a * i.color.a;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
