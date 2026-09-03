using UnityEngine;
using UnityEngine.EventSystems;

namespace JojoPuzzle.Formation
{
    /// <summary>
    /// 스티커북 위에서 <b>끌어 옮길 수 있는 것</b>. 스티커도 캐릭터도 같은 부품을 쓴다
    /// (2026-09-03 사용자 지시: "캐릭터들도 스티커 취급을 해서").
    ///
    /// <code>
    ///   그냥 누름        → onTapped   (편집 중이 아닐 때만 뜻이 있다)
    ///   꾹 누름          → onHeld     - 이제 이 장을 옮길 수 있다
    ///   꾹 누른 채로 끌기 → onDragged  - 손가락을 따라온다
    /// </code>
    ///
    /// ⭐ <b>꾹 누르기와 끌기가 한 동작이다.</b> 눌러서 고르고 다시 끌게 하면 손이 두 번 간다 -
    /// 꾹 눌러 집은 그 손으로 그대로 끌면 된다.
    ///
    /// ⚠ <c>Button</c> 을 쓰지 않는다 - 버튼은 <b>뗄 때</b> 눌린 것으로 쳐서 꾹 누르기와 가를 수 없고,
    /// 끌기도 안 준다.
    /// </summary>
    public class BookPlaceable : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
                                 IPointerClickHandler,
                                 IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>무엇인지. 스티커는 시트 번호, 캐릭터는 음수(리더 -1 · 파트너 -2).</summary>
        public int id;

        /// <summary>
        /// 이 <b>배치</b>의 고유 번호(PlayerStickers.Placed.key).
        /// id 로는 못 가린다 - 같은 스티커를 여러 장 붙일 수 있다(2026-09-03).
        /// </summary>
        public int key;

        /// <summary>이만큼 누르고 있으면 집은 것으로 친다.</summary>
        public float holdSeconds = 0.3f;

        /// <summary>
        /// 꾹 누르지 않아도 바로 끌 수 있는지. <b>방금 목록에서 고른 스티커</b>가 그렇다 -
        /// 이미 집어 든 셈이라 또 꾹 누르라고 하면 번거롭다.
        /// </summary>
        public bool alreadyHeld;

        // ⭐ <b>배치 자체를 넘긴다</b>(2026-09-03). 예전엔 id 만 넘겼는데, 같은 스티커를
        // 여러 장 붙일 수 있게 되면서 <b>어느 장인지</b>를 id 로는 못 가리게 됐다.
        // 받는 쪽이 id 든 key 든 필요한 걸 꺼내 쓰면 된다.
        public System.Action<BookPlaceable> onTapped;
        public System.Action<BookPlaceable> onHeld;
        public System.Action<BookPlaceable, PointerEventData> onDragged;
        public System.Action<BookPlaceable> onDragEnd;

        private float pressedAt = -1f;
        private bool held;
        private bool dragging;

        public void OnPointerDown(PointerEventData eventData)
        {
            pressedAt = Time.unscaledTime;
            held = alreadyHeld;
            dragging = false;

            if (held)
                onHeld?.Invoke(this);
        }

        private void Update()
        {
            if (pressedAt < 0f || held)
                return;

            if (Time.unscaledTime - pressedAt >= holdSeconds)
            {
                held = true;
                onHeld?.Invoke(this);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // 집지 않은 것은 끌리지 않는다 - 그래야 화면을 스치는 손가락에 스티커가 안 딸려간다.
            dragging = held;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (dragging)
                onDragged?.Invoke(this, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragging)
                onDragEnd?.Invoke(this);

            dragging = false;
        }

        /// <summary>
        /// ⚠⚠ <b>여기서 dragging 을 지우면 안 된다</b>(2026-09-03 사용자 신고로 찾음).
        ///
        /// 유니티는 손을 뗄 때 <c>OnPointerUp</c> 을 <b>먼저</b> 부르고 그 뒤에
        /// <c>OnEndDrag</c> 를 부른다(StandaloneInputModule 의 처리 순서). 여기서 지워 버리면
        /// OnEndDrag 가 <c>if (dragging)</c> 에 걸려 <b>끝났다는 신호를 아예 안 보낸다</b>.
        ///
        /// 그 탓에 옮긴 뒤에도 계속 '들고 있는' 상태로 남아 좌우 넘기기가 막혔고,
        /// 자동 확정이 안 걸려 <b>옮긴 자리가 저장되지 않았다</b>.
        /// 지우는 건 OnEndDrag 와 다음 OnPointerDown 이 한다.
        /// </summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            tapped = !held && !dragging;

            pressedAt = -1f;
            held = false;
        }

        private bool tapped;

        /// <summary>
        /// ⭐⭐ <b>누름을 여기서 삼킨다</b>(2026-09-03 사용자 지시: "스티커 위로 누르면
        /// 스티커 목록이 안 뜨게"). 유니티는 <c>IPointerClickHandler</c> 를 가진 <b>가장 가까운</b>
        /// 조상에게 누름을 주므로, 이걸 안 달면 누름이 그대로 <b>책(Button)</b> 까지 올라가
        /// 스티커를 만질 때마다 붙이기 화면이 열린다.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!tapped)
                return;

            tapped = false;
            onTapped?.Invoke(this);
        }
    }

}
