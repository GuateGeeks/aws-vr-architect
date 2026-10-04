Shader "GuateGeeks/ArchitectGrid"
{
    Properties { _Color ("Floor", Color) = (0.012,0.024,0.04,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            float _LabAnimationTime;
            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = TransformObjectToWorld(v.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.world); return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.world.xz * 2;
                float2 d = abs(frac(uv - .5) - .5) / max(fwidth(uv), .0001);
                float grid = 1 - saturate(min(d.x, d.y));
                float radius = length(i.world.xz - float2(0,2.65));
                float fade = 1 - smoothstep(3, 12, radius);
                float ring=1-smoothstep(.008,.025,abs(radius-4.85));
                float sweep=pow(saturate(1-abs(radius-fmod(_LabAnimationTime*.3,12))*.9),5)*.08;
                float aisle=1-smoothstep(.01,.035,abs(abs(i.world.x)-2.55));
                float guide=aisle*(1-smoothstep(4,8,abs(i.world.z-2.65)));
                return half4(_Color.rgb + half3(.018,.07,.085)*grid*fade + half3(.04,.30,.36)*(ring*.5+guide*.28+sweep*fade),1);
            }
            ENDHLSL
        }
    }
}
