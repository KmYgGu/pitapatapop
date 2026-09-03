using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 체력바 UI. 적/보스 체력 표시용으로 우선 쓰고, 나중에 플레이어 캐릭터 체력이
    /// 생기면 그대로 재사용 가능. 씬 세팅: fillImage에 Image(Type=Filled, Horizontal) 연결.
    /// </summary>
    public class HealthBarUI : MonoBehaviour
    {
        [SerializeField] private Image fillImage;
        [SerializeField] private Text valueText; // 선택 - "1234/5000" 같은 숫자 표시용. 안 쓰면 비워둬도 됨.

        private float maxValue = 1f;
        private float currentValue = 1f;

        public float CurrentValue => currentValue;
        public float MaxValue => maxValue;
        public bool IsDepleted => currentValue <= 0f;

        public void SetMax(float max)
        {
            maxValue = Mathf.Max(1f, max);
            SetValue(maxValue);
        }

        public void SetValue(float value)
        {
            currentValue = Mathf.Clamp(value, 0f, maxValue);
            ApplyVisual();
        }

        public void ApplyDamage(float amount)
        {
            SetValue(currentValue - amount);
        }

        private void ApplyVisual()
        {
            if (fillImage != null)
                fillImage.fillAmount = maxValue > 0f ? currentValue / maxValue : 0f;

            if (valueText != null)
                valueText.text = $"{Mathf.CeilToInt(currentValue)}/{Mathf.CeilToInt(maxValue)}";
        }
    }
}
