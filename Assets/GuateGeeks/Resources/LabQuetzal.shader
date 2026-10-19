Shader "GuateGeeks/Quetzal"
{
    // The digital quetzal as shipped in 0.24.3/0.24.4: reconstructed instruction by instruction from the Vulkan programs
    // of the production shader asset (Recovery/0.24.3-qa/QUETZAL-REFERENCE.md). One material, two passes per part:
    // QuetzalDepth writes the part's depth (skipping self-lit surfaces), then QuetzalHologram draws it translucent and
    // premultiplied on top.
    // Vertex data (QuetzalMesh.Paint): colour (alpha 0 = pale underside on the back face), uv0 = (iridescence, self-lit),
    // uv1 = (hair, tip). hair > 1.5 marks two-sided filaments; tip fades feather ends.
    // The plumage shows a feather-scale pattern on iridescent feathers (68 cells per metre, staggered rows, a darker
    // edge on each scale), gold-tinted iridescent highlights, wrap-lit hair, scanlines, a slow sweep band, flicker,
    // horizontal glitch slices (_Glitch), a radial dissolve from _RevealCenter (_Reveal, metres), a glow under the
    // touching finger (_TouchPoint, w = strength) and a ring from the last touch (_TouchRipple, w = age in seconds).
    Properties {
        _Color ("Tint", Color) = (1,1,1,1)
        _Underside ("Flight feather underside", Color) = (0.73, 0.75, 0.76, 1)
        _Holo ("Projection light", Color) = (0.36, 0.88, 0.95, 1)
        _Opacity ("Plumage opacity", Range(0,1)) = 0.86
        _Reveal ("Reveal radius (m)", Float) = 1000
        _RevealCenter ("Reveal centre (world)", Vector) = (0,0,0,0)
        _Glitch ("Glitch", Range(0,1)) = 0
        _Highlight ("Attention highlight", Range(0,1)) = 0
        _TouchPoint ("Touch point (world, strength)", Vector) = (0,0,0,0)
        _TouchRipple ("Touch ripple (world, age)", Vector) = (0,0,0,10)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _Color, _Underside, _Holo;
        float4 _RevealCenter, _TouchPoint, _TouchRipple;
        float _Opacity, _Reveal, _Glitch, _Highlight;
        CBUFFER_END
        float _LabAnimationTime;
        float Hash13(float3 p3)
        {
            p3 = frac(p3 * float3(.1031, .1030, .0973));
            p3 += dot(p3, p3.yxz + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }
        // Glitch: 34 slices per metre, re-rolled 24 times a second; a picked slice shifts sideways (across the view).
        float3 Glitched(float3 world)
        {
            float row = floor(world.y * 34), frame = floor(_LabAnimationTime * 24);
            float jump = _Glitch * .4 >= Hash13(float3(row, frame, 7.1)) ? 1 : 0;
            float shift = (Hash13(float3(row, frame, 3.3)) - .5) * jump * _Glitch * .05;
            float3 toEye = _WorldSpaceCameraPos - world;
            float3 across = float3(toEye.z, 0, -toEye.x);
            float len = dot(across.xz, across.xz);
            across = len > 1e-6 ? across * rsqrt(len) : float3(1, 0, 0);
            return world + across * shift;
        }
        // Radial dissolve with a grainy front (70 cells per metre).
        float RevealGap(float3 world)
        {
            return _Reveal - distance(world, _RevealCenter.xyz) - Hash13(floor(world * 70)) * .05;
        }
        ENDHLSL

        Pass
        {
            Name "QuetzalDepth"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ColorMask 0
            ZWrite On ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float emit:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 world = Glitched(TransformObjectToWorld(a.positionOS.xyz));
                o.world = world; o.positionCS = TransformWorldToHClip(world); o.emit = a.uv.y;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(RevealGap(i.world));
                clip(.6 - i.emit);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "QuetzalHologram"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; float2 uv:TEXCOORD0; float2 uv1:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; half4 color:COLOR;
                float2 uv:TEXCOORD2; float3 local:TEXCOORD3; float3 localNormal:TEXCOORD4; float2 uv1:TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 world = Glitched(TransformObjectToWorld(a.positionOS.xyz));
                o.world = world; o.positionCS = TransformWorldToHClip(world);
                o.normal = TransformObjectToWorldNormal(a.normalOS); o.color = a.color; o.uv = a.uv; o.uv1 = a.uv1;
                o.local = a.positionOS.xyz; o.localNormal = a.normalOS;
                return o;
            }
            half4 frag(V i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 paint = float4(i.uv, i.uv1);              // iridescence, self-lit, hair, tip
                float gap = RevealGap(i.world);
                clip(gap);
                float forming = 1 - saturate(gap * 28.571428);     // the bright dissolve front

                // View-relative light: key above and to the side of the eye, a fill from the other side.
                float3 v = GetWorldSpaceNormalizeViewDir(i.world);
                float3 side = float3(-v.z, 0, v.x);
                float sideLen = dot(side.xz, side.xz);
                side = sideLen > 1e-4 ? side * rsqrt(sideLen) : float3(1, 0, 0);
                float3 key = normalize(v * .6 + float3(0, .75, 0) - side * .3);
                float3 halfway = normalize(key + v);
                float3 fillDir = normalize(side * .5 + v);
                float3 n = normalize(i.normal) * ((paint.z > 1.5 || frontFace) ? 1 : -1);

                float spec = pow(saturate(dot(n, halfway)), 26);
                float4 specTint = paint.x * float4(-.05, -.12, -.65, .34) + float4(1, 1, 1, .16);
                float3 specular = spec * specTint.rgb * specTint.a;
                float fill = saturate(dot(n, fillDir));
                float nl = dot(n, key);
                float nv = saturate(dot(n, v));
                float rimSoft = pow(1 - nv, 1.6), rim = pow(1 - nv, 2.4);
                float hair = saturate(paint.z), emit = saturate(paint.y);
                float wrap = saturate((nl + hair * .6) / (hair * .6 + 1)) * .62 + .2;
                float light = fill * .175 + wrap;

                half3 base = frontFace ? i.color.rgb : lerp(_Underside.rgb, i.color.rgb, i.color.a);
                float greenLift = base.g * _Color.g + .5;
                base *= _Color.rgb;
                // Iridescence: toward blue-green at grazing angles.
                half3 albedo = lerp(base, greenLift * half3(.03, .3, .38), paint.x * rimSoft * .45);

                // Feather scales on iridescent plumage, projected along the dominant local axis, faded before they alias.
                float2 sp = (abs(i.localNormal.y) < abs(i.localNormal.x) ? i.local.yz : i.local.xz) * 68;
                sp.x += floor(sp.y) * .5;
                float widthX = abs(ddx(sp.x)) + abs(ddy(sp.x));
                float widthY = abs(ddx(sp.y)) + abs(ddy(sp.y));
                float r = length((frac(sp) - float2(.5, .3)) * float2(1, 1.3));
                float3 rings = saturate((r - float3(.32, .5, .62)) * float3(3.8461538, 12.5, 12.5));
                rings = rings * rings * (3 - 2 * rings);
                float scaleFade = saturate(1.5 - max(widthX, widthY) * 1.8) * paint.x * (1 - hair);
                float scaleEdge = scaleFade * (1 - rings.z) * rings.y;
                albedo *= 1 + scaleFade * (.03 - .15 * rings.x);

                half3 col = albedo * light + specular;
                col = lerp(col, i.color.rgb * 1.15, emit);
                float luma = dot(col, float3(.299, .587, .114));
                half3 hue = albedo / max(max(max(albedo.b, albedo.g), albedo.r), .001);
                half3 holoHue = lerp(_Holo.rgb, hue, .55);
                col = lerp(col, luma * holoHue * 1.6, .08);
                col = max(col, holoHue * .035 + albedo * .22);

                // Hologram: scanlines, glitch tint, flicker, a slow sweep band.
                float4 t = _LabAnimationTime * float4(.9, .32, 37, 11.3);
                float s = i.world.y * 260 - t.x;
                float sWidth = abs(ddx(s)) + abs(ddy(s));
                float lineEdge = saturate((abs(frac(s) - .5) - .2) / max(sWidth, .02));
                float scan = saturate(1.6 - sWidth * 1.3) * (1 - lineEdge * lineEdge * (3 - 2 * lineEdge));
                col *= 1 + scan * _Glitch * float3(.3, -.1, .25);
                col *= sin(i.world.y * 3 + t.z) * .035 * sin(t.w) + .965;
                float band = max(1 - abs(frac(i.world.y * .9 - t.y) - .5) * 9, 0);
                float2 scanShade = scan * float2(.04, .07) + float2(.96, .95);
                col *= scanShade.y;
                float alpha = saturate(rim * .3 + _Opacity + emit * .3 + forming) * scanShade.x;
                float glow = forming * 1.8 + rim * (_Highlight * .9 + .42) + band * band * band * .2 + _Highlight * .05;
                half3 emission = _Holo.rgb * glow + (scan * .045 + scaleEdge * .12) * holoHue;

                // Touch: a glow under the finger and a ring spreading from the last contact.
                float age = _TouchRipple.w;
                float ring = max(1 - abs(distance(i.world, _TouchRipple.xyz) - age * .32) * 111.11111, 0) * saturate(1 - age * 1.6666666) * .8;
                float touchDist = distance(i.world, _TouchPoint.xyz);
                float touch = exp2(-touchDist * touchDist * 2019.7731) * _TouchPoint.w;
                emission += _Holo.rgb * (touch * .7 + ring);

                // Feather tips fade out.
                float tip = saturate((paint.w - .35) * 1.5384616);
                tip = tip * tip * (3 - 2 * tip);
                emission *= 1 - .7 * tip;
                alpha *= 1 - .9 * tip;
                return half4(col * alpha + emission, alpha);
            }
            ENDHLSL
        }
    }
}
