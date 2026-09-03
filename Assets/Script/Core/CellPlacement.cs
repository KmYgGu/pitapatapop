namespace JojoPuzzle.Core
{
    /// <summary>
    /// 특수 블록을 놓을 자리를 <b>어떻게 고르는가</b>. 캐릭터마다 다르다.
    ///
    /// ⭐ <b>스킬은 캐릭터의 성격을 보여주는 장치이기도 하다</b>(2026-09-03 사용자 기획).
    /// 모두가 조심스럽게 놓으면 캐릭터가 전부 상냥해진다 - 그건 기획이 아니라 사고다.
    /// </summary>
    public enum PlacementStyle
    {
        /// <summary>
        /// <b>기본값.</b> 덜 아까운 칸부터 고른다 - 일반=방해블록 &lt; 큐브 &lt; 특수 블록.
        /// 유나의 버닝 트랙이 이렇다.
        /// </summary>
        Careful = 0,

        /// <summary>
        /// <b>아랑곳하지 않는다.</b> 놓을 수 있는 자리면 무엇이든 똑같이 보고 무작위로 고른다 -
        /// 큐브든 남의 특수 블록이든.
        ///
        /// ⭐ <b>미스틱이 이렇다</b>(2026-09-03 사용자 확정). 이 캐릭터의 값어치는
        /// "우리 쪽에도 피해를 입힐지 모르지만 제거 데미지와 자기 퍼즐이 남아 이득이 크다"에
        /// 있다. 단점을 알아서 피해 가면 그 값어치가 사라지고, 캐릭터가 통째로 물러진다.
        /// </summary>
        Reckless = 1,
    }

    /// <summary>
    /// <b>이 칸을 내주기 얼마나 아까운가.</b>
    /// 값이 작을수록 먼저 내준다.
    /// </summary>
    public enum PlacementCost
    {
        /// <summary>
        /// 빈 칸·일반 조각·고정 칸·<b>방해블록</b>. 아깝지 않다 - 마음껏 덮어쓴다.
        /// 방해블록이 여기 있는 건 <b>적의 것</b>이라 아까울 이유가 없기 때문이다.
        /// </summary>
        Free = 0,

        /// <summary>상자. <b>되도록 피하지만</b> 판에 다른 자리가 없으면 내준다.</summary>
        Box = 1,

        /// <summary>
        /// <b>특수 블록</b> - 미스틱의 특수 퍼즐과 유나의 점화 블록을 함께 이른다.
        /// <b>제일 마지막</b>이다: 상자와 특수 블록이 같이 있으면 상자를 먼저 내준다.
        ///
        /// ⭐ 서로 덮어쓸 수 있다 - <b>나중에 소환한 쪽이 우선권</b>을 가지므로,
        /// 지금 놓는 것은 언제나 판 위의 모든 특수 블록보다 새것이라 전부 덮을 수 있다.
        /// 그 중에서는 <b>가장 오래된 것부터</b> 내준다.
        /// </summary>
        Special = 2,

        /// <summary>구멍. <b>어떤 수단으로도 안 지워진다</b>는 판 전체의 규칙이다.</summary>
        Never = 3,
    }

    /// <summary>
    /// 판에 무언가를 <b>새로 놓아야 할 때</b> 어느 칸부터 희생시킬지 정하는 공통 기준.
    ///
    /// ⭐ <b>놓는 쪽마다 따로 정하지 않는다</b>(2026-09-03 사용자 확정). 유나의 점화 블록이
    /// 처음 쓰지만, 앞으로 판에 무언가를 심는 스킬이 늘어도 같은 기준을 쓰라고 여기 모아 뒀다 -
    /// 캐릭터마다 "상자는 피하나?"를 따로 정하기 시작하면 금세 서로 어긋난다.
    /// </summary>
    public static class CellPlacement
    {
        public static PlacementCost CostOf(in Cell cell)
        {
            switch (cell.kind)
            {
                case CellKind.Empty:
                case CellKind.Normal:
                case CellKind.StandHeld:
                case CellKind.Obstacle:
                    return PlacementCost.Free;

                case CellKind.Box:
                    return PlacementCost.Box;

                // 미스틱의 특수 퍼즐과 유나의 점화 블록은 <b>같은 등급</b>이다 - 둘 다 특수 블록이고,
                // 나중에 소환한 쪽이 먼저 소환한 쪽을 덮을 수 있다.
                case CellKind.Special:
                case CellKind.BurnTrack:
                    return PlacementCost.Special;

                // 구멍만 남는다.
                default:
                    return PlacementCost.Never;
            }
        }

        /// <summary>
        /// 같은 등급 안에서 <b>어느 것을 먼저 내줄지</b>. 작을수록 먼저다.
        ///
        /// ⭐ 특수 퍼즐은 <b>나중에 소환한 쪽이 우선권</b>을 갖는다(2026-09-03 사용자 확정) -
        /// 그래서 <b>먼저 소환된 것부터</b> 내준다. 방금 쓴 스킬의 결과물이 곧바로 지워지면
        /// 스킬을 쓴 보람이 없기 때문이다.
        ///
        /// 소환 순번은 <see cref="BoardData"/> 가 아니라 칸 자신이 들고 다닌다 - 낙하로 옮겨져도
        /// 따라가고, 지워지면 자연히 사라진다(specialMatchesLeft 와 같은 이유).
        /// </summary>
        public static int SacrificeOrderOf(in Cell cell)
            => CostOf(cell) == PlacementCost.Special ? cell.specialSummonOrder : 0;

        /// <summary>
        /// 그 구역 전체를 내주려면 얼마나 아까운가 - <b>가장 아까운 칸</b>이 값을 정한다.
        /// 못 내주는 칸이 하나라도 있으면 구역 전체가 <see cref="PlacementCost.Never"/> 다
        /// (2x2 가 조각난 채로 생기면 안 되기 때문이다).
        /// </summary>
        public static PlacementCost Worst(PlacementCost a, PlacementCost b)
            => a > b ? a : b;
    }
}
