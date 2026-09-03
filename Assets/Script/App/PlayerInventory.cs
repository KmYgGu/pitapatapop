using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 플레이어가 들고 있는 소모품 개수. 경험치 아이템과 <b>배틀 보조 아이템</b> 둘 다.
    ///
    /// <b>저장되지 않는다.</b> 세이브가 생기면 여기에 실제 값을 넣어주면 되고 화면 코드는
    /// 안 고쳐도 된다(<see cref="PlayerProfile"/>·<see cref="PlayerCollection"/> 과 같은 방침).
    /// 지금 들어 있는 수는 <b>화면을 만들기 위한 임시값</b>이다.
    ///
    /// <b>배틀 아이템이 개수제가 된 이유</b>(2026-08-28 사용자 기획): 네 가지를 다 사면 5,000골드인데
    /// 한 판에 버는 골드가 100 남짓이라 초반에는 살 수가 없다. 그래서 우편·보물상자 같은 경로로
    /// <b>현물을 나눠주고</b>, 골드로 사는 건 그 현물을 다 쓴 뒤의 길로 남긴다.
    /// </summary>
    public static class PlayerInventory
    {
        private static readonly Dictionary<ExpItemKind, int> expItems = new Dictionary<ExpItemKind, int>
        {
            { ExpItemKind.Small, 25 },
            { ExpItemKind.Medium, 8 },
            { ExpItemKind.Large, 2 },
        };

        /// <summary>
        /// 배틀 보조 아이템의 보유 개수. <b>시작값은 0이다</b> - 경험치 아이템처럼 미리 넣어두면
        /// 우편함이 실제로 주는 건지 확인할 수가 없다. 우편함에서 받아 채우는 게 정상 경로다.
        /// </summary>
        private static readonly Dictionary<BattleItemKind, int> battleItems =
            new Dictionary<BattleItemKind, int>();

        public static int GetCount(ExpItemKind kind) =>
            expItems.TryGetValue(kind, out int n) ? n : 0;

        public static void SetCount(ExpItemKind kind, int count) =>
            expItems[kind] = count < 0 ? 0 : count;

        /// <summary>한 개 쓴다. 없으면 아무것도 하지 않고 false.</summary>
        public static bool TrySpend(ExpItemKind kind, int amount = 1)
        {
            if (amount <= 0)
                return false;

            int owned = GetCount(kind);
            if (owned < amount)
                return false;

            expItems[kind] = owned - amount;
            return true;
        }

        // ------------------------------------------------------------------ 배틀 보조 아이템

        public static int GetCount(BattleItemKind kind) =>
            battleItems.TryGetValue(kind, out int n) ? n : 0;

        public static void SetCount(BattleItemKind kind, int count) =>
            battleItems[kind] = count < 0 ? 0 : count;

        /// <summary>
        /// 아이템을 넣어준다. <b>주는 경로는 전부 여기를 지난다</b> - 우편함이 지금 유일한
        /// 손님이고, 나중에 보물 상자·시간 보상도 같은 문으로 들어오면 된다.
        /// </summary>
        public static void Add(BattleItemKind kind, int amount)
        {
            if (amount <= 0)
                return;

            battleItems[kind] = GetCount(kind) + amount;
        }

        /// <summary>한 개 쓴다. 없으면 아무것도 하지 않고 false.</summary>
        public static bool TrySpend(BattleItemKind kind, int amount = 1)
        {
            if (amount <= 0)
                return false;

            int owned = GetCount(kind);
            if (owned < amount)
                return false;

            battleItems[kind] = owned - amount;
            return true;
        }
    }
}
