using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.Board
{
    /// <summary>
    /// 같은 색으로 이어붙은 영역을 정사각형(2x2 이상) 블록들로 쪼갠다. 화면 합체 표시와 스탠드업
    /// 데미지 계산이 <b>같은 함수</b>를 써서 둘이 어긋나지 않게 한다. 순수 좌표 계산이라 뷰/보드
    /// 데이터와 무관하게 단위 테스트 가능.
    ///
    /// 쪼개는 기준은 "가장 큰 것부터"가 아니라 <b>데미지가 가장 커지는 조합</b>이다.
    /// 예전엔 제일 큰 정사각형을 하나 집고 그 칸들을 빼는 그리디였는데, 같은 크기 후보가 여럿일 때
    /// 어느 걸 집느냐에 따라 나머지로 정사각형을 더 만들 수 있는지가 갈린다 - 12칸짜리 무리에서
    /// 2x2를 두 개 만들 수 있는데도 하나만 잡히고 나머지가 전부 낱개로 흩어져서, 플레이어가 실제로
    /// 만든 모양보다 데미지가 줄어드는 일이 있었다.
    /// </summary>
    public static class SquareMergeFinder
    {
        public const int MinSquareSize = 2; // 이보다 작으면(=1칸) 합체 대상 아님, 원래 크기로 둠

        // 비트마스크(ulong) 한 칸당 1비트라 이 이상은 정확 탐색을 못 한다. 보드가 6x8(48칸)이라
        // 실제로는 닿을 일이 없지만, 넘으면 조용히 예전 그리디로 물러선다.
        private const int MaxCellsForExactSearch = 64;

        // 정확 탐색이 훑어볼 상태 수 상한. 현실적인 무리(수십 칸)에서는 한참 못 미치지만,
        // 보드가 한 색으로 가득 차는 극단적인 경우에 프레임이 튀지 않도록 둔 안전장치다.
        private const int SearchStateBudget = 200000;

        // 보너스를 이 배수로 부풀려서 저장하고, "이미 화면에 자리 잡은 정사각형"에만 +1을 준다.
        // 데미지가 완전히 같은 조합이 여럿일 때(예: 2x2 하나를 놓을 자리가 두 군데) 기존 자리를
        // 고르게 하기 위한 것 - 안 그러면 옆에 조각 하나를 붙였을 뿐인데 이미 합쳐져 있던 블록이
        // 엉뚱한 자리에 다시 만들어져 툭 옮겨 다니는 것처럼 보인다.
        // 실제 보너스 차이는 항상 이 배수의 정수배라, 정사각형 개수(최대 12개)만큼의 +1이 쌓여도
        // 진짜 데미지 차이를 뒤집을 수 없다.
        private const int BonusScale = 1000;

        public struct SquareBlock
        {
            public int originX; // 정사각형 좌하단 칸
            public int originY;
            public int size;    // 한 변의 칸 수
        }

        /// <summary>
        /// 이 영역에서 데미지 합이 가장 커지는 정사각형 조합을 찾는다. 결과끼리는 절대 겹치지 않고,
        /// 정사각형에 못 낀 낱개 칸은 결과에 포함되지 않는다(호출부가 "전체 칸 수 - 정사각형 칸 수"로 센다).
        ///
        /// 입력 순서가 달라도 항상 같은 결과가 나오도록 좌표로 정렬한 뒤 계산한다 - 화면 합체와
        /// 데미지 계산이 서로 다른 경로로 같은 무리를 넘겨오기 때문에, 순서에 따라 답이 갈리면
        /// 눈에 보이는 덩어리와 실제 데미지가 어긋난다.
        /// </summary>
        /// <param name="preferred">
        /// 지금 화면에 이미 합쳐져 있는 정사각형들(있다면). 데미지가 똑같은 조합이 여럿일 때
        /// 이쪽을 고르게 해서, 옆에 조각 하나 붙였을 뿐인데 블록이 다른 자리로 옮겨가 보이는 걸 막는다.
        /// 데미지가 더 나은 조합이 있으면 그쪽이 항상 이긴다.
        /// </param>
        // 파라미터를 IEnumerable이 아니라 List로 받는 이유: foreach가 열거자를 박싱하지 않고
        // (인터페이스로 받으면 호출마다 힙 할당), new List<>(cells)도 개수를 미리 알아 정확한 크기로
        // 한 번에 잡는다(IEnumerable이면 늘려가며 여러 번 재할당). 스탠드업 중 매치마다 무리 수만큼
        // 불리는 자리라 이 차이가 쌓인다.
        public static List<SquareBlock> FindSquareBlocks(List<(int x, int y)> cells,
            List<SquareBlock> preferred = null)
        {
            var results = new List<SquareBlock>();

            var list = new List<(int x, int y)>(cells);
            if (list.Count == 0)
                return results;

            list.Sort(CompareCells);

            var set = new HashSet<(int x, int y)>(list);

            if (list.Count > MaxCellsForExactSearch)
                return FindSquareBlocksGreedy(set);

            var candidates = BuildCandidates(list, set, preferred);
            if (candidates.Count == 0)
                return results;

            ulong fullMask = list.Count == 64 ? ulong.MaxValue : (1UL << list.Count) - 1UL;

            var bestValue = new Dictionary<ulong, int>();
            var bestChoice = new Dictionary<ulong, int>();
            int budget = SearchStateBudget;

            Solve(fullMask, candidates, bestValue, bestChoice, ref budget);

            if (budget <= 0)
                return FindSquareBlocksGreedy(set); // 너무 복잡한 모양 - 안전하게 예전 방식으로

            // 고른 조합을 되짚어 실제 정사각형 목록으로 옮긴다.
            ulong current = fullMask;
            while (current != 0UL)
            {
                if (!bestChoice.TryGetValue(current, out int choice))
                    break; // 방어적 처리 - 정상 경로에서는 항상 기록돼 있다

                if (choice < 0)
                {
                    current &= current - 1UL; // 이 칸은 정사각형에 안 넣기로 함 - 최하위 비트만 제거
                    continue;
                }

                var picked = candidates[choice];
                results.Add(new SquareBlock { originX = picked.originX, originY = picked.originY, size = picked.size });
                current &= ~picked.mask;
            }

            return results;
        }

        private static int CompareCells((int x, int y) a, (int x, int y) b)
        {
            if (a.y != b.y)
                return a.y.CompareTo(b.y);
            return a.x.CompareTo(b.x);
        }

        /// <summary>
        /// 이 영역 안에 완전히 들어가는 정사각형을 전부 후보로 모은다. 각 후보는 자기가 덮는 칸들의
        /// 비트마스크와 "이 정사각형을 만들면 낱개로 둘 때보다 데미지가 얼마나 늘어나는지"(bonus)를 들고 있다.
        ///
        /// bonus 계산: 무리 안의 칸은 전부 같은 캐릭터라 전투력이 공통이므로, 전투력을 빼고 비교할 수 있다.
        ///   정사각형 데미지 = 전투력 × 칸수 × 배율,  낱개 데미지 = 전투력 × 칸수
        ///   → 차이 = 전투력 × 칸수 × (배율 - 1)
        /// 100배로 저장된 배율(퍼센트)을 그대로 써서 정수로 다룬다.
        /// </summary>
        private static List<Candidate> BuildCandidates(List<(int x, int y)> list, HashSet<(int x, int y)> set,
            List<SquareBlock> preferred)
        {
            var index = new Dictionary<(int x, int y), int>(list.Count);
            for (int i = 0; i < list.Count; i++)
                index[list[i]] = i;

            HashSet<(int x, int y, int size)> keep = null;
            if (preferred != null)
            {
                keep = new HashSet<(int x, int y, int size)>();
                foreach (var square in preferred)
                    keep.Add((square.originX, square.originY, square.size));
            }

            var candidates = new List<Candidate>();

            foreach (var (ox, oy) in list)
            {
                for (int size = MinSquareSize; size <= StandUpDamageTable.MaxSquareSize; size++)
                {
                    ulong mask = 0UL;
                    bool complete = true;

                    for (int dx = 0; dx < size && complete; dx++)
                    {
                        for (int dy = 0; dy < size; dy++)
                        {
                            if (!index.TryGetValue((ox + dx, oy + dy), out int bit))
                            {
                                complete = false;
                                break;
                            }
                            mask |= 1UL << bit;
                        }
                    }

                    if (!complete)
                        break; // 이 크기가 안 되면 더 큰 크기도 당연히 안 된다

                    int cells = size * size;
                    int bonus = cells * (StandUpDamageTable.GetSizeMultiplierPercent(size) - 100) * BonusScale;

                    // 이미 그 자리에 합쳐져 있던 정사각형이면 아주 작은 가산점 - 동점일 때만 갈린다.
                    if (keep != null && keep.Contains((ox, oy, size)))
                        bonus += 1;

                    candidates.Add(new Candidate
                    {
                        originX = ox,
                        originY = oy,
                        size = size,
                        mask = mask,
                        bonus = bonus
                    });
                }
            }

            return candidates;
        }

        /// <summary>
        /// 남은 칸들(remaining)로 낼 수 있는 최대 보너스를 구한다.
        /// 항상 <b>가장 낮은 비트의 칸</b>부터 처리한다 - "그 칸을 낱개로 둘지, 그 칸을 덮는 정사각형 중
        /// 하나를 쓸지"만 따지면 되므로 같은 조합을 여러 순서로 세지 않게 되고, 남은 칸 집합이 같으면
        /// 결과도 같으니 메모이제이션이 그대로 먹는다.
        /// </summary>
        private static int Solve(ulong remaining, List<Candidate> candidates,
            Dictionary<ulong, int> bestValue, Dictionary<ulong, int> bestChoice, ref int budget)
        {
            if (remaining == 0UL)
                return 0;

            if (bestValue.TryGetValue(remaining, out int cached))
                return cached;

            if (--budget <= 0)
                return 0; // 예산 초과 - 호출부가 그리디로 물러선다

            ulong firstBit = remaining & (~remaining + 1UL); // 최하위 1비트

            // 후보 1) 이 칸은 정사각형에 넣지 않는다(낱개로 둔다)
            int best = Solve(remaining & ~firstBit, candidates, bestValue, bestChoice, ref budget);
            int choice = -1;

            // 후보 2) 이 칸을 덮으면서 남은 칸 안에 완전히 들어가는 정사각형들
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];

                if ((candidate.mask & firstBit) == 0UL)
                    continue;
                if ((candidate.mask & remaining) != candidate.mask)
                    continue; // 이미 다른 정사각형이 가져간 칸이 섞여 있다

                int value = candidate.bonus
                    + Solve(remaining & ~candidate.mask, candidates, bestValue, bestChoice, ref budget);

                if (value > best)
                {
                    best = value;
                    choice = i;
                }
            }

            bestValue[remaining] = best;
            bestChoice[remaining] = choice;
            return best;
        }

        /// <summary>
        /// 예전 방식(가장 큰 정사각형부터 그리디). 정확 탐색이 감당 못 할 만큼 모양이 복잡할 때만
        /// 쓰는 대비책이다 - 최적은 아니지만 결과가 겹치지 않는다는 보장은 동일하다.
        /// </summary>
        private static List<SquareBlock> FindSquareBlocksGreedy(HashSet<(int x, int y)> cells)
        {
            var remaining = new HashSet<(int x, int y)>(cells);
            var results = new List<SquareBlock>();

            if (remaining.Count == 0)
                return results;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var (x, y) in remaining)
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            int w = maxX - minX + 1;
            int h = maxY - minY + 1;

            while (remaining.Count > 0)
            {
                var best = FindLargestSquare(remaining, minX, minY, w, h);
                if (best.size < MinSquareSize)
                    break; // 남은 칸들로는 더 이상 2x2를 못 만듦 - 낱개로 남겨두고 종료

                results.Add(best);

                for (int dx = 0; dx < best.size; dx++)
                    for (int dy = 0; dy < best.size; dy++)
                        remaining.Remove((best.originX + dx, best.originY + dy));
            }

            return results;
        }

        /// <summary>
        /// 표준 "최대 정사각형(maximal square)" DP. dp[gx,gy] = (minX+gx, minY+gy)를 우상단 모서리로 하는,
        /// remaining 안에 완전히 포함되는 가장 큰 정사각형의 한 변 길이.
        /// </summary>
        private static SquareBlock FindLargestSquare(HashSet<(int x, int y)> remaining, int minX, int minY, int w, int h)
        {
            var dp = new int[w, h];
            int bestSize = 0, bestOriginX = 0, bestOriginY = 0;

            for (int gy = 0; gy < h; gy++)
            {
                for (int gx = 0; gx < w; gx++)
                {
                    if (!remaining.Contains((minX + gx, minY + gy)))
                    {
                        dp[gx, gy] = 0;
                        continue;
                    }

                    dp[gx, gy] = (gx == 0 || gy == 0)
                        ? 1
                        : 1 + System.Math.Min(dp[gx - 1, gy], System.Math.Min(dp[gx, gy - 1], dp[gx - 1, gy - 1]));

                    if (dp[gx, gy] > bestSize)
                    {
                        bestSize = dp[gx, gy];
                        bestOriginX = minX + gx - bestSize + 1;
                        bestOriginY = minY + gy - bestSize + 1;
                    }
                }
            }

            return new SquareBlock { originX = bestOriginX, originY = bestOriginY, size = bestSize };
        }

        private struct Candidate
        {
            public int originX;
            public int originY;
            public int size;
            public ulong mask; // 이 정사각형이 덮는 칸들
            public int bonus;  // 낱개로 둘 때보다 늘어나는 데미지(전투력 제외, 퍼센트 스케일)
        }
    }
}
