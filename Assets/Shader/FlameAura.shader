// 셀(플랫) 스타일 불꽃 오라.
//
// 구성 방식: "반지름을 흔드는" 방식(→ 가시 달린 공)을 버리고, 세로 중심선을 따라가는
// 물방울 폭 프로파일로 만든다. 화면의 한 점이 불꽃 안인지 밖인지를 field 값 하나로 계산하고
// 마지막에 step()으로 딱 잘라 단색 실루엣을 낸다.
//
// 기획된 불꽃 규칙 5가지가 각각 아래 코드에 대응한다:
//  1) 물방울 + 지그재그  : profile(아래는 둥글고 위로 갈수록 좁아짐) + 좌우 가장자리를 삼각파로 깎음
//  2) S자형 곡선         : 중심선 cx를 높이에 따라 사인으로 휘게 함(직선 상승 금지)
//  3) 독립된 덩어리 분리 : 본체와 별개로 위로 떠오르며 작아지는 불씨 덩어리를 max로 합침
//  4) 동적인 비대칭성    : 좌/우 가장자리에 서로 다른 위상의 삼각파를 써서 굴곡을 다르게 함
//
// (음성 공간 = 중간에 구멍 뚫기는 기획 판단으로 제거했다. 되살리려면 field를 안쪽에서만
//  깎아내는 처리를 다시 넣으면 되는데, 가장자리까지 깎으면 실루엣이 너덜너덜해지므로
//  saturate(field * k)를 곱해 안쪽으로 한정해야 한다.)
//
// 갈래 3개는 아래에서 겹쳐 하나의 몸통처럼 보이다가, 위로 갈수록 벌어지면서 끝이 쪼개진다.
// 합칠 때 평균이 아니라 max를 쓰는 이유: 평균내면 서로 상쇄돼 뭉개지지만, max면 실루엣이
// 살아있는 채로 겹쳐 보인다.
//
// 텍스처를 전혀 쓰지 않는다 - 노이즈 텍스처 페치도, Simplex/Gradient 노이즈 같은 비싼
// 절차적 계산도 없다. 난수는 sin 없이 도는 해시(Hash)로 뽑는다.
//
// SpriteRenderer(퍼즐 조각)와 UI Image(캐릭터 초상화) 양쪽에서 동작한다.
// LightMode 태그를 달지 않아서 URP에서는 SRPDefaultUnlit으로, Built-in에서는 그냥 unlit으로
// 처리된다(Sprites/Default가 두 파이프라인에서 다 도는 것과 같은 이유).
//
// 색은 머티리얼이 아니라 SpriteRenderer.color / Image.color(정점 색)로 곱해서 정한다.
// 덕분에 조각마다 프레임 색이 달라도 머티리얼 인스턴스나 MaterialPropertyBlock이 전혀 필요 없다
// (UI의 CanvasRenderer는 MaterialPropertyBlock을 지원하지 않아서 이 점이 특히 중요하다).
Shader "JojoPuzzle/FlameAura"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _Color ("색 (보통 흰색 - 실제 색은 렌더러 color로 지정)", Color) = (1, 1, 1, 1)

        // --- 물방울 몸통 ---
        _Width ("불꽃 폭", Range(0, 1.5)) = 0.5
        _TipTaper ("끝 뾰족함 (클수록 물방울처럼 급히 좁아짐)", Range(0.3, 6)) = 1.8
        _BaseRound ("밑동 넓고 둥글게 (0=위아래 뾰족한 나뭇잎, 1=넓은 돔)", Range(0, 1)) = 0.85
        _BaseBlobSize ("밑동 원 크기 (0이면 원 없음)", Range(0, 1.2)) = 0.35
        _BaseBlobY ("밑동 원 높이", Range(-1, 1)) = -0.15
        _BaseBlobSquash ("밑동 원 납작함 (1=정원, 작을수록 납작)", Range(0.2, 2)) = 0.8

        // --- S자형 곡선 (줄기가 직선으로 안 올라가게) ---
        _SAmount ("S자 휘어짐 세기", Range(0, 1)) = 0.28
        _SWaves ("S자 굴곡 수", Range(0.3, 4)) = 1.4
        _SSpeed ("S자 흔들리는 속도", Range(0, 5)) = 1.1

        // --- 끝이 여러 갈래로 쪼개짐 + 지그재그 ---
        _SplitSpread ("갈래 벌어짐 (위로 갈수록)", Range(0, 1)) = 0.38
        _SideBranchScale ("옆 갈래 굵기", Range(0, 1)) = 0.62
        _BranchHeightVar ("갈래 높이 편차", Range(0, 0.6)) = 0.28
        _ZigAmount ("지그재그 세기", Range(0, 1)) = 0.45
        _ZigFreq ("지그재그 촘촘함 (세로 방향)", Range(1, 40)) = 12
        _Detail ("혀 개수 / 잘게 쪼개짐 (가로 방향)", Range(1, 40)) = 8

        // --- 본체에서 분리돼 떠오르는 불씨 ---
        _EmberSize ("불씨 크기", Range(0, 0.4)) = 0.11
        _EmberStartY ("불씨 시작 높이", Range(-1, 1)) = 0.25
        _EmberRise ("불씨 상승 거리", Range(0, 1.5)) = 0.75
        _EmberSpread ("불씨 좌우 퍼짐", Range(0, 1)) = 0.28
        _EmberSpeed ("불씨 반복 속도", Range(0, 10)) = 2.5

        // --- 공통 ---
        _WidthScale ("가로 비율 (작을수록 세로로 길쭉)", Range(0.3, 1.5)) = 0.9
        _Speed ("전체 일렁이는 속도", Range(0, 40)) = 4
        _WindStrength ("바람 세기 (얼마나 기우는지)", Range(0, 1.5)) = 0.35
        _WindSpeed ("바람 왕복 속도 (시계 ↔ 반시계)", Range(0, 5)) = 0.7
        _Offset ("불꽃 위치 오프셋 XY", Vector) = (0, 0, 0, 0)
        _Alpha ("전체 투명도", Range(0, 1)) = 1
        _Seed ("위상 오프셋(수동)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                float  seed   : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float _Width;
            float _TipTaper;
            float _BaseRound;
            float _BaseBlobSize;
            float _BaseBlobY;
            float _BaseBlobSquash;
            float _SAmount;
            float _SWaves;
            float _SSpeed;
            float _SplitSpread;
            float _SideBranchScale;
            float _BranchHeightVar;
            float _ZigAmount;
            float _ZigFreq;
            float _Detail;
            float _EmberSize;
            float _EmberStartY;
            float _EmberRise;
            float _EmberSpread;
            float _EmberSpeed;
            float _WidthScale;
            float _Speed;
            float _WindStrength;
            float _WindSpeed;
            float4 _Offset;
            float _Alpha;
            float _Seed;

            // 0~1 삼각파. 사인과 달리 꼭짓점이 각져서 지그재그 실루엣에 맞는다.
            float Tri(float x)
            {
                return abs(frac(x) * 2.0 - 1.0);
            }

            // 삼각함수 없이 도는 가벼운 해시(0~1). 가지/불씨마다 고정된 난수를 뽑는 용도.
            float Hash(float n)
            {
                float f = frac(n * 0.1031);
                f *= f + 33.33;
                f *= f + f;
                return frac(f);
            }

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.uv = IN.texcoord;
                OUT.color = IN.color;

                // 인스턴스마다 위상을 어긋내기 위한 시드를 오브젝트의 월드 위치에서 뽑는다.
                // 이렇게 하면 여러 조각이 동시에 타올라도 똑같이 일렁이지 않으면서,
                // CPU에서 프로퍼티를 따로 넘길 필요가 없다(머티리얼 인스턴스 0개 유지).
                float2 worldXY = float2(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13);
                OUT.seed = frac(sin(dot(worldXY, float2(12.9898, 78.233))) * 43758.5453) * 6.2831853;

                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 중심 기준 -1 ~ 1 좌표. 가로를 눌러서 세로로 길쭉한 비율을 만든다.
                float2 p = (IN.uv - 0.5) * 2.0;
                p.x /= max(_WidthScale, 0.01);
                p -= _Offset.xy;

                float y01 = saturate(p.y * 0.5 + 0.5); // 아래 0 → 위 1

                // _Time.y는 Time.timeScale의 영향을 받으므로, 일시정지(timeScale = 0) 중에는
                // 불꽃도 함께 멈춘다 - 별도 처리 없이 PauseMenuUI와 자동으로 맞물린다.
                float t = _Time.y * _Speed + _Seed + IN.seed;

                // 바람: 시계 방향으로 기울었다가 반시계 방향으로 되돌아오기를 반복.
                // 밑동은 그대로 두고 위로 갈수록 더 밀리게 해서 불이 바람에 쓸리듯 휘어진다.
                float wind = sin(_Time.y * _WindSpeed + IN.seed) * _WindStrength;
                p.x -= wind * y01 * y01;

                // [규칙 1] 물방울 폭 프로파일: 위로 갈수록 좁아지고(taper), 밑동은 둥글게.
                // 갈래 3개가 공유하므로 루프 밖에서 한 번만 계산한다(pow/sqrt를 3번 돌리지 않도록).
                float taper = pow(saturate(1.0 - y01), _TipTaper);

                // 둥글리기를 "높이의 몇 %에 걸쳐 진행할지"로 환산한다. 이 구간이 길면 아래쪽까지
                // 폭이 서서히 빨려들어가 위아래가 모두 뾰족한 나뭇잎(=덩쿨) 모양이 되고,
                // 짧으면 바닥 근처에서 순식간에 최대 폭에 도달해 넓고 둥근 불꽃 밑동이 된다.
                // 그래서 _BaseRound가 1일 때 구간이 가장 짧아지도록(=가장 둥글게) 뒤집어 매핑한다.
                float roundSpan = lerp(0.6, 0.02, saturate(_BaseRound));
                float br = saturate(y01 / roundSpan);
                float profile = taper * sqrt(saturate(br * (2.0 - br)));

                float field = -10.0;

                // 갈래 3개. 아래에서는 겹쳐서 하나의 몸통, 위로 갈수록 벌어져 끝이 쪼개진다.
                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    float fi = i - 1.0; // -1(왼) / 0(가운데) / 1(오른)
                    float rnd = Hash(IN.seed * 7.13 + i * 13.7);

                    // [규칙 2] S자형 곡선: 중심선이 높이에 따라 좌우로 부드럽게 휘며 올라간다.
                    // 가지마다 위상이 달라서 세 줄기가 각자 다르게 굽는다.
                    float cx = sin(y01 * _SWaves * 3.14159265 + t * _SSpeed + rnd * 6.2831853)
                             * _SAmount * y01;

                    // 위로 갈수록 갈래가 벌어짐(y01 제곱이라 밑동에서는 거의 겹쳐 있음)
                    cx += fi * _SplitSpread * y01 * y01;

                    // 가운데 줄기는 굵고 길게, 옆 갈래는 가늘고 짧게 - 가지마다 끝나는 높이를 다르게.
                    float scale = (i == 1) ? 1.0 : _SideBranchScale;
                    float hCut = saturate((1.0 - _BranchHeightVar * rnd - y01) * 6.0);
                    float w = _Width * profile * scale * hCut;

                    // [규칙 1·5] 좌우 가장자리를 서로 다른 위상의 삼각파로 깎는다.
                    // 위상이 다르기 때문에 왼쪽과 오른쪽 굴곡이 절대 대칭이 되지 않는다.
                    float dx = p.x - cx;
                    float zBase = y01 * _ZigFreq * 0.15 + t * 0.27 + rnd;
                    float zig = (dx > 0.0) ? Tri(zBase) : Tri(zBase + 0.41);
                    float edge = w * (1.0 + (zig - 0.5) * _ZigAmount * y01);

                    // 가로 방향으로 한 번 더 잘게 쪼개서 혀 개수를 늘린다. 위(_ZigFreq)는 세로로
                    // 훑는 굴곡이라 큰 흐름을 만들고, 이쪽은 가로로 훑어서 가장자리를 톱니처럼 나눈다.
                    // 세기는 _ZigAmount를 같이 쓰고(프로퍼티를 늘리지 않으려고), 촘촘함만 _Detail로 따로 조절.
                    float fine = Tri(dx * _Detail * 0.12 + y01 * 1.7 + t * 0.5 + rnd);
                    edge *= 1.0 + (fine - 0.5) * _ZigAmount * 0.6 * y01;

                    field = max(field, edge - abs(dx));
                }

                // 밑동의 둥근 덩어리. 폭 프로파일만으로는 크기를 따로 키울 수 없어서 별도의 타원을
                // 합쳐준다(불꽃 뿌리에 뭉쳐 있는 핵 같은 부분). _BaseBlobSize가 0이면 없는 것과 같다.
                float2 bp = p - float2(0.0, _BaseBlobY);
                bp.y /= max(_BaseBlobSquash, 0.01);
                field = max(field, _BaseBlobSize - length(bp));

                // [규칙 3] 본체에서 떨어져 나가 위로 떠오르는 불씨. 올라갈수록 작아지다 사라지고
                // frac 주기로 다시 아래에서 태어난다.
                [unroll]
                for (int k = 0; k < 2; k++)
                {
                    float er = Hash(IN.seed * 3.71 + k * 29.3);
                    float cyc = frac(t * _EmberSpeed * 0.05 + er); // 0→1 반복 상승

                    float ey = _EmberStartY + cyc * _EmberRise;
                    float ex = (er * 2.0 - 1.0) * _EmberSpread + sin(cyc * 4.0 + er * 6.2831853) * 0.08;
                    float es = _EmberSize * saturate(1.0 - cyc);

                    float2 dv = p - float2(ex, ey);
                    dv.y *= 0.65; // 세로로 늘려 동그라미가 아니라 눈물방울처럼 보이게

                    field = max(field, es - length(dv));
                }

                // 셀 스타일의 핵심: 부드럽게 섞지 않고 딱 잘라낸다
                float inside = step(0.0, field);

                fixed4 col = _Color * IN.color; // 실제 색은 렌더러 color(프레임 색)에서 옴
                col.a *= inside * _Alpha;

                // 완전히 투명한 픽셀은 아예 버려서 불필요한 블렌딩(오버드로)을 줄인다
                clip(col.a - 0.003);

                return col;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
