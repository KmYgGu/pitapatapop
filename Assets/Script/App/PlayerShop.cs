using System.Collections.Generic;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 상점에서 산 물건을 <b>몇 개</b> 가지고 있는지.
    ///
    /// ⭐ <b>개수제다</b>(2026-09-02 사용자 지시: *"한 번 사고 끝이면 방꾸미기가 쉬워지잖아"*).
    /// 인테리어는 방마다 발라야 하므로, 방 셋을 같은 벽지로 꾸미려면 세 개를 사야 한다 -
    /// 한 번 사면 끝이면 첫 구매로 아파트 전체가 끝나 버린다.
    ///
    /// <b>물건을 쓰는 곳은 여기를 안 본다</b> - 인테리어를 어느 방에 발랐는지는
    /// <c>ApartmentRoomDecor</c> 가 따로 들고 있다. 여기는 "몇 개 남았는가"만 안다.
    ///
    /// <b>⚠ 저장되지 않는다</b>(이 프로젝트의 모든 유저 상태와 같다).
    /// 세이브 계층이 생기면 이 클래스가 그 값을 받아오는 창구가 된다.
    /// </summary>
    public static class PlayerShop
    {
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        /// <summary>가진 것이 달라졌다. 상점 화면과 방꾸미기 화면이 다시 그린다.</summary>
        public static event System.Action OnChanged;

        public static int GetCount(string id)
            => !string.IsNullOrEmpty(id) && counts.TryGetValue(id, out int n) ? n : 0;

        public static bool Owns(string id) => GetCount(id) > 0;

        /// <summary>
        /// 값을 치르고 하나 넣는다. <b>돈을 먼저 확인하고 깎은 뒤에</b> 넣는다 -
        /// 넣고 나서 깎으면 모자랄 때 공짜로 준 꼴이 된다.
        /// </summary>
        public static bool TryBuy(ShopGood good)
        {
            if (good == null || string.IsNullOrEmpty(good.id))
                return false;

            bool paid = good.currency == ShopCurrency.Gem
                ? PlayerProfile.TrySpendGems(good.price)
                : PlayerProfile.TrySpendGold(good.price);

            if (!paid)
                return false;

            Add(good.id, 1);
            return true;
        }

        /// <summary>
        /// 값만 치른다 - <b>창고에 넣지 않는다</b>. 사는 순간 곧바로 무언가가 일어나는 물건이
        /// 쓴다(스티커 뽑기처럼). 창고에 넣어 두면 "쓰지도 못하는 뽑기권"이 쌓인다.
        /// </summary>
        public static bool TrySpend(ShopGood good)
        {
            if (good == null)
                return false;

            return good.currency == ShopCurrency.Gem
                ? PlayerProfile.TrySpendGems(good.price)
                : PlayerProfile.TrySpendGold(good.price);
        }

        /// <summary>값을 안 받고 넣는다. 보상으로 주는 길(우편함 등)이 쓴다.</summary>
        public static void Add(string id, int count = 1)
        {
            if (string.IsNullOrEmpty(id) || count <= 0)
                return;

            counts[id] = GetCount(id) + count;
            OnChanged?.Invoke();
        }

        /// <summary>
        /// 하나 쓴다. 없으면 false - <b>없는데 쓴 것으로 치지 않는다</b>.
        /// 방에 인테리어를 바르는 쪽이 부른다.
        /// </summary>
        public static bool TryUse(string id, int count = 1)
        {
            if (count <= 0 || GetCount(id) < count)
                return false;

            int left = GetCount(id) - count;

            if (left > 0)
                counts[id] = left;
            else
                counts.Remove(id);

            OnChanged?.Invoke();
            return true;
        }

        public static void Clear()
        {
            if (counts.Count == 0)
                return;

            counts.Clear();
            OnChanged?.Invoke();
        }
    }
}
