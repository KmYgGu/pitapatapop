using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 러시 타임 개시를 알리는 띠. <b>왼쪽 밖에서 들어와 오른쪽 밖으로 빠져나간다.</b>
    ///
    /// <code>
    ///   1) 진입 - 왼쪽 밖에서 화면 안까지 <b>빠르게</b> (감속하며 도착)
    ///   2) 정독 - 화면 한가운데를 <b>아주 천천히</b> 가로지른다. 여기가 글자를 읽는 시간이다.
    ///   3) 퇴장 - 오른쪽 밖으로 <b>다시 빠르게</b> (가속하며 빠짐)
    /// </code>
    ///
    /// <b>멈추지 않고 계속 흐르는 게 핵심이다.</b> 가운데서 아예 멈춰 세우면 "애니메이션이
    /// 끊겼나" 싶고, 등속으로 지나가면 읽을 틈이 없다. 빠름 - 느림 - 빠름으로 속도만 바꾼다.
    ///
    /// 띠가 떠 있는 동안은 <see cref="ScreenDimOverlay"/> 가 켜져 조작이 막힌다 - 그건
    /// <see cref="RushTimeController"/> 가 맡고 이 컴포넌트는 움직이기만 한다.
    ///
    /// <see cref="StandUpTimeUI"/> 와 같은 이유로 <b>움직일 거리를 화면 폭에서 실제로 잰다</b> -
    /// 캔버스 가로는 기기 비율마다 달라서 고정 숫자로는 어떤 기기에서 화면 밖까지 안 나간다.
    /// </summary>
    public class RushTimeBannerUI : MonoBehaviour
    {
        [Header("껐다 켤 대상")]
        [Tooltip("띠 전체를 묶은 부모. 평소엔 꺼져 있다.")]
        [SerializeField] private RectTransform root;

        [Tooltip("실제로 좌우로 움직일 것. root 의 자식이어야 한다.")]
        [SerializeField] private RectTransform mover;

        [Tooltip("러시 타임을 할 수 있는 시간(초)을 정수로 보여줄 곳.")]
        [SerializeField] private Text secondsText;

        [Tooltip("초 숫자 뒤에 붙일 글자.")]
        [SerializeField] private string secondsSuffix = "초";

        [Header("타이밍")]
        [Tooltip("왼쪽 밖에서 들어오는 데 걸리는 시간(초). 짧을수록 스피디하다.")]
        [SerializeField] private float enterDuration = 0.28f;

        [Tooltip("천천히 가로지르는 시간(초). <b>플레이어가 읽는 시간이다.</b>")]
        [SerializeField] private float readDuration = 1.1f;

        [Tooltip("오른쪽 밖으로 빠지는 데 걸리는 시간(초).")]
        [SerializeField] private float exitDuration = 0.24f;

        [Header("이동 - 화면 폭 대비 비율")]
        [Tooltip("시작 위치. -0.5면 화면 폭의 절반만큼 왼쪽 밖에서 시작한다.")]
        [SerializeField] private float startOffset = -1.1f;

        [Tooltip("진입이 끝나 멈춰 서는 위치. 0이 화면 한가운데다.")]
        [SerializeField] private float readStartOffset = -0.06f;

        [Tooltip("천천히 가로지르기가 끝나는 위치. 시작보다 조금 오른쪽이라야 계속 흐르는 것처럼 보인다.")]
        [SerializeField] private float readEndOffset = 0.06f;

        [Tooltip("끝 위치. 화면 밖으로 확실히 나가야 한다.")]
        [SerializeField] private float endOffset = 1.1f;

        /// <summary>띠가 화면에 떠 있는 전체 시간(초). 조작을 막을 구간을 이 값에 맞춘다.</summary>
        public float TotalDuration =>
            Mathf.Max(0f, enterDuration) + Mathf.Max(0f, readDuration) + Mathf.Max(0f, exitDuration);

        /// <summary>지금 재생 중인지.</summary>
        public bool IsPlaying { get; private set; }

        private void Awake()
        {
            if (root != null)
                root.gameObject.SetActive(false);
        }

        /// <summary>띠를 한 번 흘려보낸다. 끝날 때까지 기다리는 코루틴이다.</summary>
        /// <param name="seconds">러시 타임을 할 수 있는 시간. 정수로 보여준다.</param>
        public IEnumerator Play(int seconds)
        {
            if (root == null || mover == null)
                yield break;

            IsPlaying = true;

            if (secondsText != null)
                secondsText.text = seconds + secondsSuffix;

            // 움직일 거리는 <b>부모 폭</b>에서 잰다. 캔버스 가로는 기기마다 다르다.
            float width = root.rect.width;
            if (width <= 1f)
            {
                // 아직 레이아웃이 안 잡혔으면 한 프레임 기다린 뒤 다시 잰다.
                root.gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();
                width = root.rect.width;
            }

            root.gameObject.SetActive(true);

            yield return Slide(width * startOffset, width * readStartOffset, enterDuration, EaseOut);
            yield return Slide(width * readStartOffset, width * readEndOffset, readDuration, Linear);
            yield return Slide(width * readEndOffset, width * endOffset, exitDuration, EaseIn);

            root.gameObject.SetActive(false);
            IsPlaying = false;
        }

        /// <summary>재생 중이면 즉시 치운다.</summary>
        public void Cancel()
        {
            IsPlaying = false;

            if (root != null)
                root.gameObject.SetActive(false);
        }

        private IEnumerator Slide(float fromX, float toX, float duration, System.Func<float, float> ease)
        {
            var pos = mover.anchoredPosition;

            if (duration <= 0f)
            {
                mover.anchoredPosition = new Vector2(toX, pos.y);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                float t = ease(Mathf.Clamp01(elapsed / duration));
                mover.anchoredPosition = new Vector2(Mathf.Lerp(fromX, toX, t), pos.y);

                yield return null;
            }

            mover.anchoredPosition = new Vector2(toX, pos.y);
        }

        private static float Linear(float t) => t;

        // 도착할 때 속도가 죽는다 - 밖에서 날아와 자리를 잡는 느낌.
        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        // 떠날 때 속도가 붙는다 - 읽고 나면 미련 없이 빠진다.
        private static float EaseIn(float t) => t * t;
    }
}
