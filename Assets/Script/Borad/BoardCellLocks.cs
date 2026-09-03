using System.Collections.Generic;

namespace JojoPuzzle.View
{
    /// <summary>
    /// <b>지금 이 칸을 누가 쥐고 있는가.</b>
    ///
    /// 판 위에서는 매치 처리·낙하·리필·스킬 연출이 <b>동시에</b> 돌 수 있어서, 어느 칸이
    /// "지금 다른 처리가 쓰는 중"인지 표시해 두지 않으면 서로 같은 칸을 가져간다.
    /// 그 표시를 한 곳에 모아 둔 것이 이 클래스다.
    ///
    /// ⭐ <b>인터페이스가 아니라 객체다</b>(2026-09-03). 예전엔 입력 컨트롤러가 구현하는
    /// 인터페이스였는데, 판을 고치는 처리가 하나 늘 때마다 멤버가 따라 붙었다. 집합 셋을
    /// 들고 있는 객체 하나를 <b>다들 그냥 나눠 들면</b> 그 배관이 통째로 없어진다.
    ///
    /// ⭐ <b>잠금은 세 겹이다</b>:
    /// <code>
    ///   기본(Claim)     - 자동 처리가 못 건드린다.               집는 중, 매치 처리 중.
    ///   전용(Exclusive) - 아무도 못 건드린다.                    스킬 연출처럼 칸이 통째로 바뀔 때.
    ///   안착(Settling)  - 자동 처리만 막고 손은 연다.            데이터는 확정됐고 연출만 남았을 때.
    /// </code>
    /// 안착 잠금까지 손을 막으면, 스탠드업 타임에 빠르게 움직일 때 낙하·리필 연출마다
    /// 0.5초씩 손이 묶인다. 데이터는 이미 정해졌으니 막을 이유가 없다.
    /// </summary>
    public sealed class BoardCellLocks
    {
        // 지금 다른 작업(드래그 중 / 매치 이펙트 진행 중)에 쓰이고 있어서 만지면 안 되는 칸들.
        // 여러 매치 처리 코루틴이 동시에 돌 수 있으므로 공유 집합으로 관리.
        private readonly HashSet<(int x, int y)> locked = new HashSet<(int x, int y)>();

        // locked 중에서 "데이터는 이미 확정됐고 연출만 남아 잠겨있는" 칸을 따로 표시해두는 집합.
        // 두 경우가 여기 들어온다:
        //   - 매치가 커밋되어 비워졌지만 접기 연출이 재생 중인 칸
        //   - 낙하/리필로 값이 이미 확정됐지만 떨어지는 연출이 재생 중인 칸
        // ApplyGravity/RefillEmptyCells/ScanBoardForMatches 같은 자동 시스템은 여전히 locked 를
        // 그대로 벽처럼 취급해야 하지만(그래야 무관한 다른 매치의 중력/리필이 아직 연출 중인 이 칸을
        // 가로채 못 씀 - 그러지 않으면 A의 리필 호출 때문에 아직 접기 연출이 안 끝난 B의 칸이 마치
        // 다시 처리당한 것처럼 보이는 버그가 생김), 플레이어 조작만은 예외적으로 허용하기 위한 표시다.
        // 집기(TryBeginDrag)와 놓기(EndDrag)가 이 집합을 참고해서 locked 의 차단을 우회시킨다.
        private readonly HashSet<(int x, int y)> playerAllowed = new HashSet<(int x, int y)>();

        // 플레이어가 방금 조각을 놓아서 그 처리(ResolveMoveRoutine)가 소유하고 있는 목적지 칸.
        // 낙하/리필 중인 칸에도 드롭할 수 있게 되면서 필요해졌다: 그 칸의 잠금은 원래
        // 낙하 쪽이 걸어둔 것인데, 드롭이 성립하면 소유권이 이쪽으로 넘어온다.
        // 이 표시가 없으면 낙하 연출이 끝나는 순간 낙하 쪽이 "내가 건 잠금"이라 여기고 풀어버려서,
        // 아직 처리 중인 목적지를 다른 코루틴의 낙하/리필이 가로챌 수 있다.
        private readonly HashSet<(int x, int y)> owned = new HashSet<(int x, int y)>();

        /// <summary>
        /// 규칙 층(BoardManager)에 <b>"벽으로 칠 칸"</b>으로 넘기는 집합.
        /// ⚠ <b>넘겨만 주고 고치지 않는다</b> - 고치는 건 아래 메서드들의 몫이다.
        /// BoardManager 쪽 서명이 ISet 을 받아서 그대로 열어 둔다.
        /// </summary>
        public ISet<(int x, int y)> Blocked => locked;

        // ---- 기본 ----

        /// <summary>
        /// 자동 처리가 이 칸을 못 건드리게 한다.
        /// ⚠ <b>HashSet 처럼 bool 을 돌려준다</b> - 원래 없던 칸이면 true.
        /// 부르는 쪽에 그 값을 쓰는 자리가 있어서(구멍 정리) 그대로 흘려보낸다.
        /// </summary>
        public bool Claim((int x, int y) cell) => locked.Add(cell);

        /// <summary>기본 잠금을 푼다. <b>실제로 잠겨 있었으면 true.</b></summary>
        public bool Release((int x, int y) cell) => locked.Remove(cell);

        public bool IsLocked((int x, int y) cell) => locked.Contains(cell);

        // ---- 플레이어 예외 ----

        /// <summary>잠겨 있어도 <b>플레이어 조작만은</b> 통과시킨다.</summary>
        public bool AllowPlayer((int x, int y) cell) => playerAllowed.Add(cell);

        public bool DisallowPlayer((int x, int y) cell) => playerAllowed.Remove(cell);

        public bool PlayerAllowed((int x, int y) cell) => playerAllowed.Contains(cell);

        // ---- 소유권 ----

        /// <summary>이 칸의 잠금 주인이 나로 바뀌었다고 표시한다.</summary>
        public bool TakeOwnership((int x, int y) cell) => owned.Add(cell);

        public bool DropOwnership((int x, int y) cell) => owned.Remove(cell);

        /// <summary>
        /// 다른 처리가 이 칸의 <b>소유권을 가져갔는지</b>.
        /// 그렇다면 내가 건 잠금이 아니므로 풀지 않고 두고 간다.
        /// </summary>
        public bool OwnedByOther((int x, int y) cell) => owned.Contains(cell);

        // ---- 전용: 아무도 못 건드린다 ----

        /// <summary>연출이 도는 동안 이 칸들을 아무도 못 건드리게 잡아둔다.</summary>
        public void ClaimExclusive(IReadOnlyList<(int x, int y)> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                locked.Add(cells[i]);
                owned.Add(cells[i]);
                playerAllowed.Remove(cells[i]);
            }
        }

        /// <summary>
        /// 전용 잠금을 놓는다.
        /// <b>반드시 finally 에서 부른다</b> - 중간에 끊겨 잠금이 남으면 그 자리가
        /// 영영 낙하·리필에서 빠진다(2026-08-30 빈 칸 버그에서 세운 규칙).
        ///
        /// ⚠ <b>ClaimExclusive 와 짝이 안 맞는다.</b> 잠글 때는 "조작 허용" 표시를 빼지만
        /// 풀 때는 도로 넣지 않는다 - 처리가 끝난 칸은 대개 비어서 곧 낙하·리필이 가져갈
        /// 자리라, 그때 조작 허용을 되살리면 아직 안 채워진 칸을 집을 수 있게 된다.
        /// </summary>
        public void ReleaseExclusive(IReadOnlyList<(int x, int y)> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                locked.Remove(cells[i]);
                owned.Remove(cells[i]);
            }
        }

        // ---- 안착: 자동 처리만 막고 손은 연다 ----

        /// <summary><b>데이터는 확정됐고 연출만 남은</b> 칸으로 표시한다.</summary>
        public void ClaimSettling(IEnumerable<(int x, int y)> cells)
        {
            foreach (var cell in cells)
            {
                locked.Add(cell);
                playerAllowed.Add(cell);
            }
        }

        /// <summary>칸 하나짜리. 리필 스폰처럼 한 번에 하나씩 표시할 때 쓴다.</summary>
        public void ClaimSettling((int x, int y) cell)
        {
            locked.Add(cell);
            playerAllowed.Add(cell);
        }

        public void ReleaseSettling((int x, int y) cell)
        {
            locked.Remove(cell);
            playerAllowed.Remove(cell);
        }

        // ---- 묻기 ----

        /// <summary>
        /// 이 칸의 <b>데이터가 아직 확정되지 않았는지</b>.
        /// 잠겨 있더라도 플레이어 조작이 허용된 칸(=안착 잠금)이면 확정된 것으로 본다 -
        /// 변환 스킬·박스 십자변환이 쓰는 기준과 같다.
        /// </summary>
        public bool IsUnsettled((int x, int y) cell)
            => locked.Contains(cell) && !playerAllowed.Contains(cell);

        /// <summary>확정되지 않은 칸을 전부 담는다. 넘긴 쪽을 비우지 않으니 부르는 쪽이 비운다.</summary>
        public void CollectUnsettled(ISet<(int x, int y)> into)
        {
            foreach (var cell in locked)
            {
                if (!playerAllowed.Contains(cell))
                    into.Add(cell);
            }
        }
    }
}
