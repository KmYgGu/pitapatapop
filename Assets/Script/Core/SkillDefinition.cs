using System;
using System.Collections.Generic;
using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 스킬 효과 한 가지의 종류. 캐릭터가 늘어나도 여기 항목만 늘리고 조합해서 쓴다.
    /// </summary>
    public enum SkillEffectKind
    {
        /// <summary>지정한 칸들을 대상 색으로 바꾼다(리더의 구역 변환).</summary>
        ConvertRegion = 0,

        /// <summary>판에 있는 대상 색 조각을 전부 강화한다(파트너의 강화).</summary>
        EmpowerColor = 1,

        /// <summary>
        /// <b>무작위 지점에 자기 패널을 뿌리고, 근처의 자기 조각을 강화하며 이어진다</b>
        /// (라미아의 브릴란스, 2026-08-30 사용자 기획).
        ///
        /// 한 사이클: 서로 다른 무작위 칸 <see cref="SkillEffect.scatterCount"/> 개에 자기 패널을
        /// 만들고 → 그 칸들의 <b>상하좌우</b>에 자기 조각이 있으면 전부 강화하고 → <b>한 사이클 더</b>.
        /// 이웃이 하나도 없으면 거기서 끝난다.
        ///
        /// <b>판이 다 자기 색이 되면 멈춘다</b> - 그 상태에서는 이웃이 늘 있어서 연쇄 조건만으로는
        /// 끝나지 않는다. 그래서 "바꿀 칸이 남아 있는가"를 함께 본다.
        /// </summary>
        ScatterConvert = 2,

        /// <summary>
        /// <b>무작위 정사각 구역을 특수 패널로 박는다</b>(미스틱의 포지셔닝, 2026-08-30 사용자 기획).
        ///
        /// 그 칸들은 <see cref="CellKind.Special"/> 이 되어 중력·변환·방해블록·상자를 전부 버티고,
        /// <b>자기들끼리만으로는 매치가 성립하지 않는다</b>. 매치에 낄 때마다 횟수가 하나 줄고
        /// 0이 되는 매치에서 같이 사라진다.
        /// </summary>
        SpecialAnchor = 3,

        /// <summary>
        /// <b>무작위 열과 행을 쓸어버리고 그 자리를 자기 패널로 채운다</b>
        /// (루바니아의 검은 파동!, 2026-08-30 사용자 기획).
        ///
        /// 쓸어낸 칸의 <b>전투력 합만큼 적에게 데미지</b>가 들어가고, 그 자리는 시전자 색이 된다.
        /// 방해블록과 스탠드업 고정 칸까지 걷어내지만 <b>상자와 미스틱의 특수 퍼즐은 버틴다</b>.
        /// </summary>
        CrossWipe = 4,

        /// <summary>
        /// <b>맨 아랫줄에 점화 블록을 놓고 끝난다</b>(유나의 버닝 트랙!, 2026-09-01 사용자 기획).
        ///
        /// 다른 효과와 달리 <b>쓸 때 판을 거의 건드리지 않는다</b> - 무엇을 지울지는
        /// 플레이어가 정한다. 놓인 블록(<see cref="CellKind.BurnTrack"/>)은 큰브처럼 드래그로
        /// 옮길 수 있고, 거기에 일반 조각을 밀어 넣으면 그 순간 <b>그 열을 그 행부터 위로</b>
        /// 통째로 태우며 "먹인 조각의 전투력 × 지워진 칸 수"만큼 적을 때린다.
        /// 그 발동은 스킬 연출이 아니라 조작이라 BoardInputController 가 맡는다.
        /// </summary>
        BurnTrack = 5,
    }

    /// <summary>
    /// 스킬을 한 마디로 분류한 것. <b>화면에 보여주기 위한 딱지</b>이고, 실제로 무슨 일이
    /// 일어나는지는 <see cref="SkillDefinition.effects"/> 가 정한다.
    ///
    /// <b>왜 effects 에서 자동으로 뽑지 않는가</b>: 효과를 여럿 조합한 스킬은 어느 쪽으로도
    /// 부를 수 있고("구역을 바꾸고 그 자리를 강화"), 무엇으로 부를지는 기획이 정할 일이다.
    /// 게다가 <see cref="Remove"/> 는 아직 대응하는 효과 종류가 없다.
    /// </summary>
    public enum SkillCategory
    {
        /// <summary>변화형 - 퍼즐 조각을 다른 색으로 바꾼다.</summary>
        Convert = 0,

        /// <summary>강화형 - 조각의 위력을 올린다.</summary>
        Empower = 1,

        /// <summary>제거형 - 조각을 지운다. <b>아직 구현된 효과가 없다</b>(딱지만 있는 상태).</summary>
        Remove = 2,
    }

    /// <summary>
    /// 스킬 효과 하나. <b>효과를 조합해서 스킬을 만든다</b>는 게 이 구조의 요점이다 -
    /// "구역을 바꾸고 그 자리를 강화한다" 같은 캐릭터가 나와도 항목을 두 개 넣으면 되고,
    /// 연출 코드(SkillPresentation)는 손대지 않는다.
    ///
    /// 종류마다 쓰는 필드가 다르다(인스펙터에는 전부 보인다) - 아래 주석 참고.
    /// </summary>
    [Serializable]
    public class SkillEffect
    {
        public SkillEffectKind kind;

        [Tooltip("효과가 향할 색의 편성 슬롯. 0=리더, 1=파트너. " +
                 "-1이면 '시전자 자신의 색'이라 편성이 바뀌어도 따라온다.\n" +
                 "ConvertRegion=이 색으로 바꾼다 / EmpowerColor=이 색 조각을 강화한다.")]
        public int targetSlot = -1;

        [Tooltip("[ConvertRegion 전용] 바꿀 칸의 보드 좌표. (0,0)이 왼쪽 아래다.")]
        public Vector2Int[] cells;

        [Tooltip("[EmpowerColor / ScatterConvert 전용] 강화된 조각의 데미지 배율. 1.5면 그 조각 한 칸이 " +
                 "1.5칸어치로 세어진다(일반 매치·스탠드업 정사각형 양쪽 모두). " +
                 "1 이하면 강화가 아니므로 아무 일도 일어나지 않는다.")]
        [Min(1f)]
        public float empowerMultiplier = 1.5f;

        [Tooltip("[ScatterConvert 전용] 한 사이클에 만들 지점 수. 시트의 '무작위 두 지점'이라 2다. " +
                 "돌파로 늘어나는 값이 이것이다(기획 시트: '돌파시, 무작위 생성 개수가 증가합니다').")]
        [Min(1)]
        public int scatterCount = 2;

        [Tooltip("[ScatterConvert 전용] 상자까지 덮어쓸지. 브릴란스는 켠다(2026-08-30 사용자 확정) - " +
                 "다른 변환 스킬은 상자를 건드리지 않는 게 기본이다.")]
        public bool overwritesBoxes;

        [Tooltip("[ScatterConvert 전용] 사이클 상한. <b>0이면 상한 없음</b>이고, 그때는 " +
                 "'판을 더 바꿀 수 없을 때까지' 이어진다(2026-08-30 사용자 지시). " +
                 "너무 길다 싶으면 여기에 숫자를 넣어 끊는다.")]
        [Min(0)]
        public int maxCycles;

        [Tooltip("[SpecialAnchor 전용] 한 변의 칸 수. 시트가 'random 2x2' 라 2다.")]
        [Min(1)]
        public int specialSize = 2;

        [Tooltip("[SpecialAnchor 전용] 특수 패널이 버틸 매치 횟수. 시트가 '3번 매칭이 될 때까지' 라 3이다. " +
                 "돌파로 늘어나는 값이 이것이다(기획 시트: '돌파시, 특수 패널 매칭 가능 횟수가 증가합니다').")]
        [Min(1)]
        public int specialMatches = 3;

        [Tooltip("[CrossWipe 전용] 쓸어버릴 세로줄 수. 시트가 '무작위 1열' 이라 1이다. " +
                 "돌파로 늘어나는 값이 이것이다(기획 시트: '돌파시, 열 제거를 한번더 시도합니다').")]
        [Min(0)]
        public int wipeColumns = 1;

        [Tooltip("[CrossWipe 전용] 쓸어버릴 가로줄 수. 시트가 '무작위 1행' 이라 1이다.")]
        [Min(0)]
        public int wipeRows = 1;

        [Tooltip("[특수 블록 소환 전용] 놓을 자리를 고르는 성향. " +
                 "Careful 은 덜 아까운 칸부터(일반=방해블록 < 큐브 < 특수 블록), " +
                 "Reckless 는 놓을 수 있는 자리면 무엇이든 똑같이 보고 무작위로 고른다. " +
                 "미스틱은 Reckless 다 - 아군 자원까지 날릴 수 있는 게 그 캐릭터의 값어치라, " +
                 "단점을 피해 가면 캐릭터가 물러진다(2026-09-03 사용자 기획).")]
        public PlacementStyle placementStyle = PlacementStyle.Careful;

        [Tooltip("[BurnTrack 전용] 맨 아랫줄에 놓을 점화 블록 수. 시트가 '특수 블록을 하나' 라 1이다. " +
                 "돌파로 늘어나는 값이 이것이다(기획 시트: '돌파시, 특수 블록이 늘어납니다').")]
        [Min(1)]
        public int burnBlocks = 1;

        /// <summary>
        /// 이 효과가 실제로 향할 팔레트 색 인덱스. targetSlot 이 음수면 시전자 자신이다.
        /// 슬롯 번호가 곧 팔레트 색 인덱스라는 계약(BattleSetup.BuildPalette)에 기대고 있다.
        /// </summary>
        public int ResolveTargetSlot(int casterSlot) => targetSlot < 0 ? casterSlot : targetSlot;
    }

    /// <summary>
    /// 캐릭터 스킬 하나의 데이터. PanelType 에서 참조한다(PanelType.skill).
    ///
    /// <b>PanelType 과 애셋을 나눈 이유</b>는 CharacterSpeechSet 과 같다 - 스킬 수치는 밸런싱하며
    /// 자주 고치는 데이터인데, 그때마다 성장·퍼즐 설정이 들어 있는 PanelType 을 여는 건 위험하다.
    /// 게이지가 차는 속도(skillRequiredMatchCount)만은 PanelType 에 남아 있는데, 그건 퍼즐 진행
    /// 쪽 수치라 스킬 내용이 바뀌어도 같이 움직이지 않기 때문이다.
    ///
    /// 연출 타이밍(암전 길이, 구름 지연 등)은 여기 없다 - 그건 캐릭터별 데이터가 아니라
    /// 화면 연출 설정이라 SkillPresentation 인스펙터에 그대로 둔다.
    /// </summary>
    [CreateAssetMenu(fileName = "Skill_", menuName = "JojoPuzzle/Skill Definition")]
    public class SkillDefinition : ScriptableObject
    {
        [Header("표시용")]
        public string skillName;

        [TextArea]
        public string description;

        [Tooltip("편성 화면에 보여줄 분류. 실제 동작은 아래 effects 가 정하고, 이건 딱지다.")]
        public SkillCategory category = SkillCategory.Convert;

        [Tooltip("편성 화면에 보여줄 범위 그림(Assets/image/skillpanel). " +
                 "비워두면 '범위가 정해지지 않은 스킬'로 보고 빈 판에 '무작위'라고 적는다.\n" +
                 "⚠ 아래 effects 의 cells 를 고치면 이 그림도 같이 갈아야 한다 - 그림은 따라오지 않는다.")]
        public Sprite rangeImage;

        [Header("효과")]
        [Tooltip("위에서부터 차례로 적용된다. 구름 연출은 이 효과들이 건드릴 칸을 전부 모아서 한 번에 피운다.")]
        public SkillEffect[] effects;

        /// <summary>분류의 한글 이름. 화면에 그대로 쓴다.</summary>
        public string CategoryLabel
        {
            get
            {
                switch (category)
                {
                    case SkillCategory.Empower: return "강화형";
                    case SkillCategory.Remove: return "제거형";
                    default: return "변화형";
                }
            }
        }

        /// <summary>
        /// 이 스킬이 퍼즐판에서 건드리는 칸 전부. 편성 화면의 범위 미리보기가 쓴다.
        /// <b>효과가 여럿이면 다 합친다</b> - 구름 연출이 칸을 모으는 것과 같은 기준이다.
        /// 칸을 지정하지 않는 효과(강화형처럼 판 전체가 대상)는 여기 아무것도 더하지 않는다.
        /// </summary>
        public void CollectCells(List<Vector2Int> buffer)
        {
            if (buffer == null || effects == null)
                return;

            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                if (effect == null || effect.cells == null)
                    continue;

                for (int c = 0; c < effect.cells.Length; c++)
                {
                    if (!buffer.Contains(effect.cells[c]))
                        buffer.Add(effect.cells[c]);
                }
            }
        }
    }
}
