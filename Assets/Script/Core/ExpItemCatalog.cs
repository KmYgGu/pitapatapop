using System;
using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>경험치 아이템의 종류. 개수가 늘면 여기 항목만 늘린다.</summary>
    public enum ExpItemKind
    {
        Small = 0,
        Medium = 1,
        Large = 2,
    }

    /// <summary>경험치 아이템 하나의 설정.</summary>
    [Serializable]
    public class ExpItem
    {
        public ExpItemKind kind = ExpItemKind.Small;

        public string displayName = "경험치 조각";

        [Tooltip("한 개를 쓰면 오르는 경험치.")]
        [Min(1)]
        public int exp = 100;

        public Sprite icon;
    }

    /// <summary>
    /// 강화 화면에서 레벨업에 쓰는 경험치 아이템 목록.
    ///
    /// <b>애셋 하나에 모아둔 이유</b>는 <see cref="BattleItemCatalog"/> 와 같다 - 종류가 적고
    /// 다른 데서 개별로 참조할 일이 없다.
    /// </summary>
    [CreateAssetMenu(fileName = "ExpItemCatalog", menuName = "JojoPuzzle/Exp Item Catalog")]
    public class ExpItemCatalog : ScriptableObject
    {
        public ExpItem[] items = new ExpItem[0];
    }
}
