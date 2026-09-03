using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 점수 배너 바로 아래, 이번 판에서 획득한 골드량을 보여주는 작은 캡슐형 배지.
    /// ScoreUI와 마찬가지로 아직 실제 보상 계산 로직이 없어 표시 전용.
    /// </summary>
    public class GoldUI : MonoBehaviour
    {
        [SerializeField] private Text goldText;

        private int currentGold;

        public int CurrentGold => currentGold;

        public void SetGold(int gold)
        {
            currentGold = gold;
            ApplyVisual();
        }

        public void AddGold(int amount) => SetGold(currentGold + amount);

        private void ApplyVisual()
        {
            if (goldText != null)
                goldText.text = currentGold.ToString("N0");
        }
    }
}
