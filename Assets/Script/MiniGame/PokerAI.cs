using System;
using JojoPuzzle.Core;
using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>캐릭터가 이번 베팅에 어떻게 답할지, 그리고 <b>왜 그랬는지</b>.</summary>
    public readonly struct PokerDecision
    {
        public readonly PokerResponse response;

        /// <summary>레이즈일 때 맞춘 뒤에 더 얹는 금액.</summary>
        public readonly long raiseAmount;

        /// <summary>
        /// <b>허세인지</b> - 이길 가망이 낮은데도 세게 나온 것. 대사를 고를 때 쓴다.
        /// 정직한 캐릭터일수록 이 값이 true 가 되는 일이 드물다.
        /// </summary>
        public readonly bool bluffing;

        /// <summary>이 캐릭터가 스스로 매긴 승산(0~1). 대사의 세기를 고르는 데 쓴다.</summary>
        public readonly float confidence;

        public PokerDecision(PokerResponse response, long raiseAmount, bool bluffing, float confidence)
        {
            this.response = response;
            this.raiseAmount = raiseAmount;
            this.bluffing = bluffing;
            this.confidence = confidence;
        }
    }

    /// <summary>
    /// 인디언 포커의 <b>상대 캐릭터</b>. 판단 기준은 전부
    /// <see cref="CharacterPersonality"/>(=Chardata.xlsx 의 성격·욕구 시트)에서 나온다
    /// (2026-09-02 사용자 지시: "엑셀 시트에 있는 캐릭터 성향으로는 충분하지 않나?").
    ///
    /// <code>
    ///   honesty    낮을수록 → 허세(블러프)를 자주 부린다
    ///   courage    높을수록 → 나쁜 패에서도 콜을 받는다
    ///   aggression 높을수록 → 레이즈를 자주 한다
    ///   greed      높을수록 → 레이즈 금액이 커진다
    /// </code>
    ///
    /// <b>인디언 포커의 요점</b>: 이 캐릭터는 <b>자기 패를 모르고 플레이어의 패만 본다.</b>
    /// 그래서 승산은 "플레이어 패보다 높은 수가 나올 확률" 하나로 정해진다 - 플레이어 패가
    /// 낮을수록 자신 있게 나온다. 플레이어도 캐릭터 패를 보고 똑같이 계산하므로, 서로가
    /// 서로의 계산을 읽는 판이 된다.
    ///
    /// <b>성향 애셋이 없으면</b> 전부 50 짜리 무난한 상대가 된다 - 캐릭터가 늘어나도 게임은 돌아간다.
    /// </summary>
    public static class PokerAI
    {
        /// <summary>
        /// 플레이어 패가 <paramref name="playerCard"/> 일 때, 내 패가 그보다 높을 확률.
        /// 내 패는 1~13 중 무엇이든 될 수 있다(같은 수는 무승부라 이기는 축에 안 넣는다).
        /// </summary>
        public static float WinChance(int playerCard)
        {
            int range = IndianPoker.HighestCard - IndianPoker.LowestCard + 1;
            int higher = IndianPoker.HighestCard - playerCard;
            return Mathf.Clamp01(higher / (float)range);
        }

        /// <summary>
        /// 플레이어의 베팅에 어떻게 답할지 정한다.
        /// </summary>
        /// <param name="personality">없으면 전부 50 인 무난한 상대로 친다.</param>
        /// <param name="playerCard">이 캐릭터에게 <b>보이는</b> 플레이어의 패.</param>
        /// <param name="toCall">콜하려면 더 내야 하는 금액.</param>
        /// <param name="pot">지금 판에 쌓인 돈.</param>
        /// <param name="purse">이 캐릭터가 낼 수 있는 최대 금액(소지금에서 이미 낸 몫을 뺀 것).</param>
        public static PokerDecision Decide(CharacterPersonality personality, System.Random rng,
            int playerCard, long toCall, long pot, long purse)
        {
            float honesty = Trait(personality, p => p.honesty);
            float courage = Trait(personality, p => p.courage);
            float aggression = Trait(personality, p => p.aggression);
            float greed = Trait(personality, p => p.greed);

            float chance = WinChance(playerCard);
            double roll = rng.NextDouble();

            // <b>허세</b>: 승산이 낮은데도 세게 나가본다. 정직할수록 이 문이 좁다 -
            // 라미아(정직 10)는 밥 먹듯 속이고 라뷰린스(정직 90)는 거의 안 속인다.
            // 승산이 이미 좋으면 허세가 아니므로 낮을 때만 굴린다.
            float bluffChance = (1f - honesty) * 0.55f;
            bool wantsBluff = chance < 0.45f && roll < bluffChance;

            // <b>접는 선</b>: 이보다 승산이 낮으면 접는다. 배짱이 좋을수록 선이 내려간다.
            float foldLine = 0.34f - courage * 0.26f;

            // <b>지르는 선</b>: 이보다 승산이 좋으면 레이즈한다. 공격적일수록 선이 내려간다.
            float raiseLine = 0.72f - aggression * 0.34f;

            if (!wantsBluff && chance < foldLine)
            {
                // 판돈이 아직 작으면 접기 아깝다 - 앞돈만 걸린 판은 그냥 받아본다.
                bool cheapToStay = toCall <= 0L || toCall * 4L <= pot;
                if (!cheapToStay)
                    return new PokerDecision(PokerResponse.Fold, 0L, false, chance);
            }

            if (wantsBluff || chance >= raiseLine)
            {
                long raise = RaiseAmount(pot, aggression, greed, chance, rng);

                // 낼 수 있는 만큼만 지른다. 콜을 맞추고 나서 남는 돈이 레이즈의 한도다.
                long headroom = purse - toCall;
                if (headroom > 0L)
                {
                    raise = Math.Min(raise, headroom);
                    if (raise > 0L)
                        return new PokerDecision(PokerResponse.Raise, raise, wantsBluff && chance < 0.45f, chance);
                }
            }

            return new PokerDecision(PokerResponse.Call, 0L, false, chance);
        }

        /// <summary>
        /// 얼마나 지를지. 판돈에 비례해서 부르고, 공격성과 탐욕이 그 비율을 키운다.
        /// 확신이 클수록 조금 더 부른다 - 다만 <b>승산에 딱 비례하게 두지 않는다</b>.
        /// 그러면 금액만 보고 상대 속을 읽을 수 있어서 허세가 통하지 않는다.
        /// </summary>
        private static long RaiseAmount(long pot, float aggression, float greed, float chance,
            System.Random rng)
        {
            float ratio = 0.25f + aggression * 0.45f + greed * 0.35f;
            ratio *= 0.85f + chance * 0.3f;

            // 매번 똑같은 금액이면 그것 자체가 정보가 된다. 살짝 흔든다.
            ratio *= 0.8f + (float)rng.NextDouble() * 0.4f;

            long raise = (long)Math.Round(pot * ratio);
            return raise < 1L ? 1L : raise;
        }

        private static float Trait(CharacterPersonality personality, Func<CharacterPersonality, int> pick)
            => personality != null ? CharacterPersonality.Normalized(pick(personality)) : 0.5f;
    }
}
