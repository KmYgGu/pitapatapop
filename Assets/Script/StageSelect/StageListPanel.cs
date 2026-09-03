using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.Core;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.StageSelect
{
    /// <summary>
    /// 한 챕터 안의 스테이지 목록(최소 1개, 최대 5개). 챕터 목록과 같은 템플릿 복제 방식이다.
    ///
    /// <b>특별·이벤트 챕터는 여기 오지 않는다</b> - 그건 목록 없이 곧바로 준비 화면으로 간다
    /// (<see cref="ChapterDefinition.GoesStraightToPrep"/>). 그 판단은 흐름 쪽이 한다.
    /// </summary>
    public class StageListPanel : MonoBehaviour
    {
        private class Card
        {
            public GameObject root;
            public Button button;
            public Text nameText;
            public Text levelText;
            public Text costText;
            public Text conditionText;
        }

        [Header("구조")]
        [SerializeField] private RectTransform content;
        [SerializeField] private GameObject cardTemplate;
        [SerializeField] private Text chapterTitleText;
        [SerializeField] private Button backButton;

        [Header("배치")]
        [SerializeField] private float cardHeight = 84f;
        [SerializeField] private float cardSpacing = 10f;

        private readonly List<Card> cards = new List<Card>();
        private ChapterDefinition chapter;

        public event Action<ChapterDefinition, StageDefinition> OnStageChosen;
        public event Action OnBack;

        private void Awake()
        {
            if (cardTemplate != null)
                cardTemplate.SetActive(false);

            if (backButton != null)
                backButton.onClick.AddListener(() => OnBack?.Invoke());
        }

        public void Show(ChapterDefinition target)
        {
            chapter = target;
            gameObject.SetActive(true);
            Build();
        }

        public void Hide() => gameObject.SetActive(false);

        private void Build()
        {
            var stages = chapter != null ? chapter.stages : null;
            int count = stages != null ? stages.Length : 0;

            if (chapterTitleText != null)
                chapterTitleText.text = chapter != null ? chapter.displayName : string.Empty;

            EnsureCards(count);

            float y = 0f;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                bool used = i < count && stages[i] != null;

                card.root.SetActive(used);
                if (!used)
                    continue;

                var stage = stages[i];

                var rect = (RectTransform)card.root.transform;
                rect.anchoredPosition = new Vector2(0f, -y);
                y += cardHeight + cardSpacing;

                SetText(card.nameText, stage.displayName);
                SetText(card.levelText, $"권장 Lv.{stage.recommendedLevel}");
                SetText(card.costText, stage.heartCost > 0 ? $"하트 {stage.heartCost}" : "무료");

                // 어떤 조건으로 클리어하는지 목록에서 미리 보여준다(2026-08-24 요청).
                SetText(card.conditionText, stage.clearConditionText);
            }
        }

        private void EnsureCards(int count)
        {
            if (cardTemplate == null || content == null)
                return;

            while (cards.Count < count)
            {
                var go = Instantiate(cardTemplate, content);
                go.name = $"StageCard{cards.Count}";

                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
                rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
                rect.sizeDelta = new Vector2(0f, cardHeight);

                var card = new Card
                {
                    root = go,
                    button = go.GetComponent<Button>(),
                    nameText = FindText(go, "NameText"),
                    levelText = FindText(go, "LevelText"),
                    costText = FindText(go, "CostText"),
                    conditionText = FindText(go, "ConditionText")
                };

                // 카드가 재사용되므로 만든 시점의 스테이지를 람다에 가두지 않고 인덱스로 되짚는다.
                int index = cards.Count;
                if (card.button != null)
                    card.button.onClick.AddListener(() => Choose(index));

                cards.Add(card);
            }
        }

        private void Choose(int index)
        {
            var stages = chapter != null ? chapter.stages : null;
            if (stages == null || index < 0 || index >= stages.Length)
                return;

            var stage = stages[index];
            if (stage == null)
                return;

            OnStageChosen?.Invoke(chapter, stage);
        }

}
}
