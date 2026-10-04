Shader "GuateGeeks/LabMetal"
{
    Properties { _Color ("Titanium tint", Color) = (0.12,0.2,0.26,1) }
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
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            V vert(A a) {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world=TransformObjectToWorld(a.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(a.normalOS); return o;
            }
            half4 frag(V i):SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n=normalize(i.normal), v=GetWorldSpaceNormalizeViewDir(i.world);
                float key=saturate(dot(n,normalize(float3(-.4,.85,-.6))));
                float edge=pow(1-saturate(abs(dot(n,v))),3);
                float spec=pow(saturate(dot(n,normalize(v+normalize(float3(-.4,.85,-.6))))),38);
                return half4(_Color.rgb*(.42+key*.85)+half3(.12,.32,.4)*edge+spec*.25,1);
            }
            ENDHLSL
        }
    }
}
