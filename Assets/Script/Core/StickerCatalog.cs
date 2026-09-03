using System;
using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 스티커가 <b>언제</b> 듣는지(2026-09-03 사용자 기획).
    /// </summary>
    public enum StickerKind
    {
        /// <summary>판이 시작하자마자 내내 듣는다.</summary>
        Passive = 0,

        /// <summary>조건을 만족해야 터진다.</summary>
        Triggered = 1
    }

    /// <summary>
    /// 스티커가 <b>무엇을</b> 하는지. 글로 적힌 설명을 코드가 읽을 수 있는 값으로 옮긴 것이다 -
    /// 설명은 사람이 읽고, 이 값은 전투가 읽는다.
    /// </summary>
    public enum StickerEffect
    {
        None = 0,

        /// <summary>그 색 조각이 다시 나올 확률 +값%.</summary>
        BlockRegen,

        /// <summary>중복색이 있을 때 <b>원본색</b> 조각이 다시 나올 확률 +값%.</summary>
        DuplicateRegen,

        /// <summary>그 색 조각의 데미지 +값%.</summary>
        BlockDamage,

        /// <summary>중복색 조각의 데미지 +값%.</summary>
        DuplicateDamage,

        /// <summary>판이 끝날 때 받는 코인 +값%.</summary>
        CoinGain,

        /// <summary>일정 시간마다 아군 스킬 게이지 회복(멈춘 시간은 안 센다).</summary>
        SkillGaugeOverTime,

        /// <summary>보스 최대 체력의 일부를 넘는 한 방에 코인을 준다.</summary>
        BigHitCoin,

        /// <summary>방해 블록이 생기면 <b>한 번만</b> 리더의 상자로 덮는다.</summary>
        CoverObstacle,

        /// <summary>시작하고 잠깐 리더 색 조각이 잘 나온다.</summary>
        LeaderRegenBurst,

        /// <summary>러시 타임에서 번 코인 +값%.</summary>
        RushCoin,

        /// <summary>이긴 판에서 가장 많이 지운 캐릭터 경험치 +값%.</summary>
        TopMatcherExp,

        /// <summary>이번 판에 쓴 스킬 수만큼 코인을 더 준다.</summary>
        SkillCountCoin,

        /// <summary>강화된 조각 하나를 <b>세 조각 맞춘 것</b>으로 친다(스킬이 빨리 찬다).</summary>
        EmpoweredCountsAsThree
    }

    /// <summary>스티커 한 장.</summary>
    [Serializable]
    public class StickerDefinition
    {
        [Tooltip("시트의 no. 저장·붙이기 판정에 쓰는 이름표다.")]
        public int id;

        [Tooltip("붙이는 데 드는 코스트. 5 · 10 · 15 · 20.")]
        [Min(0)]
        public int cost;

        public StickerKind kind;

        public StickerEffect effect;

        [Tooltip("색을 가리는 효과일 때 그 색(PanelFrameColor). 색과 상관없으면 -1.")]
        public int color = -1;

        [Tooltip("효과의 세기. 대개 퍼센트다.")]
        public float value;

        [Tooltip("효과가 <b>터지는 문턱</b>. '보스 최대 체력 10% 초과시' 의 10 이 여기 들어간다. " +
                 "0이면 문턱이 없는 효과다. value(주는 값)와 갈라 둔 이유는 seconds 와 같다 - " +
                 "한 숫자로 둘을 담고 있었다.")]
        [Min(0)]
        public float threshold;

        [Tooltip("시간이 얽힌 효과의 초. '10초마다 10% 회복'의 앞 10초, " +
                 "'10초 동안 +10%'의 앞 10초가 여기 들어간다. 0이면 시간과 상관없는 효과다. " +
                 "⭐ value 하나로 두 숫자를 다 담고 있었는데(지금은 둘 다 10이라 우연히 맞았다), " +
                 "기획이 '15초마다 10%' 로 바뀌는 순간 표현할 수 없어서 갈라 뒀다.")]
        [Min(0)]
        public float seconds;

        [Tooltip("사람이 읽는 설명. 시트에 적힌 그대로다.")]
        [TextArea(2, 3)]
        public string description;

        [Tooltip("스티커 그림. ⭐ 목록에서 보고 싶은 건 <b>스티커</b>지 글이 아니다 " +
                 "(2026-09-03 사용자 지시) - 설명은 꾹 눌러야 말풍선으로 뜬다.")]
        public Sprite sprite;
    }

    /// <summary>
    /// 붙일 수 있는 <b>스티커 목록</b>. 원본은 <c>Chardata.xlsx</c> 의 <c>sticker</c> 시트이고,
    /// 스크래치패드의 <c>import_stickers.py</c> 가 이 애셋을 다시 쓴다 -
    /// 값을 코드에 박으면 기획을 손볼 때마다 스크립트를 고치게 된다.
    /// </summary>
    public class StickerCatalog : ScriptableObject
    {
        [SerializeField] private StickerDefinition[] stickers = new StickerDefinition[0];

        public int Count => stickers != null ? stickers.Length : 0;

        public StickerDefinition At(int index)
            => stickers != null && index >= 0 && index < stickers.Length ? stickers[index] : null;

        /// <summary>시트 번호로 찾는다. 없으면 null.</summary>
        public StickerDefinition Find(int id)
        {
            if (stickers == null)
                return null;

            for (int i = 0; i < stickers.Length; i++)
            {
                if (stickers[i] != null && stickers[i].id == id)
                    return stickers[i];
            }

            return null;
        }
    }
}
