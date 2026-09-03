using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 깜짝 놀라 <b>깡총 뛰는</b> 연출. 빠르게 솟았다가 가속하며 떨어진다.
    /// 적이 가벼운 방해를 걸었을 때 리더·파트너 초상화가 이걸 재생한다.
    ///
    /// <see cref="HitFlinchUI"/>와 짝을 이루는 "시키면 하는" 컴포넌트다 - 언제 재생할지는 전혀
    /// 모르고 Hop() 을 부르면 그때 한 번 뛴다. 구조도 같은 이유로 같다: 코루틴 대신 Update 에서
    /// 경과 시간만 굴리므로, 연출 도중에 또 불려도 경과 시간을 0으로 되돌리는 것만으로 다시
    /// 시작되고(코루틴 중복 걱정 없음) 재생 중이 아닐 때는 첫 줄에서 바로 빠져나간다.
    ///
    /// <b>같은 오브젝트에 HitFlinchUI 와 함께 붙이지 말 것.</b> 둘 다 anchoredPosition/localScale 을
    /// 자기 기준값에서 다시 쓰기 때문에, 동시에 재생되면 나중에 쓴 쪽이 이기고 연출이 끝날 때
    /// 서로의 기준값으로 되돌려 초상화가 조금씩 밀려난다. 굳이 둘 다 필요하면 부모/자식으로
    /// 한 단계 나눠서 각자 다른 RectTransform 을 움직이게 할 것.
    /// </summary>
    public class StartleHopUI : MonoBehaviour
    {
        [Tooltip("뛰어오를 대상. 비워두면 이 컴포넌트가 붙은 오브젝트를 사용.")]
        [SerializeField] private RectTransform target;

        [Header("연출")]
        [Tooltip("솟아오르는 데 걸리는 시간(초). 짧을수록 '깜짝' 놀란 느낌이 난다.")]
        [SerializeField] private float riseDuration = 0.12f;

        [Tooltip("떨어지는 데 걸리는 시간(초). 올라가는 시간보다 길어야 뛴 것처럼 보인다.")]
        [SerializeField] private float fallDuration = 0.2f;

        [Tooltip("뛰어오르는 높이 - 대상 세로 크기 대비 비율이라 해상도가 바뀌어도 같은 느낌이 난다.")]
        [SerializeField] private float hopHeightFactor = 0.18f;

        [Tooltip("떠 있는 동안 세로로 늘어나는 정도(가로는 그만큼 줄어든다). 0이면 늘어나지 않는다.")]
        [SerializeField] private float airborneStretch = 0.06f;

        // 원래 상태. 연출이 끝나면 정확히 이 값으로 되돌린다.
        private Vector2 basePosition;
        private Vector3 baseScale;

        // 음수면 재생 중이 아님 - 0 이상일 때만 Update가 일을 한다.
        private float elapsed = -1f;

        private void Awake()
        {
            if (target == null)
                target = transform as RectTransform;

            CaptureBaseState();
        }

        private void CaptureBaseState()
        {
            if (target == null)
                return;

            basePosition = target.anchoredPosition;
            baseScale = target.localScale;
        }

        /// <summary>한 번 뛴다. 재생 중에 다시 불러도 안전하게 처음부터 다시 시작된다.</summary>
        public void Hop()
        {
            if (target == null)
                return;

            // 재생 중이 아닐 때만 원래 상태를 다시 읽는다. 연출 도중에 읽으면 떠 있는 위치가
            // "원래 위치"로 굳어버려서, 연달아 맞을수록 초상화가 위로 밀려 올라간다.
            if (elapsed < 0f)
                CaptureBaseState();

            elapsed = 0f;
        }

        private void Update()
        {
            if (elapsed < 0f)
                return;

            elapsed += Time.deltaTime; // timeScale을 따르므로 일시정지 중엔 함께 멈춤

            float rise = Mathf.Max(0.01f, riseDuration);
            float fall = Mathf.Max(0.01f, fallDuration);

            if (elapsed >= rise + fall)
            {
                Restore();
                elapsed = -1f;
                return;
            }

            // 높이는 0~1. 올라갈 때는 ease-out 으로 튀어오르고, 내려올 때는 가속해서 떨어진다.
            float height;
            if (elapsed < rise)
            {
                float p = elapsed / rise;
                height = 1f - (1f - p) * (1f - p);
            }
            else
            {
                float p = (elapsed - rise) / fall;
                height = 1f - p * p;
            }

            target.anchoredPosition = basePosition
                + new Vector2(0f, target.rect.height * hopHeightFactor * height);

            // 떠 있는 동안 살짝 늘어난다. 부피가 유지되는 것처럼 가로를 반대로 줄여야
            // 그냥 커지는 게 아니라 "쭉 늘어났다"로 보인다.
            float stretch = airborneStretch * height;
            target.localScale = new Vector3(
                baseScale.x * (1f - stretch * 0.5f),
                baseScale.y * (1f + stretch),
                baseScale.z);
        }

        private void Restore()
        {
            target.anchoredPosition = basePosition;
            target.localScale = baseScale;
        }

        private void OnDisable()
        {
            // 연출 도중에 꺼지면 떠 있는 상태로 굳어버리므로 반드시 되돌려놓는다.
            if (elapsed >= 0f)
            {
                Restore();
                elapsed = -1f;
            }
        }
    }
}
