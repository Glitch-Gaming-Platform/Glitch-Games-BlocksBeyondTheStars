// Lit opaque shader for the block atlas (M27 renderer). Samples the atlas and shades each face by
// the real sun direction (normal . sun) in the per-system sun colour, using two globals set by Sky:
//   _Sc_Light  = system sun colour x day brightness x weather dim (a>0.5 marks it as set)
//   _Sc_SunDir = world-space direction TO the sun
// Built-in render pipeline; no per-light passes (robust, no ForwardBase dependency).
Shader "Spacecraft/BlockAtlas"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Sc_Light;   // system sun colour x day brightness x weather (a>0.5 = set)
            float4 _Sc_SunDir;  // world-space direction TO the sun

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wn = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 albedo = tex2D(_MainTex, i.uv).rgb;
                fixed3 light = (_Sc_Light.a < 0.5) ? fixed3(1, 1, 1) : _Sc_Light.rgb;

                float ndl = saturate(dot(normalize(i.wn), normalize(_Sc_SunDir.xyz)));
                // Ambient floor + directional term: faces toward the sun are brighter, shaded faces
                // keep a tinted ambient so they never go black. Coloured by the system sun (light).
                fixed3 col = albedo * light * (0.55 + 0.55 * ndl);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }

    Fallback "Spacecraft/VertexColorOpaque"
}
