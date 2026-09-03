using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 캐릭터에게 경험치를 넣고 레벨을 올리는 <b>유일한 자리</b>.
    ///
    /// <b>⚠ 지금은 `PanelType` 애셋을 직접 고친다.</b> 레벨과 경험치가 캐릭터 도감 데이터인
    /// `PanelType` 에 들어 있기 때문인데, 그건 이 프로젝트에 적어둔 <b>가장 큰 부채</b>다.
    /// 그래서 에디터에서 실행하면 <b>애셋 값이 실제로 바뀌어 저장된다</b>(플레이를 멈춰도 안 돌아온다).
    ///
    /// 세이브가 생기면 레벨·경험치는 유저 데이터로 빠져야 하고, 그때 고칠 곳은 <b>여기 한 곳</b>이다 -
    /// 화면들은 전부 이 함수만 부른다.
    /// </summary>
    public static class CharacterLeveling
    {
        /// <summary>
        /// 경험치를 넣고 오를 수 있는 만큼 레벨을 올린다.
        /// 만렙이거나 넣을 게 없으면 아무것도 하지 않고 false.
        /// </summary>
        public static bool TryApplyExp(PanelType character, int amount)
        {
            if (character == null || amount <= 0 || character.IsMaxLevel)
                return false;

            character.currentExp += amount;

            // 한 번에 여러 레벨이 오를 수 있다. 필요치가 0이면(표가 비었거나 만렙) 무한 루프가
            // 되므로 반드시 같이 확인한다.
            while (!character.IsMaxLevel)
            {
                int need = character.ExpToNextLevel;
                if (need <= 0 || character.currentExp < need)
                    break;

                character.currentExp -= need;
                character.level++;
            }

            // 만렙에 닿으면 남은 경험치는 갈 곳이 없다 - 들고 있으면 게이지가 꽉 찬 채로 남아
            // "곧 오를 것처럼" 보인다.
            if (character.IsMaxLevel)
                character.currentExp = 0;

            return true;
        }

        /// <summary>
        /// 아이템 한 개를 써서 경험치를 넣는다. 아이템이 없거나 만렙이면 <b>소모하지 않고</b> false.
        /// </summary>
        public static bool TryUseExpItem(PanelType character, ExpItem item)
        {
            if (character == null || item == null || character.IsMaxLevel)
                return false;

            // 먼저 쓸 수 있는지 확인하고 나서 소모한다 - 넣지도 못했는데 아이템만 사라지면 안 된다.
            if (!PlayerInventory.TrySpend(item.kind))
                return false;

            if (TryApplyExp(character, item.exp))
                return true;

            // 여기까지 오면 넣지 못한 것이므로 아이템을 돌려준다.
            PlayerInventory.SetCount(item.kind, PlayerInventory.GetCount(item.kind) + 1);
            return false;
        }
    }
}
