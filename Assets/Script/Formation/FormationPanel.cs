using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.Formation
{
    /// <summary>보유 목록을 늘어놓는 기준.</summary>
    public enum RosterSort
    {
        Level = 0,
        CombatPower = 1,
        Grade = 2,

        /// <summary>
        /// 획득순. 순서는 <c>CharacterRoster.ownedCharacters</c> 를 기준으로 하고(먼저 넣은 것이
        /// 먼저 얻은 것), 칸에 보여줄 "며칠 전"은 <see cref="App.PlayerCollection"/> 이 계산한다.
        /// <b>진짜 획득 시각은 아직 없다</b> - 세이브가 생기면 그쪽에 실제 값을 넣으면 된다.
        /// </summary>
        Acquired = 3,
    }

    /// <summary>
    /// 편성 화면. 위에는 리더/파트너 슬롯, 아래에는 보유 캐릭터 5x2 격자와 정렬·페이지.
    ///
    /// <b>고르는 방식</b>: 위의 슬롯 하나가 항상 "지금 채우는 칸"이고(기본은 리더), 아래에서
    /// 캐릭터를 누르면 그 칸에 들어간다. 슬롯을 누르면 채우는 칸이 바뀐다.
    /// 이미 반대편에 있는 캐릭터를 고르면 <b>둘을 맞바꾼다</b> - 같은 캐릭터가 양쪽에 설 수 없고,
    /// 그렇다고 "이미 편성됨"이라고 거절하면 자리를 바꾸려는 사람이 두 번 일해야 한다.
    ///
    /// <b>확정을 누르기 전에는 <see cref="PartySelection"/> 을 건드리지 않는다</b> - 뒤로 나가면
    /// 원래 편성이 그대로여야 한다.
    /// </summary>
    public class FormationPanel : MonoBehaviour
    {
        [Serializable]
        private class SlotView
        {
            [Tooltip("이 슬롯을 누르면 '지금 채우는 칸'이 여기로 바뀐다.")]
            public Button button;

            [Tooltip("선택된 슬롯임을 알리는 테두리. 켜고 끈다.")]
            public GameObject selectedMark;

            public Text roleText;
            public Text nameText;

            [Tooltip("<b>캐릭터 아이콘만</b> 넣는다 - 퍼즐 프레임은 보유 목록 칸에만 쓴다.")]
            public Image iconImage;

            [Tooltip("캐릭터가 들어올 때 말랑 튕기는 효과. 없어도 된다.")]
            public SquashPunch iconPunch;

            public Text levelText;
            public Text powerText;
            public SkillRangePreview skillRange;
            public Text skillTypeText;
        }

        private class RosterCell
        {
            public GameObject root;

            /// <summary>
            /// <b>Button 이 아니다.</b> 짧게 누르면 편성, 꾹 누르면 상세 화면이라 둘을 갈라야 하는데
            /// Button 은 손을 뗄 때 무조건 클릭을 발행해서 두 동작이 겹친다(정렬 버튼과 같은 이유).
            /// </summary>
            public LongPressButton press;
            public PuzzlePieceIcon icon;
            public Text nameText;
            public Text infoText;
            public GameObject leaderMark;
            public GameObject partnerMark;
        }

        [Header("데이터")]
        [SerializeField] private CharacterRoster roster;

        [Header("위 - 슬롯")]
        [SerializeField] private SlotView leaderSlot;
        [SerializeField] private SlotView partnerSlot;

        [Header("아래 - 보유 목록")]
        [SerializeField] private RectTransform cellContent;
        [SerializeField] private GameObject cellTemplate;

        [Tooltip("한 페이지에 놓는 칸 수. 기획은 5x2 = 10.")]
        [SerializeField] private int columns = 5;
        [SerializeField] private int rows = 2;

        [Tooltip("칸 사이 간격(칸 크기 대비 비율).")]
        [Range(0f, 0.4f)]
        [SerializeField] private float cellGapFraction = 0.08f;

        [Header("정렬 - 파트너 슬롯 아래 버튼")]
        [Tooltip("짧게 누르면 다음 기준으로 넘어가고, 꾹 누르면 상세 창이 열린다.")]
        [SerializeField] private LongPressButton sortCycleButton;

        [Tooltip("지금 기준과 방향을 한 줄로 보여준다. 예: \"레벨순 ↓\"")]
        [SerializeField] private Text sortLabelText;

        [Header("정렬 - 상세 창")]
        [Tooltip("꾹 눌렀을 때 열리는 창. 꺼둔 채로 씬에 있어야 한다.")]
        [SerializeField] private GameObject sortPopup;

        [Tooltip("레벨 / 전투력 / 등급 / 획득 순서대로. RosterSort 순서와 같아야 한다.")]
        [SerializeField] private Button[] sortButtons;

        [SerializeField] private Button sortDirectionButton;
        [SerializeField] private Text sortDirectionText;
        [SerializeField] private Button sortPopupCloseButton;

        [SerializeField] private Color sortNormalColor = new Color(0.20f, 0.22f, 0.30f, 0.95f);
        [SerializeField] private Color sortActiveColor = new Color(0.30f, 0.45f, 0.34f, 0.98f);

        [Header("페이지")]
        [SerializeField] private Button prevPageButton;
        [SerializeField] private Button nextPageButton;
        [SerializeField] private Text pageText;

        [Header("하단")]
        [SerializeField] private Button backButton;
        [SerializeField] private Button confirmButton;

        [Header("상세(강화) 화면")]
        [Tooltip("보유 목록에서 조각을 꾹 눌렀을 때 열린다. 꺼둔 채로 씬에 있어야 한다.")]
        [SerializeField] private CharacterDetailPanel detailPanel;

        [Header("안내")]
        [SerializeField] private Text noticeText;
        [SerializeField] private float noticeDuration = 1.6f;

        private readonly List<RosterCell> cells = new List<RosterCell>();

        /// <summary>정렬한 결과. 페이지 계산과 칸 채우기가 이걸 본다.</summary>
        private readonly List<PanelType> sorted = new List<PanelType>();

        private PanelType pendingLeader;
        private PanelType pendingPartner;

        private RosterSort sort = RosterSort.Level;

        /// <summary>오름차순인지. 기본은 <b>내림차순</b>(높은 것이 앞)이라 false 다.</summary>
        private bool ascending;

        /// <summary>보유 목록에서 몇 번째였는지. 획득 시각을 지어낼 때 기준이 된다.</summary>
        private readonly Dictionary<PanelType, int> rosterOrder = new Dictionary<PanelType, int>();

        private int page;
        private bool editingPartner;
        private float noticeRemaining;

        /// <summary>확정. 새 편성이 <see cref="PartySelection"/> 에 들어간 뒤에 불린다.</summary>
        public event Action OnConfirmed;

        public event Action OnBack;

        private int PageSize => Mathf.Max(1, columns * rows);

        private int PageCount => Mathf.Max(1, (sorted.Count + PageSize - 1) / PageSize);

        private void Awake()
        {
            if (cellTemplate != null)
                cellTemplate.SetActive(false);

            if (leaderSlot != null && leaderSlot.button != null)
                leaderSlot.button.onClick.AddListener(() => SetEditingPartner(false));

            if (partnerSlot != null && partnerSlot.button != null)
                partnerSlot.button.onClick.AddListener(() => SetEditingPartner(true));

            if (sortCycleButton != null)
            {
                sortCycleButton.OnShortPress += CycleSort;
                sortCycleButton.OnLongPress += OpenSortPopup;
            }

            if (sortButtons != null)
            {
                for (int i = 0; i < sortButtons.Length; i++)
                {
                    if (sortButtons[i] == null)
                        continue;

                    // 버튼이 재사용되지는 않지만 인덱스로 되짚는 방식을 목록들과 맞춰둔다.
                    int index = i;
                    sortButtons[i].onClick.AddListener(() => SetSort((RosterSort)index));
                }
            }

            if (sortDirectionButton != null)
                sortDirectionButton.onClick.AddListener(ToggleDirection);

            if (sortPopupCloseButton != null)
                sortPopupCloseButton.onClick.AddListener(CloseSortPopup);

            if (sortPopup != null)
                sortPopup.SetActive(false);

            if (detailPanel != null)
            {
                detailPanel.OnBack += CloseDetail;
                detailPanel.OnCharacterChanged += HandleCharacterChanged;
                detailPanel.Hide();
            }

            if (prevPageButton != null)
                prevPageButton.onClick.AddListener(() => MovePage(-1));

            if (nextPageButton != null)
                nextPageButton.onClick.AddListener(() => MovePage(1));

            if (backButton != null)
                backButton.onClick.AddListener(() => OnBack?.Invoke());

            if (confirmButton != null)
                confirmButton.onClick.AddListener(Confirm);
        }

        private void OnDestroy()
        {
            // 이벤트는 풀어둔다 - 컴포넌트가 먼저 사라지면 남은 구독이 죽은 객체를 부른다.
            if (sortCycleButton != null)
            {
                sortCycleButton.OnShortPress -= CycleSort;
                sortCycleButton.OnLongPress -= OpenSortPopup;
            }

            if (detailPanel != null)
            {
                detailPanel.OnBack -= CloseDetail;
                detailPanel.OnCharacterChanged -= HandleCharacterChanged;
            }
        }

        public void Show()
        {
            gameObject.SetActive(true);

            // 지금 편성을 들고 들어와서 여기서만 만지작거린다 - 확정 전에는 원본을 안 건드린다.
            pendingLeader = PartySelection.Leader;
            pendingPartner = PartySelection.Partner;

            editingPartner = false;
            page = 0;
            CloseSortPopup();
            CloseDetail();

            RebuildSorted();
            RefreshAll();
            ShowNotice(string.Empty);
        }

        public void Hide() => gameObject.SetActive(false);

        private void Update()
        {
            TickNotice();
        }

        // ------------------------------------------------------------------ 정렬 / 페이지

        private void SetSort(RosterSort value)
        {
            if (sort == value)
                return;

            sort = value;
            page = 0;
            RebuildSorted();
            RefreshAll();
        }

        /// <summary>레벨순 → 전투력순 → 등급순 → 획득순 → 레벨순. 짧게 누르면 이것만 돈다.</summary>
        private void CycleSort()
        {
            int count = System.Enum.GetValues(typeof(RosterSort)).Length;
            SetSort((RosterSort)(((int)sort + 1) % count));
        }

        private void ToggleDirection()
        {
            ascending = !ascending;
            page = 0;
            RebuildSorted();
            RefreshAll();
        }

        private void OpenSortPopup()
        {
            if (sortPopup != null)
                sortPopup.SetActive(true);
        }

        private void CloseSortPopup()
        {
            if (sortPopup != null)
                sortPopup.SetActive(false);
        }

        /// <summary>기준의 한글 이름.</summary>
        private static string SortLabel(RosterSort value)
        {
            switch (value)
            {
                case RosterSort.CombatPower: return "전투력순";
                case RosterSort.Grade: return "등급순";
                case RosterSort.Acquired: return "획득순";
                default: return "레벨순";
            }
        }

        private void MovePage(int delta)
        {
            int next = Mathf.Clamp(page + delta, 0, PageCount - 1);
            if (next == page)
                return;

            page = next;
            RefreshCells();
            RefreshPageControls();
        }

        private void RebuildSorted()
        {
            sorted.Clear();

            var owned = roster != null ? roster.ownedCharacters : null;
            if (owned == null)
                return;

            for (int i = 0; i < owned.Count; i++)
            {
                // ⭐ 담보로 잡힌 캐릭터는 <b>어디서도 쓸 수 없다</b>(2026-09-02) - 목록에도 안 뜬다.
                if (owned[i] != null && !JojoPuzzle.App.BankLoan.IsLocked(owned[i]))
                    sorted.Add(owned[i]);
            }

            // 목록에 들어 있는 순서가 곧 획득 순서다(먼저 넣은 것이 먼저 얻은 것).
            // 획득 시각을 지어낼 때도, 값이 같을 때 순서를 지킬 때도 이 번호를 쓴다.
            rosterOrder.Clear();
            for (int i = 0; i < sorted.Count; i++)
                rosterOrder[sorted[i]] = i;

            // 안정 정렬이 필요하다 - 값이 같으면 획득순(원래 순서)이 유지되는 게 자연스럽다.
            // List.Sort 는 불안정하므로 원래 인덱스를 타이브레이커로 쓴다.
            sorted.Sort((a, b) =>
            {
                int result = CompareBySort(a, b);
                if (result == 0)
                    return rosterOrder[a].CompareTo(rosterOrder[b]);

                // 방향은 <b>맨 마지막에</b> 뒤집는다 - 타이브레이커까지 뒤집으면 값이 같은
                // 캐릭터들의 순서가 방향에 따라 요동친다.
                return ascending ? -result : result;
            });
        }

        /// <summary>
        /// 정렬 기준 비교. 여기서는 언제나 <b>내림차순</b>(높은 것·최근 것이 앞)을 돌려주고,
        /// 오름차순 뒤집기는 부르는 쪽이 한다.
        /// </summary>
        private int CompareBySort(PanelType a, PanelType b)
        {
            switch (sort)
            {
                case RosterSort.CombatPower:
                    return b.CombatPower.CompareTo(a.CombatPower);

                case RosterSort.Grade:
                    // CharacterGrade 는 GR·SR·BR 순으로 정의돼 있어 값이 작을수록 높은 등급이다.
                    return ((int)a.grade).CompareTo((int)b.grade);

                case RosterSort.Acquired:
                    // 최근에 얻은 것이 앞. 목록 뒤쪽일수록 최근이다.
                    return rosterOrder[b].CompareTo(rosterOrder[a]);

                default:
                    return b.level.CompareTo(a.level);
            }
        }

        /// <summary>정렬 기준에 따라 칸에 곁들여 보여줄 한 줄.</summary>
        private string GetCellInfo(PanelType character)
        {
            switch (sort)
            {
                case RosterSort.CombatPower:
                    return $"전투력 {character.CombatPower:N0}";

                case RosterSort.Grade:
                    return character.grade.ToString();

                case RosterSort.Acquired:
                {
                    int days = PlayerCollection.GetDaysOwned(
                        character, rosterOrder[character], rosterOrder.Count, DateTime.UtcNow);

                    return days <= 0 ? "오늘" : $"{days}일 전";
                }

                default:
                    return $"Lv.{character.level}";
            }
        }

        // ------------------------------------------------------------------ 고르기

        private void SetEditingPartner(bool value)
        {
            editingPartner = value;
            RefreshSlotMarks();
        }

        private void ChooseCharacter(int indexInPage)
        {
            int index = page * PageSize + indexInPage;
            if (index < 0 || index >= sorted.Count)
                return;

            var picked = sorted[index];
            if (picked == null)
                return;

            if (editingPartner)
            {
                // 리더 자리에 있던 캐릭터를 파트너로 고르면 서로 맞바꾼다.
                if (picked == pendingLeader)
                    pendingLeader = pendingPartner;

                pendingPartner = picked;
            }
            else
            {
                if (picked == pendingPartner)
                    pendingPartner = pendingLeader;

                pendingLeader = picked;
            }

            RefreshSlots();
            RefreshCells();

            // 어느 칸에 들어갔는지 눈으로 알려준다.
            PlayPunch(editingPartner ? partnerSlot : leaderSlot);
        }

        private static void PlayPunch(SlotView slot)
        {
            if (slot != null && slot.iconPunch != null)
                slot.iconPunch.Play();
        }

        /// <summary>보유 목록의 조각을 꾹 눌렀을 때. 그 캐릭터의 상세를 연다.</summary>
        private void OpenDetail(int indexInPage)
        {
            if (detailPanel == null)
                return;

            int index = page * PageSize + indexInPage;
            if (index < 0 || index >= sorted.Count)
                return;

            var character = sorted[index];
            if (character == null)
                return;

            int days = PlayerCollection.GetDaysOwned(
                character, rosterOrder[character], rosterOrder.Count, DateTime.UtcNow);

            detailPanel.Show(character, days);
        }

        /// <summary>
        /// 상세 화면에서 레벨이 올랐다. 레벨·전투력이 바뀌었으니 목록과 슬롯을 다시 그리고,
        /// <b>정렬 결과도 다시 만든다</b> - 레벨순·전투력순이면 자리가 달라져야 맞다.
        /// </summary>
        private void HandleCharacterChanged()
        {
            RebuildSorted();
            RefreshSlots();
            RefreshCells();
        }

        private void CloseDetail()
        {
            if (detailPanel != null)
                detailPanel.Hide();
        }

        private void Confirm()
        {
            if (pendingLeader == null || pendingPartner == null)
            {
                ShowNotice("리더와 파트너를 모두 골라주세요");
                return;
            }

            if (pendingLeader == pendingPartner)
            {
                // 맞바꾸기 때문에 여기 걸릴 일은 없지만, 규칙을 코드로도 못 박아둔다.
                ShowNotice("같은 캐릭터를 둘 다 쓸 수 없습니다");
                return;
            }

            PartySelection.Set(pendingLeader, pendingPartner);
            OnConfirmed?.Invoke();
        }

        // ------------------------------------------------------------------ 그리기

        private void RefreshAll()
        {
            RefreshSlots();
            RefreshSortButtons();
            EnsureCells();
            RefreshCells();
            RefreshPageControls();
        }

        private void RefreshSlots()
        {
            FillSlot(leaderSlot, "리더", pendingLeader);
            FillSlot(partnerSlot, "파트너", pendingPartner);
            RefreshSlotMarks();
        }

        private void FillSlot(SlotView slot, string role, PanelType character)
        {
            if (slot == null)
                return;

            SetText(slot.roleText, role);

            // displayName 이 비어 있는 애셋이 많아 애셋 이름으로 물러선다 - 빈 칸보다는 낫다.
            SetText(slot.nameText, character != null ? DisplayNameOf(character) : "비어 있음");

            if (slot.iconImage != null)
            {
                slot.iconImage.sprite = character != null ? character.icon : null;

                // 스프라이트가 없으면 아예 끈다 - 켜두면 흰 사각형이 남는다.
                slot.iconImage.enabled = character != null && character.icon != null;
            }

            SetText(slot.levelText, character != null ? $"Lv.{character.level}" : string.Empty);
            SetText(slot.powerText, character != null ? $"전투력 {character.CombatPower:N0}" : string.Empty);

            var skill = character != null ? character.skill : null;

            if (slot.skillRange != null)
                slot.skillRange.Show(skill);

            SetText(slot.skillTypeText, skill != null ? skill.CategoryLabel : string.Empty);
        }

        private void RefreshSlotMarks()
        {
            if (leaderSlot != null && leaderSlot.selectedMark != null)
                leaderSlot.selectedMark.SetActive(!editingPartner);

            if (partnerSlot != null && partnerSlot.selectedMark != null)
                partnerSlot.selectedMark.SetActive(editingPartner);
        }

        private void RefreshSortButtons()
        {
            // 화살표로 방향을 보인다 - 글자로 적으면 좁은 버튼을 넘친다.
            if (sortLabelText != null)
                sortLabelText.text = $"{SortLabel(sort)} {(ascending ? "\u2191" : "\u2193")}";

            if (sortDirectionText != null)
                sortDirectionText.text = ascending ? "오름차순 \u2191" : "내림차순 \u2193";

            if (sortButtons == null)
                return;

            for (int i = 0; i < sortButtons.Length; i++)
            {
                if (sortButtons[i] == null)
                    continue;

                var image = sortButtons[i].targetGraphic as Image;
                if (image != null)
                    image.color = (RosterSort)i == sort ? sortActiveColor : sortNormalColor;
            }
        }

        private void EnsureCells()
        {
            if (cellTemplate == null || cellContent == null)
                return;

            float stepX = 1f / Mathf.Max(1, columns);
            float stepY = 1f / Mathf.Max(1, rows);
            float gapX = stepX * cellGapFraction * 0.5f;
            float gapY = stepY * cellGapFraction * 0.5f;

            while (cells.Count < PageSize)
            {
                int index = cells.Count;
                var go = Instantiate(cellTemplate, cellContent);
                go.name = $"RosterCell{index}";

                int column = index % columns;

                // 위에서 아래로 채운다 - 앵커는 아래가 0이라 행을 뒤집어야 한다.
                int row = rows - 1 - (index / columns);

                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(column * stepX + gapX, row * stepY + gapY);
                rect.anchorMax = new Vector2((column + 1) * stepX - gapX, (row + 1) * stepY - gapY);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var cell = new RosterCell
                {
                    root = go,
                    press = go.GetComponent<LongPressButton>(),
                    icon = go.GetComponentInChildren<PuzzlePieceIcon>(true),
                    nameText = FindText(go, "NameText"),
                    infoText = FindText(go, "InfoText"),
                    leaderMark = FindChild(go, "LeaderMark"),
                    partnerMark = FindChild(go, "PartnerMark")
                };

                // 칸은 페이지마다 다시 쓰이므로 캐릭터를 람다에 가두지 않고 자리 번호로 되짚는다.
                int slotInPage = index;
                if (cell.press != null)
                {
                    cell.press.OnShortPress += () => ChooseCharacter(slotInPage);
                    cell.press.OnLongPress += () => OpenDetail(slotInPage);
                }

                cells.Add(cell);
            }
        }

        private void RefreshCells()
        {
            int start = page * PageSize;

            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                int index = start + i;
                bool used = index < sorted.Count;

                cell.root.SetActive(used);
                if (!used)
                    continue;

                var character = sorted[index];

                if (cell.icon != null)
                    cell.icon.Show(character);

                SetText(cell.nameText, DisplayNameOf(character));
                SetText(cell.infoText, GetCellInfo(character));

                if (cell.leaderMark != null)
                    cell.leaderMark.SetActive(character == pendingLeader);

                if (cell.partnerMark != null)
                    cell.partnerMark.SetActive(character == pendingPartner);
            }
        }

        private void RefreshPageControls()
        {
            if (pageText != null)
                pageText.text = $"{page + 1} / {PageCount}";

            if (prevPageButton != null)
                prevPageButton.interactable = page > 0;

            if (nextPageButton != null)
                nextPageButton.interactable = page < PageCount - 1;
        }

        // ------------------------------------------------------------------ 잡다

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

        /// <summary>displayName 이 비어 있는 애셋이 많아서 애셋 이름으로 물러선다.</summary>
        private static string DisplayNameOf(PanelType character)
        {
            if (character == null)
                return string.Empty;

            return character.DisplayName;
        }

private static GameObject FindChild(GameObject root, string childName)
        {
            var t = root.transform.Find(childName);
            return t != null ? t.gameObject : null;
        }
    }
}
