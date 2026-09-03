using UnityEngine;

namespace JojoPuzzle.Core
{
    /// <summary>
    /// 캐릭터의 <b>성격과 욕구</b>. Chardata.xlsx 의 <c>social simulation</c> · <c>desire</c>
    /// 시트를 그대로 옮긴 것이다(2026-09-02).
    ///
    /// <b>왜 PanelType 이 아니라 별도 애셋인가</b>: CharacterSpeechSet · SkillDefinition 과 같은 이유다.
    /// PanelType 은 퍼즐·성장 데이터인데 거기에 이미 유저 상태가 섞여 있는 게 이 프로젝트의 가장 큰
    /// 부채로 적혀 있다. 성격은 <b>기획이 시트에서 굴리는 값</b>이라 따로 두고 참조만 건다.
    ///
    /// <b>0~100 이 전부 같은 눈금이다</b> - 시트가 그렇게 쓰여 있고, 읽는 쪽은 전부
    /// <see cref="Normalized"/> 로 0~1 로 바꿔 쓴다. 눈금을 바꾸려면 여기 한 곳만 고치면 된다.
    ///
    /// 지금 이 값을 읽는 건 <b>미니게임(인디언 포커)의 상대 AI</b> 하나뿐이다. 앞으로 방 대화·
    /// 부탁·선물 반응 같은 게 붙으면 같은 애셋을 보면 된다 - 그게 시트를 통째로 옮겨둔 이유다.
    /// </summary>
    [CreateAssetMenu(fileName = "Personality_", menuName = "JojoPuzzle/Character Personality")]
    public class CharacterPersonality : ScriptableObject
    {
        [Header("신상 (social simulation 시트)")]
        public string affiliation;
        public int age;
        public string job;

        [Header("성격 - 0~100")]
        [Tooltip("호의. 남에게 잘해주려는 정도.")]
        [Range(0, 100)] public int goodwill = 50;

        [Tooltip("사교성. 먼저 말을 거는 정도.")]
        [Range(0, 100)] public int sociability = 50;

        [Tooltip("정직함. <b>낮을수록 잘 속인다</b> - 포커의 허세(블러프) 빈도가 여기서 나온다.")]
        [Range(0, 100)] public int honesty = 50;

        [Tooltip("공감력.")]
        [Range(0, 100)] public int empathy = 50;

        [Tooltip("공격성. <b>포커에서 레이즈를 얼마나 세게 거는지</b>.")]
        [Range(0, 100)] public int aggression = 50;

        [Tooltip("이기심.")]
        [Range(0, 100)] public int egoistic = 50;

        [Tooltip("용기. <b>나쁜 패에서도 콜을 받는 배짱</b>.")]
        [Range(0, 100)] public int courage = 50;

        [Tooltip("규칙성. 행동이 얼마나 일정한지.")]
        [Range(0, 100)] public int regularity = 50;

        [Tooltip("장난기. 대사 고르기와 실없는 행동에 쓴다.")]
        [Range(0, 100)] public int playful = 50;

        [Header("욕구 - 0~100 (desire 시트)")]
        [Range(0, 100)] public int loneliness = 50;
        [Range(0, 100)] public int appetite = 50;
        [Range(0, 100)] public int fatigue = 50;
        [Range(0, 100)] public int fun = 50;
        [Range(0, 100)] public int safety = 50;
        [Range(0, 100)] public int recognition = 50;

        [Tooltip("탐욕. <b>판돈을 얼마나 크게 부르는지</b>.")]
        [Range(0, 100)] public int greed = 50;

        /// <summary>0~100 값을 0~1 로. 읽는 쪽은 전부 이걸 지난다.</summary>
        public static float Normalized(int value) => Mathf.Clamp01(value / 100f);
    }
}
