using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.Board
{
    /// <summary>
    /// 배틀 시작 시 초기 보드를 생성한다.
    /// 목표: 팔레트(보통 6색)로 무작위 배치하되, 배치 직후 4개 이상 연결된 그룹이 존재하면 안 됨.
    /// </summary>
    public static class BoardGenerator
    {
        private const int MaxReshuffleAttempts = 50; // 무한루프 방지용 상한

        /// <summary>
        /// paletteSize: 이번 배틀에서 쓸 색상 개수 (BattleSetup.BuildPalette 결과의 Count, 보통 6)
        /// </summary>
        public static BoardData GenerateInitialBoard(int width, int height, int paletteSize, System.Random rng)
        {
            var board = new BoardData(width, height);

            // 1단계: 지역적 매치 방지를 적용하며 한 칸씩 채움 (왼쪽 2칸, 아래 2칸이 같은 색이면 회피)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int panelIndex = PickIndexAvoidingLocalMatch(board, x, y, paletteSize, rng);
                    board.Set(x, y, new Cell { kind = CellKind.Normal, panelIndex = panelIndex });
                }
            }

            // 2단계: 혹시 남아있는 4개 이상 연결 그룹(대각선 형태 등, 지역 검사로 못 막은 케이스)을 전수 검사 후 재배치
            ResolveRemainingMatches(board, paletteSize, rng);

            return board;
        }

        private static int PickIndexAvoidingLocalMatch(BoardData board, int x, int y, int paletteSize, System.Random rng)
        {
            for (int attempt = 0; attempt < MaxReshuffleAttempts; attempt++)
            {
                int candidate = rng.Next(paletteSize);

                bool matchesLeft = x >= 2
                    && board.Get(x - 1, y).panelIndex == candidate
                    && board.Get(x - 2, y).panelIndex == candidate;

                bool matchesBelow = y >= 2
                    && board.Get(x, y - 1).panelIndex == candidate
                    && board.Get(x, y - 2).panelIndex == candidate;

                if (!matchesLeft && !matchesBelow)
                    return candidate;
            }

            // 상한 도달 시 그냥 마지막 후보 사용 (극단적으로 팔레트가 작을 때 대비)
            return rng.Next(paletteSize);
        }

        /// <summary>
        /// 전체 보드를 대상으로 ConnectionFinder를 돌려서 4개 이상 연결된 그룹이 있으면
        /// 그룹의 일부 셀을 다른 색으로 재배치하는 과정을 매치가 없어질 때까지 반복.
        /// </summary>
        private static void ResolveRemainingMatches(BoardData board, int paletteSize, System.Random rng)
        {
            for (int pass = 0; pass < MaxReshuffleAttempts; pass++)
            {
                var offendingGroup = FindAnyMatchGroup(board);
                if (offendingGroup == null)
                    return; // 매치 없음, 종료

                // 그룹 내 셀 중 절반 정도만 재배치해서 자연스럽게 깨뜨림
                foreach (var (x, y) in offendingGroup)
                {
                    int original = board.Get(x, y).panelIndex;
                    int replacement;
                    do
                    {
                        replacement = rng.Next(paletteSize);
                    } while (replacement == original && paletteSize > 1);

                    board.Set(x, y, new Cell { kind = CellKind.Normal, panelIndex = replacement });
                }
            }
        }

        private static List<(int x, int y)> FindAnyMatchGroup(BoardData board)
        {
            var visited = new bool[board.width, board.height];

            for (int y = 0; y < board.height; y++)
            {
                for (int x = 0; x < board.width; x++)
                {
                    if (visited[x, y])
                        continue;

                    var group = ConnectionFinder.FindConnectedGroup(board, x, y);
                    foreach (var (gx, gy) in group)
                        visited[gx, gy] = true;

                    if (group.Count >= ConnectionFinder.MinRemoveCount)
                        return group;
                }
            }

            return null;
        }
    }
}
