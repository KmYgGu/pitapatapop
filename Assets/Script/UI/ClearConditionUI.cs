using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 퍼즐판 바로 위(게이지 바 아래)에 놓이는 클리어 조건 표시 창.
    /// 조건 문구("보스를 쓰러뜨려라")와 진행도(0/1)를 각각 다른 Text로 나눠서 보여준다 -
    /// 진행도만 자주 갱신되므로 문구 쪽은 건드리지 않게 분리해둠.
    /// 표시 전용이고 판정은 BattleManager가 한다 - 배틀 시작 시 SetCondition으로 목표를 걸고,
    /// 승리 시 SetProgress(1)로 채운다.
    /// </summary>
    public class ClearConditionUI : MonoBehaviour
    {
        [SerializeField] private Text conditionText;
        [SerializeField] private Text progressText;

        [Tooltip("문구에 쓸 타이포그래피 <b>최대</b> 단계. 화면이 좁아 문구가 상자를 넘치면 " +
                 "여기서부터 사다리를 한 단씩 내려간다(UITypography.FitToWidth).")]
        [SerializeField] private int conditionMaxFontSize = UITypography.Small;

        [Tooltip("진행도에 쓸 최대 단계. 위와 같은 방식으로 줄어든다.")]
        [SerializeField] private int progressMaxFontSize = UITypography.Small;

        private string condition = string.Empty;
        private int current;
        private int target;

        /// <summary>목표가 채워졌는지 - 승리 판정 쪽에서 이 값만 보면 됨.</summary>
        public bool IsCleared => target > 0 && current >= target;

        /// <summary>새 조건을 걸고 진행도를 0으로 초기화. 배틀 시작 시 호출하는 용도.</summary>
        public void SetCondition(string description, int targetCount)
        {
            condition = description;
            target = Mathf.Max(0, targetCount);
            current = 0;
            ApplyVisual();
        }

        /// <summary>진행도를 목표치 안에서 갱신(넘치지 않게 고정).</summary>
        public void SetProgress(int currentCount)
        {
            current = Mathf.Clamp(currentCount, 0, target);
            ApplyVisual();
        }

        public void AddProgress(int amount) => SetProgress(current + amount);

        private void ApplyVisual()
        {
            if (conditionText != null)
                conditionText.text = condition;

            if (progressText != null)
                progressText.text = $"{current}/{target}";

            FitTextToBoxes();
        }

        /// <summary>
        /// 문구가 상자 폭을 넘지 않는 단계로 맞춘다.
        ///
        /// 세로 기준으로 고른 크기는 어떤 기기에서도 같지만 <b>가로는 기기 비율마다 좁아진다</b> -
        /// 세로로 긴 폰에서는 같은 문구가 상자를 넘어 화면 밖까지 삐져나왔다.
        /// 글자 수가 많은 조건("보스를 쓰러뜨려라!")일수록 먼저 걸린다.
        /// </summary>
        private void FitTextToBoxes()
        {
            if (conditionText != null)
                UITypography.FitToWidth(conditionText, conditionText.rectTransform.rect.width,
                                        conditionMaxFontSize);

            if (progressText != null)
                UITypography.FitToWidth(progressText, progressText.rectTransform.rect.width,
                                        progressMaxFontSize);
        }

        /// <summary>
        /// 화면 크기가 바뀌면(기기 회전, 에디터 창 조절) 상자 폭도 달라지므로 다시 맞춘다.
        /// 이 콜백은 자기 RectTransform 이 변할 때 불린다.
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            FitTextToBoxes();
        }
    }
}
