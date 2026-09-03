using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JojoPuzzle.Board;
using JojoPuzzle.Core;

namespace JojoPuzzle.View
{
    /// <summary>
    /// <b>매치가 성립했을 때 무슨 일이 일어나는가.</b>
    ///
    /// 규칙은 <b>둘</b>이고, 둘은 서로 남남이다:
    /// <code>
    ///   스탠드업 타임이면 → 고정한다 (사라지지 않고 그 자리에 박힌다)
    ///   아니면            → 접어 없앤다 (한 점으로 모이며 적을 때린다)
    /// </code>
    /// 위아래로 <b>잠금 → 가려졌으면 대기 → 해제</b> 라는 틀만 같이 쓴다.
    /// 규칙이 하나 더 생기면 <see cref="Resolve"/> 의 갈림길에 한 줄을 더하면 된다.
    ///
    /// <b>매치를 처리하는 경로는 넷</b>(플레이어 드롭 / 캐스케이드 / 안착 재스캔 / 십자변환 직후)
    /// 인데 전부 여기를 지난다 - 그래서 억제나 판정을 스캔하는 쪽마다 붙이면 하나만 빠뜨려도
    /// 구멍이 난다. 실제로 그렇게 새던 버그가 있었다.
    ///
    /// <b>MonoBehaviour 가 아니다.</b> 코루틴은 입력 컨트롤러가 자기 것으로 굴린다.
    /// </summary>
    public sealed class MatchResolver
    {
        private readonly BoardManager boardManager;
        private readonly BoardView boardView;
        private readonly BoardCellLocks locks;
        private readonly SpecialPuzzle special;
        private readonly IMatchHost host;

        /// <summary>
        /// 특수 퍼즐이 접히는 속도 배율. 1이면 일반 조각과 같다.
        /// 인스펙터 값이라 주인이 들고 있다.
        /// </summary>
        private readonly float foldSpeed;

        // 이번 매치에 걸린 특수 퍼즐 뭉치. 네 칸이 통째로 움직인다.
        private readonly List<(int x, int y)> specialCluster = new List<(int x, int y)>();

        // 매치에서 실제로 걷어낼 칸(특수 뭉치는 따로 뗀다).
        private readonly List<(int x, int y)> collectTargets = new List<(int x, int y)>();

        /// <summary>
        /// 합체 회전 연출이 재생 중인 코루틴 개수.
        ///
        /// 데이터 커밋은 연출보다 <b>먼저</b> 끝나므로(2026-09-03 연출 규칙) "뒤늦게 커밋되는
        /// 칸을 놓친다"는 예전 문제는 없다. 그래도 세는 이유는 <b>연출</b> 때문이다 -
        /// 회전이 도는 중에 스탠드업 정리가 훑고 지나가면 그 뷰들이 회수되면서
        /// 돌아가던 연출이 엉뚱한 칸에 남는다.
        /// </summary>
        public int MergesPlaying => mergesPlaying;

        private int mergesPlaying;

        public MatchResolver(BoardManager boardManager, BoardView boardView, BoardCellLocks locks,
                             SpecialPuzzle special, IMatchHost host, float foldSpeed)
        {
            this.boardManager = boardManager;
            this.boardView = boardView;
            this.locks = locks;
            this.special = special;
            this.host = host;
            this.foldSpeed = foldSpeed;
        }

        /// <summary>
        /// 매치 그룹 하나를 처리하는 공통 루틴: 그룹 전체를 잠그고 → 데이터 커밋/뷰 정리 →
        /// 접기 연출 재생(완료까지 대기) → 잠금 해제. 플레이어 매치/캐스케이드 매치/십자변환 매치
        /// 전부 공유. 잠금 자체는 이 매치의 접기 연출이 완전히 끝날 때까지 그룹 전체(anchor
        /// 포함)가 계속 잠긴 채로 유지된다 - 그래야 완전히 무관한 다른 매치의 중력/리필/캐스케이드
        /// 스캔이 아직 연출 중인 이 칸을 가로채지 못한다("작업대" 격리). 다만 데이터는 커밋되자마자
        /// 비워지므로, pivot(anchor)을 제외한 칸은 플레이어 예외에 등록해서 "플레이어가
        /// 직접 그 자리에 드래그해서 놓는 것"만 예외적으로 즉시 허용한다(EndDrag만 이걸 참고).
        /// 낙하(중력)는 여러 조각이 동시에 쏟아지는 것처럼 보이지 않도록 접기 연출이 완전히 끝난
        /// 뒤에만 시작돼야 하므로, 이 코루틴 자체는 연출이 끝날 때까지 계속 대기한다(호출부의
        /// GravityAndCascadeRoutine이 그 뒤에 이어짐).
        /// </summary>
        public IEnumerator Resolve(ConnectionResult group, int anchorX, int anchorY)
        {
            // 박스로 생겨난 조각이 하나라도 끼어 있으면 6개 이상이어도 새 박스를 만들지 않는다.
            // 여기서 거르는 이유: 매치를 처리하는 경로가 여럿인데(플레이어 드롭 / 캐스케이드 /
            // 안착 재스캔 / 십자변환 직후 스캔) 전부 이 함수를 지나가므로, 스캔하는 쪽마다
            // 억제를 붙이면 하나만 빠뜨려도 구멍이 난다. 실제로 그렇게 새던 게 이 버그였다.
            if (group.createsBox && boardManager.AnyBornFromBox(group.cells))
                group.createsBox = false;

            // 매치가 성립했으니 힌트 타이머를 처음으로 되돌린다(콤보 카운트와는 무관 - 주석 참고).
            host.NotifyActivity();

            // 이 매치의 pivot 칸 월드 좌표. 손가락을 따라가지 않고 판을 기준으로 잡는다 -
            // 가장자리에서 매치해도 받는 쪽이 중심으로 당겨 띄울 수 있다.
            Vector3 matchWorldPosition = boardView.GridToWorld(anchorX, anchorY);

            foreach (var cell in group.cells)
                locks.Claim(cell);
            locks.Claim((anchorX, anchorY));

            // 가림막이 떠 있거나 스탠드업 개시 배너가 재생 중이면 매치 처리를 미룬다. 지금은 판이
            // 아니라 그 위를 봐야 하는데, 그 위로 조각이 접히고 사라지는 게 겹치면 어수선해지기
            // 때문이다. 구간이 끝나면 그때 평소 연출과 함께 처리된다.
            // 대기는 칸을 잠근 뒤에 한다 - 먼저 잠가둬야 기다리는 동안 다른 코루틴이 이 칸을
            // 가로채지 않는다. 데이터 커밋은 아직 아래에서 일어나므로 매치된 조각은 그 자리에 멈춰 있다.
            while (host.IsResolveFrozen)
                yield return null;

            // 연속 매칭 카운트. 판이 다시 움직이기 시작하는 지금 알린다 - 위 대기 전에 알리면
            // 스킬 컷인으로 화면이 어두운 동안 숫자가 떠 있게 된다.
            host.MatchCounted(matchWorldPosition);

            // 매치가 성립했을 때 <b>무슨 일이 일어나는가</b>는 구간에 따라 완전히 다르다.
            // 위아래 틀(잠금 → 대기 → 해제)만 같고, 가운데 규칙은 서로 남남이다.
            if (host.IsStandUpTimeActive)
                yield return HoldForStandUpRoutine(group);
            else
                yield return FoldAndCollectRoutine(group, anchorX, anchorY);

            foreach (var cell in group.cells)
            {
                locks.Release(cell);
                locks.DisallowPlayer(cell);
            }
            locks.Release((anchorX, anchorY));
        }

        /// <summary>
        /// <b>스탠드업 타임의 규칙: 사라지지 않고 그 자리에 고정된다.</b>
        ///
        /// 방해블록처럼 덮어쓰기도 안 되고 중력도 통과하지 못한다. group.cells 는 새로 합류한
        /// 조각뿐 아니라 <b>이미 고정돼 있던 같은 색 무더기까지</b> 포함한 "지금 이어진 전체
        /// 영역"이다(FindConnectedGroupThroughStandHeld 참고) - 그래서 그 안에서 2x2 이상
        /// 정사각형을 찾아 합치는 것까지 한 번에 처리된다.
        ///
        /// ⭐ <b>데이터를 먼저 커밋하고 연출은 그 뒤다</b>(2026-09-03 연출 규칙).
        ///
        /// anchor 를 안 받는다 - 고정은 <b>모일 자리가 없는</b> 규칙이라 필요가 없다.
        /// 접기 쪽만 조각들이 한 점으로 모이므로 anchor 를 받는다.
        /// </summary>
        private IEnumerator HoldForStandUpRoutine(ConnectionResult group)
        {
            // 스탠드업 타임 중엔 사라지는 대신 그 자리에 고정됨(방해블록처럼 덮어쓰기 불가,
            // 중력도 통과 못 함). group.cells는 새로 합류한 조각 + 기존에 고정돼 있던 같은 색
            // 무더기까지 전부 포함한 "지금 이어진 전체 영역"이므로(FindConnectedGroupThroughStandHeld
            // 참고), 그 안에서 2x2 이상 정사각형을 찾아 회전과 함께 합치는 것까지 BoardView가 처리함.

            // 스킬 게이지는 이번에 "새로" 고정되는 조각만 센다. group.cells에는 이미 고정돼 있던
            // 칸까지 들어 있어서, 무리에 한 칸씩 붙을 때마다 무리 전체를 세면 게이지가 폭주한다.
            // 반드시 HoldGroupAsStandHeld 커밋 전에 세야 한다 - 커밋 뒤엔 전부 StandHeld라 구분이 안 된다.
            int newlyHeldCount = 0;
            foreach (var (cx, cy) in group.cells)
            {
                if (boardManager.Board.Get(cx, cy).kind != CellKind.StandHeld)
                    newlyHeldCount++;
            }

            // <b>이 카운터는 반드시 되돌아와야 한다</b>(2026-08-30) - 스탠드업 종료가
            // <c>while (mergesPlaying > 0)</c> 로 이걸 기다리므로, 한 번이라도
            // 안 줄면 종료 연출이 영영 시작되지 않는다.
            // <b>특수 퍼즐 뭉치도 통째로 같이 고정된다</b>(2026-08-30 사용자 확정) - 매치에
            // 두 칸만 걸렸어도 네 칸이 다 움직이는 규칙은 여기서도 같다. 그리고 아래에서
            // <b>새 2x2 가 다른 자리에 생긴다</b> - 운이 좋으면 그것까지 이어 붙여 큰 정사각형을 만든다.
            boardManager.CollectSpecialCluster(group.cells, specialCluster);
            int standUpSpecialLeft = boardManager.SpecialMatchesLeftIn(specialCluster);

            foreach (var cell in specialCluster)
            {
                if (group.cells.Contains(cell))
                    continue;

                group.cells.Add(cell);

                // 무리에 <b>덧붙이는 칸은 여기서 같이 잠근다</b>(2026-09-03 사용자 신고).
                // 아래 연출이 이 칸까지 고정된 것처럼 보이게 만드는데(ApplyStandUpLook +
                // 정사각형 합체), 데이터가 StandHeld 가 되는 건 0.5초 뒤다. 그 사이 안 잠겨
                // 있으면 동시에 도는 낙하가 이 칸을 끌고 내려가고, 커밋은 <b>원래 좌표</b>에
                // 찍힌다 - 화면엔 고정처럼 보이는데 데이터는 아닌 조각이 남아서, 종료 연출에
                // 흡수되지도 않고 덮어써지기까지 한다.
                // 맨 위의 잠금 루프가 이때는 아직 이 칸을 몰랐다 - 끝의 해제 루프는
                // group.cells 를 돌므로 이걸로 <b>짝이 맞는다</b>.
                locks.Claim(cell);
            }

            // 회전 연출 대상은 <b>커밋 전에</b> 뽑아 둔다 - 커밋 뒤엔 전부 StandHeld라
            // "이번에 새로 합류한 칸"을 데이터에서 구분할 수 없다.
            var newlyJoined = new HashSet<(int x, int y)>();
            foreach (var (cx, cy) in group.cells)
            {
                if (boardManager.Board.Get(cx, cy).kind != CellKind.StandHeld)
                    newlyJoined.Add((cx, cy));
            }

            mergesPlaying++;
            try
            {
                // ⭐ <b>데이터가 먼저다</b>(2026-09-03 연출 규칙). 합체 연출은 이미 고정된
                // 것을 뒤늦게 보여주는 겉보기일 뿐이다.
                //
                // 예전엔 순서가 반대였다. 그 0.5초 동안 화면은 합체된 정사각형인데 데이터는
                // 평범한 조각이라, 그 창으로 정사각형을 통째로 끌어낼 수도 덮어쓸 수도 있었다
                // (2026-09-03 사용자 신고). 커밋을 앞으로 옮기니 CanBeDragged 같은 기존
                // 판정이 저절로 옳은 답을 해서, 창을 막던 특별 장치가 통째로 사라졌다.
                boardManager.HoldGroupAsStandHeld(group);

                yield return boardView.AnimateStandUpLockAndSquareMerge(group.cells, newlyJoined);
            }
            finally
            {
                mergesPlaying--;
            }

            if (newlyHeldCount > 0)
            {
                host.PiecesCleared(group.panelIndex, newlyHeldCount,
                    boardManager.CountEmpowered(group.cells));
            }

            // 고정된 뒤에 새 2x2 를 심는다 - 스탠드업 중에도 자리를 옮기는 건 같다.
            if (standUpSpecialLeft > 0)
            {
                boardView.RefreshSpecialLook();
                yield return special.RelocateRoutine(group.panelIndex, standUpSpecialLeft - 1);
            }
        
        }

        /// <summary>
        /// <b>평소의 규칙: 접어 모아서 없앤다.</b>
        ///
        /// 조각들이 anchor 로 빨려들며 사라지고, 그만큼 적을 때리고 게이지를 채운다.
        /// 여섯 칸 이상이면 anchor 자리에 박스가 남는다.
        ///
        /// ⚠ <b>세는 것은 전부 데이터를 비우기 전에 센다</b> - 강화 배율도, 특수 퍼즐의 남은
        /// 횟수도. 비운 뒤에 세면 이 게임의 핵심 콤보(변환 → 강화 → 매치)에서 강화가
        /// 소리 없이 사라진다.
        /// </summary>
        private IEnumerator FoldAndCollectRoutine(ConnectionResult group, int anchorX, int anchorY)
        {
            // 데이터 커밋(제거/박스 전환)은 즉시 진행하지만, 박스 뷰 스폰은 접기 연출이 다 끝난
            // 뒤로 미룬다 - 안 그러면 옛 조각이 축소되며 사라지는 애니메이션과 새로 나타난 박스가
            // 같은 자리에서 겹쳐 보여서 매치되자마자 박스가 툭 튀어나오는 것처럼 어색해 보임.
            // <b>이번 매치에서 살아남는 특수 패널은 떼지 않는다</b>(2026-08-30) - 데이터는
            // 그 자리에 남는데 뷰만 접혀 사라지면 <b>보이지 않는 조각</b>이 판에 박힌다.
            // 판정은 ResolveGroup 이 횟수를 깎기 <b>전에</b> 해야 한다.
            // <b>특수 퍼즐 뭉치는 통째로, 그리고 두 배 빠르게 접는다</b>(2026-08-30 사용자 기획).
            // 매치에 두 칸만 걸렸어도 네 칸이 다 움직이므로 뭉치 전체를 따로 뗀다.
            boardManager.CollectSpecialCluster(group.cells, specialCluster);
            int specialLeft = boardManager.SpecialMatchesLeftIn(specialCluster);

            collectTargets.Clear();
            foreach (var cell in group.cells)
            {
                if (!specialCluster.Contains(cell))
                    collectTargets.Add(cell);
            }

            Coroutine specialFold = null;
            if (specialCluster.Count > 0)
            {
                var specialViews = boardView.DetachGroupForCollectEffect(specialCluster);
                boardManager.ClearCells(specialCluster);

                specialFold = host.Run(boardView.AnimateDetachedCollectEffect(
                    specialViews, anchorX, anchorY, false, null,
                    Mathf.Max(0.05f, foldSpeed)));
            }

            var detachedViews = boardView.DetachGroupForCollectEffect(collectTargets);

            // 강화 배율은 <b>데이터를 비우기 전에</b> 세야 한다. 데미지는 접기 연출이 끝난 뒤에
            // 알리는데(RaiseMatchDamage), 그때는 ResolveGroup 이 이미 칸을 비워서 강화 표시가
            // 사라진 뒤다. 여기서 실효 칸 수를 미리 구해 그대로 들고 간다.
            float matchWeight = boardManager.SumDamageWeight(group.cells);

            // 강화 조각 수도 <b>비우기 전에</b> 센다(강화 배율과 같은 함정).
            int empoweredCount = boardManager.CountEmpowered(group.cells);

            boardManager.ResolveGroup(group, anchorX, anchorY);

            // pivot(anchor)을 제외한 칸은 이미 데이터가 비워졌으니 "플레이어가 직접 그 자리에
            // 드래그해서 놓는 것"만은 접기 연출이 재생되는 동안에도 곧바로 허용한다
            // (플레이어 예외 - EndDrag만 참고함). 다만 잠금 자체에서는
            // 아직 빼지 않는다 - 여기서 빼버리면 지금 완전히 무관한 다른 매치(A)의 중력/리필/
            // 캐스케이드 스캔이 이 칸을 "비어있고 안 잠긴 칸"으로 보고 접기 연출이 채 끝나기도
            // 전에 새 조각을 채워 넣어버려서, 이 매치(B)의 연출이 끝나기도 전에 그 자리가
            // 다시 처리당한 것처럼 겹쳐 보이는 버그가 있었음. 잠금는 각 매치의 접기
            // 연출이 완전히 끝날 때까지 계속 벽으로 유지해서, 서로 다른 매치의 자동 처리(중력/
            // 리필/스캔)가 절대 서로의 칸을 가로채지 못하게 한다 - pivot 자신은 이미 그렇게
            // 처리되고 있었으므로 이제 그룹 전체가 동일하게 "완전히 독립된 작업"으로 취급된다.
            // 룬 개수를 데이터에 맞춘다(횟수가 하나 줄었다).
            boardView.RefreshSpecialLook();

            foreach (var cell in collectTargets)
            {
                if (cell != (anchorX, anchorY))
                    locks.AllowPlayer(cell);
            }

            // 낙하(중력)는 접기 연출이 완전히 끝난 뒤에만 시작돼야 하므로 계속 대기.
            yield return boardView.AnimateDetachedCollectEffect(
                detachedViews, anchorX, anchorY, group.createsBox, host.ChargeGaugeByOnePiece);

            // 접기 연출이 완전히 끝난 지금에서야 박스를 실제로 스폰(옛 조각은 이미 사라졌음).
            if (group.createsBox)
                boardView.ApplyBoxConversion(anchorX, anchorY);

            // <b>특수 퍼즐은 접힌 뒤 다른 자리에 새로 생긴다</b>(2026-08-30 사용자 기획).
            // 접기가 다 끝난 뒤에 심어야 "여기서 사라져 저기서 났다"로 읽힌다.
            if (specialFold != null)
            {
                yield return specialFold;
                yield return special.RelocateRoutine(group.panelIndex, specialLeft - 1);
            }

            // 데미지는 조각이 다 접혀 사라진 뒤에 알린다 - 숫자가 뜨는 타이밍이 연출과 맞아야
            // "이 매치가 이만큼 때렸다"로 읽힌다. 연출이 중간에 취소돼도(CancelAllCollectEffects)
            // 그 코루틴은 즉시 정리하고 반환하므로 데미지는 빠짐없이 발행된다.
            // 스탠드업 타임 분기(위쪽 if)는 이 경로를 타지 않으므로 고정된 조각은 제외된다.
            host.RaiseMatchDamage(group, matchWeight);

            // 스킬 게이지 충전용 알림. 박스가 되는 매치는 앵커 한 칸이 박스로 남아 실제로는
            // 지워지지 않으므로 그만큼 빼서 알린다.
            int clearedCount = group.createsBox ? group.Count - 1 : group.Count;
            if (clearedCount > 0)
            {
                host.PiecesCleared(group.panelIndex, clearedCount, empoweredCount);
            }
        
        }
    }
}
