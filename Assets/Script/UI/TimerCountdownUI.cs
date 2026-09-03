using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 시계의 남은 시간이 얼마 안 남았을 때 <b>시계 앞에 숫자가 튀어나왔다 사라지는</b> 초읽기.
    /// 5, 4, 3, 2, 1 이 1초에 하나씩 크게 떴다가 커지며 흐려진다.
    ///
    /// <b>시계를 읽기만 한다.</b> 자기 시계를 따로 굴리지 않으므로 제한시간이든 러시 타임이든
    /// 시계에 실린 것이면 무엇이든 그대로 초읽기가 된다 - 러시 타임이 시계를 다시 굴려도
    /// 이 컴포넌트는 손댈 필요가 없다.
    ///
    /// <b>시계가 멈춰 있으면 숫자도 멈춘다</b>(대사창·스킬 암전 구간). 그때 초읽기만 계속
    /// 흐르면 "아무것도 못 하는데 시간이 간다"로 보인다 - 시계를 멈추는 것과 같은 방침이다.
    ///
    /// 코루틴 대신 Update 에서 경과 시간만 굴린다(이 프로젝트 UI 연출의 기본 방식).
    /// </summary>
    public class TimerCountdownUI : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("남은 시간을 읽어올 시계.")]
        [SerializeField] private RadialTimerUI timer;

        [Tooltip("숫자를 그릴 곳. <b>시계보다 앞에 그려지는 자리</b>여야 한다.")]
        [SerializeField] private Text numberText;

        [Header("언제")]
        [Tooltip("남은 시간이 이 값 이하로 떨어지면 초읽기가 시작된다(초).")]
        [SerializeField] private int startAtSeconds = 5;

        [Header("연출")]
        [Tooltip("숫자 하나가 떴다 사라지기까지 걸리는 시간(초). 1초를 넘기면 다음 숫자와 겹친다.")]
        [SerializeField] private float popDuration = 0.85f;

        [Tooltip("튀어나올 때의 시작 크기 배율.")]
        [SerializeField] private float startScale = 1.9f;

        [Tooltip("사라질 때의 크기 배율. 1보다 크면 커지면서 흐려진다.")]
        [SerializeField] private float endScale = 1.15f;

        [Tooltip("처음 이 비율만큼은 또렷하게 보여준 뒤에 흐려지기 시작한다.")]
        [Range(0f, 1f)]
        [SerializeField] private float holdFraction = 0.3f;

        [Header("글자 크기 - 칸에 맞춰 자동")]
        [Tooltip("칸 높이의 몇 할까지 글자가 차지해도 되는지. 이 프로젝트 타이포그래피 규칙이 0.75다.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float heightFraction = 0.75f;

        // 지금 보여주고 있는 숫자. 0이면 아무것도 안 보여주는 중이다.
        private int shownNumber;

        // 음수면 재생 중이 아님 - 0 이상일 때만 Update 가 일한다.
        private float elapsed = -1f;

        // 글자 크기를 이미 칸에 맞췄는지.
        private bool fontFitted;

        private Color baseColor;
        private Vector3 baseScale = Vector3.one;
        private RectTransform numberRect;

        private void Awake()
        {
            if (numberText == null)
                return;

            numberRect = (RectTransform)numberText.transform;
            baseColor = numberText.color;
            baseScale = numberRect.localScale;

            numberText.text = string.Empty;
        }

        private void OnDisable()
        {
            Clear();
        }

        private void Update()
        {
            TickTrigger();
            TickPop();
        }

        /// <summary>시계를 보고 새 숫자를 띄울 때가 됐는지 살핀다.</summary>
        private void TickTrigger()
        {
            if (timer == null || numberText == null)
                return;

            // 멈춰 있거나 이미 끝난 시계에서는 초읽기가 없다.
            if (!timer.IsRunning)
                return;

            float remaining = timer.RemainingSeconds;

            // 남은 시간이 3.2초면 "4"가 떠 있어야 한다 - 올림이 곧 지금 세는 숫자다.
            int number = Mathf.CeilToInt(remaining);

            if (number > startAtSeconds || number <= 0)
                return;

            if (number == shownNumber)
                return;

            // 글자 크기는 칸을 실제로 재서 정한다. <b>Start 가 아니라 처음 띄울 때</b> 재는 이유:
            // 이 칸은 HudContent(AspectRatioFitter) 안에 비율로만 잡혀 있어서 Start 시점엔
            // 아직 크기가 0일 수 있고, 그러면 조용히 실패한 채 굳는다.
            if (!fontFitted)
                fontFitted = FitFontToBox();

            shownNumber = number;
            numberText.text = number.ToString();
            elapsed = 0f;
        }

        /// <summary>떠 있는 숫자를 굴린다.</summary>
        private void TickPop()
        {
            if (elapsed < 0f || numberText == null)
                return;

            elapsed += Time.deltaTime;

            float duration = Mathf.Max(0.01f, popDuration);
            if (elapsed >= duration)
            {
                Clear();
                return;
            }

            float t = elapsed / duration;
            float hold = Mathf.Max(0.01f, holdFraction);

            float scale;
            float alpha;

            if (t < hold)
            {
                // 확 커진 채로 나타나 제 크기까지 <b>감속하며</b> 줄어든다 - "탁" 나타나는 느낌은
                // 이 감속에서 나온다. 대칭 곡선으로는 안 난다. 이 구간은 또렷하게 보여준다.
                float p = t / hold;
                scale = Mathf.Lerp(startScale, 1f, 1f - (1f - p) * (1f - p));
                alpha = 1f;
            }
            else
            {
                // 남은 구간에서 살짝 부풀며 흐려진다.
                float p = Mathf.InverseLerp(hold, 1f, t);
                scale = Mathf.Lerp(1f, endScale, p);
                alpha = 1f - p;
            }

            numberRect.localScale = baseScale * scale;

            var c = baseColor;
            c.a = baseColor.a * alpha;
            numberText.color = c;
        }

        /// <summary>숫자를 즉시 치운다.</summary>
        public void Clear()
        {
            elapsed = -1f;
            shownNumber = 0;

            if (numberText == null)
                return;

            numberText.text = string.Empty;

            if (numberRect != null)
                numberRect.localScale = baseScale;

            numberText.color = baseColor;
        }

        /// <summary>
        /// 칸 높이의 <see cref="heightFraction"/> 를 넘지 않는 가장 큰 타이포그래피 단계를 고른다.
        /// 눈대중 숫자를 쓰지 않는다는 프로젝트 방침(<see cref="UITypography"/>)을 지키면서도,
        /// 기기마다 달라지는 칸 크기에 맞추기 위해 실행 시점에 고른다.
        /// </summary>
        /// <returns>실제로 맞췄으면 true. 칸 크기가 아직 안 잡혔으면 false 라 다음에 다시 잰다.</returns>
        private bool FitFontToBox()
        {
            if (numberText == null || numberRect == null)
                return false;

            float limit = numberRect.rect.height * heightFraction;
            if (limit <= 0f)
                return false;

            int chosen = UITypography.Micro;
            for (int i = 0; i < UITypography.Steps.Length; i++)
            {
                if (UITypography.Steps[i] <= limit)
                {
                    chosen = UITypography.Steps[i];
                    break; // Steps 는 큰 것부터라 처음 통과한 게 가장 큰 단계다
                }
            }

            numberText.fontSize = chosen;
            return true;
        }
    }
}
