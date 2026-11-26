Shader "Blacktheon/Cable Feedback"
{
    Properties { _Color ("Tint", Color) = (1,0,0,0.64) }
    SubShader
    {
        Tags { "Queue"="Overlay+50" "RenderType"="Transparent" "DisableBatching"="True" }
        Pass
        {
            Cull Off ZTest Always ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct Vertex { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Fragment { float4 vertex : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Fragment vert(Vertex v)
            {
                Fragment o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Fill the viewport independently for each eye, including asymmetric Quest projections.
                o.vertex = float4(v.vertex.xy * 2.0, 0.5, 1.0);
                return o;
            }
            fixed4 frag(Fragment i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return _Color;
            }
            ENDCG
        }
    }
}
