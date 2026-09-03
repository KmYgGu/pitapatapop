using System;
using System.Collections.Generic;
using JojoPuzzle.Board;
using JojoPuzzle.Core;
using UnityEngine;

namespace JojoPuzzle.View
{
    /// <summary>
    /// <b>막혔을 때 다음 수를 짚어 주는 힌트.</b>
    ///
    /// <code>
    ///   손을 뗀 지 idleDelay 초가 지나면 → 보드에서 성립하는 수를 찾아 → 반짝인다
    ///   무언가 하거나 매치가 나면        → 시계가 처음으로 돌아가고 힌트는 꺼진다
    /// </code>
    ///
    /// ⭐ <b>2.5초는 "마지막으로 뭔가를 한 지 2.5초"다</b>(2026-08-22 사용자 신고로 바뀜).
    /// 매치가 난 순간부터 재면 접기(약 0.3초)와 낙하·리필(약 0.5초)이 그 시간을 먼저 까먹어서,
    /// 평범하게 계속 매치하는 중에도 힌트가 튀어나왔다. 힌트는 <b>막혔을 때</b> 나와야 한다.
    ///
    /// ⭐ <b>판이 멈춘 동안은 시계도 멈춘다</b>(제한시간·안착 타이머와 같은 방침).
    /// 아무것도 못 하는 사이에 시간이 흘러 연출이 끝나자마자 힌트가 튀어나오면 안 된다.
    /// 그 판단은 <b>부르는 쪽</b>이 한다 - 어느 구간이 조작 구간인지는 입력 쪽이 안다.
    ///
    /// <b>MonoBehaviour 가 아니다</b> - 힌트는 시계와 좌표 몇 개일 뿐이라 씬에 놓을 이유가 없다.
    /// 입력 컨트롤러가 하나 들고 매 프레임 <see cref="Tick"/> 을 굴린다.
    /// </summary>
    public sealed class BoardHint
    {
        private readonly BoardManager boardManager;
        private readonly BoardView boardView;

        /// <summary>이 시간(초) 동안 아무것도 안 하면 힌트를 띄운다.</summary>
        private readonly float idleDelay;

        /// <summary>보여줄 수를 못 찾았을 때 다시 찾아보기까지의 간격(초).</summary>
        private readonly float searchInterval;

        /// <summary>
        /// 힌트에서 뺄 칸을 채워 달라고 부르는 것. <b>찾기 직전에만</b> 부른다 -
        /// 매 프레임 훑을 이유가 없다. 델리게이트를 한 번만 받아 두므로 프레임마다 새로 만들지 않는다.
        /// </summary>
        private readonly Action<HashSet<(int x, int y)>> collectBlockedCells;

        public BoardHint(BoardManager boardManager, BoardView boardView,
                         float idleDelay, float searchInterval,
                         Action<HashSet<(int x, int y)>> collectBlockedCells)
        {
            this.boardManager = boardManager;
            this.boardView = boardView;
            this.idleDelay = idleDelay;
            this.searchInterval = searchInterval;
            this.collectBlockedCells = collectBlockedCells;
        }

        // 마지막으로 무언가를 한 뒤 흐른 시간.
        //
        // ⚠ <b>연속 매칭 카운트(콤보)와는 완전히 별개다.</b> 힌트가 떴다고 콤보가 끊기면 안 된다 -
        // 힌트는 도와주는 표시일 뿐 플레이어가 무언가를 잘못한 게 아니다.
        // 콤보를 붙일 때 이 시계를 재활용하지 말고 별도 시계를 둘 것.
        private float idleSeconds;

        private float searchCooldown;
        private bool shown;
        private int panelIndex = -1;
        private (int x, int y) donor = (-1, -1);

        // 좌표 버퍼. 매번 새 리스트를 만들지 않도록 돌려쓴다.
        private readonly List<(int x, int y)> groupCells = new List<(int x, int y)>();
        private readonly List<(int x, int y)> allCells = new List<(int x, int y)>();
        private readonly HashSet<(int x, int y)> blockedCells = new HashSet<(int x, int y)>();

        public bool IsShown => shown;

        /// <summary>
        /// 무언가 일어났다 - 시계를 처음으로 되돌리고 떠 있던 힌트를 끈다.
        ///
        /// ⭐ <b>매치가 났을 때와 플레이어가 손댔을 때가 같은 일이다.</b> 예전에는
        /// <c>NotifyMatchResolved</c>·<c>NotifyPlayerActed</c> 두 이름이 <b>똑같은 본문</b>을
        /// 들고 있었다(2026-09-03 정리하며 확인) - 하나로 합친다.
        /// </summary>
        public void NotifyActivity()
        {
            idleSeconds = 0f;
            searchCooldown = 0f;
            Clear();
        }

        public void Clear()
        {
            if (!shown)
                return;

            shown = false;
            panelIndex = -1;
            donor = (-1, -1);
            allCells.Clear();
            boardView.ClearHint();
        }

        /// <summary>
        /// 매 프레임.
        /// </summary>
        /// <param name="canShow">
        /// 지금 힌트가 나와도 되는 구간인지. <b>false 면 시계도 안 굴린다</b> -
        /// 아무것도 못 하는 사이에 시간이 흘러 구간이 끝나자마자 힌트가 튀어나오면 안 된다.
        /// </param>
        /// <param name="includeStandHeld">
        /// 고정된 조각까지 이어 붙여 매치가 성립하는 구간인지(스탠드업 타임).
        /// ⚠ <b>매치 판정에 넘기는 값과 반드시 같아야</b> 화면의 힌트와 실제 판정이 안 어긋난다.
        /// </param>
        public void Tick(float deltaTime, bool canShow, bool includeStandHeld)
        {
            if (!canShow)
            {
                Clear();   // 조작 불가 구간에 힌트가 남아 반짝이지 않게. 시계는 그대로 둔다.
                return;
            }

            if (shown)
            {
                // ⚠ boardView.IsHintActive 도 같이 본다 - 플레이어가 힌트 조각을 집어 들면
                // 그 뷰가 viewGrid 에서 빠져 뷰 쪽이 스스로 힌트를 꺼버린다. 데이터만 보면
                // 아직 유효해서 "켜져 있다"고 착각한 채 영영 다시 안 띄우게 된다.
                if (boardView.IsHintActive && IsStillValid())
                    return;

                Clear();   // 더 이상 성립하지 않거나 꺼져버렸다 - 다시 찾는다
            }

            idleSeconds += deltaTime;
            if (idleSeconds < idleDelay)
                return;

            searchCooldown -= deltaTime;
            if (searchCooldown > 0f)
                return;

            searchCooldown = Mathf.Max(0.1f, searchInterval);

            blockedCells.Clear();
            collectBlockedCells?.Invoke(blockedCells);

            if (!boardManager.TryFindHint(groupCells, out var found, blockedCells,
                    includeStandHeld: includeStandHeld))
            {
                return;   // 지금 이 판에서는 만들 수 있는 수가 없다 - 다음 간격에 다시 찾는다
            }

            allCells.Clear();
            allCells.AddRange(groupCells);
            allCells.Add(found);

            donor = found;

            // 색은 donor 에서 읽는다. 이어붙일 대상이 전부 고정 조각이면 반짝일 무리가 비어 있어서
            // (고정 조각은 힌트에서 제외한다) 무리의 첫 칸을 볼 수 없다.
            panelIndex = boardManager.Board.Get(found.x, found.y).panelIndex;
            shown = true;
            boardView.ShowHint(allCells);
        }

        /// <summary>
        /// 떠 있는 힌트가 아직 성립하는지 <b>보드 데이터에서 다시 확인한다</b>.
        /// 화면 상태를 증분으로 믿지 않고 매번 데이터에서 재확인하는 이 프로젝트의 방침 그대로다 -
        /// 조각이 낙하로 옮겨지거나 다른 색으로 덮어써지면 힌트는 그 순간 거짓말이 된다.
        ///
        /// ⚠ 무리와 donor 의 조건이 <b>다르다</b>: 무리는 고정된 조각이어도 된다(매치 판정에
        /// 이어 붙으므로). donor 는 플레이어가 실제로 집어서 옮겨야 하므로 반드시 움직일 수 있는
        /// 평범한 조각이어야 한다 - 다른 매치에 휩쓸려 고정되면 그 수는 사라진 것이다.
        /// </summary>
        private bool IsStillValid()
        {
            for (int i = 0; i < allCells.Count; i++)
            {
                var (x, y) = allCells[i];
                if (!boardManager.Board.InBounds(x, y))
                    return false;

                var cell = boardManager.Board.Get(x, y);
                if (cell.panelIndex != panelIndex)
                    return false;

                bool isDonor = (x, y) == donor;
                if (isDonor && !cell.IsConnectable)
                    return false;

                if (!isDonor && !cell.IsConnectable && cell.kind != CellKind.StandHeld)
                    return false;
            }

            return true;
        }
    }
}
