using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 공지사항·이벤트 배너. 일정 시간마다 저절로 다음 장으로 넘어가고, <b>손으로 밀어서</b>
    /// 다음/이전 장을 볼 수도 있다. 끝에 닿으면 <b>양쪽 다 반대편으로 이어진다</b>
    /// (마지막 → 첫 번째, 첫 번째 → 마지막. 2026-08-24 기획).
    ///
    /// <b>구조</b>: <c>viewport</c>(마스크) 안에 <c>content</c> 가 있고, 그 자식 하나가 배너 한 장이다.
    /// 각 장은 앵커를 <c>(칸, 0)~(칸+1, 1)</c> 로 잡아 <b>자기 칸 번호만큼 오른쪽에</b> 놓이므로,
    /// content 를 왼쪽으로 한 폭씩 밀면 다음 장이 나온다. 폭을 숫자로 적을 필요가 없다.
    ///
    /// <b>순환은 띠를 늘려서가 아니라 "장을 옮겨서" 한다.</b> 마지막 장에서 더 가려면 첫 장을
    /// <c>칸 N</c> 으로 미리 옮겨두고 그쪽으로 민다. 안 그러면 넘어가는 동안 빈칸이 보인다.
    /// 넘어간 뒤에는 <see cref="Normalize"/> 가 모든 장을 제 칸으로 되돌리므로,
    /// <b>가만히 있을 때는 언제나 "장 i 는 칸 i, content 는 -pageIndex×폭"</b> 이라는 상태가 유지된다.
    ///
    /// <b>드래그를 받으려면 raycastTarget 이 켜진 Graphic 이 필요하다.</b> 지금은
    /// <c>BannerViewport</c> 의 Image 가 그 역할이고, 이벤트가 부모인 여기까지 올라온다.
    /// 배너 장들은 꺼져 있어 그 아래로 통과한다.
    /// </summary>
    public class EventBannerRotator : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>순환용으로 빌려 쓰는 칸이 없음을 뜻하는 값.</summary>
        private const int NoBorrowedSlot = int.MinValue;

        /// <summary>순환하려면 장이 최소 3개는 있어야 한다 - 2개면 빌려 갈 장이 제자리에도 있어야 해서 겹친다.</summary>
        private const int MinPagesForWrap = 3;

        [Header("구조")]
        [Tooltip("잘라내는 창. 이 폭이 배너 한 장의 폭이 된다.")]
        [SerializeField] private RectTransform viewport;

        [Tooltip("배너 장들을 담고 있는 것. 자식 수가 곧 장 수다.")]
        [SerializeField] private RectTransform content;

        [Tooltip("몇 번째 장인지 보여주는 점들. 없어도 된다.")]
        [SerializeField] private Image[] pageDots;

        [Header("시간")]
        [Tooltip("한 장이 머무는 시간(초). 손으로 밀면 이 시계는 처음부터 다시 간다.")]
        [SerializeField] private float holdDuration = 4f;

        [Tooltip("넘어가는 데 걸리는 시간(초).")]
        [SerializeField] private float slideDuration = 0.35f;

        [Header("드래그")]
        [Tooltip("장 폭의 몇 배만큼 끌어야 넘어가는지. 이보다 덜 끌면 제자리로 돌아온다.")]
        [Range(0.05f, 0.9f)]
        [SerializeField] private float swipeThreshold = 0.2f;

        [Header("점 색")]
        [SerializeField] private Color activeDotColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color inactiveDotColor = new Color(1f, 1f, 1f, 0.32f);

        private int pageIndex;
        private float holdRemaining;

        /// <summary>슬라이드가 향하는 칸. 순환 중에는 -1 이나 N 처럼 범위 밖일 수 있다.</summary>
        private int slideToSlot;

        /// <summary>슬라이드 시작 <b>좌표</b>. 인덱스가 아닌 이유는 드래그를 놓은 자리가 장 경계가
        /// 아니기 때문이다 - 인덱스로 보간하면 놓는 순간 화면이 튄다.</summary>
        private float slideFromX;
        private float slideElapsed = -1f; // 음수 = 슬라이드 중 아님

        /// <summary>지금 순환용으로 옮겨둔 칸(-1 또는 N). 없으면 <see cref="NoBorrowedSlot"/>.</summary>
        private int borrowedSlot = NoBorrowedSlot;

        private bool dragging;
        private float dragStartLocalX;
        private float dragStartContentX;

        private int PageCount => content != null ? content.childCount : 0;

        private float PageWidth => viewport != null ? viewport.rect.width : 0f;

        private bool CanWrap => PageCount >= MinPagesForWrap;

        private void Start()
        {
            holdRemaining = holdDuration;
            Normalize();
            RefreshDots();
        }

        private void Update()
        {
            if (viewport == null || content == null || PageCount <= 1)
                return;

            // 손을 대고 있는 동안에는 자동으로 넘어가지 않는다.
            if (dragging)
                return;

            if (slideElapsed >= 0f)
            {
                TickSlide();
                return;
            }

            holdRemaining -= Time.deltaTime;
            if (holdRemaining > 0f)
                return;

            // 자동 넘김도 순환을 쓴다. 예전처럼 마지막에서 첫 장으로 <b>되감으면</b>
            // 중간 장들이 역방향으로 훑고 지나가 "되돌아가는" 그림이 된다.
            GoToSlot(pageIndex + 1);
        }

        // ------------------------------------------------------------------ 드래그

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (viewport == null || content == null || PageCount <= 1)
                return;

            if (!TryGetLocalX(eventData, out float localX))
                return;

            // 슬라이드 중에 잡으면 그 자리에서 즉시 마무리한다. 어중간한 위치에서 순환용으로
            // 옮겨둔 장까지 얽히면 어느 칸이 어디 있는지가 꼬인다.
            if (slideElapsed >= 0f)
            {
                slideElapsed = -1f;
                Normalize();
            }

            dragging = true;
            dragStartLocalX = localX;
            dragStartContentX = content.anchoredPosition.x;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging)
                return;

            if (!TryGetLocalX(eventData, out float localX))
                return;

            float moved = localX - dragStartLocalX;

            // 끝 장에서 더 끌려고 하면, 그 방향에 반대편 장을 미리 갖다 둔다.
            if (CanWrap)
            {
                if (moved > 0f && pageIndex == 0)
                    BorrowSlot(-1);
                else if (moved < 0f && pageIndex == PageCount - 1)
                    BorrowSlot(PageCount);
            }

            // 장이 없는 쪽으로는 끌리지 않게 막는다. 넘어가면 빈칸이 보인다.
            float max = TargetX(borrowedSlot == -1 ? -1 : 0);
            float min = TargetX(borrowedSlot == PageCount ? PageCount : PageCount - 1);
            SetContentX(Mathf.Clamp(dragStartContentX + moved, min, max));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging)
                return;

            dragging = false;

            float width = PageWidth;
            if (width <= 0f)
                return;

            float moved = content.anchoredPosition.x - dragStartContentX;
            int target = pageIndex;

            // 왼쪽으로 끌면(음수) 다음 장, 오른쪽으로 끌면 이전 장.
            if (moved <= -width * swipeThreshold)
                target = pageIndex + 1;
            else if (moved >= width * swipeThreshold)
                target = pageIndex - 1;

            GoToSlot(target);
        }

        private bool TryGetLocalX(PointerEventData eventData, out float localX)
        {
            localX = 0f;
            if (viewport == null)
                return false;

            // Screen Space - Overlay 캔버스라 카메라 인자는 null 이 맞다.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    viewport, eventData.position, eventData.pressEventCamera, out Vector2 local))
                return false;

            localX = local.x;
            return true;
        }

        // ------------------------------------------------------------------ 넘기기

        /// <summary>
        /// 지정한 칸으로 미끄러진다. 칸이 범위 밖(-1 또는 N)이면 그 자리에 반대편 장을 빌려다 둔다.
        /// 어느 쪽이든 <b>머무는 시계는 처음부터</b> 다시 간다.
        /// </summary>
        private void GoToSlot(int slot)
        {
            int count = PageCount;
            if (count <= 0 || content == null)
                return;

            if (slot < 0 || slot >= count)
            {
                if (CanWrap)
                    BorrowSlot(slot);
                else
                    slot = Mathf.Clamp(slot, 0, count - 1);
            }

            slideToSlot = slot;
            slideFromX = content.anchoredPosition.x;
            slideElapsed = 0f;
            holdRemaining = holdDuration;

            // 점 표시는 도착을 기다리지 않고 바로 바꾼다 - 밀자마자 어디로 가는지 보여야 한다.
            pageIndex = Wrap(slot);
            RefreshDots();
        }

        private void TickSlide()
        {
            slideElapsed += Time.deltaTime;

            float t = slideDuration <= 0f ? 1f : Mathf.Clamp01(slideElapsed / slideDuration);

            // 부드럽게 서고 부드럽게 출발한다. 등속이면 배너가 "튕겨" 보인다.
            float eased = t * t * (3f - 2f * t);

            SetContentX(Mathf.Lerp(slideFromX, TargetX(slideToSlot), eased));

            if (t < 1f)
                return;

            slideElapsed = -1f;
            holdRemaining = holdDuration;
            Normalize();
        }

        /// <summary>
        /// 빌려 간 장을 제 칸으로 돌리고 content 를 현재 장의 제자리로 맞춘다.
        /// 이걸 거치고 나면 "장 i 는 칸 i" 라는 상태로 돌아온다.
        /// </summary>
        private void Normalize()
        {
            ReturnBorrowedSlot();
            SetContentX(TargetX(pageIndex));
        }

        /// <summary>범위 밖 칸에 그 자리에 와야 할 장을 옮겨 둔다.</summary>
        private void BorrowSlot(int slot)
        {
            if (borrowedSlot == slot)
                return;

            ReturnBorrowedSlot();

            SetPageSlot(Wrap(slot), slot);
            borrowedSlot = slot;
        }

        private void ReturnBorrowedSlot()
        {
            if (borrowedSlot == NoBorrowedSlot)
                return;

            int page = Wrap(borrowedSlot);
            SetPageSlot(page, page);
            borrowedSlot = NoBorrowedSlot;
        }

        /// <summary>장 하나를 지정한 칸에 놓는다. 앵커만 바꾸고 여백은 0으로 되돌린다.</summary>
        private void SetPageSlot(int page, int slot)
        {
            if (content == null || page < 0 || page >= content.childCount)
                return;

            if (!(content.GetChild(page) is RectTransform rt))
                return;

            Vector2 min = rt.anchorMin;
            Vector2 max = rt.anchorMax;
            min.x = slot;
            max.x = slot + 1;
            rt.anchorMin = min;
            rt.anchorMax = max;

            // 앵커를 바꾸면 Unity 가 화면 위치를 유지하려고 여백을 자동으로 조정한다.
            // 그대로 두면 장이 옮겨지지 않으므로 여백을 다시 0으로 눌러야 한다.
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private int Wrap(int slot)
        {
            int count = PageCount;
            if (count <= 0)
                return 0;

            return ((slot % count) + count) % count;
        }

        /// <summary>그 칸이 창에 딱 맞을 때의 content x 좌표.</summary>
        private float TargetX(int slot) => -slot * PageWidth;

        private void SetContentX(float x)
        {
            if (content == null)
                return;

            Vector2 pos = content.anchoredPosition;
            pos.x = x;
            content.anchoredPosition = pos;
        }

        private void RefreshDots()
        {
            if (pageDots == null)
                return;

            for (int i = 0; i < pageDots.Length; i++)
            {
                if (pageDots[i] == null)
                    continue;

                pageDots[i].color = i == pageIndex ? activeDotColor : inactiveDotColor;
            }
        }
    }
}
