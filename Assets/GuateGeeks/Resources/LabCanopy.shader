Shader "GuateGeeks/Canopy"
{
    // Holographic ceiling canopy: a hexagonal light lattice on a surface of revolution over the arches
    // (uv.x runs around the room, uv.y from the pillar heads in to the projector hub). Thin anti-aliased lines, a faint
    // glass tint, cells that light up like data packets and a power wave that travels outward from the hub.
    // Additive, so the sky dome shows through. Animation uses the shared reduced-motion clock.
    Properties
    {
        _Color ("Lattice", Color) = (0.1, 0.55, 0.6, 1)
        _Cells ("Cells around, along", Vector) = (72, 13, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-60" "RenderPipeline"="UniversalPipeline" }
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
            half4 _Color; float4 _Cells;
            CBUFFER_END
            float _LabAnimationTime;
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz); o.uv = a.uv; return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _LabAnimationTime;
                const float2 r = float2(1, 1.7320508);
                // Integer cells around the room keep the lattice seamless; the offset keeps fmod positive.
                float2 p = i.uv * _Cells.xy + r * 4;
                float2 a = fmod(p, r) - r * .5;
                float2 b = fmod(p - r * .5, r) - r * .5;
                float2 gv = dot(a, a) < dot(b, b) ? a : b;
                float2 id = round((p - gv) / (r * .5));                           // exact integer cell id (no per-pixel hash noise)
                float edge = .5 - max(dot(abs(gv), normalize(r)), abs(gv.x));   // 0 on the hexagon outline
                float w = fwidth(edge);
                float lattice = (1 - smoothstep(.011, .011 + w * 1.5, edge)) * saturate(1.3 - w * 14);
                // Data packets: a few cells fill with light and fade, each on its own schedule.
                float cellHash = hash(id);
                float phase = t * .3 + cellHash * 7;
                float packet = step(.955, hash(id + floor(phase) * 1.37)) * (1 - frac(phase)) * smoothstep(0, .2, edge);
                // Power wave from the hub (uv.y = 1) out to the pillar heads (uv.y = 0).
                float wave = exp(-pow((1 - i.uv.y) - frac(t * .07) * 1.2 + .1, 2) / .003);
                float fade = smoothstep(0, .28, i.uv.y) * smoothstep(1, .93, i.uv.y);   // quiet near the pillar heads, strongest overhead
                half3 c = _Color.rgb * (lattice * (.25 + 1.3 * wave) + .016 + packet * .24) * fade;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
