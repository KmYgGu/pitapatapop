using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 적이 쓰러졌을 때 <b>빙빙 돌면서 하늘로 날아가는</b> 연출. 승리가 확정된 순간
    /// <see cref="BattleResultPanel"/> 이 한 번 불러준다.
    ///
    /// <see cref="EnemyBattleAnimator"/>(공격 모션)와 하는 일이 완전히 다르다. 그쪽은 Spine
    /// 애니메이션 상태를 갈아끼우는 다리이고, 이쪽은 <b>초상화 RectTransform 자체를</b>
    /// 움직인다. 그래서 Spine 애셋이 없는 캐릭터에도 그대로 통한다.
    ///
    /// 순서는 두 단계다:
    ///  1. <b>움츠림</b> - 세로로 눌리며 살짝 내려앉는다. 이게 있어야 다음 단계가 "튕겨 올라간
    ///     것"으로 읽힌다. 없이 그냥 올라가면 그냥 미끄러지는 것처럼 보인다.
    ///  2. <b>비행</b> - 회전하며 위로 솟고 옆으로 흐르며 작아진다. 화면 밖까지 나가므로
    ///     사라지는 처리(알파)가 필요 없다 - <b>이건 의도된 것</b>이다. 초상화 안에 Spine 이
    ///     들어 있으면 페이지마다 CanvasRenderer 가 따로라 색을 한 번에 못 낮춘다.
    ///
    /// <b>HitFlinchUI 와 같은 오브젝트에 붙어 있어도 되는 이유</b>: 둘 다 anchoredPosition 과
    /// localScale 을 자기 기준값에서 다시 쓰기 때문에 겹쳐 재생되면 서로를 밀어낸다
    /// (HitFlinchUI 주석의 경고 그대로다). 마지막 일격을 맞은 직후에 이 연출이 시작되므로
    /// 실제로 겹친다 - 그래서 <see cref="PlayDefeat"/> 가 <b>먼저 움찔 연출을 꺼서</b>
    /// 그쪽 OnDisable 이 원래 자리로 되돌려놓게 하고, 그 뒤에 기준값을 읽는다.
    ///
    /// 코루틴 대신 Update 에서 경과 시간만 굴린다(HitFlinchUI·StartleHopUI 와 같은 방식).
    /// </summary>
    public class EnemyDefeatAnimator : MonoBehaviour
    {
        [Tooltip("날려보낼 대상. 비워두면 이 컴포넌트가 붙은 오브젝트를 쓴다.")]
        [SerializeField] private RectTransform target;

        [Tooltip("같은 초상화에 붙어 있는 움찔 연출. 연출을 시작하기 전에 꺼서 자리를 " +
                 "돌려받는다. 비워두면 자기 오브젝트에서 찾는다.")]
        [SerializeField] private HitFlinchUI flinch;

        [Tooltip("적과 함께 사라져야 하는 것들(적의 불꽃 오라 등). 초상화의 자식이 아니라 " +
                 "형제로 놓여 있어서 따라오지 않으므로 여기에 넣어 같이 끈다.")]
        [SerializeField] private GameObject[] hideOnDefeat = new GameObject[0];

        [Header("1. 움츠림")]
        [Tooltip("움츠리는 시간(초).")]
        [SerializeField] private float crouchDuration = 0.18f;

        [Tooltip("세로로 눌리는 정도. 0.22면 세로가 0.78배가 되고 가로는 그만큼 넓어진다.")]
        [SerializeField] private float crouchSquash = 0.22f;

        [Tooltip("내려앉는 깊이 - 대상 세로 크기 대비 비율이라 해상도가 바뀌어도 같은 느낌이 난다.")]
        [SerializeField] private float crouchDipFactor = 0.08f;

        [Header("2. 비행")]
        [Tooltip("날아가는 시간(초).")]
        [SerializeField] private float flightDuration = 0.95f;

        [Tooltip("올라가는 높이 - <b>캔버스 높이</b> 대비 비율이다. 1보다 크면 화면 위로 " +
                 "확실히 빠져나간다. 초상화 크기 기준으로 잡으면 기기마다 안 나가는 일이 생긴다.")]
        [SerializeField] private float riseScreenFraction = 1.2f;

        [Tooltip("옆으로 흐르는 폭 - 대상 가로 크기 대비 비율. 0이면 수직으로만 올라간다. " +
                 "약간 흘러야 '튕겨 날아갔다'로 읽힌다.")]
        [SerializeField] private float driftFactor = 0.5f;

        [Tooltip("날아가는 동안 도는 바퀴 수. 음수면 반대 방향으로 돈다.")]
        [SerializeField] private float spinTurns = 2.5f;

        [Tooltip("멀어지며 작아지는 최종 배율.")]
        [SerializeField] private float endScale = 0.4f;

        /// <summary>연출 전체 길이(초). 결과 화면이 이만큼 기다렸다가 뜬다.</summary>
        public float TotalDuration => Mathf.Max(0f, crouchDuration) + Mathf.Max(0f, flightDuration);

        /// <summary>지금 날아가는 중인지.</summary>
        public bool IsPlaying => elapsed >= 0f;

        // 원래 상태. 되돌릴 때 정확히 이 값으로 돌아간다.
        private Vector2 basePosition;
        private Vector3 baseScale;
        private Quaternion baseRotation;
        private bool hasBaseState;

        // 음수면 재생 중이 아님 - 0 이상일 때만 Update 가 일을 한다.
        private float elapsed = -1f;

        // 날아간 채로 끝났는지. 끝난 뒤에는 화면 밖에 그대로 두어야 하므로 Update 가 손을 뗀다.
        private bool finished;

        // 캔버스 높이는 배틀 내내 안 바뀌므로 한 번만 잰다.
        private float canvasHeight;

        // hideOnDefeat 중 <b>내가 껐던 것</b>만 표시해둔다. 이걸 안 보고 전부 켜면
        // 원래부터 꺼져 있던 것까지 켜버린다 - 적의 불꽃 오라는 보스일 때만 켜지므로
        // 실제로 그런 일이 생긴다.
        private bool[] hidByMe;

        private void Awake()
        {
            if (target == null)
                target = transform as RectTransform;

            if (flinch == null)
                flinch = GetComponent<HitFlinchUI>();
        }

        /// <summary>
        /// 빙빙 돌며 날아가는 연출을 시작한다. 이미 재생 중이거나 이미 날아간 뒤면 무시한다 -
        /// 승패는 한 번만 확정되지만, 다시 부른다고 적이 두 번 날아가면 곤란하다.
        /// </summary>
        public void PlayDefeat()
        {
            if (target == null || elapsed >= 0f || finished)
                return;

            // 움찔 연출을 먼저 끈다. 끄면 그쪽 OnDisable 이 흔들린 자리를 원래대로 되돌려주므로,
            // 바로 다음 줄에서 읽는 기준값이 "맞은 뒤 밀려난 자리"가 아니라 진짜 제자리가 된다.
            if (flinch != null)
                flinch.enabled = false;

            CaptureBaseState();
            MeasureCanvasHeight();

            if (hidByMe == null || hidByMe.Length != hideOnDefeat.Length)
                hidByMe = new bool[hideOnDefeat.Length];

            for (int i = 0; i < hideOnDefeat.Length; i++)
            {
                bool hide = hideOnDefeat[i] != null && hideOnDefeat[i].activeSelf;
                hidByMe[i] = hide;

                if (hide)
                    hideOnDefeat[i].SetActive(false);
            }

            elapsed = 0f;
        }

        /// <summary>
        /// 날아가기 전 상태로 되돌린다. 배틀을 다시 시작할 때처럼 같은 씬을 이어 쓰는 자리에서 쓴다.
        /// </summary>
        public void ResetState()
        {
            elapsed = -1f;
            finished = false;

            if (hasBaseState && target != null)
            {
                target.anchoredPosition = basePosition;
                target.localScale = baseScale;
                target.localRotation = baseRotation;
            }

            if (flinch != null)
                flinch.enabled = true;

            if (hidByMe == null)
                return;

            for (int i = 0; i < hideOnDefeat.Length && i < hidByMe.Length; i++)
            {
                if (hidByMe[i] && hideOnDefeat[i] != null)
                    hideOnDefeat[i].SetActive(true);

                hidByMe[i] = false;
            }
        }

        private void OnDisable()
        {
            // 꺼졌다 켜지면 깨끗한 상태에서 다시 시작하는 게 맞다 - 날아간 자세로 굳어 있으면
            // 다음 배틀에서 적이 화면 밖에 서 있게 된다.
            ResetState();
        }

        private void Update()
        {
            if (elapsed < 0f)
                return;

            elapsed += Time.deltaTime;

            float crouch = Mathf.Max(0f, crouchDuration);
            float flight = Mathf.Max(0.01f, flightDuration);

            if (elapsed < crouch)
            {
                ApplyCrouch(elapsed / crouch);
                return;
            }

            float t = (elapsed - crouch) / flight;
            if (t >= 1f)
            {
                ApplyFlight(1f);
                elapsed = -1f;
                finished = true; // 화면 밖에 그대로 둔다
                return;
            }

            ApplyFlight(t);
        }

        /// <param name="p">0 = 선 자세, 1 = 가장 많이 움츠린 자세.</param>
        private void ApplyCrouch(float p)
        {
            // 끝으로 갈수록 빨리 눌린다 - 발을 굴러 튕겨 오르기 직전의 느낌.
            float e = p * p;

            float squash = crouchSquash * e;
            target.localScale = new Vector3(baseScale.x * (1f + squash),
                                            baseScale.y * (1f - squash),
                                            baseScale.z);

            float dip = target.rect.height * crouchDipFactor * e;
            target.anchoredPosition = basePosition + new Vector2(0f, -dip);
        }

        /// <param name="p">0 = 막 떠오름, 1 = 화면 밖.</param>
        private void ApplyFlight(float p)
        {
            // 처음엔 빠르게 솟았다가 위로 갈수록 느려진다(ease-out). 등속으로 올리면
            // 튕겨 나간 게 아니라 끌려 올라가는 것처럼 보인다.
            float rise = 1f - (1f - p) * (1f - p);

            float height = canvasHeight > 0f ? canvasHeight : target.rect.height * 8f;
            float drift = target.rect.width * driftFactor * p;

            target.anchoredPosition = basePosition + new Vector2(drift, height * riseScreenFraction * rise);

            // 회전은 등속이라야 "빙글빙글"로 읽힌다. 위치와 같은 곡선을 태우면 위로 갈수록
            // 회전까지 느려져서 힘이 빠져 보인다.
            target.localRotation = baseRotation * Quaternion.Euler(0f, 0f, -360f * spinTurns * p);

            float scale = Mathf.Lerp(1f, endScale, p);
            target.localScale = baseScale * scale;
        }

        private void CaptureBaseState()
        {
            if (hasBaseState || target == null)
                return;

            basePosition = target.anchoredPosition;
            baseScale = target.localScale;
            baseRotation = target.localRotation;
            hasBaseState = true;
        }

        /// <summary>
        /// 올라갈 거리의 기준이 되는 캔버스 높이를 잰다. 이 프로젝트의 캔버스는 세로 기준으로
        /// 맞춰져 있어 어떤 기기에서도 600이지만, 그 숫자를 여기 박지 않고 실제로 읽는다.
        /// </summary>
        private void MeasureCanvasHeight()
        {
            if (canvasHeight > 0f)
                return;

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                return;

            var rect = canvas.transform as RectTransform;
            if (rect != null)
                canvasHeight = rect.rect.height;
        }
    }
}
