using UnityEngine;
using UnityEngine.UI;
using Spine.Unity;

namespace JojoPuzzle.UI
{
    /// <summary>말하는 쪽. 아군이면 캐릭터 이미지가 왼쪽, 적이면 오른쪽에 온다.</summary>
    public enum SpeechSide
    {
        Player,
        Enemy
    }

    /// <summary>
    /// 캐릭터 대사창. 캐릭터 이미지 + 살짝 기울어진 말풍선이 한 덩어리인 오버레이로,
    /// 편성 캐릭터와 적이 같은 창을 돌려쓴다.
    ///
    /// 씬에는 <b>아군 배치(이미지 왼쪽 / 말풍선 오른쪽)</b> 기준으로만 그려두면 된다. 적이 말할 때는
    /// Awake에서 캡처해둔 그 배치를 좌우로 뒤집어서 쓴다 - 적용도 앵커/피벗/오프셋을 뒤집는 방식이라
    /// 이 프로젝트의 "위치·크기는 전부 퍼센트 앵커로만" 규칙과 충돌하지 않는다(anchoredPosition을
    /// 직접 만지지 않는다). 기울기도 부호만 뒤집어서 서로 마주보는 방향으로 기운다.
    ///
    /// PauseMenuUI와 같은 이유로 이 컴포넌트는 <b>항상 켜져 있는 오브젝트</b>에 붙이고, 실제로 보이는
    /// 부분(root)만 껐다 켠다 - 비활성 오브젝트에 붙으면 Awake가 돌지 않아 배치 캡처 자체를 못 한다.
    ///
    /// 코루틴 대신 Update에서 경과 시간만 굴린다(HitFlinchUI와 같은 방식). 표시 중에 다음 대사가
    /// 들어와도 코루틴 중복 걱정 없이 그 자리에서 새로 시작되고, 안 떠 있을 땐 첫 줄에서 바로 빠진다.
    /// 런타임에 오브젝트를 만들거나 버리지 않는다 - 창 하나를 계속 재사용한다.
    /// </summary>
    public class SpeechBubbleUI : MonoBehaviour
    {
        [Header("껐다 켤 대상")]
        [Tooltip("캐릭터 이미지 + 말풍선을 묶은 부모. 평소엔 꺼져 있다.")]
        [SerializeField] private RectTransform root;

        [Header("구성 요소")]
        [SerializeField] private RectTransform portrait;
        [SerializeField] private Image portraitImage;

        [Tooltip("대사창에 스파인 캐릭터를 띄울 때 쓰는 SkeletonGraphic. " +
                 "메뉴 'JojoPuzzle > Spine > 대사창에 Spine 캐릭터 배치'로 만들어 연결한다. " +
                 "비어 있으면 정지 이미지(portraitImage)만 쓴다.")]
        [SerializeField] private SkeletonGraphic portraitSpine;

        [Tooltip("<b>portraitSpine 을 씬에 미리 놓을 수 없을 때</b> 대신 쓰는 런타임 생성기. " +
                 "SkeletonGraphic 은 아틀라스 페이지마다 자식 CanvasRenderer 를 Spine 내부 코드가 " +
                 "만들어야 해서 씬 YAML 로는 못 그린다 - 손으로 만든 화면에서는 이쪽을 쓴다. " +
                 "둘 다 있으면 portraitSpine 이 이긴다(대사가 잦은 배틀 쪽이 더 가볍다).")]
        [SerializeField] private SpineCharacterView portraitSpineView;

        [SerializeField] private RectTransform bubble;
        [SerializeField] private Text messageText;

        [Header("기울기")]
        [Tooltip("말풍선이 기우는 각도(도). 아군 기준이고 적일 땐 부호가 뒤집힌다. 0이면 안 기운다.")]
        [SerializeField] private float tiltAngle = -4f;

        [Tooltip("캐릭터 이미지도 같이 기울일지. 끄면 말풍선만 기운다.")]
        [SerializeField] private bool tiltPortraitToo;

        [Header("표시")]
        [Tooltip("Show에 시간을 따로 주지 않았을 때 유지할 시간(초).")]
        [SerializeField] private float defaultHoldDuration = 2f;

        [Tooltip("톡 튀어나오는 시간(초). 0이면 즉시 나타난다.")]
        [SerializeField] private float popInDuration = 0.14f;

        [Tooltip("튀어나올 때 시작 크기.")]
        [SerializeField] private float popInStartScale = 0.7f;

        [Tooltip("튀어나오는 도중 원래 크기를 얼마나 넘어섰다 돌아올지.")]
        [SerializeField] private float popInOvershoot = 0.12f;

        /// <summary>대사창이 떠오른 순간 발행. 이미 떠 있는 상태에서 다음 대사로 갈아끼울 땐 발행하지 않는다.</summary>
        public event System.Action OnShown;

        /// <summary>대사창이 닫힌 순간 발행. 퍼즐판을 가려두던 연출(BoardDimOverlay)이 이걸로 풀린다.</summary>
        public event System.Action OnHidden;

        /// <summary>지금 대사창이 떠 있는지.</summary>
        public bool IsShowing => elapsed >= 0f;

        /// <summary>지금 누가 말하고 있는지. 떠 있지 않으면 마지막으로 말한 쪽.</summary>
        public SpeechSide CurrentSide { get; private set; }

        // 씬에 그려둔 아군 배치를 그대로 캡처해둔 것. 적일 땐 이걸 좌우로 뒤집어 적용한다.
        private RectLayout portraitLayout;

        // 글자 칸의 배치도 캡처해둔다. 초상화가 말풍선 <b>안쪽</b>에 들어오면서 둘이 같은 상자를
        // 나눠 쓰게 됐기 때문에, 적이 말할 때 초상화만 오른쪽으로 옮기면 글자와 겹친다.
        // 글자도 함께 뒤집어야 "초상화 오른쪽 / 글자 왼쪽"이 된다.
        private RectLayout messageLayout;
        private RectLayout bubbleLayout;
        private TextAnchor messageAlignment;

        // 음수면 안 떠 있음 - 0 이상일 때만 Update가 일한다.
        private float elapsed = -1f;

        // 음수면 "직접 Hide를 부를 때까지 계속" - 보스 등장 대사처럼 붙잡아두고 싶을 때 쓴다.
        private float holdDuration;

        private bool popInFinished;

        private void Awake()
        {
            portraitLayout = RectLayout.Capture(portrait);
            bubbleLayout = RectLayout.Capture(bubble);
            messageLayout = RectLayout.Capture(messageText != null ? (RectTransform)messageText.transform : null);

            if (messageText != null)
            {
                messageAlignment = messageText.alignment;

                // 씬에 적어둔 크기를 <b>최대</b>로 삼는다 - 대사창마다 상자가 달라서 그 값이
                // 곧 "이 창에 어울리는 제일 큰 글씨"다(ApartmentHudController 와 같은 방식).
                messageBaseFontSize = messageText.fontSize;

                // <b>Best Fit 은 끈다.</b> 켜져 있으면 사다리로 고른 크기를 유니티가 곧바로
                // 덮어써서 두 규칙이 싸운다 - 배틀 대사창만 켜져 있어 창마다 글씨 규칙이
                // 다르기도 했다(2026-08-28).
                messageText.resizeTextForBestFit = false;
            }

            if (root != null)
                root.gameObject.SetActive(false);
        }

        /// <summary>
        /// 대사창의 스파인 캐릭터를 이 캐릭터로 맞춘다. 같은 스켈레톤이면 다시 만들지 않고
        /// 애니메이션만 바꾼다 - 대사가 잦은데 매번 Initialize를 하면 그때마다 메시를 새로 만든다.
        /// </summary>
        private void ApplySpinePortrait(SkeletonDataAsset spine, string talkAnimation, bool mirror)
        {
            if (portraitSpine.skeletonDataAsset != spine)
            {
                portraitSpine.skeletonDataAsset = spine;
                portraitSpine.Initialize(true); // 이걸 빼면 데이터만 바뀌고 화면은 이전 캐릭터 그대로다
            }

            if (portraitSpine.Skeleton == null)
                return;

            // 적이 말할 때는 창 배치가 좌우로 뒤집히므로 캐릭터도 같이 뒤집어 서로 마주보게 한다.
            portraitSpine.Skeleton.ScaleX = mirror ? -1f : 1f;

            // 재생은 SkeletonGraphic이 아니라 옆에 붙은 SkeletonAnimation이 담당한다
            // (4.3부터 렌더링과 재생이 두 컴포넌트로 나뉘었다 - SpinePortraitSetup 주석 참고).
            var player = portraitSpine.GetComponent<SkeletonAnimation>();
            var animation = player != null ? player.AnimationState : null;
            if (animation == null)
                return;

            string name = talkAnimation;
            if (string.IsNullOrEmpty(name))
                name = SpinePlayback.Idle;

            // 없는 동작은 그 캐릭터의 idle 로 메운다(규칙은 SpinePlayback 한 곳에 있다).
            SpinePlayback.Play(animation, portraitSpine.Skeleton.Data, name, true);
        }

        /// <summary>대사창을 띄운다. 유지 시간은 인스펙터의 기본값을 쓴다.</summary>
        public void Show(SpeechSide side, Sprite characterSprite, string message)
            => Show(side, characterSprite, message, defaultHoldDuration);

        /// <summary>
        /// 대사창을 띄운다. holdSeconds가 음수면 Hide()를 부를 때까지 계속 떠 있는다.
        /// 이미 떠 있는 상태에서 불러도 안전하다 - 그 자리에서 새 대사로 갈아끼우고 처음부터 다시 튀어나온다.
        /// </summary>
        public void Show(SpeechSide side, Sprite characterSprite, string message, float holdSeconds)
            => Show(side, characterSprite, null, null, message, holdSeconds);

        /// <summary>
        /// 스파인 캐릭터로 대사창을 띄운다. spine이 있으면 정지 이미지 대신 애니메이션이 나온다.
        ///
        /// 스켈레톤을 갈아끼울 때 Initialize(true)를 반드시 부른다 - 안 부르면 데이터만 바뀌고
        /// 화면은 이전 캐릭터 그대로 남는다(spine-unity에서 흔히 겪는 함정).
        /// </summary>
        public void Show(SpeechSide side, Sprite characterSprite, SkeletonDataAsset spine, string talkAnimation,
            string message, float holdSeconds)
        {
            bool wasShowing = IsShowing;

            CurrentSide = side;
            holdDuration = holdSeconds;

            bool mirror = side == SpeechSide.Enemy;

            RectLayout.Apply(portrait, portraitLayout, mirror);
            RectLayout.Apply(bubble, bubbleLayout, mirror);
            RectLayout.Apply(messageText != null ? (RectTransform)messageText.transform : null, messageLayout, mirror);

            float tilt = mirror ? -tiltAngle : tiltAngle;
            if (bubble != null)
                bubble.localRotation = Quaternion.Euler(0f, 0f, tilt);
            // 초상화는 말풍선 자식이라 상자 기울기를 그대로 물려받는다 - 글자와 똑같은 방식이다.
            // 여기서 따로 돌리지 않는다. 캐릭터만 반대로 세우면 상자와 따로 노는 데다,
            // 기울어진 상자에 똑바로 선 캐릭터가 들어가면 잘리는 모양도 어색해진다.
            // tiltPortraitToo를 켜면 부모 기울기 위에 그만큼 더 기운다.
            if (portrait != null)
                portrait.localRotation = Quaternion.Euler(0f, 0f, tiltPortraitToo ? tilt : 0f);

            bool useSpine = spine != null && (portraitSpine != null || portraitSpineView != null);

            if (portraitImage != null)
            {
                portraitImage.sprite = characterSprite;
                // 스프라이트가 없으면 빈 사각형이 보이므로 아예 끈다(대사만 띄우는 연출도 가능하게).
                // 스파인을 쓸 때도 끈다 - 둘이 겹쳐 보이면 안 된다.
                portraitImage.enabled = !useSpine && characterSprite != null;
            }

            if (portraitSpine != null)
            {
                portraitSpine.gameObject.SetActive(useSpine);
                if (useSpine)
                    ApplySpinePortrait(spine, talkAnimation, mirror);
            }
            // portraitSpineView 쪽은 root 를 켠 <b>다음에</b> 만들어야 한다 - 아래 참고.

            if (messageText != null)
            {
                messageText.text = message;
                messageText.alignment = MirrorAlignment(messageAlignment, mirror);
            }

            if (root != null)
            {
                root.gameObject.SetActive(true);
                root.localScale = popInDuration > 0f ? Vector3.one * popInStartScale : Vector3.one;
            }

            // <b>반드시 root 를 켠 뒤다</b> - 꺼져 있는 오브젝트는 rect 가 0이라 상자를 못 잰다.
            FitMessageToBubble();

            // <b>반드시 root 를 켠 뒤다.</b> SpineCharacterView 는 칸의 실제 크기를 재서 배율을
            // 잡는데, 꺼져 있는 오브젝트는 rect 가 0이라 그 측정이 조용히 실패한다(그러면
            // 캐릭터가 화면을 뒤덮는다). 위의 portraitSpine 은 씬에 이미 있는 것을 켜고 끄기만
            // 하므로 그 제약이 없어서 순서를 옮기지 않았다.
            if (portraitSpine == null && portraitSpineView != null)
            {
                portraitSpineView.gameObject.SetActive(useSpine);
                if (useSpine)
                {
                    portraitSpineView.SetFlipX(mirror);
                    portraitSpineView.Show(spine, talkAnimation);
                }
                else
                {
                    portraitSpineView.Clear();
                }
            }

            elapsed = 0f;
            popInFinished = popInDuration <= 0f;

            if (!wasShowing)
                OnShown?.Invoke();
        }

        /// <summary>대사창을 즉시 닫는다. 이미 닫혀 있으면 아무 일도 하지 않는다.</summary>
        // 씬에 적어둔 원래 글꼴 크기. <b>매번 여기서 다시 시작</b>해야 한다 - 줄어든 크기에서
        // 또 줄이면 짧은 대사가 와도 영영 안 커진다(ApartmentHudController 와 같은 함정).
        private int messageBaseFontSize;

        /// <summary>
        /// 대사 길이에 맞춰 글자 크기를 고른다(2026-08-28 사용자 지시: 긴 대사가 잘렸다).
        ///
        /// <b>상자를 켠 <em>뒤에</em> 재야 한다</b> - 꺼져 있으면 rect 가 0이라 조용히 실패한다.
        /// 켠 직후에는 레이아웃이 아직 반영되지 않았을 수 있어 <c>ForceUpdateCanvases</c> 로
        /// 한 번 밀어준 뒤 잰다(`RushTimeBannerUI` 가 화면 폭을 잴 때와 같은 이유).
        /// </summary>
        private void FitMessageToBubble()
        {
            if (messageText == null || messageBaseFontSize <= 0)
                return;

            Canvas.ForceUpdateCanvases();

            var rect = messageText.rectTransform.rect;
            UITypography.FitToBox(messageText, rect.width, rect.height, messageBaseFontSize);
        }

        public void Hide()
        {
            bool wasShowing = IsShowing;
            elapsed = -1f;

            if (root != null)
            {
                root.localScale = Vector3.one; // 다음에 띄울 때 찌그러진 채로 시작하지 않도록
                root.gameObject.SetActive(false);
            }

            if (wasShowing)
                OnHidden?.Invoke();
        }

        private void Update()
        {
            if (elapsed < 0f)
                return;

            elapsed += Time.deltaTime; // timeScale을 따르므로 일시정지 중엔 함께 멈춤

            if (!popInFinished)
            {
                float t = Mathf.Clamp01(elapsed / popInDuration);

                // 감속으로 커지면서 중간에 원래 크기를 살짝 넘었다가(sin이 중앙에서 최대) 끝에서 정확히 1이 된다.
                float eased = 1f - (1f - t) * (1f - t);
                float scale = Mathf.Lerp(popInStartScale, 1f, eased) + popInOvershoot * Mathf.Sin(t * Mathf.PI);

                if (root != null)
                    root.localScale = new Vector3(scale, scale, 1f);

                if (t >= 1f)
                {
                    popInFinished = true;
                    if (root != null)
                        root.localScale = Vector3.one; // 매 프레임 건드리지 않도록 여기서 한 번만 확정
                }
            }

            if (holdDuration < 0f)
                return; // 직접 닫을 때까지 유지

            if (elapsed >= popInDuration + holdDuration)
                Hide();
        }

        private void OnDisable()
        {
            // 떠 있는 채로 꺼지면 다음에 켜졌을 때 그 상태가 그대로 남으므로 정리한다.
            if (elapsed >= 0f)
                Hide();
        }

        /// <summary>왼쪽/오른쪽 정렬만 뒤집는다. 가운데 정렬은 뒤집어도 같으므로 그대로 둔다.</summary>
        private static TextAnchor MirrorAlignment(TextAnchor alignment, bool mirror)
        {
            if (!mirror)
                return alignment;

            switch (alignment)
            {
                case TextAnchor.UpperLeft: return TextAnchor.UpperRight;
                case TextAnchor.UpperRight: return TextAnchor.UpperLeft;
                case TextAnchor.MiddleLeft: return TextAnchor.MiddleRight;
                case TextAnchor.MiddleRight: return TextAnchor.MiddleLeft;
                case TextAnchor.LowerLeft: return TextAnchor.LowerRight;
                case TextAnchor.LowerRight: return TextAnchor.LowerLeft;
                default: return alignment;
            }
        }

        /// <summary>
        /// RectTransform의 배치를 통째로 담아두는 값. 좌우 반전은 여기 담긴 값들의 x만 뒤집어서
        /// 만들어내므로, 씬에서 아군 배치 하나만 잡아두면 적 배치는 자동으로 나온다.
        /// </summary>
        private struct RectLayout
        {
            public Vector2 anchorMin;
            public Vector2 anchorMax;
            public Vector2 pivot;
            public Vector2 offsetMin;
            public Vector2 offsetMax;

            public static RectLayout Capture(RectTransform rt)
            {
                if (rt == null)
                    return default;

                return new RectLayout
                {
                    anchorMin = rt.anchorMin,
                    anchorMax = rt.anchorMax,
                    pivot = rt.pivot,
                    offsetMin = rt.offsetMin,
                    offsetMax = rt.offsetMax
                };
            }

            public static void Apply(RectTransform rt, in RectLayout layout, bool mirror)
            {
                if (rt == null)
                    return;

                if (!mirror)
                {
                    rt.anchorMin = layout.anchorMin;
                    rt.anchorMax = layout.anchorMax;
                    rt.pivot = layout.pivot;
                    rt.offsetMin = layout.offsetMin;
                    rt.offsetMax = layout.offsetMax;
                    return;
                }

                // 가로만 거울처럼 뒤집는다. 앵커는 왼쪽 끝과 오른쪽 끝이 서로 자리를 바꾸고(1에서 뺀 값),
                // 오프셋도 왼쪽 여백과 오른쪽 여백이 맞바뀐다(부호가 반대로 저장돼 있어서 -를 붙인다).
                rt.anchorMin = new Vector2(1f - layout.anchorMax.x, layout.anchorMin.y);
                rt.anchorMax = new Vector2(1f - layout.anchorMin.x, layout.anchorMax.y);
                rt.pivot = new Vector2(1f - layout.pivot.x, layout.pivot.y);
                rt.offsetMin = new Vector2(-layout.offsetMax.x, layout.offsetMin.y);
                rt.offsetMax = new Vector2(-layout.offsetMin.x, layout.offsetMax.y);
            }
        }
    }
}
