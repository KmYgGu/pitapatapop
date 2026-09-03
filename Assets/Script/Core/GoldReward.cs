using System.Collections.Generic;

namespace JojoPuzzle.Core
{
    /// <summary>골드가 불어난 이유. 결과 화면이 줄마다 무슨 문구를 쓸지 정하는 데 쓴다.</summary>
    public enum GoldRewardSource
    {
        /// <summary>제거한 조각 수에서 나오는 밑돌.</summary>
        Base,

        /// <summary>플레이어 레벨 보정.</summary>
        PlayerLevel,

        /// <summary>러시 타임 - 보너스 시간에 직접 벌어들인 골드.</summary>
        RushTime,

        /// <summary>적 레벨이 높을수록 얹히는 몫. <b>이겼을 때만</b> 붙는다.</summary>
        EnemyLevel,

        /// <summary>배틀 전에 산 "획득 코인량 증가" 아이템.</summary>
        CoinItem,

        /// <summary>스티커북의 <b>배율</b> 효과("최종 코인 획득량 증가").</summary>
        StickerBook,

        /// <summary>
        /// 스티커북의 <b>덧셈</b> 효과("이번 판에 쓴 스킬 수만큼 추가 코인").
        /// 배율과 줄을 나눈 이유: 영수증에 "스티커북"이 두 줄이면 무엇이 무엇인지 안 읽힌다.
        /// </summary>
        StickerSkillCount,

        /// <summary>스티커 "큰 한 방" - 보스 체력의 일부를 넘긴 타격마다 쌓인 코인.</summary>
        StickerBigHit
    }

    /// <summary>영수증 한 줄.</summary>
    public struct GoldRewardLine
    {
        public GoldRewardSource source;

        /// <summary>배율. <see cref="GoldRewardSource.Base"/> 는 1이고 대신 조각 수가 의미를 갖는다.</summary>
        public float multiplier;

        /// <summary>이 줄이 더한 골드.</summary>
        public int added;

        /// <summary>이 줄까지 합친 골드. <b>마지막 줄의 값이 곧 최종 획득 골드다.</b></summary>
        public int runningTotal;
    }

    /// <summary>보상 계산에 필요한 것들. 어디서 왔는지는 계산 쪽이 알 필요 없다.</summary>
    public struct GoldRewardInput
    {
        /// <summary>이번 판에 매치한 조각 수 누적.</summary>
        public int piecesMatched;

        /// <summary>
        /// 러시 타임에 직접 벌어들인 골드. 러시를 못 갔으면 0.
        ///
        /// <b>배율이 아니라 실제로 번 금액이다</b>(2026-08-25 사용자 결정). 예전에는 "러시를 갔다"는
        /// 사실만으로 전체에 x1.5를 곱했는데, 러시 타임이 실제 플레이 구간이 되면서 그 배율을
        /// 없애고 <b>그 시간에 번 만큼</b>을 그대로 얹는 것으로 바꿨다. 둘 다 두면 같은 보상이
        /// 두 번 계산된다.
        /// </summary>
        public int rushGold;

        /// <summary>플레이어 레벨(캐릭터 레벨이 아니다).</summary>
        public int playerLevel;

        /// <summary>
        /// 쓰러뜨린 적의 레벨(스테이지의 권장 레벨). <b>이겼을 때만</b> 보너스가 된다 -
        /// 강한 적을 잡은 값이라 지고서 받을 이유가 없다(2026-08-27 사용자 기획).
        /// </summary>
        public int enemyLevel;

        /// <summary>이번 판을 이겼는지. 적 레벨 보너스가 붙을지를 가른다.</summary>
        public bool isVictory;

        /// <summary>"획득 코인량 증가" 아이템의 배율. 안 샀으면 1.</summary>
        public float coinItemMultiplier;

        /// <summary>스티커북의 배율 효과("최종 코인 획득량 증가"). 안 붙였으면 1.</summary>
        public float stickerBookMultiplier;

        /// <summary>
        /// 스티커북의 덧셈 효과("쓴 스킬 수만큼 추가 코인")로 얹을 금액. 안 붙였으면 0.
        /// <b>배율이 다 곱해진 뒤에</b> 더한다 - 스티커가 준 코인에 다시 배율이 붙으면 두 번 세는 셈이다.
        /// </summary>
        public int stickerSkillCoins;

        /// <summary>스티커 "큰 한 방"으로 번 코인. 이것도 배율이 다 곱해진 뒤에 더한다.</summary>
        public int stickerBigHitCoins;
    }

    /// <summary>
    /// 배틀 한 판의 <b>획득 골드</b>.
    ///
    ///   골드 = (제거한 조각 수 × <see cref="GoldPerPiece"/> + 러시 타임에 번 골드)
    ///          × (1 + 플레이어 레벨 × <see cref="LevelBonusPerLevel"/>)
    ///          × (1 + 적 레벨 × <see cref="EnemyLevelBonusPerLevel"/>)   ← <b>이겼을 때만</b>
    ///          × 코인 증가 아이템 배율
    ///          × 스티커북 배율
    ///
    /// <b>전부 곱연산이다</b>(2026-08-25 사용자 지시 - 레벨은 곱연산). 그런데도 화면에는
    /// 영수증처럼 "한 줄씩 얼마가 더해졌는가"로 보여주므로, 계산도 그 순서 그대로 진행하며
    /// 줄마다 늘어난 몫을 기록한다.
    ///
    /// <b>기준: 코인 아이템도 러시 타임도 없으면 100골드를 넘기기 어렵다</b>(사용자 기획).
    /// 지금 상수로 계산하면 조각 200개 / Lv12 기준 78골드, Lv50이어도 94골드다.
    ///
    /// <b>확정 수치가 아니다.</b> 실제 판에서 조각이 몇 개나 지워지는지 재보고 맞춰야 한다.
    /// 만질 곳은 아래 상수 네 개뿐이다.
    /// </summary>
    public static class GoldReward
    {
        /// <summary>조각 하나당 밑돌 골드.</summary>
        public const float GoldPerPiece = 0.35f;

        /// <summary>플레이어 레벨 1당 더해지는 비율. 0.01 = 레벨당 1%.</summary>
        public const float LevelBonusPerLevel = 0.01f;

        /// <summary>
        /// 적 레벨 1당 더해지는 비율. 플레이어 레벨보다 후하게 잡았다 - 이건 <b>이겼을 때만</b>
        /// 받는 몫이라, 어려운 스테이지에 도전할 이유가 되어야 한다.
        /// </summary>
        public const float EnemyLevelBonusPerLevel = 0.02f;

        /// <summary>이만큼 이상 시간을 남기고 이기면 러시 타임에 들어간다.</summary>
        public const float RushTimeThreshold = 1f / 3f;

        /// <summary>러시 타임에 조각 하나를 지울 때마다 그 자리에서 들어오는 골드.</summary>
        public const float GoldPerRushPiece = 1f;

        /// <summary>이 결과가 러시 타임 조건을 만족하는지.</summary>
        public static bool IsRushTime(float remainingTimeFraction)
            => remainingTimeFraction >= RushTimeThreshold;

        /// <summary>러시 타임에 지운 조각 수를 골드로.</summary>
        public static int RushGoldFor(int piecesCleared)
            => piecesCleared <= 0 ? 0 : (int)(piecesCleared * GoldPerRushPiece);

        /// <summary>
        /// 영수증을 <paramref name="into"/> 에 채운다. <b>새 리스트를 만들지 않는다</b> -
        /// 화면이 버퍼를 재사용한다(이 프로젝트의 "버퍼 채우기" 규칙).
        /// </summary>
        /// <returns>최종 획득 골드.</returns>
        public static int Build(in GoldRewardInput input, List<GoldRewardLine> into)
        {
            into.Clear();

            // 정확한 값은 실수로 굴리고 화면에 쓸 값만 잘라낸다. <b>줄마다 따로 반올림하면
            // 줄의 합과 최종 골드가 어긋난다</b> - 영수증이 안 맞으면 고장 난 것처럼 보인다.
            // 그래서 "지금까지의 합"을 매번 잘라서 직전 합과의 차이를 그 줄의 몫으로 삼는다.
            float exact = input.piecesMatched * GoldPerPiece;
            int shown = Floor(exact);

            into.Add(new GoldRewardLine
            {
                source = GoldRewardSource.Base,
                multiplier = 1f,
                added = shown,
                runningTotal = shown
            });

            // <b>러시 타임에 번 골드는 밑돌 바로 다음에 얹는다.</b> 둘 다 "판에서 직접 벌어온 것"이고,
            // 그 합에 아래 배율들이 걸려야 코인 증가 아이템이 러시 몫에도 통한다.
            if (input.rushGold > 0)
                shown = AddFlat(into, GoldRewardSource.RushTime, input.rushGold, ref exact, shown);

            float levelMultiplier = 1f + input.playerLevel * LevelBonusPerLevel;
            shown = AddStep(into, GoldRewardSource.PlayerLevel, levelMultiplier, ref exact, shown);

            // 적 레벨 보너스는 <b>이겼을 때만</b>. 진 판에서도 밑돌과 플레이어 레벨 보정은
            // 그대로 받지만, "강한 적을 잡은 값"은 잡았을 때만 받는 게 맞다.
            if (input.isVictory && input.enemyLevel > 0)
            {
                float enemyMultiplier = 1f + input.enemyLevel * EnemyLevelBonusPerLevel;
                shown = AddStep(into, GoldRewardSource.EnemyLevel, enemyMultiplier, ref exact, shown);
            }

            if (input.coinItemMultiplier > 1f)
                shown = AddStep(into, GoldRewardSource.CoinItem, input.coinItemMultiplier, ref exact, shown);

            if (input.stickerBookMultiplier > 1f)
                shown = AddStep(into, GoldRewardSource.StickerBook, input.stickerBookMultiplier, ref exact, shown);

            // <b>맨 마지막에 더한다</b> - 이 몫에는 어떤 배율도 안 붙는다(스티커가 준 코인에
            // 다시 레벨·아이템 배율이 곱해지면 같은 보상을 두 번 세는 셈이다).
            if (input.stickerSkillCoins > 0)
                shown = AddFlat(into, GoldRewardSource.StickerSkillCount, input.stickerSkillCoins,
                                ref exact, shown);

            if (input.stickerBigHitCoins > 0)
                shown = AddFlat(into, GoldRewardSource.StickerBigHit, input.stickerBigHitCoins,
                                ref exact, shown);

            return shown;
        }

        /// <summary>
        /// 배율을 한 번 더 곱하고 그만큼 늘어난 몫을 한 줄로 남긴다.
        /// <b>배율이 1이면 줄을 만들지 않는다</b> - 0골드짜리 줄이 영수증에 섞이면 지저분하다.
        /// </summary>
        private static int AddStep(List<GoldRewardLine> into, GoldRewardSource source,
            float multiplier, ref float exact, int previousTotal)
        {
            exact *= multiplier;

            int total = Floor(exact);
            if (total == previousTotal)
                return previousTotal;

            into.Add(new GoldRewardLine
            {
                source = source,
                multiplier = multiplier,
                added = total - previousTotal,
                runningTotal = total
            });

            return total;
        }

        /// <summary>배율이 아니라 금액을 그대로 더하는 줄. 러시 타임처럼 이미 번 골드에 쓴다.</summary>
        private static int AddFlat(List<GoldRewardLine> into, GoldRewardSource source,
            int amount, ref float exact, int previousTotal)
        {
            exact += amount;

            int total = Floor(exact);
            if (total == previousTotal)
                return previousTotal;

            into.Add(new GoldRewardLine
            {
                source = source,
                multiplier = 1f,
                added = total - previousTotal,
                runningTotal = total
            });

            return total;
        }

        // Mathf 를 안 쓰는 이유: 이 클래스는 UnityEngine 에 기대지 않는 순수 계산이라
        // 그대로 테스트할 수 있다(StandUpDamageTable 과 같은 방침).
        private static int Floor(float value) => value <= 0f ? 0 : (int)value;
    }
}
