using System.Collections.Generic;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JojoPuzzle.Formation
{
    /// <summary>
    /// 스티커 한 칸. <b>누르면 고르고, 꾹 누르면 설명이 말풍선으로 뜬다</b>
    /// (2026-09-03 사용자 지시: "목록에서 보고 싶은 건 스티커지 글이 아니야").
    ///
    /// ⚠ <c>Button</c> 을 쓰지 않는다 - 버튼은 <b>뗄 때</b> 눌린 것으로 치기 때문에
    /// "꾹 누르기"와 "누르기"를 가를 수가 없다. 눌림·뗌·빠져나감을 직접 받는다.
    /// </summary>
    public class StickerCell : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
                               IPointerExitHandler
    {
        /// <summary>이 칸이 나타내는 스티커 번호.</summary>
        public int stickerId;

        /// <summary>꾹 누른 것으로 치는 시간(초).</summary>
        public float holdSeconds = 0.35f;

        public System.Action<int> onPicked;
        public System.Action<int> onHeld;
        public System.Action onReleased;

        private float pressedAt = -1f;
        private bool held;

        public void OnPointerDown(PointerEventData eventData)
        {
            pressedAt = Time.unscaledTime;
            held = false;
        }

        private void Update()
        {
            if (pressedAt < 0f || held)
                return;

            if (Time.unscaledTime - pressedAt >= holdSeconds)
            {
                held = true;
                onHeld?.Invoke(stickerId);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            bool wasHeld = held;
            pressedAt = -1f;
            held = false;

            if (wasHeld)
            {
                onReleased?.Invoke();
                return;
            }

            onPicked?.Invoke(stickerId);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (pressedAt < 0f)
                return;

            bool wasHeld = held;
            pressedAt = -1f;
            held = false;

            if (wasHeld)
                onReleased?.Invoke();
        }
    }
}
