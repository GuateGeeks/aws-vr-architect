Shader "GuateGeeks/Quetzal"
{
    // The digital quetzal's plumage: lit vertex colours on double-sided geometry, with uv0.x marking iridescent feathers
    // and uv0.y self-lit (emissive) areas. Iridescent feathers shift from emerald toward blue-teal at grazing angles and
    // carry a golden-green sheen, as the resplendent quetzal's do. A cyan rim and a slow scan band mark it as a hologram.
    // Vertex alpha 0 gives a feather a pale underside on its back face.
    // Lighting is view-relative (like the AWS emblems) so the bird reads from below, beside or above. Reduced-motion clock.
    Properties { _Color ("Tint", Color) = (1,1,1,1) _Underside ("Flight feather underside", Color) = (0.73, 0.75, 0.76, 1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; half4 color:COLOR; float2 uv:TEXCOORD2; float3 local:TEXCOORD3; float3 localNormal:TEXCOORD4; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color, _Underside;
            CBUFFER_END
            float _LabAnimationTime;
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(a.normalOS); o.color = a.color; o.uv = a.uv; o.local = a.positionOS.xyz; o.localNormal = a.normalOS; return o;
            }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normal) * (front ? 1 : -1), v = GetWorldSpaceNormalizeViewDir(i.world);
                // Key light above-left of the eye; safe when looking straight up at the bird.
                float3 side = cross(float3(0, 1, 0), -v);
                side = dot(side, side) > 1e-4 ? normalize(side) : float3(1, 0, 0);
                float3 key = normalize(v * .6 + float3(0, .75, 0) - side * .3);
                float diffuse = saturate(dot(n, key)), fill = saturate(dot(n, normalize(v + side * .5))) * .35;
                float nv = saturate(dot(n, v));
                // Vertex alpha 0 marks flight feathers: their back face shows the pale grey underside of a quetzal's wing.
                half3 base = (front ? i.color.rgb : lerp(_Underside.rgb, i.color.rgb, i.color.a)) * _Color.rgb;
                // Iridescence: emerald turns blue-teal toward grazing angles.
                float grazing = pow(1 - nv, 1.6);
                half3 irid = lerp(base, half3(.02, .26, .42) * (.5 + base.g), grazing * .55);
                base = lerp(base, irid, i.uv.x);
                // Feather scales: a staggered scallop pattern, projected along the dominant local axis, faded before it aliases.
                float3 ln = abs(i.localNormal);
                float2 sp = (ln.x > ln.y ? i.local.yz : i.local.xz) * 68;
                sp.x += floor(sp.y) * .5;
                float2 cellPos = frac(sp) - float2(.5, .3);
                float scale = smoothstep(.32, .58, length(cellPos * float2(1, 1.3)));
                float scaleFade = saturate(1.5 - max(fwidth(sp.x), fwidth(sp.y)) * 1.8) * i.uv.x;
                base *= lerp(1, lerp(1.03, .88, scale), scaleFade);
                float spec = pow(saturate(dot(n, normalize(v + key))), 26);
                half3 sheen = lerp(half3(1, 1, 1), half3(.95, .88, .35), i.uv.x);
                half3 lit = base * (.18 + diffuse * .62 + fill * .5) + spec * sheen * (.16 + .34 * i.uv.x);
                half3 col = lerp(lit, i.color.rgb * 1.15, saturate(i.uv.y));
                float rim = pow(1 - nv, 3);
                float band = pow(saturate(1 - abs(frac(i.world.y * 3 - _LabAnimationTime * .4) - .5) * 14), 2);
                col += half3(.30, .85, .95) * (rim * .06 + band * .04) * (1 - saturate(i.uv.y));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
