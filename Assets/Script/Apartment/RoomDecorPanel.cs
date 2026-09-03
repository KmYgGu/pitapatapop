using System.Collections.Generic;
using JojoPuzzle.App;
using UnityEngine;
using UnityEngine.UI;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// <b>방꾸미기</b> - 상점에서 산 인테리어를 그 방에 바른다(2026-09-02).
    ///
    /// 상점(<see cref="ShopPanel"/>)은 <b>가지는 것</b>까지만 하고, <b>어느 방에 바를지</b>는
    /// 여기서 정한다 - 인테리어는 방마다 따로이기 때문이다(<see cref="ApartmentRoomDecor"/>).
    ///
    /// 목록에는 <b>남은 것만</b> 뜬다. 안 산 것까지 늘어놓으면 여기가 또 하나의 상점이 되어,
    /// 어디서 사는 건지가 흐려진다.
    ///
    /// ⭐ <b>바르면 한 장 없어지고, 바꾸면 옛 것을 되돌려 받는다</b>(2026-09-02 사용자 지시).
    /// 개수제인 이유는 방 셋을 같은 벽지로 꾸미려면 세 개를 사야 하기 때문이고,
    /// 되돌려 주는 이유는 <b>완전한 소모품이면 바꾸는 것 자체가 무섭기</b> 때문이다 -
    /// 한 번 잘못 고르면 돈이 날아가니 아무도 방꾸미기에 손을 안 대게 된다.
    /// 개수는 "지금 몇 군데 꾸밀 수 있나"를 뜻한다.
    ///
    /// ⚠ 컴포넌트는 늘 켜져 있는 바깥 껍데기에 붙는다(우편함·상점과 같은 규칙).
    /// </summary>
    public class RoomDecorPanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;

        [SerializeField] private Text titleText;
        [SerializeField] private Text noticeText;

        [Tooltip("줄의 본. 꺼진 채로 두면 복제해 쌓는다.")]
        [SerializeField] private RectTransform rowTemplate;

        [SerializeField] private RectTransform listContent;

        [SerializeField] private float rowHeight = 52f;
        [SerializeField] private float rowGap = 6f;

        [SerializeField] private Button closeButton;

        [Header("물건")]
        [Tooltip("인테리어 목록. 아파트의 ApartmentRoomInteriors 와 <b>같은 것</b>을 물린다.")]
        [SerializeField] private RoomInteriorLibrary library;

        [Tooltip("상점 물건표. 어떤 인테리어를 샀는지 여기서 이어 본다.")]
        [SerializeField] private ShopCatalog catalog;

        /// <summary>닫혔다.</summary>
        public event System.Action OnClosed;

        public bool IsOpen => root != null && root.activeSelf;

        private int room = -1;

        private readonly List<ShopGood> owned = new List<ShopGood>();
        private readonly List<ShopGood> all = new List<ShopGood>();
        private readonly List<RectTransform> rows = new List<RectTransform>();

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (rowTemplate != null)
                rowTemplate.gameObject.SetActive(false);

            root?.SetActive(false);
        }

        /// <summary>그 방의 꾸미기를 연다.</summary>
        public void Open(int roomIndex, string roomName)
        {
            if (root == null)
                return;

            room = roomIndex;
            root.SetActive(true);

            if (titleText != null)
                titleText.text = string.IsNullOrEmpty(roomName) ? "방꾸미기" : roomName + " 꾸미기";

            Refresh();
        }

        public void Close()
        {
            if (root == null || !root.activeSelf)
                return;

            root.SetActive(false);
            room = -1;
            OnClosed?.Invoke();
        }

        private void Refresh()
        {
            owned.Clear();

            if (catalog != null)
            {
                catalog.Collect(ShopTab.Interior, all);

                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].interiorIndex >= 0 && PlayerShop.Owns(all[i].id))
                        owned.Add(all[i]);   // 남은 게 있는 것만 - 개수제다
                }
            }

            if (noticeText != null)
            {
                noticeText.text = owned.Count > 0 ? string.Empty : "상점에서 인테리어를 먼저 사야 합니다";
                noticeText.gameObject.SetActive(owned.Count == 0);
            }

            BuildRows();
        }

        private void BuildRows()
        {
            if (rowTemplate == null || listContent == null)
                return;

            while (rows.Count < owned.Count)
            {
                var row = Instantiate(rowTemplate, listContent);
                row.name = "Row" + rows.Count;
                rows.Add(row);
            }

            int current = room >= 0 ? ApartmentRoomDecor.Get(room) : ApartmentRoomDecor.Plain;

            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < owned.Count;
                rows[i].gameObject.SetActive(used);

                if (used)
                    FillRow(rows[i], owned[i], i, current);
            }

            listContent.sizeDelta = new Vector2(listContent.sizeDelta.x,
                Mathf.Max(0f, owned.Count * (rowHeight + rowGap) - rowGap));
        }

        private void FillRow(RectTransform row, ShopGood good, int index, int current)
        {
            row.anchoredPosition = new Vector2(0f, -index * (rowHeight + rowGap));

            string name = library != null ? library.NameOf(good.interiorIndex) : good.displayName;
            SetText(row, "TitleText", string.IsNullOrEmpty(name) ? good.displayName : name);

            bool applied = good.interiorIndex == current;
            SetText(row, "PriceText", applied ? "바름" : PlayerShop.GetCount(good.id).ToString());

            var apply = Find<Button>(row, "BuyButton");
            if (apply == null)
                return;

            apply.onClick.RemoveAllListeners();
            apply.interactable = !applied;

            var target = good;
            apply.onClick.AddListener(() => Apply(target));
        }

        private void Apply(ShopGood good)
        {
            if (room < 0 || good == null)
                return;

            int before = ApartmentRoomDecor.Get(room);

            // ⭐ <b>바르면 한 장 없어진다</b>(2026-09-02 사용자 지시) - 방마다 사야 한다.
            // 쓰는 데 실패하면 바르지도 않는다. 안 그러면 없는 걸 바른 꼴이 된다.
            if (!PlayerShop.TryUse(good.id))
                return;

            // ⭐⭐ <b>떼어낸 벽지는 되돌려 받는다</b>(2026-09-02 사용자 지시).
            // 완전한 소모품이면 <b>바꾸는 것 자체가 무섭다</b> - 한 번 잘못 고르면 돈이 날아가니
            // 아무도 손을 안 대게 된다. 개수는 "지금 몇 군데 꾸밀 수 있나"를 뜻하고,
            // 바꾸는 건 붙였다 뗐다 하는 일이라 값이 들지 않는다.
            ReturnOld(before);

            // 바르는 건 이 한 줄이다 - 아파트도 미니게임 방도 이 값을 보고 그린다.
            ApartmentRoomDecor.Set(room, good.interiorIndex);
            Refresh();
        }

        /// <summary>전에 발라져 있던 인테리어를 도로 넣어준다. 안 꾸민 방이었으면 할 일이 없다.</summary>
        private void ReturnOld(int interiorIndex)
        {
            if (interiorIndex == ApartmentRoomDecor.Plain || catalog == null)
                return;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].interiorIndex == interiorIndex)
                {
                    PlayerShop.Add(all[i].id);
                    return;
                }
            }
        }

}
}
