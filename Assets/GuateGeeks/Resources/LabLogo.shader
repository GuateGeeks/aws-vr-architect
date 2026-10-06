Shader "GuateGeeks/Logo"
{
    // The GuateGeeks wordmark as emitted light, drawn from a signed-distance texture (R: distance, +/-64 texels around
    // the outline, positive inside; G: 1 where the nearest letter belongs to "Geeks"; B: a wide falloff for the glow pool).
    // Letters stay crisp at any distance, get a bright inner contour, a soft halo and a slow light sweep, all without a
    // bloom pass. Additive over the dark table glass. Animation uses the shared reduced-motion clock.
    Properties
    {
        _MainTex ("Distance field", 2D) = "black" {}
        _Guate ("Guate", Color) = (0.79, 0.96, 1, 1)
        _Geeks ("Geeks", Color) = (0.14, 0.71, 0.9, 1)
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-40" "RenderPipeline"="UniversalPipeline" }
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; half4 _Guate, _Geeks; float _Intensity;
            CBUFFER_END
            float _LabAnimationTime;
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz); o.uv = a.uv; return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 s = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float d = (s.r - .5) * 128;                                   // texels, positive inside the letters
                float aa = max(fwidth(d), .001);
                float fill = smoothstep(-aa, aa, d);
                float contour = saturate(1 - abs(d - 1.6) / (aa + 1.3)) * fill; // bright line just inside each edge
                float halo = exp(-max(-d, 0) / 10) * (1 - fill);
                float pool = s.b * s.b;
                half3 tint = lerp(_Guate.rgb, _Geeks.rgb, saturate(s.g));
                // A diagonal light sweep crosses the wordmark every 9 s.
                float sweepPos = frac(_LabAnimationTime / 9) * 1.9 - .45;
                float sweep = exp(-pow((i.uv.x + i.uv.y * .35 - sweepPos) / .05, 2));
                // Fine scan lines inside the letters, faded out before they could shimmer at distance.
                float lines = i.uv.y * 360;
                float lineFade = saturate(1.5 - fwidth(lines) * 2.5);
                float scan = 1 - .22 * lineFade * smoothstep(.35, .65, abs(frac(lines) - .5) * 2);
                half3 col = tint * (fill * (.40 + .85 * sweep) * scan + contour * .8 + halo * (.30 + .45 * sweep) + pool * .05);
                return half4(col * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}
