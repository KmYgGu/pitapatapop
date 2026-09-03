using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using Spine.Unity;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.Formation
{
    /// <summary>
    /// 캐릭터 상세(강화) 화면. 편성 화면의 보유 목록에서 조각을 <b>꾹 누르면</b> 열린다.
    ///
    /// 구성은 원작(ジョジョのピタパタポップ)의 강화 화면을 참고했다 - 이름과 등급이 위,
    /// 캐릭터가 가운데, 그 아래로 <b>레벨 + 올리기 버튼</b>과 전투력, 그리고 스킬 칸.
    /// 다만 탭(스킬/프로필)이나 스킬 레벨은 아직 없다 - 필요해지면 스킬 칸 옆에 붙이면 된다.
    ///
    /// <b>퍼즐 조각 모양은 보여주지 않는다</b>(2026-08-24 사용자 지시) - 여기까지 들어온 사람은
    /// 이미 목록에서 조각을 보고 온 것이라 같은 걸 두 번 볼 이유가 없다.
    /// </summary>
    public class CharacterDetailPanel : MonoBehaviour
    {
        private class ExpItemCard
        {
            public GameObject root;
            public Button button;
            public Text nameText;
            public Text expText;
            public Text ownedText;
        }

        [Header("이름 / 등급")]
        [SerializeField] private Text nameText;

        [Tooltip("캐릭터 고유 id. 애셋에 안 적혀 있으면 비어 있다.")]
        [SerializeField] private Text idText;
        [SerializeField] private Text gradeText;

        [Header("모습")]
        [SerializeField] private SpineCharacterView spineView;

        [Tooltip("Spine 이 아직 없는 캐릭터를 세울 때 대신 쓸 스켈레톤(임시).")]
        [SerializeField] private SkeletonDataAsset placeholderSpine;

        [Header("수치")]
        [SerializeField] private Text levelText;
        [SerializeField] private Text powerText;
        [SerializeField] private Text acquiredText;
        [SerializeField] private Text frameColorText;

        [Header("경험치")]
        [Tooltip("Image Type = Filled, Horizontal.")]
        [SerializeField] private Image expFill;
        [SerializeField] private Text expText;

        [Header("스킬")]
        [SerializeField] private Text skillNameText;
        [SerializeField] private Text skillCategoryText;
        [SerializeField] private Text skillDescText;

        [Tooltip("게이지를 채우는 데 필요한 매치 수(PanelType.skillRequiredMatchCount).")]
        [SerializeField] private Text skillMatchText;
        [Tooltip("범위 그림. '무작위' 글자도 이 미리보기가 직접 띄운다(2026-08-29).")]
        [SerializeField] private SkillRangePreview skillRange;

        [Header("레벨업")]
        [Tooltip("레벨 옆 + 버튼. 경험치 아이템 창을 연다.")]
        [SerializeField] private Button levelUpButton;

        [Tooltip("경험치 아이템 창. 꺼둔 채로 씬에 있어야 한다.")]
        [SerializeField] private GameObject levelUpPopup;

        [SerializeField] private ExpItemCatalog expItemCatalog;
        [SerializeField] private RectTransform expItemContent;
        [SerializeField] private GameObject expItemTemplate;

        [Tooltip("아이템 칸 하나의 높이(캔버스 단위).")]
        [SerializeField] private float expItemHeight = 54f;
        [SerializeField] private float expItemSpacing = 8f;

        [SerializeField] private Text popupLevelText;
        [SerializeField] private Image popupExpFill;
        [SerializeField] private Text popupExpText;
        [SerializeField] private Text popupNoticeText;
        [SerializeField] private Button popupCloseButton;

        [Header("하단")]
        [SerializeField] private Button backButton;

        public event Action OnBack;

        /// <summary>레벨이 실제로 올랐을 때. 편성 화면이 목록·슬롯을 다시 그리게 한다.</summary>
        public event Action OnCharacterChanged;

        private readonly List<ExpItemCard> expCards = new List<ExpItemCard>();
        private PanelType current;
        private int currentDaysOwned;

        private void Awake()
        {
            if (expItemTemplate != null)
                expItemTemplate.SetActive(false);

            if (backButton != null)
                backButton.onClick.AddListener(() => OnBack?.Invoke());

            if (levelUpButton != null)
                levelUpButton.onClick.AddListener(OpenLevelUp);

            if (popupCloseButton != null)
                popupCloseButton.onClick.AddListener(CloseLevelUp);

            CloseLevelUp();
        }

        /// <summary>
        /// 캐릭터를 보여준다. <paramref name="daysOwned"/> 는 얻은 지 며칠 됐는지
        /// (부르는 쪽이 <see cref="PlayerCollection"/> 로 구해서 넘긴다).
        /// </summary>
        public void Show(PanelType character, int daysOwned)
        {
            gameObject.SetActive(true);

            current = character;
            currentDaysOwned = daysOwned;

            CloseLevelUp();

            if (character == null)
            {
                Clear();
                return;
            }

            ShowSpine(character);
            RefreshCharacter();
        }

        public void Hide()
        {
            // Spine 은 켜져 있는 동안만 세워둔다 - 숨긴 화면이 계속 스켈레톤을 물고 있을 이유가 없다.
            if (spineView != null)
                spineView.Clear();

            CloseLevelUp();
            gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ 본문

        /// <summary>레벨이 오르면 값이 전부 달라지므로 한 번에 다시 그린다.</summary>
        private void RefreshCharacter()
        {
            if (current == null)
                return;

            // displayName 이 비어 있는 애셋이 많아 애셋 이름으로 물러선다.
            SetText(nameText, current.DisplayName);
            SetText(idText, string.IsNullOrWhiteSpace(current.panelId) ? string.Empty : current.panelId);
            SetText(gradeText, current.grade.ToString());

            SetText(levelText, $"Lv.{current.level}");
            SetText(powerText, $"전투력 {current.CombatPower:N0}");
            SetText(acquiredText, currentDaysOwned <= 0 ? "오늘 획득" : $"획득 {currentDaysOwned}일째");
            SetText(frameColorText, $"조각색 {current.frameColor}");

            ShowExp(current, expFill, expText);
            ShowSkill(current);

            // 만렙이면 더 올릴 게 없다.
            if (levelUpButton != null)
                levelUpButton.interactable = !current.IsMaxLevel;
        }

        private void ShowSpine(PanelType character)
        {
            if (spineView == null)
                return;

            var spine = character.speech != null ? character.speech.spine : null;

            // 아직 Spine 이 없는 캐릭터는 대체 스켈레톤으로라도 세운다(준비 화면과 같은 방침).
            if (spine == null)
                spine = placeholderSpine;

            if (spine == null)
                spineView.Clear();
            else
                spineView.Show(spine);
        }

        private static void ShowExp(PanelType character, Image fill, Text text)
        {
            // 최대 레벨이면 다음 레벨까지의 경험치가 의미 없다 - 게이지를 꽉 채우고 그렇게 적는다.
            if (character.IsMaxLevel)
            {
                if (fill != null)
                    fill.fillAmount = 1f;

                SetText(text, "최대 레벨");
                return;
            }

            int need = character.ExpToNextLevel;

            if (fill != null)
                fill.fillAmount = character.ExpProgress01;

            SetText(text, need > 0 ? $"{character.currentExp:N0} / {need:N0}" : $"{character.currentExp:N0}");
        }

        private void ShowSkill(PanelType character)
        {
            var skill = character.skill;

            if (skill == null)
            {
                SetText(skillNameText, "스킬 없음");
                SetText(skillCategoryText, string.Empty);
                SetText(skillDescText, string.Empty);
                SetText(skillMatchText, string.Empty);

                if (skillRange != null)
                    skillRange.Clear();

                return;
            }

            SetText(skillNameText, string.IsNullOrWhiteSpace(skill.skillName) ? skill.name : skill.skillName);
            SetText(skillCategoryText, skill.CategoryLabel);
            SetText(skillDescText, skill.description);
            SetText(skillMatchText, $"발동 {character.skillRequiredMatchCount}매치");

            // 기획에는 "무작위 2x2" 처럼 <b>칸이 정해지지 않은 스킬</b>이 있다. 그런 스킬은
            // 빈 판만 덩그러니 남으면 "범위가 없다"로 읽히므로, 미리보기가 그 위에 "무작위"라고 적는다.
            if (skillRange != null)
                skillRange.Show(skill);
        }

        // ------------------------------------------------------------------ 레벨업

        private void OpenLevelUp()
        {
            if (levelUpPopup == null || current == null)
                return;

            levelUpPopup.SetActive(true);
            SetText(popupNoticeText, string.Empty);
            BuildExpItems();
            RefreshPopup();
        }

        private void CloseLevelUp()
        {
            if (levelUpPopup != null)
                levelUpPopup.SetActive(false);
        }

        private void BuildExpItems()
        {
            var items = expItemCatalog != null ? expItemCatalog.items : null;
            int count = items != null ? items.Length : 0;

            EnsureExpCards(count);

            float y = 0f;
            for (int i = 0; i < expCards.Count; i++)
            {
                var card = expCards[i];
                bool used = i < count && items[i] != null;

                card.root.SetActive(used);
                if (!used)
                    continue;

                var rect = (RectTransform)card.root.transform;
                rect.anchoredPosition = new Vector2(0f, -y);
                y += expItemHeight + expItemSpacing;

                SetText(card.nameText, items[i].displayName);
                SetText(card.expText, $"+{items[i].exp:N0}");
            }
        }

        private void EnsureExpCards(int count)
        {
            if (expItemTemplate == null || expItemContent == null)
                return;

            while (expCards.Count < count)
            {
                var go = Instantiate(expItemTemplate, expItemContent);
                go.name = $"ExpItem{expCards.Count}";

                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
                rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
                rect.sizeDelta = new Vector2(0f, expItemHeight);

                var card = new ExpItemCard
                {
                    root = go,
                    button = go.GetComponent<Button>(),
                    nameText = FindText(go, "NameText"),
                    expText = FindText(go, "ExpText"),
                    ownedText = FindText(go, "OwnedText")
                };

                // 목록들과 같은 규칙 - 아이템을 람다에 가두지 않고 자리 번호로 되짚는다.
                int index = expCards.Count;
                if (card.button != null)
                    card.button.onClick.AddListener(() => UseExpItem(index));

                expCards.Add(card);
            }
        }

        private void UseExpItem(int index)
        {
            var items = expItemCatalog != null ? expItemCatalog.items : null;
            if (items == null || index < 0 || index >= items.Length || items[index] == null || current == null)
                return;

            if (current.IsMaxLevel)
            {
                SetText(popupNoticeText, "이미 최대 레벨입니다");
                return;
            }

            if (PlayerInventory.GetCount(items[index].kind) <= 0)
            {
                SetText(popupNoticeText, "아이템이 없습니다");
                return;
            }

            int before = current.level;

            if (!CharacterLeveling.TryUseExpItem(current, items[index]))
            {
                SetText(popupNoticeText, "사용할 수 없습니다");
                return;
            }

            SetText(popupNoticeText, current.level > before ? $"레벨 {current.level} 달성!" : string.Empty);

            RefreshCharacter();
            RefreshPopup();

            // 목록 칸과 편성 슬롯의 레벨·전투력도 달라졌다.
            OnCharacterChanged?.Invoke();
        }

        private void RefreshPopup()
        {
            if (current == null)
                return;

            SetText(popupLevelText, current.IsMaxLevel ? $"Lv.{current.level} (MAX)" : $"Lv.{current.level}");
            ShowExp(current, popupExpFill, popupExpText);

            var items = expItemCatalog != null ? expItemCatalog.items : null;
            for (int i = 0; i < expCards.Count; i++)
            {
                if (items == null || i >= items.Length || items[i] == null)
                    continue;

                int owned = PlayerInventory.GetCount(items[i].kind);
                SetText(expCards[i].ownedText, $"x{owned}");

                if (expCards[i].button != null)
                    expCards[i].button.interactable = owned > 0 && !current.IsMaxLevel;
            }
        }

        // ------------------------------------------------------------------ 잡다

        private void Clear()
        {
            SetText(nameText, string.Empty);
            SetText(idText, string.Empty);
            SetText(gradeText, string.Empty);
            SetText(levelText, string.Empty);
            SetText(powerText, string.Empty);
            SetText(acquiredText, string.Empty);
            SetText(frameColorText, string.Empty);
            SetText(expText, string.Empty);
            SetText(skillNameText, string.Empty);
            SetText(skillCategoryText, string.Empty);
            SetText(skillDescText, string.Empty);
            SetText(skillMatchText, string.Empty);

            if (spineView != null)
                spineView.Clear();

            if (skillRange != null)
                skillRange.Clear();

            if (expFill != null)
                expFill.fillAmount = 0f;
        }

}
}
