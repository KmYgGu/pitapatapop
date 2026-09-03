using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using Spine.Unity;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.StageSelect
{
    /// <summary>
    /// 전투 준비 화면. 고른 스테이지와 편성을 보여주고, 아이템을 고른 뒤 배틀로 넘긴다.
    ///
    /// <b>여기서 골드·하트가 실제로 빠진다</b>(전투 개시를 누른 순간, <see cref="StageEntry.Commit"/>).
    /// 아이템을 고르는 동안에는 아무것도 차감되지 않는다 - 뒤로 나갔다가 다시 들어와도 손해가
    /// 없어야 하고, 반쯤 차감된 상태가 생기면 안 된다.
    ///
    /// 적·아군 캐릭터는 씬에 박혀 있지 않고 <see cref="SpineCharacterView"/> 가 런타임에 세운다 -
    /// 어느 캐릭터가 설지는 고른 스테이지와 편성에 달렸기 때문이다.
    /// </summary>
    public class StagePrepPanel : MonoBehaviour
    {
        private class ItemCard
        {
            public GameObject root;
            public Button button;
            public Image background;
            public Text nameText;
            public Text priceText;
            public GameObject selectedMark;
        }

        [Header("스테이지")]
        [SerializeField] private Image bannerImage;
        [SerializeField] private Text stageNameText;

        [Header("아군")]
        [SerializeField] private SpineCharacterView leaderView;
        [SerializeField] private Text leaderLevelText;
        [SerializeField] private SpineCharacterView partnerView;
        [SerializeField] private Text partnerLevelText;

        [Tooltip("편성 화면이 없어서 PartySelection 이 비어 있을 때 쓸 임시 편성.")]
        [SerializeField] private PanelType fallbackLeader;
        [SerializeField] private PanelType fallbackPartner;

        [Tooltip("Spine 이 아직 없는 캐릭터를 세울 때 대신 쓸 스켈레톤. " +
                 "지금 speech 가 연결된 캐릭터가 하나뿐이라 나머지가 빈 칸으로 나오는 걸 막는 <b>임시</b> 조치다. " +
                 "캐릭터마다 Spine 이 붙으면 비워도 된다.")]
        [SerializeField] private SkeletonDataAsset placeholderSpine;

        [Header("적")]
        [SerializeField] private SpineCharacterView enemyView;
        [SerializeField] private Text enemyLevelText;

        [Header("전투력")]
        [SerializeField] private Text combatPowerText;

        [Header("아이템")]
        [SerializeField] private BattleItemCatalog itemCatalog;
        [SerializeField] private RectTransform itemContent;
        [SerializeField] private GameObject itemTemplate;
        [SerializeField] private float itemWidth = 72f;
        [SerializeField] private float itemSpacing = 6f;
        [SerializeField] private Color itemNormalColor = new Color(0.20f, 0.22f, 0.30f, 0.95f);
        [SerializeField] private Color itemSelectedColor = new Color(0.30f, 0.45f, 0.34f, 0.98f);

        [Header("하단")]
        [SerializeField] private Button backButton;
        [SerializeField] private Button startButton;
        [SerializeField] private Button formationButton;

        [Header("안내")]
        [SerializeField] private Text noticeText;
        [SerializeField] private float noticeDuration = 1.6f;

        [Header("상태 표시")]
        [Tooltip("골드를 쓰면 곧바로 반영되도록 여기서 다시 그린다.")]
        [SerializeField] private PlayerStatusBar statusBar;

        [Header("퇴장 연출")]
        [Tooltip("아군 일행. <b>왼쪽</b> 화면 밖으로 뛰어 나간다. 비워두면 연출 없이 곧바로 넘어간다.")]
        [SerializeField] private RunAcrossUI allyRun;

        [Tooltip("적. <b>오른쪽</b> 화면 밖으로 뛰어 나간다.")]
        [SerializeField] private RunAcrossUI enemyRun;

        [Tooltip("다 뛰어 나간 뒤 화면을 덮을 암막. 배틀 씬이 이걸 걷으며 시작한다.")]
        [SerializeField] private ScreenFadeUI fade;

        [SerializeField] private float fadeOutDuration = 0.35f;

        // 퇴장 연출이 도는 중인지. 전투 개시를 두 번 눌러도 두 번 넘어가지 않게 한다.
        private bool leaving;

        private readonly List<ItemCard> itemCards = new List<ItemCard>();
        private float noticeRemaining;

        public event Action OnBack;
        public event Action OnFormationRequested;

        private PanelType Leader => PartySelection.Leader != null ? PartySelection.Leader : fallbackLeader;

        private PanelType Partner => PartySelection.Partner != null ? PartySelection.Partner : fallbackPartner;

        private void Awake()
        {
            if (itemTemplate != null)
                itemTemplate.SetActive(false);

            if (backButton != null)
                backButton.onClick.AddListener(() => OnBack?.Invoke());

            if (formationButton != null)
                formationButton.onClick.AddListener(() => OnFormationRequested?.Invoke());

            if (startButton != null)
                startButton.onClick.AddListener(StartBattle);
        }

        public void Show()
        {
            gameObject.SetActive(true);
            Refresh();
        }

        public void Hide() => gameObject.SetActive(false);

        private void Update()
        {
            TickNotice();
        }

        private void Refresh()
        {
            var stage = StageEntry.Stage;

            if (stageNameText != null)
                stageNameText.text = stage != null ? stage.displayName : string.Empty;

            if (bannerImage != null)
            {
                bool hasBanner = stage != null && stage.banner != null;
                bannerImage.sprite = hasBanner ? stage.banner : null;

                // 그림이 없으면 단색 판으로 남겨둔다 - 끄면 배너 자리가 통째로 사라져
                // 나중에 그림을 넣었을 때 배치가 달라 보인다.
                bannerImage.color = hasBanner ? Color.white : new Color(0.18f, 0.20f, 0.28f, 0.9f);
            }

            ShowCharacter(leaderView, leaderLevelText, Leader);
            ShowCharacter(partnerView, partnerLevelText, Partner);

            var enemy = stage != null ? stage.enemy : null;
            ShowCharacter(enemyView, null, enemy);

            // 적 레벨은 캐릭터가 아니라 <b>스테이지의 권장 레벨</b>이다 - 적 애셋의 level 은
            // 도감 값이라 스테이지마다 다르게 둘 수 없다.
            if (enemyLevelText != null)
                enemyLevelText.text = stage != null ? $"Lv.{stage.recommendedLevel}" : string.Empty;

            if (combatPowerText != null)
            {
                int power = PartySelection.GetTotalCombatPower(fallbackLeader, fallbackPartner);
                combatPowerText.text = $"종합 전투력 {power:N0}";
            }

            BuildItems();
            ShowNotice(string.Empty);

            if (statusBar != null)
                statusBar.RefreshProfile();
        }

        private void ShowCharacter(SpineCharacterView view, Text levelText, PanelType character)
        {
            if (levelText != null)
                levelText.text = character != null ? $"Lv.{character.level}" : string.Empty;

            if (view == null)
                return;

            // 대사집이 Spine 애셋을 들고 있다(캐릭터마다 하나).
            var spine = character != null && character.speech != null ? character.speech.spine : null;

            // 아직 Spine 이 없는 캐릭터는 대체 스켈레톤으로라도 세운다 - 빈 칸으로 두면
            // 배치를 눈으로 확인할 수가 없다. 캐릭터가 아예 없으면(적이 안 정해진 스테이지 등)
            // 그때는 정말로 비운다.
            if (spine == null && character != null)
                spine = placeholderSpine;

            if (spine == null)
                view.Clear();
            else
                view.Show(spine);
        }

        // ------------------------------------------------------------------ 아이템

        private void BuildItems()
        {
            var items = itemCatalog != null ? itemCatalog.items : null;
            int count = items != null ? items.Length : 0;

            EnsureItemCards(count);

            float x = 0f;
            for (int i = 0; i < itemCards.Count; i++)
            {
                var card = itemCards[i];
                bool used = i < count && items[i] != null;

                card.root.SetActive(used);
                if (!used)
                    continue;

                var item = items[i];

                var rect = (RectTransform)card.root.transform;
                rect.anchoredPosition = new Vector2(x, 0f);
                x += itemWidth + itemSpacing;

                SetText(card.nameText, item.displayName);
                SetText(card.priceText, PriceLabel(item));

                RefreshItemSelection(i);
            }
        }

        private void EnsureItemCards(int count)
        {
            if (itemTemplate == null || itemContent == null)
                return;

            while (itemCards.Count < count)
            {
                var go = Instantiate(itemTemplate, itemContent);
                go.name = $"ItemCard{itemCards.Count}";

                var rect = (RectTransform)go.transform;

                // 왼쪽부터 가로로 늘어놓는다. 세로는 부모를 채운다.
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.offsetMin = new Vector2(rect.offsetMin.x, 0f);
                rect.offsetMax = new Vector2(rect.offsetMax.x, 0f);
                rect.sizeDelta = new Vector2(itemWidth, 0f);

                var mark = go.transform.Find("SelectedMark");

                var card = new ItemCard
                {
                    root = go,
                    button = go.GetComponent<Button>(),
                    background = go.GetComponent<Image>(),
                    nameText = FindText(go, "NameText"),
                    priceText = FindText(go, "PriceText"),
                    selectedMark = mark != null ? mark.gameObject : null
                };

                int index = itemCards.Count;
                if (card.button != null)
                    card.button.onClick.AddListener(() => ToggleItem(index));

                itemCards.Add(card);
            }
        }

        /// <summary>
        /// 값 자리에 무엇을 적을지. <b>갖고 있으면 값이 아니라 개수를 적는다</b>(2026-08-28
        /// 사용자 기획) - 보유분이 있는 동안은 골드가 나가지 않으므로 가격을 보여주면 거짓말이 된다.
        /// </summary>
        private static string PriceLabel(BattleItem item)
        {
            int owned = PlayerInventory.GetCount(item.kind);
            if (owned > 0)
                return $"보유 {owned}";

            return item.price > 0 ? $"{item.price:N0}" : "무료";
        }

        private void ToggleItem(int index)
        {
            var items = itemCatalog != null ? itemCatalog.items : null;
            if (items == null || index < 0 || index >= items.Length || items[index] == null)
                return;

            var item = items[index];
            bool selected = !StageEntry.IsItemSelected(item.kind);

            // 담는 순간 골드를 빼지는 않지만, 살 수 없는 걸 담아두면 전투 개시에서야 막혀
            // 왜 안 되는지 알기 어렵다. 그래서 담을 때 미리 확인한다.
            //
            // <b>갖고 있는 아이템은 이 검사를 건너뛴다</b> - 그건 사는 게 아니라 꺼내 쓰는 것이라
            // 골드가 0이어도 담을 수 있어야 한다.
            if (selected && !StageEntry.IsItemOwned(item.kind))
            {
                int wouldCost = StageEntry.GetTotalPrice(itemCatalog) + item.price;
                if (PlayerProfile.Gold < wouldCost)
                {
                    ShowNotice("골드가 모자랍니다 - 우편함을 확인해 보세요");
                    return;
                }
            }

            StageEntry.SetItemSelected(item.kind, selected);
            RefreshItemSelection(index);
        }

        private void RefreshItemSelection(int index)
        {
            var items = itemCatalog != null ? itemCatalog.items : null;
            if (items == null || index < 0 || index >= items.Length || items[index] == null)
                return;

            if (index >= itemCards.Count)
                return;

            bool selected = StageEntry.IsItemSelected(items[index].kind);
            var card = itemCards[index];

            if (card.background != null)
                card.background.color = selected ? itemSelectedColor : itemNormalColor;

            if (card.selectedMark != null)
                card.selectedMark.SetActive(selected);
        }

        // ------------------------------------------------------------------ 개시

        private void StartBattle()
        {
            // 이미 나가는 중이면 무시한다 - 연출이 도는 동안 한 번 더 누르면 골드가 두 번 빠진다.
            if (leaving)
                return;

            if (!StageEntry.Commit(itemCatalog, DateTime.UtcNow, out string reason))
            {
                ShowNotice(reason);

                // 차감이 안 됐어도 하트 개수 표시는 시간이 지나 바뀌었을 수 있다.
                if (statusBar != null)
                    statusBar.RefreshProfile();

                return;
            }

            leaving = true;
            StartCoroutine(LeaveRoutine());
        }

        /// <summary>
        /// 전투 개시 연출(2026-08-28 사용자 기획): <b>아군은 왼쪽으로, 적은 오른쪽으로</b> 뛰어
        /// 나가고(발밑에 먼지) 화면이 어두워진 뒤 배틀 씬으로 넘어간다.
        ///
        /// <b>차감은 이미 끝났다</b> - 연출 중에 뒤로 나갈 수 없게 버튼을 전부 잠근다.
        /// 여기서 되돌릴 길을 만들면 "골드는 빠졌는데 배틀에는 안 들어간" 상태가 생긴다.
        ///
        /// 둘은 <b>동시에</b> 뛴다 - 차례로 하면 늘어지고, 서로 반대쪽으로 가므로 겹치지도 않는다.
        /// </summary>
        private System.Collections.IEnumerator LeaveRoutine()
        {
            SetButtonsInteractable(false);

            var ally = allyRun != null ? StartCoroutine(allyRun.RunOut(-1f)) : null;
            var enemy = enemyRun != null ? StartCoroutine(enemyRun.RunOut(1f)) : null;

            if (ally != null)
                yield return ally;
            if (enemy != null)
                yield return enemy;

            if (fade != null)
                yield return StartCoroutine(fade.FadeOut(fadeOutDuration));

            AppScenes.GoToBattle();
        }

        private void SetButtonsInteractable(bool value)
        {
            if (startButton != null)
                startButton.interactable = value;
            if (backButton != null)
                backButton.interactable = value;
            if (formationButton != null)
                formationButton.interactable = value;

            for (int i = 0; i < itemCards.Count; i++)
            {
                if (itemCards[i].button != null)
                    itemCards[i].button.interactable = value;
            }
        }

        // ------------------------------------------------------------------ 안내

        /// <summary>바깥(흐름)에서 안내 한 줄을 띄운다.</summary>
        public void Notify(string message) => ShowNotice(message);

        private void ShowNotice(string message)
        {
            if (noticeText != null)
                noticeText.text = message ?? string.Empty;

            noticeRemaining = string.IsNullOrEmpty(message) ? 0f : noticeDuration;
        }

        private void TickNotice()
        {
            if (noticeRemaining <= 0f)
                return;

            noticeRemaining -= Time.deltaTime;
            if (noticeRemaining <= 0f)
                ShowNotice(string.Empty);
        }

}
}
