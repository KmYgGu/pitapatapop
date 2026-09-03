using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.Board
{
    public static class ConnectionFinder
    {
        // 상하좌우 4방향
        private static readonly (int dx, int dy)[] Directions =
        {
            (0, 1), (0, -1), (1, 0), (-1, 0)
        };

        public const int MinRemoveCount = 4; // 이 이상이면 제거 가능
        public const int BoxCreateCount = 6; // 이 이상 연결하면 박스 생성

        // BFS 작업용 공용 버퍼. 이 클래스의 탐색 메서드들은 전부 중간에 yield 없이 동기적으로
        // 끝나고 서로 중첩 호출되지 않으므로(BoardManager.ScanBoardForMatches도 순차 호출) 하나를
        // 돌려써도 안전하다. 예전엔 호출마다 visited 배열과 Queue를 새로 만들었는데,
        // ScanBoardForMatches가 색 영역 수만큼 이 메서드들을 반복 호출하고 그 스캔이 다시
        // 캐스케이드 루프 안에서 돌기 때문에 한 번의 연쇄 매치에 수십 세트가 GC로 갔었음.
        //
        // 주의: 반환하는 결과 List는 호출부가 코루틴 프레임 너머까지 들고 있으므로(ConnectionResult.cells)
        // 절대 공유 버퍼로 바꾸면 안 된다 - 그건 지금처럼 매번 새로 만들어야 한다.
        private static bool[] visitedBuffer;
        private static readonly Queue<(int x, int y)> searchQueue = new Queue<(int x, int y)>();

        /// <summary>
        /// 보드 크기에 맞는 visited 버퍼를 준비(필요하면 키우고, 아니면 앞부분만 지워서 재사용).
        /// 2차원 배열 대신 1차원으로 두고 [y * width + x]로 인덱싱한다 - 크기가 바뀌었을 때
        /// 재할당 판단이 단순해지고, 다차원 배열보다 인덱싱 비용도 낮다.
        /// </summary>
        private static bool[] RentVisited(BoardData board)
        {
            int needed = board.width * board.height;

            if (visitedBuffer == null || visitedBuffer.Length < needed)
                visitedBuffer = new bool[needed];
            else
                System.Array.Clear(visitedBuffer, 0, needed);

            return visitedBuffer;
        }

        /// <summary>
        /// 시작 좌표(startX, startY)와 같은 panelIndex를 가진, 인접해서 연결된 모든 셀을 BFS로 수집.
        /// Obstacle/Hole/Empty는 애초에 IsConnectable=false라 탐색 대상에서 자연히 제외됨.
        /// excludedCells: 지금 다른 매치/작업이 이미 점유 중인 칸 - 벽처럼 취급해서 이 그룹에 절대
        /// 포함되지 않게 함. 이게 없으면 두 매치가 동시에 같은 칸을 "내 것"으로 판정해버릴 수 있음.
        /// </summary>
        public static List<(int x, int y)> FindConnectedGroup(BoardData board, int startX, int startY, ISet<(int x, int y)> excludedCells = null)
        {
            var result = new List<(int x, int y)>();
            FillConnectedGroup(board, startX, startY, excludedCells, result);
            return result;
        }

        /// <summary>
        /// FindConnectedGroup과 같은 탐색이지만 결과를 <b>호출부가 준 리스트에 채운다</b>(먼저 비움).
        /// 보드 전체를 훑는 ScanBoardForMatches는 칸마다 이걸 부르는데 대부분의 결과가 "4개 미만이라
        /// 버려지는 그룹"이다 - 그때마다 리스트를 새로 만들면 스캔 한 번에 수십 개가 그대로 GC로 간다.
        /// 매치가 성립한 그룹만 호출부가 새 리스트로 복사해 가면 된다.
        /// </summary>
        public static void FillConnectedGroup(BoardData board, int startX, int startY,
            ISet<(int x, int y)> excludedCells, List<(int x, int y)> result)
        {
            result.Clear();

            if (excludedCells != null && excludedCells.Contains((startX, startY)))
                return; // 시작점 자체가 이미 다른 작업에 점유돼 있으면 빈 결과

            var startCell = board.Get(startX, startY);

            if (!startCell.IsConnectable)
                return; // 시작점 자체가 연결 불가능한 셀이면 빈 결과

            int targetIndex = startCell.panelIndex;
            int stride = board.width;
            var visited = RentVisited(board);
            var queue = searchQueue;
            queue.Clear();

            queue.Enqueue((startX, startY));
            visited[startY * stride + startX] = true;

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                result.Add((cx, cy));

                foreach (var (dx, dy) in Directions)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;

                    if (!board.InBounds(nx, ny) || visited[ny * stride + nx])
                        continue;

                    if (excludedCells != null && excludedCells.Contains((nx, ny)))
                        continue; // 다른 작업이 점유 중인 칸은 벽처럼 취급 - 절대 넘어가지 않음

                    var neighbor = board.Get(nx, ny);
                    if (!neighbor.IsConnectable || neighbor.panelIndex != targetIndex)
                        continue;

                    visited[ny * stride + nx] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        /// <summary>
        /// 스탠드업 타임 전용 연결 탐색. 일반 매치(FindConnectedGroup)와 다르게, 같은 색이면
        /// Normal뿐 아니라 StandHeld(이미 고정된 조각)도 통과시켜서 연결을 찾는다.
        /// 반환값엔 StandHeld 칸도 섞여서 나오므로, "새로 합류하는 칸만" 추리는 건 호출부(BoardManager) 책임.
        /// 시작점은 반드시 방금 놓인 Normal 패널이어야 함(StandHeld 칸에서 시작하는 상황은 없음).
        /// </summary>
        public static List<(int x, int y)> FindConnectedGroupThroughStandHeld(BoardData board, int startX, int startY, ISet<(int x, int y)> excludedCells = null)
        {
            var result = new List<(int x, int y)>();
            FillConnectedGroupThroughStandHeld(board, startX, startY, excludedCells, result);
            return result;
        }

        /// <summary>
        /// FindConnectedGroupThroughStandHeld의 버퍼 채우기 판. FillConnectedGroup과 같은 이유로 있다
        /// (스탠드업 중 스캔도 칸마다 불리는데 대부분 버려지는 결과다).
        /// </summary>
        public static void FillConnectedGroupThroughStandHeld(BoardData board, int startX, int startY,
            ISet<(int x, int y)> excludedCells, List<(int x, int y)> result)
        {
            result.Clear();

            if (excludedCells != null && excludedCells.Contains((startX, startY)))
                return;

            var startCell = board.Get(startX, startY);
            if (!startCell.IsConnectable)
                return; // 미안착 조각도 여기서 걸러진다(IsConnectable이 안착까지 함께 본다)

            int targetIndex = startCell.panelIndex;
            int stride = board.width;
            var visited = RentVisited(board);
            var queue = searchQueue;
            queue.Clear();

            queue.Enqueue((startX, startY));
            visited[startY * stride + startX] = true;

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                result.Add((cx, cy));

                foreach (var (dx, dy) in Directions)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;

                    if (!board.InBounds(nx, ny) || visited[ny * stride + nx])
                        continue;

                    if (excludedCells != null && excludedCells.Contains((nx, ny)))
                        continue;

                    var neighbor = board.Get(nx, ny);

                    // 같은 색이면 일반 조각(단, 안착한 것만)과 이미 고정된 StandHeld를 함께 통과시킨다.
                    // StandHeld는 이미 굳은 상태라 안착 여부를 따질 필요가 없다.
                    bool matchesColor = neighbor.panelIndex == targetIndex
                        && (neighbor.IsConnectable || neighbor.kind == CellKind.StandHeld);
                    if (!matchesColor)
                        continue;

                    visited[ny * stride + nx] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        /// <summary>
        /// StandHeld 셀만을 대상으로 한 연결 탐색(같은 색 + 4방향 인접). 시작점이 StandHeld가
        /// 아니면 빈 리스트. 박스 등으로 무리 중 일부가 다른 색으로 바뀐 뒤, 남은 무리가 여전히
        /// MinRemoveCount(매치 성립 기준) 이상을 유지하는지 확인할 때 사용
        /// (BoardManager.ReleaseUndersizedStandHeldGroupsNear 참고).
        /// </summary>
        public static List<(int x, int y)> FindConnectedStandHeldGroup(BoardData board, int startX, int startY)
        {
            var result = new List<(int x, int y)>();
            FillConnectedStandHeldGroup(board, startX, startY, result);
            return result;
        }

        /// <summary>
        /// FindConnectedStandHeldGroup의 버퍼 채우기 판. 다른 Fill~과 같은 이유로 있다 -
        /// 힌트 탐색이 칸마다 부르는데 대부분 버려지는 결과다.
        /// </summary>
        public static void FillConnectedStandHeldGroup(BoardData board, int startX, int startY,
            List<(int x, int y)> result)
        {
            result.Clear();

            var startCell = board.Get(startX, startY);
            if (startCell.kind != CellKind.StandHeld)
                return;

            int targetIndex = startCell.panelIndex;
            int stride = board.width;
            var visited = RentVisited(board);
            var queue = searchQueue;
            queue.Clear();

            queue.Enqueue((startX, startY));
            visited[startY * stride + startX] = true;

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                result.Add((cx, cy));

                foreach (var (dx, dy) in Directions)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;

                    if (!board.InBounds(nx, ny) || visited[ny * stride + nx])
                        continue;

                    var neighbor = board.Get(nx, ny);
                    if (neighbor.kind != CellKind.StandHeld || neighbor.panelIndex != targetIndex)
                        continue;

                    visited[ny * stride + nx] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        /// <summary>
        /// 그 무리에 <b>특수 패널이 아닌 조각</b>이 하나라도 있는지.
        ///
        /// 미스틱의 특수 패널은 <b>스스로 매칭할 수 없다</b>(2026-08-30 사용자 확정) - 2x2 네 칸이면
        /// 그 자체로 이미 <see cref="MinRemoveCount"/> 를 채우기 때문에, 이걸 안 보면 박아놓는 순간
        /// 스스로 터져 사라진다. 일반 조각이 하나라도 붙어야 매치가 성립한다.
        /// </summary>
        public static bool HasPlainPiece(BoardData board, List<(int x, int y)> group)
        {
            if (group == null)
                return false;

            for (int i = 0; i < group.Count; i++)
            {
                if (!board.Get(group[i].x, group[i].y).IsSpecial)
                    return true;
            }

            return false;
        }

        /// <summary>매치가 성립하는 무리인지. 개수와 "특수 패널만은 아닌지"를 함께 본다.</summary>
        public static bool CanRemoveGroup(BoardData board, List<(int x, int y)> group)
            => group != null && group.Count >= MinRemoveCount && HasPlainPiece(board, group);

        /// <summary>
        /// FindConnectedGroupThroughStandHeld을 감싸서 ConnectionResult로 반환.
        /// 스탠드업 타임 중 매치 판정에 사용 - 새로 놓인 패널이 이미 고정된(StandHeld) 같은 색
        /// 무더기에 이어붙는 경우까지 하나의 그룹으로 인식시킴.
        /// </summary>
        public static ConnectionResult EvaluateThroughStandHeld(BoardData board, int x, int y, ISet<(int x, int y)> excludedCells = null)
        {
            var group = FindConnectedGroupThroughStandHeld(board, x, y, excludedCells);
            int panelIndex = group.Count > 0 ? board.Get(group[0].x, group[0].y).panelIndex : -1;
            return new ConnectionResult
            {
                cells = group,
                canRemove = CanRemoveGroup(board, group),
                createsBox = group.Count >= BoxCreateCount,
                panelIndex = panelIndex
            };
        }

        /// <summary>
        /// 특정 좌표를 탭했을 때 제거 가능한 그룹인지 + 박스 생성 조건 충족 여부까지 판단.
        /// </summary>
        public static ConnectionResult Evaluate(BoardData board, int x, int y, ISet<(int x, int y)> excludedCells = null)
        {
            var group = FindConnectedGroup(board, x, y, excludedCells);
            int panelIndex = group.Count > 0 ? board.Get(group[0].x, group[0].y).panelIndex : -1;
            return new ConnectionResult
            {
                cells = group,
                canRemove = CanRemoveGroup(board, group),
                createsBox = group.Count >= BoxCreateCount,
                panelIndex = panelIndex
            };
        }
    }

    public struct ConnectionResult
    {
        public List<(int x, int y)> cells;
        public bool canRemove;
        public bool createsBox;
        public int panelIndex; // 판정 시점에 캡처한 색 - 나중에 커밋할 때 다시 읽지 않고 이 값을 그대로 씀
        public int Count => cells?.Count ?? 0;
    }
}