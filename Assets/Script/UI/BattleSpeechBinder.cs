using UnityEngine;
using JojoPuzzle.Core;
using JojoPuzzle.View;
using JojoPuzzle.Battle;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 배틀에서 일어나는 일을 <see cref="SpeechTrigger"/>로 번역해 <see cref="SpeechDirector"/>에 넘기는 곳.
    ///
    /// 게임 시스템(BattleManager/BoardInputController)은 대사를 전혀 모르고 자기 이벤트만 발행하며,
    /// 대사를 붙이거나 떼는 일은 전부 여기서 끝난다. 상황을 추가하고 싶으면 SpeechTrigger에 항목을
    /// 하나 늘리고 여기에 구독 한 줄을 더하면 되고, 게임 로직은 손대지 않는다.
    ///
    /// <b>전부 배선해둬도 안전하다</b> - 그 캐릭터의 대사집에 해당 줄이 없으면 SpeechDirector가
    /// 아무것도 하지 않는다(대사창이 안 뜨므로 판도 안 멈춘다). 그래서 "일단 걸어두고 대사를
    /// 쓰는 순간부터 나오게" 하는 방식이 된다.
    ///
    /// 슬롯 → 캐릭터 변환은 팔레트를 쓴다. 팔레트 색 인덱스가 곧 편성 순서라서
    /// (0=리더, 1=파트너 - BattleSetup.BuildPalette) boardView.GetCharacter(슬롯)이 그대로 답이다.
    /// </summary>
    public class BattleSpeechBinder : MonoBehaviour
    {
        [SerializeField] private SpeechDirector director;
        [SerializeField] private BattleManager battleManager;
        [SerializeField] private BoardInputController boardInput;

        [Tooltip("슬롯 번호로 편성 캐릭터를 찾는 데 쓴다(팔레트 조회).")]
        [SerializeField] private BoardView boardView;

        [Header("어떤 상황에 말하게 할지")]
        [Tooltip("스탠드업 타임이 시작될 때. 배너가 막 끝나고 플레이어가 조작을 시작하는 시점이라, " +
                 "대사를 길게 잡으면 10초를 그만큼 까먹는 느낌이 든다.")]
        [SerializeField] private bool speakOnStandUpStart = true;

        [Tooltip("스탠드업 종료 연출이 시작될 때(불꽃이 리더에게 모이기 직전). " +
                 "이 구간은 원래도 판이 멈춰 있어서 대사를 넣기 가장 자연스럽다.")]
        [SerializeField] private bool speakOnStandUpFinish = true;

        [Tooltip("스킬을 썼을 때. 스킬 연출(1.대사 → 2.애니메이션 → 3.효과)이 만들어지면 " +
                 "1단계를 그 시퀀스가 직접 가져가야 하므로, 그때 이 항목을 꺼야 대사가 두 번 나오지 않는다.")]
        [SerializeField] private bool speakOnSkill = true;

        [SerializeField] private bool speakOnBattleEnd = true;

        /// <summary>대사를 말할 기본 화자 = 리더(편성 0번).</summary>
        private PanelType Leader => boardView != null ? boardView.GetCharacter(0) : null;

        private void OnEnable()
        {
            if (battleManager != null)
            {
                battleManager.OnCharacterSkillUsed += HandleSkillUsed;
                battleManager.OnBattleEnded += HandleBattleEnded;
            }

            if (boardInput != null)
            {
                boardInput.OnStandUpTimeStart += HandleStandUpStart;
                boardInput.OnStandUpEndSequenceStart += HandleStandUpFinish;
            }
        }

        private void OnDisable()
        {
            if (battleManager != null)
            {
                battleManager.OnCharacterSkillUsed -= HandleSkillUsed;
                battleManager.OnBattleEnded -= HandleBattleEnded;
            }

            if (boardInput != null)
            {
                boardInput.OnStandUpTimeStart -= HandleStandUpStart;
                boardInput.OnStandUpEndSequenceStart -= HandleStandUpFinish;
            }
        }

        private void Start()
        {
            // 배틀 시작 대사. 팔레트가 만들어진 뒤여야 하므로 Start에서 한다
            // (GameEntryPoint가 보드/팔레트를 세운 다음 BeginBattle을 부른다).
            Speak(Leader, SpeechTrigger.BattleStart);
        }

        /// <param name="slot">편성 순서. 0=리더, 1=파트너.</param>
        private void HandleSkillUsed(int slot)
        {
            if (!speakOnSkill || director == null || boardView == null)
                return;

            // 스킬은 "대사가 끝나야 다음 단계"라서 Play(기다리는 쪽)를 쓴다. 지금은 뒤에 이어질
            // 단계가 없어서 기다리는 의미가 없지만, 나중에 스킬 시퀀스가 이 자리를 가져갈 때
            // 형태가 그대로 맞도록 처음부터 이렇게 둔다.
            var speaker = boardView.GetCharacter(slot);
            if (speaker != null)
                StartCoroutine(director.Play(speaker, SpeechTrigger.SkillActivate));
        }

        private void HandleStandUpStart()
        {
            if (speakOnStandUpStart)
                Speak(Leader, SpeechTrigger.StandUpStart);
        }

        private void HandleStandUpFinish()
        {
            if (speakOnStandUpFinish)
                Speak(Leader, SpeechTrigger.StandUpFinish);
        }

        private void HandleBattleEnded(BattleOutcome outcome)
        {
            if (!speakOnBattleEnd)
                return;

            // <b>졌을 때 아군은 아무 말도 하지 않는다</b>(2026-08-28 사용자 확정:
            // "아군용 패배 대사는 존재하지 않는다"). Defeat 는 "플레이어가 졌다"는 상황이고
            // 그 줄은 <b>이긴 적이 건네는 말</b>이라("소녀에게 패배하셨군요?"), 리더에게 시키면
            // 자기가 이긴 것처럼 들린다. 그 대사는 패배 화면에서 <b>적이</b> 말한다
            // (<see cref="BattleDefeatPanel"/>).
            if (outcome.result != BattleResult.Victory)
                return;

            Speak(Leader, SpeechTrigger.Victory);
        }

        /// <summary>
        /// 기다리지 않는 대사. 더 중요한 대사가 떠 있으면 그냥 버려진다 - 큐에 쌓아두면
        /// 상황이 다 지난 뒤에 뒤늦게 튀어나오고 그만큼 판이 더 오래 멈춘다.
        /// </summary>
        private void Speak(PanelType speaker, SpeechTrigger trigger)
        {
            if (director != null && speaker != null)
                director.TryReport(speaker, trigger);
        }
    }
}
