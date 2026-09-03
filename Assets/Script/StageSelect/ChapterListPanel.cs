using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.StageSelect
{
    /// <summary>
    /// 챕터 목록. 카드 한 장을 씬에 <b>템플릿으로 하나만</b> 두고 챕터 수만큼 복제한다
    /// (DamagePopupUI·ComboCountUI 와 같은 방식 - 씬에 카드를 미리 잔뜩 만들어두면 챕터가
    /// 늘 때마다 씬을 고쳐야 한다).
    ///
    /// 만든 카드는 버리지 않고 <see cref="cards"/> 에 들고 있다가 다시 쓴다.
    /// </summary>
    public class ChapterListPanel : MonoBehaviour
    {
        [Serializable]
        private class Card
        {
            public GameObject root;
            public Button button;
            public Image background;
            public Text nameText;
            public Text levelText;
            public Text scheduleText;
            public Text lockedText;
        }

        [Header("구조")]
        [Tooltip("카드가 쌓이는 곳. 세로로 늘어난다.")]
        [SerializeField] private RectTransform content;

        [Tooltip("복제할 카드 한 장. 꺼둔 채로 씬에 있어야 한다.")]
        [SerializeField] private GameObject cardTemplate;

        [Header("배치")]
        [Tooltip("카드 한 장의 높이(캔버스 단위).")]
        [SerializeField] private float cardHeight = 92f;

        [Tooltip("카드 사이 간격.")]
        [SerializeField] private float cardSpacing = 10f;

        [Header("색")]
        [SerializeField] private Color openColor = new Color(0.20f, 0.22f, 0.30f, 0.95f);
        [SerializeField] private Color closedColor = new Color(0.14f, 0.14f, 0.17f, 0.9f);

        private readonly List<Card> cards = new List<Card>();

        /// <summary>마지막으로 그린 목록. 카드를 눌렀을 때 인덱스로 되짚는 데 쓴다.</summary>
        private ChapterCatalog lastCatalog;

        /// <summary>카드를 눌렀을 때. <see cref="StageSelectFlow"/> 가 받는다.</summary>
        public event Action<ChapterDefinition> OnChapterChosen;

        private void Build(ChapterCatalog catalog)
        {
            lastCatalog = catalog;

            var list = catalog != null ? catalog.chapters : null;
            int count = list != null ? list.Length : 0;

            EnsureCards(count);

            DateTime now = DateTime.Now;
            float y = 0f;

            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                bool used = i < count && list[i] != null;

                card.root.SetActive(used);
                if (!used)
                    continue;

                var chapter = list[i];
                bool open = chapter.IsOpen(now);

                // 위에서 아래로 쌓는다. content 의 pivot 이 위쪽이라 y 를 음수로 내려간다.
                var rect = (RectTransform)card.root.transform;
                rect.anchoredPosition = new Vector2(0f, -y);
                y += cardHeight + cardSpacing;

                if (card.background != null)
                    card.background.color = open ? openColor : closedColor;

                SetText(card.nameText, chapter.displayName);
                SetText(card.levelText, $"권장 Lv.{chapter.recommendedLevel}");

                // 상설이면 기간 칸을 아예 비운다 - "상설"이라고 적으면 기간이 있는 챕터와
                // 같은 무게로 읽혀서 눈에 걸린다.
                SetText(card.scheduleText, chapter.GetScheduleText());

                SetText(card.lockedText, open ? string.Empty : "기간이 아닙니다");

                if (card.button != null)
                    card.button.interactable = open;
            }

            // 쌓인 높이만큼 content 를 늘려야 스크롤이 끝까지 간다.
            if (content != null && count > 0)
            {
                var size = content.sizeDelta;
                size.y = Mathf.Max(0f, y - cardSpacing);
                content.sizeDelta = size;
            }
        }

        private void EnsureCards(int count)
        {
            if (cardTemplate == null || content == null)
                return;

            while (cards.Count < count)
            {
                var go = Instantiate(cardTemplate, content);
                go.name = $"ChapterCard{cards.Count}";

                var rect = (RectTransform)go.transform;

                // 가로는 부모를 채우고 세로는 고정 높이. 위에서부터 쌓기 위해 위쪽 앵커에 붙인다.
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
                    background = go.GetComponent<Image>(),
                    nameText = FindText(go, "NameText"),
                    levelText = FindText(go, "LevelText"),
                    scheduleText = FindText(go, "ScheduleText"),
                    lockedText = FindText(go, "LockedText")
                };

                int index = cards.Count;
                if (card.button != null)
                    card.button.onClick.AddListener(() => Choose(index));

                cards.Add(card);
            }
        }

        /// <summary>
        /// 인덱스로 되짚어 챕터를 찾는다. <b>람다에 챕터를 직접 가두지 않는 이유</b>: 카드는
        /// 재사용되므로 만든 시점의 챕터를 붙들고 있으면 목록이 바뀐 뒤 엉뚱한 데로 들어간다.
        /// </summary>
        private void Choose(int index)
        {
            if (lastCatalog == null || lastCatalog.chapters == null)
                return;

            if (index < 0 || index >= lastCatalog.chapters.Length)
                return;

            var chapter = lastCatalog.chapters[index];
            if (chapter == null || !chapter.IsOpen(DateTime.Now))
                return;

            OnChapterChosen?.Invoke(chapter);
        }

        private void Awake()
        {
            if (cardTemplate != null)
                cardTemplate.SetActive(false);
        }

        /// <summary>목록을 다시 그린다. 카탈로그를 기억해뒀다가 눌렀을 때 되짚는다.</summary>
        public void Show(ChapterCatalog catalog)
        {
            gameObject.SetActive(true);
            Build(catalog);
        }

        public void Hide() => gameObject.SetActive(false);

}
}
