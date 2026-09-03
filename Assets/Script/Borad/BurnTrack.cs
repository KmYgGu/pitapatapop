using System;
using System.Collections;
using System.Collections.Generic;
using JojoPuzzle.Board;
using JojoPuzzle.Core;

namespace JojoPuzzle.View
{
    /// <summary>
    /// 유나의 <b>버닝 트랙!</b> - 점화 블록을 심고, 조각을 먹이면 그 열을 태운다.
    ///
    /// <code>
    ///   스킬 발동 → PickCells 로 맨 아랫줄에 자리를 고르고 → PlaceRoutine 으로 블록을 박는다
    ///   플레이어가 조각을 블록에 갖다 댐 → IgniteRoutine 이 그 열을 위로 태운다
    /// </code>
    ///
    /// ⭐ <b>어느 열을 태울지는 스킬이 안 정한다.</b> 스킬은 블록만 놓고 물러나고,
    /// 열은 플레이어가 조각을 어디로 미느냐로 정해진다(2026-09-01 사용자 기획).
    ///
    /// <b>MonoBehaviour 가 아니다</b> - 규칙은 <see cref="BoardManager"/>, 연출은
    /// <see cref="BoardView"/> 가 이미 갖고 있어서 여기 남는 건 <b>순서 잡기</b>뿐이다.
    /// 코루틴은 입력 컨트롤러가 자기 것으로 굴린다.
    /// </summary>
    public sealed class BurnTrack
    {
        private readonly BoardManager boardManager;
        private readonly BoardView boardView;
        private readonly BoardCellLocks cellLock;

        /// <summary>패널 한 칸의 전투력을 묻는 것. 강화 배율은 칸이 따로 들고 있다.</summary>
        private readonly Func<int, float> panelCombatPower;

        /// <summary>날것의 전투력을 넘기면 배율을 씌워 적에게 먹이는 것.</summary>
        private readonly Action<float> dealDamage;

        /// <summary>
        /// 자리를 고를 때 피할 칸을 담는 버퍼.
        /// ⭐ <b>자기 것을 따로 갖는다</b> - 예전엔 박스 십자변환의 버퍼를 같이 썼다.
        /// 지금은 그 사이에 기다리는 구간이 없어 안 터졌지만, 한쪽에 <c>yield</c> 가 하나만
        /// 들어가도 상대의 기준이 조용히 바뀐다. 집합 하나 값에 살 만한 안전이다.
        /// </summary>
        private readonly HashSet<(int x, int y)> blockedCells = new HashSet<(int x, int y)>();

        public BurnTrack(BoardManager boardManager, BoardView boardView, BoardCellLocks cellLock,
                         Func<int, float> panelCombatPower, Action<float> dealDamage)
        {
            this.boardManager = boardManager;
            this.boardView = boardView;
            this.cellLock = cellLock;
            this.panelCombatPower = panelCombatPower;
            this.dealDamage = dealDamage;
        }

        /// <summary>
        /// 점화 블록을 놓을 자리를 고른다. <b>스킬이 하는 일은 여기까지다</b> -
        /// 어느 열을 태울지는 플레이어가 조각을 밀어 넣어서 정한다(<see cref="IgniteRoutine"/>).
        /// </summary>
        public void PickCells(int count, PlacementStyle style, List<(int x, int y)> into)
        {
            // 다른 변환 스킬과 같은 기준: 잠긴 칸 전부가 아니라
            // "데이터가 아직 확정되지 않은 칸"만 피한다.
            blockedCells.Clear();
            cellLock.CollectUnsettled(blockedCells);

            boardManager.PickBurnTrackCells(count, blockedCells, style, into);
        }

        /// <summary>골라둔 칸에 점화 블록을 박고 화면을 맞춘다. 구름이 그 칸을 덮은 뒤에 불린다.</summary>
        public IEnumerator PlaceRoutine(IReadOnlyList<(int x, int y)> cells)
        {
            if (cells == null || cells.Count == 0)
                yield break;

            boardManager.MakeBurnTracks(cells);

            // ⭐ <b>일반 조각만 골라 놓던 시절이 아니다</b>(2026-09-03 사용자 확정).
            // 고정 칸·방해블록은 마음껏, 상자와 특수 퍼즐도 판에 다른 자리가 없으면 덮어쓴다
            // (등급은 CellPlacement 가 정한다). 그래서 박고 나면 뒤처리가 필요하다.
            //
            // 고정 칸을 덮었으면 그 무리가 매치 기준 밑으로 줄었을 수 있다 - 더 이상 매치된
            // 상태가 아니므로 무리 전체를 다시 움직일 수 있는 일반 조각으로 풀어준다
            // (박스 십자변환과 같은 처리).
            var released = boardManager.ReleaseUndersizedStandHeldGroupsNear(cells);

            // ⚠ <b>합체를 먼저 깬다.</b> 정사각형은 칸 여러 개가 큰 뷰 하나로 묶여 있어서,
            // 그대로 둔 채 뷰를 갈아 끼우면 커진 호스트 뷰가 그대로 남거나 숨겨진 멤버 뷰가
            // 엉뚱한 칸에 재활용된다(박스 십자변환에서 겪은 그 순서다).
            var affected = new List<(int x, int y)>(cells);
            affected.AddRange(released);
            boardView.BreakStandSquareMergesOverlapping(affected);

            // 고정이 풀린 칸은 다시 평범한 조각이니 스탠드업 전용 아이콘도 되돌린다.
            boardView.RestoreDefaultLook(released);

            // 남은 칸들로 여전히 정사각형이 성립하면 다시 맞춰 준다.
            boardView.RefreshStandUpSquareMerges();

            boardView.ApplyCrossConversion(cells);

            // 특수 퍼즐을 덮었으면 남은 횟수 표시(룬)를 데이터에 다시 맞춘다.
            boardView.RefreshSpecialLook();

            yield return null;   // 뷰 갱신이 한 프레임 반영되도록
        }

        /// <summary>
        /// 점화 블록에 조각이 닿았을 때 - <b>그 열을 블록이 선 행부터 위로</b>
        /// 통째로 태우고, "<b>먹인 조각 하나의 전투력 x 지워진 조각 갯수</b>"만큼
        /// 적을 때린다(2026-09-01 사용자 확정).
        ///
        /// 먹인 조각은 사라지지 않고 <b>맨 위까지 한 칸씩 뛰어오른다</b> - 닿는 자리마다
        /// 그 칸이 뿅 사라지고, 끝에 닿으면 자기도 사라진다(BoardView.AnimateBurnRise).
        /// 순식간에 끝나면 뭐가 일어난 건지 안 보인다는 사용자 지적으로 넣은 연출이다.
        ///
        /// 구멍만 빼고 <b>전부</b> 지워진다 - 방해블록도 상자도 고정 칸도 미스틱의
        /// 특수 퍼즐도. 먹인 조각과 점화 블록 자신도 같이 사라진다.
        ///
        /// <b>버퍼를 돌려 쓰지 않는다</b> - 중간에 기다리는 구간이 있어서, 그 사이에
        /// 또 한 번 발동하면 공유 버퍼가 덮여 잠금을 푸는 finally 가 엉뚱한 칸을 푸게 된다.
        /// </summary>
        /// <param name="burnedColumnsOut">
        /// 실제로 탔을 때만 그 열들이 담긴다. <b>비어 있으면 아무 일도 안 일어난 것</b>이라
        /// 부르는 쪽이 낙하를 굴릴지 말지 이걸로 정한다 - 예전에 <c>yield break</c> 로
        /// 낙하를 건너뛰던 갈림길을 그대로 옮긴 것이다.
        /// </param>
        public IEnumerator IgniteRoutine(int fuelX, int fuelY, int trackX, int trackY,
            PanelView riser, ISet<int> burnedColumnsOut)
        {
            var board = boardManager.Board;
            var fuel = board.Get(fuelX, fuelY);

            // 태울 칸을 <b>먼저</b> 정한다 - 규칙은 데이터 층이 정하고, 연출은 그걸 따라간다.
            var burnable = new HashSet<(int x, int y)>();

            bool valid = fuel.kind == CellKind.Normal && board.Get(trackX, trackY).IsBurnTrack;

            if (valid)
            {
                var column = new List<(int x, int y)>();
                boardManager.CollectBurnColumn(trackX, trackY, column);

                // 데이터가 아직 확정되지 않은 칸은 건드리지 않는다(변환 스킬과 같은 기준).
                for (int i = 0; i < column.Count; i++)
                {
                    if (cellLock.IsUnsettled(column[i]))
                        continue;

                    burnable.Add(column[i]);
                }

                // 점화 블록 자신이 걸러졌다면 통째로 없던 일로 한다 - 블록은 남았는데
                // 먹인 조각만 사라지는 게 제일 억울하다.
                valid = burnable.Contains((trackX, trackY));
            }

            if (!valid)
            {
                // 손을 대는 사이에 판이 바뀌었다. 집었던 조각을 제자리에 돌려놓고 끝낸다.
                if (riser != null)
                {
                    riser.SetHeldOnTop(false);
                    boardView.PlaceView(riser, fuelX, fuelY);
                }
                yield break;
            }

            // 전투력은 <b>지우기 전에</b> 재둔다(이 프로젝트의 되풀이되는 함정).
            // 강화된 조각을 먹이면 그만큼 세게 탄다 - 데미지 무게를 곱하는 자리는 늘 같다.
            float fuelPower = panelCombatPower(fuel.panelIndex) * fuel.DamageWeight;

            // 먹인 조각은 이미 판을 떠났다(뷰는 riser 가 데려간다). 데이터에서 지우고
            // 태울 목록에서도 뺀다 - 안 그러면 지나갈 때 한 번 더 세어진다.
            board.Clear(fuelX, fuelY);
            burnable.Remove((fuelX, fuelY));

            var held = new List<(int x, int y)>(burnable) { (fuelX, fuelY) };

            // 연출이 도는 동안 다른 처리가 이 칸을 가져가지 못하게 잡아둔다.
            cellLock.ClaimExclusive(held);

            int burnedPieces = 1;   // 먹인 조각 자신부터 센다(사용자 확정)

            // ⚠ 합체를 <b>연출 전에</b> 푸는 게 중요하다. 스탠드업 정사각형은 칸 여러 개가
            // 큰 뷰 하나로 묶여 있어서, 그대로 둔 채 한 칸씩 뿅뿅 없앤다가는
            // 묶여 있던 나머지 칸의 뷰가 엉킨 크기로 남는다.
            boardView.BreakStandSquareMergesOverlapping(held);

            try
            {
                // 조각이 블록 자리에서 출발해 맨 위까지 한 칸씩 뛰어오르고, 닿는 자리마다
                // 뿅 사라진다. 마지막엔 그 조각도 같이 사라진다.
                yield return boardView.AnimateBurnRise(riser, trackX, trackY, y =>
                {
                    if (!burnable.Contains((trackX, y)))
                        return false;

                    // 점화 블록은 조각이 아니라 장치라 "지워진 조각 갯수"에 안 들어간다.
                    if (!board.Get(trackX, y).IsBurnTrack)
                        burnedPieces++;

                    board.Clear(trackX, y);
                    return true;
                });

                dealDamage(fuelPower * burnedPieces);

                // 열이 빠지면서 스탠드업 무리가 매치 기준 밑으로 줄었을 수 있다(변환 스킬과 같은 처리).
                var released = boardManager.ReleaseUndersizedStandHeldGroupsNear(held);
                boardView.RestoreDefaultLook(released);
                boardView.RefreshStandUpSquareMerges();
                boardView.RefreshSpecialLook();
            }
            finally
            {
                cellLock.ReleaseExclusive(held);
            }

            burnedColumnsOut?.Add(trackX);
            burnedColumnsOut?.Add(fuelX);
        }
    }
}
