using System.Collections;
using UnityEngine;
using JojoPuzzle.App;
using JojoPuzzle.Board;
using JojoPuzzle.Core;
using JojoPuzzle.View;

namespace JojoPuzzle.Battle
{
    /// <summary>
    /// 붙여 둔 스티커 중 <b>판이 도는 동안 듣는 것</b>들을 한곳에서 굴린다.
    ///
    /// <code>
    ///   리젠(BlockRegen · DuplicateRegen) → 리필 가중치로 옮긴다
    ///   리더 리젠 버스트(LeaderRegenBurst) → 시작하고 잠깐 리더 색을 더 얹었다 되돌린다
    /// </code>
    ///
    /// ⭐ <b>스티커를 아는 자리를 여기 하나로 모은다.</b> 판을 굴리는 쪽(BoardManager)은
    /// "이 색이 얼마나 자주 나와야 하는가"라는 숫자만 받는다 - 스티커가 늘어도 그쪽은 안 고친다.
    /// 반대로 이걸 전투 곳곳에 흩으면 하나 빠뜨렸을 때 <b>조용히 안 듣는다</b>.
    ///
    /// <b>MonoBehaviour 가 아니다</b> - 코루틴은 주인(GameEntryPoint)이 자기 것으로 굴린다.
    /// </summary>
    public sealed class StickerBattleEffects
    {
        private readonly BoardManager boardManager;
        private readonly BoardView boardView;
        private readonly int paletteSize;

        /// <summary>
        /// 색마다의 리필 가중치. <b>BoardManager 에 그대로 넘겨 두고 여기서 값만 고친다</b> -
        /// 버스트가 끝날 때 되돌리려면 같은 배열을 계속 들고 있어야 한다.
        /// </summary>
        private readonly float[] refillWeights;

        public StickerBattleEffects(BoardManager boardManager, BoardView boardView, int paletteSize)
        {
            this.boardManager = boardManager;
            this.boardView = boardView;
            this.paletteSize = Mathf.Max(0, paletteSize);
            refillWeights = new float[this.paletteSize];
        }

        /// <summary>
        /// 판이 시작할 때 한 번. 리젠 스티커를 가중치로 옮기고 판에 물려 준다.
        ///
        /// 가중치는 <b>1 + 보너스</b>다 - 아무것도 안 붙였으면 전부 1이라 예전과 똑같이 고르게 나온다.
        /// </summary>
        public void ApplyRefillWeights()
        {
            if (paletteSize <= 0)
                return;

            for (int i = 0; i < paletteSize; i++)
                refillWeights[i] = 1f + RegenBonusOf(i);

            // 중복색이 있으면 <b>원본색</b>이 더 자주 나온다(시트: "중복색이 있을 경우,
            // 원본색 퍼즐 블록 리젠 확률 +N%"). 색을 갈아 낀 건 파트너 쪽이므로,
            // 원본색은 리더의 색이다.
            // ⚠ 숫자는 <b>시트가 정한다</b> - 여기 적어 두면 시트를 고칠 때마다 주석이 거짓말이 된다.
            if (HasDuplicateColor())
                refillWeights[LeaderIndex] += StickerEffects.DuplicateRegenBonus();

            boardManager.SetRefillWeights(refillWeights);
        }

        /// <summary>
        /// 시작하고 잠깐 리더 색이 더 자주 나온다(시트: "시작시 N초 동안 리더 캐릭터의
        /// 퍼즐 블록 리젠 확률 +M%"). <b>N 은 seconds, M 은 value</b> 로 들어온다.
        ///
        /// ⚠ <b>멈춘 시간은 안 센다</b>(시트에 그렇게 적혀 있다) - 대사창·스킬 연출로 판이
        /// 멈춰 있는 동안 시계가 흐르면, 플레이어가 아무것도 못 한 사이에 효과가 다 녹는다.
        /// 다른 시계들(제한시간·스탠드업·안착)과 같은 방침이다.
        ///
        /// ⚠⚠ <b>다른 효과와 중첩되지 않는다</b>(2026-09-03 시트에 붙은 조건). 그래서 더하지 않고
        /// <b>덮어쓴다</b> - 리더 색에 리젠 스티커가 이미 붙어 있어도 이 시간 동안은 이 값만 듣는다.
        /// 끝나면 원래 계산값으로 되돌린다.
        /// </summary>
        public IEnumerator RunLeaderRegenBurstRoutine(System.Func<bool> isPaused)
        {
            var sticker = StickerEffects.FindAttached(StickerEffect.LeaderRegenBurst);
            if (sticker == null || paletteSize <= 0)
                yield break;

            float duration = sticker.seconds;
            if (duration <= 0f)
                yield break;

            // 중첩 불가라 <b>덮어쓴다</b>. 되돌릴 값을 먼저 적어 둔다.
            float restore = refillWeights[LeaderIndex];
            refillWeights[LeaderIndex] = 1f + sticker.value * 0.01f;

            try
            {
                float left = duration;
                while (left > 0f)
                {
                    if (isPaused == null || !isPaused())
                        left -= Time.deltaTime;

                    yield return null;
                }
            }
            finally
            {
                // <b>반드시 되돌린다</b> - 안 되돌리면 판이 끝날 때까지 리더 색만 쏟아진다.
                refillWeights[LeaderIndex] = restore;
            }
        }

        /// <summary>
        /// N초마다 아군 스킬 게이지를 M% 채운다(시트: "N초마다 아군 캐릭터의 스킬 게이지
        /// M% 회복(정지된 시간 제외)").
        ///
        /// ⚠ <b>멈춘 시간은 안 센다</b> - 버스트와 같은 이유다.
        /// </summary>
        public IEnumerator RunSkillGaugeOverTimeRoutine(System.Func<bool> isPaused,
            System.Action<float> chargeAll)
        {
            var sticker = StickerEffects.FindAttached(StickerEffect.SkillGaugeOverTime);
            if (sticker == null || chargeAll == null)
                yield break;

            float interval = sticker.seconds;
            float fraction = sticker.value * 0.01f;
            if (interval <= 0f || fraction <= 0f)
                yield break;

            float left = interval;
            while (true)
            {
                if (isPaused == null || !isPaused())
                    left -= Time.deltaTime;

                if (left <= 0f)
                {
                    chargeAll(fraction);
                    left = interval;
                }

                yield return null;
            }
        }

        /// <summary>리더는 팔레트 0번이다(BattleSetup.BuildPalette 가 그렇게 넣는다).</summary>
        private const int LeaderIndex = 0;

        private float RegenBonusOf(int panelIndex)
        {
            var color = boardView != null ? boardView.ColorOf(panelIndex) : null;
            return color.HasValue ? StickerEffects.RegenBonus(color.Value) : 0f;
        }

        private bool HasDuplicateColor()
        {
            if (boardView == null)
                return false;

            for (int i = 0; i < paletteSize; i++)
            {
                if (boardView.IsDuplicateColor(i))
                    return true;
            }

            return false;
        }
    }
}
