using UnityEngine;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 퍼즐판을 가리는 이유. 여러 개가 동시에 걸릴 수 있어서(예: 공격 연출 중에 캐릭터가 대사를 함)
    /// 비트 플래그로 모아두고, 하나라도 남아 있으면 계속 가려둔다. 마지막 하나가 풀릴 때만 걷힌다.
    /// </summary>
    [System.Flags]
    public enum BoardDimReason
    {
        None = 0,

        /// <summary>캐릭터 대사창이 떠 있는 동안.</summary>
        Speech = 1 << 0,

        /// <summary>스탠드업 종료 - 조각이 불꽃이 되어 날아가기 시작해서 캐릭터 공격 모션이 끝날 때까지.</summary>
        StandUpFinish = 1 << 1,

        /// <summary>그 밖에 외부에서 직접 거는 경우(컷인 등, 아직 쓰는 곳 없음).</summary>
        Manual = 1 << 2
    }

    /// <summary>
    /// <b>퍼즐판 영역만</b> 어둡게 덮는 가림막. 화면 전체를 덮는 ScreenDimOverlay와 역할은 같지만
    /// 덮는 범위가 다르다 - 이게 켜져 있어도 위쪽 HUD(캐릭터, 체력바, 대사창)는 밝게 남아서
    /// "지금은 퍼즐이 아니라 저 위를 보라"는 뜻이 된다.
    ///
    /// <b>UI(Canvas)가 아니라 월드 스프라이트다.</b> 이 프로젝트의 Canvas는 Screen Space - Overlay라
    /// 그 위에 그려지는 UI는 월드에 있는 어떤 것보다도 무조건 앞에 온다 - 그러면 스탠드업 종료 때
    /// 날아가는 불꽃(월드 오브젝트)까지 같이 어두워져서, 정작 봐야 할 연출이 가려진다.
    /// 그래서 보드 배경판(BoardBackgroundPlate)과 같은 월드 스프라이트로 두고 정렬 순서로 앞뒤를 가른다:
    ///   보드 배경판(-10) &lt; 일반 조각(0~2) &lt; 드래그 중(100) &lt; <b>가림막(150)</b> &lt; 날아가는 불꽃(200)
    /// 이 숫자들의 기준점은 전부 PanelView에 있다(PanelView.DimOverlaySortingOrder).
    ///
    /// 터치 차단은 콜라이더가 아니라 BoardInputController.IsBoardCovered로 한다 -
    /// BoardInputController는 EventSystem이 아니라 Input.GetMouseButton을 직접 읽기 때문.
    ///
    /// 켜고 끄는 건 이유(BoardDimReason)별로 따로 관리한다. 공격 연출 도중에 캐릭터가 대사를 하는
    /// 상황이 자연스럽게 생기는데, 둘을 한 bool로 다루면 대사가 끝나는 순간 아직 진행 중인 공격
    /// 연출까지 가림이 풀려버린다.
    /// </summary>
    public class BoardDimOverlay : MonoBehaviour
    {
        [Header("가림막")]
        [Tooltip("퍼즐판을 덮을 스프라이트. 비워두면 이 오브젝트의 SpriteRenderer를 쓰고, " +
                 "그것도 없으면 시작할 때 하나 만들어 붙인다(보드 배경판과 같은 방식).")]
        [SerializeField] private SpriteRenderer overlayRenderer;

        [SerializeField] private Color dimColor = Color.black;

        [Tooltip("완전히 어두워졌을 때의 알파값.")]
        [SerializeField] private float dimAlpha = 0.55f;

        [Tooltip("어두워지고 밝아지는 데 걸리는 시간(초).")]
        [SerializeField] private float fadeDuration = 0.15f;

        [Header("퍼즐판 위치 추적")]
        [SerializeField] private BoardView boardView;

        [Tooltip("퍼즐판 바깥으로 더 덮을 여유(월드 유닛). 0이면 배경판 경계에 딱 맞는다.")]
        [SerializeField] private float padding = 0f;

        [Header("보드 입력")]
        [Tooltip("가려져 있는 동안 조작을 막을 대상이자, 스탠드업 종료 연출 알림을 받을 대상. " +
                 "비워두면 어두워지기만 하고 조작 차단도 스탠드업 자동 연동도 안 된다.")]
        [SerializeField] private BoardInputController boardInput;

        [Header("자동 연동 (선택)")]
        [Tooltip("이 대사창이 뜨고 닫힐 때 자동으로 켜고 끈다.")]
        [SerializeField] private SpeechBubbleUI speechBubble;

        /// <summary>지금 걸려 있는 이유들. 하나라도 있으면 가려져 있다.</summary>
        public BoardDimReason ActiveReasons { get; private set; }

        public bool IsDimmed => ActiveReasons != BoardDimReason.None;

        /// <summary>가려진 상태가 바뀐 순간 발행. 제한시간 타이머를 멈추고 재개하는 데 쓴다.</summary>
        public event System.Action<bool> OnDimChanged;

        private float currentAlpha;
        private float targetAlpha;
        private bool fitted;

        private void Awake()
        {
            if (overlayRenderer == null)
                overlayRenderer = GetComponent<SpriteRenderer>();

            // 씬에 미리 안 만들어뒀으면 여기서 한 번 만든다 - BoardView가 보드 배경판을 만드는 것과
            // 같은 방식이다. 시작할 때 딱 한 번이고 이후로는 이 하나를 계속 재사용한다.
            if (overlayRenderer == null)
                overlayRenderer = gameObject.AddComponent<SpriteRenderer>();

            if (overlayRenderer != null)
            {
                // 스프라이트를 안 넣어뒀으면 보드 배경판과 같은 흰 사각형을 빌려 쓴다.
                if (overlayRenderer.sprite == null)
                    overlayRenderer.sprite = PanelView.FallbackSprite;

                overlayRenderer.sortingOrder = PanelView.DimOverlaySortingOrder;
            }

            ApplyAlpha(0f);
        }

        private void OnEnable()
        {
            if (speechBubble != null)
            {
                speechBubble.OnShown += HandleSpeechShown;
                speechBubble.OnHidden += HandleSpeechHidden;
            }

            if (boardInput != null)
            {
                boardInput.OnStandUpEndSequenceStart += HandleStandUpFinishStart;
                boardInput.OnStandUpTimeEnd += HandleStandUpFinishEnd;
            }
        }

        private void OnDisable()
        {
            if (speechBubble != null)
            {
                speechBubble.OnShown -= HandleSpeechShown;
                speechBubble.OnHidden -= HandleSpeechHidden;
            }

            if (boardInput != null)
            {
                boardInput.OnStandUpEndSequenceStart -= HandleStandUpFinishStart;
                boardInput.OnStandUpTimeEnd -= HandleStandUpFinishEnd;
            }

            // 가린 채로 꺼지면 조작이 영영 막힌 상태로 남는다.
            ClearAll();
        }

        private void HandleSpeechShown() => SetReason(BoardDimReason.Speech, true);
        private void HandleSpeechHidden() => SetReason(BoardDimReason.Speech, false);
        private void HandleStandUpFinishStart() => SetReason(BoardDimReason.StandUpFinish, true);
        private void HandleStandUpFinishEnd() => SetReason(BoardDimReason.StandUpFinish, false);

        /// <summary>
        /// 이유 하나를 걸거나 푼다. 이유가 하나라도 남아 있으면 계속 가려진 상태를 유지하고,
        /// 마지막 하나가 풀릴 때 밝아진다.
        /// </summary>
        public void SetReason(BoardDimReason reason, bool active)
        {
            if (reason == BoardDimReason.None)
                return;

            var next = active ? (ActiveReasons | reason) : (ActiveReasons & ~reason);
            if (next == ActiveReasons)
                return;

            bool wasDimmed = IsDimmed;
            ActiveReasons = next;
            ApplyState();

            if (IsDimmed != wasDimmed)
                OnDimChanged?.Invoke(IsDimmed);
        }

        /// <summary>모든 이유를 한 번에 푼다. 배틀이 끝나거나 씬을 정리할 때처럼 확실히 걷어야 할 때.</summary>
        public void ClearAll()
        {
            if (ActiveReasons == BoardDimReason.None)
                return;

            ActiveReasons = BoardDimReason.None;
            ApplyState();
            OnDimChanged?.Invoke(false);
        }

        private void ApplyState()
        {
            targetAlpha = IsDimmed ? dimAlpha : 0f;

            if (boardInput != null)
                boardInput.IsBoardCovered = IsDimmed;
        }

        private void LateUpdate()
        {
            FitToBoardIfNeeded();
            StepFade();
        }

        /// <summary>
        /// 퍼즐판의 실제 월드 영역(배경판 포함)에 맞춰 위치와 크기를 잡는다. 보드는 한 번 만들어지면
        /// 크기가 변하지 않으므로 준비된 뒤 한 번만 계산한다. 보드가 아직 없으면(GameEntryPoint.Start
        /// 이전) 조용히 넘어가고 준비되는 순간 적용된다.
        /// </summary>
        private void FitToBoardIfNeeded()
        {
            if (fitted || overlayRenderer == null || boardView == null || !boardView.IsInitialized)
                return;

            Bounds bounds = boardView.GetBoardVisualBounds();
            Vector2 spriteSize = overlayRenderer.sprite != null
                ? (Vector2)overlayRenderer.sprite.bounds.size
                : Vector2.one;

            float width = bounds.size.x + padding * 2f;
            float height = bounds.size.y + padding * 2f;

            transform.position = new Vector3(bounds.center.x, bounds.center.y, 0f);
            transform.localScale = new Vector3(
                spriteSize.x > 0f ? width / spriteSize.x : 1f,
                spriteSize.y > 0f ? height / spriteSize.y : 1f,
                1f);

            fitted = true;
        }

        private void StepFade()
        {
            if (Mathf.Approximately(currentAlpha, targetAlpha))
                return;

            float step = fadeDuration > 0f ? Time.deltaTime / fadeDuration : 1f;
            ApplyAlpha(Mathf.MoveTowards(currentAlpha, targetAlpha, step));
        }

        private void ApplyAlpha(float alpha)
        {
            currentAlpha = alpha;

            if (overlayRenderer == null)
                return;

            var color = dimColor;
            color.a = alpha;
            overlayRenderer.color = color;

            // 완전히 투명할 땐 그리지 않는다 - 퍼즐판 전체를 덮는 큰 사각형이라 켜둘 이유가 없다.
            overlayRenderer.enabled = alpha > 0.001f;
        }
    }
}
