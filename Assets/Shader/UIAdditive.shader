// UI 위에 "빛을 더하는" 셰이더.
//
// 기본 UI 셰이더는 알파 블렌딩(Blend SrcAlpha OneMinusSrcAlpha)이라 위에 얹은 그림이
// 아래를 "덮어버린다" - 흰색을 얹으면 흰 판이 그대로 보인다. 여기는 Blend SrcAlpha One 이라
// 아래 색에 더해지기만 해서, 투명한 곳은 아무 일도 없고 밝은 곳만 밝아진다.
// 게이지 테두리가 스스로 빛나는 연출처럼 "덮는 게 아니라 밝아지는" 표현에 쓴다.
//
// 마스크(Mask/RectMask2D) 안에서도 정상 동작하도록 UI 공통 프로퍼티(Stencil, ClipRect)를 그대로 둔다.
Shader "JojoPuzzle/UI Additive"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 빛이 안쪽으로 얼마나 깊이 들어왔는지. 0이면 가장 바깥 테두리만, 1이면 그라데이션 전체.
        // 텍스처의 알파에 "가장자리에서 얼마나 안쪽인지"가 이미 구워져 있어서(가장자리=1, 안쪽=0),
        // 이 값으로 잘라내면 빛이 안으로 스며들었다 물러나는 것처럼 보인다.
        _GlowDepth ("Glow Depth", Range(0,1)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One          // <- 여기가 핵심. 덮지 않고 더한다.
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _GlowDepth;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 tex = tex2D(_MainTex, IN.texcoord);

                // 알파에 구워둔 "가장자리로부터의 거리"를 깊이로 잘라낸다.
                // depth 가 작으면 알파가 아주 높은 곳(=가장 바깥 테두리)만 남고,
                // 1에 가까워질수록 안쪽 그라데이션까지 드러난다.
                half depth = max(_GlowDepth, 0.001);
                tex.a = saturate((tex.a - (1.0 - depth)) / depth);

                half4 color = tex * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
