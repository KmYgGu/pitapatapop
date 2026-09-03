using System;
using JojoPuzzle.Core;
using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 도둑잡기의 <b>상대 캐릭터</b>. 다른 도박과 같이 판단 기준은 전부
    /// <see cref="CharacterPersonality"/>(=Chardata.xlsx 의 성격 시트)에서 나온다.
    ///
    /// 이 게임은 규칙이 반반이라 <b>성향이 곧 게임 전부</b>다:
    ///
    /// <code>
    ///   [ 밀 때 ]  honesty    낮을수록 → 조커를 내민다(속이려고)
    ///              regularity 높을수록 → 그 버릇을 매번 지킨다(읽히지만 굳건하다)
    ///
    ///   [ 집을 때 ] empathy   높을수록 → 상대가 내민 장을 <b>의심한다</b>
    /// </code>
    ///
    /// ⭐ 그래서 캐릭터마다 <b>속이는 법과 속는 법이 다르다</b> - 라뷰린스(정직 90)가 내민 장은
    /// 대체로 안전하고, 라미아(정직 10)가 내밀면 십중팔구 조커다. 반대로 루바니아(공감 90)는
    /// 이쪽 수를 잘 읽고, 라미아(공감 30)는 잘 넘어온다. 몇 판 붙어 보면 그 버릇이 보인다.
    /// </summary>
    public static class OldMaidAI
    {
        /// <summary>
        /// 캐릭터가 조커를 들었을 때 <b>어느 장을 밀어 올릴지</b>.
        /// </summary>
        /// <returns>밀어 올릴 자리(0 또는 1).</returns>
        public static int ChooseOffer(CharacterPersonality personality, System.Random rng,
            int jokerSlot)
        {
            float honesty = Trait(personality, p => p.honesty);
            float regularity = Trait(personality, p => p.regularity);

            // 정직하지 않을수록 조커를 내민다. 규칙성이 높을수록 그 버릇을 곧이곧대로 지키고,
            // 낮으면 반반에 가까워져 읽히지 않는다.
            float jokerChance = Mathf.Lerp(0.5f, 1f - honesty, regularity);

            bool offerJoker = rng.NextDouble() < jokerChance;
            return offerJoker ? jokerSlot : 1 - jokerSlot;
        }

        /// <summary>
        /// 플레이어가 조커를 들었을 때 캐릭터가 <b>어느 장을 집을지</b>.
        ///
        /// 캐릭터는 어느 쪽이 조커인지 모른다 - <b>내민 장을 믿을지 말지</b>만 정한다.
        /// </summary>
        /// <param name="offeredSlot">플레이어가 밀어 올린 자리.</param>
        public static int ChoosePick(CharacterPersonality personality, System.Random rng,
            int offeredSlot)
        {
            float empathy = Trait(personality, p => p.empathy);

            // 공감이 높을수록 "이걸 내미는 데는 이유가 있다"고 의심해서 반대쪽을 집는다.
            float suspicion = Mathf.Lerp(0.25f, 0.8f, empathy);

            bool avoidOffered = rng.NextDouble() < suspicion;
            return avoidOffered ? 1 - offeredSlot : offeredSlot;
        }

        private static float Trait(CharacterPersonality personality, Func<CharacterPersonality, int> pick)
            => personality != null ? CharacterPersonality.Normalized(pick(personality)) : 0.5f;
    }
}
