using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 게임 안 모든 글자 크기의 <b>단 하나의 기준</b>. 새 UI 텍스트를 만들 때는 여기 있는 단계 중
    /// 하나를 쓰고, 눈대중으로 숫자를 적지 않는다(2026-08-23 사용자 방침).
    ///
    /// <b>왜 그냥 상수인가</b>: 캔버스 스케일러가 <b>세로를 기준</b>으로 맞춘다
    /// (Scale With Screen Size, 기준 800x600, Match=Height). 그래서 캔버스 세로는 어떤 기기에서도
    /// 항상 600이고, 글꼴 크기 34는 늘 "화면 높이의 34/600"이다. 기기마다 다시 계산할 필요가 없다.
    /// (가로는 기기 비율마다 달라지므로 <b>가로 크기</b>를 다룰 때는 이 상수를 쓰면 안 된다 -
    ///  그건 화면 폭을 실제로 재서 비율로 잡아야 한다. StandUpTimeUI 참고.)
    ///
    /// 단계는 1.25배쯤씩 올라간다(14 · 18 · 22 · 28 · 34 · 42 · 52). 사이 값이 필요해 보이면
    /// 대개는 <b>단계를 잘못 고른 것</b>이니 새 숫자를 만들기 전에 위아래 단계를 먼저 대볼 것.
    ///
    /// <b>고를 때 규칙: 글자가 들어갈 상자 높이의 75%를 넘는 단계는 고르지 않는다.</b>
    /// 글자는 글꼴 크기의 70% 남짓을 차지하고 줄 높이는 그보다 크다 - 상자에 꽉 맞는 단계를
    /// 고르면 위아래로 삐져나온다. HUD 판들은 생각보다 작다(2026-08-23 실측, 캔버스 600 기준):
    ///   점수 판 12.9 · 골드 배지 28.9 · 클리어 조건 띠 24.3
    /// 그래서 HUD 는 Micro~Caption 대에서 고르게 되고, Body 이상은 화면 위에 자유롭게 뜨는
    /// 글자(데미지·누적 매칭)의 자리다. <b>더 크게 쓰고 싶으면 먼저 판을 키워야 한다.</b>
    ///
    /// <b>Best Fit 과 같이 쓰지 말 것.</b> Best Fit 은 글자를 상자에 꽉 채우도록 자동으로 키우므로
    /// 여기서 정한 크기를 그냥 덮어쓴다 - 크기를 상자가 정하게 되어 화면마다 제각각이 된다.
    /// 대신 Horizontal/Vertical Overflow 를 Overflow 로 두면 상자를 넘어도 잘리지 않는다.
    /// </summary>
    public static class UITypography
    {
        /// <summary>아주 좁은 판에 들어가는 숫자 - 점수 바처럼 높이가 20 아래인 자리.</summary>
        public const int Micro = 14;

        /// <summary>좁은 띠에 들어가는 문구 - 클리어 조건처럼 높이가 30 아래인 자리.</summary>
        public const int Small = 18;

        /// <summary>보조 문구 - 라벨, 단위, 배지 안의 짧은 숫자.</summary>
        public const int Caption = 22;

        /// <summary>넉넉한 자리의 읽을거리. HUD 판들은 대개 이걸 담지 못한다 - 위 규칙 참고.</summary>
        public const int Body = 28;

        /// <summary><b>기준 단계.</b> 데미지 숫자와 누적 매칭 숫자가 여기다.</summary>
        public const int Headline = 34;

        /// <summary>대사처럼 한 번에 눈에 들어와야 하는 문장.</summary>
        public const int Title = 42;

        /// <summary>가장 큰 강조. 화면 전체를 차지하는 연출용.</summary>
        public const int Display = 52;

        /// <summary>큰 것부터 늘어놓은 사다리. FitToWidth 가 이 순서로 내려간다.</summary>
        public static readonly int[] Steps = { Display, Title, Headline, Body, Caption, Small, Micro };

        /// <summary>
        /// 정해진 폭 안에 들어갈 때까지 <b>사다리를 한 단씩 내려가며</b> 글꼴을 정한다.
        ///
        /// <b>왜 필요한가</b>: 위 상수들은 세로 기준이라 어떤 기기에서도 같은 크기지만,
        /// <b>가로는 기기 비율마다 달라진다</b> - 세로로 긴 폰일수록 캔버스가 좁다. 그래서 문구가
        /// 들어갈 상자의 폭도 같이 좁아지고, 세로만 보고 고른 단계가 가로로 삐져나온다
        /// (클리어 조건 문구가 실제로 그랬다 - 2026-08-23).
        ///
        /// Best Fit 을 쓰지 않는 이유는 그대로다 - 그건 상자에 <b>꽉 채우도록</b> 아무 크기나
        /// 만들어 내지만, 이건 <b>사다리 위의 값만</b> 고르므로 화면이 달라져도 글자 크기가
        /// 정해진 단계 중 하나로 떨어진다.
        ///
        /// 가장 작은 단계로도 안 들어가면 그 단계를 쓴다 - 그때는 상자를 넓히는 게 맞다.
        /// </summary>
        /// <param name="startStep">여기서부터 내려간다. 이보다 큰 단계는 쓰지 않는다.</param>
        /// <returns>고른 글꼴 크기.</returns>
        public static int FitToWidth(Text text, float maxWidth, int startStep)
        {
            if (text == null)
                return startStep;

            int chosen = startStep;
            for (int i = 0; i < Steps.Length; i++)
            {
                int step = Steps[i];
                if (step > startStep)
                    continue;

                text.fontSize = step;
                chosen = step;

                if (maxWidth <= 0f || text.preferredWidth <= maxWidth)
                    break;
            }

            return chosen;
        }

        /// <summary>
        /// <b>상자 안에 다 들어갈 때까지</b> 사다리를 한 단씩 내려가며 글꼴을 정한다.
        /// <see cref="FitToWidth"/> 와 달리 <b>줄바꿈된 높이까지</b> 본다 - 여러 줄로 접히는
        /// 문단(대사창)은 폭만 봐서는 넘치는지 알 수 없다.
        ///
        /// <b>글자가 길수록 작아진다</b>(2026-08-28 사용자 지시). 대사 길이는 캐릭터마다 제각각인데
        /// 상자는 고정이라, 긴 대사가 위아래로 잘려 나가고 있었다.
        ///
        /// <b>줄바꿈이 켜져 있어야 한다</b>(<c>horizontalOverflow = Wrap</c>). 이 함수가 켜준다 -
        /// 안 그러면 preferredHeight 가 늘 한 줄 높이라 아무리 내려가도 조건이 맞아버린다.
        ///
        /// <b>Unity 의 Best Fit 을 쓰지 않는 이유</b>는 <see cref="FitToWidth"/> 와 같다 -
        /// 그건 사다리 밖의 아무 크기나 만들어 낸다. 부르는 쪽에서 Best Fit 을 꺼줄 것.
        /// </summary>
        /// <param name="startStep">여기서부터 내려간다(보통 씬에 적어둔 크기).</param>
        public static int FitToBox(Text text, float maxWidth, float maxHeight, int startStep)
        {
            if (text == null)
                return startStep;

            // 상자 크기를 못 재면(꺼져 있는 오브젝트는 rect 가 0이다) 손대지 않는다 -
            // 0으로 재고 제일 작은 단계까지 내려가면 멀쩡한 대사가 개미처럼 작아진다.
            if (maxWidth <= 0f || maxHeight <= 0f)
                return text.fontSize;

            text.horizontalOverflow = HorizontalWrapMode.Wrap;

            int chosen = startStep;
            for (int i = 0; i < Steps.Length; i++)
            {
                int step = Steps[i];
                if (step > startStep)
                    continue;

                text.fontSize = step;
                chosen = step;

                if (text.preferredHeight <= maxHeight)
                    break;
            }

            return chosen;
        }
    }
}
