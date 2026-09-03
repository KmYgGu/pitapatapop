using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 사는 방을 확대했을 때 뜨는 <b>방 화면</b>(2026-08-28 사용자 기획).
    ///
    /// <code>
    ///   위 : 포만도 / 소지금 / 기분              ← 그 캐릭터의 지금 상태
    ///   가운데 : 방 (가리지 않는다)
    ///   아래 : 정보 · 선물하기 · 미니게임 · 부탁 · 방꾸미기 · 뒤로가기
    /// </code>
    ///
    /// <b>가운데를 덮지 않는다</b> - 확대한 방과 그 안의 캐릭터를 봐야 하는 화면이다.
    /// 위아래 띠만 있고, 카메라가 그만큼 방을 좁혀 담는다.
    ///
    /// <b>지금 살아 있는 건 상단 표시와 '정보'뿐이다</b>(사용자가 그 범위로 지시했다).
    /// 나머지 넷은 아파트 HUD 와 같은 방식으로 "준비 중입니다"만 띄운다 - 화면이 생기면
    /// 그 자리에서 넘기면 된다.
    /// </summary>
    public class RoomScreenPanel : MonoBehaviour
    {
        [Tooltip("껐다 켜는 뿌리. 이 컴포넌트는 <b>항상 켜져 있는</b> 바깥에 붙는다.")]
        [SerializeField] private GameObject root;

        [Header("머리말")]
        [SerializeField] private Text roomNameText;
        [SerializeField] private Text characterNameText;

        [Header("상단 - 상태")]
        [Tooltip("포만도 게이지의 채워지는 부분. 가로 비율(anchorMax.x)로 그린다.")]
        [SerializeField] private RectTransform satietyFill;
        [SerializeField] private Text satietyText;

        [Tooltip("기분 게이지.")]
        [SerializeField] private RectTransform moodFill;
        [SerializeField] private Text moodText;

        [Tooltip("그 캐릭터가 들고 있는 돈(CharacterWallet).")]
        [SerializeField] private Text moneyText;

        [Header("하단 - 버튼")]
        [SerializeField] private Button infoButton;
        [SerializeField] private Button giftButton;
        [SerializeField] private Button miniGameButton;
        [SerializeField] private Button favorButton;
        [SerializeField] private Button decorButton;
        [SerializeField] private Button backButton;

        [Header("안내")]
        [Tooltip("아직 없는 기능을 눌렀을 때 잠깐 뜨는 한 줄.")]
        [SerializeField] private Text noticeText;
        [SerializeField] private float noticeDuration = 1.4f;

        [Header("이어지는 화면")]
        [SerializeField] private RoomInfoPanel infoPanel;

        [Header("띠 - 카메라가 방을 담을 때 쓴다")]
        [Tooltip("위 띠. <b>실제로 화면을 덮는 만큼</b>을 재려고 참조한다 - " +
                 "레터박스 배율이 기기마다 달라서 비율을 숫자로 박을 수 없다.")]
        [SerializeField] private RectTransform topBar;

        [SerializeField] private RectTransform bottomBar;

        /// <summary>
        /// 위 띠가 화면 위쪽에서 덮는 비율. 띠를 안 물려뒀으면 음수 - 부르는 쪽이
        /// 인스펙터에 적힌 예전 값으로 물러선다.
        /// <b>판이 켜져 있고 레이아웃이 잡힌 뒤에</b> 물어야 한다.
        /// </summary>
        public float TopCoverFraction => UiScreenMetrics.CoverFractionFromTop(topBar);

        public float BottomCoverFraction => UiScreenMetrics.CoverFractionFromBottom(bottomBar);

        /// <summary>뒤로가기를 눌렀다.</summary>
        public event System.Action OnBackRequested;

        /// <summary>
        /// 방꾸미기를 눌렀다. <b>어느 방인지는 여기서 넘기지 않는다</b> -
        /// 이 화면을 연 쪽(<see cref="ApartmentRoomFlow"/>)이 이미 알고 있다.
        /// </summary>
        public event System.Action OnDecorRequested;

        public bool IsOpen => root != null && root.activeSelf;

        private int roomIndex = -1;
        private PanelType resident;
        private float noticeRemaining;

        // 이 방의 주인이 은행 감옥에 있는지. 그렇다면 이 화면은 안내 한 줄만 한다.
        private bool jailed;

        private void Awake()
        {
            if (infoButton != null)
                infoButton.onClick.AddListener(OpenInfo);

            if (backButton != null)
                backButton.onClick.AddListener(() => OnBackRequested?.Invoke());

            BindNotReady(giftButton, "선물하기");

            if (miniGameButton != null)
                miniGameButton.onClick.AddListener(OpenMiniGame);
            BindNotReady(favorButton, "부탁");
            if (decorButton != null)
                decorButton.onClick.AddListener(() => OnDecorRequested?.Invoke());

            if (root != null)
                root.SetActive(false);

            ShowNotice(string.Empty);
        }

        public void Open(int index, string roomName)
        {
            roomIndex = index;
            resident = ApartmentResidents.Get(index);

            // ⭐ 담보로 잡혀 간 방(2026-09-03 사용자 지시). 살긴 사는데 지금은 없는 방이라,
            // 정보도 버튼도 아무 뜻이 없다 - 왜 비었는지만 알려준다.
            jailed = JojoPuzzle.App.BankLoan.IsLocked(resident);

            if (roomNameText != null)
                roomNameText.text = roomName;

            if (root != null)
                root.SetActive(true);

            ShowNotice(string.Empty);
            Refresh();
        }

        public void Close()
        {
            infoPanel?.Close();

            if (root != null)
                root.SetActive(false);

            roomIndex = -1;
            resident = null;
        }

        /// <summary>상단 표시를 다시 그린다. 값이 바뀌는 기능이 생기면 그쪽에서 부르면 된다.</summary>
        public void Refresh()
        {
            // ⭐ 주인이 은행 감옥에 있으면 이 화면은 <b>왜 비었는지만</b> 말한다.
            // 포만도·기분·소지금을 그대로 보여주면 없는 사람의 상태를 읽는 꼴이 된다.
            LockForJail(jailed);

            if (jailed)
            {
                if (characterNameText != null)
                    characterNameText.text = resident != null ? resident.DisplayName : string.Empty;

                ShowNotice("이 방의 주인은 감옥에 있습니다..");
                return;
            }

            if (characterNameText != null)
                characterNameText.text = resident != null ? resident.DisplayName : "비어 있음";

            SetGauge(satietyFill, ResidentState.GetSatiety(resident));
            SetText(satietyText, $"{ResidentState.GetSatiety(resident)}/{ResidentState.Max}");

            SetGauge(moodFill, ResidentState.GetMood(resident));
            SetText(moodText, ResidentState.DescribeMood(resident));

            SetText(moneyText, CharacterWallet.Get(resident).ToString("N0"));
        }

        /// <summary>
        /// 감옥에 간 방에서는 <b>할 수 있는 게 없다</b> - 버튼을 눌러도 아무 일이 없느니
        /// 아예 못 누르게 한다. '뒤로가기'만 남긴다.
        /// </summary>
        private void LockForJail(bool locked)
        {
            SetUsable(infoButton, !locked);
            SetUsable(giftButton, !locked);
            SetUsable(miniGameButton, !locked);
            SetUsable(favorButton, !locked);
            SetUsable(decorButton, !locked);

            if (locked)
            {
                SetGauge(satietyFill, 0);
                SetGauge(moodFill, 0);
                SetText(satietyText, string.Empty);
                SetText(moodText, string.Empty);
                SetText(moneyText, string.Empty);
            }
        }

        private static void SetUsable(Button button, bool usable)
        {
            if (button != null)
                button.interactable = usable;
        }

        /// <summary>
        /// 미니게임(도박) 씨으로 간다. 누구와 하는지와 <b>돌아올 방</b>을 적어두고 넘긴다.
        /// 빈 방에서는 못 한다 - 상대가 있어야 성립하는 놀이다.
        /// </summary>
        private void OpenMiniGame()
        {
            if (resident == null)
            {
                ShowNotice("방이 비어 있습니다");
                return;
            }

            MiniGameEntry.Set(resident, roomIndex);
            AppScenes.GoToMiniGame();
        }

        private void OpenInfo()
        {
            if (infoPanel == null)
            {
                ShowNotice("정보 - 준비 중입니다");
                return;
            }

            infoPanel.Open(roomIndex, resident);
        }

        /// <summary>
        /// 게이지를 <b>가로 비율</b>로 그린다. 폭을 숫자로 넣지 않는 이유는 화면 폭이 기기마다
        /// 달라서다([[feedback-mobile-resolution]]) - 앵커로 그리면 어디서나 같은 비율이 된다.
        /// </summary>
        private static void SetGauge(RectTransform fill, int value)
        {
            if (fill == null)
                return;

            float ratio = Mathf.Clamp01(value / (float)ResidentState.Max);

            var max = fill.anchorMax;
            max.x = ratio;
            fill.anchorMax = max;

            // 앵커를 바꾸면 유니티가 화면 위치를 지키려고 여백을 조정한다 - 0으로 눌러야
            // 실제로 그 비율이 된다(EventBannerRotator 에서 겪은 것과 같다).
            fill.offsetMin = new Vector2(0f, fill.offsetMin.y);
            fill.offsetMax = new Vector2(0f, fill.offsetMax.y);
        }

private void BindNotReady(Button button, string name)
        {
            if (button == null)
                return;

            button.onClick.AddListener(() => ShowNotice($"{name} - 준비 중입니다"));
        }

        private void ShowNotice(string message)
        {
            if (noticeText != null)
                noticeText.text = message;

            noticeRemaining = string.IsNullOrEmpty(message) ? 0f : noticeDuration;
        }

        private void Update()
        {
            if (noticeRemaining <= 0f)
                return;

            noticeRemaining -= Time.deltaTime;
            if (noticeRemaining <= 0f)
                ShowNotice(string.Empty);
        }
    }
}
