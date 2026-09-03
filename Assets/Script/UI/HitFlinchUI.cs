using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 타격을 받았을 때 움찔하는 연출. 좌우로 짧게 흔들리면서 살짝 부풀었다가 원래대로 돌아온다.
    ///
    /// 언제 재생할지는 전혀 모르는 "시키면 하는" 컴포넌트다 - 지금은 적이 데미지를 받을 때
    /// DamagePopupUI가 호출하지만, 나중에 적의 방해 효과로 리더/파트너가 맞을 때도
    /// 그쪽 초상화에 이 컴포넌트를 붙이고 Flinch()만 부르면 그대로 재사용된다.
    ///
    /// 코루틴 대신 Update에서 경과 시간만 굴린다. 덕분에 연출 도중에 또 맞아도 Flinch()가
    /// 경과 시간을 0으로 되돌리는 것만으로 자연스럽게 다시 시작되고(코루틴 중복 실행 걱정 없음),
    /// 재생 중이 아닐 때는 첫 줄에서 바로 빠져나가 비용이 사실상 0이다.
    /// </summary>
    public class HitFlinchUI : MonoBehaviour
    {
        [Tooltip("흔들 대상. 비워두면 이 컴포넌트가 붙은 오브젝트를 사용.")]
        [SerializeField] private RectTransform target;

        [Tooltip("맞는 순간 잠깐 색이 번쩍일 대상(선택). 비워두면 색 변화 없음.")]
        [SerializeField] private Graphic flashTarget;

        [Header("연출")]
        [SerializeField] private float duration = 0.22f;

        [Tooltip("좌우로 흔들리는 폭 - 대상 가로 크기 대비 비율이라 해상도가 바뀌어도 같은 느낌이 난다.")]
        [SerializeField] private float shakeFactor = 0.06f;

        [Tooltip("연출 동안 좌우로 왕복하는 횟수.")]
        [SerializeField] private float shakeCount = 3f;

        [Tooltip("맞는 순간 커지는 정도. 음수로 주면 반대로 움츠러든다.")]
        [SerializeField] private float punchScale = 0.1f;

        [SerializeField] private Color flashColor = Color.white;

        // 원래 상태. 연출이 끝나면 정확히 이 값으로 되돌린다.
        private Vector2 basePosition;
        private Vector3 baseScale;
        private Color baseColor;

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
            if (target != null)
            {
                basePosition = target.anchoredPosition;
                baseScale = target.localScale;
            }

            if (flashTarget != null)
                baseColor = flashTarget.color;
        }

        /// <summary>움찔 연출을 처음부터 재생. 재생 중에 다시 불러도 안전하게 다시 시작된다.</summary>
        public void Flinch()
        {
            if (target == null)
                return;

            // 재생 중이 아닐 때만 원래 상태를 다시 읽는다. 연출 도중에 읽으면 흔들린 위치가
            // "원래 위치"로 굳어버려서, 여러 번 맞을수록 초상화가 조금씩 밀려나게 된다.
            if (elapsed < 0f)
                CaptureBaseState();

            elapsed = 0f;
        }

        private void Update()
        {
            if (elapsed < 0f)
                return;

            elapsed += Time.deltaTime; // timeScale을 따르므로 일시정지 중엔 함께 멈춤

            float t = duration > 0f ? elapsed / duration : 1f;
            if (t >= 1f)
            {
                Restore();
                elapsed = -1f;
                return;
            }

            float damp = 1f - t; // 시간이 갈수록 잦아듦

            float amplitude = target.rect.width * shakeFactor;
            float offset = Mathf.Sin(t * Mathf.PI * 2f * shakeCount) * amplitude * damp;
            target.anchoredPosition = basePosition + new Vector2(offset, 0f);

            // 맞는 순간 가장 크고 빠르게 원래대로 - 타격감이 앞쪽에 몰리도록 damp를 제곱
            float scale = 1f + punchScale * damp * damp;
            target.localScale = new Vector3(baseScale.x * scale, baseScale.y * scale, baseScale.z);

            if (flashTarget != null)
                flashTarget.color = Color.Lerp(baseColor, flashColor, damp * damp);
        }

        private void Restore()
        {
            target.anchoredPosition = basePosition;
            target.localScale = baseScale;

            if (flashTarget != null)
                flashTarget.color = baseColor;
        }

        private void OnDisable()
        {
            // 연출 도중에 꺼지면 흔들린 상태로 굳어버리므로 반드시 되돌려놓는다.
            if (elapsed >= 0f)
            {
                Restore();
                elapsed = -1f;
            }
        }
    }
}
