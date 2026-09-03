using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.Board
{
    /// <summary>
    /// 낙하 결과 하나: 어떤 셀이 어디서 어디로 이동했는지.
    /// 뷰 레이어가 이걸 받아서 트윈 애니메이션 재생.
    /// </summary>
    public struct FallMove
    {
        public int x;
        public int fromY;
        public int toY;
        public Cell cell;
    }

    /// <summary>
    /// 보드 런타임 조작을 담당. MonoBehaviour가 아닌 순수 로직 클래스라 유닛테스트 가능.
    /// 실제 게임에서는 이 클래스를 감싸는 BoardController(MonoBehaviour)가
    /// 애니메이션 타이밍에 맞춰 호출 순서를 제어하게 될 것.
    /// </summary>
    public class BoardManager
    {
        public BoardData Board { get; private set; }
        private readonly int paletteSize;
        private readonly System.Random rng;

        public BoardManager(BoardData board, int paletteSize, System.Random rng)
        {
            Board = board;
            this.paletteSize = paletteSize;
            this.rng = rng;
        }

        /// <summary>
        /// 이동 시도 결과. moved=false면 애초에 이동 자체가 막힌 것(목적지가 오자마/구멍이거나
        /// 좌표가 유효하지 않은 경우)이라 뷰에서 원래 위치로 되돌리는 연출을 해야 함.
        /// </summary>
        public struct MoveOutcome
        {
            public bool moved;
            public ConnectionResult connection; // moved=true일 때만 유효
        }

        /// <summary>
        /// (fromX, fromY)에 있던 패널(또는 박스)을 (toX, toY)로 이동시켜 그 자리를 덮어쓴다(스왑 아님).
        /// 원래 있던 칸(from)은 비게 된다. 이동 성립 후 도착 지점 기준으로 연결 판정만 하고,
        /// 실제 제거/박스 생성은 여기서 하지 않는다 (3D 수집 애니메이션이 끝난 뒤 ResolveGroup으로 별도 반영).
        /// 목적지가 Obstacle/Hole/Box면 일반 이동으로는 덮어쓸 수 없으므로 이동 자체가 무효.
        /// 박스가 이동한 경우 목적지 kind가 Box로 유지되므로 IsConnectable이 false라 매치 판정은 자동으로 안 됨.
        /// lockedCells: 지금 다른 매치가 처리 중인 칸 - 판정 시 벽처럼 취급해서 이 매치에 절대
        /// 끼어들지 못하게 함 (동시에 여러 매치가 처리될 때 서로 칸을 가로채는 것 방지).
        /// includeStandHeld: 스탠드업 타임 중이면 true로 넘겨서, 이미 고정된(StandHeld) 같은 색
        /// 무더기까지 이어서 연결 판정을 함(새로 합류하는 조각만 나중에 회전 처리하면 됨).
        /// </summary>
        public MoveOutcome MoveAndResolve(int fromX, int fromY, int toX, int toY, ISet<(int x, int y)> lockedCells = null, bool includeStandHeld = false)
        {
            if (!Board.InBounds(fromX, fromY) || !Board.InBounds(toX, toY))
                return new MoveOutcome { moved = false };

            if (fromX == toX && fromY == toY)
                return new MoveOutcome { moved = false };

            var sourceCell = Board.Get(fromX, fromY);
            if (!sourceCell.CanBeDragged) // 일반 패널 또는 박스만 직접 이동 가능 (장애물 등은 제외)
                return new MoveOutcome { moved = false };

            var destCell = Board.Get(toX, toY);
            if (destCell.BlocksNormalOverwrite)
                return new MoveOutcome { moved = false }; // 일반 조작으로는 덮어쓰기 불가 (오자마/구멍/박스)

            // 덮어쓰기: 목적지는 이동해온 패널로 교체, 원래 자리는 빈 칸이 됨
            Board.Set(toX, toY, sourceCell);
            Board.Clear(fromX, fromY);

            var result = includeStandHeld
                ? ConnectionFinder.EvaluateThroughStandHeld(Board, toX, toY, lockedCells)
                : ConnectionFinder.Evaluate(Board, toX, toY, lockedCells);

            // 박스를 만들지 않기로 한 구간이면 여기서 한 번만 꺼둔다 - ConnectionResult 는
            // 구조체라 여기서 지운 값이 그대로 호출부로 간다.
            if (!BoxCreationEnabled)
                result.createsBox = false;

            return new MoveOutcome { moved = true, connection = result };
        }

        /// <summary>
        /// 박스를 두 번 탭했을 때: 박스 자신 + 상하좌우 4칸(십자 5칸)을 박스가 만들어졌던 색(panelIndex)의
        /// 일반 패널로 변환한다. 변환 대상 칸이 Obstacle/Hole/다른 Box처럼 덮어쓸 수 없는 칸이거나,
        /// blockedCells에 있으면 건너뜀 (박스 자기 자신은 항상 변환 대상에 포함됨).
        ///
        /// <b>blockedCells는 "잠긴 칸"이 아니라 "보드 데이터가 아직 확정되지 않은 칸"이다.</b>
        /// 이 구분이 중요하다 - 낙하·리필 중인 칸과 접기 연출 중인 칸은 잠겨 있긴 해도 데이터는
        /// 이미 확정돼 있어서(연출만 0.25초 남은 상태) 얼마든지 덮어써도 된다. 예전엔 잠긴 칸을
        /// 전부 건너뛰어서, 매치나 리필이 진행 중인 근처에서 박스를 쓰면 십자가 통째로 빠져
        /// "조각이 제대로 안 생기는" 버그가 있었다. 호출부는 lockedCells에서 interactionAllowedCells
        /// (=데이터 확정, 연출만 남음)를 뺀 집합을 넘긴다.
        ///
        /// 진짜로 막아야 하는 건 아직 커밋 전인 매치의 칸이다 - 그건 데이터상 아직 Normal이라
        /// BlocksNormalOverwrite에 안 걸리는데, 이 변환이 가로채면 색이 뒤바뀐다.
        /// 단, StandHeld(스탠드업 타임 중 고정된 조각 - 정사각형으로 합쳐져 보이는 것 포함)는 예외적으로
        /// 덮어쓸 수 있음: 일반 드래그 이동에서는 여전히 방해블록처럼 막히지만(Cell.BlocksNormalOverwrite
        /// 그대로 적용), 박스는 고정된 무더기를 풀어주는 도구 역할도 겸하도록 의도적으로 여기서만 예외를 둠.
        /// 반환값: 실제로 변환된 좌표 목록 - 뷰 갱신에 사용(이 중 원래 StandHeld였던 칸이 포함돼 있으면,
        /// 그 칸이 속했던 정사각형 합체를 뷰가 알아서 원래 크기로 되돌려야 함).
        /// </summary>
        /// <summary>
        /// 6개 이상 매치가 <b>박스를 만드는지</b>. 평소엔 켜져 있고, <b>게임 종료 처리가 시작되면
        /// 그 판이 끝날 때까지 꺼진다</b>(2026-08-28 사용자 지시로 범위를 넓혔다 - 예전엔 러시
        /// 타임 동안만이었다). 그 뒤에 생긴 박스는 쓸 기회가 없다: 러시가 끝나기 직전의 6매치가
        /// 박스만 남기고 판이 끝나는 게 실제로 지적된 경우다.
        ///
        /// 끄는 곳은 <c>BoardInputController.BeginEndSequence</c>, 되돌리는 곳은
        /// <c>ResetForNewBattle</c> <b>하나뿐</b>이다. 여러 곳에서 되돌리면 그중 하나가
        /// 종료 구간에 다시 켜준다(러시 종료가 그랬다).
        ///
        /// 매치 판정이 지나가는 자리가 둘뿐이라(드롭 판정 / 보드 전체 스캔) 여기서 한 번씩만
        /// 꺼주면 된다. <see cref="ConnectionFinder"/> 는 순수 계산이라 이런 상태를 안 갖는다.
        /// </summary>
        public bool BoxCreationEnabled { get; set; } = true;

        public List<(int x, int y)> ConvertCrossToNormal(int centerX, int centerY, ISet<(int x, int y)> blockedCells = null)
        {
            var converted = new List<(int x, int y)>();

            var boxCell = Board.Get(centerX, centerY);
            if (boxCell.kind != CellKind.Box)
                return converted; // 방어적 처리 - 박스가 아니면 아무 일도 안 함

            int colorIndex = boxCell.panelIndex;
            var targets = new List<(int x, int y)> { (centerX, centerY) };
            foreach (var (dx, dy) in OrthogonalOffsets)
                targets.Add((centerX + dx, centerY + dy));

            foreach (var (x, y) in targets)
            {
                if (!Board.InBounds(x, y))
                    continue;

                bool isCenterItself = x == centerX && y == centerY;

                // 자기 자신(박스) 제외, 데이터가 아직 확정되지 않은 칸은 절대 가로채지 않음
                if (!isCenterItself && blockedCells != null && blockedCells.Contains((x, y)))
                    continue;

                var cell = Board.Get(x, y);

                // 구멍과 다른 박스는 여전히 덮어쓸 수 없다. StandHeld와 <b>방해블록</b>은 예외로
                // 덮어쓸 수 있다 - 박스는 판에 박힌 것들을 걷어내는 도구를 겸한다.
                // 방해블록은 이렇게 지워지는 게 <b>유일한 제거 수단 중 하나</b>다(다른 하나는 캐릭터 스킬).
                // 미스틱의 특수 패널도 못 바꾼다(시트: "변환이 불가능한", 2026-08-30 사용자 확정).
                bool blocksBoxOverwrite = cell.kind == CellKind.Hole || cell.kind == CellKind.Box
                                          || cell.kind == CellKind.Special
                                          || cell.kind == CellKind.BurnTrack;
                if (!isCenterItself && blocksBoxOverwrite)
                    continue;

                // bornFromBox: 이 조각들이 낀 매치는 6개 이상이어도 새 박스를 만들지 않는다.
                // 표시가 조각을 따라다녀야 하므로 Cell에 담는다(Cell.bornFromBox 주석 참고).
                Board.Set(x, y, new Cell { kind = CellKind.Normal, panelIndex = colorIndex, bornFromBox = true });
                converted.Add((x, y));
            }

            return converted;
        }

        /// <summary>
        /// 이 무리에 박스로 생겨난 조각(Cell.bornFromBox)이 하나라도 있는지.
        /// 하나라도 있으면 그 매치는 새 박스를 만들지 않는다.
        /// </summary>
        public bool AnyBornFromBox(List<(int x, int y)> cells)
        {
            if (cells == null)
                return false;

            foreach (var (x, y) in cells)
            {
                if (Board.InBounds(x, y) && Board.Get(x, y).bornFromBox)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 지정한 칸들을 특정 색의 일반 패널로 바꾼다. 캐릭터 스킬(구역 변환)이 쓰는 경로다.
        ///
        /// 규칙은 박스 십자변환(ConvertCrossToNormal)과 같다:
        ///  - blockedCells(=데이터가 아직 확정되지 않은 칸)는 절대 가로채지 않는다. 진행 중인
        ///    매치의 칸을 덮어쓰면 그 매치의 색이 뒤바뀐다.
        ///  - Obstacle/Hole/Box 는 덮어쓸 수 없다. StandHeld 는 예외로 덮어쓸 수 있게 둔다 -
        ///    스킬이 고정된 무더기를 풀어주는 역할도 겸하도록(박스와 같은 판단).
        ///  - 바뀐 칸은 <b>반드시 미안착으로 둔다</b>(settleDuration). 안 그러면 변환 직후 곧바로
        ///    매치 처리에 들어가서 파트너 스킬을 이어 쓸 틈이 없다 - 미안착 시스템이 애초에
        ///    이걸 위해 만들어졌다.
        ///
        /// 반환값: 실제로 바뀐 좌표 목록(뷰 갱신과 연출에 쓴다).
        /// </summary>
        /// <param name="overwritesBoxes">
        /// 상자까지 덮어쓸지. <b>기본은 안 덮어쓴다</b> - 상자는 플레이어가 모아 쓰는 것이라
        /// 스킬이 함부로 지우면 안 된다. 라미아의 브릴란스처럼 <b>기획이 그렇게 정한 스킬</b>만 켠다
        /// (2026-08-30 사용자 확정).
        /// </param>
        public List<(int x, int y)> ConvertCellsToPanel(IEnumerable<(int x, int y)> cells, int panelIndex,
            ISet<(int x, int y)> blockedCells = null, float settleDuration = 0f,
            bool overwritesBoxes = false)
        {
            var converted = new List<(int x, int y)>();
            if (cells == null || panelIndex < 0 || panelIndex >= paletteSize)
                return converted;

            foreach (var (x, y) in cells)
            {
                if (!Board.InBounds(x, y))
                    continue;

                if (blockedCells != null && blockedCells.Contains((x, y)))
                    continue;

                var cell = Board.Get(x, y);

                // 구멍과 박스는 덮어쓸 수 없다. StandHeld와 <b>방해블록</b>은 덮어쓸 수 있다 -
                // 스킬이 판에 박힌 것들을 걷어내는 역할을 겸한다(박스 십자변환과 같은 판단).
                // 방해블록은 이렇게 지워지는 게 유일한 제거 수단 중 하나다.
                // 미스틱의 특수 패널은 <b>어떤 스킬로도</b> 못 바꾼다(시트: "변환이 불가능한").
                bool blocksOverwrite = cell.kind == CellKind.Hole
                                       || cell.kind == CellKind.Special
                                       || cell.kind == CellKind.BurnTrack
                                       || (cell.kind == CellKind.Box && !overwritesBoxes);
                if (blocksOverwrite)
                    continue;

                Board.Set(x, y, new Cell
                {
                    kind = CellKind.Normal,
                    panelIndex = panelIndex,
                    unsettleRemaining = settleDuration > 0f ? settleDuration : 0f
                });
                converted.Add((x, y));
            }

            if (settleDuration > 0f && converted.Count > 0)
                hasUnsettledCells = true;

            return converted;
        }

        /// <summary>
        /// 지금 판에 있는 특정 색 조각의 좌표를 모은다. 스킬이 <b>건드리기 전에</b> 대상 칸을
        /// 알아야 그 자리에 연출(구름)을 먼저 피울 수 있어서 조회와 적용을 나눠 뒀다.
        /// </summary>
        public void CollectCellsOfPanel(int panelIndex, List<(int x, int y)> result)
        {
            result.Clear();
            if (panelIndex < 0)
                return;

            for (int x = 0; x < Board.width; x++)
            {
                for (int y = 0; y < Board.height; y++)
                {
                    var cell = Board.Get(x, y);
                    if (cell.panelIndex != panelIndex)
                        continue;
                    if (cell.kind != CellKind.Normal && cell.kind != CellKind.StandHeld)
                        continue;

                    result.Add((x, y));
                }
            }
        }

        /// <summary>
        /// 지금 판에 있는 특정 색 조각을 전부 강화한다(파트너 스킬).
        ///
        /// 강화는 시간이 지나 풀리는 게 아니라 <b>그 조각이 사라질 때까지</b> 유지된다.
        /// Cell.empowerMultiplier 에 담기므로 낙하로 옮겨져도 따라가고, 매치되거나 덮어써지면
        /// 그 칸이 새 Cell 로 바뀌면서 자연히 사라진다 - 따로 지울 필요가 없다.
        ///
        /// <b>이미 강화된 칸에 다시 걸면 더 센 쪽이 남는다</b>(덮어쓰기가 아니라 최댓값).
        /// 배율이 다른 파트너를 둘 편성했을 때 나중에 쓴 약한 스킬이 강한 강화를 깎아내리면
        /// "스킬을 더 썼는데 약해지는" 그림이 되기 때문이다.
        ///
        /// 반환값: 실제로 강화된 좌표 목록(연출에 쓴다).
        /// </summary>
        /// <param name="multiplier">데미지 배율(1.5 = 1.5배). 1 이하면 강화가 아니므로 아무것도 하지 않는다.</param>
        public List<(int x, int y)> EmpowerCellsOfPanel(int panelIndex, float multiplier,
            ISet<(int x, int y)> blockedCells = null)
        {
            var changed = new List<(int x, int y)>();
            if (panelIndex < 0 || multiplier <= 1f)
                return changed;

            for (int x = 0; x < Board.width; x++)
            {
                for (int y = 0; y < Board.height; y++)
                {
                    if (blockedCells != null && blockedCells.Contains((x, y)))
                        continue;

                    var cell = Board.Get(x, y);
                    if (cell.panelIndex != panelIndex || cell.empowerMultiplier >= multiplier)
                        continue;

                    // 일반 조각과 스탠드업에 고정된 조각만 대상. 오자마/구멍/빈칸은 강화 개념이 없다.
                    if (cell.kind != CellKind.Normal && cell.kind != CellKind.StandHeld)
                        continue;

                    cell.empowerMultiplier = multiplier;
                    Board.Set(x, y, cell);
                    changed.Add((x, y));
                }
            }

            return changed;
        }

        /// <summary>
        /// <b>고른 칸만</b> 강화한다. <see cref="EmpowerCellsOfPanel"/> 이 색 전체를 올리는 것과 달리
        /// 여기는 넘겨받은 칸만 본다 - 라미아의 브릴란스처럼 "생성 지점 근처"만 올리는 스킬용이다.
        /// 이미 그만큼 강화돼 있으면 건드리지 않는다(강화는 겹쳐서 올라가지 않는다 - 색 전체 강화와 같은 규칙).
        /// </summary>
        public List<(int x, int y)> EmpowerCells(IEnumerable<(int x, int y)> cells, float multiplier)
        {
            var changed = new List<(int x, int y)>();
            if (cells == null || multiplier <= 1f)
                return changed;

            foreach (var (x, y) in cells)
            {
                if (!Board.InBounds(x, y))
                    continue;

                var cell = Board.Get(x, y);
                if (cell.empowerMultiplier >= multiplier)
                    continue;

                // 일반 조각과 스탠드업에 고정된 조각만 대상(색 전체 강화와 같은 기준).
                if (cell.kind != CellKind.Normal && cell.kind != CellKind.StandHeld)
                    continue;

                cell.empowerMultiplier = multiplier;
                Board.Set(x, y, cell);
                changed.Add((x, y));
            }

            return changed;
        }

        /// <summary>
        /// 그 칸에 <paramref name="panelIndex"/> 색 조각이 놓여 있는지.
        /// 일반·스탠드업 고정·상자를 다 친다 - 화면에 그 캐릭터가 보이면 자기 조각이다.
        ///
        /// 라미아의 뿌리가 <b>자기 조각 쪽으로는 안 뻗도록</b> 거를 때 쓴다
        /// (2026-08-30 사용자 지시로 바뀐 규칙 - 예전엔 자기 색 칸도 후보였다).
        /// </summary>
        public bool IsOwnPiece(int x, int y, int panelIndex)
        {
            if (!Board.InBounds(x, y) || panelIndex < 0)
                return false;

            var cell = Board.Get(x, y);
            if (cell.panelIndex != panelIndex)
                return false;

            return cell.kind == CellKind.Normal || cell.kind == CellKind.StandHeld
                   || cell.kind == CellKind.Box;
        }

        /// <summary>
        /// 그 칸의 <b>상하좌우</b>에 <paramref name="panelIndex"/> 색이면서
        /// <b>아직 강화되지 않은</b> 조각이 있는지.
        ///
        /// 대각선은 안 본다(2026-08-30 사용자 확정 - 매치 판정과 같은 기준이라 눈에 잘 읽힌다).
        ///
        /// <b>⚠ 강화된 조각은 세지 않는다</b>(2026-08-30 사용자가 시험 뒤 바꾼 규칙). 이게 브릴란스의
        /// <b>제동 장치</b>다 - 연쇄가 이어질 때마다 강화된 칸이 늘고, 그 칸들은 다음 탐지에서
        /// 빠지므로 연쇄가 저절로 짧아진다. 이게 없으면 판이 자기 색으로 덮일수록 끝없이 이어진다.
        /// </summary>
        public bool HasPlainOwnNeighbor(int x, int y, int panelIndex)
        {
            return IsPlainOwn(x + 1, y, panelIndex)
                   || IsPlainOwn(x - 1, y, panelIndex)
                   || IsPlainOwn(x, y + 1, panelIndex)
                   || IsPlainOwn(x, y - 1, panelIndex);
        }

        private bool IsPlainOwn(int x, int y, int panelIndex)
        {
            if (!Board.InBounds(x, y))
                return false;

            var cell = Board.Get(x, y);
            if (cell.panelIndex != panelIndex || cell.empowered)
                return false;

            // 일반 조각과 스탠드업에 고정된 조각만 자기 블록으로 친다(강화 대상과 같은 기준).
            return cell.kind == CellKind.Normal || cell.kind == CellKind.StandHeld;
        }

        /// <summary>
        /// 그 칸들을 <b>미스틱의 특수 패널</b>로 만든다(포지셔닝).
        /// 무엇 위에든 놓는다 - 방해블록·상자·스탠드업 고정 칸까지(2026-08-30 사용자 확정).
        /// <b>구멍만은 못 덮는다</b> - 거긴 판에 뚫린 자리라 조각이 설 수 없다.
        ///
        /// <b>미안착으로 두지 않는다</b> - 특수 패널은 놓이는 순간부터 그 자리에 박혀 있는 게
        /// 이 스킬의 요점이고, 어차피 자기들끼리는 매치가 성립하지 않아 서두를 이유가 없다.
        /// </summary>
        // <b>특수 블록</b>(미스틱의 특수 퍼즐 + 유나의 점화 블록)이 소환될 때마다 하나씩 오른다.
        // 한 판 안에서만 뜻이 있는 번호라 저장하지 않는다 - 순서만 알면 되고 절대값은 뜻이 없다.
        private int lastSpecialBlockOrder;

        /// <summary>이번에 소환하는 특수 블록의 번호를 받아 간다. 한 번에 소환되는 것들이 나눠 쓴다.</summary>
        private int NextSpecialBlockOrder() => ++lastSpecialBlockOrder;

        public List<(int x, int y)> MakeSpecialPanels(IEnumerable<(int x, int y)> cells, int panelIndex,
            int matches, ISet<(int x, int y)> blockedCells = null)
        {
            var made = new List<(int x, int y)>();
            if (cells == null || panelIndex < 0 || matches <= 0)
                return made;

            // ⭐ 이번에 소환하는 뭉치의 번호. <b>뭉치 하나가 같은 번호를 나눠 갖는다</b>.
            // 특수 퍼즐을 만드는 캐릭터가 늘어도 전부 이 함수를 지나므로, 여기서 한 번만
            // 붙이면 "나중에 소환한 쪽이 우선권" 규칙이 저절로 모두에게 적용된다.
            int summonOrder = NextSpecialBlockOrder();

            foreach (var (x, y) in cells)
            {
                if (!Board.InBounds(x, y))
                    continue;

                if (blockedCells != null && blockedCells.Contains((x, y)))
                    continue;

                // ⚠ <b>여기는 CanConvert 와 기준이 다르다.</b> 구역 변환 스킬은 특수 블록을
                // 못 바꾸지만(그건 "평범한 조각으로 되돌리는" 일이다), 특수 블록을 <b>새로
                // 소환하는</b> 건 나중에 온 쪽이 우선권을 갖는다. 두 규칙을 하나로 합치지 말 것.
                //
                // ⭐ <b>점화 블록도 덮어쓴다</b>(2026-09-03 사용자 확정) - 특수 블록끼리는
                // 나중에 소환한 쪽이 우선권을 갖는다. 예전엔 여기서 건너뛰었는데, 부르는 쪽은
                // 그 칸의 뷰를 이미 떼어낸 뒤라 <b>데이터는 점화 블록인데 화면은 빈 칸</b>이
                // 되어 버렸다(사용자 신고: 그 자리에 조각을 놓으면 버닝 트랙이 발동했다).
                if (Board.Get(x, y).kind == CellKind.Hole)
                    continue;

                Board.Set(x, y, new Cell
                {
                    kind = CellKind.Special,
                    panelIndex = panelIndex,
                    specialMatchesLeft = matches,
                    specialSummonOrder = summonOrder
                });

                made.Add((x, y));
            }

            return made;
        }

        /// <summary>
        /// 그 칸을 스킬이 <b>덮어쓸 수 있는지</b>. <see cref="ConvertCellsToPanel"/> 의 판단과
        /// 같은 기준이라야 "후보로 골랐는데 막상 안 바뀌는" 일이 안 생긴다.
        /// </summary>
        public bool CanConvert(int x, int y, bool overwritesBoxes)
        {
            if (!Board.InBounds(x, y))
                return false;

            var kind = Board.Get(x, y).kind;

            // 구멍과 미스틱의 특수 퍼즐은 어떤 스킬로도 못 바꾼다(ConvertCellsToPanel 과 같은 기준).
            if (kind == CellKind.Hole || kind == CellKind.Special || kind == CellKind.BurnTrack)
                return false;

            return kind != CellKind.Box || overwritesBoxes;
        }

        /// <summary>
        /// 아직 이 색으로 <b>바꿀 수 있는 칸</b>이 남아 있는지. 판이 통째로 그 색이 되면 false 다.
        ///
        /// 끝없이 도는 스킬(브릴란스)의 <b>진짜 종료 조건</b>이다 - 판이 다 내 색이 되면
        /// 생성 지점마다 이웃이 늘 있어서 연쇄 조건만으로는 영영 안 끝난다.
        /// </summary>
        public bool HasCellToConvert(int panelIndex, bool overwritesBoxes)
        {
            for (int y = 0; y < Board.height; y++)
            {
                for (int x = 0; x < Board.width; x++)
                {
                    var cell = Board.Get(x, y);

                    if (cell.kind == CellKind.Hole || cell.kind == CellKind.BurnTrack)
                        continue;

                    if (cell.kind == CellKind.Box && !overwritesBoxes)
                        continue;

                    // 이미 내 색인 일반/고정 칸은 바꿔봐야 달라지는 게 없다.
                    bool alreadyMine = cell.panelIndex == panelIndex
                                       && (cell.kind == CellKind.Normal || cell.kind == CellKind.StandHeld);

                    if (!alreadyMine)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 유나의 <b>버닝 트랙!</b> 점화 블록을 <b>맨 아랫줄의 무작위 칸</b>에 count개 놓는다.
        ///
        /// ⭐ <b>소환 자리를 정하는 세 순위</b>(2026-09-03 사용자 정의). 특수 블록을 만드는
        /// 스킬은 전부 이 순서를 따른다 - 유나의 구역이 "맨 아랫줄"일 뿐이다.
        /// <code>
        ///   1순위  자기 구역          유나에게는 <b>맨 아랫줄</b>. 여기부터 본다.
        ///   2순위  퍼즐 우선순위      그 줄 안에서 일반=방해블록 < 큐브 < 특수 블록 순으로 내준다.
        ///   3순위  가까운 구역        그 줄에 놓을 곳이 아예 없을 때만 한 줄씩 위로.
        /// </code>
        ///
        /// ⚠⚠ <b>2순위를 다 쓰기 전에는 3순위로 못 간다.</b> 아랫줄이 큐브로 가득 찼으면
        /// <b>그 큐브를 덮는다</b> - 위 줄에 일반 조각이 있다고 도망가지 않는다.
        /// 아랫줄에 있을수록 태우는 칸이 많은 게 이 스킬의 값이라, 줄을 포기하는 쪽이 더 손해다
        /// (2026-09-03 사용자 신고로 바로잡음 - 처음엔 판 전체에서 등급별로 훑어서 큐브를 피했다).
        ///
        /// 등급은 <see cref="CellPlacement"/> 가 정한다. 같은 등급끼리는 줄 안에서 무작위로,
        /// 특수 블록끼리는 <b>먼저 소환된 것부터</b> 내준다.
        ///
        /// ⚠ 고정 칸을 덮으면 그 무리가 매치 기준 밑으로 줄 수 있다 - 박는 쪽(BurnTrack)이
        /// 박스 십자변환과 같은 뒤처리를 한다.
        /// 어찍턴 플레이어가 그 블록을 드래그로 원하는 자리에 옮길 수 있다.
        /// </summary>
        /// <returns>블록을 놓을 칸들. <b>아직 판을 고치지는 않는다</b> -
        /// 실제로 박는 건 <see cref="MakeBurnTracks"/> 가 한다(구름을 먼저 피우기 위해 나눠 둔다).</returns>
        public List<(int x, int y)> PickBurnTrackCells(int count, ISet<(int x, int y)> blockedCells,
            PlacementStyle style, List<(int x, int y)> into = null)
        {
            var placed = into ?? new List<(int x, int y)>();
            placed.Clear();

            if (count <= 0)
                return placed;

            // 아래 줄부터. <b>한 줄을 등급 끝까지 다 써 본 뒤에야</b> 위로 올라간다.
            for (int y = 0; y < Board.height && placed.Count < count; y++)
                TakeFromRow(y, count, blockedCells, style, placed);

            return placed;
        }

        /// <summary>등급이 아까운 순서. 이 순서대로 한 줄을 훑는다.</summary>
        private static readonly PlacementCost[] SacrificeOrder =
        {
            PlacementCost.Free,
            PlacementCost.Box,
            PlacementCost.Special,
        };

        /// <summary>
        /// 그 줄에서 <b>덜 아까운 칸부터</b> 채운다.
        /// 같은 등급끼리는 무작위로, 특수 블록끼리만 <b>먼저 소환된 것부터</b>.
        /// </summary>
        private void TakeFromRow(int y, int count, ISet<(int x, int y)> blockedCells,
            PlacementStyle style, List<(int x, int y)> placed)
        {
            for (int t = 0; t < SacrificeOrder.Length && placed.Count < count; t++)
            {
                var cost = SacrificeOrder[t];

                burnCandidates.Clear();
                for (int x = 0; x < Board.width; x++)
                {
                    if (blockedCells != null && blockedCells.Contains((x, y)))
                        continue;

                    var here = CellPlacement.CostOf(Board.Get(x, y));
                    if (here == PlacementCost.Never)
                        continue;

                    // 아랑곳하지 않는 성향은 등급을 안 본다 - 첫 바퀴에 그 줄을 통째로 담는다.
                    if (style == PlacementStyle.Reckless ? t > 0 : here != cost)
                        continue;

                    burnCandidates.Add((x, y));
                }

                if (style == PlacementStyle.Careful && cost == PlacementCost.Special)
                {
                    // 나중에 소환한 쪽이 우선권을 가지므로, 오래된 것부터 내준다.
                    burnCandidates.Sort((a, b) =>
                    {
                        int byAge = CellPlacement.SacrificeOrderOf(Board.Get(a.x, a.y))
                            .CompareTo(CellPlacement.SacrificeOrderOf(Board.Get(b.x, b.y)));
                        return byAge != 0 ? byAge : a.x.CompareTo(b.x);
                    });
                }
                else
                {
                    // 같은 등급 안에서는 순서를 섞어 무작위로 고른다.
                    for (int i = burnCandidates.Count - 1; i > 0; i--)
                    {
                        int r = rng.Next(i + 1);
                        var swap = burnCandidates[i];
                        burnCandidates[i] = burnCandidates[r];
                        burnCandidates[r] = swap;
                    }
                }

                for (int i = 0; i < burnCandidates.Count && placed.Count < count; i++)
                    placed.Add(burnCandidates[i]);
            }
        }

        // 자리를 고를 때 돌려쓰는 버퍼. 스킬을 쓸 때만 불리므로 하나로 충분하다.
        private readonly List<(int x, int y)> burnCandidates = new List<(int x, int y)>();

        /// <summary>
        /// 판에 남은 <b>특수 블록</b>(미스틱의 특수 퍼즐 + 유나의 점화 블록)을 전부 모은다.
        /// 러시 타임이 시작될 때 걷어내려고 쓴다 - 판단 기준은 <see cref="CellPlacement"/> 하나다.
        /// </summary>
        public List<(int x, int y)> CollectSpecialBlocks(List<(int x, int y)> into)
        {
            into.Clear();

            for (int y = 0; y < Board.height; y++)
            {
                for (int x = 0; x < Board.width; x++)
                {
                    if (CellPlacement.CostOf(Board.Get(x, y)) == PlacementCost.Special)
                        into.Add((x, y));
                }
            }

            return into;
        }

        /// <summary>
        /// 그 칸을 그 색의 <b>박스</b>로 만든다. 스티커 "방해 블록이 생성되면 한 번만
        /// 리더 캐릭터의 박스로 덮어씌우기" 가 쓴다.
        /// 구멍만 못 덮는다(판 전체의 규칙).
        /// </summary>
        public bool MakeBox(int x, int y, int panelIndex)
        {
            if (!Board.InBounds(x, y) || panelIndex < 0)
                return false;

            if (Board.Get(x, y).kind == CellKind.Hole)
                return false;

            Board.Set(x, y, new Cell { kind = CellKind.Box, panelIndex = panelIndex });
            return true;
        }

        /// <summary>
        /// 그 칸들 중 <b>강화된</b> 조각이 몇 개인지. 스티커 "강화된 퍼즐 한 조각은 N조각 맞춘
        /// 걸로 처리(스킬 채우기)" 가 쓴다 - 배율이 얼마든 <b>개수</b>만 센다.
        /// </summary>
        public int CountEmpowered(IReadOnlyList<(int x, int y)> cells)
        {
            if (cells == null)
                return 0;

            int count = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                var (x, y) = cells[i];
                if (Board.InBounds(x, y) && Board.Get(x, y).empowered)
                    count++;
            }

            return count;
        }

        /// <summary>골라둔 칸들을 실제로 점화 블록으로 바꿘다.</summary>
        public void MakeBurnTracks(IReadOnlyList<(int x, int y)> cells)
        {
            if (cells == null)
                return;

            // 점화 블록도 특수 블록이라 같은 번호 체계를 쓴다 - 이번에 놓는 것들이 한 번호를 나눠 갖는다.
            int summonOrder = NextSpecialBlockOrder();

            for (int i = 0; i < cells.Count; i++)
            {
                var (x, y) = cells[i];
                if (Board.InBounds(x, y))
                {
                    Board.Set(x, y, new Cell
                    {
                        kind = CellKind.BurnTrack,
                        panelIndex = -1,
                        specialSummonOrder = summonOrder
                    });
                }
            }
        }

        /// <summary>
        /// 점화 블록이 태울 칸들 - <b>그 칸의 열을 그 행부터 맨 위까지</b>.
        /// 그래서 블록을 위로 올려놓을수록 타는 칸이 줄어든다(사용자 확정).
        ///
        /// 방해블록도 상자도 고정 칸도 특수 퍼즐도 전부 지우고, <b>구멍만 남는다</b>
        /// (구멍은 어떤 수단으로도 지워지지 않는다는 판 전체의 규칙이다).
        /// 점화 블록 자신도 목록에 들어간다 - 한 번 쓰면 같이 사라진다.
        /// </summary>
        public void CollectBurnColumn(int x, int fromY, List<(int x, int y)> into)
        {
            if (into == null || !Board.InBounds(x, fromY))
                return;

            for (int y = fromY; y < Board.height; y++)
            {
                var kind = Board.Get(x, y).kind;

                // 구멍은 어떤 수단으로도 안 지워지고, 빈 칸은 지울 것이 없다 -
                // 빈 칸을 넣으면 아무 일도 안 일어나는데 "지워진 조각 갯수"만 올라간다.
                if (kind == CellKind.Hole || kind == CellKind.Empty)
                    continue;

                into.Add((x, y));
            }
        }

        // 힌트 탐색 전용 버퍼. 매치 스캔(scanVisited/scanGroupBuffer)과 나눠 쓰는 이유는,
        // 힌트는 Update 에서 불리고 매치 스캔은 코루틴에서 불려서 같은 버퍼를 공유하면
        // "언제 누가 먼저 부르는지"에 결과가 묶이기 때문이다. 둘 다 호출당 할당은 없다.
        private int[] hintComponentId;                                    // 칸 -> 덩어리 번호(-1 = 없음)
        private readonly List<int> hintComponentSize = new List<int>();   // 덩어리 번호 -> 칸 수
        private readonly List<int> hintComponentColor = new List<int>();  // 덩어리 번호 -> 색
        private readonly Queue<(int x, int y)> hintQueue = new Queue<(int x, int y)>();
        private readonly int[] hintNeighborIds = new int[4];              // 놓을 자리에 닿는 덩어리들
        private bool[] hintVisited;
        private readonly List<(int x, int y)> hintGroupBuffer = new List<(int x, int y)>();

        /// <summary>
        /// 힌트로 보여줄 한 수를 찾는다.
        ///
        /// <b>정의: "조각 하나를 옮겨 놓기만 하면 매치가 되는 자리"가 있으면 그게 힌트다.</b>
        /// 이 게임은 조각을 집어 아무 데나 놓는 자유 드래그라, 한 수 = 한 조각을 한 칸에 놓는 것이다.
        /// 그래서 <b>놓을 자리</b>를 기준으로 센다: 그 칸에 어떤 색을 놓았을 때 사방으로 이어지는
        /// 같은 색 덩어리들의 합 + 1 이 매치 기준(4) 이상이면 성립한다.
        ///
        /// 덩어리를 미리 한 번 이름표 붙여두고(LabelHintComponents) 놓을 자리마다 이웃 4칸의
        /// 덩어리 번호만 보면 되므로, 모든 조각 × 모든 칸을 시뮬레이션하는 것보다 훨씬 싸다.
        ///
        /// <b>"3칸 + 하나"만 찾지 않고 덩어리 합으로 세는 이유</b>: 2칸짜리 덩어리 둘 사이에
        /// 하나를 끼워 넣어도 2+2+1 = 5 라 매치가 된다. 3칸만 찾으면 이런 수를 못 보고
        /// "수가 없다"고 잘못 판단한다 - 아래 스탠드업 판정이 그 오판에 그대로 걸린다.
        ///
        /// <b>스탠드업 타임(includeStandHeld)에는 규칙이 달라진다.</b> 고정된 조각(StandHeld)은
        /// 집을 수도 덮어쓸 수도 없지만 <b>매치 판정에는 이어 붙는다</b>. 그래서
        ///  - 고정된 무더기(보통 4칸 이상)도 덩어리로 세어진다 - 옆에 하나만 갖다 대면 매치가
        ///    성립하므로 오히려 가장 쉬운 수다.
        ///  - 반대로 움직일 수 있는 조각이 크게 줄어 <b>아무 수도 없는 판이 실제로 나온다.</b>
        ///    그때는 false 를 반환한다 - 못 하는 수를 억지로 가리키지 않는다.
        /// 이 제약은 따로 넣은 게 아니라 조건에 이미 들어 있다: 놓을 자리는 덮어쓸 수 있는
        /// 평범한 칸(Normal)뿐이고, donor 는 IsConnectable(=Normal·안착)이라야 하므로
        /// 고정된 조각은 애초에 후보가 아니다.
        ///
        /// donor 는 놓을 자리에서 <b>가장 가까운</b> 같은 색 조각을 고른다. 아무거나 골라도 수는
        /// 성립하지만, 판 반대편 조각을 가리키면 무엇을 하라는 건지 읽히지 않는다.
        /// </summary>
        /// <param name="groupOut">완성 대상이 되는 덩어리(들)의 칸 전부.</param>
        /// <param name="donor">거기로 가져가야 할 같은 색 조각 하나.</param>
        /// <param name="blockedCells">지금 다른 처리가 쓰고 있어 건드리면 안 되는 칸.</param>
        /// <param name="includeStandHeld">스탠드업 타임인지. 매치 스캔에 넘기는 값과 같아야 한다.</param>
        /// <returns>힌트를 찾았으면 true. <b>가능한 수가 없으면 false</b>.</returns>
        public bool TryFindHint(List<(int x, int y)> groupOut, out (int x, int y) donor,
            ISet<(int x, int y)> blockedCells = null, bool includeStandHeld = false)
        {
            groupOut.Clear();
            donor = (-1, -1);

            LabelHintComponents(blockedCells, includeStandHeld);

            // 이어붙일 대상이 전부 고정 조각이라 반짝일 게 donor 하나뿐인 수. 성립하는 수이긴
            // 하지만 "이 조각을 옮겨라"까지만 말해줄 수 있어서, 보여줄 조각이 있는 수를 먼저 찾고
            // 그런 게 없을 때만 쓴다.
            bool hasDonorOnly = false;
            (int x, int y) donorOnly = (-1, -1);

            int stride = Board.width;

            for (int y = 0; y < Board.height; y++)
            {
                for (int x = 0; x < Board.width; x++)
                {
                    if (blockedCells != null && blockedCells.Contains((x, y)))
                        continue;

                    // 놓을 수 있는 칸만 후보다. 고정 조각·오자마·구멍·박스는 덮어쓸 수 없고,
                    // 빈 칸은 낙하 중이라 잠깐 비어 있는 것뿐이라 놓을 자리로 치지 않는다.
                    var targetCell = Board.Get(x, y);
                    if (targetCell.kind != CellKind.Normal)
                        continue;

                    int neighborCount = 0;
                    for (int d = 0; d < OrthogonalOffsets.Length; d++)
                    {
                        int nx = x + OrthogonalOffsets[d].dx;
                        int ny = y + OrthogonalOffsets[d].dy;
                        if (!Board.InBounds(nx, ny))
                            continue;

                        int id = hintComponentId[ny * stride + nx];
                        if (id >= 0)
                            hintNeighborIds[neighborCount++] = id;
                    }

                    for (int i = 0; i < neighborCount; i++)
                    {
                        int color = hintComponentColor[hintNeighborIds[i]];

                        // 같은 색을 그 자리에 또 놓는 건 수가 아니다(이미 그 덩어리의 일부다).
                        if (color == targetCell.panelIndex)
                            continue;

                        // 같은 색이 여러 번 나오면 첫 번째에서만 계산한다(중복 방지).
                        bool firstOfColor = true;
                        for (int j = 0; j < i; j++)
                        {
                            if (hintComponentColor[hintNeighborIds[j]] == color)
                            {
                                firstOfColor = false;
                                break;
                            }
                        }
                        if (!firstOfColor)
                            continue;

                        // 이 색의 <b>서로 다른</b> 덩어리 크기를 더한다. 한 덩어리가 두 방향에서
                        // 닿을 수 있으므로 번호로 중복을 거른다.
                        // 이 값은 "이 칸에 이 색을 놓았을 때 나올 수 있는 최대 크기"(낙관적 상한)라,
                        // 여기서 못 넘으면 어떤 조각을 가져와도 안 된다 - 후보를 싸게 거르는 용도다.
                        int total = 1; // 놓는 조각 자신
                        for (int j = i; j < neighborCount; j++)
                        {
                            int id = hintNeighborIds[j];
                            if (hintComponentColor[id] != color)
                                continue;

                            bool alreadyCounted = false;
                            for (int k = i; k < j; k++)
                            {
                                if (hintNeighborIds[k] == id)
                                {
                                    alreadyCounted = true;
                                    break;
                                }
                            }
                            if (!alreadyCounted)
                                total += hintComponentSize[id];
                        }

                        if (total < ConnectionFinder.MinRemoveCount)
                            continue;

                        if (!TryFindNearestDonor(color, x, y, blockedCells, includeStandHeld,
                                groupOut, out donor))
                        {
                            continue;
                        }

                        if (groupOut.Count > 0)
                            return true; // 같이 반짝일 조각이 있는 수 - 이게 가장 잘 읽힌다

                        if (!hasDonorOnly)
                        {
                            hasDonorOnly = true;
                            donorOnly = donor;
                        }
                    }
                }
            }

            if (hasDonorOnly)
            {
                // 더 나은 수가 없었다 - 고정 무더기에 갖다 붙이는 수뿐이다.
                groupOut.Clear();
                donor = donorOnly;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 같은 색으로 이어진 덩어리마다 번호를 붙인다. 스탠드업 중에는 고정된 조각도 같은
        /// 덩어리에 포함된다 - 매치 판정이 그렇게 이어지기 때문이다
        /// (ConnectionFinder.FillConnectedGroupThroughStandHeld 와 같은 규칙).
        /// </summary>
        private void LabelHintComponents(ISet<(int x, int y)> blockedCells, bool includeStandHeld)
        {
            int stride = Board.width;
            int needed = Board.width * Board.height;
            if (hintComponentId == null || hintComponentId.Length < needed)
                hintComponentId = new int[needed];

            for (int i = 0; i < needed; i++)
                hintComponentId[i] = -1;

            hintComponentSize.Clear();
            hintComponentColor.Clear();

            for (int y = 0; y < Board.height; y++)
            {
                for (int x = 0; x < Board.width; x++)
                {
                    int index = y * stride + x;
                    if (hintComponentId[index] >= 0)
                        continue;
                    if (!JoinsHintGroup(x, y, blockedCells, includeStandHeld))
                        continue;

                    int id = hintComponentSize.Count;
                    int color = Board.Get(x, y).panelIndex;
                    hintComponentSize.Add(0);
                    hintComponentColor.Add(color);

                    hintQueue.Clear();
                    hintQueue.Enqueue((x, y));
                    hintComponentId[index] = id;

                    int size = 0;
                    while (hintQueue.Count > 0)
                    {
                        var (cx, cy) = hintQueue.Dequeue();
                        size++;

                        for (int d = 0; d < OrthogonalOffsets.Length; d++)
                        {
                            int nx = cx + OrthogonalOffsets[d].dx;
                            int ny = cy + OrthogonalOffsets[d].dy;
                            if (!Board.InBounds(nx, ny))
                                continue;

                            int nIndex = ny * stride + nx;
                            if (hintComponentId[nIndex] >= 0)
                                continue;
                            if (Board.Get(nx, ny).panelIndex != color)
                                continue;
                            if (!JoinsHintGroup(nx, ny, blockedCells, includeStandHeld))
                                continue;

                            hintComponentId[nIndex] = id;
                            hintQueue.Enqueue((nx, ny));
                        }
                    }

                    hintComponentSize[id] = size;
                }
            }
        }

        /// <summary>이 칸이 매치 판정에서 같은 색 덩어리로 이어질 수 있는지.</summary>
        private bool JoinsHintGroup(int x, int y, ISet<(int x, int y)> blockedCells, bool includeStandHeld)
        {
            if (blockedCells != null && blockedCells.Contains((x, y)))
                return false;

            var cell = Board.Get(x, y);
            return cell.IsConnectable || (includeStandHeld && cell.kind == CellKind.StandHeld);
        }

        /// <summary>
        /// 놓을 자리(targetX, targetY)로 가져오면 <b>실제로 매치가 되는</b> 조각 중 가장 가까운 것을 찾는다.
        ///
        /// <b>덩어리 크기 합만으로 판단하면 안 된다.</b> 가져올 조각이 그 덩어리 안에 있으면 옮기는
        /// 순간 그만큼 줄고, 빠진 자리 때문에 덩어리가 끊어질 수도 있다. 반대로 "덩어리 안에 있으면
        /// 무조건 제외"로 두면 <b>멀쩡한 수를 놓친다</b> - 크기 1짜리 덩어리에 혼자 있던 조각을
        /// 바로 옆 칸으로 옮겨 큰 무더기에 붙이는 수가 실제로 그렇게 사라졌다(파이썬 퍼즈로 확인).
        /// 그래서 후보마다 옮겨본 결과를 정확히 센다.
        ///
        /// 찾으면 groupOut 에 <b>같이 반짝일 칸들</b>(놓을 자리 자신은 빼고)을 채워 준다.
        /// </summary>
        private bool TryFindNearestDonor(int color, int targetX, int targetY,
            ISet<(int x, int y)> blockedCells, bool includeStandHeld,
            List<(int x, int y)> groupOut, out (int x, int y) donor)
        {
            donor = (-1, -1);
            int bestDistance = int.MaxValue;

            for (int x = 0; x < Board.width; x++)
            {
                for (int y = 0; y < Board.height; y++)
                {
                    if (x == targetX && y == targetY)
                        continue;
                    if (blockedCells != null && blockedCells.Contains((x, y)))
                        continue;

                    var cell = Board.Get(x, y);
                    if (cell.panelIndex != color || !cell.IsConnectable)
                        continue; // 고정 조각과 미안착 조각은 집어서 옮길 수 없다

                    int distance = System.Math.Abs(targetX - x) + System.Math.Abs(targetY - y);
                    if (distance >= bestDistance)
                        continue; // 이미 더 가까운 성공이 있으면 볼 필요가 없다

                    FillGroupAfterMove(targetX, targetY, color, (x, y), blockedCells, includeStandHeld);
                    if (!ConnectionFinder.CanRemoveGroup(Board, hintGroupBuffer))
                        continue;   // 특수 패널만 모인 자리는 짚어줘봐야 안 터진다

                    bestDistance = distance;
                    donor = (x, y);

                    groupOut.Clear();
                    for (int i = 0; i < hintGroupBuffer.Count; i++)
                    {
                        var found = hintGroupBuffer[i];
                        if (found.x == targetX && found.y == targetY)
                            continue; // 놓을 자리는 아직 다른 색이라 반짝이면 안 된다

                        // <b>이미 고정된 조각은 반짝이지 않는다.</b> 매치가 끝나 제자리에 박힌
                        // 조각인데 반짝이면 "여기에 뭔가 더 해야 한다"로 읽혀 헷갈린다.
                        // 판정에는 그대로 세어진다(고정 조각을 타고 매치가 이어지므로) -
                        // 세는 것과 보여주는 것을 나눈 것뿐이다.
                        if (Board.Get(found.x, found.y).kind == CellKind.StandHeld)
                            continue;

                        groupOut.Add(found);
                    }
                }
            }

            return bestDistance != int.MaxValue;
        }

        /// <summary>
        /// donor 를 (targetX, targetY)에 놓았다고 <b>가정하고</b> 거기서 이어지는 같은 색 덩어리를 구한다.
        /// 보드를 실제로 건드리지 않는다 - 잠깐 바꿨다 되돌리는 방식은 중간에 예외가 나면
        /// 판이 망가진 채로 남는다. 대신 "donor 칸은 비어 있다"고 치고 탐색에서 빼는 것으로 같은 결과를 낸다.
        /// </summary>
        private void FillGroupAfterMove(int targetX, int targetY, int color, (int x, int y) donor,
            ISet<(int x, int y)> blockedCells, bool includeStandHeld)
        {
            hintGroupBuffer.Clear();

            int stride = Board.width;
            int needed = Board.width * Board.height;
            if (hintVisited == null || hintVisited.Length < needed)
                hintVisited = new bool[needed];
            else
                System.Array.Clear(hintVisited, 0, needed);

            hintQueue.Clear();
            hintQueue.Enqueue((targetX, targetY));
            hintVisited[targetY * stride + targetX] = true;

            while (hintQueue.Count > 0)
            {
                var (cx, cy) = hintQueue.Dequeue();
                hintGroupBuffer.Add((cx, cy));

                for (int d = 0; d < OrthogonalOffsets.Length; d++)
                {
                    int nx = cx + OrthogonalOffsets[d].dx;
                    int ny = cy + OrthogonalOffsets[d].dy;
                    if (!Board.InBounds(nx, ny))
                        continue;

                    int index = ny * stride + nx;
                    if (hintVisited[index])
                        continue;
                    if (nx == donor.x && ny == donor.y)
                        continue; // 그 조각은 옮겨왔으니 원래 자리는 비었다
                    if (Board.Get(nx, ny).panelIndex != color)
                        continue;
                    if (!JoinsHintGroup(nx, ny, blockedCells, includeStandHeld))
                        continue;

                    hintVisited[index] = true;
                    hintQueue.Enqueue((nx, ny));
                }
            }
        }

        // 방해 대상 후보를 담는 재사용 버퍼.
        private readonly List<(int x, int y)> harassCandidates = new List<(int x, int y)>();

        /// <summary>
        /// 적의 방해(방해블록·구멍)를 놓을 만한 칸을 무작위로 하나 고른다.
        /// <b>고르기만 하고 바꾸지는 않는다</b> - 호출부가 그 자리에 구름을 먼저 피우고,
        /// 가려진 뒤에 바꿔야 하기 때문이다(스킬과 같은 순서).
        ///
        /// 후보는 <b>평범한 조각과 박스</b>다. 박스가 걸리면 박스는 사라지고 그 자리가 방해로 바뀐다
        /// (기획 확정 사항). 구멍·이미 방해블록인 칸·빈 칸은 바꿔봐야 의미가 없고,
        /// 스탠드업으로 고정된 칸은 그 판의 성과라 건드리지 않는다.
        /// </summary>
        /// <param name="blockedCells">데이터가 아직 확정되지 않은 칸(진행 중인 매치·낙하 등).</param>
        /// <param name="target">고른 칸.</param>
        /// <returns>고를 수 있는 칸이 하나도 없으면 false.</returns>
        public bool TryPickHarassTarget(ISet<(int x, int y)> blockedCells, out (int x, int y) target)
        {
            harassCandidates.Clear();

            for (int x = 0; x < Board.width; x++)
            {
                for (int y = 0; y < Board.height; y++)
                {
                    if (blockedCells != null && blockedCells.Contains((x, y)))
                        continue;

                    var kind = Board.Get(x, y).kind;
                    if (kind == CellKind.Normal || kind == CellKind.Box)
                        harassCandidates.Add((x, y));
                }
            }

            if (harassCandidates.Count == 0)
            {
                target = (-1, -1);
                return false;
            }

            target = harassCandidates[rng.Next(harassCandidates.Count)];
            return true;
        }

        /// <summary>
        /// 그 칸을 방해블록으로 바꾼다. 고른 뒤 구름이 덮이기까지 시간이 흐르므로,
        /// <b>바꾸기 직전에 자격을 다시 확인한다</b> - 그 사이 매치로 비워졌거나 다른 것이
        /// 차지했을 수 있다. 자격을 잃었으면 아무 일도 하지 않고 false.
        /// </summary>
        public bool TryPlaceObstacle((int x, int y) cell, ISet<(int x, int y)> blockedCells)
        {
            if (!Board.InBounds(cell.x, cell.y))
                return false;
            if (blockedCells != null && blockedCells.Contains(cell))
                return false;

            var kind = Board.Get(cell.x, cell.y).kind;
            if (kind != CellKind.Normal && kind != CellKind.Box)
                return false;

            // panelIndex 는 -1. 방해블록은 색이 없어서 매치·강화 어디에도 끼지 않는다.
            Board.Set(cell.x, cell.y, new Cell { kind = CellKind.Obstacle, panelIndex = -1 });
            return true;
        }

        /// <summary>
        /// 그 칸의 조각을 <b>다른 무작위 색</b>으로 바꾼다. 적의 가장 낮은 단계 방해다.
        ///
        /// <b>지금 그 자리에 있는 색은 후보에서 뺀다</b> - 같은 색이 뽑히면 아무 일도 안 일어난
        /// 것처럼 보여서, 구름만 피었다 사라지는 헛방이 된다.
        /// 박스가 있던 자리면 박스는 사라지고 평범한 조각이 된다(방해블록·구멍과 같은 규칙).
        ///
        /// 자격 확인은 <see cref="TryPlaceObstacle"/>과 같다.
        /// </summary>
        public bool TryRecolorCell((int x, int y) cell, ISet<(int x, int y)> blockedCells)
        {
            if (!Board.InBounds(cell.x, cell.y))
                return false;
            if (blockedCells != null && blockedCells.Contains(cell))
                return false;

            var current = Board.Get(cell.x, cell.y);
            if (current.kind != CellKind.Normal && current.kind != CellKind.Box)
                return false;

            // 색이 하나뿐이면 "다른 색"이 존재하지 않는다.
            if (paletteSize <= 1)
                return false;

            // 지금 색을 뺀 나머지 중에서 고른다. 0..paletteSize-2 를 뽑고, 지금 색 이상이면
            // 한 칸 밀어서 지금 색을 건너뛴다 - 다시 뽑기 반복 없이 균등하게 고르는 방법.
            int picked = rng.Next(paletteSize - 1);
            if (current.panelIndex >= 0 && picked >= current.panelIndex)
                picked++;

            Board.Set(cell.x, cell.y, new Cell { kind = CellKind.Normal, panelIndex = picked });
            return true;
        }

        /// <summary>
        /// 그 칸을 구멍으로 만든다. 자격 확인은 <see cref="TryPlaceObstacle"/>과 같다.
        /// </summary>
        /// <param name="duration">
        /// 구멍이 유지될 시간(초). 0 이하면 시간제한 없이 영구히 남는다 - 구멍은 지울 수단이
        /// 없으므로 그렇게 두면 판이 영영 좁아진다. 보통은 양수를 넘길 것.
        /// </param>
        public bool TryPlaceHole((int x, int y) cell, ISet<(int x, int y)> blockedCells, float duration)
        {
            if (!Board.InBounds(cell.x, cell.y))
                return false;
            if (blockedCells != null && blockedCells.Contains(cell))
                return false;

            var kind = Board.Get(cell.x, cell.y).kind;
            if (kind != CellKind.Normal && kind != CellKind.Box)
                return false;

            Board.Set(cell.x, cell.y, new Cell
            {
                kind = CellKind.Hole,
                panelIndex = -1,
                holeRemaining = duration > 0f ? duration : 0f
            });

            if (duration > 0f)
                hasHoleCells = true;

            return true;
        }

        // 시간제한이 걸린 구멍이 하나도 없으면 매 프레임 보드를 훑지 않기 위한 표시
        // (미안착 칸의 hasUnsettledCells 와 같은 방식).
        private bool hasHoleCells;

        // TickHoles 가 돌려쓰는 버퍼. 호출부는 다음 Tick 전까지만 유효한 것으로 취급해야 한다.
        private readonly List<(int x, int y)> expiredHoleBuffer = new List<(int x, int y)>();

        /// <summary>
        /// 구멍들의 남은 시간을 흘려보내고, <b>이번에 수명이 다한 구멍</b>의 좌표를 반환한다.
        ///
        /// <b>여기서 칸을 비우지는 않는다.</b> 호출부가 그 자리에 구름을 먼저 피우고 가려진 뒤에
        /// <see cref="ClearExpiredHole"/>로 비워야 하기 때문이다 - 생길 때와 사라질 때 모두
        /// 같은 규칙(구름이 먼저)을 지킨다. 남은 시간을 정확히 0으로 만들어 두므로 같은 구멍이
        /// 다음 프레임에 또 보고되지는 않는다(TickSettle 과 같은 방식).
        /// </summary>
        public List<(int x, int y)> TickHoles(float deltaTime)
        {
            expiredHoleBuffer.Clear();

            if (!hasHoleCells || deltaTime <= 0f)
                return expiredHoleBuffer;

            bool anyStillTicking = false;

            for (int x = 0; x < Board.width; x++)
            {
                for (int y = 0; y < Board.height; y++)
                {
                    var cell = Board.Get(x, y);
                    if (cell.kind != CellKind.Hole || cell.holeRemaining <= 0f)
                        continue;

                    cell.holeRemaining -= deltaTime;

                    if (cell.holeRemaining <= 0f)
                    {
                        cell.holeRemaining = 0f;
                        expiredHoleBuffer.Add((x, y));
                    }
                    else
                    {
                        anyStillTicking = true;
                    }

                    Board.Set(x, y, cell);
                }
            }

            hasHoleCells = anyStillTicking;
            return expiredHoleBuffer;
        }

        /// <summary>
        /// 수명이 다한 구멍을 실제로 없앤다(빈 칸이 된다). 호출부는 그 뒤에 낙하·리필을 돌려야
        /// 그 자리가 다시 채워진다. 아직 구멍이 아니면 아무 일도 하지 않는다.
        /// </summary>
        public bool ClearExpiredHole((int x, int y) cell)
        {
            if (!Board.InBounds(cell.x, cell.y))
                return false;
            if (Board.Get(cell.x, cell.y).kind != CellKind.Hole)
                return false;

            Board.Clear(cell.x, cell.y);
            return true;
        }

        /// <summary>
        /// 칸 목록이 데미지 계산에서 차지하는 무게의 합(= 강화를 반영한 "실효 칸 수").
        /// 강화가 하나도 없으면 정확히 cells.Count 와 같다.
        ///
        /// 데미지 공식이 전부 "전투력 × 칸 수"를 뼈대로 삼고 있어서, 칸 수를 세는 자리를
        /// 이 합으로 바꾸는 것만으로 일반 매치·스탠드업 정사각형·낱개 칸 세 경로에 같은 규칙이 적용된다.
        /// </summary>
        public float SumDamageWeight(List<(int x, int y)> cells)
        {
            if (cells == null)
                return 0f;

            float total = 0f;
            for (int i = 0; i < cells.Count; i++)
                total += Board.Get(cells[i].x, cells[i].y).DamageWeight;

            return total;
        }

        private static readonly (int dx, int dy)[] OrthogonalOffsets =
        {
            (0, 1), (0, -1), (1, 0), (-1, 0)
        };

        // TickSettle이 매번 새 리스트를 만들지 않도록 돌려쓰는 버퍼. 호출부는 다음 Tick 전까지만
        // 유효한 것으로 취급해야 한다(코루틴 프레임 너머로 들고 있으면 안 됨).
        private readonly List<(int x, int y)> settledBuffer = new List<(int x, int y)>();

        // 미안착 칸이 하나도 없으면 매 프레임 보드를 훑지 않기 위한 표시.
        private bool hasUnsettledCells;

        /// <summary>
        /// 주어진 칸들을 duration초 동안 <b>미안착</b> 상태로 만든다 - 화면에는 그대로 보이지만
        /// 그동안 매치 판정에 잡히지 않는다(Cell.unsettleRemaining 참고).
        ///
        /// 스킬로 구역을 변환하거나 박스가 십자로 펼쳐진 직후처럼, "조각을 놓아두되 플레이어가
        /// 다음 수를 이어 붙일 틈을 주고 싶은" 경우에 쓴다. duration이 0 이하면 아무 일도 하지 않는다
        /// (곧바로 매치 대상이 되는 기존 동작).
        /// </summary>
        public void MarkUnsettled(IEnumerable<(int x, int y)> cells, float duration)
        {
            if (cells == null || duration <= 0f)
                return;

            foreach (var (x, y) in cells)
            {
                if (!Board.InBounds(x, y))
                    continue;

                var cell = Board.Get(x, y);
                if (cell.kind == CellKind.Empty)
                    continue; // 빈 칸에 걸어봐야 다음에 채워지면서 덮어써진다

                // 이미 더 오래 기다리기로 돼 있으면 그대로 둔다 - 나중에 건 쪽이 먼저 굳게 만들면
                // 콤보를 노리고 길게 걸어둔 쪽이 짧은 값에 덮여버린다.
                if (cell.unsettleRemaining >= duration)
                    continue;

                cell.unsettleRemaining = duration;
                Board.Set(x, y, cell);
                hasUnsettledCells = true;
            }
        }

        /// <summary>
        /// 미안착 칸들의 남은 시간을 흘려보내고, <b>이번에 막 안착된 칸 목록</b>을 반환한다.
        /// 호출부는 이 목록이 비어있지 않을 때만 매치 스캔을 다시 돌리면 된다 - 안착은 새 매치가
        /// 생길 수 있는 유일한 순간인데 낙하/리필과 달리 스스로 스캔을 유발하지 않기 때문이다.
        ///
        /// 반환 리스트는 다음 호출 때 재사용되므로 호출부가 프레임 너머로 들고 있으면 안 된다.
        /// </summary>
        public List<(int x, int y)> TickSettle(float deltaTime)
        {
            settledBuffer.Clear();

            if (!hasUnsettledCells || deltaTime <= 0f)
                return settledBuffer;

            bool anyStillUnsettled = false;

            for (int x = 0; x < Board.width; x++)
            {
                for (int y = 0; y < Board.height; y++)
                {
                    var cell = Board.Get(x, y);
                    if (cell.unsettleRemaining <= 0f)
                        continue;

                    cell.unsettleRemaining -= deltaTime;

                    if (cell.unsettleRemaining <= 0f)
                    {
                        cell.unsettleRemaining = 0f;
                        settledBuffer.Add((x, y));
                    }
                    else
                    {
                        anyStillUnsettled = true;
                    }

                    Board.Set(x, y, cell);
                }
            }

            hasUnsettledCells = anyStillUnsettled;
            return settledBuffer;
        }

        /// <summary>
        /// 각 열마다 빈 칸을 아래로 밀어내고(압축), 위에서부터 채워지도록 낙하 이동 목록을 계산 + 실제 반영.
        ///
        /// 움직이지 않는 칸은 두 종류이고, "위 조각을 통과시키느냐"가 완전히 다르다:
        ///  - **벽**(Hole, protectedCells): 위 조각들도 이 칸을 넘어 내려오지 못한다. 구멍은 보드에
        ///    실제로 길이 막힌 자리고, protectedCells는 지금 다른 매치/드래그가 점유 중이라 이 낙하
        ///    계산이 건드리면 안 되는 칸이다(여러 매치가 동시에 진행될 때 서로 꼬이는 걸 방지).
        ///  - **고정**(StandHeld, pinnedCell): 그 칸 자신은 절대 안 움직이지만 위 조각들은 이 칸이
        ///    아예 없는 것처럼 통과해서 아래 빈 칸까지 내려온다. pinnedCell은 방금 플레이어가 드래그로
        ///    놓은 조각이고, StandHeld는 스탠드업 타임에 고정된 조각이다.
        ///
        /// StandHeld를 벽이 아니라 고정으로 두는 게 중요하다 - 벽으로 취급하면 보드 중간에서 매치가
        /// 성립했을 때 그 위 조각들이 통째로 멈춰 서고 아래 빈 칸은 전부 새 조각으로 채워져서,
        /// "위 조각은 가만히 있는데 밑에서 새 조각이 솟아나는" 그림이 된다(2026-08-06 수정된 버그).
        /// columns: null이면 모든 열을 처리(기존 동작). 특정 열 번호만 넘기면 그 열들만 계산한다 -
        /// 매치가 동시에 여러 개 진행될 때, 이 매치와 무관한 열의 낙하/리필까지 한꺼번에 처리해버리면
        /// 그 열에서 다른 매치의 접기 연출이 아직 재생 중인데 리필된 조각이 끼어들어 겹쳐 보이는
        /// 문제가 있었음 - 호출부(BoardInputController)가 이번 이벤트가 실제로 건드린 열만 넘겨서
        /// 그 열끼리는 여유롭게, 서로 다른 매치는 서로의 열을 침범하지 않게 한다.
        /// </summary>
        public List<FallMove> ApplyGravity(ISet<(int x, int y)> protectedCells = null, (int x, int y)? pinnedCell = null, ISet<int> columns = null)
        {
            var moves = new List<FallMove>();

            for (int x = 0; x < Board.width; x++)
            {
                if (columns != null && !columns.Contains(x))
                    continue;

                int writeY = 0;
                for (int y = 0; y < Board.height; y++)
                {
                    // 쓰기 커서가 고정된 자리를 가리키고 있으면 그 위 첫 자리로 밀어낸다 - 고정 칸을
                    // 덮어쓰지 않기 위함. 고정 칸이 여러 개 연달아 있을 수 있으므로 while.
                    while (writeY < Board.height && IsPinnedInGravity(x, writeY, pinnedCell))
                        writeY++;

                    if (IsPinnedInGravity(x, y, pinnedCell))
                        continue; // 이 칸의 조각은 절대 움직이지 않음 - writeY도 건드리지 않아서
                                  // 위쪽 조각들이 이 자리를 그냥 통과해서 계속 내려올 수 있게 함

                    var cell = Board.Get(x, y);

                    if (cell.BlocksGravity || (protectedCells != null && protectedCells.Contains((x, y))))
                    {
                        // 구멍이거나(영구) 지금 다른 작업이 점유 중인 칸은 "벽" - 이 칸 위의 조각들은
                        // 여기를 통과해 내려오지 못한다. writeY를 그 다음 칸으로 리셋.
                        writeY = y + 1;
                        continue;
                    }

                    if (cell.kind == CellKind.Empty)
                        continue;

                    if (writeY != y)
                    {
                        moves.Add(new FallMove { x = x, fromY = y, toY = writeY, cell = cell });
                        Board.Set(x, writeY, cell);
                        Board.Clear(x, y);
                    }
                    writeY++;
                }
            }

            return moves;
        }

        /// <summary>
        /// 낙하 계산에서 (x, y)가 "고정"인지 - 그 자리 조각은 안 움직이지만 위 조각은 통과시킨다.
        /// 스탠드업 타임에 고정된 조각(StandHeld)과, 방금 플레이어가 드래그해 놓은 칸(pinnedCell)이 해당.
        /// 둘은 이유만 다를 뿐 낙하에서 원하는 동작이 완전히 같아서 한 함수로 합쳤다.
        /// </summary>
        private bool IsPinnedInGravity(int x, int y, (int x, int y)? pinnedCell)
        {
            if (Board.Get(x, y).PinnedInGravity)
                return true;

            return pinnedCell.HasValue && pinnedCell.Value.x == x && pinnedCell.Value.y == y;
        }

        /// <summary>
        /// 낙하 후 남은 빈 칸을 팔레트에서 무작위로 채운다(위에서 새로 스폰되는 연출용).
        /// columns: ApplyGravity와 동일한 의미 - null이면 모든 열, 아니면 그 열들만 리필한다.
        /// protectedCells: 지금 다른 곳(소멸 애니메이션 등)에서 쓰이고 있어서 아직 리필하면 안 되는
        /// 칸들 - 데이터상 Empty라도 건너뛴다. 이게 없으면, 예를 들어 스탠드업 타임 종료 시 조각이
        /// "뿅"하고 사라지는 애니메이션이 재생되는 동안 그 칸을 동시에 진행 중인 다른 매치의 리필이
        /// 먼저 채워버리는 버그가 있었음(ApplyGravity는 이미 protectedCells를 지원했는데
        /// RefillEmptyCells에는 그 체크가 없어서 생긴 구멍).
        /// </summary>
        /// <param name="settleDuration">
        /// 새로 채워진 조각이 매치 대상이 되기까지 기다릴 시간(초). 0이면 곧바로 매치 대상이 된다
        /// (기본 동작). 리필까지 늦추면 캐스케이드 템포가 통째로 느려지므로 기본값은 0으로 둔다.
        /// </param>
        public List<(int x, int y, Cell cell)> RefillEmptyCells(ISet<int> columns = null, ISet<(int x, int y)> protectedCells = null,
            float settleDuration = 0f, bool avoidImmediateMatch = true)
        {
            var spawned = new List<(int x, int y, Cell cell)>();

            for (int x = 0; x < Board.width; x++)
            {
                if (columns != null && !columns.Contains(x))
                    continue;

                for (int y = 0; y < Board.height; y++)
                {
                    if (Board.Get(x, y).kind != CellKind.Empty)
                        continue;

                    if (protectedCells != null && protectedCells.Contains((x, y)))
                        continue;

                    // UnityEngine에 의존하지 않는 순수 로직 클래스라 Mathf 대신 삼항으로 처리
                    var cell = new Cell
                    {
                        kind = CellKind.Normal,
                        panelIndex = PickRefillIndex(x, y, avoidImmediateMatch),
                        unsettleRemaining = settleDuration > 0f ? settleDuration : 0f
                    };
                    Board.Set(x, y, cell);
                    if (settleDuration > 0f)
                        hasUnsettledCells = true;
                    spawned.Add((x, y, cell));
                }
            }

            return spawned;
        }

        // 리필 색을 고를 때 연결 검사에 쓰는 재사용 버퍼. 리필은 한 번에 수십 칸을 채우므로
        // 칸마다 새 리스트를 만들면 그대로 GC로 간다.
        private readonly List<(int x, int y)> refillProbeBuffer = new List<(int x, int y)>();

        /// <summary>
        /// 리필할 칸에 넣을 색을 고른다. avoidImmediateMatch면 <b>놓자마자 매치가 성립하는 색은 피한다.</b>
        ///
        /// 초기 판 생성(BoardGenerator)은 원래부터 공짜 매치를 피하고 있었는데 리필만 순수 무작위라
        /// 비대칭이었다. 그래서 리필 도중 저절로 터지는 연쇄가 공짜 데미지로 들어갔고, 스탠드업
        /// 중에는 공짜 고정 조각까지 됐다. 양쪽 기준을 맞춘 것이라 "확률을 조작한다"기보다
        /// 원래 그랬어야 하는 동작에 가깝다.
        ///
        /// 방법: 무작위 색에서 시작해 팔레트를 한 바퀴 돌며, 그 색을 놓았을 때 4개 이상(MinRemoveCount)
        /// 이어지지 않는 첫 색을 고른다. 한 바퀴를 다 돌아도 없으면(어느 색을 놔도 매치가 되는 배치)
        /// 처음 뽑은 색을 그대로 쓴다 - 억지로 피하려다 리필이 멈추는 것보다 낫다.
        ///
        /// 주의: 여기서 보는 건 <b>평소 연결 규칙</b>이다. 스탠드업 중 고정된 조각(StandHeld)을
        /// 타고 이어지는 판정(FindConnectedGroupThroughStandHeld)까지는 보지 않는다 - 그것까지
        /// 막으면 스탠드업에서 조각이 떨어져 정사각형이 완성되는 재미 자체가 줄어든다.
        /// </summary>
        private int PickRefillIndex(int x, int y, bool avoidImmediateMatch)
        {
            if (!avoidImmediateMatch || paletteSize <= 1)
                return PickWeightedIndex();

            int first = PickWeightedIndex();

            for (int offset = 0; offset < paletteSize; offset++)
            {
                int candidate = (first + offset) % paletteSize;

                // 실제로 놓아보고 몇 개가 이어지는지 센 뒤 되돌린다. 안착 시간은 0으로 두는데,
                // 미안착으로 두면 IsConnectable이 false라 아무것도 안 이어져서 검사가 무의미해진다
                // (어차피 안착한 뒤엔 매치 대상이 되므로 "안착한 상태"로 봐야 맞다).
                Board.Set(x, y, new Cell { kind = CellKind.Normal, panelIndex = candidate });
                ConnectionFinder.FillConnectedGroup(Board, x, y, null, refillProbeBuffer);
                int connected = refillProbeBuffer.Count;
                Board.Clear(x, y); // 원래 상태(Empty)로 복구 - 호출부가 곧 진짜 값을 써 넣는다

                if (connected < ConnectionFinder.MinRemoveCount)
                    return candidate;
            }

            return first;
        }

        /// <summary>
        /// 색이 다시 나올 <b>가중치</b>. 길이는 팔레트 크기이고, 기본은 전부 1이다.
        ///
        /// ⭐ <b>BoardManager 는 스티커를 모른다</b> - "이 색이 얼마나 자주 나와야 하는가"라는
        /// 숫자만 받는다. 그래서 나중에 다른 것(스테이지 규칙, 적의 방해)이 확률을 건드려도
        /// 이 통로를 그대로 쓴다. 넘긴 배열은 <b>그대로 들고 있으니</b> 부르는 쪽이 값을 고치면
        /// 바로 반영된다(리더 리젠 버스트처럼 잠깐 올렸다 되돌리는 효과가 이걸 쓴다).
        /// </summary>
        public void SetRefillWeights(float[] weights)
        {
            refillWeights = weights != null && weights.Length == paletteSize ? weights : null;
        }

        private float[] refillWeights;

        /// <summary>
        /// 가중치대로 색 하나를 뽑는다. 가중치가 없거나 합이 0이면 <b>고르게</b> 뽑는다 -
        /// 스티커를 하나도 안 붙였을 때 예전과 똑같이 굴러가야 하기 때문이다.
        /// </summary>
        private int PickWeightedIndex()
        {
            if (refillWeights == null)
                return rng.Next(paletteSize);

            float total = 0f;
            for (int i = 0; i < paletteSize; i++)
            {
                if (refillWeights[i] > 0f)
                    total += refillWeights[i];
            }

            if (total <= 0f)
                return rng.Next(paletteSize);

            double roll = rng.NextDouble() * total;
            for (int i = 0; i < paletteSize; i++)
            {
                if (refillWeights[i] <= 0f)
                    continue;

                roll -= refillWeights[i];
                if (roll < 0d)
                    return i;
            }

            return paletteSize - 1;
        }

        /// <summary>
        /// 낙하/리필이 끝난 뒤, 우연히 새로 생긴 매치가 있는지 보드 전체를 스캔.
        /// 플레이어가 직접 만든 매치가 아니라 "떨어지다 우연히 맞춰진" 연쇄(체인) 매치를 찾는 용도.
        /// lockedCells: 지금 다른 매치가 처리 중인 칸 - 벽처럼 취급해서 새 그룹에 절대 섞이지 않게 함.
        /// includeStandHeld: 스탠드업 타임 중이면 true로 넘겨서, 새로 떨어진/변환된 조각이 이미
        /// 고정된(StandHeld) 같은 색 무더기까지 이어서 연결 판정하게 함.
        /// </summary>
        public List<ConnectionResult> ScanBoardForMatches(ISet<(int x, int y)> lockedCells = null, bool includeStandHeld = false)
        {
            // 결과 리스트와 "매치가 성립한 그룹"만 새로 만든다 - 호출부가 코루틴 프레임 너머까지
            // 들고 다니기 때문. 나머지(방문 표시, 탐색 버퍼)는 전부 돌려쓴다.
            var results = new List<ConnectionResult>();

            int stride = Board.width;
            int needed = Board.width * Board.height;
            if (scanVisited == null || scanVisited.Length < needed)
                scanVisited = new bool[needed];
            else
                System.Array.Clear(scanVisited, 0, needed);

            for (int y = 0; y < Board.height; y++)
            {
                for (int x = 0; x < Board.width; x++)
                {
                    if (scanVisited[y * stride + x])
                        continue;

                    if (lockedCells != null && lockedCells.Contains((x, y)))
                    {
                        scanVisited[y * stride + x] = true; // 잠긴 칸은 시작점으로도 쓰지 않음 - 통과만 표시하고 건너뜀
                        continue;
                    }

                    // 여기서 나오는 그룹은 대부분 4개 미만이라 그대로 버려진다 - 칸마다 리스트를
                    // 새로 만들면 스캔 한 번에 수십 개가 GC로 가므로 공용 버퍼에 받는다.
                    if (includeStandHeld)
                        ConnectionFinder.FillConnectedGroupThroughStandHeld(Board, x, y, lockedCells, scanGroupBuffer);
                    else
                        ConnectionFinder.FillConnectedGroup(Board, x, y, lockedCells, scanGroupBuffer);

                    for (int i = 0; i < scanGroupBuffer.Count; i++)
                    {
                        var (gx, gy) = scanGroupBuffer[i];
                        scanVisited[gy * stride + gx] = true;
                    }

                    // 특수 패널만으로 이뤄진 무리는 매치가 아니다(ConnectionFinder.CanRemoveGroup).
                    if (ConnectionFinder.CanRemoveGroup(Board, scanGroupBuffer))
                    {
                        // 살아남은 그룹만 호출부가 들고 갈 수 있게 복사해서 넘긴다.
                        results.Add(new ConnectionResult
                        {
                            cells = new List<(int x, int y)>(scanGroupBuffer),
                            canRemove = true,
                            createsBox = BoxCreationEnabled
                                         && scanGroupBuffer.Count >= ConnectionFinder.BoxCreateCount,
                            panelIndex = Board.Get(scanGroupBuffer[0].x, scanGroupBuffer[0].y).panelIndex
                        });
                    }
                }
            }

            return results;
        }

        // 보드 전체 스캔용 재사용 버퍼. 스캔은 캐스케이드 한 단계마다 도는 가장 잦은 경로라
        // 여기서 새로 만들면 그대로 GC 부담이 된다. 스캔 중에 다른 스캔이 끼어들지 않으므로
        // (전부 동기적으로 끝남) 하나를 돌려써도 안전하다.
        private bool[] scanVisited;
        private readonly List<(int x, int y)> scanGroupBuffer = new List<(int x, int y)>();

        /// <summary>
        /// 판정된 그룹을 실제로 보드 데이터에 반영(제거 또는 박스 전환). 3D 수집 애니메이션이
        /// 끝난 뒤 호출해야 함 - 그 전까지는 데이터가 그대로 유지되어야 하기 때문.
        /// anchorX/anchorY: 박스가 생성될 위치 (플레이어 매치는 "손을 뗀 자리", 체인 매치는 그룹의 첫 셀).
        /// </summary>
        public void ResolveGroup(ConnectionResult group, int anchorX, int anchorY)
        {
            if (group.createsBox)
            {
                // <b>특수 패널은 박스가 되지 않는다</b> - 시트의 "변환이 불가능한"을 여기까지 지킨다.
                // 앵커가 특수 패널이면 무리 안의 다른 칸으로 옮기고, 그런 칸이 없으면 박스를 안 만든다.
                if (Board.Get(anchorX, anchorY).IsSpecial)
                {
                    bool moved = false;
                    foreach (var (cx, cy) in group.cells)
                    {
                        if (Board.Get(cx, cy).IsSpecial)
                            continue;

                        anchorX = cx;
                        anchorY = cy;
                        moved = true;
                        break;
                    }

                    if (!moved)
                    {
                        foreach (var (cx, cy) in group.cells)
                            ClearOrSpendSpecial(cx, cy);
                        return;
                    }
                }

                // 판정 시점(Evaluate/ScanBoardForMatches)에 미리 캡처해둔 색을 그대로 사용.
                // 여기서 다시 Board.Get(anchorX,anchorY)로 읽으면, 이펙트 재생 중에 그 칸이
                // 다른 조작(다른 색 퍼즐 드롭 등)으로 바뀌었을 때 엉뚱한 색의 박스가 생기는 버그가 있었음.
                Board.Set(anchorX, anchorY, new Cell { kind = CellKind.Box, panelIndex = group.panelIndex });

                foreach (var (cx, cy) in group.cells)
                {
                    if (cx == anchorX && cy == anchorY)
                        continue;
                    ClearOrSpendSpecial(cx, cy);
                }
            }
            else
            {
                foreach (var (cx, cy) in group.cells)
                    ClearOrSpendSpecial(cx, cy);
            }
        }

        /// <summary>
        /// 매치로 없앨 칸 하나를 처리한다. <b>특수 패널은 바로 사라지지 않고 횟수를 하나 쓴다</b>
        /// (미스틱의 포지셔닝, 2026-08-30 사용자 기획). 0이 되는 매치에서 비로소 같이 사라진다.
        /// </summary>
        /// <summary>
        /// 주어진 칸들이 속한 <b>특수 퍼즐 뭉치 전체</b>를 모은다. 매치에 두 칸만 걸렸어도
        /// <b>네 칸이 통째로 움직이기</b> 때문에 온전한 뭉치를 알아야 한다(2026-08-30 사용자 확정).
        /// 특수 칸끼리 이어진 것만 따라간다.
        /// </summary>
        public void CollectSpecialCluster(IReadOnlyList<(int x, int y)> seeds, List<(int x, int y)> into)
        {
            into.Clear();
            if (seeds == null)
                return;

            for (int i = 0; i < seeds.Count; i++)
            {
                if (Board.InBounds(seeds[i].x, seeds[i].y) && Board.Get(seeds[i].x, seeds[i].y).IsSpecial
                    && !into.Contains(seeds[i]))
                {
                    into.Add(seeds[i]);
                }
            }

            // 뭉치가 커봐야 네 칸이라 목록을 그대로 훑는 것으로 충분하다.
            for (int i = 0; i < into.Count; i++)
            {
                var (x, y) = into[i];

                foreach (var (dx, dy) in OrthogonalOffsets)
                {
                    var next = (x + dx, y + dy);
                    if (!Board.InBounds(next.Item1, next.Item2) || into.Contains(next))
                        continue;

                    if (Board.Get(next.Item1, next.Item2).IsSpecial)
                        into.Add(next);
                }
            }
        }

        /// <summary>그 칸들을 비운다. 특수 퍼즐 뭉치를 통째로 걷어낼 때 쓴다.</summary>
        public void ClearCells(IReadOnlyList<(int x, int y)> cells)
        {
            if (cells == null)
                return;

            for (int i = 0; i < cells.Count; i++)
            {
                if (Board.InBounds(cells[i].x, cells[i].y))
                    Board.Clear(cells[i].x, cells[i].y);
            }
        }

        /// <summary>그 뭉치에 남아 있는 매치 횟수(가장 큰 값). 뭉치가 아니면 0.</summary>
        public int SpecialMatchesLeftIn(IReadOnlyList<(int x, int y)> cells)
        {
            int left = 0;
            if (cells == null)
                return 0;

            for (int i = 0; i < cells.Count; i++)
            {
                var cell = Board.Get(cells[i].x, cells[i].y);
                if (cell.IsSpecial && cell.specialMatchesLeft > left)
                    left = cell.specialMatchesLeft;
            }

            return left;
        }

        /// <summary>
        /// 그 칸들에 <b>지금 놓여 있는 조각의 전투력 합</b>. 미스틱이 특수 퍼즐을 심으면서
        /// 원래 있던 조각을 지울 때, 그만큼을 그대로 적에게 준다(2026-08-30 사용자 기획).
        /// 강화된 조각은 배율만큼 더 센다(매치·스탠드업과 같은 기준).
        /// 색이 없는 칸(방해블록·구멍·빈 칸)은 0이다.
        /// </summary>
        public float SumCombatPower(IReadOnlyList<(int x, int y)> cells, System.Func<int, float> powerOf)
        {
            float total = 0f;
            if (cells == null || powerOf == null)
                return 0f;

            for (int i = 0; i < cells.Count; i++)
            {
                if (!Board.InBounds(cells[i].x, cells[i].y))
                    continue;

                var cell = Board.Get(cells[i].x, cells[i].y);
                if (cell.panelIndex < 0)
                    continue;

                if (cell.kind != CellKind.Normal && cell.kind != CellKind.StandHeld
                    && cell.kind != CellKind.Box && cell.kind != CellKind.Special)
                    continue;

                total += powerOf(cell.panelIndex) * cell.DamageWeight;
            }

            return total;
        }

        private void ClearOrSpendSpecial(int x, int y)
        {
            var cell = Board.Get(x, y);

            if (!cell.IsSpecial)
            {
                Board.Clear(x, y);
                return;
            }

            // <b>특수 퍼즐도 그 자리에서는 사라진다</b>(2026-08-30 규칙 변경) - 예전엔 제자리에
            // 남아 횟수만 줄였는데, 이제는 접힌 뒤 <b>다른 자리에 새로 생긴다</b>.
            // 새로 심는 건 호출부(BoardInputController.ResolveSingleGroup)가 한다.
            Board.Clear(x, y);
        }

        /// <summary>
        /// ResolveGroup의 편의 오버로드 - 체인(캐스케이드) 매치처럼 별도 앵커 개념이 없을 때
        /// 그룹의 첫 셀을 자동으로 앵커(박스 생성 위치)로 사용.
        /// </summary>
        public void ResolveGroup(ConnectionResult group)
        {
            var anchor = group.cells[0];
            ResolveGroup(group, anchor.x, anchor.y);
        }

        /// <summary>
        /// 스탠드업 타임 중 매치된 그룹을 제거하는 대신 그 자리에 고정(StandHeld)시킴.
        /// 원래 색(panelIndex)은 그대로 유지 - 시각적으로는 같은 이미지가 계속 보이고,
        /// 데이터상으로만 더 이상 이동/매치 대상이 아닌 고정 장애물이 됨.
        /// </summary>
        public void HoldGroupAsStandHeld(ConnectionResult group)
        {
            foreach (var (cx, cy) in group.cells)
            {
                var cell = Board.Get(cx, cy);

                // 강화 배율은 반드시 따라가야 한다. 새 Cell 을 통째로 만들면서 이걸 빠뜨리면,
                // "리더가 구역 변환 → 파트너가 강화 → 스탠드업으로 큰 정사각형" 이라는 이 게임의
                // 핵심 콤보에서 강화가 고정되는 순간 소리 없이 사라진다(화면의 스파크만 남고
                // 데미지에는 반영 안 되는, 원인을 찾기 어려운 형태로).
                // <b>특수 패널도 스탠드업에는 고정 칸이 된다</b>(2026-08-30 사용자 확정).
                // 남은 횟수는 안고 넘어가되 <b>이번 매치 몫으로 하나 쓴다</b> - 매치는 매치다.
                // 스탠드업이 끝날 때 횟수가 남아 있으면 다시 특수 패널로 돌아온다
                // (ClearAllStandHeldCells 참고).
                // 고정되고 나면 남은 횟수는 뜻이 없다 - 새 2x2 가 이미 다른 자리에 생겼다.
                int specialLeft = 0;

                Board.Set(cx, cy, new Cell
                {
                    kind = CellKind.StandHeld,
                    panelIndex = cell.panelIndex,
                    empowerMultiplier = cell.empowerMultiplier,
                    specialMatchesLeft = specialLeft
                });
            }
        }

        /// <summary>
        /// 보드에 남아있는 StandHeld 칸들을, 서로 이어진 무리 단위로 묶어서 반환한다.
        /// 스탠드업 종료 연출에서 "매칭된 퍼즐끼리 불꽃 하나로 합치기" 위해 어떤 칸들이 한 덩어리였는지
        /// 알아야 해서 필요하다. 색이 다르면 이어져 있어도 다른 무리로 나뉜다(같은 panelIndex만 따라감).
        ///
        /// 보드 데이터만 본다. 예전엔 "아직 커밋 안 됐지만 곧 고정될 칸"을 같이 받았다 -
        /// 합체 연출이 커밋보다 먼저 일어나서, 보드만 보면 방금 만든 합체가 안 보였기 때문이다.
        /// 이제 커밋이 먼저라(2026-09-03 연출 규칙) 그 인자를 지웠다 - 다시 들이지 말 것.
        /// 필요해졌다면 어딘가에서 순서가 뒤집힌 것이다.
        /// </summary>
        public List<List<(int x, int y)>> FindStandHeldGroups()
        {
            var groups = new List<List<(int x, int y)>>();

            heldVisited.Clear(); // 호출마다 새로 만들지 않고 돌려쓴다

            for (int x = 0; x < Board.width; x++)
            {
                for (int y = 0; y < Board.height; y++)
                {
                    if (heldVisited.Contains((x, y)) || !IsHeld(x, y))
                        continue;

                    var group = CollectHeldGroup(x, y, heldVisited);
                    if (group.Count > 0)
                        groups.Add(group);
                }
            }

            return groups;
        }

        // 고정 무리 탐색용 재사용 버퍼. 스탠드업 중 매치가 성립할 때마다(합체 재계산 + 데미지 계산)
        // 불리는 경로라, 호출마다 새로 만들면 그만큼 GC로 간다. 무리 목록 자체는 호출부가 들고
        // 다니므로 그것만 새로 만든다.
        private readonly HashSet<(int x, int y)> heldVisited = new HashSet<(int x, int y)>();
        private readonly Queue<(int x, int y)> heldQueue = new Queue<(int x, int y)>();

        private bool IsHeld(int x, int y)
        {
            return Board.Get(x, y).kind == CellKind.StandHeld;
        }

        /// <summary>
        /// (startX, startY)에서 시작해 같은 색으로 이어진 "고정된(또는 곧 고정될)" 칸들을 BFS로 모은다.
        /// visited는 호출부와 공유해서 한 무리를 두 번 세지 않게 한다.
        /// </summary>
        private List<(int x, int y)> CollectHeldGroup(int startX, int startY, HashSet<(int x, int y)> visited)
        {
            var group = new List<(int x, int y)>();
            int targetIndex = Board.Get(startX, startY).panelIndex;
            if (targetIndex < 0)
                return group;

            var queue = heldQueue;
            queue.Clear();
            queue.Enqueue((startX, startY));
            visited.Add((startX, startY));

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                group.Add((cx, cy));

                foreach (var (dx, dy) in OrthogonalOffsets)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;

                    if (!Board.InBounds(nx, ny) || visited.Contains((nx, ny)))
                        continue;

                    if (!IsHeld(nx, ny) || Board.Get(nx, ny).panelIndex != targetIndex)
                        continue;

                    visited.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }

            return group;
        }

        /// <summary>
        /// 스탠드업 타임이 끝났을 때 호출. 보드 전체를 훑어서 StandHeld인 칸을 전부 찾아 즉시
        /// Empty로 비우고(소멸 처리), 비워진 좌표 목록을 반환한다. 뷰 쪽 정리(뷰 detach, "뿅"
        /// 사라지는 연출 재생)는 호출부(BoardInputController.StandUpTimeEndSequenceRoutine) 책임 -
        /// 이 메서드는 데이터만 담당.
        /// </summary>
        public List<(int x, int y)> ClearAllStandHeldCells()
        {
            var cleared = new List<(int x, int y)>();

            for (int x = 0; x < Board.width; x++)
            {
                for (int y = 0; y < Board.height; y++)
                {
                    var cell = Board.Get(x, y);
                    if (cell.kind != CellKind.StandHeld)
                        continue;

                    // <b>되돌리지 않는다</b>(2026-08-30 규칙 변경) - 매치되는 순간 이미 다른 자리에
                    // 새 2x2 가 생겼으므로, 여기서 또 되살리면 특수 퍼즐이 둘로 늘어난다.
                    cleared.Add((x, y));
                    Board.Clear(x, y);
                }
            }

            return cleared;
        }

        /// <summary>
        /// 박스 십자변환 등으로 일부 칸이 다른 색으로 덮어써진 뒤, 그 주변에 남은 StandHeld 무리가
        /// 더 이상 MinRemoveCount(매치 성립 기준, 4개) 이상을 유지하지 못하게 됐다면 - 더 이상
        /// "매치된 상태"라고 볼 수 없으므로 - 그 무리 전체를 다시 움직일 수 있는 일반 패널(Normal)로
        /// 풀어준다. 큰 무리 하나가 다리 역할을 하던 칸이 사라지면서 여러 개의 작은 무리로 쪼개질 수도
        /// 있으므로, 각 무리를 독립적으로 판정한다(하나는 여전히 4개 이상이라 유지, 다른 하나는
        /// 3개 이하로 줄어 해제되는 식으로 서로 다르게 처리될 수 있음).
        /// changedCells: 방금 다른 색으로 바뀌어 더 이상 StandHeld가 아니게 된 칸들 - 이 칸들의
        /// 상하좌우 이웃 중 여전히 StandHeld인 칸부터 탐색을 시작한다.
        /// 반환값: 실제로 풀린(Normal로 전환된) 좌표 목록 - 뷰 쪽 정사각형 합체 해제 판정에도 재사용됨.
        /// </summary>
        public List<(int x, int y)> ReleaseUndersizedStandHeldGroupsNear(IEnumerable<(int x, int y)> changedCells)
        {
            var released = new List<(int x, int y)>();
            var visited = new HashSet<(int x, int y)>();

            foreach (var (cx, cy) in changedCells)
            {
                foreach (var (dx, dy) in OrthogonalOffsets)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;

                    if (!Board.InBounds(nx, ny) || visited.Contains((nx, ny)))
                        continue;

                    if (Board.Get(nx, ny).kind != CellKind.StandHeld)
                        continue;

                    var group = ConnectionFinder.FindConnectedStandHeldGroup(Board, nx, ny);
                    foreach (var cell in group)
                        visited.Add(cell);

                    if (group.Count >= ConnectionFinder.MinRemoveCount)
                        continue; // 여전히 매치 기준을 유지하니 고정 상태 그대로 둠

                    foreach (var (gx, gy) in group)
                    {
                        var cell = Board.Get(gx, gy);
                        Board.Set(gx, gy, new Cell { kind = CellKind.Normal, panelIndex = cell.panelIndex });
                        released.Add((gx, gy));
                    }
                }
            }

            return released;
        }
    }
}