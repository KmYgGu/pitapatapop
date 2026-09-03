using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 방을 확대했을 때 아래에서 올라오는 <b>입주 화면</b>. 보유 캐릭터를 늘어놓고 하나를 골라
    /// 결정하면 그 방에 들어간다(2026-08-28 사용자 기획).
    ///
    /// <code>
    ///   "2층"                        라뷰린스   ← 지금 고른 캐릭터 이름
    ///   [조각][조각][조각][조각]                  ← 프레임 위에 "이 방"·"1층" 배지
    ///   [조각][조각][조각][조각]      ‹ 1/2 ›
    ///   [결정]            [닫기]
    /// </code>
    ///
    /// <b>규칙은 <see cref="ApartmentResidents"/> 가 안다</b> - 이 화면은 고른 것을 넘기기만 한다.
    /// 이사·중복 처리도 전부 그쪽이다.
    ///
    /// <b>⚠ 컴포넌트는 항상 켜져 있는 바깥에 붙는다</b>(껐다 켜는 건 자식 <c>root</c>) -
    /// 꺼진 오브젝트에 붙으면 Awake 가 안 돌아 버튼 연결이 안 된다.
    ///
    /// <b>드래그로도 페이지를 넘긴다.</b> 이 컴포넌트가 <see cref="IDragHandler"/> 를 들고 있고
    /// 시트의 Image 가 raycast 를 받으므로, 그 위에서 민 이벤트가 부모인 여기까지 올라온다
    /// (<c>EventBannerRotator</c> 와 같은 방식).
    /// </summary>
    public class RoomResidentPanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private GameObject root;

        [Header("머리말")]
        [SerializeField] private Text roomNameText;

        [Tooltip("<b>지금 고른 캐릭터의 이름</b>을 적는 곳. 아무도 안 골랐으면 '비어 있음'.")]
        [SerializeField] private Text currentText;

        [Tooltip("방 이름을 물어볼 곳(프레임 위 배지에 '1층' 이라고 적을 때 쓴다).")]
        [SerializeField] private ApartmentRooms rooms;

        [Header("보유 캐릭터")]
        [SerializeField] private CharacterRoster roster;
        [SerializeField] private RectTransform cellContent;

        [Tooltip("칸 하나의 본. 자식 이름: Piece(PuzzlePieceIcon) / NameText / SelectedMark, " +
                 "그리고 Piece 안에 HomeBadge > HomeText")]
        [SerializeField] private GameObject cellTemplate;

        [SerializeField] private int columns = 4;
        [SerializeField] private int rows = 2;
        [SerializeField] private float cellWidth = 62f;
        [SerializeField] private float cellHeight = 76f;
        [SerializeField] private float cellSpacing = 8f;

        [Header("페이지")]
        [SerializeField] private Button prevPageButton;
        [SerializeField] private Button nextPageButton;
        [SerializeField] private Text pageText;

        [Tooltip("이만큼(유닛) 밀어야 페이지가 넘어간다. 덜 밀면 제자리다.")]
        [SerializeField] private float swipeThreshold = 40f;

        [Header("버튼")]
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button closeButton;

        [Tooltip("<b>빈 방으로 만들기</b>(2026-08-28 사용자 추가). 실수로 넣은 캐릭터를 " +
                 "교체할 상대가 없을 때 빼는 길이다. " +
                 "<b>격자의 첫 칸을 차지한다</b> - 캐릭터 칸과 같은 크기·같은 줄에 있어야 " +
                 "'고르는 것들 중 하나'로 읽힌다. 빈 방에서는 아예 안 나온다.")]
        [SerializeField] private Button vacateButton;

        [Tooltip("아래에서 올라오는 판. 카메라가 방을 담을 때 <b>실제로 덮는 만큼</b>을 재려고 " +
                 "참조한다 - 레터박스 배율이 기기마다 달라서 비율을 숫자로 박을 수 없다.")]
        [SerializeField] private RectTransform sheet;

        /// <summary>
        /// 이 판이 화면 아래쪽에서 덮는 비율. 판을 안 물려뒀으면 음수 -
        /// 부르는 쪽이 인스펙터에 적힌 예전 값으로 물러선다.
        /// </summary>
        public float CoverFraction => UiScreenMetrics.CoverFractionFromBottom(sheet);

        [Header("색")]
        [SerializeField] private Color cellNormalColor = new Color(0.20f, 0.22f, 0.30f, 0.95f);
        [SerializeField] private Color cellSelectedColor = new Color(0.30f, 0.45f, 0.34f, 0.98f);

        /// <summary>닫혔을 때. 아파트 흐름이 카메라와 HUD 를 되돌린다.</summary>
        public event System.Action OnClosed;

        /// <summary>결정을 눌러 입주가 끝났을 때(인자 = 방 번호).</summary>
        public event System.Action<int> OnResidentChanged;

        /// <summary>
        /// 목록에서 <b>누굴 눌러봤을 때</b>(방 번호, 그 캐릭터). 아직 정한 게 아니라
        /// 입주 정보는 안 바뀐다 - 방에 미리 세워 보여주기만 하는 자리다
        /// (2026-08-30 사용자 지시: "누르면 곧바로 그 화면에 나타나야 고른 줄 안다").
        /// </summary>
        public event System.Action<int, PanelType> OnPreviewed;

        private class Cell
        {
            public GameObject root;
            public RectTransform rect;
            public Image background;
            public Button button;
            public PuzzlePieceIcon icon;
            public Text nameText;
            public GameObject homeBadge;
            public Text homeText;
            public GameObject selectedMark;
            public PanelType character;
        }

        private readonly List<Cell> cells = new List<Cell>();

        private int roomIndex = -1;
        private int page;
        private PanelType picked;
        private float dragStartX;

        private int PageSize => Mathf.Max(1, columns) * Mathf.Max(1, rows);

        private int OwnedCount
        {
            get
            {
                var owned = roster != null ? roster.ownedCharacters : null;
                return owned != null ? owned.Count : 0;
            }
        }

        /// <summary>빈 방 버튼이 첫 칸을 먹으므로 장당 캐릭터 수가 하나 줄어든다.</summary>
        private int PageCount
        {
            get
            {
                int reserved = roomIndex >= 0 && ApartmentResidents.Get(roomIndex) != null ? 1 : 0;
                return PageCountFor(Mathf.Max(1, PageSize - reserved));
            }
        }

        public bool IsOpen => root != null && root.activeSelf;

        private void Awake()
        {
            if (cellTemplate != null)
                cellTemplate.SetActive(false);

            if (confirmButton != null)
                confirmButton.onClick.AddListener(Confirm);

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (vacateButton != null)
                vacateButton.onClick.AddListener(Vacate);

            if (prevPageButton != null)
                prevPageButton.onClick.AddListener(() => TurnPage(-1));

            if (nextPageButton != null)
                nextPageButton.onClick.AddListener(() => TurnPage(1));

            if (root != null)
                root.SetActive(false);
        }

        /// <summary>방 하나를 연다.</summary>
        public void Open(int index, string roomName)
        {
            roomIndex = index;

            // <b>지금 사는 캐릭터가 처음부터 골라져 있다</b> - 방을 열자마자 결정을 누르면
            // 아무것도 안 바뀌는 게 자연스럽다(빈 방이면 아무도 안 골라진 상태).
            picked = ApartmentResidents.Get(index);

            // 그 캐릭터가 있는 쪽을 펴놓는다 - 지금 사는 사람이 다른 장에 있으면 안 보인다.
            page = 0;
            int at = IndexOf(picked);
            if (at >= 0)
            {
                int reserved = picked != null ? 1 : 0;   // 사는 사람이 있으면 첫 칸이 빈 방 버튼
                page = at / Mathf.Max(1, PageSize - reserved);
            }

            if (roomNameText != null)
                roomNameText.text = roomName;

            if (root != null)
                root.SetActive(true);

            Build();
        }

        public void Close()
        {
            if (root != null)
                root.SetActive(false);

            roomIndex = -1;
            OnClosed?.Invoke();
        }

        private void Confirm()
        {
            // 아무도 안 골랐으면 아무 일도 하지 않는다. <b>방을 비우는 길은 없다</b>(사용자 확정).
            if (roomIndex < 0 || picked == null)
            {
                Close();
                return;
            }

            ApartmentResidents.MoveIn(roomIndex, picked);
            OnResidentChanged?.Invoke(roomIndex);

            Close();
        }

        /// <summary>
        /// 이 방을 빈 방으로 만든다. <b>고른 것과 무관하게 지금 <em>살고 있는</em> 사람을 뺀다</b> -
        /// 목록에서 누굴 눌러본 뒤라도 "비우기"는 그 방을 비우는 뜻이어야 한다.
        /// </summary>
        private void Vacate()
        {
            if (roomIndex < 0 || ApartmentResidents.Get(roomIndex) == null)
                return;

            ApartmentResidents.Vacate(roomIndex);
            OnResidentChanged?.Invoke(roomIndex);

            Close();
        }

        private int IndexOf(PanelType character)
        {
            var owned = roster != null ? roster.ownedCharacters : null;
            if (owned == null || character == null)
                return -1;

            return owned.IndexOf(character);
        }

        // ------------------------------------------------------------------ 페이지

        private void TurnPage(int delta)
        {
            int next = Mathf.Clamp(page + delta, 0, PageCount - 1);
            if (next == page)
                return;

            page = next;
            Build();
        }

        public void OnBeginDrag(PointerEventData eventData) => dragStartX = eventData.position.x;

        public void OnDrag(PointerEventData eventData) { /* 미는 동안은 그리지 않는다 - 아래 참고 */ }

        /// <summary>
        /// <b>미는 동안 칸이 따라 움직이지는 않는다</b>(놓을 때 한 번에 넘어간다). 칸을 실제로
        /// 끌고 다니려면 두 장을 동시에 그려야 하는데, 여기는 장이 몇 개 안 되는 화면이라
        /// 그 복잡함이 값을 못 한다 - 배너(EventBannerRotator)와 다른 선택이다.
        /// </summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            float moved = eventData.position.x - dragStartX;
            if (Mathf.Abs(moved) < swipeThreshold)
                return;

            // 왼쪽으로 밀면 다음 장(책장을 넘기는 방향).
            TurnPage(moved < 0f ? 1 : -1);
        }

        // ------------------------------------------------------------------ 그리기

        private void Build()
        {
            var owned = roster != null ? roster.ownedCharacters : null;
            int total = owned != null ? owned.Count : 0;

            // <b>빈 방 버튼이 격자의 첫 칸을 먹는다</b> - 사는 사람이 있을 때만.
            bool showVacate = roomIndex >= 0 && ApartmentResidents.Get(roomIndex) != null;
            int reserved = showVacate ? 1 : 0;
            int perPage = Mathf.Max(1, PageSize - reserved);

            page = Mathf.Clamp(page, 0, PageCountFor(perPage) - 1);
            int first = page * perPage;
            int shown = Mathf.Clamp(total - first, 0, perPage);

            EnsureCells(PageSize);

            float totalWidth = columns * cellWidth + (columns - 1) * cellSpacing;

            // 첫 칸은 <b>모든 장에</b> 둔다 - 지우려고 들어왔는데 장을 넘겼다고 사라지면 안 된다.
            if (vacateButton != null)
            {
                vacateButton.gameObject.SetActive(showVacate);

                if (showVacate)
                {
                    var rect = (RectTransform)vacateButton.transform;
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                    rect.pivot = new Vector2(0.5f, 1f);
                    rect.sizeDelta = new Vector2(cellWidth, cellHeight);
                    rect.anchoredPosition = SlotPosition(0, totalWidth);
                }
            }

            for (int i = 0; i < cells.Count; i++)
            {
                bool used = i < shown && owned[first + i] != null;

                cells[i].root.SetActive(used);
                if (!used)
                {
                    cells[i].character = null;
                    continue;
                }

                var character = owned[first + i];
                cells[i].character = character;

                cells[i].rect.anchoredPosition = SlotPosition(i + reserved, totalWidth);

                cells[i].icon?.Show(character);

                if (cells[i].nameText != null)
                    cells[i].nameText.text = character.DisplayName;

                ApplyHomeBadge(cells[i], character);
            }

            RefreshCells();
            RefreshPage();
        }

        /// <summary>격자에서 <paramref name="slot"/> 번째 칸의 자리.</summary>
        private Vector2 SlotPosition(int slot, float totalWidth)
        {
            int col = slot % columns;
            int row = slot / columns;

            float x = -totalWidth * 0.5f + cellWidth * 0.5f + col * (cellWidth + cellSpacing);
            float y = -row * (cellHeight + cellSpacing);
            return new Vector2(x, y);
        }

        private int PageCountFor(int perPage)
            => Mathf.Max(1, Mathf.CeilToInt(OwnedCount / (float)Mathf.Max(1, perPage)));

        /// <summary>
        /// <b>거주 표시는 프레임 위에 얹는다</b>(2026-08-28 사용자 지시). 칸 아래에 한 줄을 더
        /// 두면 그만큼 칸이 길어져 여백이 사라진다 - 대부분의 캐릭터는 아무 데도 안 살아서
        /// 그 줄이 늘 비어 있기도 하다.
        /// </summary>
        private void ApplyHomeBadge(Cell cell, PanelType character)
        {
            int home = ApartmentResidents.FindRoomOf(character);
            bool show = home >= 0;

            if (cell.homeBadge != null)
                cell.homeBadge.SetActive(show);

            if (!show || cell.homeText == null)
                return;

            cell.homeText.text = home == roomIndex
                ? "이 방"
                : (rooms != null ? rooms.GetName(home) : (home + 1) + "층");
        }

        private void Pick(int cellIndex)
        {
            if (cellIndex < 0 || cellIndex >= cells.Count)
                return;

            if (cells[cellIndex].character == null)
                return;

            picked = cells[cellIndex].character;
            RefreshCells();

            // 고른 사람을 방에 바로 세워 본다. 취소하고 나가면 흐름이 Refresh 한 번으로 되돌린다.
            OnPreviewed?.Invoke(roomIndex, picked);
        }

        private void RefreshCells()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (!cells[i].root.activeSelf)
                    continue;

                bool selected = picked != null && cells[i].character == picked;

                if (cells[i].background != null)
                    cells[i].background.color = selected ? cellSelectedColor : cellNormalColor;

                if (cells[i].selectedMark != null)
                    cells[i].selectedMark.SetActive(selected);
            }

            RefreshCurrent();
        }

        /// <summary>머리말의 이름. <b>프레임을 누르면 여기가 그 캐릭터 이름으로 바뀐다.</b></summary>
        private void RefreshCurrent()
        {
            if (currentText != null)
                currentText.text = picked != null ? picked.DisplayName : "비어 있음";

            if (confirmButton != null)
                confirmButton.interactable = picked != null;

        }

        private void RefreshPage()
        {
            int count = PageCount;

            if (pageText != null)
                pageText.text = count > 1 ? $"{page + 1}/{count}" : string.Empty;

            // 장이 하나뿐이면 화살표를 아예 감춘다 - 눌러도 아무 일 없는 버튼은 없느니만 못하다.
            if (prevPageButton != null)
            {
                prevPageButton.gameObject.SetActive(count > 1);
                prevPageButton.interactable = page > 0;
            }

            if (nextPageButton != null)
            {
                nextPageButton.gameObject.SetActive(count > 1);
                nextPageButton.interactable = page < count - 1;
            }
        }

        private void EnsureCells(int count)
        {
            if (cellTemplate == null || cellContent == null)
                return;

            while (cells.Count < count)
            {
                var go = Instantiate(cellTemplate, cellContent);
                go.name = $"RoomCell{cells.Count}";

                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(cellWidth, cellHeight);

                var badge = go.transform.Find("Piece/HomeBadge");
                var mark = go.transform.Find("SelectedMark");

                var cell = new Cell
                {
                    root = go,
                    rect = rect,
                    background = go.GetComponent<Image>(),
                    button = go.GetComponent<Button>(),
                    icon = go.GetComponentInChildren<PuzzlePieceIcon>(true),
                    nameText = FindText(go, "NameText"),
                    homeBadge = badge != null ? badge.gameObject : null,
                    homeText = badge != null ? badge.GetComponentInChildren<Text>(true) : null,
                    selectedMark = mark != null ? mark.gameObject : null
                };

                int index = cells.Count;
                if (cell.button != null)
                    cell.button.onClick.AddListener(() => Pick(index));

                cells.Add(cell);
            }
        }

}
}
