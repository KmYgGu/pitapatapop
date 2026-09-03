using System;
using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>배틀 전에 사서 쓰는 보조 아이템의 종류.</summary>
    public enum BattleItemKind
    {
        /// <summary>데미지 증가.</summary>
        DamageUp,

        /// <summary>획득 코인량 증가.</summary>
        CoinUp,

        /// <summary>제한시간 증가.</summary>
        TimeUp,

        /// <summary>스킬 게이지를 시작부터 100%로.</summary>
        SkillFull
    }

    /// <summary>
    /// 아이템 하나의 설정. <b>효과 수치를 <see cref="value"/> 하나로 두는 이유</b>: 종류마다 단위가
    /// 다르지만(배율/초/비율) 종류를 보면 무엇인지 알 수 있어서, 필드를 종류별로 나누면 대부분이
    /// 빈 칸으로 남는다. 해석은 쓰는 쪽이 한다.
    /// </summary>
    [Serializable]
    public class BattleItem
    {
        public BattleItemKind kind = BattleItemKind.DamageUp;

        public string displayName = "데미지 증가";

        [TextArea(1, 2)]
        public string description = string.Empty;

        [Tooltip("골드 가격.")]
        [Min(0)]
        public int price = 1000;

        [Tooltip("효과 수치. DamageUp/CoinUp 은 배율(1.2 = 20% 증가), TimeUp 은 초, " +
                 "SkillFull 은 쓰지 않는다.")]
        public float value = 1.2f;

        public Sprite icon;
    }

    /// <summary>
    /// 준비 화면에 나열되는 아이템 목록. 네 가지가 기획으로 정해져 있어서
    /// (데미지 증가 / 획득 코인량 증가 / 시간 증가 / 스킬 100% 시작) 애셋 하나에 모아둔다.
    ///
    /// <b>아이템마다 애셋을 나누지 않은 이유</b>: 개수가 고정이고 다른 데서 개별로 참조할 일이
    /// 없다. 캐릭터별로 늘어나는 대사·스킬과는 사정이 다르다.
    /// </summary>
    [CreateAssetMenu(fileName = "BattleItemCatalog", menuName = "JojoPuzzle/Battle Item Catalog")]
    public class BattleItemCatalog : ScriptableObject
    {
        public BattleItem[] items = new BattleItem[0];
    }
}
