using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 짧게 누르기와 <b>꾹 누르기</b>를 나눠서 알려주는 버튼.
    ///
    /// <b>Button 을 쓰지 않는 이유</b>: <c>Button</c> 은 손을 뗄 때 무조건 클릭을 발행한다.
    /// 그 위에 꾹 누르기를 얹으면 길게 눌러도 클릭이 같이 나가서 두 동작이 겹친다.
    /// 그래서 눌림 처리를 직접 하고, 눌린 느낌(색 변화)도 여기서 낸다.
    ///
    /// <b>꾹 누르기는 손을 떼기 전에 발행된다</b> - 기준 시간을 넘기는 순간 바로 알린다.
    /// 그래야 "얼마나 더 눌러야 하지" 하고 기다리지 않는다. 그 뒤에 손을 떼도 짧은 누르기는
    /// 나가지 않는다.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class LongPressButton : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("이 시간(초)을 넘겨 누르고 있으면 꾹 누르기로 친다.")]
        [SerializeField] private float holdSeconds = 0.45f;

        [Tooltip("눌린 동안 겹칠 색. 알파가 0이면 아무 표시도 하지 않는다.")]
        [SerializeField] private Color pressedTint = new Color(0f, 0f, 0f, 0.25f);

        [SerializeField] private Graphic targetGraphic;

        public event Action OnShortPress;
        public event Action OnLongPress;

        private bool pressing;
        private bool longFired;
        private float pressedAt;
        private Color normalColor;
        private bool hasNormalColor;

        private void Awake()
        {
            if (targetGraphic == null)
                targetGraphic = GetComponent<Graphic>();

            CacheNormalColor();
        }

        private void OnEnable()
        {
            // 눌린 채로 화면이 꺼졌다 켜지면 색이 눌린 상태로 굳는다.
            CacheNormalColor();
            ResetPress();
        }

        private void Update()
        {
            if (!pressing || longFired)
                return;

            if (Time.unscaledTime - pressedAt < holdSeconds)
                return;

            longFired = true;
            ApplyTint(false);
            OnLongPress?.Invoke();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressing = true;
            longFired = false;
            pressedAt = Time.unscaledTime;
            ApplyTint(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            bool wasPressing = pressing;
            bool wasLong = longFired;

            ResetPress();

            // 꾹 누르기가 이미 나갔으면 짧은 누르기는 없던 일로 한다.
            if (wasPressing && !wasLong)
                OnShortPress?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // 손가락이 버튼 밖으로 나가면 취소. 둘 다 발행하지 않는다.
            ResetPress();
        }

        private void ResetPress()
        {
            pressing = false;
            longFired = false;
            ApplyTint(false);
        }

        private void CacheNormalColor()
        {
            if (targetGraphic == null || hasNormalColor)
                return;

            normalColor = targetGraphic.color;
            hasNormalColor = true;
        }

        private void ApplyTint(bool pressed)
        {
            if (targetGraphic == null || !hasNormalColor || pressedTint.a <= 0f)
                return;

            if (!pressed)
            {
                targetGraphic.color = normalColor;
                return;
            }

            // 원래 색 위에 어두운 판을 겹친 것과 같은 값을 직접 계산한다.
            float a = pressedTint.a;
            targetGraphic.color = new Color(
                Mathf.Lerp(normalColor.r, pressedTint.r, a),
                Mathf.Lerp(normalColor.g, pressedTint.g, a),
                Mathf.Lerp(normalColor.b, pressedTint.b, a),
                normalColor.a);
        }
    }
}
