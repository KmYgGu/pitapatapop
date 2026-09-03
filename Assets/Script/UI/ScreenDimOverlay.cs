using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 화면 전체를 반투명한 검은색으로 살짝 어둡게 만드는 재사용 가능한 오버레이.
    /// 스탠드업 타임 배너뿐 아니라, 나중에 캐릭터 스킬 컷인에도 그대로 재사용할 예정이라
    /// 어두워지는/밝아지는 시간을 매번 외부(호출하는 쪽)에서 지정받는다 - 그래야 캐릭터
    /// 애니메이션 길이가 다른 컷인마다 그 길이에 맞춰 싱크할 수 있음.
    /// 씬 세팅: Canvas 밑에 화면 전체를 덮는 Image(검은색, 알파 0에서 시작) 만들고 이 컴포넌트 붙이기.
    /// 렌더링 순서: 이 오버레이보다 강조하고 싶은 요소(배너, 컷인 캐릭터 등)는 Hierarchy에서
    /// 이 오브젝트보다 아래(나중)에 둬야 그 위에 그려져서 어두워지지 않음.
    /// </summary>
    public class ScreenDimOverlay : MonoBehaviour
    {
        [SerializeField] private Image overlayImage; // 비워두면 자기 자신의 Image 사용
        [SerializeField] private float dimAlpha = 0.6f; // 완전히 어두워졌을 때의 알파값

        private Coroutine activeRoutine;

        /// <summary>
        /// 지금 어둡게 하기로 되어 있는지. 페이드가 끝났는지가 아니라 <b>마지막으로 요청받은 상태</b>다 -
        /// "어두워지기 시작한 순간부터" 제한시간을 멈추는 쪽이 자연스럽기 때문.
        /// </summary>
        public bool IsDimmed { get; private set; }

        /// <summary>IsDimmed가 바뀐 순간 발행. 제한시간 타이머를 멈추고 재개하는 데 쓴다.</summary>
        public event System.Action<bool> OnDimChanged;

        private void Awake()
        {
            if (overlayImage == null)
                overlayImage = GetComponent<Image>();

            SetAlpha(0f);
        }

        /// <summary>
        /// 화면을 어둡게(dimmed=true) 또는 원래대로(dimmed=false) 전환.
        /// duration: 이 전환에 걸리는 시간(초) - 캐릭터 애니메이션 길이 등과 맞추고 싶을 때 그 값을 그대로 넘기면 됨.
        /// </summary>
        public void SetDim(bool dimmed, float duration)
        {
            if (IsDimmed != dimmed)
            {
                IsDimmed = dimmed;
                OnDimChanged?.Invoke(dimmed);
            }

            if (activeRoutine != null)
                StopCoroutine(activeRoutine);

            activeRoutine = StartCoroutine(FadeRoutine(dimmed ? dimAlpha : 0f, duration));
        }

        private IEnumerator FadeRoutine(float targetAlpha, float duration)
        {
            float startAlpha = overlayImage.color.a;
            float t = 0f;

            while (t < duration)
            {
                t += Time.deltaTime;
                float p = duration > 0f ? Mathf.Clamp01(t / duration) : 1f;
                SetAlpha(Mathf.Lerp(startAlpha, targetAlpha, p));
                yield return null;
            }

            SetAlpha(targetAlpha);
            activeRoutine = null;
        }

        private void SetAlpha(float alpha)
        {
            var color = overlayImage.color;
            color.a = alpha;
            overlayImage.color = color;
        }
    }
}
