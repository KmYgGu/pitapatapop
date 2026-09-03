using System.Collections.Generic;
using JojoPuzzle.App;
using UnityEngine;
using UnityEngine.UI;
using static JojoPuzzle.UI.UiBind;

using JojoPuzzle.Core;
namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// <b>상점</b> 화면(2026-09-02 사용자 기획). 뽑기와는 <b>완전히 다른 화면</b>이다 -
    /// 뽑기는 운으로 캐릭터를 얻는 곳이고 여기는 값을 알고 사는 곳이라, 한 화면에 두면
    /// "얼마인지 아는 것"과 "운에 맡기는 것"이 섞인다.
    ///
    /// 칸 넷: 스티커 · 은행 · 인테리어 · 선물(<see cref="ShopTab"/>).
    /// 아직 규칙이 안 정해진 칸은 <b>목록이 비어 있고 안내만</b> 뜬다 - 칸 자체를 숨기면
    /// 무엇이 생길 예정인지가 안 보인다.
    ///
    /// ⚠ <b>컴포넌트는 늘 켜져 있는 바깥 껍데기에 붙는다.</b> 껐다 켜는 건 자식 <c>Root</c> 다 -
    /// 꺼진 오브젝트에 붙으면 Awake 가 안 돌아 버튼 연결이 안 된다(우편함과 같은 함정).
    /// </summary>
    public class ShopPanel : MonoBehaviour
    {
        [Header("껐다 켜는 곳")]
        [SerializeField] private GameObject root;

        [Header("칸")]
        [Tooltip("ShopTab 순서대로. 스티커·은행·인테리어·선물.")]
        [SerializeField] private Button[] tabButtons = new Button[0];

        [Tooltip("고른 칸의 버튼에 입힐 색.")]
        [SerializeField] private Color tabOnColor = new Color(0.30f, 0.26f, 0.42f, 1f);

        [SerializeField] private Color tabOffColor = new Color(0.16f, 0.15f, 0.20f, 1f);

        [Header("목록")]
        [Tooltip("줄의 본. 꺼진 채로 두면 이걸 복제해 쌓는다.")]
        [SerializeField] private RectTransform rowTemplate;

        [SerializeField] private RectTransform listContent;

        [Tooltip("줄 하나의 높이와 사이(캔버스 단위).")]
        [SerializeField] private float rowHeight = 58f;
        [SerializeField] private float rowGap = 6f;

        [Header("글자")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text noticeText;
        [SerializeField] private Text goldText;
        [SerializeField] private Text gemText;

        [Header("닫기")]
        [SerializeField] private Button closeButton;

        [Header("물건")]
        [SerializeField] private ShopCatalog catalog;

        [Header("은행")]
        [Tooltip("은행 칸은 목록이 아니라 <b>자기 화면</b>을 쓴다 - 맡기고 찾는 일이라 " +
                 "물건을 고르는 것과 모양이 아주 다르다.")]
        [SerializeField] private BankView bank;

        [Tooltip("물건 목록 쪽(은행 칸에서는 통째로 비킨다).")]
        [SerializeField] private GameObject goodsRoot;

        [Tooltip("스티커 뽑기가 고를 목록. 비워두면 뽑기 물건이 안내만 띄운다.")]
        [SerializeField] private StickerCatalog stickerCatalog;

        [Tooltip("뽑은 스티커를 보여 주는 연출. 비워두면 안내 문구로만 알린다.")]
        [SerializeField] private StickerDrawReveal reveal;

        /// <summary>닫혔다. 아파트 HUD 가 이걸 보고 되돌린다.</summary>
        public event System.Action OnClosed;

        public bool IsOpen => root != null && root.activeSelf;

        private ShopTab tab = ShopTab.Sticker;

        private readonly List<ShopGood> shown = new List<ShopGood>();
        private readonly List<RectTransform> rows = new List<RectTransform>();

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null)
                    continue;

                var which = (ShopTab)i;   // 람다가 늦게 읽어 마지막 칸만 잡히는 걸 막는다
                tabButtons[i].onClick.AddListener(() => SelectTab(which));
            }

            if (rowTemplate != null)
                rowTemplate.gameObject.SetActive(false);

            root?.SetActive(false);
        }

        private void OnEnable()
        {
            PlayerShop.OnChanged += Refresh;
            PlayerProfile.OnCurrencyChanged += RefreshMoney;
        }

        private void OnDisable()
        {
            PlayerShop.OnChanged -= Refresh;
            PlayerProfile.OnCurrencyChanged -= RefreshMoney;
        }

        public void Open()
        {
            if (root == null)
                return;

            root.SetActive(true);
            SelectTab(ShopTab.Sticker);
        }

        public void Close()
        {
            if (root == null || !root.activeSelf)
                return;

            root.SetActive(false);
            OnClosed?.Invoke();
        }

        private void SelectTab(ShopTab which)
        {
            tab = which;

            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null)
                    continue;

                var image = tabButtons[i].targetGraphic as Image;
                if (image != null)
                    image.color = (ShopTab)i == tab ? tabOnColor : tabOffColor;
            }

            Refresh();
        }

        /// <summary>
        /// 목록을 <b>통째로</b> 다시 그린다. 산 물건 하나만 고쳐도 될 것 같지만,
        /// 값·보유 표시가 함께 바뀌므로 전부 그리는 게 단순하다(우편함과 같은 방침).
        /// </summary>
        public void Refresh()
        {
            RefreshMoney();

            if (titleText != null)
                titleText.text = TabName(tab);

            // ⭐ 은행은 물건을 고르는 곳이 아니라 <b>맡기고 찾는</b> 곳이라 화면이 통째로 다르다.
            bool banking = tab == ShopTab.Bank && bank != null;

            bank?.SetVisible(banking);
            goodsRoot?.SetActive(!banking);

            if (banking)
            {
                if (noticeText != null)
                    noticeText.gameObject.SetActive(false);

                return;
            }

            if (catalog != null)
                catalog.Collect(tab, shown);
            else
                shown.Clear();

            if (noticeText != null)
            {
                noticeText.text = shown.Count > 0 ? string.Empty : "준비 중입니다";
                noticeText.gameObject.SetActive(shown.Count == 0);
            }

            BuildRows();
        }

        /// <summary>
        /// 스티커를 한 장 뽑는다. <b>무엇이 잘 나오는지는 화폐가 정한다</b> -
        /// 골드는 저코스트, 보석은 고코스트(<see cref="StickerGacha"/>).
        ///
        /// 중복이 나와도 그대로 준다 - 스티커는 중복 보유·중복 착용이 되므로 꽝이 없다.
        /// </summary>
        private void DrawSticker(ShopGood good)
        {
            if (stickerCatalog == null)
            {
                Notice("스티커 목록이 연결돼 있지 않습니다");
                return;
            }

            if (!PlayerShop.TrySpend(good))
            {
                Notice(good.currency == ShopCurrency.Gem ? "보석이 모자랍니다" : "골드가 모자랍니다");
                return;
            }

            var kind = good.currency == ShopCurrency.Gem
                ? StickerDrawKind.Gem : StickerDrawKind.Gold;

            var picked = StickerGacha.Draw(stickerCatalog, kind);
            Refresh();

            if (picked == null)
            {
                Notice("뽑을 스티커가 없습니다");
                return;
            }

            int owned = PlayerStickers.OwnedCount(picked.id);

            // 값을 치른 일에는 연출이 있어야 한다 - 안내 한 줄로는 무엇을 샀는지 안 읽힌다.
            if (reveal != null)
                reveal.Show(picked, owned);
            else
                Notice(picked.description + (owned > 1 ? "  (" + owned + "장)" : string.Empty));
        }

        /// <summary>
        /// 한 마디 알린다. 연출 창이 있으면 <b>그쪽으로</b> 띄운다 - 목록 옆의 작은 글자는
        /// 다시 그릴 때마다 지워져서 놓치기 쉽다(2026-09-03 사용자 신고: 안내가 안 보인다).
        /// </summary>
        private void Notice(string message)
        {
            if (reveal != null)
            {
                reveal.ShowMessage(message);
                return;
            }

            if (noticeText == null)
                return;

            noticeText.text = message;
            noticeText.gameObject.SetActive(true);
        }

        private void RefreshMoney()
        {
            if (goldText != null)
                goldText.text = PlayerProfile.Gold.ToString("N0");

            if (gemText != null)
                gemText.text = PlayerProfile.Gems.ToString("N0");
        }

        /// <summary>줄은 <b>버리지 않고 다시 쓴다</b> - 화면을 열 때마다 지웠다 만들면 GC 가 돈다.</summary>
        private void BuildRows()
        {
            if (rowTemplate == null || listContent == null)
                return;

            while (rows.Count < shown.Count)
            {
                var row = Instantiate(rowTemplate, listContent);
                row.name = "Row" + rows.Count;
                rows.Add(row);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < shown.Count;
                rows[i].gameObject.SetActive(used);

                if (used)
                    FillRow(rows[i], shown[i], i);
            }

            listContent.sizeDelta = new Vector2(listContent.sizeDelta.x,
                Mathf.Max(0f, shown.Count * (rowHeight + rowGap) - rowGap));
        }

        private void FillRow(RectTransform row, ShopGood good, int index)
        {
            row.anchoredPosition = new Vector2(0f, -index * (rowHeight + rowGap));

            SetText(row, "TitleText", good.displayName);
            SetText(row, "BodyText", good.description);

            // ⭐ 개수제다 - 값은 늘 보이고, 가진 수를 옆에 덧붙인다.
            int have = PlayerShop.GetCount(good.id);
            SetText(row, "PriceText", have > 0
                ? PriceLabel(good) + "  (" + have + ")"
                : PriceLabel(good));

            var buy = Find<Button>(row, "BuyButton");
            if (buy != null)
            {
                buy.onClick.RemoveAllListeners();
                // ⭐ <b>못 사도 눌리게 둔다</b>(2026-09-03 사용자 지시).
                // 예전엔 값이 모자라면 버튼을 꺼 버렸는데, 그러면 눌러도 아무 일이 없어서
                // <b>왜 안 되는지</b>가 안 남는다 - 눌리게 두고 이유를 말해 준다.
                buy.interactable = true;

                // 대신 눈으로도 알 수 있게 흐리게 한다.
                if (buy.targetGraphic != null)
                    buy.targetGraphic.color = CanAfford(good) ? buyColor : cannotAffordColor;

                var target = good;   // 람다가 늦게 읽는 걸 막는다
                buy.onClick.AddListener(() => Buy(target));
            }
        }

        private void Buy(ShopGood good)
        {
            // ⭐ 뽑기는 <b>창고에 넣지 않는다</b>(2026-09-03 사용자 기획) - 사는 순간 스티커
            // 한 장이 나온다. 창고에 넣어 두면 "쓰지도 못하는 뽑기권"이 쌓인다.
            if (good != null && good.isStickerDraw)
            {
                DrawSticker(good);
                return;
            }

            if (PlayerShop.TryBuy(good))
            {
                Refresh();
                return;
            }

            // 값을 못 치렀다 - 왜인지 알려 준다.

            Notice(good.currency == ShopCurrency.Gem ? "보석이 모자랍니다" : "골드가 모자랍니다");
        }

        [Header("구매 버튼 색")]
        [Tooltip("살 수 있을 때의 버튼 색.")]
        [SerializeField] private Color buyColor = new Color(0.36f, 0.32f, 0.55f, 1f);

        [Tooltip("값이 모자랄 때의 버튼 색. <b>버튼은 그래도 눌린다</b> - 누르면 이유를 알려 준다.")]
        [SerializeField] private Color cannotAffordColor = new Color(0.22f, 0.21f, 0.26f, 1f);

        private static bool CanAfford(ShopGood good)
            => good.currency == ShopCurrency.Gem
                ? PlayerProfile.Gems >= good.price
                : PlayerProfile.Gold >= good.price;

        private static string PriceLabel(ShopGood good)
            => (good.currency == ShopCurrency.Gem ? "보석 " : "골드 ") + good.price.ToString("N0");

        private static string TabName(ShopTab tab)
        {
            switch (tab)
            {
                case ShopTab.Sticker: return "스티커";
                case ShopTab.Bank: return "은행";
                case ShopTab.Interior: return "방 인테리어";
                default: return "선물";
            }
        }

        // ⚠ 자식 이름으로 찾는다 - 이름을 바꾸면 조용히 끊긴다(우편함과 같은 규칙).
}
}
