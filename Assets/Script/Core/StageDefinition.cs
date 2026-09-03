using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 스테이지 하나. <b>배틀의 규칙 수치가 여기서 온다.</b>
    ///
    /// 지금까지 적 체력·제한시간·클리어 조건은 <c>BattleManager</c> 인스펙터에 직접 박혀 있었고,
    /// 그건 "스테이지가 생기면 데이터로 빠져야 한다"고 적어둔 부채였다. 이 애셋이 그 자리다.
    /// <c>BattleManager.BeginBattle</c> 이 <see cref="App.StageEntry"/> 에 고른 스테이지가 있으면
    /// 인스펙터 값 대신 이걸 쓴다 - 없으면(배틀 씬을 직접 열어 테스트할 때) 예전처럼 동작한다.
    /// </summary>
    [CreateAssetMenu(fileName = "Stage", menuName = "JojoPuzzle/Stage")]
    public class StageDefinition : ScriptableObject
    {
        [Header("표시")]
        [Tooltip("목록과 준비 화면에 쓰는 이름. 예: \"1-3 뒷골목\"")]
        public string displayName = "1-1";

        [Tooltip("준비 화면 중상단에 걸리는 배너 그림. 비어 있으면 이름만 나온다.")]
        public Sprite banner;

        [Header("난이도")]
        [Tooltip("권장 레벨. 준비 화면에서 적의 레벨로도 이 값을 보여준다.")]
        public int recommendedLevel = 1;

        [Tooltip("적 캐릭터. 준비 화면의 적 Spine 과 초상화가 여기서 온다.")]
        public PanelType enemy;

        [Tooltip("<b>보스전인지</b>(2026-08-28). 켜면 시작 연출에서 적의 '보스 등장' 대사가 나오고 " +
                 "적 초상화에 불꽃이 붙는다. 예전엔 배틀 씬 인스펙터(BattleFlameController)에 " +
                 "손으로 적어두던 값인데, 스테이지마다 다른 것이라 여기가 제자리다.")]
        public bool isBoss;

        [Header("배틀 규칙")]
        public float enemyMaxHealth = 18000f;

        [Tooltip("제한시간(초). 기획상 기본 60초 고정이고 특별 스테이지만 예외다.")]
        public float battleDuration = 60f;

        public string clearConditionText = "보스를 쓰러뜨려라";

        [Header("적의 방해")]
        [Tooltip("이 스테이지에서 적이 <b>언제 무엇으로</b> 방해할지(2026-08-25 사용자 기획). " +
                 "<b>비워두면 배틀 씬 인스펙터의 기본 설정을 쓴다</b> - 스테이지를 안 거치고 " +
                 "배틀 씬을 직접 열어 테스트할 때가 그렇다.")]
        public System.Collections.Generic.List<HarassTrigger> harassTriggers =
            new System.Collections.Generic.List<HarassTrigger>();

        [Tooltip("방해와 방해 사이 최소 간격(초). 0 이하면 배틀 씬 기본값을 쓴다.")]
        [Min(0)]
        public float harassMinInterval = 0f;

        [Header("보상")]
        [Tooltip("클리어하면 캐릭터가 받는 경험치의 <b>기준값</b>. 자리마다 배율이 붙는다 - " +
                 "리더 1.25배 / 파트너 1배 / 나머지 0.75배(StageExpReward). 0이면 경험치가 없다.")]
        [Min(0)]
        public int clearExp = 120;

        [Header("입장")]
        [Tooltip("입장에 드는 하트. 0이면 공짜로 들어간다.")]
        [Min(0)]
        public int heartCost = 1;
    }
}
