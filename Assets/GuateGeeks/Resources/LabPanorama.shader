Shader "GuateGeeks/Panorama"
{
    // Procedural 360° horizon around the lab: a night sky over a holographic Guatemalan volcano range
    // (Agua, Acatenango and Fuego with its glowing summit), drawn on one inward-facing cylinder. No textures.
    // Animation uses the shared reduced-motion clock, so stars, plume and aurora freeze with "Animación: NO".
    Properties { _Color ("Tint", Color) = (1,1,1,1) _Horizon ("Horizon height", Float) = 1.05 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 local:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color; float _Horizon;
            CBUFFER_END
            float _LabAnimationTime;
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.local = a.positionOS.xyz; o.positionCS = TransformObjectToHClip(a.positionOS.xyz); return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            // Stratovolcano profile: broad concave base steepening toward a small summit crater.
            float volcano(float az, float centre, float width, float height)
            {
                float d = abs(az - centre) / width;
                float p = height * pow(saturate(1 - d), 2.1);
                float crater = height * .965;
                p = min(p, crater) - (p > crater ? .0 : 0);
                // Erosion: small ridges and gullies along the flanks.
                p += height * .025 * sin(az * 1.9 + centre) * saturate(1 - d) + height * .012 * sin(az * 4.3 + centre * 2);
                return max(p, 0);
            }
            float ridge(float az, float seed)
            {
                return .16 + .10 * sin(radians(az) * 5 + seed) + .06 * sin(radians(az) * 11 + seed * 2.3) + .03 * sin(radians(az) * 29 + seed * 4.1);
            }
            float nearRange(float az)
            {
                float agua = volcano(az, -76, 30, 3.0);
                float acatenango = volcano(az, 66, 26, 3.1), fuego = volcano(az, 81, 13, 2.6);
                float pacaya = volcano(az, -148, 18, 1.5);
                return max(max(max(agua, acatenango), max(fuego, pacaya)), ridge(az, 4.2) * .75);
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _LabAnimationTime;
                float az = degrees(atan2(i.local.x, i.local.z));          // 0 = straight ahead of the lab
                float y = i.local.y - _Horizon;                             // metres above the horizon on the 8.4 m band
                // Sky: deep navy overhead, teal haze at the horizon (the logo's palette).
                float up = saturate(y / 6);
                half3 sky = lerp(half3(.016, .075, .09), half3(.005, .012, .028), pow(up, .55));
                // Aurora ribbons echo the logo swooshes: one teal, one leaf green, slowly drifting.
                float r1 = exp(-pow((y - 2.9 - .7 * sin(radians(az) * 2 + t * .16) - .15 * sin(radians(az) * 7 - t * .4)) / .26, 2));
                float r2 = exp(-pow((y - 3.6 - .6 * sin(radians(az) * 2 + 1.7 + t * .12) - .12 * sin(radians(az) * 9 + t * .33)) / .16, 2));
                float fadeAz = .55 + .45 * sin(radians(az) * 3 + t * .09);
                // Curtain folds ripple through the aurora so it visibly moves.
                float curtain = .55 + .45 * sin(az * .9 + t * .55 + y * 1.5) * sin(az * .23 - t * .21);
                sky += (half3(.02, .30, .30) * r1 * .24 * fadeAz + half3(.20, .45, .14) * r2 * .15 * (1 - fadeAz * .6)) * curtain;
                // Drifting high cloud wisps catch the teal haze.
                float cloud = smoothstep(.55, .95, .5 + .5 * sin(az * .17 + t * .05) * sin(az * .41 - t * .035 + y * .8)) * exp(-pow((y - 2.0) / .5, 2));
                sky = lerp(sky, half3(.04, .12, .14), cloud * .35);
                // Digital data streams: sparse columns of light rising into the sky.
                float lane = floor(az * .6), laneHash = hash(float2(lane, 5.7));
                if (laneHash > .82 && y > .3 && y < 5.5)
                {
                    float dash = frac(y * 1.2 - t * (.25 + laneHash * .4) + laneHash * 10);
                    float seg = smoothstep(0, .05, dash) * (1 - smoothstep(.12, .2, dash));
                    float across = abs(frac(az * .6) - .5);
                    sky += half3(.05, .45, .45) * seg * (1 - smoothstep(.02, .06, across)) * .22 * (1 - y / 5.5);
                }
                // Point stars (one candidate per cell, small round core), twinkling slowly.
                float2 g = float2(az * 1.4, y * 7);
                float2 cell = floor(g), f = frac(g) - .5;
                float2 offset = float2(hash(cell + 3.1), hash(cell + 9.7)) - .5;
                float on = step(.965, hash(cell));
                float dist = length((f - offset * .7) * float2(1, .9));
                float star = on * exp(-dist * dist / .006) * saturate((y - 1.2) * .7);
                star *= .55 + .45 * sin(t * (1.2 + hash(cell + 7) * 2.5) + hash(cell) * 40);
                sky += star * half3(.75, .9, 1) * 1.4;
                // A meteor every ~7 s somewhere in the upper sky.
                {
                    float cycle = t / 7, id = floor(cycle), life = frac(cycle) * 3;
                    if (life < 1)
                    {
                        float dirSign = hash(float2(id, 3.3)) > .5 ? 1 : -1;
                        float2 d = normalize(float2(dirSign, -.38));
                        float2 start = float2((hash(float2(id, 1.3)) - .5) * 300 * .147, 4.2 + hash(float2(id, 7.1)) * 2.2);
                        float2 rel = float2(az * .147, y) - (start + d * life * 3.2);
                        float along = dot(rel, -d), across = dot(rel, float2(-d.y, d.x));
                        float tail = saturate(1 - along / .9) * step(0, along);
                        sky += half3(.7, .95, 1) * exp(-across * across / .0004) * tail * (1 - life) * 1.2;
                    }
                }
                half3 col = sky;
                // Far haze range.
                float far = ridge(az, 1.3) * 1.5 + volcano(az, 130, 26, 1.0) + volcano(az, -20, 30, .8);
                if (y < far) col = lerp(half3(.016, .058, .068), sky, .45);
                // Near volcanoes with relief shading from the west, contour lines and a luminous ridge.
                float near = nearRange(az);
                if (y < near)
                {
                    float slope = nearRange(az + .6) - nearRange(az - .6);
                    float light = saturate(.55 - slope * 1.6);
                    float h = saturate(max(y, 0) / max(near, .001));
                    half3 rock = lerp(half3(.004, .014, .018), half3(.013, .05, .058), light) * (.7 + .5 * h);
                    float contour = 1 - smoothstep(0, .035, abs(frac(y * 2.2) - .5) * 2 - .93);
                    float gully = pow(saturate(sin(az * 2.7 + y * 3.1) * .5 + .5), 6) * h;
                    col = rock + half3(.04, .30, .32) * (contour * .22 + gully * .05) * (.3 + .7 * h);
                    float edge = 1 - smoothstep(0, .05, near - y);
                    col += half3(.08, .58, .58) * edge * .5 * (.7 + .3 * sin(az * .5 - t * 1.2));
                    // A holographic scan line climbs the slopes.
                    float scanY = frac(t * .06) * 4.2;
                    col += half3(.10, .70, .70) * exp(-pow((y - scanY) / .05, 2)) * .45 * h;
                    col = lerp(col, half3(.014, .05, .06), exp(-max(y, 0) * 1.4) * .5);   // valley haze at the base
                }
                // Valley mist drifting along the foot of the range.
                float mist = (.5 + .5 * sin(az * .31 - t * .11)) * (.5 + .5 * sin(az * .77 + t * .07 + 1.3));
                if (y >= 0) col = lerp(col, half3(.03, .11, .12), mist * exp(-y * 2.2) * .45);
                // Fuego is active: a warm summit glow and a slow, drifting plume, with periodic eruptions.
                float erupt = pow(saturate(sin(t * .21)), 12);
                float summitY = volcano(81, 81, 13, 2.6) + .04;
                float2 dp = float2((az - 81) * .147, y - summitY);
                col += half3(1, .45, .12) * exp(-dot(dp, dp) / .012) * (.75 + .25 * sin(t * 3.1)) * (1 + 2 * erupt);
                float rise = max(dp.y, 0);
                float plume = exp(-pow((dp.x - rise * .35 - sin(rise * 2 - t * .4) * .06) / (.08 + rise * .09), 2)) * saturate(dp.y * 4) * exp(-rise * .8);
                col += half3(.28, .15, .10) * plume * .4 * (1 + 1.5 * erupt);
                float ember = step(.97, hash(floor(float2(dp.x * 30, (dp.y - t * .8) * 12)))) * erupt * saturate(1 - rise * 1.2) * exp(-dp.x * dp.x / .02) * step(0, dp.y);
                col += half3(1, .5, .15) * ember;
                if (y < 0) col = lerp(half3(.006, .02, .026), half3(.003, .008, .012), saturate(-y));
                return half4(col * _Color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
