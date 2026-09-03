using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 스탠드업 타임 시작 시 화면에 나타나는 배너. 글자 두 덩이가 화면 밖에서 날아와 부딪힌다:
    ///
    ///   1) <b>진입</b>  - "버스트"는 화면 위 밖에서 떨어지고 "타임!"은 아래 밖에서 올라온다.
    ///                     가속하며 다가와 한가운데서 부딪힌다.
    ///   2) <b>충돌</b>  - 부딪힌 순간 말랑하게 눌렸다가 몇 번 출렁이며 펴진다.
    ///                     <b>플레이어가 글자를 읽는 시간</b>이 여기다.
    ///   3) <b>퇴장</b>  - 둘이 함께 잠깐 아래로 내려갔다가(예행동작) 위로 솟아 사라진다.
    ///
    /// <b>전체 길이는 예전과 같다</b>(진입 0.3 + 유지 2.0 + 퇴장 0.4 = 2.7초). 배너가 떠 있는
    /// 동안 화면이 어두워지는 것과 조작이 막히는 구간이 이 타이밍에 맞춰져 있어서, 총 시간이
    /// 달라지면 그쪽까지 전부 다시 맞춰야 한다. PopInDuration/ExitDuration 을 그대로 노출하는
    /// 이유도 같다 - BoardInputController 가 화면 어둡기를 이 값에 싱크시킨다.
    ///
    /// 배너가 떠 있는 동안은 터치 조작을 막아야 하므로 OnBannerShown/OnBannerHidden 으로 알린다
    /// (실제 차단은 BoardInputController 가 처리하며, 낙하·매치 진행은 안 막는다).
    /// </summary>
    public class StandUpTimeUI : MonoBehaviour
    {
        [Header("글자")]
        [Tooltip("화면 <b>위</b>에서 떨어지는 글자('버스트'). 크기와 자리는 화면에 맞춰 " +
                 "재생할 때마다 자동으로 계산되므로 씬에서 맞춰둘 필요가 없다.")]
        [SerializeField] private RectTransform burstRect;

        [Tooltip("화면 <b>아래</b>에서 올라오는 글자('타임!'). 위와 같이 자동으로 계산된다.")]
        [SerializeField] private RectTransform timeRect;

        [SerializeField] private CanvasGroup canvasGroup; // 페이드용 (선택, 없으면 이동만)

        [Header("타이밍 (합이 예전과 같아야 한다)")]
        [Tooltip("화면 밖에서 날아와 부딪히기까지의 시간(초).")]
        [SerializeField] private float popInDuration = 0.3f;

        [Tooltip("부딪힌 뒤 글자가 머무는 시간(초). 앞부분은 눌렸다 펴지는 데 쓰고 나머지는 가만히 있는다.")]
        [SerializeField] private float holdDuration = 2f;

        [Tooltip("예행동작 + 위로 솟아 사라지는 데 걸리는 시간(초).")]
        [SerializeField] private float exitDuration = 0.4f;

        [Header("크기 - 화면에 맞춰 자동 계산")]
        [Tooltip("글자 폭을 화면 가로의 몇 배로 할지. 0.9면 좌우에 5%씩 여백이 남는다.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float widthFraction = 0.9f;

        [Tooltip("두 글자를 합친 높이가 화면 세로의 이 비율을 넘지 않게 한다. " +
                 "가로가 넓은 화면(태블릿)에서 글자가 위아래로 넘치는 걸 막는 상한이다.")]
        [Range(0.2f, 0.95f)]
        [SerializeField] private float maxHeightFraction = 0.55f;

        [Tooltip("두 글자 사이 간격 - 글자 폭 대비 비율. 음수면 살짝 겹친다.")]
        [SerializeField] private float gapFraction = 0f;

        [Header("충돌 - 말랑하게 눌리기")]
        [Tooltip("부딪힌 순간 눌리는 정도. 0.25면 세로가 75%까지 눌리고 가로는 그만큼 벌어진다.")]
        [SerializeField] private float squashAmount = 0.25f;

        [Tooltip("눌림이 한 번 출렁이는 주기(초).")]
        [SerializeField] private float squashPeriod = 0.26f;

        [Tooltip("눌림이 잦아드는 속도. 작을수록 오래 출렁인다.")]
        [SerializeField] private float squashDecay = 4.5f;

        [Tooltip("부딪힐 때 서로를 밀고 들어가는 거리 - 글자 높이 대비 비율. 눌림과 같은 곡선으로 되돌아온다.")]
        [SerializeField] private float pressFraction = 0.14f;

        [Header("퇴장")]
        [Tooltip("퇴장 시간 중 <b>예행동작(아래로)</b>이 차지하는 비율. 0.35면 앞 35%는 아래로, " +
                 "나머지 65%는 위로 솟는다.")]
        [Range(0.05f, 0.8f)]
        [SerializeField] private float anticipationFraction = 0.35f;

        [Tooltip("예행동작으로 내려가는 거리 - 글자 높이 대비 비율.")]
        [SerializeField] private float anticipationDipFraction = 0.25f;

        [Tooltip("퇴장할 때 위로 솟는 거리 - 화면 세로 대비 비율. 1이면 화면 높이만큼 솟는다.")]
        [SerializeField] private float exitRiseFraction = 1f;

        /// <summary>배너가 나타나기 시작할 때 발행 - 이때부터 터치 입력을 막아야 함.</summary>
        public event System.Action OnBannerShown;

        /// <summary>배너가 완전히 사라졌을 때 발행 - 이때부터 다시 터치 입력 가능.</summary>
        public event System.Action OnBannerHidden;

        /// <summary>퇴장이 막 시작될 때 발행 - 화면 어둡기를 배너 퇴장과 같은 속도로 되돌리는 데 쓴다.</summary>
        public event System.Action OnExitStart;

        /// <summary>진입에 걸리는 시간(초) - 화면이 어두워지는 속도를 여기 맞추면 배너 등장과 싱크된다.</summary>
        public float PopInDuration => popInDuration;

        /// <summary>퇴장에 걸리는 시간(초) - 화면이 밝아지는 속도를 여기 맞추면 배너 퇴장과 싱크된다.</summary>
        public float ExitDuration => exitDuration;

        // 부딪히는 자리와 이동 거리. 모두 화면 크기에서 매번 다시 계산한다 - 고정 픽셀로 두면
        // 기기 비율마다 글자가 화면 밖으로 잘려 나간다(실제로 겪음).
        private Vector2 burstRest;
        private Vector2 timeRest;
        private float textHeight;      // 두 글자 중 큰 쪽 높이 - 밀림·예행동작의 기준
        private float offscreenRise;   // 화면 밖까지의 거리
        private float exitRise;
        private bool isPlaying;

        private void Awake()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 글자 크기와 자리를 <b>지금 화면에 맞춰</b> 다시 잡는다.
        ///
        /// 캔버스 스케일러가 <b>세로를 기준</b>으로 맞추고 있어서(기준 800x600, Match=Height),
        /// 세로는 항상 600이지만 <b>가로는 기기 비율에 따라 달라진다</b> - 세로로 긴 폰이면
        /// 270 남짓밖에 안 된다. 그래서 폭을 고정 값으로 두면 폰에서 글자가 양옆으로 잘린다.
        /// 화면 가로를 실제로 재서 그 비율로 잡아야 어떤 기기에서도 같은 그림이 나온다.
        ///
        /// 가로가 넓은 기기(태블릿)에서는 반대로 위아래가 넘칠 수 있어서 높이 상한도 함께 본다.
        /// </summary>
        private void ApplyLayout()
        {
            // 이 컴포넌트가 붙은 오브젝트는 캔버스에 꽉 차 있지만, 방금 켠 직후라 rect 가
            // 아직 갱신 안 됐을 수 있다. 부모(캔버스)는 늘 켜져 있으므로 그쪽을 재는 게 안전하다.
            var self = transform as RectTransform;
            var basis = transform.parent as RectTransform;
            if (basis == null)
                basis = self;
            if (basis == null)
                return;

            float screenW = basis.rect.width;
            float screenH = basis.rect.height;
            if (screenW <= 1f || screenH <= 1f)
                return;

            float burstAspect = GetAspect(burstRect);
            float timeAspect = GetAspect(timeRect);

            // 가로 기준 폭과, "두 글자 높이 합"이 상한을 넘지 않는 폭 중 작은 쪽을 쓴다.
            float widthByWidth = screenW * widthFraction;
            float stackPerWidth = 1f / burstAspect + 1f / timeAspect; // 폭 1일 때의 높이 합
            float widthByHeight = screenH * maxHeightFraction / Mathf.Max(0.0001f, stackPerWidth);
            float width = Mathf.Min(widthByWidth, widthByHeight);

            float burstH = Resize(burstRect, width, burstAspect);
            float timeH = Resize(timeRect, width, timeAspect);
            float gap = width * gapFraction;

            burstRest = new Vector2(0f, burstH * 0.5f + gap * 0.5f);
            timeRest = new Vector2(0f, -(timeH * 0.5f + gap * 0.5f));

            textHeight = Mathf.Max(burstH, timeH);

            // 화면 밖으로 완전히 빠지는 거리(화면 절반 + 글자 하나 + 여유).
            offscreenRise = screenH * 0.5f + textHeight * 1.2f;
            exitRise = screenH * exitRiseFraction;
        }

        private static float GetAspect(RectTransform rect)
        {
            if (rect != null)
            {
                var image = rect.GetComponent<Image>();
                if (image != null && image.sprite != null && image.sprite.rect.height > 0f)
                    return image.sprite.rect.width / image.sprite.rect.height;
            }
            return 2f; // 그림이 없으면 가로로 긴 글자라고 가정
        }

        private static float Resize(RectTransform rect, float width, float aspect)
        {
            float height = width / Mathf.Max(0.0001f, aspect);
            if (rect != null)
                rect.sizeDelta = new Vector2(width, height);
            return height;
        }

        /// <summary>스탠드업 게이지가 가득 찼을 때 호출 - 배너 연출을 재생.</summary>
        public void Play()
        {
            if (isPlaying)
                return; // 이미 재생 중이면 중복 실행 방지

            gameObject.SetActive(true);
            StartCoroutine(PlayRoutine());
        }

        private IEnumerator PlayRoutine()
        {
            isPlaying = true;

            // 크기·자리를 지금 화면에 맞춰 다시 잡는다. 연출을 시작할 때마다 하므로
            // 기기 회전이나 해상도 변경에도 저절로 따라온다.
            ApplyLayout();

            OnBannerShown?.Invoke();

            if (canvasGroup != null)
                canvasGroup.alpha = 1f;

            SetScale(1f, 1f);

            // 1) 진입 - 화면 밖에서 제자리로. 가속(p²)해서 다가와야 "떨어졌다/솟았다"로 읽힌다.
            //    등속으로 오면 그냥 미끄러져 들어오는 것처럼 보인다.
            Vector2 burstFrom = burstRest + Vector2.up * offscreenRise;
            Vector2 timeFrom = timeRest + Vector2.down * offscreenRise;

            float t = 0f;
            float popIn = Mathf.Max(0.01f, popInDuration);
            while (t < popIn)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / popIn);
                float eased = p * p;
                SetPositions(Vector2.Lerp(burstFrom, burstRest, eased),
                             Vector2.Lerp(timeFrom, timeRest, eased));
                yield return null;
            }

            // 2) 충돌 + 유지 - 부딪힌 순간 눌렸다가 출렁이며 펴지고, 남은 시간은 가만히 있는다.
            //    이 구간 전체가 플레이어가 글자를 읽는 시간이다.
            t = 0f;
            float hold = Mathf.Max(0f, holdDuration);
            while (t < hold)
            {
                t += Time.deltaTime;

                // 눌림에서 시작해 1로 수렴하는 감쇠 진동. t=0 이면 cos=1 이라 가장 많이 눌린다.
                float k = Mathf.Exp(-squashDecay * t)
                          * Mathf.Cos(2f * Mathf.PI * t / Mathf.Max(0.01f, squashPeriod));

                SetScale(1f + squashAmount * k * 0.6f, 1f - squashAmount * k);

                // 서로를 밀고 들어갔다가 같은 곡선으로 되돌아온다.
                float press = textHeight * pressFraction * k;
                SetPositions(burstRest + Vector2.down * press, timeRest + Vector2.up * press);

                yield return null;
            }

            SetScale(1f, 1f);
            SetPositions(burstRest, timeRest);

            // 3) 퇴장 - 잠깐 아래로 내려갔다가(예행동작) 위로 솟아 사라진다.
            OnExitStart?.Invoke(); // 화면을 다시 밝게 하는 쪽이 이 시점에 맞춰 시작하면 정확히 싱크된다

            float exit = Mathf.Max(0.01f, exitDuration);
            float dipSpan = exit * Mathf.Clamp01(anticipationFraction);
            float riseSpan = Mathf.Max(0.01f, exit - dipSpan);

            float dip = textHeight * anticipationDipFraction;
            Vector2 burstDip = burstRest + Vector2.down * dip;
            Vector2 timeDip = timeRest + Vector2.down * dip;

            t = 0f;
            while (t < dipSpan)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / dipSpan);
                // 감속하며 내려앉는다 - 힘을 모으는 동작이라 끝에서 느려져야 자연스럽다.
                float eased = 1f - (1f - p) * (1f - p);
                SetPositions(Vector2.Lerp(burstRest, burstDip, eased),
                             Vector2.Lerp(timeRest, timeDip, eased));
                yield return null;
            }

            Vector2 burstOut = burstDip + Vector2.up * exitRise;
            Vector2 timeOut = timeDip + Vector2.up * exitRise;

            t = 0f;
            while (t < riseSpan)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / riseSpan);
                float eased = p * p; // 가속하며 솟구친다
                SetPositions(Vector2.Lerp(burstDip, burstOut, eased),
                             Vector2.Lerp(timeDip, timeOut, eased));

                if (canvasGroup != null)
                    canvasGroup.alpha = 1f - eased;

                yield return null;
            }

            gameObject.SetActive(false);
            isPlaying = false;
            OnBannerHidden?.Invoke();
        }

        private void SetPositions(Vector2 burstPos, Vector2 timePos)
        {
            if (burstRect != null)
                burstRect.anchoredPosition = burstPos;
            if (timeRect != null)
                timeRect.anchoredPosition = timePos;
        }

        private void SetScale(float x, float y)
        {
            var scale = new Vector3(x, y, 1f);
            if (burstRect != null)
                burstRect.localScale = scale;
            if (timeRect != null)
                timeRect.localScale = scale;
        }
    }
}
