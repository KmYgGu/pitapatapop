using UnityEngine;
using UnityEngine.EventSystems;

namespace JojoPuzzle.Formation
{
    /// <summary>
    /// 책을 <b>좌우로 밀어</b> 다른 스티커북으로 넘긴다(2026-09-03 사용자 기획).
    ///
    /// ⚠ 스티커를 끄는 손짓과 겹치므로 <b>편집 중에는 안 넘긴다</b> -
    /// 그 판단은 <see cref="StickerBookPanel"/> 이 한다. 여기는 얼마나 밀렸는지만 알린다.
    /// </summary>
    public class BookSwipe : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public System.Action<float> onSwiped;

        private Vector2 from;

        public void OnBeginDrag(PointerEventData eventData) => from = eventData.position;

        /// <summary>
        /// ⚠⚠ <b>비어 있어도 반드시 있어야 한다.</b> 유니티는 <c>IDragHandler</c> 를 가진 것을
        /// 찾아 끌 대상으로 삼는다(<c>GetEventHandler&lt;IDragHandler&gt;</c>) - 없으면
        /// <c>OnBeginDrag</c>·<c>OnEndDrag</c> 도 <b>아예 안 온다</b>.
        /// 이것 때문에 좌우로 밀어도 권이 안 넘어갔다(2026-09-03).
        /// </summary>
        public void OnDrag(PointerEventData eventData) { }

        public void OnEndDrag(PointerEventData eventData)
            => onSwiped?.Invoke(eventData.position.x - from.x);
    }
}
