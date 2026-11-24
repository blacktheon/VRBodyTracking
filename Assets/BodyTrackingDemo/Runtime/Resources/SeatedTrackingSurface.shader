Shader "Blacktheon/Seated Tracking Surface"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct Vertex
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Fragment
            {
                float4 vertex : SV_POSITION;
                half shade : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Fragment vert(Vertex v)
            {
                Fragment o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                half3 normal = UnityObjectToWorldNormal(v.normal);
                o.shade = 0.72h + 0.28h * saturate(dot(normal, half3(-0.35h, 0.82h, -0.45h)));
                return o;
            }
            fixed4 frag(Fragment i) : SV_Target { return fixed4(_Color.rgb * i.shade, 1); }
            ENDCG
        }
    }
}
