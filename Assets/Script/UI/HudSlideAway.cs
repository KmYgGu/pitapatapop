using System.Collections;
using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// HUD 조각들을 <b>각자 가까운 화면 밖으로</b> 밀어냈다가 제자리로 되돌린다
    /// (2026-08-28 사용자 지시: 아파트 방에 들어가면 메인 HUD 는 그 컨텐츠와 상관이 없으니 비켜야 한다).
    ///
    /// <b>방향을 손으로 적지 않는다.</b> 조각이 판 가운데를 기준으로 어느 쪽에 있는지를 재서
    /// 위쪽 것은 위로, 아래쪽 것은 아래로, 옆의 것은 옆으로 나간다 - HUD 배치를 바꿔도
    /// 따라오고, 조각마다 방향을 적어두면 하나 빠뜨렸을 때 그것만 화면에 남는다.
    ///
    /// <b>지우지 않고 밀어낸다.</b> 꺼버리면 <c>ApartmentCameraRig</c> 가 HUD 판의 크기를 못 재서
    /// 카메라 여백 계산이 틀어진다(그쪽은 판이 살아 있어야 한다).
    /// </summary>
    public class HudSlideAway : MonoBehaviour
    {
        [Tooltip("밀어낼 것들. 보통 HUD 판의 자식 전부. 비워두면 <b>이 오브젝트의 자식</b>을 쓴다.")]
        [SerializeField] private RectTransform[] targets = new RectTransform[0];

        [Tooltip("나가고 들어오는 데 걸리는 시간(초).")]
        [SerializeField] private float duration = 0.28f;

        [Tooltip("가장자리를 넘어 더 밀어낼 여유(판 크기 대비 비율). 그림자나 글자 꼬리가 " +
                 "걸쳐 보이지 않게 넉넉히 준다.")]
        [SerializeField] private float overshoot = 0.25f;

        // 씬에 적혀 있던 제자리. 되돌아올 곳이라 <b>Awake 에서</b> 잡는다 - 밀어낸 뒤에 잡으면
        // 화면 밖이 제자리가 된다(RunAcrossUI 와 같은 함정).
        private Vector2[] basePositions;
        private Vector2[] awayOffsets;
        private Coroutine routine;

        /// <summary>지금 밀려나 있는지.</summary>
        public bool IsAway { get; private set; }

        private void Awake()
        {
            if (targets == null || targets.Length == 0)
                CollectChildren();

            basePositions = new Vector2[targets.Length];
            awayOffsets = new Vector2[targets.Length];

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                    continue;

                basePositions[i] = targets[i].anchoredPosition;
            }
        }

        private void CollectChildren()
        {
            var found = new System.Collections.Generic.List<RectTransform>();
            foreach (Transform child in transform)
            {
                if (child is RectTransform rect)
                    found.Add(rect);
            }

            targets = found.ToArray();
        }

        /// <summary>화면 밖으로 밀어낸다.</summary>
        public void SlideAway() => Play(true);

        /// <summary>제자리로 되돌린다.</summary>
        public void SlideBack() => Play(false);

        /// <summary>
        /// ⭐ <b>연출 없이 즉시</b> 화면 밖에 둔다(2026-09-02 사용자 지시).
        ///
        /// 미니게임에서 돌아와 방 화면이 바로 열릴 때 쓴다 - 그때는 HUD 가 <b>애초에 없었어야</b>
        /// 하는데, 밀려나는 걸 보여주면 "메인 화면이 한 번 떴다가 치워지는" 흔적으로 읽힌다.
        /// </summary>
        public void HideInstantly()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            IsAway = true;
            SnapAway();

            // ⚠ 씬에 들어온 <b>첫 프레임</b>에는 레터박스 배율(UiScaleToFit)이 아직 안 잡혔을 수
            // 있다 - 그 상태로 재면 덜 밀려나 조각이 띠 안에 남는다. 한 프레임 뒤에 한 번 더 맞춘다.
            if (isActiveAndEnabled)
                StartCoroutine(SnapAgainNextFrame());
        }

        private void SnapAway()
        {
            MeasureAwayOffsets();

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                    targets[i].anchoredPosition = basePositions[i] + awayOffsets[i];
            }
        }

        private IEnumerator SnapAgainNextFrame()
        {
            yield return null;

            // 그새 되돌아오라고 했으면 건드리지 않는다.
            if (IsAway && routine == null)
                SnapAway();
        }

        private void Play(bool away)
        {
            if (IsAway == away)
                return;

            IsAway = away;

            // 나가는 도중에 되돌아오라고 해도 <b>지금 자리에서</b> 이어져야 한다 - 코루틴을
            // 겹쳐 돌리면 두 개가 같은 값을 서로 덮어쓴다.
            if (routine != null)
                StopCoroutine(routine);

            if (away)
                MeasureAwayOffsets();

            routine = StartCoroutine(Move(away));
        }

        /// <summary>
        /// 조각마다 <b>어느 쪽으로 얼마나</b> 나갈지 정한다. 판 크기가 기기마다 다르므로
        /// 열 때마다 다시 잰다.
        /// </summary>
        private void MeasureAwayOffsets()
        {
            var parent = transform as RectTransform;
            if (parent == null)
                return;

            Vector2 half = MeasureScreenHalfInLocalUnits(parent);

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                    continue;

                // 판 가운데에서 이 조각이 어느 쪽에 있는지. 퍼센트 앵커라 anchoredPosition 은
                // 대개 0이므로 <b>localPosition</b> 을 봐야 한다(앵커가 반영된 실제 자리).
                Vector2 center = targets[i].localPosition;
                Vector2 size = targets[i].rect.size * 0.5f;

                bool horizontal = half.y <= 0.0001f
                                  || Mathf.Abs(center.x) / Mathf.Max(0.0001f, half.x)
                                     > Mathf.Abs(center.y) / Mathf.Max(0.0001f, half.y);

                if (horizontal)
                {
                    float sign = center.x >= 0f ? 1f : -1f;
                    float distance = half.x + size.x - sign * center.x;
                    awayOffsets[i] = new Vector2(sign * distance * (1f + overshoot), 0f);
                }
                else
                {
                    float sign = center.y >= 0f ? 1f : -1f;
                    float distance = half.y + size.y - sign * center.y;
                    awayOffsets[i] = new Vector2(0f, sign * distance * (1f + overshoot));
                }
            }
        }

        /// <summary>
        /// <b>화면</b>의 절반 크기를 이 판의 로컬 단위로 환산한다.
        ///
        /// <b>⚠ 판(HudContent)의 rect 를 그대로 쓰면 안 된다</b>(2026-08-28 사용자 신고:
        /// 기기를 바꾸면 HUD 가 다 안 숨겨졌다). 이 판은 <see cref="UiScaleToFit"/> 가
        /// <b>설계 크기 337.5x600 을 통째로 축소</b>해서 쓰므로, 좁은 기기에서는 판이 화면보다
        /// 작다 - 판 가장자리까지만 밀면 조각이 <b>레터박스 띠 안에 그대로 남는다.</b>
        /// 9:16 기기에서만 배율이 1이라 그때는 우연히 맞아 보였다.
        ///
        /// 그래서 <b>루트 캔버스</b>를 기준으로 재고, 두 판의 배율 차이만큼 되돌려 준다.
        /// </summary>
        private static Vector2 MeasureScreenHalfInLocalUnits(RectTransform parent)
            => UiScreenMetrics.ScreenHalfInLocalUnits(parent);

        private IEnumerator Move(bool away)
        {
            var from = new Vector2[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                    from[i] = targets[i].anchoredPosition;
            }

            float elapsed = 0f;
            while (duration > 0f && elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // 양쪽 끝이 뭉툭한 곡선. 한쪽만 감속하면 튀어나왔다 멎는 느낌이 난다.
                float eased = t * t * (3f - 2f * t);

                Apply(from, eased, away);
                yield return null;
            }

            Apply(from, 1f, away);
            routine = null;
        }

        private void Apply(Vector2[] from, float t, bool away)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                    continue;

                Vector2 to = away ? basePositions[i] + awayOffsets[i] : basePositions[i];
                targets[i].anchoredPosition = Vector2.LerpUnclamped(from[i], to, t);
            }
        }
    }
}
