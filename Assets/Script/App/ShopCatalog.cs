using System;
using UnityEngine;

namespace JojoPuzzle.App
{
    /// <summary>상점의 칸. 파는 것의 성격이 아주 달라 칸으로 나눈다(2026-09-02 사용자 기획).</summary>
    public enum ShopTab
    {
        /// <summary>퍼즐 전투를 강화하는 스티커.</summary>
        Sticker = 0,

        /// <summary>예금하거나, 캐릭터를 담보로 빌린다.</summary>
        Bank = 1,

        /// <summary>방에 바를 벽지·바닥(방꾸미기).</summary>
        Interior = 2,

        /// <summary>캐릭터에게 줄 선물(음식 등).</summary>
        Gift = 3
    }

    /// <summary>
    /// 무엇으로 값을 치르는지.
    ///
    /// ⭐ <b>골드와 보석을 나눈 이유</b>: 골드는 배틀과 도박으로 <b>놀아서 버는 돈</b>이라,
    /// 값비싼 물건까지 골드로 팔면 벌이의 균형이 곧바로 무너진다(2026-09-02 사용자 결정).
    /// </summary>
    public enum ShopCurrency
    {
        Gold = 0,
        Gem = 1
    }

    /// <summary>상점에 놓인 물건 하나.</summary>
    [Serializable]
    public class ShopGood
    {
        [Tooltip("보유 판정에 쓰는 이름표. <b>겹치면 안 된다.</b>")]
        public string id;

        public ShopTab tab;

        public string displayName;

        [Tooltip("한 줄 설명. 길면 줄이 잘린다.")]
        public string description;

        public ShopCurrency currency;

        [Min(0)]
        public int price;

        [Tooltip("인테리어 칸일 때, RoomInteriorLibrary 의 몇 번째인지. 아니면 -1.")]
        public int interiorIndex = -1;

        [Tooltip("스티커 칸일 때, 이 물건이 <b>뽑기</b>인지. 켜면 사는 순간 스티커 한 장이 나온다. " +
                 "무엇이 잘 나오는지는 화폐가 정한다 - 골드는 저코스트, 보석은 고코스트 " +
                 "(2026-09-03 사용자 기획).")]
        public bool isStickerDraw;
    }

    /// <summary>
    /// 상점에 <b>무엇이 놓여 있는지</b>. 값과 목록을 코드에 박지 않는다 -
    /// 물건이 늘어날 때마다 스크립트를 고치게 되면 기획을 손볼 수가 없다
    /// (<c>BattleItemCatalog</c> 와 같은 방침).
    /// </summary>
    public class ShopCatalog : ScriptableObject
    {
        [SerializeField] private ShopGood[] goods = new ShopGood[0];

        public int Count => goods != null ? goods.Length : 0;

        public ShopGood Get(int index)
            => goods != null && index >= 0 && index < goods.Length ? goods[index] : null;

        /// <summary>그 칸의 물건을 <paramref name="into"/> 에 담는다. 새 리스트를 만들지 않는다.</summary>
        public void Collect(ShopTab tab, System.Collections.Generic.List<ShopGood> into)
        {
            if (into == null)
                return;

            into.Clear();

            if (goods == null)
                return;

            for (int i = 0; i < goods.Length; i++)
            {
                if (goods[i] != null && goods[i].tab == tab)
                    into.Add(goods[i]);
            }
        }
    }
}
