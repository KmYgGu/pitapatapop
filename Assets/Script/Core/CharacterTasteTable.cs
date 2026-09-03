using System;
using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>입맛 항목 하나 - "고기 70" 처럼 갈래·맛 이름과 그 점수.</summary>
    [Serializable]
    public class TasteScore
    {
        [Tooltip("음식의 갈래나 맛 이름. <b>FoodCatalog 의 type·tastes 와 글자가 같아야</b> 이어진다.")]
        public string key = string.Empty;

        [Tooltip("0~100. 높을수록 좋아한다. 50이 그저 그런 정도다.")]
        [Range(0, 100)]
        public int score = 50;
    }

    /// <summary>캐릭터 한 명의 입맛.</summary>
    [Serializable]
    public class CharacterTaste
    {
        public PanelType character;

        [Tooltip("기획 시트 `taste preference` 의 한 줄. 고기·생선·…·신맛.")]
        public TasteScore[] scores = new TasteScore[0];
    }

    /// <summary>
    /// 캐릭터별 <b>입맛</b>. 기획 시트 `taste preference` 를 옮긴 것이다.
    ///
    /// <b>⚠ "좋아하는 음식"이 여기 적혀 있지 않다</b>(2026-08-28 사용자 정정). 좋아하는 음식은
    /// <b>먹어 봐야 정해진다</b> - 이 표는 그 판정에 쓰는 <b>입맛</b>일 뿐이고, 무엇을 먹었는지는
    /// <see cref="App.ResidentState"/> 가, 점수 계산은 <see cref="FoodPreference"/> 가 맡는다.
    ///
    /// <b>왜 표 하나인가</b>: 캐릭터마다 애셋을 나누면 캐릭터가 늘 때마다 연결을 하나씩 더 해야
    /// 한다(<see cref="ChapterCatalog"/> 와 같은 방침).
    ///
    /// <b>왜 이름(문자열)으로 잇는가</b>: 시트가 갈래·맛을 글자로 적고 있어서, 그대로 옮겨
    /// 적는 것이 대조하기 쉽다. 표에 없는 갈래가 나오면 <b>그저 그런 값(50)</b>으로 친다 -
    /// 시트에 새 갈래가 생겨도 화면이 깨지지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterTasteTable", menuName = "JojoPuzzle/Character Taste Table")]
    public class CharacterTasteTable : ScriptableObject
    {
        /// <summary>표에 없는 항목의 점수. "특별히 좋아하지도 싫어하지도 않는다".</summary>
        public const int NeutralScore = 50;

        public CharacterTaste[] tastes = new CharacterTaste[0];

        public CharacterTaste Find(PanelType character)
        {
            if (tastes == null || character == null)
                return null;

            for (int i = 0; i < tastes.Length; i++)
            {
                if (tastes[i] != null && tastes[i].character == character)
                    return tastes[i];
            }

            return null;
        }

        /// <summary>그 캐릭터가 이 갈래·맛을 얼마나 좋아하는지(0~100). 모르면 50.</summary>
        public int GetScore(PanelType character, string key)
        {
            if (string.IsNullOrEmpty(key))
                return NeutralScore;

            var taste = Find(character);
            if (taste == null || taste.scores == null)
                return NeutralScore;

            for (int i = 0; i < taste.scores.Length; i++)
            {
                if (taste.scores[i] != null && taste.scores[i].key == key)
                    return taste.scores[i].score;
            }

            return NeutralScore;
        }
    }
}
