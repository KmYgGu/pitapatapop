using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 패널(캐릭터 색) 하나를 정의하는 데이터.
    /// 캐릭터마다 하나씩 ScriptableObject 애셋으로 만들어서 인스펙터에서 관리.
    /// </summary>
    [CreateAssetMenu(fileName = "PanelType_", menuName = "JojoPuzzle/Panel Type")]
    public class PanelType : ScriptableObject
    {
        [Header("식별자")]
        public string panelId;          // 예: "jotaro", "jolyne" 등 캐릭터 고유 ID

        [Header("표시용")]
        public string displayName;
        public Sprite icon;

        /// <summary>
        /// <b>아이콘 안에서 그림이 실제로 든 자리</b>(0~1 비율, 유니티처럼 y 가 위로 간다).
        /// 기본값은 그림 전체다.
        ///
        /// ⭐ <b>왜 필요한가</b>(2026-09-03 사용자 신고: 편성 아이콘의 얼굴 크기가 제각각):
        /// 아이콘 png 들은 다 정사각형인데 <b>그림이 캔버스를 채우는 정도가 69%~100% 로 다르다</b>.
        /// 캔버스 전체를 맞추면 여백이 많은 그림은 그만큼 작게 그려진다 - 머리 모양 때문이 아니라
        /// <b>여백 때문에</b> 얼굴 크기가 달라 보인다.
        ///
        /// ⚠ <b>손으로 정하는 값이 아니다.</b> scratchpad/bake_icon_trim.py 가 알파를 읽어
        /// 계산해 적는다. 캐릭터가 늘면 그 스크립트를 다시 돌리면 되고, 안 돌려도 기본값이
        /// 그림 전체라 예전과 똑같이 그려진다.
        /// </summary>
        public Rect iconTrim = new Rect(0f, 0f, 1f, 1f);
        public Color themeColor = Color.white;

        /// <summary>
        /// <b>화면에 적을 이름.</b> <see cref="displayName"/> 이 비어 있으면 애셋 이름으로 물러선다.
        ///
        /// <b>왜 필요한가</b>(2026-08-28): 지금 캐릭터 애셋들의 <c>displayName</c> 이 <b>전부
        /// 비어 있다</b>. 그대로 그리면 이름 칸이 빈 글자가 되어 화면이 고장 난 것처럼 보이는데,
        /// 원인이 데이터라는 걸 알기까지 코드를 한참 뒤지게 된다(실제로 그랬다).
        /// <b>이름을 그리는 곳은 전부 여기를 지날 것</b> - 각자 물러서기를 짜면 화면마다 답이 달라진다.
        ///
        /// 기획 시트에는 한글 이름(<c>kname</c>)이 따로 있다 - 그게 애셋에 들어오면
        /// <c>displayName</c> 이 채워지고 이 물러서기는 저절로 안 쓰이게 된다.
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        [Header("퍼즐 프레임")]
        // 이 캐릭터가 보드 위에서 기본으로 그려질 프레임 색(0~7 중 하나). 편성한 리더/파트너가
        // 같은 기본색이면 파트너 쪽만 런타임에 +8(스왑색)로 바뀌어 그려짐 - BattleSetup.BuildPalette 참고.
        public PanelFrameColor frameColor;

        [Header("스탠드업 타임")]
        [Tooltip("스탠드업 타임 중 이 퍼즐 조각이 매칭돼 고정(StandHeld)될 때 바뀔 아이콘. " +
                 "비워두면 기존 icon을 그대로 사용.")]
        public Sprite standUpIcon;

        [Header("대사")]
        [Tooltip("이 캐릭터의 상황별 대사 모음. 비워두면 이 캐릭터는 아무 말도 하지 않는다 " +
                 "(대사창이 안 뜨므로 판이 멈추지도 않는다). 애셋을 따로 둔 이유는 " +
                 "대사가 가장 자주 고치고 늘어나는 데이터라서 - CharacterSpeechSet 주석 참고.")]
        public CharacterSpeechSet speech;

        [Header("스킬")]
        [Tooltip("이 캐릭터가 게이지를 채우고 발동하는 스킬. 비워두면 연출만 돌고 보드는 " +
                 "바뀌지 않는다(SkillPresentation 인스펙터의 임시 값으로 대체된다). " +
                 "대사와 같은 이유로 애셋을 따로 뒀다 - SkillDefinition 주석 참고.")]
        public SkillDefinition skill;

        [Tooltip("성격·욕구(Chardata.xlsx 의 social simulation · desire 시트). " +
                 "비워두면 전부 50 인 무난한 성격으로 친다. " +
                 "지금은 미니게임(인디언 포커)의 상대 AI 가 이걸 읽는다.")]
        public CharacterPersonality personality;

        [Tooltip("이 캐릭터의 스킬 게이지가 가득 차기까지 필요한 매치 조각 수. " +
                 "색은 상관없다 - 어떤 색을 맞추든 편성한 두 캐릭터의 게이지가 함께 오르고, " +
                 "이 값이 클수록 그 캐릭터만 늦게 찬다(스킬이 셀수록 크게 잡는 식으로 밸런싱). " +
                 "스탠드업 타임 중 새로 고정되는 조각도 이 수에 포함된다.")]
        [Min(1)]
        public int skillRequiredMatchCount = 70;

        // ── 아래는 원래 유저 세이브 데이터에서 불러올 값들. 수집/저장 시스템이 생기기 전까지는
        //    인스펙터에서 임의로 지정해두고 쓴다(CharacterRoster가 더미 풀 역할을 하는 것과 같은 맥락).
        [Header("성장 (임시 - 원래는 유저 세이브 데이터에서 로드)")]
        public CharacterGrade grade = CharacterGrade.BR;

        [Range(CharacterGrowthTable.MinLevel, CharacterGrowthTable.MaxLevel)]
        public int level = 1;

        [Tooltip("현재 레벨에서 다음 레벨까지 쌓인 경험치(누적 총량이 아니라 이번 레벨 구간 안에서의 값). " +
                 "다음 레벨까지 필요한 양은 ExpToNextLevel로 조회.")]
        [Min(0)]
        public int currentExp;

        /// <summary>
        /// 등급+레벨로 결정되는 전투력. 값을 따로 저장하지 않고 항상 표에서 조회하는 이유는,
        /// 레벨만 바꾸고 전투력 갱신을 깜빡해서 둘이 어긋나는 상황을 아예 만들지 않기 위함.
        /// 표 자체는 기획 엑셀에서 옮겨온 CharacterGrowthTable에 있다.
        /// </summary>
        public int CombatPower => CharacterGrowthTable.GetCombatPower(grade, level);

        /// <summary>다음 레벨까지 필요한 경험치. 만렙이면 0.</summary>
        public int ExpToNextLevel => CharacterGrowthTable.GetRequiredExp(level + 1);

        /// <summary>만렙 여부 - 경험치 UI에서 "MAX" 표시를 판단할 때 사용.</summary>
        public bool IsMaxLevel => level >= CharacterGrowthTable.MaxLevel;

        /// <summary>
        /// 다음 레벨까지의 진행도(0~1). 경험치 바를 채우는 용도.
        /// 만렙이면 더 이상 오를 곳이 없으므로 항상 1.
        /// </summary>
        public float ExpProgress01
        {
            get
            {
                int needed = ExpToNextLevel;
                if (needed <= 0)
                    return 1f;

                return Mathf.Clamp01((float)currentExp / needed);
            }
        }

        public override string ToString() => panelId;
    }
}
