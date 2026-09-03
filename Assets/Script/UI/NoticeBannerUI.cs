using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 한 줄짜리 알림 띠. <b>톡 튀어나와 잠깐 머물다 사라진다.</b>
    /// 지금은 "타임 오버" 를 알리는 데 쓴다 - 마무리 처리가 시작되기 전에 <b>플레이어가 무슨 일이
    /// 일어났는지 알아차릴 시간</b>을 주기 위해서다(2026-08-25 사용자 지시).
    ///
    /// <see cref="RushTimeBannerUI"/> 와 나눠 둔 이유: 그쪽은 <b>흘러가는</b> 띠라 진입·정독·퇴장의
    /// 속도 곡선이 연출의 핵심이고, 이쪽은 그냥 떴다 지는 알림이다. 한 컴포넌트에 두 성격을 넣으면
    /// 인스펙터가 서로 안 쓰는 값으로 뒤덮인다.
    ///
    /// 앞으로 다른 알림(콤보 달성, 보스 등장 등)에도 그대로 재사용할 수 있게 문구를 인자로 받는다.
    /// </summary>
    public class NoticeBannerUI : MonoBehaviour
    {
        [Header("껐다 켤 대상")]
        [Tooltip("띠 전체를 묶은 부모. 평소엔 꺼져 있다.")]
        [SerializeField] private RectTransform root;

        [Tooltip("문구를 그릴 곳.")]
        [SerializeField] private Text messageText;

        [Header("타이밍")]
        [Tooltip("톡 튀어나오는 시간(초).")]
        [SerializeField] private float popInDuration = 0.22f;

        [Tooltip("머무는 시간(초). <b>플레이어가 읽는 시간이다.</b>")]
        [SerializeField] private float holdDuration = 1.1f;

        [Tooltip("사라지는 시간(초).")]
        [SerializeField] private float popOutDuration = 0.22f;

        [Header("모양")]
        [Tooltip("튀어나올 때의 시작 크기. 1보다 크면 크게 나타났다 제 크기로 줄어든다.")]
        [SerializeField] private float popInStartScale = 1.45f;

        [Tooltip("사라질 때의 끝 크기.")]
        [SerializeField] private float popOutEndScale = 1.15f;

        /// <summary>띠가 화면에 떠 있는 전체 시간(초).</summary>
        public float TotalDuration =>
            Mathf.Max(0f, popInDuration) + Mathf.Max(0f, holdDuration) + Mathf.Max(0f, popOutDuration);

        /// <summary>지금 재생 중인지.</summary>
        public bool IsPlaying { get; private set; }

        private CanvasGroup group;

        private void Awake()
        {
            if (root == null)
                return;

            // 통째로 흐려지게 하려고 CanvasGroup 을 쓴다. 글자와 판을 따로 흐리면 층이 따로 논다.
            group = root.GetComponent<CanvasGroup>();
            if (group == null)
                group = root.gameObject.AddComponent<CanvasGroup>();

            root.gameObject.SetActive(false);
        }

        /// <summary>띠를 한 번 띄운다. 끝날 때까지 기다리는 코루틴이다.</summary>
        public IEnumerator Play(string message)
        {
            if (root == null)
                yield break;

            IsPlaying = true;

            if (messageText != null)
                messageText.text = message;

            root.gameObject.SetActive(true);

            yield return Scale(popInStartScale, 1f, popInDuration, 0f, 1f, EaseOut);

            if (holdDuration > 0f)
                yield return new WaitForSeconds(holdDuration);

            yield return Scale(1f, popOutEndScale, popOutDuration, 1f, 0f, EaseIn);

            root.gameObject.SetActive(false);
            root.localScale = Vector3.one;

            if (group != null)
                group.alpha = 1f;

            IsPlaying = false;
        }

        /// <summary>재생 중이면 즉시 치운다.</summary>
        public void Cancel()
        {
            IsPlaying = false;

            if (root == null)
                return;

            root.gameObject.SetActive(false);
            root.localScale = Vector3.one;

            if (group != null)
                group.alpha = 1f;
        }

        private IEnumerator Scale(float fromScale, float toScale, float duration,
            float fromAlpha, float toAlpha, System.Func<float, float> ease)
        {
            if (duration <= 0f)
            {
                root.localScale = Vector3.one * toScale;
                if (group != null)
                    group.alpha = toAlpha;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                float t = ease(Mathf.Clamp01(elapsed / duration));
                root.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, t);

                if (group != null)
                    group.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);

                yield return null;
            }

            root.localScale = Vector3.one * toScale;
            if (group != null)
                group.alpha = toAlpha;
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
        private static float EaseIn(float t) => t * t;
    }
}
