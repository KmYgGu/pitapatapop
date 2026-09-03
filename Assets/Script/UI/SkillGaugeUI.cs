using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 캐릭터 스킬 게이지 UI. 게이지가 가득 차기 전까지는 탭해도 반응 없고,
    /// 가득 차면 탭했을 때 OnSkillActivated 이벤트가 발행됨.
    /// 씬 세팅: 게이지 바 Image(Type=Filled)를 이 컴포넌트에 연결. 캐릭터 아이콘 위에
    /// 겹쳐서 배치하면 원작처럼 "캐릭터 자체를 탭해서 스킬 발동" 느낌을 낼 수 있음.
    ///
    /// <b>가득 찼을 때 연출은 세 단계로 이어진다.</b> 전부 이 컴포넌트의 Update 하나가 굴린다 -
    /// 오브젝트마다 코루틴을 띄우지 않는 이 프로젝트의 방식 그대로다.
    ///  1) 큰 동그라미가 게이지 중심으로 빠르게 빨려들며 작아진다. 1초 안에 3번.
    ///  2) 가득 찬 게이지 바의 잔상이 <b>가로로만</b> 커지며 옅어진다. 1초 안에 3번.
    ///  3) 그 뒤로는 테두리가 은은하게 반짝이기를 계속 반복한다(스킬을 쓸 때까지).
    /// 게이지가 다시 비면(ConsumeGauge) 전부 즉시 정리된다.
    /// </summary>
    public class SkillGaugeUI : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image fillImage; // 게이지 바 (Image Type = Filled 권장)
        [SerializeField] private GameObject readyGlowIndicator; // 가득 찼을 때 켜지는 반짝임 표시 (선택, 없어도 됨)

        [Header("가득 참 연출 1 - 빨려드는 동그라미")]
        [Tooltip("게이지 중심으로 빨려드는 큰 동그라미. 비워두면 이 단계를 건너뛴다.")]
        [SerializeField] private Image readyRing;

        [Tooltip("동그라미가 시작할 때의 크기 배수.")]
        [SerializeField] private float ringStartScale = 4f;

        [Tooltip("빨려들어 사라질 때의 크기 배수.")]
        [SerializeField] private float ringEndScale = 0.2f;

        [Tooltip("가장 클 때의 불투명도. 낮게 잡으면 처음엔 흐릿하게 나타난다.")]
        [Range(0f, 1f)]
        [SerializeField] private float ringStartAlpha = 0.15f;

        [Tooltip("가장 작아졌을 때의 불투명도. 작아질수록 이 값으로 또렷해진다.")]
        [Range(0f, 1f)]
        [SerializeField] private float ringEndAlpha = 1f;

        [Tooltip("반복 횟수와 그 전체에 걸리는 시간(초). 3번을 0.75초 안에 = 한 번에 0.25초.")]
        [SerializeField] private int ringRepeatCount = 3;
        [SerializeField] private float ringTotalDuration = 0.75f;

        [Header("가득 참 연출 2 - 가로로 퍼지는 잔상")]
        [Tooltip("가로로만 커지며 옅어지는 게이지 바 잔상. 비워두면 이 단계를 건너뛴다. " +
                 "보통 채움 이미지를 그대로 복제해서 쓴다.")]
        [SerializeField] private Image afterImage;

        [Tooltip("잔상이 가로로 최대 몇 배까지 늘어나는지. 세로는 늘어나지 않는다.")]
        [SerializeField] private float afterImageMaxWidthScale = 1.6f;

        [Tooltip("동그라미가 <b>몇 번째 반복을 시작할 때</b> 잔상도 같이 시작할지. " +
                 "3이면 동그라미의 3번째가 시작되는 순간부터 둘이 겹쳐 돈다. " +
                 "1이면 처음부터 같이, 반복 횟수보다 크면 동그라미가 다 끝난 뒤에 시작한다.")]
        [Min(1)]
        [SerializeField] private int afterImageStartsAtRingRepeat = 3;

        [SerializeField] private int afterImageRepeatCount = 3;
        [SerializeField] private float afterImageTotalDuration = 1f;

        [Header("가득 참 연출 3 - 테두리 숨쉬기")]
        [Tooltip("테두리에서 안쪽으로 스며드는 빛. 비워두면 이 단계를 건너뛴다.\n" +
                 "테두리 이미지의 색을 밝게 곱하는 방식은 쓸 수 없다 - 테두리가 이미 흰색이라 " +
                 "무엇을 곱해도 흰색 그대로여서 아무 변화가 없다. 그래서 위에 얹는 빛으로 처리한다.")]
        [SerializeField] private Image edgeGlow;

        [Tooltip("빛에 곱해지는 색. 흰색으로 두면 테두리 아트의 <b>원래 색</b>이 그대로 빛난다 " +
                 "(빛 이미지가 아트에서 색까지 그대로 떠온 것이라 단색 tint 가 필요 없다). " +
                 "특정 색조로 물들이고 싶을 때만 바꿀 것.")]
        [SerializeField] private Color glowColor = Color.white;

        [Tooltip("숨쉬기 한 번에 걸리는 시간(초).")]
        [SerializeField] private float glintPeriod = 1.2f;

        [Tooltip("한 주기에서 <b>밝아지는 데</b> 쓰는 비율. 0.2면 20%는 빠르게 밝아지고 " +
                 "나머지 80%는 천천히 어두워진다 - 반짝하고 여운이 남는 느낌.")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float glowRiseFraction = 0.22f;

        [Tooltip("가장 옅을 때와 가장 진할 때의 불투명도.")]
        [Range(0f, 1f)]
        [SerializeField] private float glowMinAlpha = 0.1f;

        [Range(0f, 1f)]
        [SerializeField] private float glowMaxAlpha = 0.85f;

        [Tooltip("빛이 안쪽으로 스며드는 깊이의 최소/최대. 낮을수록 가장 바깥 테두리만 빛나고, " +
                 "1에 가까울수록 게이지 안쪽까지 그라데이션이 들어온다. " +
                 "이 값이 오르내리면서 '빛이 안으로 들어왔다 물러나는' 모습이 된다.")]
        [Range(0f, 1f)]
        [SerializeField] private float glowMinDepth = 0.25f;

        [Range(0f, 1f)]
        [SerializeField] private float glowMaxDepth = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float currentValue;

        /// <summary>가득 찬 상태에서 탭했을 때 발행.</summary>
        public event System.Action OnSkillActivated;

        public bool IsFull => currentValue >= 1f;
        public float CurrentValue => currentValue;

        /// <summary>가득 찬 뒤 흐른 시간. 음수면 연출이 꺼져 있다는 뜻.</summary>
        private float readyElapsed = -1f;

        // 잔상의 원래 크기 - 가로만 늘렸다가 되돌리려면 기준값이 필요하다.
        private Vector3 afterImageBaseScale = Vector3.one;
        private bool afterImageBaseCaptured;

        // 빛의 깊이(_GlowDepth)를 게이지마다 따로 움직이려면 머티리얼도 따로 있어야 한다.
        // 애셋을 그대로 쓰면 두 게이지가 같은 머티리얼을 공유해서 서로의 값을 덮어쓴다.
        private static readonly int GlowDepthId = Shader.PropertyToID("_GlowDepth");
        private Material glowMaterial;

        private void Awake()
        {
            // 원래 값은 연출이 건드리기 전에 잡아둔다. Start에서 잡으면 다른 스크립트가 먼저
            // 색이나 크기를 바꿨을 때 그 값을 "원래 값"으로 착각한다.
            if (afterImage != null)
            {
                afterImageBaseScale = afterImage.rectTransform.localScale;
                afterImageBaseCaptured = true;
            }

            // 머티리얼 복제는 여기서 딱 한 번만 한다(매 프레임 생성은 이 프로젝트가 피하는 것).
            if (edgeGlow != null && edgeGlow.material != null)
            {
                glowMaterial = new Material(edgeGlow.material);
                edgeGlow.material = glowMaterial;
            }
        }

        private void OnDestroy()
        {
            // Awake에서 만든 사본이므로 여기서 정리한다 - 안 그러면 씬을 오갈 때마다 쌓인다.
            if (glowMaterial != null)
                Destroy(glowMaterial);
        }

        private void Start()
        {
            ApplyVisual();
        }

        private void Update()
        {
            if (readyElapsed < 0f)
                return;

            readyElapsed += Time.deltaTime;

            float ringSpan = Mathf.Max(0.01f, ringTotalDuration);
            float afterSpan = Mathf.Max(0.01f, afterImageTotalDuration);

            // 잔상은 동그라미가 N번째 반복을 "시작하는 순간"부터 함께 돈다.
            // 반복 한 번의 길이 x (N-1) 이 그 시각이다 - 횟수나 시간을 바꿔도 알아서 따라간다.
            int ringRepeats = Mathf.Max(1, ringRepeatCount);
            float afterStart = ringSpan / ringRepeats * (afterImageStartsAtRingRepeat - 1);

            // 반짝임은 둘 다 끝난 뒤에 시작한다(겹쳐 시작하면 무엇이 끝난 건지 안 읽힌다).
            float glintStart = Mathf.Max(ringSpan, afterStart + afterSpan);

            UpdateRing(readyElapsed, ringSpan);
            UpdateAfterImage(readyElapsed - afterStart, afterSpan);
            UpdateGlint(readyElapsed - glintStart);
        }

        /// <summary>게이지를 0~1 사이 값으로 직접 설정.</summary>
        public void SetGauge(float normalizedValue)
        {
            currentValue = Mathf.Clamp01(normalizedValue);
            ApplyVisual();
        }

        /// <summary>현재 값에서 amount만큼 충전(음수면 소모). 매치로 패널을 지울 때마다 호출하는 용도.</summary>
        public void AddCharge(float amount)
        {
            SetGauge(currentValue + amount);
        }

        /// <summary>스킬 발동 처리 - 보통 OnSkillActivated 구독 쪽에서 실제 효과를 실행한 뒤 이걸 호출해 게이지를 비움.</summary>
        public void ConsumeGauge()
        {
            SetGauge(0f);
        }

        /// <summary>
        /// 1단계. 큰 동그라미가 중심으로 빨려들며 작아진다.
        /// ringTotalDuration 안에 ringRepeatCount번 반복하고, 끝나면 꺼진다.
        /// </summary>
        private void UpdateRing(float t, float span)
        {
            if (readyRing == null)
                return;

            if (t < 0f || t >= span)
            {
                if (readyRing.gameObject.activeSelf)
                    readyRing.gameObject.SetActive(false);
                return;
            }

            if (!readyRing.gameObject.activeSelf)
                readyRing.gameObject.SetActive(true);

            // 반복 한 번 안에서의 진행도(0~1)
            int repeats = Mathf.Max(1, ringRepeatCount);
            float p = Mathf.Repeat(t / span * repeats, 1f);

            float scale = Mathf.Lerp(ringStartScale, ringEndScale, p);
            readyRing.rectTransform.localScale = new Vector3(scale, scale, 1f);

            // 클 때는 흐릿하고, 빨려들며 작아질수록 또렷해진다 - 시선이 게이지 중심으로 모인다.
            var color = readyRing.color;
            color.a = Mathf.Lerp(Mathf.Clamp01(ringStartAlpha), Mathf.Clamp01(ringEndAlpha), p);
            readyRing.color = color;
        }

        /// <summary>
        /// 2단계. 가득 찬 게이지 바의 잔상이 <b>가로로만</b> 커지며 옅어진다.
        /// 세로를 그대로 두는 게 핵심이다 - 세로까지 키우면 사방으로 번지는 빛처럼 보여서
        /// "게이지가 좌우로 뻗어나간다"는 인상이 사라진다.
        /// </summary>
        private void UpdateAfterImage(float t, float span)
        {
            if (afterImage == null)
                return;

            if (t < 0f || t >= span)
            {
                if (afterImage.gameObject.activeSelf)
                {
                    afterImage.gameObject.SetActive(false);
                    if (afterImageBaseCaptured)
                        afterImage.rectTransform.localScale = afterImageBaseScale;
                }
                return;
            }

            if (!afterImage.gameObject.activeSelf)
            {
                afterImage.gameObject.SetActive(true);
                afterImage.fillAmount = 1f; // 잔상은 언제나 "가득 찬 모습"이다
            }

            int repeats = Mathf.Max(1, afterImageRepeatCount);
            float p = Mathf.Repeat(t / span * repeats, 1f);

            var scale = afterImageBaseScale;
            scale.x *= Mathf.Lerp(1f, afterImageMaxWidthScale, p);
            afterImage.rectTransform.localScale = scale;

            var color = afterImage.color;
            color.a = 1f - p;
            afterImage.color = color;
        }

        /// <summary>
        /// 3단계. 앞의 두 연출이 끝난 뒤부터 테두리가 계속 숨쉰다.
        ///
        /// 테두리에서 안쪽(게이지 쪽)으로 스며드는 그라데이션 이미지를 얹고 그 불투명도만
        /// 오르내린다. 테두리 이미지 자체의 색을 밝게 만드는 방법은 못 쓴다 - 그 이미지가
        /// 이미 흰색이라 무엇을 곱해도 흰색이라 화면에 아무 변화가 없다(실제로 그래서 안 보였다).
        ///
        /// cos 기반이라 양 끝에서 속도가 0이 된다. 그래서 딱딱한 점멸이 아니라 숨쉬듯 오간다.
        /// </summary>
        private void UpdateGlint(float t)
        {
            if (edgeGlow == null || t < 0f)
                return;

            if (!edgeGlow.gameObject.activeSelf)
                edgeGlow.gameObject.SetActive(true);

            // 밝아질 때는 빠르게, 어두워질 때는 느리게. 좌우 대칭인 cos 로는 이 느낌이 안 나서
            // 주기를 "올라가는 구간 / 내려가는 구간"으로 갈라 각각 따로 보간한다.
            float period = Mathf.Max(0.05f, glintPeriod);
            float phase = Mathf.Repeat(t / period, 1f);
            float rise = Mathf.Clamp(glowRiseFraction, 0.05f, 0.95f);

            float wave = phase < rise
                ? phase / rise
                : 1f - (phase - rise) / (1f - rise);

            wave = wave * wave * (3f - 2f * wave); // smoothstep - 꺾이는 지점이 각지지 않게

            var color = glowColor;
            color.a = Mathf.Lerp(Mathf.Clamp01(glowMinAlpha), Mathf.Clamp01(glowMaxAlpha), wave);
            edgeGlow.color = color;

            // 세기뿐 아니라 <b>스며드는 깊이</b>도 같이 움직인다. 이게 있어야 "빛이 안쪽으로
            // 들어왔다 물러난다"로 읽힌다 - 알파만 오르내리면 그냥 전체가 껌뻑이는 것처럼 보인다.
            if (glowMaterial != null)
            {
                glowMaterial.SetFloat(GlowDepthId,
                    Mathf.Lerp(Mathf.Clamp01(glowMinDepth), Mathf.Clamp01(glowMaxDepth), wave));
            }
        }

        /// <summary>가득 참 연출을 처음부터 시작하거나(begin=true), 즉시 정리한다.</summary>
        private void SetReadyEffect(bool begin)
        {
            readyElapsed = begin ? 0f : -1f;

            if (begin)
                return;

            if (readyRing != null)
                readyRing.gameObject.SetActive(false);

            if (afterImage != null)
            {
                afterImage.gameObject.SetActive(false);
                if (afterImageBaseCaptured)
                    afterImage.rectTransform.localScale = afterImageBaseScale;
            }

            if (edgeGlow != null)
                edgeGlow.gameObject.SetActive(false);
        }

        private void ApplyVisual()
        {
            if (fillImage != null)
                fillImage.fillAmount = currentValue;
            if (readyGlowIndicator != null)
                readyGlowIndicator.SetActive(IsFull);

            // 가득 찬 "순간"에만 연출을 시작한다. 충전될 때마다 다시 시작하면 3번 반복이 계속
            // 처음으로 되돌아가서 영영 3단계(반짝임)까지 가지 못한다.
            bool effectRunning = readyElapsed >= 0f;
            if (IsFull != effectRunning)
                SetReadyEffect(IsFull);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!IsFull)
                return;

            OnSkillActivated?.Invoke();
        }
    }
}
