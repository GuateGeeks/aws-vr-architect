Shader "GuateGeeks/Emblem"
{
    // Lit vertex-coloured solid for AWS service emblems. Vertex alpha marks self-lit areas (the white symbol);
    // a cyan rim and a slow scan band tie the solid object into the holographic projection.
    Properties { _Color ("Tint", Color) = (1,1,1,1) }
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
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; half4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            float _LabAnimationTime;
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(a.normalOS); o.color = a.color; return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normal), v = GetWorldSpaceNormalizeViewDir(i.world);
                // View-relative lighting: the emblem turns to face whoever looks at it, so its key light follows the viewer
                // (above-left of the eye) and the relief always reads, from any side of the table.
                float3 right = normalize(cross(float3(0, 1, 0), -v));
                float3 key = normalize(v * .75 + float3(0, .7, 0) - right * .35), fillDir = normalize(v * .8 + right * .6);
                float diffuse = saturate(dot(n, key)), fill = saturate(dot(n, fillDir)) * .3;
                float spec = pow(saturate(dot(n, normalize(v + key))), 56) * .45;
                float rim = pow(1 - saturate(abs(dot(n, v))), 3);
                half3 base = i.color.rgb * _Color.rgb;
                half3 lit = base * (.36 + diffuse * .78 + fill) + spec;
                half3 col = lerp(lit, base * 1.04, i.color.a);
                float band = pow(saturate(1 - abs(frac(i.world.y * 2.4 - _LabAnimationTime * .32) - .5) * 16), 2);
                col += half3(.36, .88, .95) * (rim * .32 + band * .10);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
