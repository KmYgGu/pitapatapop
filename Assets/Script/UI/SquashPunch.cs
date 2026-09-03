using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 한 번 "말랑" 하고 튕기는 효과. 무언가가 <b>방금 들어왔다</b>는 걸 알릴 때 쓴다
    /// (편성 슬롯에 캐릭터가 들어갔을 때).
    ///
    /// 곡선은 배틀의 데미지 숫자와 같은 방식이다: <b>감쇠 진동</b>으로 크기를 흔들고,
    /// <b>1에서 벗어난 만큼만</b> 가로/세로를 반대로 늘려 젤리 느낌을 낸다. 그래서 잠잠해지면
    /// 저절로 원래 비율로 돌아오고, 중간에 다시 불러도 크기가 튀지 않는다.
    ///
    /// <b>사인으로 시작하는 이유</b>: 0에서 출발해 커지므로 "눌렸다 펴짐"이 아니라
    /// "부풀었다 가라앉음"으로 읽힌다 - 들어온 것을 반기는 느낌에 맞다.
    /// </summary>
    public class SquashPunch : MonoBehaviour
    {
        [Tooltip("가장 크게 부풀 때의 세기. 0.28이면 최대 1.28배쯤 된다.")]
        [SerializeField] private float amount = 0.28f;

        [Tooltip("한 번 출렁이는 데 걸리는 시간(초).")]
        [SerializeField] private float period = 0.32f;

        [Tooltip("클수록 빨리 잦아든다.")]
        [SerializeField] private float decay = 5.5f;

        [Tooltip("젤리 정도. 부풀면 그만큼 가로로 넓어지고 세로로 눌린다.")]
        [Range(0f, 1f)]
        [SerializeField] private float squashFactor = 0.35f;

        [Tooltip("이 시간이 지나면 멈추고 원래 크기로 돌려놓는다.")]
        [SerializeField] private float duration = 0.7f;

        [SerializeField] private RectTransform target;

        private float elapsed = -1f;
        private Vector3 baseScale = Vector3.one;
        private bool hasBaseScale;

        private void Awake()
        {
            if (target == null)
                target = transform as RectTransform;

            CacheBaseScale();
        }

        private void OnDisable()
        {
            // 흔들리던 도중에 화면이 꺼지면 그 크기로 굳는다.
            Stop();
        }

        /// <summary>처음부터 다시 튕긴다. 이미 흔들리는 중이어도 새로 시작한다.</summary>
        public void Play()
        {
            CacheBaseScale();
            elapsed = 0f;
        }

        /// <summary>즉시 멈추고 원래 크기로.</summary>
        public void Stop()
        {
            elapsed = -1f;

            if (target != null && hasBaseScale)
                target.localScale = baseScale;
        }

        private void Update()
        {
            if (elapsed < 0f || target == null)
                return;

            elapsed += Time.unscaledDeltaTime;

            if (elapsed >= duration)
            {
                Stop();
                return;
            }

            float envelope = Mathf.Exp(-decay * elapsed);
            float wave = Mathf.Sin(2f * Mathf.PI * elapsed / Mathf.Max(0.01f, period));
            float scale = 1f + amount * envelope * wave;

            // 1에서 벗어난 만큼만 가로/세로를 반대로 - 잠잠해지면 저절로 원래 비율이 된다.
            float squash = (scale - 1f) * squashFactor;

            target.localScale = new Vector3(baseScale.x * (scale + squash),
                                            baseScale.y * (scale - squash),
                                            baseScale.z);
        }

        /// <summary>
        /// 원래 크기를 기억해둔다. <b>흔들리는 도중에는 다시 재지 않는다</b> -
        /// 그때 재면 흔들린 크기가 기준이 되어 점점 커지거나 작아진다.
        /// </summary>
        private void CacheBaseScale()
        {
            if (hasBaseScale || target == null)
                return;

            baseScale = target.localScale;
            hasBaseScale = true;
        }
    }
}
