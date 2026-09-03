using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 캐릭터가 <b>자기 몫으로 들고 있는 돈</b>. 플레이어의 골드(<see cref="PlayerProfile.Gold"/>)와
    /// 별개다 - 이건 그 캐릭터 개인의 주머니이고, 나중에 아파트 컨텐츠에서 쓰인다
    /// (2026-08-25 사용자 기획).
    ///
    /// 배틀 한 판이 끝나면 <b>그 판에서 그 캐릭터의 퍼즐 조각을 몇 개 썼는지</b>에 따라 들어온다.
    /// 넣는 자리는 <see cref="UI.BattleCharacterPanel"/> 한 곳뿐이다.
    ///
    /// <b>왜 PanelType 에 넣지 않는가</b>: `PanelType` 은 캐릭터 도감 데이터인데 거기에 이미
    /// level·grade·exp 같은 유저 상태가 섞여 있는 게 이 프로젝트의 가장 큰 부채로 적혀 있다.
    /// 소지금은 명백히 유저 상태라 같은 실수를 반복하지 않는다(<see cref="PlayerCollection"/> 과 같은 방침).
    ///
    /// <b>지금은 저장되지 않는다.</b> 세이브가 생기면 여기에 실제 값을 넣어주면 되고,
    /// 화면 코드는 안 고쳐도 된다.
    /// </summary>
    public static class CharacterWallet
    {
        /// <summary>
        /// 퍼즐 조각 하나를 쓸 때 그 캐릭터에게 들어오는 돈.
        /// <b>확정 수치가 아니다</b> - 아파트에서 이 돈으로 무엇을 사게 될지 정해지면 맞춰야 한다.
        /// </summary>
        public const int MoneyPerPiece = 1;

        private static readonly Dictionary<PanelType, long> money = new Dictionary<PanelType, long>();

        /// <summary>
        /// 입주할 때 기본으로 들고 있는 돈(2026-09-02 사용자 확정).
        ///
        /// <b>0 이면 미니게임을 시작할 수가 없다</b> - 도박은 양쪽이 앞돈을 내야 성립하는데
        /// 배틀을 한 번도 안 돌린 캐릭터는 벌어둔 게 없어서 문전푸리다.
        /// </summary>
        public const long StartingMoney = 100L;

        /// <summary>이 캐릭터가 들고 있는 돈. 기록이 없으면 <see cref="StartingMoney"/>.</summary>
        public static long Get(PanelType character)
            => character != null && money.TryGetValue(character, out long value)
                ? value
                : (character != null ? StartingMoney : 0L);

        /// <summary>돈을 넣는다. 음수면 아무 일도 하지 않는다 - 빼는 건 쓰는 쪽이 생기면 그때 연다.</summary>
        public static void Add(PanelType character, long amount)
        {
            if (character == null || amount <= 0L)
                return;

            money[character] = Get(character) + amount;
        }

        /// <summary>실제 값을 넣는다. 세이브를 불러올 때 부르면 된다.</summary>
        public static void Set(PanelType character, long amount)
        {
            if (character == null)
                return;

            money[character] = amount < 0L ? 0L : amount;
        }

        /// <summary>
        /// 그 캐릭터에게서 돈을 빼온다. 모자라면 <b>아무것도 빼지 않고</b> false 를 돌려준다 -
        /// 반씩만 빼가면 지갑과 화면이 어긋난다.
        ///
        /// <b>미니게임(도박)이 이걸 열게 했다</b>(2026-09-02) - 그전까지는 넣기만 했고
        /// 주석에도 "빼는 건 쓰는 쪽이 생기면 그때 열다"고 적혀 있었다.
        /// </summary>
        public static bool TrySpend(PanelType character, long amount)
        {
            if (character == null || amount < 0L)
                return false;

            if (amount == 0L)
                return true;

            long have = Get(character);
            if (have < amount)
                return false;

            money[character] = have - amount;
            return true;
        }

        /// <summary>조각 수를 돈으로 환산한다.</summary>
        public static long MoneyFor(int pieces) => pieces <= 0 ? 0L : (long)pieces * MoneyPerPiece;
    }
}
