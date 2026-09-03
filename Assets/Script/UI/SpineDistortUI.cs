using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 얻어맞을 때마다 <b>몸이 찌그러졌다 되돌아오는</b> 연출. 러시 타임에 매치가 성립할 때마다
    /// 적 캐릭터가 이걸 재생한다.
    ///
    /// <b>진짜 픽셀 왜곡이 아니다.</b> 대상이 <c>SkeletonGraphic</c> 인데 그건 아틀라스 페이지마다
    /// 렌더러가 따로라 화면을 통째로 일그러뜨리려면 전용 셰이더가 있어야 한다(이 프로젝트는
    /// 셰이더 그래프를 사용자가 만든다). 대신 <b>감쇠 진동으로 가로/세로를 서로 반대로 늘였다
    /// 줄이고 기울기를 같이 흔들어</b> 젤리처럼 일그러지는 그림을 낸다 -
    /// <see cref="SquashPunch"/> 와 같은 곡선을 쓰되 훨씬 세고 기울기가 붙는다.
    ///
    /// <b>연달아 맞으면 더 심해진다.</b> 러시 타임은 매치가 쏟아지는 구간이라, 매번 처음부터
    /// 다시 시작하면 오히려 흔들림이 매번 끊겨서 밋밋해진다. 그래서 남아 있던 세기에 얹되
    /// 상한을 둔다.
    ///
    /// <b>HitFlinchUI 와 같은 오브젝트에 붙이지 말 것</b> - 둘 다 localScale 을 자기 기준값에서
    /// 다시 쓰므로 서로를 밀어낸다. 적 초상화에서는 이걸 <c>SpineChar</c> 자식에 붙여서
    /// 초상화 본체(HitFlinchUI·EnemyDefeatAnimator 가 쓰는 RectTransform)와 갈라놓았다.
    /// </summary>
    public class SpineDistortUI : MonoBehaviour
    {
        [Tooltip("일그러뜨릴 대상. 비워두면 이 컴포넌트가 붙은 오브젝트를 쓴다.")]
        [SerializeField] private RectTransform target;

        [Header("세기")]
        [Tooltip("한 번 맞을 때 더해지는 세기. 0.25면 최대 1.25배쯤 늘어난다.")]
        [SerializeField] private float amountPerHit = 0.22f;

        [Tooltip("세기 상한. 연달아 맞아도 이보다 더 심해지지는 않는다.")]
        [SerializeField] private float maxAmount = 0.45f;

        [Tooltip("가로/세로가 서로 반대로 늘어나는 정도. 1이면 부피가 유지되는 것처럼 보인다.")]
        [Range(0f, 1f)]
        [SerializeField] private float squashFactor = 0.9f;

        [Tooltip("같이 흔들릴 기울기(도). 0이면 안 기운다 - 기울기가 있어야 '일그러졌다'로 읽힌다.")]
        [SerializeField] private float tiltDegrees = 7f;

        [Header("곡선")]
        [Tooltip("한 번 출렁이는 데 걸리는 시간(초).")]
        [SerializeField] private float period = 0.26f;

        [Tooltip("클수록 빨리 잦아든다.")]
        [SerializeField] private float decay = 6.5f;

        // 지금 남아 있는 세기. 0이면 재생 중이 아니라 Update 가 첫 줄에서 빠진다.
        private float amount;

        // 진동의 위상. 맞을 때마다 0으로 되돌리지 않는다 - 되돌리면 매번 뚝 끊겨 보인다.
        private float phase;

        private Vector3 baseScale;
        private Quaternion baseRotation;
        private bool hasBaseState;

        private void Awake()
        {
            if (target == null)
                target = transform as RectTransform;

            CaptureBaseState();
        }

        /// <summary>한 대 맞은 것으로 친다. 재생 중에 불러도 안전하다 - 세기가 얹힌다.</summary>
        public void Hit()
        {
            if (target == null)
                return;

            // 잠잠할 때만 기준을 다시 읽는다. 흔들리는 도중에 읽으면 일그러진 크기가
            // "원래 크기"로 굳어서 맞을수록 조금씩 커지거나 작아진다.
            if (amount <= 0f)
            {
                CaptureBaseState();
                phase = 0f;
            }

            amount = Mathf.Min(maxAmount, amount + amountPerHit);
        }

        /// <summary>즉시 멈추고 원래 모습으로.</summary>
        public void Stop()
        {
            amount = 0f;
            Restore();
        }

        private void OnDisable()
        {
            // 흔들리던 도중에 꺼지면 일그러진 채로 굳는다.
            if (amount > 0f)
                Stop();
        }

        private void Update()
        {
            if (amount <= 0f || target == null)
                return;

            phase += Time.deltaTime;
            amount = Mathf.Max(0f, amount - amount * decay * Time.deltaTime);

            if (amount < 0.001f)
            {
                Stop();
                return;
            }

            float wave = Mathf.Sin(2f * Mathf.PI * phase / Mathf.Max(0.01f, period));
            float scale = 1f + amount * wave;

            // 1에서 벗어난 만큼만 가로/세로를 반대로 - 잠잠해지면 저절로 원래 비율이 된다.
            float squash = (scale - 1f) * squashFactor;

            target.localScale = new Vector3(baseScale.x * (scale + squash),
                                            baseScale.y * (scale - squash),
                                            baseScale.z);

            target.localRotation = baseRotation * Quaternion.Euler(0f, 0f, tiltDegrees * amount * wave);
        }

        private void CaptureBaseState()
        {
            if (hasBaseState || target == null)
                return;

            baseScale = target.localScale;
            baseRotation = target.localRotation;
            hasBaseState = true;
        }

        private void Restore()
        {
            if (!hasBaseState || target == null)
                return;

            target.localScale = baseScale;
            target.localRotation = baseRotation;
        }
    }
}
