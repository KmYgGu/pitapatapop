using System;
using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 음식 하나. <b>기획 시트(Chardata.xlsx 의 `food` 시트) 열을 그대로 옮긴 것</b>이다 -
    /// 이름을 바꾸면 시트에서 옮겨 적을 때 대조가 안 된다.
    /// </summary>
    [Serializable]
    public class FoodItem
    {
        [Tooltip("시트의 fno. 나중에 시트와 다시 맞출 때 열쇠가 된다.")]
        public int foodId;

        public string displayName = "음식";

        [TextArea(2, 3)]
        public string explanation = string.Empty;

        [Tooltip("먹였을 때 오르는 포만도(시트의 fullness).")]
        [Min(0)]
        public int fullness = 20;

        [Tooltip("따뜻한 정도(시트의 Temperature). 아직 쓰는 곳이 없다 - 자리만 잡아둔다.")]
        [Min(0)]
        public int temperature = 50;

        [Tooltip("음식 갈래(고기·생선·채소·쌀·빵·간식·계란·식사·반찬 …). " +
                 "<b>캐릭터 입맛 표의 항목 이름과 같아야</b> 점수가 매겨진다.")]
        public string type = string.Empty;

        [Tooltip("맛(기름진·짠맛·매운맛·고소한맛·신맛 …). 최대 셋. 빈 칸은 무시된다.")]
        public string[] tastes = new string[0];

        [Min(0)]
        public int price = 10;

        public Sprite icon;
    }

    /// <summary>
    /// 아파트에서 쓰는 <b>음식 목록</b>. 기획 시트의 `food` 시트를 옮긴 것이다.
    ///
    /// <b>애셋 하나에 모아둔 이유</b>는 <see cref="BattleItemCatalog"/> 와 같다 - 개수가 적고
    /// 다른 데서 개별로 참조할 일이 없다.
    ///
    /// <b>캐릭터가 무엇을 좋아하는지는 여기 없다</b> - 그건 먹어봐야 아는 것이고
    /// (<see cref="FoodPreference"/>), 입맛 자체는 <see cref="CharacterTasteTable"/> 이 갖는다.
    /// </summary>
    [CreateAssetMenu(fileName = "FoodCatalog", menuName = "JojoPuzzle/Food Catalog")]
    public class FoodCatalog : ScriptableObject
    {
        public FoodItem[] items = new FoodItem[0];

        public int Count => items != null ? items.Length : 0;

        public FoodItem Get(int index)
            => items != null && index >= 0 && index < items.Length ? items[index] : null;

        /// <summary>이름으로 찾는다. 없으면 null.</summary>
        public FoodItem Find(string displayName)
        {
            if (items == null || string.IsNullOrEmpty(displayName))
                return null;

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null && items[i].displayName == displayName)
                    return items[i];
            }

            return null;
        }
    }
}
