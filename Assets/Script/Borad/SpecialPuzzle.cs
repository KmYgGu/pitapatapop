using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JojoPuzzle.Board;
using JojoPuzzle.Core;

namespace JojoPuzzle.View
{
    /// <summary>
    /// <b>미스틱의 특수 퍼즐</b> - 2x2 뭉치 하나가 판 위를 옮겨 다닌다.
    ///
    /// <code>
    ///   스킬 발동 → 온전한 자리를 찾아 → 심는다
    ///   그 뭉치가 매치에 걸리면 → 통째로 접히고 → 남은 횟수가 있으면 다른 자리에 다시 난다
    /// </code>
    ///
    /// ⭐ <b>자리는 완전 무작위다</b>(사용자 확정) - 운이 나쁘면 거의 제자리에 다시 날 수도 있다.
    ///
    /// <b>MonoBehaviour 가 아니다.</b> 규칙은 <see cref="BoardManager"/>, 연출은
    /// <see cref="BoardView"/> 가 갖고 있어서 여기 남는 건 순서 잡기와 자리 고르기뿐이다.
    /// </summary>
    public sealed class SpecialPuzzle
    {
        private readonly BoardManager boardManager;
        private readonly BoardView boardView;
        private readonly BoardCellLocks cellLock;

        /// <summary>패널 한 칸의 전투력을 묻는 것.</summary>
        private readonly Func<int, float> panelCombatPower;

        /// <summary>날것의 전투력을 넘기면 배율을 씌워 적에게 먹이는 것.</summary>
        private readonly Action<float> dealDamage;

        /// <summary>
        /// 그 열들을 다시 굴려 달라는 부탁.
        /// ⚠ <b>기다리지 않는다</b> - 기다리면 교착이 난다(스킬 연출은 이 코루틴을 기다리고,
        /// 이건 캐스케이드를 기다리는데, 그 안의 매치 처리는 화면 암전이 풀리길 기다리고,
        /// 암전은 스킬 연출이 끝나야 풀린다).
        /// </summary>
        private readonly Action<ISet<int>> requestCascade;

        /// <summary>접힌 뒤 새 뭉치가 나타나기까지 쉬는 시간(초). 인스펙터 값이라 주인이 들고 있다.</summary>
        private readonly float relocateDelay;

        /// <summary>
        /// 한 변 크기. 스킬 애셋이 정하지만 <b>이동은 스킬이 끝난 뒤에도 일어나므로</b>
        /// 심을 때 받아 적어 둔다.
        /// </summary>
        private int squareSize = 2;

        /// <summary>
        /// 자리를 고르는 성향. 한 변 크기와 같은 이유로 <b>심을 때 받아 적어 둔다</b> -
        /// 다시 나는 건 스킬이 끝난 뒤에도 일어나기 때문이다.
        /// </summary>
        private PlacementStyle squareStyle = PlacementStyle.Careful;

        // 버퍼들. 호출마다 새로 만들지 않고 돌려쓴다.
        private readonly List<(int x, int y)> targets = new List<(int x, int y)>();
        private readonly List<(int x, int y)> squareBuffer = new List<(int x, int y)>();
        private readonly List<(int x, int y)> origins = new List<(int x, int y)>();

        /// <summary>
        /// 자리가 날 때까지 몇 프레임까지 기다릴지. 낙하 한 번이 0.25초 남짓이라 넉넉하다.
        /// </summary>
        private const int PlaceableSquareAttempts = 120;

        public SpecialPuzzle(BoardManager boardManager, BoardView boardView, BoardCellLocks cellLock,
                             Func<int, float> panelCombatPower, Action<float> dealDamage,
                             Action<ISet<int>> requestCascade, float relocateDelay)
        {
            this.boardManager = boardManager;
            this.boardView = boardView;
            this.cellLock = cellLock;
            this.panelCombatPower = panelCombatPower;
            this.dealDamage = dealDamage;
            this.requestCascade = requestCascade;
            this.relocateDelay = relocateDelay;
        }

        /// <summary>
        /// <b>온전한</b> size x size 구역 하나를 무작위로 고른다. 자리가 날 때까지 몇 프레임 기다린다.
        ///
        /// <b>⚠ 왜 기다리는가</b>(2026-08-30 사용자 신고): 고른 자리에 <b>지금 낙하·리필이 진행 중인
        /// 칸</b>이 끼면 그 칸은 건너뛰어져서 <b>2x2 가 조각난 채로</b> 생긴다. 자리를 고를 때부터
        /// 그런 칸을 피하고, 판이 통째로 굴러가는 중이면 잠깐 기다렸다 다시 본다 - 낙하는 금방 끝난다.
        ///
        /// 끝내 자리가 안 나면 <paramref name="into"/> 를 비운 채로 끝낸다(부르는 쪽이 포기한다).
        /// </summary>
        public IEnumerator WaitForPlaceableSquare(int size, PlacementStyle style, List<(int x, int y)> into)
        {
            squareSize = Mathf.Max(1, size);
            squareStyle = style;

            for (int attempt = 0; attempt < PlaceableSquareAttempts; attempt++)
            {
                if (TryPickPlaceableSquare(size, style, into))
                    yield break;

                yield return null;
            }

            into.Clear();
            Debug.LogWarning("[SpecialPuzzle] 특수 퍼즐을 놓을 " + size + "x" + size +
                             " 자리를 못 찾았습니다 - 판이 계속 굴러가는 중입니다.");
        }

        /// <summary>
        /// 자리가 <b>통째로</b> 비어 있는 구역을 하나 고른다. 6x6 이면 후보가 25개뿐이라
        /// 전부 훑어 고르는 게 무작위로 다시 뽑는 것보다 확실하다(자리가 몇 개 없을 때 특히).
        /// </summary>
        private bool TryPickPlaceableSquare(int size, PlacementStyle style, List<(int x, int y)> into)
        {
            into.Clear();

            var board = boardManager != null ? boardManager.Board : null;
            if (board == null || size <= 0)
                return false;

            size = Mathf.Min(size, Mathf.Min(board.width, board.height));

            // ⭐ <b>덜 아까운 구역부터</b>(2026-09-03 사용자 정의: 자기 구역 → 퍼즐 우선순위).
            // 일반 조각만 있는 자리가 있으면 거기로, 없으면 큐브, 그마저 없으면 특수 블록 위로.
            var best = PlacementCost.Never;
            int bestNewest = int.MaxValue;
            origins.Clear();

            for (int oy = 0; oy <= board.height - size; oy++)
            {
                for (int ox = 0; ox <= board.width - size; ox++)
                {
                    var cost = SquareCost(ox, oy, size);
                    if (cost == PlacementCost.Never)
                        continue;

                    // ⭐ 아랑곳하지 않는 성향(미스틱)은 등급을 안 본다 - 놓을 수 있는 자리면
                    // 큐브 위든 남의 특수 블록 위든 똑같이 보고 무작위로 고른다.
                    if (style == PlacementStyle.Reckless)
                    {
                        origins.Add((ox, oy));
                        continue;
                    }

                    // 특수 블록 위에 놓아야 하는 구역끼리는 <b>가장 새것이 덜 새것인</b> 쪽을 고른다 -
                    // 방금 소환된 블록을 부수지 않으려는 것이다. 다른 등급은 이 값이 0이라 영향이 없다.
                    int newest = cost == PlacementCost.Special ? SquareNewestSpecial(ox, oy, size) : 0;

                    if (cost < best || (cost == best && newest < bestNewest))
                    {
                        best = cost;
                        bestNewest = newest;
                        origins.Clear();
                    }

                    if (cost == best && newest == bestNewest)
                        origins.Add((ox, oy));
                }
            }

            if (origins.Count == 0)
                return false;

            var origin = origins[UnityEngine.Random.Range(0, origins.Count)];
            for (int dy = 0; dy < size; dy++)
            {
                for (int dx = 0; dx < size; dx++)
                    into.Add((origin.x + dx, origin.y + dy));
            }

            return true;
        }

        /// <summary>
        /// 그 구역 네 칸이 전부 지금 놓을 수 있는 자리인지.
        /// <b>다른 처리가 쥐고 있는 칸은 하나라도 있으면 안 된다</b> - 그게 조각난 2x2 의 원인이다.
        /// </summary>
        private PlacementCost SquareCost(int originX, int originY, int size)
        {
            var worst = PlacementCost.Free;

            for (int dy = 0; dy < size; dy++)
            {
                for (int dx = 0; dx < size; dx++)
                {
                    var cell = (x: originX + dx, y: originY + dy);

                    // 낙하·리필·접기가 쥔 칸. 안착만 남은 칸도 피한다 - 데이터는 확정됐어도
                    // 그 위로 조각이 떨어지는 연출이 아직 도는 중이라, 여기 특수 퍼즐을 놓으면
                    // 떨어지던 조각이 그 위에 내려앉는다.
                    if (cellLock.Blocked.Contains(cell))
                        return PlacementCost.Never;

                    worst = CellPlacement.Worst(worst,
                        CellPlacement.CostOf(boardManager.Board.Get(cell.x, cell.y)));

                    if (worst == PlacementCost.Never)
                        return PlacementCost.Never;
                }
            }

            return worst;
        }

        /// <summary>
        /// 그 구역에서 지워질 특수 블록 중 <b>가장 새것의 번호</b>. 작을수록 덜 아깝다.
        /// 특수 블록끼리는 나중에 소환한 쪽이 우선권을 가지므로, 이 값이 작은 구역을 고른다.
        /// </summary>
        private int SquareNewestSpecial(int originX, int originY, int size)
        {
            int newest = 0;

            for (int dy = 0; dy < size; dy++)
            {
                for (int dx = 0; dx < size; dx++)
                {
                    int order = CellPlacement.SacrificeOrderOf(
                        boardManager.Board.Get(originX + dx, originY + dy));
                    if (order > newest)
                        newest = order;
                }
            }

            return newest;
        }

        /// <summary>
        /// 그 칸들을 특수 패널로 박고 화면을 맞춘다. 진행 중인 다른 처리가 쥔 칸은 건너뛴다
        /// (다른 스킬 변환과 같은 기준).
        /// </summary>
        public IEnumerator MakeRoutine(IEnumerable<(int x, int y)> cells, int panelIndex,
            int matches, List<(int x, int y)> madeOut = null)
        {
            // ⚠⚠ <b>뷰를 떼는 칸과 실제로 바뀌는 칸이 반드시 같아야 한다</b>(2026-09-03 사용자 신고).
            // 예전엔 넘어온 칸을 그대로 다 떼어냈는데, 아래 MakeSpecialPanels 가 못 바꾸는 칸을
            // 건너뛰는 바람에 <b>데이터는 남고 그림만 사라진 칸</b>이 생겼다. 그 자리에 조각을
            // 놓으면 보이지도 않는 점화 블록이 발동했다.
            //
            // <b>잠긴 칸으로는 거르지 않는다</b>(2026-08-30 사용자 신고: 2x2 가 조각나던 버그) -
            // 자리는 부르는 쪽(WaitForPlaceableSquare)이 확인해서 넘긴다. 여기서 거르는 건
            // <b>어떤 수단으로도 못 바꾸는 칸</b>(구멍·판 밖)뿐이고, 그건 아래에서도 똑같이 걸러진다.
            targets.Clear();
            foreach (var cell in cells)
            {
                if (!boardManager.Board.InBounds(cell.x, cell.y))
                    continue;

                if (CellPlacement.CostOf(boardManager.Board.Get(cell.x, cell.y)) == PlacementCost.Never)
                    continue;

                targets.Add(cell);
            }

            // <b>덮어쓰지 않고 걷어낸다</b>(2026-08-30 사용자 기획). 원래 있던 조각은 뿅 사라지고,
            // 그 <b>전투력이 그대로 적에게</b> 간다 - 자리를 빼앗는 값을 치르는 셈이다.
            // 데이터를 비우기 전에 세야 한다(매치가 강화 배율을 미리 세는 것과 같은 함정).
            float removedPower = boardManager.SumCombatPower(targets, panelCombatPower);
            var removedViews = boardView.DetachGroupForCollectEffect(targets);

            var made = boardManager.MakeSpecialPanels(targets, panelIndex, matches);
            madeOut?.Clear();

            if (made.Count == 0)
                yield break;

            madeOut?.AddRange(made);

            // 연출이 도는 동안 다른 처리가 이 칸을 가져가지 못하게 잡아둔다(십자변환과 같은 처리).
            // <b>푸는 건 finally 가 책임진다</b> - 중간에 끊겨 잠금이 남으면 그 자리가 영영
            // 낙하·리필에서 빠진다(2026-08-30 빈 칸 버그에서 세운 규칙).
            cellLock.ClaimExclusive(made);

            try
            {

                // 뿅 사라진다 - 접어 모으지 않고 그 자리에서 없앤다(마무리 처리와 같은 방식).
                if (removedViews.Count > 0 && targets.Count > 0)
                {
                    yield return boardView.RemoveDetachedViews(
                        removedViews, targets[0].x, targets[0].y);
                }

                dealDamage(removedPower);

                // 특수 패널이 들어서면서 스탠드업 무리가 매치 기준 밑으로 줄었으면 풀어준다(변환과 같은 처리).
                var released = boardManager.ReleaseUndersizedStandHeldGroupsNear(made);

                var affected = new List<(int x, int y)>(made);
                affected.AddRange(released);
                boardView.BreakStandSquareMergesOverlapping(affected);
                boardView.RestoreDefaultLook(released);
                boardView.RefreshStandUpSquareMerges();

                boardView.ApplyCrossConversion(made);
                boardView.RefreshSpecialLook();

                yield return null;   // 뷰 갱신이 한 프레임 반영되도록
            }
            finally
            {
                cellLock.ReleaseExclusive(made);
            }

            // <b>심고 나면 매치를 다시 훑어야 한다</b>(2026-08-30 사용자 신고: 옆에 같은 색이
            // 있는데도 매치가 안 됐다). 매치 판정은 <b>무언가가 훑어줘야</b> 일어나는데,
            // 특수 퍼즐은 미안착으로 두지 않아서 안착 틱조차 돌지 않는다 - 여기서 직접 띄운다.
            //
            // <b>기다리지 않는다</b> - 기다리면 교착이 난다(변환 스킬의 같은 자리 주석 참고):
            // 스킬 연출은 이 코루틴을 기다리고, 이 코루틴은 캐스케이드를 기다리는데,
            // 그 안의 매치 처리는 화면 암전이 풀리길 기다리고, 암전은 스킬 연출이 끝나야 풀린다.
            var columns = new HashSet<int>();
            foreach (var cell in made)
                columns.Add(cell.x);

            requestCascade(columns);
        }

        /// <summary>
        /// 특수 퍼즐 뭉치를 <b>다른 자리에 새로 심는다</b>(2026-08-30 사용자 기획).
        /// 남은 횟수가 없으면 아무 일도 하지 않는다 - 그게 이 스킬의 끝이다.
        ///
        /// 자리는 <b>완전 무작위</b>다(사용자 확정) - 운이 나쁘면 거의 제자리에 다시 날 수도 있다.
        /// </summary>
        public IEnumerator RelocateRoutine(int panelIndex, int matchesLeft)
        {
            if (matchesLeft <= 0 || panelIndex < 0)
                yield break;

            if (relocateDelay > 0f)
                yield return new WaitForSeconds(relocateDelay);

            yield return WaitForPlaceableSquare(squareSize, squareStyle, squareBuffer);
            if (squareBuffer.Count == 0)
                yield break;

            yield return MakeRoutine(squareBuffer, panelIndex, matchesLeft);
        }
    }
}
