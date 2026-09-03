using System;
using JojoPuzzle.Core;
using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 블랙잭의 <b>상대 캐릭터</b>. 인디언 포커와 같이 판단 기준은 전부
    /// <see cref="CharacterPersonality"/>(=Chardata.xlsx 의 성격·욕구 시트)에서 나온다.
    ///
    /// <code>
    ///   courage    높을수록 → 높은 수에서도 한 장 더 받는다
    ///   aggression 높을수록 → 같은 뜻으로 조금 더 밀어붙인다
    ///   regularity 높을수록 → <b>매번 같은 선에서 멈춘다</b>(낮으면 들쭉날쭉하다)
    /// </code>
    ///
    /// ⭐ <b>규칙성(regularity)이 여기서 처음 쓰인다.</b> 포커에서는 쓸 자리가 없었는데,
    /// 블랙잭은 "몇에서 멈추나"가 곧 그 사람의 버릇이라 딱 맞는다 - 미스틱(80)은 늘 같은 선에서
    /// 멈추고 루바니아(50)는 그날그날 다르다. 자주 붙어 본 플레이어는 그 버릇을 읽게 된다.
    ///
    /// <b>성향 애셋이 없으면</b> 전부 50 짜리 무난한 상대가 된다.
    /// </summary>
    public static class BlackjackAI
    {
        /// <summary>배짱이 없는 쪽의 멈추는 선. 이보다 아래에서는 무조건 더 받는다.</summary>
        private const float TimidStand = 14f;

        /// <summary>배짱이 좋은 쪽의 멈추는 선.</summary>
        private const float BoldStand = 19f;

        /// <summary>
        /// 한 장 더 받을지. true 면 받는다.
        ///
        /// ⭐⭐ <b>딜러는 플레이어의 수를 다 보고 뽑는다</b>(2026-09-02 사용자 지적:
        /// "그냥 진행하면 이기는데 카드를 더 뽑다가 초과해서 지는 경우가 있다").
        /// 캐릭터는 언제나 <b>플레이어가 멈춘 뒤에</b> 뽑으므로, 몇을 이겨야 하는지 이미 안다.
        /// <code>
        ///   이미 앞선다  → 멈춘다. 더 받을 이유가 없다
        ///   뒤진다      → 받는다. 멈추면 그냥 지는 자리다
        ///   같다        → <b>여기서만 성향이 갈린다</b> - 무승부로 만족할지, 이기러 갈지
        /// </code>
        /// ⚠ 그래서 성향이 결과를 가르는 자리는 <b>동점일 때뿐</b>이다. 대신 그 판단은
        /// 여전히 그 사람다워야 하므로 아래 멈추는 선을 그대로 쓴다.
        /// </summary>
        /// <param name="personality">없으면 전부 50 인 무난한 상대로 친다.</param>
        /// <param name="total">지금 손패의 값.</param>
        /// <param name="playerTotal">
        /// 이겨야 하는 수. 0 이하면 아직 모른다는 뜻이라 성향대로만 판단한다.
        /// </param>
        public static bool ShouldHit(CharacterPersonality personality, System.Random rng,
                                     int total, int playerTotal = 0)
        {
            if (total >= Blackjack.Target)
                return false;

            if (playerTotal > 0)
            {
                if (total > playerTotal)
                    return false;   // 이대로 멈추면 이긴다

                if (total < playerTotal)
                    return true;    // 멈추면 지는 자리다 - 터지더라도 받아야 한다
            }

            float courage = Trait(personality, p => p.courage);
            float aggression = Trait(personality, p => p.aggression);
            float regularity = Trait(personality, p => p.regularity);

            // 배짱과 공격성이 멈추는 선을 끌어올린다. 배짱이 더 크게 작용한다 -
            // "한 장 더 받는 담력"에 가까운 성질이라서.
            float nerve = courage * 0.7f + aggression * 0.3f;
            float line = Mathf.Lerp(TimidStand, BoldStand, nerve);

            // 동점이라면 이 선이 "무승부로 만족하지 않고 한 장 더 받는" 담력이 된다.
            // 20에서 동점이면 누구나 멈추고, 15쯤이면 배짱 있는 쪽이 이기러 간다.
            // 규칙성이 낮을수록 그 선이 판마다 흔들린다. 높으면 늘 같은 자리에서 멈춘다.
            float wobble = (1f - regularity) * 3f;
            if (wobble > 0f)
                line += ((float)rng.NextDouble() * 2f - 1f) * wobble;

            return total < line;
        }

        private static float Trait(CharacterPersonality personality, Func<CharacterPersonality, int> pick)
            => personality != null ? CharacterPersonality.Normalized(pick(personality)) : 0.5f;
    }
}
