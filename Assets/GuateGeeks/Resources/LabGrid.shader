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
            float Lines(float2 uv)
            {
                float2 d = abs(frac(uv - .5) - .5) / max(fwidth(uv), .0001);
                return 1 - saturate(min(d.x, d.y));
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = i.world.xz;
                float2 q = p - float2(0, 2.65);
                float radius = length(q);
                // Fine 25 cm grid close to the table, 1 m major grid further out; both fade with distance.
                float fine = Lines(p * 4) * (1 - smoothstep(1.8, 5.5, radius));
                float major = Lines(p) * (1 - smoothstep(3, 11, radius));
                // Twelve radial spokes with light pulses running inward to the projection table.
                float sector = atan2(q.x, q.y) / (PI / 6);
                float spokeDist = abs(frac(sector + .5) - .5) * (PI / 6) * radius;
                float spoke = (1 - smoothstep(.004, .018, spokeDist)) * smoothstep(2.05, 2.5, radius) * (1 - smoothstep(6.5, 8.5, radius));
                float phase = frac(radius * .16 + _LabAnimationTime * .22 + floor(sector + .5) * .37);
                float pulse = pow(phase, 14) * spoke;
                float ring = 1 - smoothstep(.008, .025, abs(radius - 4.85));
                float inner = 1 - smoothstep(.006, .02, abs(radius - 2.15));
                float aisle = 1 - smoothstep(.01, .035, abs(abs(p.x) - 2.55));
                float guide = aisle * (1 - smoothstep(4, 8, abs(p.y - 2.65)));
                half3 col = _Color.rgb;
                col += half3(.012, .05, .06) * fine + half3(.02, .08, .095) * major;
                col += half3(.04, .30, .36) * (ring * .5 + inner * .35 + guide * .22 + spoke * .12);
                col += half3(.10, .62, .66) * pulse;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
