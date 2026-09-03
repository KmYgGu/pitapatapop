using System.Collections.Generic;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JojoPuzzle.Formation
{
    /// <summary>
    /// <b>스티커북</b>(2026-09-03 사용자 기획). 아파트의 '편성' 버튼이 여기로 온다 -
    /// 편성 화면이 바로 뜨지 않고, <b>스티커북을 펼친 채</b> 그 위에 편성된 캐릭터가 서 있다.
    ///
    /// <code>
    ///   평소   : 캐릭터를 누르면 편성 화면 · 여백을 누르면 붙이기 화면
    ///   편집 중 : 꾹 눌러 집고 <b>끌어서</b> 옮긴다. 캐릭터도 스티커처럼 옮긴다
    ///           → '확정'을 눌러야 전투에 반영된다
    /// </code>
    ///
    /// ⭐⭐ <b>캐릭터도 스티커다</b>(2026-09-03 사용자 지시). 자리를 같은 초안에 담고
    /// (리더 <see cref="PlayerStickers.LeaderSlot"/> · 파트너 <see cref="PlayerStickers.PartnerSlot"/>),
    /// 같은 부품(<see cref="BookPlaceable"/>)으로 끈다 - 그래야 스티커를 캐릭터 위에 붙일 수 있고
    /// 캐릭터도 원하는 데 놓을 수 있다.
    ///
    /// ⭐ <b>편집 중에는 편성 화면으로 안 넘어간다.</b> 캐릭터 위에 스티커를 붙이려면
    /// 캐릭터를 눌러도 화면이 바뀌지 않아야 한다.
    ///
    /// ⭐ <b>놓는다고 확정되지 않는다.</b> 붙이고 옮기는 건 전부 초안에서 일어난다.
    /// </summary>
    public class StickerBookPanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;

        [Header("책")]
        [Tooltip("책의 여백. 편집 중이 아닐 때 누르면 붙이기 화면이 열린다.")]
        [SerializeField] private Button pageButton;

        [Tooltip("자리를 재는 기준이 되는 책의 사각형.")]
        [SerializeField] private RectTransform pageRect;

        [SerializeField] private Text costText;
        [SerializeField] private Image costFill;
        [SerializeField] private Text hintText;

        [Header("여러 권")]
        [Tooltip("권 이름. 누르면 고칠 수 있다(편집 중에는 안 눌린다).")]
        [SerializeField] private Button nameButton;

        [SerializeField] private Text nameText;

        [Tooltip("몇 번째 권인지 보여주는 점들. 왼쪽부터 1권.")]
        [SerializeField] private Image[] pageDots = new Image[0];

        [SerializeField] private Color dotOnColor = new Color(0.42f, 0.36f, 0.64f, 1f);
        [SerializeField] private Color dotOffColor = new Color(0.72f, 0.68f, 0.60f, 1f);

        [Tooltip("이만큼(화면 폭 대비) 밀어야 옆 권으로 넘어간다. " +
                 "⭐ 넉넉히 눅였다 - 꾹 누른 뒤에 밀어도 넘어가야 한다(2026-09-03 사용자 지시).")]
        [Range(0.02f, 0.6f)]
        [SerializeField] private float swipeFraction = 0.07f;

        [Header("초기화")]
        [Tooltip("이 권의 스티커를 전부 뗀다. 캐릭터 자리는 남는다.")]
        [SerializeField] private Button resetButton;

        [Header("편성된 캐릭터")]
        [Tooltip("캐릭터 한 명이 차지하는 칸 크기(캔버스 단위). " +
                 "⚠⚠ <b>0이면 스파인이 안 보인다</b> - 자리를 점 앵커로 접는 순간 " +
                 "늘어나 있던 크기가 사라지므로, 여기 적힌 크기를 직접 넣어 줘야 한다(2026-09-03).")]
        [SerializeField] private Vector2 characterSlotSize = new Vector2(105f, 139f);

        [Tooltip("리더가 서는 자리. ⚠ 테두리가 보이면 안 된다 - 이 Image 는 투명해야 한다.")]
        [SerializeField] private RectTransform leaderSlot;

        [Tooltip("캐릭터 이름을 보여줄지. ⭐ 기본은 <b>안 보인다</b>(2026-09-03 사용자 지시) - " +
                 "꾸미는 화면이라 글자가 적을수록 낫다.")]
        [SerializeField] private bool showCharacterNames;

        [SerializeField] private JojoPuzzle.UI.SpineCharacterView leaderSpine;
        [SerializeField] private Text leaderNameText;

        [SerializeField] private RectTransform partnerSlot;
        [SerializeField] private JojoPuzzle.UI.SpineCharacterView partnerSpine;
        [SerializeField] private Text partnerNameText;

        [Header("붙인 스티커")]
        [SerializeField] private RectTransform stickerTemplate;
        [SerializeField] private RectTransform stickerRoot;

        [Header("확정")]
        [SerializeField] private Button confirmButton;

        [Tooltip("⭐ 캐릭터 편성 화면으로. 예전엔 '되돌리기' 자리였다 - " +
                 "편성으로 들어갈 길이 없다는 지적을 받아 바꿨다(2026-09-03).")]
        [SerializeField] private Button formationButton;

        [Header("나가기")]
        [SerializeField] private Button backButton;

        [SerializeField] private StickerCatalog catalog;

        public event System.Action OnFormationRequested;
        public event System.Action OnAttachRequested;
        public event System.Action OnBackRequested;

        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>
        /// 지금 스티커를 만지는 중인지. 이때는 편성 화면으로 안 넘어가고 권도 안 넘긴다.
        ///
        /// ⭐ 기준은 <b>사용자가 실제로 만졌는지</b>다 - 집고 있거나, 이 권에서 무언가를 옮겼거나.
        ///
        /// ⚠ <b>"확정 안 한 손질이 있는지"로 재면 안 된다</b>(2026-09-03 사용자 지적).
        /// 책을 여는 것만으로 캐릭터 자리가 초안에 더해져, <b>손도 안 댄 권</b>이 잠긴 채로
        /// "강제로 꾸미고 나서야 벗어날 수 있는" 화면이 됐다.
        /// </summary>
        /// <summary>
        /// <b>지금 스티커를 손에 들고 있는가.</b> 이때만 좌우 넘기기·편성·이름 고치기를 막는다 -
        /// 놓다 말고 화면이 바뀌면 어디에 무엇을 놓았는지 잃어버린다.
        ///
        /// ⚠ 예전엔 "확정 안 한 손질이 있는가(touched)"도 함께 봤는데, 확정이 자동이 되면서
        /// 뜻이 없어졌다. 게다가 <b>놓은 뒤 activeId 를 아무도 안 비워서</b> 이게 계속 참으로
        /// 남았고, 그래서 좌우로 밀어도 권이 안 넘어갔다(2026-09-03 사용자 신고).
        /// </summary>
        public bool IsEditing => activeId != 0;

        // 이 권에서 사용자가 무언가를 옮겼는지. 확정하거나 권을 넘기면 다시 깨끗해진다.

        private int activeId;

        private readonly List<RectTransform> cells = new List<RectTransform>();

        private void Awake()
        {
            // ⭐ <b>책 여백을 눌러도 목록이 안 열린다</b>(2026-09-03 사용자 지시).
            // 책 위에는 스티커와 캐릭터가 겹쳐 있어서, 여백을 노린 손가락이 자꾸
            // 그 위의 것을 집었다. 목록은 아래의 '목록' 버튼으로만 연다.

            // ⭐ 이 버튼은 이제 <b>목록</b>을 연다(2026-09-03). 확정은 붙일 때마다 저절로 된다 -
            // 따로 누르게 하면 "확정을 안 눌러서 안 저장됐다"가 계속 생긴다.
            if (confirmButton != null)
                confirmButton.onClick.AddListener(OpenList);

            if (formationButton != null)
                formationButton.onClick.AddListener(OpenFormation);

            if (resetButton != null)
                resetButton.onClick.AddListener(ResetBook);

            if (nameButton != null)
                nameButton.onClick.AddListener(BeginRename);

            if (backButton != null)
                backButton.onClick.AddListener(HandleBack);

            if (stickerTemplate != null)
                stickerTemplate.gameObject.SetActive(false);

            BindSlot(leaderSlot, PlayerStickers.LeaderSlot);
            BindSlot(partnerSlot, PlayerStickers.PartnerSlot);

            // 책을 밀면 다른 권으로. 붙이는 판(Page)에 얹는다.
            if (pageRect != null)
            {
                var swipe = pageRect.GetComponent<BookSwipe>();
                if (swipe == null)
                    swipe = pageRect.gameObject.AddComponent<BookSwipe>();

                swipe.onSwiped = SwipeBook;
            }

            root?.SetActive(false);
        }

        private void OnEnable() => PlayerStickers.OnChanged += Refresh;

        private void OnDisable() => PlayerStickers.OnChanged -= Refresh;

        public void Open()
        {
            if (root == null)
                return;

            // 아직 스티커를 얻는 길(상점 칸)이 없어서 전부 가진 것으로 채운다.
            PlayerStickers.GrantAllForTesting(catalog);

            // 확정 안 하고 나갔던 손질은 들고 오지 않는다 - 보이는 것과 전투가 쓰는 것이 늘 같아야 한다.
            PlayerStickers.RevertAll();

            // ⚠ <b>모든 권</b>의 캐릭터 자리를 챙긴다. 지금 권만 챙기면 다른 권으로 넘어갔을 때
            // 자리가 없어 둘 다 가운데(0.5, 0.5)에 겹쳐 나온다(2026-09-03 사용자 지적).
            PlayerStickers.EnsureCharacterSlotsAll();
            PlayerStickers.ApplyParty();

            activeId = 0;

            root.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            activeId = 0;
            root?.SetActive(false);
        }

        /// <summary>
        /// 편성 화면에서 돌아왔다 - 방금 고른 편성을 <b>이 권에</b> 담고 연다
        /// (2026-09-03 사용자 지시: "스티커북마다 캐릭터도 각자 저장").
        /// </summary>
        public void OpenAfterFormation()
        {
            PlayerStickers.StoreParty();
            Open();
        }

        /// <summary>
        /// 목록에서 고른 스티커를 <b>책 가운데에 놓고 집어 든 상태</b>로 시작한다 -
        /// 보이지 않으면 어디로 끌지 알 수가 없다(2026-09-03 사용자 지적).
        /// </summary>
        public void BeginPlacing(int stickerId)
        {
            activeId = stickerId;

            // ⭐ <b>이미 붙어 있어도 한 장 더 붙는다</b>(2026-09-03 사용자 확정: 중복 착용).
            // 예전엔 "안 붙어 있을 때만" 붙여서 같은 스티커를 둘 붙일 수 없었다.
            if (PlayerStickers.CanAttachMore(stickerId)
                && PlayerStickers.TryAttach(catalog, stickerId, PlayerProfile.Level,
                                            new Vector2(0.5f, 0.42f), out int placedKey))
            {
                // 방금 붙인 <b>그 장</b>을 집어 든 상태로 둔다 - 안 그러면 먼저 붙어 있던
                // 같은 스티커가 손가락을 따라온다.
                activeKey = placedKey;
            }

            AutoConfirm();
            Refresh();
        }

        // ---------------------------------------------------------------- 집고 끌기

        private void BindSlot(RectTransform slot, int id)
        {
            if (slot == null)
                return;

            var hook = slot.GetComponent<BookPlaceable>();
            if (hook == null)
                hook = slot.gameObject.AddComponent<BookPlaceable>();

            hook.id = id;

            // ⚠ <b>번호를 안 채우면 못 움직인다</b>(2026-09-03 사용자 신고). 옮기기는 배치의
            // 고유 번호로 하는데, 캐릭터 쪽만 이걸 빠뜨려서 끌어도 MoveByKey(0) 이 됐다.
            // 캐릭터는 id 가 하나뿐이라 id 로 번호를 찾아올 수 있다.
            hook.key = PlayerStickers.KeyOf(id);

            hook.onTapped = HandleTapped;
            hook.onHeld = HandleHeld;
            hook.onDragged = HandleDragged;
            hook.onDragEnd = HandleDragEnd;
        }

        /// <summary>그냥 눌렀다. <b>편집 중이면 아무 일도 없다</b> - 캐릭터 위에 스티커를 붙여야 하니까.</summary>
        private void HandleTapped(BookPlaceable hook)
        {
            int id = hook.id;

            // ⭐ <b>빠르게 두 번 누르면 뗀다</b>(2026-09-03 사용자 요청) - 붙인 걸 지우는 길이
            // 없어서 번거로웠다. 캐릭터 자리는 뗄 수 없다(리더·파트너는 늘 있어야 한다).
            //
            // ⚠ <b>편집 중인지를 안 본다.</b> 아래 분기는 편집 중이면 그냥 돌아가는데,
            // 지우는 건 편집 중에 하고 싶은 일이라 그 앞에 둔다.
            if (id != PlayerStickers.LeaderSlot && id != PlayerStickers.PartnerSlot)
            {
                bool again = hook.key == lastTapKey
                             && Time.unscaledTime - lastTapTime <= doubleTapSeconds;

                lastTapKey = hook.key;
                lastTapTime = Time.unscaledTime;

                if (again && PlayerStickers.DetachByKey(hook.key))
                {

                    // 집어 든 게 방금 뗀 그 장이었으면 손을 놓는다 - 안 그러면 없는 걸 끌게 된다.
                    if (activeKey == hook.key)
                    {
                        activeId = 0;
                        activeKey = 0;
                    }

                    lastTapKey = 0;
                    AutoConfirm();
                    Refresh();
                    return;
                }
            }

            // 들고 있던 것을 그냥 눌렀다 - <b>여기 놓는다</b>. 끌지 않고 목록에서 고르기만 한
            // 경우에도 손을 놓을 길이 있어야 한다(없으면 '편집 중' 에 갇힌다).
            if (activeKey != 0 && hook.key == activeKey)
            {
                activeId = 0;
                activeKey = 0;
                Refresh();
                return;
            }

            // ⭐ <b>캐릭터는 눌러도 아무 일이 없다</b>(2026-09-03 사용자 지시) -
            // 꾹 눌러 끌어 옮기는 용도뿐이다. 편성은 아래 '캐릭터 편성' 버튼으로 간다.
            // 예전엔 누르면 편성 화면이 열려서, 옮기려다 화면이 바뀌는 일이 잦았다.
        }

        /// <summary>꾹 눌러 집었다 - 이제 끌어서 옮길 수 있다.</summary>
        private void HandleHeld(BookPlaceable hook)
        {
            activeId = hook.id;
            activeKey = hook.key;
            Refresh();
        }

        // 지금 집어 든 배치의 고유 번호. 같은 스티커가 여러 장이면 이게 어느 장인지를 가린다.
        private int activeKey;

        [Tooltip("두 번 누른 것으로 칠 시간(초). 이 안에 같은 스티커를 또 누르면 뗀다.")]
        [SerializeField] private float doubleTapSeconds = 0.35f;

        // 직전에 누른 배치와 그 시각. 두 번 누름을 가리는 데만 쓴다.
        private int lastTapKey;
        private float lastTapTime;

        private void HandleDragged(BookPlaceable hook, PointerEventData eventData)
        {
            PlayerStickers.MoveByKey(hook.key, SpotOf(eventData));
            Refresh();
        }

        /// <summary>
        /// 끌기가 끝났다 - 확정하고 <b>손을 놓는다</b>.
        /// 안 놓으면 계속 '편집 중' 이라 권을 넘길 수도, 편성으로 갈 수도 없다.
        /// </summary>
        private void HandleDragEnd(BookPlaceable hook)
        {
            AutoConfirm();
            activeId = 0;
            activeKey = 0;
            Refresh();
        }

        /// <summary>손가락이 닿은 자리를 <b>책 안에서의 0~1 비율</b>로.</summary>
        private Vector2 SpotOf(PointerEventData eventData)
        {
            var rect = pageRect;
            if (rect == null)
                return new Vector2(0.5f, 0.5f);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect, eventData.position, eventData.pressEventCamera, out Vector2 local))
                return new Vector2(0.5f, 0.5f);

            Rect r = rect.rect;
            return new Vector2(Mathf.Clamp01((local.x - r.xMin) / Mathf.Max(1f, r.width)),
                               Mathf.Clamp01((local.y - r.yMin) / Mathf.Max(1f, r.height)));
        }

        /// <summary>
        /// 여백을 눌렀다 - <b>언제나 붙이기 화면을 연다</b>(2026-09-03 사용자 지시:
        /// "편집 중에 여백을 누르면 계속해서 다음 스티커를 붙일 수 있게"). 한 장 붙일 때마다
        /// 편집을 끝내고 다시 들어오게 하면 여러 장을 붙이는 데 손이 너무 많이 간다.
        /// 들고 있던 것은 내려놓는다 - 다음 장을 고르러 가는 길이다.
        /// </summary>
        /// <summary>'목록' 버튼. 들고 있던 것은 내려놓고 목록을 연다.</summary>
        private void OpenList()
        {
            activeId = 0;
            activeKey = 0;
            OnAttachRequested?.Invoke();
        }

        // ---------------------------------------------------------------- 확정

        /// <summary>
        /// <b>손질을 그 자리에서 확정한다</b>(2026-09-03 사용자 지시: 붙일 때마다 자동 확정).
        ///
        /// 붙이기·옮기기·떼기가 끝날 때마다 부른다. 확정을 따로 누르게 하면 "안 눌러서
        /// 안 저장됐다"가 계속 생기고, <b>편집 중</b>으로 잠긴 채라 권을 넘기지도 못한다.
        /// </summary>
        private void AutoConfirm()
        {
            PlayerStickers.Commit();
        }

        /// <summary>편성 화면으로. <b>편집 중에는 안 간다</b> - 스티커를 놓다 말고 화면이 바뀌면 안 된다.</summary>
        private void OpenFormation()
        {
            if (IsEditing)
                return;

            OnFormationRequested?.Invoke();
        }

        /// <summary>
        /// ⭐ 이 권을 <b>바로</b> 비운다(2026-09-03 사용자 지시). 확정을 또 누르게 하면
        /// "지웠는데 안 지워졌다"로 읽힌다 - 초기화는 그 자체가 결정이다.
        /// </summary>
        private void ResetBook()
        {
            PlayerStickers.ClearBook();
            PlayerStickers.Commit();

            activeId = 0;
            Refresh();
        }

        // ---------------------------------------------------------------- 권 넘기기

        /// <summary>
        /// ⭐ 좌우로 밀어 <b>다른 스티커북</b>으로(2026-09-03 사용자 기획).
        /// 스티커 조합에 따라 판이 아주 달라지므로 미리 짜둔 것을 골라 쓴다.
        /// ⚠ 편집 중에는 안 넘어간다 - 스티커를 끄는 손짓과 겹친다.
        /// </summary>
        public void SwipeBook(float dx)
        {
            // ⭐ <b>편집 중에는 안 넘긴다</b>(2026-09-03 사용자 지시: "충분히 헷갈려").
            // 스티커를 놓다 말고 책장이 넘어가면 어디에 무엇을 놓았는지 잃어버린다.
            if (IsEditing)
            {
                return;
            }

            float threshold = Screen.width * swipeFraction;
            if (Mathf.Abs(dx) < threshold)
                return;

            // 왼쪽으로 밀면 다음 권 - 갤러리와 같은 손맛이다. 끝에서는 반대쪽 끝으로 돈다.
            PlayerStickers.ActiveBook += dx < 0f ? 1 : -1;

            // 넘어간 권은 아직 안 만진 권이다 - 곧바로 또 넘길 수 있어야 한다.
            activeId = 0;
            Refresh();
        }

        // ---------------------------------------------------------------- 이름 고치기

        private TouchScreenKeyboard keyboard;

        /// <summary>
        /// 이름을 고친다. <b>모바일 자판을 부른다</b>(사용자 지시) -
        /// 자판이 없는 데(에디터)서는 그냥 눌러 넣는다.
        /// </summary>
        private void BeginRename()
        {
            if (IsEditing)
                return;

            string now = PlayerStickers.NameOf(PlayerStickers.ActiveBook);

            if (TouchScreenKeyboard.isSupported)
            {
                keyboard = TouchScreenKeyboard.Open(now, TouchScreenKeyboardType.Default,
                                                    false, false, false, false, now, 12);
                return;
            }

            typing = true;
            typed = now;
            Refresh();
        }

        // 자판이 없는 데서 쓰는 임시 입력. 에디터에서 확인하려고 둔다.
        private bool typing;
        private string typed = string.Empty;

        private void Update()
        {
            if (keyboard != null)
            {
                if (keyboard.status == TouchScreenKeyboard.Status.Done)
                    PlayerStickers.Rename(PlayerStickers.ActiveBook, keyboard.text);

                if (keyboard.status != TouchScreenKeyboard.Status.Visible)
                {
                    keyboard = null;
                    Refresh();
                }

                return;
            }

            if (!typing)
                return;

            foreach (char c in Input.inputString)
            {
                if (c == '\b')
                {
                    if (typed.Length > 0)
                        typed = typed.Substring(0, typed.Length - 1);
                }
                else if (c == '\n' || c == '\r')
                {
                    PlayerStickers.Rename(PlayerStickers.ActiveBook, typed);
                    typing = false;
                    Refresh();
                    return;
                }
                else if (typed.Length < 12)
                {
                    typed += c;
                }
            }

            if (nameText != null)
                nameText.text = typed + "_";
        }

        /// <summary>
        /// 뒤로가기는 <b>한 칸씩</b> 물러난다: 편집 그만두기 → 확정 안 한 손질 알림 → 나가기.
        /// ⚠ 확정 안 한 손질을 말없이 버리지 않는다.
        /// </summary>
        private void HandleBack()
        {
            // ⭐ <b>그냥 나간다</b>(2026-09-03 사용자 지시). 예전엔 "확정하지 않았습니다"로
            // 한 번 막았는데, 붙는 순간 이미 확정이라 <b>거짓말이었다</b>.
            // Revert() 도 뺀다 - 되돌릴 게 없는데 되돌리면 방금 붙인 것을 날린다.
            activeId = 0;
            activeKey = 0;
            OnBackRequested?.Invoke();
        }

        // ---------------------------------------------------------------- 그리기

        public void Refresh()
        {
            RefreshCost();
            RefreshParty();
            RefreshStickers();

            // ⭐ 이 버튼은 이제 <b>목록</b>을 여는 버튼이라 늘 눌린다(2026-09-03).
            // 예전엔 '확정' 이라 저장할 게 있을 때만 켰는데, 확정이 자동이 되면서
            // <b>영영 꺼진 채</b>로 남았다 - 눌러도 아무 일이 없던 원인이다.
            if (confirmButton != null)
                confirmButton.interactable = true;

            // 편집 중에는 편성으로 못 간다 - 스티커를 놓다 말고 화면이 바뀌면 안 된다.
            if (formationButton != null)
                formationButton.interactable = !IsEditing;

            if (nameButton != null)
                nameButton.interactable = !IsEditing;

            if (nameText != null && !typing)
                nameText.text = PlayerStickers.NameOf(PlayerStickers.ActiveBook);

            for (int i = 0; i < pageDots.Length; i++)
            {
                if (pageDots[i] != null)
                    pageDots[i].color = i == PlayerStickers.ActiveBook ? dotOnColor : dotOffColor;
            }

            // 안내는 <b>지금 할 수 있는 일</b>을 말한다. 빈 곳을 누르는 길은 없어졌고
            // 확정도 저절로 되므로, 남은 건 '목록에서 고르기' 와 '두 번 눌러 지우기' 다.
            // ⭐ 책 한가운데의 안내 글씨는 <b>지웠다</b>(2026-09-03 사용자 지시) -
            // 책 위에 늘 떠 있어서 스티커와 겹쳐 보였다. 조작은 버튼 이름으로 읽힌다.

        }

        private void RefreshCost()
        {
            int max = PlayerStickers.MaxCost(PlayerProfile.Level);
            int used = PlayerStickers.UsedCost(catalog);

            if (costText != null)
                costText.text = $"{used} / {max}";

            if (costFill != null)
                costFill.fillAmount = max > 0 ? Mathf.Clamp01(used / (float)max) : 0f;
        }

        private void RefreshParty()
        {
            Place(leaderSlot, PlayerStickers.LeaderSlot);
            Place(partnerSlot, PlayerStickers.PartnerSlot);

            Bind(leaderSpine, leaderNameText, PartySelection.Leader, "리더 없음");
            Bind(partnerSpine, partnerNameText, PartySelection.Partner, "파트너 없음");

            if (leaderNameText != null)
                leaderNameText.gameObject.SetActive(showCharacterNames);

            if (partnerNameText != null)
                partnerNameText.gameObject.SetActive(showCharacterNames);
        }

        /// <summary>
        /// 캐릭터도 스티커처럼 <b>초안에 적힌 자리</b>로 옮긴다.
        ///
        /// ⚠⚠ 앵커를 점으로 접으면 <b>크기를 sizeDelta 가 정한다</b>. 늘어난 앵커로 만들어 둔
        /// 칸은 sizeDelta 가 0이라, 접는 순간 0x0 이 되어 스파인이 안 보인다
        /// (2026-09-03 "크기 자동 측정 실패 (칸 크기 (0.00, 0.00))"). 크기를 직접 넣어 준다.
        /// </summary>
        private void Place(RectTransform slot, int id)
        {
            if (slot == null)
                return;

            Vector2 spot = PlayerStickers.PositionOf(id);
            slot.anchorMin = slot.anchorMax = spot;
            slot.anchoredPosition = Vector2.zero;
            slot.sizeDelta = characterSlotSize;
        }

        private static void Bind(JojoPuzzle.UI.SpineCharacterView view, Text nameText,
                                 PanelType character, string empty)
        {
            var skeleton = character != null && character.speech != null
                ? character.speech.spine : null;

            if (view != null)
            {
                if (skeleton != null)
                    view.Show(skeleton);
                else
                    view.Clear();
            }

            if (nameText != null)
                nameText.text = character != null ? character.DisplayName : empty;
        }

        private void RefreshStickers()
        {
            if (stickerTemplate == null || stickerRoot == null || catalog == null)
                return;

            // 그리는 건 초안이다 - 확정 전에도 눈에 보여야 고민을 할 수 있다.
            var draft = PlayerStickers.Draft;

            int shown = 0;
            for (int i = 0; i < draft.Count; i++)
            {
                // 캐릭터 자리는 스티커 칸으로 그리지 않는다 - 자기 자리가 따로 있다.
                if (draft[i].id < 0)
                    continue;

                while (cells.Count <= shown)
                {
                    var made = Instantiate(stickerTemplate, stickerRoot);
                    made.name = "Sticker" + cells.Count;
                    cells.Add(made);
                }

                FillCell(cells[shown], draft[i]);
                shown++;
            }

            for (int i = shown; i < cells.Count; i++)
                cells[i].gameObject.SetActive(false);
        }

        private void FillCell(RectTransform cell, PlayerStickers.Placed placed)
        {
            cell.gameObject.SetActive(true);
            cell.anchorMin = cell.anchorMax = placed.position;
            cell.anchoredPosition = Vector2.zero;

            var sticker = catalog.Find(placed.id);
            var image = cell.GetComponent<Image>();

            if (image != null)
            {
                if (sticker != null && sticker.sprite != null)
                    image.sprite = sticker.sprite;

                // 집고 있는 것은 또렷하게 - 지금 무엇을 옮기는지 보여야 한다.
                image.color = placed.id == activeId
                    ? Color.white : new Color(1f, 1f, 1f, 0.94f);
            }

            var hook = cell.GetComponent<BookPlaceable>();
            if (hook == null)
                hook = cell.gameObject.AddComponent<BookPlaceable>();

            hook.id = placed.id;
            hook.key = placed.key;
            hook.onTapped = HandleTapped;
            hook.onHeld = HandleHeld;
            hook.onDragged = HandleDragged;
            hook.onDragEnd = HandleDragEnd;

            // 방금 집어 든 것은 꾹 누르지 않아도 바로 끌린다.
            hook.alreadyHeld = placed.id == activeId;
        }
    }
}
