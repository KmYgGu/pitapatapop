using System;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 셀에 들어갈 수 있는 특수 패널 종류.
    /// Normal: 일반 캐릭터 패널 (PanelType 참조)
    /// Obstacle: 오자마 패널 - 스킬/박스로 제거 가능
    /// Hole: 구멍 패널 - 어떤 수단으로도 제거 불가, 영구 장애물
    /// Box: 박스 패널 - 탭하면 십자 5칸을 같은 패널로 변환
    /// StandHeld: 스탠드업 타임 중 매치되어 Y축으로 회전 후 고정된 패널 - 방해블록처럼
    ///            덮어쓰기 불가, 중력 계산에서도 Hole처럼 고정 취급. 스탠드업 타임 종료 시 해제됨.
    /// Empty: 빈 칸 (제거 후 낙하 대기 상태)
    /// </summary>
    public enum CellKind
    {
        Empty,
        Normal,
        Obstacle,
        Hole,
        Box,
        StandHeld,

        /// <summary>
        /// 미스틱의 <b>포지셔닝</b>으로 박아둔 특수 패널(2026-08-30 사용자 기획).
        ///
        /// 색은 있지만(시전자 색) <b>자기들끼리만으로는 매치가 성립하지 않고</b>, 그 자리에 고정돼
        /// 중력·변환·방해블록·상자 십자변환을 전부 버틴다. 매치에 낄 때마다
        /// <see cref="Cell.specialMatchesLeft"/> 가 하나씩 줄고, 0이 되는 매치에서 같이 사라진다.
        /// </summary>
        Special,

        /// <summary>
        /// 유나의 <b>버닝 트랙!</b>이 맨 아랫줄에 놓는 점화 블록(2026-09-01 사용자 기획).
        ///
        /// 색이 없고 매치에도 안 끼며 중력도 받지 않는다. 방해블록이나 다른 스킬도
        /// 이 칸을 덮지 못한다. 다만 큰브처럼 <b>드래그로 옮길 수는 있고</b>,
        /// 여기에 일반 조각을 밀어 넣으면 그 조각의 전투력 × 지워진 칸 수만큼 적을 때리며
        /// <b>자기 열을 자기 행부터 위로</b> 통째로 태운다. 한 번 쓰면 자신도 같이 사라진다.
        /// </summary>
        BurnTrack
    }

    /// <summary>
    /// 보드 한 칸의 상태. 구조체로 두어 배열 순회 시 GC 부담을 줄임.
    /// </summary>
    public struct Cell
    {
        public CellKind kind;
        public int panelIndex; // PanelType 팔레트 내 인덱스. Normal/Box/StandHeld일 때만 유효, 그 외는 -1

        /// <summary>
        /// 안착까지 남은 시간(초). 0 이하면 안착 상태다.
        ///
        /// "판 위에 보이지만 아직 굳지 않은" 조각을 표현한다. 미안착 조각은 화면에는 평범하게
        /// 보이고 집기·놓기·낙하도 그대로 되지만 <b>매치 판정에만 안 잡힌다</b>. 그 사이에
        /// 파트너 스킬의 강화 효과나 적의 방해블록 변환 같은 걸 받을 수 있다.
        ///
        /// 이게 필요한 이유: 예전엔 칸이 채워지는 순간 곧바로 매치 대상이 돼서, 리더 스킬로 구역을
        /// 변환하면 그 조각들이 즉시 처리에 들어가 파트너 스킬을 이어 쓸 틈이 없었다. 간격이 필요한
        /// 기능들이 저마다 임시방편(박스의 1초 정지 등)을 쓰던 걸 이 한 가지 개념으로 모은 것이다.
        ///
        /// 남은 시간을 Cell 안에 두는 게 중요하다 - 낙하로 조각이 옮겨질 때 구조체가 통째로 복사되며
        /// 남은 시간도 같이 따라가고, 칸이 비워지거나 덮어써지면 자연히 사라진다. 별도 표로 관리하면
        /// 그 동기화를 전부 손으로 해야 한다.
        /// </summary>
        public float unsettleRemaining;

        /// <summary>
        /// 박스(큐브) 십자변환으로 <b>생겨난</b> 조각인지.
        ///
        /// 이 조각이 낀 매치는 6개 이상이어도 새 박스를 만들지 않는다. 허용하면 박스 → 같은 색
        /// 5칸 → 즉시 6매치 → 새 박스가 무한히 이어져서 박스를 공짜로 계속 찍어낼 수 있다.
        ///
        /// <b>표를 따로 두지 않고 Cell 안에 두는 게 핵심이다</b>(unsettleRemaining과 같은 이유).
        /// 낙하로 조각이 옮겨지면 구조체가 통째로 복사되며 표시도 따라가고, 칸이 비워지거나
        /// 덮어써지면 자연히 사라진다. 좌표 목록으로 들고 있으면 낙하할 때마다 손으로 옮겨야 한다.
        ///
        /// 예전엔 이 표시 없이 "십자변환 직후의 스캔에서만 createsBox를 끄는" 방식이었는데,
        /// 변환된 조각을 잠시 미안착으로 두게 되면서(boxSettleDuration) 그 스캔에는 아예 안 잡히고
        /// 1초 뒤 안착 재스캔에서 매치가 성립하게 됐다. 그 경로에는 억제가 없어서 박스가 다시
        /// 만들어졌다 - 이 표시가 그 구멍을 막는다.
        /// </summary>
        public bool bornFromBox;

        /// <summary>
        /// 파트너 스킬 등으로 강화된 조각의 <b>데미지 배율</b>(1.5 = 1.5배). 0이나 1 이하면 강화 안 됨.
        /// 강화는 <b>매치되거나 다른 조각으로 덮어써지기 전까지 사라지지 않는다</b> - 시간이 지나
        /// 풀리는 게 아니다.
        ///
        /// 그래서 별도 표가 아니라 Cell 안에 둔다(unsettleRemaining, bornFromBox 와 같은 이유).
        /// 낙하로 조각이 옮겨지면 구조체가 통째로 복사되며 강화도 따라가고, 칸이 비워지거나
        /// 덮어써지면 자연히 사라진다 - 지속 조건이 저절로 맞아떨어진다.
        /// 좌표 목록으로 들고 있으면 낙하할 때마다 손으로 옮겨야 하고, 매치로 사라질 때
        /// 지우는 것도 잊기 쉽다.
        ///
        /// <b>bool 이 아니라 배율을 담는 이유</b>: 강화 배율은 캐릭터(SkillDefinition)마다 다르다.
        /// "강화됐다"만 기억하면 어느 파트너가 걸었든 같은 세기가 되어, 캐릭터가 늘어나는 순간
        /// 스킬을 구분할 수 없다. 배율을 조각에 실어두면 낙하로 따라가는 성질도 그대로 유지된다.
        /// </summary>
        public float empowerMultiplier;

        /// <summary>
        /// 미스틱의 특수 패널이 <b>앞으로 몇 번 더 매치에 쓰일 수 있는지</b>.
        /// <see cref="CellKind.Special"/> 일 때만 뜻이 있고, 0이 되는 매치에서 같이 사라진다.
        ///
        /// <b>스탠드업 타임에는 잠깐 <see cref="CellKind.StandHeld"/> 로 바뀌는데 이 값은 남는다</b>
        /// (사용자 확정) - 스탠드업이 끝날 때 이 값이 남아 있으면 특수 패널로 되돌아온다.
        ///
        /// 표를 따로 두지 않고 Cell 안에 두는 건 <see cref="unsettleRemaining"/> 과 같은 이유다.
        /// </summary>
        public int specialMatchesLeft;

        /// <summary>
        /// 이 <b>특수 블록</b>이 몇 번째로 소환됐는지. 한 번에 소환된 것들이 같은 번호를 나눠 갖는다.
        /// 미스틱의 특수 퍼즐과 유나의 점화 블록이 <b>같은 번호 체계</b>를 쓴다 - 서로 덮어쓸 수
        /// 있어야 하므로 누가 더 새것인지 견줄 수 있어야 한다.
        ///
        /// ⭐ <b>나중에 소환한 쪽이 우선권을 갖는다</b>(2026-09-03 사용자 확정). 판에 무언가를
        /// 새로 놓느라 특수 퍼즐 하나를 지워야 하면 <b>번호가 작은 것부터</b> 내준다 -
        /// 방금 쓴 스킬의 결과물이 곧바로 지워지면 스킬을 쓴 보람이 없기 때문이다.
        /// 판단은 <see cref="CellPlacement"/> 가 한다.
        ///
        /// 표를 따로 두지 않고 Cell 안에 두는 건 <see cref="unsettleRemaining"/> 과 같은 이유다 -
        /// 낙하로 옮겨지면 따라가고, 지워지면 자연히 사라진다.
        /// </summary>
        public int specialSummonOrder;

        /// <summary>
        /// 구멍이 사라지기까지 남은 시간(초). 구멍이 아닌 칸에서는 의미 없다.
        ///
        /// <b>구멍만 시간제한이 있는 이유</b>: 방해블록은 박스나 스킬로 걷어낼 수 있지만 구멍은
        /// <b>어떤 수단으로도 지울 수 없다.</b> 영구히 남으면 판이 조금씩 좁아져서 되돌릴 방법이
        /// 없어지므로, 대신 스스로 사라지게 해서 균형을 맞춘다.
        ///
        /// 다른 시간값(unsettleRemaining)과 달리 이 값은 <b>조각이 아니라 자리에 붙어 있다</b> -
        /// 구멍은 낙하에서 "벽"이라 절대 움직이지 않으므로 구조체가 복사될 일이 없다.
        /// </summary>
        public float holeRemaining;

        public static Cell Empty => new Cell { kind = CellKind.Empty, panelIndex = -1 };

        /// <summary>강화 표시가 붙어 있는지(=배율이 1배를 넘는지). 화면 표시가 이걸 본다.</summary>
        public bool empowered => empowerMultiplier > 1f;

        /// <summary>
        /// 이 칸 하나가 데미지 계산에서 차지하는 무게. 강화 안 된 칸은 1, 강화된 칸은 그 배율.
        /// 일반 매치도 스탠드업 정사각형도 "전투력 × 칸 수"가 뼈대라, 칸 수를 세는 자리를
        /// 전부 이 무게의 합으로 바꾸면 두 공식 모두에 같은 규칙으로 강화가 반영된다.
        /// </summary>
        public float DamageWeight => empowerMultiplier > 1f ? empowerMultiplier : 1f;

        /// <summary>안착이 끝나 매치 판정에 잡힐 수 있는 상태인지.</summary>
        public bool IsSettled => unsettleRemaining <= 0f;

        public bool IsRemovable => kind == CellKind.Normal || kind == CellKind.Box;

        /// <summary>
        /// 같은 색으로 취급되어 연결 판정에 들어갈 수 있는지.
        /// Obstacle/Hole/Empty/Box/StandHeld는 연결(매치) 대상이 아니고, <b>아직 안착하지 않은
        /// 조각도 제외된다</b>(unsettleRemaining 참고 - 매치 판정 경로가 전부 이 프로퍼티를 거치므로
        /// 여기 한 줄이면 스캔·드롭 판정 어느 쪽으로도 미안착 조각이 새어들지 않는다).
        /// (스탠드업 타임 중 StandHeld까지 이어붙는 연결 판정은 ConnectionFinder의
        /// FindConnectedGroupThroughStandHeld/EvaluateThroughStandHeld가 별도로 담당함)
        /// </summary>
        /// <summary>
        /// 매치 판정에 잡히는 칸인지. <b>특수 패널도 잡힌다</b> - 다만 특수 패널<b>만</b>으로는
        /// 매치가 성립하지 않는다(그 규칙은 <see cref="Borad.ConnectionFinder"/> 가 본다).
        /// </summary>
        public bool IsConnectable =>
            (kind == CellKind.Normal || kind == CellKind.Special) && panelIndex >= 0 && IsSettled;

        /// <summary>미스틱의 특수 패널인지.</summary>
        public bool IsSpecial => kind == CellKind.Special;

        /// <summary>
        /// 일반 드래그 조작으로 이 칸 위에 다른 패널을 "덮어씌울 수 없는지" 여부.
        /// Obstacle/Hole/Box뿐 아니라 StandHeld(스탠드업 타임 중 고정된 조각)도 포함.
        /// 나중에 새로운 방해요소가 추가되면 여기 한 곳만 확장하면 전체 시스템에 재사용됨.
        /// </summary>
        public bool BlocksNormalOverwrite => kind == CellKind.Obstacle || kind == CellKind.Hole
            || kind == CellKind.Box || kind == CellKind.StandHeld || kind == CellKind.Special
            || kind == CellKind.BurnTrack;

        /// <summary>
        /// 낙하 계산에서 "벽"인지 - 이 칸 자신이 안 움직이는 건 물론이고, 위에 있는 조각들도
        /// 이 칸을 통과해서 아래로 내려올 수 없다. 구멍은 보드에 뚫린 자리라 실제로 길이 막힌다.
        /// </summary>
        public bool BlocksGravity => kind == CellKind.Hole;

        /// <summary>
        /// 낙하 계산에서 "고정"인지 - 이 칸 자신은 절대 움직이지 않지만, <b>위에 있는 조각들은
        /// 이 칸이 아예 없는 것처럼 통과해서 아래 빈 칸까지 내려온다.</b> 벽(BlocksGravity)과 다른 점이 이것.
        /// 스탠드업 타임에 고정된 조각이 여기 해당한다 - 보드 중간에서 매치가 성립했을 때 그 위 조각들이
        /// 통째로 멈춰 서면, 아래 빈 칸이 전부 새 조각으로 채워져서 "위 조각은 가만히 있는데 밑에서
        /// 새 조각이 솟아나는" 이상한 그림이 된다.
        ///
        /// <b>방해블록도 여기 해당한다</b>(2026-08-21) - 적이 특정 자리에 심어놓은 것이라 그 자리에
        /// 그대로 있어야 한다. 예전엔 일반 조각처럼 아래로 미끄러져서 생긴 자리와 실제로 남는 자리가
        /// 달랐다. <b>벽(BlocksGravity)으로 두면 안 된다</b> - 그러면 그 아래 빈 칸을 영영 채울 수
        /// 없어서 판에 구멍이 남는다(StandHeld를 벽으로 뒀을 때와 똑같은 문제).
        /// </summary>
        /// <b>특수 패널도 여기 해당한다</b>(2026-08-30) - 그 자리에 박혀 있어야 하지만,
        /// 벽으로 두면 그 아래 빈 칸을 영영 못 채워 판에 구멍이 남는다(위 경고와 같은 이유).
        public bool PinnedInGravity => kind == CellKind.StandHeld || kind == CellKind.Obstacle
            || kind == CellKind.Special || kind == CellKind.BurnTrack;

        /// <summary>
        /// 플레이어가 드래그로 집어서 옮길 수 있는 칸인지. 일반 패널뿐 아니라 박스도 이동 가능.
        /// (매치 판정 대상인지를 뜻하는 IsConnectable과는 별개 개념 - 박스는 이동은 되지만 매치는 안 됨)
        /// </summary>
        public bool CanBeDragged => kind == CellKind.Normal || kind == CellKind.Box
            || kind == CellKind.BurnTrack;

        /// <summary>유나의 점화 블록인지.</summary>
        public bool IsBurnTrack => kind == CellKind.BurnTrack;
    }

    /// <summary>
    /// 보드 전체 상태를 담는 순수 데이터 클래스. MonoBehaviour 아님 - 뷰와 완전히 분리.
    /// 좌표계: [col, row], row 0이 맨 아래.
    /// </summary>
    public class BoardData
    {
        public readonly int width;
        public readonly int height;
        private readonly Cell[,] cells;

        public BoardData(int width, int height)
        {
            this.width = width;
            this.height = height;
            cells = new Cell[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    cells[x, y] = Cell.Empty;
        }

        public bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

        public Cell Get(int x, int y) => cells[x, y];

        public void Set(int x, int y, Cell cell) => cells[x, y] = cell;

        public void Clear(int x, int y) => cells[x, y] = Cell.Empty;

        /// <summary>
        /// 디버그/테스트용 얕은 복사.
        /// </summary>
        public BoardData Clone()
        {
            var copy = new BoardData(width, height);
            Array.Copy(cells, copy.cells, cells.Length);
            return copy;
        }
    }
}