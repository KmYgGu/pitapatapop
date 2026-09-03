using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JojoPuzzle.Board;
using JojoPuzzle.Core;

namespace JojoPuzzle.View
{
    /// <summary>
    /// <b>판이 스스로 사는 일</b> - 낙하 → 리필 → 우연히 생긴 매치 재스캔을,
    /// 더 이상 매치가 없을 때까지 되풀이한다.
    ///
    /// <b>입력과 상관없이 돈다.</b> 플레이어가 손을 놓아도, 스킬이 칸을 지워도,
    /// 상자가 터져도 빈 칸이 생기면 이게 굴러가서 판을 다시 채운다.
    /// 그래서 입력 컨트롤러가 아니라 따로 산다(2026-09-03에 옮김).
    ///
    /// ⚠ <b>이게 여럿 동시에 돈다.</b> 매치 하나마다 하나씩 띄워지므로, 상태를 필드에
    /// 두면 서로 덮어쓴다 - 한 번의 실행에 필요한 것은 전부 <see cref="Run"/> 안의
    /// 지역 변수로 둔다. 필드는 바뀌지 않는 협력자들뿐이다.
    /// </summary>
    public sealed class BoardCascade
    {
        /// <summary>리필이 없을 때 돌려줄 빈 목록. 마무리 처리 중에는 새 조각을 안 채운다.</summary>
        private static readonly List<(int x, int y, Cell cell)> EmptyRefill
            = new List<(int x, int y, Cell cell)>();

        private readonly BoardManager boardManager;
        private readonly BoardView boardView;
        private readonly BoardCellLocks cellLock;
        private readonly ICascadeHost host;

        /// <summary>리필된 조각이 제자리에 앉기까지의 시간(초). 인스펙터 값이라 주인이 들고 있다.</summary>
        private readonly float refillSettleDuration;

        /// <summary>채우자마자 매치가 되는 색을 피할지. 마찬가지로 인스펙터 값.</summary>
        private readonly bool refillAvoidsImmediateMatch;

        public BoardCascade(BoardManager boardManager, BoardView boardView,
                            BoardCellLocks cellLock, ICascadeHost host,
                            float refillSettleDuration, bool refillAvoidsImmediateMatch)
        {
            this.boardManager = boardManager;
            this.boardView = boardView;
            this.cellLock = cellLock;
            this.host = host;
            this.refillSettleDuration = refillSettleDuration;
            this.refillAvoidsImmediateMatch = refillAvoidsImmediateMatch;
        }

        /// <summary>
        /// 중력 낙하 → 리필 → 우연히 생긴 매치 재스캔을, 더 이상 매치가 없을 때까지 반복.
        /// 잠긴 칸(다른 작업이 쓰는 중)은 구멍처럼 고정 취급해서 건드리지 않음.
        ///
        /// 낙하/리필로 움직이거나 새로 스폰된 칸들은 그 애니메이션이 끝날 때까지 <b>안착 잠금</b>을
        /// 걸어서 다른 자동 처리(매치·스탠드업 합체 등)가 건드리지 못하게 한다 - 안 그러면
        /// 아직 목적지로 이동 중인 뷰를 동시에 진행되는 다른 시스템이 다른 위치/크기로 옮기려
        /// 들면서(예: 정사각형 합체 호스트로 뽑힘) 화면이 찢어지듯 겹쳐 보이는 버그가 생겼었음.
        /// <b>손은 열어 둔다</b> - 데이터는 이미 확정됐고 남은 건 0.25초짜리 연출뿐이라,
        /// 막아두면 스탠드업 타임에 빠르게 움직일 때 캐스케이드 한 단계마다 손이 묶인다.
        /// </summary>
        /// <param name="protectOnFirstPass">
        /// 방금 플레이어가 드래그로 놓은 자리(있다면) - 같은 열에서 원래 있던 자리가 비어서 생긴
        /// 빈틈 때문에, 방금 놓은 조각 자신이 이번 낙하에 휩쓸려 한 칸 아래로 밀려 내려가는 걸
        /// 막기 위해 <b>첫 번째 낙하 패스에서만</b> ApplyGravity 의 pinnedCell 로 넘긴다
        /// (잠긴 칸 같은 "벽"이 아니라, 그 위 조각은 자연스럽게 통과해서 내려올 수 있는 고정 -
        /// 그래야 빈 칸에 새 조각이 뜬금없이 끼어드는 대신, 위에 있던 조각이 실제로 내려와서
        /// 채우고 빈 자리는 열 맨 위로 밀려 올라감). 두 번째 패스(캐스케이드)부터는 더 이상
        /// 고정하지 않음 - 이후에 생기는 빈 자리는 정상적으로 그 위 조각들이 채워야 자연스러움.
        /// </param>
        /// <param name="initialColumns">
        /// 이 낙하/리필이 처리할 열 번호 목록. null이면 보드 전체(기존 동작) - 캐스케이드 스캔처럼
        /// 특정 이벤트에 묶이지 않은 경우에만 null을 쓴다. 특정 매치/이동이 원인이라면 그 매치가
        /// 실제로 걸친 열만 넘겨서, 무관한 다른 열에서 진행 중인 다른 매치의 접기 연출 위로 이
        /// 매치의 리필된 조각이 끼어들어 겹쳐 보이는 문제를 막는다. 캐스케이드로 매치가 다른
        /// 열까지 번지면 그 열도 자동으로 추가돼서 다음 패스부터 포함된다(안 그러면 그 열에
        /// 영구히 빈 칸이 남음).
        /// </param>
        public IEnumerator Run((int x, int y)? protectOnFirstPass = null, ISet<int> initialColumns = null)
        {
            bool isFirstPass = true;
            HashSet<int> activeColumns = initialColumns != null ? new HashSet<int>(initialColumns) : null;

            // 한꺼번에 띄운 매치들을 기다릴 목록. 이 코루틴은 동시에 여러 개가 돌 수 있으므로
            // 반드시 호출마다 따로 가져야 한다(공유 필드로 두면 서로 덮어쓴다).
            var resolvingGroups = new List<Coroutine>();

            while (true)
            {
                // <b>승패가 확정되면 판을 세운다</b>(2026-08-28 사용자 결정). 결과 화면이 불투명하게
                // 덮고 있어서 보이지도 않는데 낙하·리필·캐스케이드가 계속 돌면 그만큼 발열만 난다.
                // 멈추지 않고 <b>빠져나온다</b> - 대기로 두면 결과 화면 내내 코루틴이 매 프레임 깨어난다.
                if (host.IsBoardStopped)
                    yield break;

                // 스탠드업 종료 연출 중에는 낙하·리필도 멈춘다 - 불꽃이 리더에게 모이는 동안
                // 그 아래에서 조각이 우수수 떨어지면 시선이 갈린다.
                // 개시 배너와 대사창은 여기 해당하지 않는다: 그때도 조각은 계속 떨어지고 채워지되,
                // 그러다 매치가 성립하면 그 처리만 매치 쪽에서 미뤄진다.
                while (host.IsFallFrozen)
                    yield return null;

                (int x, int y)? pinnedCell = null;
                if (isFirstPass && protectOnFirstPass.HasValue
                    && boardManager.Board.Get(protectOnFirstPass.Value.x, protectOnFirstPass.Value.y).kind != CellKind.Empty)
                {
                    pinnedCell = protectOnFirstPass;
                }
                isFirstPass = false;

                // <b>마무리 처리 중에도 낙하는 그대로 둔다</b> - 상자가 터져 빈 칸이 생겼을 때
                // 위 조각이 안 내려오면 판이 어색하게 뜬 채로 남는다. 안 하는 건 리필뿐이다.
                var moves = boardManager.ApplyGravity(cellLock.Blocked, pinnedCell, activeColumns);

                // 떨어지는(그리고 새로 채워지는) 칸은 자동 시스템에는 잠긴 칸이지만 플레이어에게는
                // 열어둔다 - 데이터는 이미 확정됐고 남은 건 연출뿐이라, 접기 연출 중인 칸과 같은 상황.
                var settlingCells = new HashSet<(int x, int y)>();
                foreach (var move in moves)
                    settlingCells.Add((move.x, move.toY));
                cellLock.ClaimSettling(settlingCells);

                yield return boardView.AnimateGravityMoves(moves);

                // <b>마무리 처리 중에는 새 조각을 채우지 않는다</b>(2026-08-25 사용자 기획).
                // 남은 조각을 전부 데미지로 바꾸고 끝내는 구간이라, 계속 채워지면 끝나지 않는다.
                var spawned = host.IsFinisherRunning
                    ? EmptyRefill
                    : boardManager.RefillEmptyCells(activeColumns, cellLock.Blocked, refillSettleDuration,
                        refillAvoidsImmediateMatch);
                foreach (var (sx, sy, _) in spawned)
                {
                    if (settlingCells.Add((sx, sy)))
                        cellLock.ClaimSettling((sx, sy));
                }

                yield return boardView.AnimateRefill(spawned);

                // 낙하/리필 애니메이션이 완전히 끝난 지금에서야 잠금을 풀고 재판정.
                foreach (var cell in settlingCells)
                {
                    // 플레이어가 낙하 도중 집어서 지금도 들고 있는 칸이거나, 방금 놓아서 그 처리가
                    // 소유 중인 칸이면 내 잠금이 아니다 - 손대지 않고 주인이 풀도록 둔다.
                    if (host.IsHeldByPlayer(cell))
                        continue;
                    if (cellLock.OwnedByOther(cell))
                        continue;

                    cellLock.ReleaseSettling(cell);
                }

                var cascadeGroups = boardManager.ScanBoardForMatches(cellLock.Blocked,
                    includeStandHeld: host.IsStandUpTimeActive);
                if (cascadeGroups.Count == 0)
                    yield break; // 더 이상 매치 없음 - 종료

                bool processedAny = false;
                resolvingGroups.Clear();
                foreach (var group in cascadeGroups)
                {
                    bool overlapsLocked = false;
                    foreach (var cell in group.cells)
                    {
                        if (cellLock.Blocked.Contains(cell))
                        {
                            overlapsLocked = true;
                            break;
                        }
                    }
                    if (overlapsLocked)
                        continue; // 다른 코루틴이 이미 처리 중인 칸과 겹침 - 이번엔 건너뛰고 다음 기회에

                    processedAny = true;

                    // 이 캐스케이드 매치가 걸친 열도 다음 낙하 패스에 포함시킴(범위를 좁혀뒀다고
                    // 캐스케이드가 번진 열을 빼먹으면 그 열에 영구히 빈 칸이 남게 됨)
                    if (activeColumns != null)
                    {
                        foreach (var cell in group.cells)
                            activeColumns.Add(cell.x);
                    }

                    // 캐스케이드 매치는 플레이어가 놓은 위치가 없으므로, 모일 자리를 무작위로 선정
                    var anchorCell = group.cells[Random.Range(0, group.cells.Count)];

                    // 이번 스캔에서 나온 무리들은 서로 겹치지 않는 별개 덩어리이므로 <b>한꺼번에</b>
                    // 처리한다. 예전엔 하나씩 끝날 때까지 기다렸는데, 한 번에 여러 곳이 터지는
                    // 상황(대량 낙하 뒤)에서 접기 연출이 줄줄이 이어지며 리필이 그만큼 늦어졌다.
                    // 매치 처리는 시작하자마자(첫 yield 전에) 자기 칸을 잠그므로,
                    // 먼저 띄운 것의 잠금이 다음 것의 겹침 판정에 곧바로 반영된다.
                    resolvingGroups.Add(host.ResolveMatch(group, anchorCell.x, anchorCell.y));

                    // TODO: 연쇄로 인한 데미지/콤보 이벤트도 여기서 발행 예정
                }

                // 이번에 띄운 매치들이 전부 끝나야 다음 낙하로 넘어간다(낙하가 연출 위로 끼어들면
                // 조각이 겹쳐 보인다). 다만 서로는 동시에 진행된다.
                for (int i = 0; i < resolvingGroups.Count; i++)
                    yield return resolvingGroups[i];
                resolvingGroups.Clear();

                if (!processedAny)
                    yield return null; // 전부 다른 곳에서 처리 중이었으면 한 프레임 쉬고 다시 스캔

                // 방금 제거한 그룹들 때문에 다시 빈 칸이 생겼으니 while 루프를 돌아 다시 낙하부터 반복
            }
        }
    }
}
