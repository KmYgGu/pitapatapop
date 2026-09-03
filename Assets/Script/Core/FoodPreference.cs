using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>그 캐릭터에게 이 음식이 어땠는지.</summary>
    public enum FoodOpinion
    {
        /// <summary>아직 안 먹어봤다 - 좋아하는지 싫어하는지 알 수 없다.</summary>
        Unknown,

        Disliked,
        Neutral,
        Liked
    }

    /// <summary>
    /// <b>이 캐릭터가 이 음식을 좋아할까</b>를 입맛에서 계산한다(2026-08-28 사용자 기획:
    /// "좋아하는 음식은 미리 정해져 있는 게 아니고, 캐릭터의 입맛에 따라 정해진다").
    ///
    /// <code>
    ///   점수 = 그 음식의 <b>갈래</b>와 <b>맛들</b>에 대한 입맛 점수의 평균
    /// </code>
    ///
    /// <b>순수 계산이다</b>(<see cref="GoldReward"/>·<see cref="StandUpDamageTable"/> 과 같은 방침) -
    /// UnityEngine 의 화면 값을 안 보고, 같은 입력에 늘 같은 답을 준다.
    ///
    /// <b>갈래와 맛을 같은 무게로 평균낸다.</b> 맛이 여럿인 음식(감자탕: 짠맛+매운맛)은 그만큼
    /// 맛 쪽 비중이 커지는데, 그게 자연스럽다 - 맛이 여러 개라는 건 그 맛들이 실제로 두드러진다는 뜻이다.
    /// </summary>
    public static class FoodPreference
    {
        /// <summary>이 점수 이상이면 좋아하는 음식.</summary>
        public const int LikeThreshold = 60;

        /// <summary>이 점수 이하면 싫어하는 음식.</summary>
        public const int DislikeThreshold = 40;

        /// <summary>그 음식에 대한 입맛 점수(0~100). 표에 없는 갈래는 그저 그런 값으로 친다.</summary>
        public static int Score(CharacterTasteTable table, PanelType character, FoodItem food)
        {
            if (food == null)
                return CharacterTasteTable.NeutralScore;

            int sum = 0;
            int count = 0;

            if (!string.IsNullOrEmpty(food.type))
            {
                sum += ScoreOf(table, character, food.type);
                count++;
            }

            if (food.tastes != null)
            {
                for (int i = 0; i < food.tastes.Length; i++)
                {
                    if (string.IsNullOrEmpty(food.tastes[i]))
                        continue;

                    sum += ScoreOf(table, character, food.tastes[i]);
                    count++;
                }
            }

            return count > 0 ? Mathf.RoundToInt(sum / (float)count) : CharacterTasteTable.NeutralScore;
        }

        private static int ScoreOf(CharacterTasteTable table, PanelType character, string key)
            => table != null ? table.GetScore(character, key) : CharacterTasteTable.NeutralScore;

        /// <summary>
        /// 점수를 <b>좋아함/싫어함/그저 그럼</b>으로 가른다.
        /// <b>먹어보지 않았으면 이 함수를 부르지 말 것</b> - 그때는 Unknown 이고, 그 판단은
        /// 무엇을 먹었는지 아는 쪽(<see cref="App.ResidentState"/>)이 한다.
        /// </summary>
        public static FoodOpinion ToOpinion(int score)
        {
            if (score >= LikeThreshold)
                return FoodOpinion.Liked;

            if (score <= DislikeThreshold)
                return FoodOpinion.Disliked;

            return FoodOpinion.Neutral;
        }
    }
}
