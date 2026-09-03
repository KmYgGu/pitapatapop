using System.Collections.Generic;
using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 현재 보유 중인 모든 캐릭터(PanelType) 애셋을 모아두는 단일 목록 애셋.
    /// 세이브 데이터 기반 수집 시스템이 만들어지기 전까지, "보유 캐릭터 풀"의 실제 소스로 사용한다.
    /// 편성한 리더/파트너를 여기에도 포함해도 무방 - BattleSetup.BuildPalette가 파티원은
    /// 후보에서 자동으로 제외한다.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterRoster", menuName = "JojoPuzzle/Character Roster")]
    public class CharacterRoster : ScriptableObject
    {
        public List<PanelType> ownedCharacters = new List<PanelType>();
    }
}
