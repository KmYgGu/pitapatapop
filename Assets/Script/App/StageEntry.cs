using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 스테이지 선택에서 배틀로 넘기는 짐. 씬이 갈리므로 static 으로 들고 간다
    /// (<see cref="SessionState"/> 와 같은 이유).
    ///
    /// <b>여기 규칙을 넣지 말 것.</b> 무엇을 고랐는지만 담고, 그 값으로 무엇을 하는지는
    /// 배틀 쪽이 정한다. 골드 차감처럼 되돌릴 수 없는 일은 <b>실제로 배틀에 들어갈 때</b>
    /// 한 번만 해야 해서 <see cref="Commit"/> 로 모아뒀다.
    /// </summary>
    public static class StageEntry
    {
        private static readonly List<BattleItemKind> purchased = new List<BattleItemKind>();

        public static ChapterDefinition Chapter { get; private set; }

        public static StageDefinition Stage { get; private set; }

        /// <summary>
        /// 이번 판에 쓰기로 한 아이템들. 배틀이 시작될 때 읽는다.
        ///
        /// <b>이름과 달리 "산 것"만 있는 게 아니다</b> - 개수제가 생기면서(2026-08-28) 갖고 있던
        /// 걸 꺼내 쓴 것도 여기 들어온다. 배틀 쪽은 어느 쪽이든 똑같이 효과만 걸면 되므로
        /// 구분해서 넘기지 않는다. 이름은 <see cref="Battle.BattleManager"/> 가 쓰고 있어 그대로 뒀다.
        /// </summary>
        public static IReadOnlyList<BattleItemKind> PurchasedItems => purchased;

        public static bool HasStage => Stage != null;

        public static void Select(ChapterDefinition chapter, StageDefinition stage)
        {
            Chapter = chapter;
            Stage = stage;
            purchased.Clear();
        }

        /// <summary>아이템을 담거나 뺀다. 아직 골드는 빠지지 않는다.</summary>
        public static void SetItemSelected(BattleItemKind kind, bool selected)
        {
            bool has = purchased.Contains(kind);

            if (selected && !has)
                purchased.Add(kind);
            else if (!selected && has)
                purchased.Remove(kind);
        }

        public static bool IsItemSelected(BattleItemKind kind) => purchased.Contains(kind);

        /// <summary>
        /// 그 아이템을 <b>현물로 들고 있는지</b>. 들고 있으면 고를 때 골드가 안 나가고
        /// 배틀에 들어갈 때 한 개가 빠진다.
        ///
        /// <b>구매는 다 쓴 뒤에만</b>(2026-08-28 사용자 기획) - 그래서 "산다"와 "쓴다"를 화면에서
        /// 따로 고르게 하지 않는다. 고르는 동작은 하나이고, 보유분이 있으면 그게 먼저 나간다.
        /// 남은 게 없을 때 비로소 그 고름이 구매가 된다.
        /// </summary>
        public static bool IsItemOwned(BattleItemKind kind) => PlayerInventory.GetCount(kind) > 0;

        /// <summary>
        /// 고른 아이템 중 <b>실제로 돈을 내야 하는 것</b>들의 값의 합. 보유분으로 나가는 아이템은
        /// 빠진다 - 여기서 빼지 않으면 갖고 있는 아이템을 골랐는데 골드가 모자라다며 막힌다.
        /// </summary>
        public static int GetTotalPrice(BattleItemCatalog catalog)
        {
            if (catalog == null || catalog.items == null)
                return 0;

            int total = 0;
            for (int i = 0; i < catalog.items.Length; i++)
            {
                var item = catalog.items[i];
                if (item != null && purchased.Contains(item.kind) && !IsItemOwned(item.kind))
                    total += item.price;
            }

            return total;
        }

        /// <summary>
        /// 실제로 배틀에 들어갈 때 딱 한 번 부른다 - 하트를 쓰고 골드를 낸다.
        /// 하나라도 모자라면 <b>아무것도 하지 않고</b> false 를 돌려준다(반쯤 차감되면 안 된다).
        /// </summary>
        public static bool Commit(BattleItemCatalog catalog, System.DateTime utcNow, out string failReason)
        {
            failReason = string.Empty;

            if (Stage == null)
            {
                failReason = "스테이지가 선택되지 않았습니다.";
                return false;
            }

            int price = GetTotalPrice(catalog);
            if (PlayerProfile.Gold < price)
            {
                failReason = "골드가 모자랍니다.";
                return false;
            }

            int heartCost = Stage.heartCost;
            if (heartCost > 0 && PlayerProfile.Hearts.GetCount(utcNow) < heartCost)
            {
                failReason = "하트가 모자랍니다.";
                return false;
            }

            // 여기까지 왔으면 둘 다 충분하다 - 이제 실제로 뺀다.
            if (heartCost > 0)
                PlayerProfile.Hearts.TrySpend(utcNow, heartCost);

            PlayerProfile.Gold -= price;

            // <b>보유분은 여기서 빠진다.</b> 위 GetTotalPrice 가 값을 안 매긴 아이템이 정확히
            // 이것들이라, 판정과 차감이 같은 기준(IsItemOwned)을 본다.
            //
            // <b>골드를 뺀 뒤에 뺀다</b> - 순서가 반대면 개수를 먼저 깎아놓고 골드가 모자라
            // 돌아가는 길이 생긴다(그 판정은 위에서 이미 끝났지만, 나중에 조건이 하나 더 붙어도
            // 이 순서면 안전하다).
            for (int i = 0; i < purchased.Count; i++)
            {
                if (IsItemOwned(purchased[i]))
                    PlayerInventory.TrySpend(purchased[i]);
            }

            return true;
        }

        public static void Clear()
        {
            Chapter = null;
            Stage = null;
            purchased.Clear();
        }
    }
}
