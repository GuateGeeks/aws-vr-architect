Shader "GuateGeeks/Panorama"
{
    // Procedural sky dome around the lab, drawn on one inward-facing sphere so every direction is filled: overhead, the
    // ceiling openings and the full 360° horizon. A Guatemalan night: teal aurora, the Milky Way, twinkling stars, a
    // moon and a passing satellite above holographic volcano ranges (Agua, Acatenango, Fuego with its glowing summit,
    // Pacaya and the Atitlán trio) drawn as terrain scans, Guatemala City's skyline behind the lab and valley lights
    // below the horizon. No textures. Animation uses the shared reduced-motion clock, so it freezes with "Animación: NO".
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
            float hash3(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float wrapDeg(float a) { return a - 360 * floor((a + 180) / 360); }
            // Anti-aliased line at every integer of x.
            float gridLine(float x, float width) { float w = min(fwidth(x), .25); return 1 - smoothstep(width, width + w * 1.2, abs(frac(x + .5) - .5)); }
            // Stratovolcano profile: broad concave base steepening toward a small summit crater, with eroded flanks.
            float volcano(float az, float centre, float width, float height)
            {
                float delta = wrapDeg(az - centre), d = abs(delta) / width;
                float p = height * pow(saturate(1 - d), 2.1);
                p = min(p, height * .965);
                p += height * (.025 * sin(delta * 1.9 + centre) + .012 * sin(delta * 4.3 + centre * 2)) * saturate(1 - d);
                return max(p, 0);
            }
            float ridge(float ar, float seed) { return .16 + .10 * sin(ar * 5 + seed) + .06 * sin(ar * 11 + seed * 2.3) + .03 * sin(ar * 29 + seed * 4.1); }
            float nearRange(float az)
            {
                float ar = radians(az);
                float agua = volcano(az, -76, 30, 3.0);
                float acatenango = volcano(az, 66, 26, 3.1), fuego = volcano(az, 81, 13, 2.6);
                float pacaya = volcano(az, -146, 18, 1.5);
                // Lake Atitlán's volcanoes over the right shoulder: Tolimán, Atitlán and San Pedro.
                float toliman = volcano(az, 128, 15, 1.9), atitlan = volcano(az, 141, 17, 2.4), sanPedro = volcano(az, 158, 14, 1.8);
                float range = max(max(agua, acatenango), max(fuego, pacaya));
                return max(range, max(max(toliman, atitlan), max(sanPedro, ridge(ar, 4.2) * .75)));
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _LabAnimationTime;
                float3 d = normalize(i.local);
                float az = degrees(atan2(d.x, d.z));                 // 0 = straight ahead of the lab
                float ar = radians(az);
                float el = asin(clamp(d.y, -1, 1));
                // Height on the former 8.4 m horizon band (metres above the horizon) keeps the ranges' proportions.
                float y = .15 + 8.4 * tan(clamp(el, -1.2, 1.25));
                // Sky: teal haze at the horizon, deep navy overhead and almost black at the zenith.
                float up = saturate(y / 6);
                half3 sky = lerp(half3(.016, .075, .09), half3(.005, .012, .028), pow(up, .55));
                sky = lerp(sky, half3(.002, .006, .016), smoothstep(.55, 1, d.y));
                // Faint nebulae give the dome depth: one teal, one violet.
                sky += half3(.010, .030, .036) * exp(-dot(d - float3(.35, .82, .45), d - float3(.35, .82, .45)) / .12);
                sky += half3(.022, .012, .034) * exp(-dot(d - float3(-.55, .70, -.45), d - float3(-.55, .70, -.45)) / .09);
                // The Milky Way: a soft band on a tilted great circle with clumps and a dust lane.
                float3 n = normalize(float3(.62, .42, -.66));
                float3 b1 = normalize(cross(n, float3(0, 1, 0))), b2 = cross(n, b1);
                float across = dot(d, n), along = atan2(dot(d, b1), dot(d, b2));
                float band = exp(-across * across / .028);
                float clumps = .55 + .45 * sin(along * 5 + 1.3) * sin(along * 11 - .7);
                float lane = 1 - .65 * exp(-pow((across - .015 - .012 * sin(along * 7)) / .03, 2));
                sky += half3(.045, .075, .095) * band * clumps * lane * saturate(d.y * 4 + .2);
                // Aurora ribbons echo the logo swooshes: one teal, one leaf green, slowly drifting.
                float r1 = exp(-pow((y - 2.9 - .7 * sin(ar * 2 + t * .16) - .15 * sin(ar * 7 - t * .4)) / .26, 2));
                float r2 = exp(-pow((y - 3.6 - .6 * sin(ar * 2 + 1.7 + t * .12) - .12 * sin(ar * 9 + t * .33)) / .16, 2));
                float fadeAz = .55 + .45 * sin(ar * 3 + t * .09);
                float curtain = .55 + .45 * sin(ar * 52 + t * .55 + y * 1.5) * sin(ar * 13 - t * .21);
                sky += (half3(.02, .30, .30) * r1 * .24 * fadeAz + half3(.20, .45, .14) * r2 * .15 * (1 - fadeAz * .6)) * curtain;
                // Drifting high cloud wisps catch the teal haze.
                float cloud = smoothstep(.55, .95, .5 + .5 * sin(ar * 10 + t * .05) * sin(ar * 23 - t * .035 + y * .8)) * exp(-pow((y - 2.0) / .5, 2));
                sky = lerp(sky, half3(.04, .12, .14), cloud * .35);
                // Digital data streams: sparse columns of light rising into the sky.
                float lane2 = floor(az * .6), laneHash = hash(float2(lane2, 5.7));
                if (laneHash > .82 && y > .3 && y < 5.5)
                {
                    float dash = frac(y * 1.2 - t * (.25 + laneHash * .4) + laneHash * 10);
                    float seg = smoothstep(0, .05, dash) * (1 - smoothstep(.12, .2, dash));
                    float acrossLane = abs(frac(az * .6) - .5);
                    sky += half3(.05, .45, .45) * seg * (1 - smoothstep(.02, .06, acrossLane)) * .22 * (1 - y / 5.5);
                }
                // Stars on a direction lattice (uniform over the whole dome, no seams), denser along the Milky Way.
                {
                    float3 sp = d * 95, cell = floor(sp);
                    float h = hash3(cell);
                    float3 jitter = float3(hash3(cell + 1.7), hash3(cell + 4.3), hash3(cell + 8.1)) - .5;
                    float3 starDir = normalize(cell + .5 + jitter * .6);
                    float dist = length(d - starDir) * 95;
                    float on = step(.952 - band * .03, h);
                    float size = .012 + .02 * hash3(cell + 2.2);
                    float star = on * exp(-dist * dist / size) * saturate((y - 1.0) * .5);
                    star *= .55 + .45 * sin(t * (1.2 + hash3(cell + 7) * 2.5) + h * 40);
                    half3 starColor = lerp(half3(.72, .88, 1), half3(1, .86, .7), step(.8, hash3(cell + 5.5)));
                    sky += star * starColor * (1 + 1.4 * hash3(cell + 3.3));
                }
                // A waxing gibbous moon high over the left of the console, with a soft halo and earthshine.
                {
                    float3 m = normalize(float3(-.50, .62, .60));
                    float md = length(d - m); const float R = .028;
                    float w = max(fwidth(md), .0004);
                    if (md < .3)
                    {
                        float disc = 1 - smoothstep(R - w, R + w, md);
                        float3 t1 = normalize(cross(m, float3(0, 1, 0))), t2 = cross(t1, m);
                        float2 lp = float2(dot(d - m, t1), dot(d - m, t2)) / R;
                        float shadow = 1 - smoothstep(-.06, .06, length(lp - float2(-1.45, .25)) - 1.05);
                        float maria = 1 - .22 * smoothstep(.45, .85, .5 + .5 * sin(lp.x * 3.1 + 1) * sin(lp.y * 2.7 - .4));
                        maria *= 1 - .12 * smoothstep(.6, .9, .5 + .5 * sin(lp.x * 7.3 - lp.y * 5.1));
                        sky = lerp(sky, half3(.86, .95, 1) * maria * (1 - shadow * .9) + half3(.02, .05, .06) * shadow, disc);
                        sky += half3(.10, .24, .27) * exp(-max(md - R, 0) / .035) * (1 - disc) * .55;
                    }
                }
                // A satellite crosses the dome on a tilted orbit.
                {
                    float3 oa = normalize(float3(.97, .12, .2)), ob = float3(-.15, .95, .27);
                    ob = normalize(ob - dot(ob, oa) * oa);
                    float phase = t * .05;
                    float3 sat = cos(phase) * oa + sin(phase) * ob;
                    float3 ds = d - sat;
                    sky += half3(.85, .95, 1) * exp(-dot(ds, ds) / 3.5e-6) * saturate((sat.y - .08) * 8) * .9;
                }
                // A meteor every ~7 s somewhere in the upper sky.
                {
                    float cycle = t / 7, id = floor(cycle), life = frac(cycle) * 3;
                    if (life < 1)
                    {
                        float dirSign = hash(float2(id, 3.3)) > .5 ? 1 : -1;
                        float2 dm = normalize(float2(dirSign, -.38));
                        float2 start = float2((hash(float2(id, 1.3)) - .5) * 300 * .147, 4.2 + hash(float2(id, 7.1)) * 2.2);
                        float2 rel = float2(az * .147, y) - (start + dm * life * 3.2);
                        float alongM = dot(rel, -dm), acrossM = dot(rel, float2(-dm.y, dm.x));
                        float tail = saturate(1 - alongM / .9) * step(0, alongM);
                        sky += half3(.7, .95, 1) * exp(-acrossM * acrossM / .0004) * tail * (1 - life) * 1.2;
                    }
                }
                half3 col = sky;
                // Line patterns are evaluated outside the branches below so their screen-space derivatives stay defined.
                float farLine = gridLine(y * 3, .02), contour = gridLine(y * 2.2, .035), meridian = gridLine(az / 1.5, .03);
                // Far haze range, all the way around, with a faint scan contour.
                float far = ridge(ar, 1.3) * 1.5 + volcano(az, 110, 26, 1.0) + volcano(az, -20, 30, .8) + volcano(az, -112, 22, .9) + volcano(az, 178, 30, .7);
                if (y < far)
                {
                    col = lerp(half3(.016, .058, .068), sky, .45);
                    col += half3(.03, .22, .24) * farLine * .12;
                }
                // Near volcanoes as holographic terrain scans: relief shading, contours, meridian lines and a lit ridge.
                float near = nearRange(az);
                if (y < near)
                {
                    float slope = nearRange(az + .6) - nearRange(az - .6);
                    float light = saturate(.55 - slope * 1.6);
                    float h = saturate(max(y, 0) / max(near, .001));
                    half3 rock = lerp(half3(.004, .014, .018), half3(.013, .05, .058), light) * (.7 + .5 * h);
                    float gully = pow(saturate(sin(ar * 155 + y * 3.1) * .5 + .5), 6) * h;
                    col = rock + half3(.04, .30, .32) * (contour * .22 + meridian * (.35 + .65 * h) * .10 + gully * .05) * (.3 + .7 * h);
                    float edge = 1 - smoothstep(0, .05, near - y);
                    col += half3(.08, .58, .58) * edge * .5 * (.7 + .3 * sin(ar * 29 - t * 1.2));
                    // A holographic scan line climbs the slopes.
                    float scanY = frac(t * .06) * 4.2;
                    col += half3(.10, .70, .70) * exp(-pow((y - scanY) / .05, 2)) * .45 * h;
                    col = lerp(col, half3(.014, .05, .06), exp(-max(y, 0) * 1.4) * .5);   // valley haze at the base
                }
                // Guatemala City behind the lab: a skyline of lit towers in front of the far ranges.
                float dc = wrapDeg(az - 180);
                float city = saturate(1 - abs(dc) / 28);
                if (city > 0 && y >= 0 && y < 1.3)
                {
                    float bid = floor(dc * 2.4), bh = hash(float2(bid, 4.4));
                    float height = (.10 + .62 * pow(bh, 3)) * (.35 + .65 * city);
                    if (y < height)
                    {
                        float2 win = float2(dc * 24, y * 34);
                        float lit = step(.66, hash(floor(win) + bid)) * smoothstep(.15, .3, abs(frac(win.x) - .5)) * smoothstep(.12, .3, abs(frac(win.y) - .5));
                        lit *= .75 + .25 * sin(t * .3 + hash(floor(win)) * 30);
                        half3 windowColor = lerp(half3(1, .72, .38), half3(.55, .9, 1), step(.6, hash(floor(win) + 9.1)));
                        col = half3(.005, .018, .024) + windowColor * lit * .5;
                        col += half3(.06, .5, .52) * (1 - smoothstep(0, .02, height - y)) * .6;   // lit roofline
                        col += half3(.04, .3, .32) * smoothstep(.88, .98, abs(frac(dc * 2.4) - .5) * 2) * .25;   // lit tower edges
                    }
                    // Aviation lights blink on the tallest towers.
                    float2 top = float2((frac(dc * 2.4) - .5) * 2, (y - height - .02) * 25);
                    col += half3(1, .12, .1) * exp(-dot(top, top) * 6) * step(.86, bh) * step(.5, frac(t * .5 + bh * 3)) * city;
                }
                // Fuego is active: a warm summit glow and a slow, drifting plume, with periodic eruptions.
                float erupt = pow(saturate(sin(t * .21)), 12);
                float summitY = volcano(81, 81, 13, 2.6) + .04;
                float2 dp = float2(wrapDeg(az - 81) * .147, y - summitY);
                col += half3(1, .45, .12) * exp(-dot(dp, dp) / .012) * (.75 + .25 * sin(t * 3.1)) * (1 + 2 * erupt);
                float rise = max(dp.y, 0);
                float plume = exp(-pow((dp.x - rise * .35 - sin(rise * 2 - t * .4) * .06) / (.08 + rise * .09), 2)) * saturate(dp.y * 4) * exp(-rise * .8);
                col += half3(.28, .15, .10) * plume * .4 * (1 + 1.5 * erupt);
                float ember = step(.97, hash(floor(float2(dp.x * 30, (dp.y - t * .8) * 12)))) * erupt * saturate(1 - rise * 1.2) * exp(-dp.x * dp.x / .02) * step(0, dp.y);
                col += half3(1, .5, .15) * ember;
                // Valley mist along the foot of the ranges and a thin holographic horizon line all the way round.
                float mist = (.5 + .5 * sin(ar * 18 - t * .11)) * (.5 + .5 * sin(ar * 44 + t * .07 + 1.3));
                if (y >= 0) col = lerp(col, half3(.03, .11, .12), mist * exp(-y * 2.2) * .45);
                col += half3(.04, .38, .40) * exp(-y * y / .0012) * .35;
                // Below the horizon: the dark valley floor scattered with town lights, densest toward the city.
                if (y < 0)
                {
                    col = lerp(half3(.006, .02, .026), half3(.003, .008, .012), saturate(-y));
                    float2 g = float2(ar * 160, y * 22), c = floor(g), f = frac(g) - .5;
                    float2 o = float2(hash(c + 2.1), hash(c + 6.3)) - .5;
                    float on = step(.975 - city * .12, hash(c + 11.3));
                    float l = on * exp(-dot(f - o * .6, f - o * .6) / .02) * saturate(-y * 4) * saturate(1 + y * .5);
                    col += lerp(half3(1, .62, .3), half3(.6, .9, 1), step(.7, hash(c + 3.3))) * l * .55;
                }
                return half4(col * _Color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
