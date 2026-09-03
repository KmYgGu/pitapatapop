using System.Collections;
using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 화면 전체를 덮었다 걷는 암막. 씬을 갈아탈 때 쓴다 - 준비 화면에서 캐릭터가 뛰어 나간 뒤
    /// 여기로 덮고, 배틀 화면에서 이걸 걷으며 시작한다(2026-08-28).
    ///
    /// <b><see cref="ScreenDimOverlay"/> 와 다른 물건이다.</b> 저쪽은 "조작을 막는 반투명 막"이라
    /// 배틀 규칙(시계 멈춤)에 물려 있다 - 씬 전환용으로 빌려 쓰면 그 규칙까지 딸려온다.
    /// </summary>
    public class ScreenFadeUI : MonoBehaviour
    {
        [Tooltip("전체를 덮는 판. 알파는 여기서 조절한다. blocksRaycasts 도 같이 켜져 " +
                 "덮여 있는 동안은 아무것도 눌리지 않는다.")]
        [SerializeField] private CanvasGroup group;

        [SerializeField] private float defaultDuration = 0.35f;

        private void Awake()
        {
            if (group == null)
                group = GetComponent<CanvasGroup>();

            // 시작은 걷힌 상태. 씬에 켜진 채로 저장돼 있어도 화면을 가리지 않는다.
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.gameObject.SetActive(false);
            }
        }

        /// <summary>어두워진다(덮는다).</summary>
        public IEnumerator FadeOut(float duration = -1f) => Fade(1f, duration);

        /// <summary>밝아진다(걷는다).</summary>
        public IEnumerator FadeIn(float duration = -1f) => Fade(0f, duration);

        /// <summary>기다리지 않고 곧바로 덮는다. 배틀 씬이 시작하자마자 부른다.</summary>
        public void CoverInstantly()
        {
            if (group == null)
                return;

            group.gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.alpha = 1f;
        }

        private IEnumerator Fade(float target, float duration)
        {
            if (group == null)
                yield break;

            if (duration < 0f)
                duration = defaultDuration;

            group.gameObject.SetActive(true);
            group.blocksRaycasts = true;

            float from = group.alpha;

            if (duration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    group.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / duration));
                    yield return null;
                }
            }

            group.alpha = target;

            // 걷혔으면 아예 꺼둔다 - 알파 0짜리 판이 남아 있으면 그 뒤로 터치를 먹는 사고가 난다.
            if (target <= 0f)
            {
                group.blocksRaycasts = false;
                group.gameObject.SetActive(false);
            }
        }
    }
}
