using System.Collections.Generic;
using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 배틀 화면 HUD 총괄. 적 캐릭터/체력, 내 캐릭터들/스킬게이지, 제한시간 타이머를 한 곳에서 관리.
    /// 표시 전용이다 - 값을 스스로 정하지 않고 BattleManager가 넘겨주는 걸 그리기만 한다.
    /// 적 체력의 진짜 값도, 제한시간도 BattleManager가 갖고 있다.
    /// (스킬 게이지는 아직 BattleManager와 연결되지 않아 예외 - playerSkillGauges가 비어 있고
    ///  OnCharacterSkillActivated를 듣는 쪽도 없다. 스킬 시스템 구현 시 이어붙일 자리.)
    /// </summary>
    public class BattleHUDController : MonoBehaviour
    {
        [Header("적")]
        [SerializeField] private HealthBarUI enemyHealthBar;

        [Header("내 캐릭터 (편성 순서와 동일하게)")]
        [SerializeField] private List<SkillGaugeUI> playerSkillGauges;

        [Header("타이머")]
        [SerializeField] private RadialTimerUI timer;
        // 제한시간(초)은 여기 두지 않는다 - 배틀 규칙이지 HUD 표시 설정이 아니라서
        // BattleManager가 갖고 있다가 StartBattle로 넘겨준다.

        /// <summary>타임오버 시 발행 (RadialTimerUI.OnTimeUp을 그대로 전달).</summary>
        public event System.Action OnBattleTimeUp;

        /// <summary>캐릭터 스킬이 발동됐을 때 발행 (몇 번째 캐릭터인지 인덱스와 함께).</summary>
        public event System.Action<int> OnCharacterSkillActivated;

        private void Awake()
        {
            for (int i = 0; i < playerSkillGauges.Count; i++)
            {
                int capturedIndex = i; // 클로저 캡처용 - 반복 변수를 직접 캡처하면 마지막 값으로 고정되는 문제 방지
                playerSkillGauges[i].OnSkillActivated += () => HandleSkillActivated(capturedIndex);
            }

            if (timer != null)
                timer.OnTimeUp += () => OnBattleTimeUp?.Invoke();
        }

        /// <summary>남은 시간 비율 (1=가득 남음, 0=타임오버). 러시타임 보너스 판정 등에 쓴다.</summary>
        public float RemainingTimeFraction => timer != null ? timer.RemainingFraction : 0f;

        /// <summary>배틀 시작 시 호출 - 적 체력과 타이머를 초기화하고 시작.</summary>
        public void StartBattle(float enemyMaxHealth, float durationSeconds)
        {
            PrepareBattle(enemyMaxHealth);
            timer?.StartTimer(durationSeconds);
        }

        /// <summary>
        /// 적 체력만 맞춰두고 <b>시계는 굴리지 않는다</b>. 시작 연출이 있는 판이 쓴다
        /// (<see cref="Battle.BattleIntroSequence"/>) - 연출을 보는 동안 시간이 깎이면 안 된다.
        ///
        /// <b>StartBattle 에 0초를 넘기는 걸로 대신하지 말 것</b> - 그러면 시계가 그 자리에서
        /// 만료돼 타임오버가 나간다.
        /// </summary>
        public void PrepareBattle(float enemyMaxHealth)
        {
            if (enemyHealthBar != null)
                enemyHealthBar.SetMax(enemyMaxHealth);
        }

        /// <summary>
        /// 적 체력을 절대값으로 지정. 체력의 실제 주인은 BattleManager이고 이쪽은 표시 전용이라,
        /// 뺄셈(DamageEnemy)보다 이 절대값 반영이 기본 경로다 - 값이 두 군데서 따로 줄어들다
        /// 어긋나는 일이 없다.
        /// </summary>
        public void SetEnemyHealth(float current) => enemyHealthBar?.SetValue(current);

        /// <summary>표시용 체력을 amount만큼 깎는다. BattleManager를 거치지 않는 연출/테스트용.</summary>
        public void DamageEnemy(float amount) => enemyHealthBar?.ApplyDamage(amount);

        /// <summary>배틀 종료 시 타이머를 멈춘다(승리했는데 시계가 계속 도는 걸 방지).</summary>
        public void StopTimer() => timer?.Pause();

        /// <summary>
        /// 제한시간과 무관하게 시계를 <b>다시</b> 굴린다. 러시 타임이 자기 길이를 시계에 실을 때 쓴다
        /// (<see cref="RushTimeController"/>) - 그때는 배틀이 이미 끝나 있어서 StartBattle 로
        /// 되돌릴 수는 없다(적 체력까지 되살아난다).
        /// </summary>
        public void StartTimer(float durationSeconds) => timer?.StartTimer(durationSeconds);

        /// <summary>
        /// 제한시간을 잠시 멈추거나 다시 굴린다. 연출(가림막/화면 암전)이 떠 있는 동안 시간이
        /// 흐르지 않게 하는 용도라 StopTimer와 달리 되돌릴 수 있다.
        /// 이미 시간이 다 찬 타이머는 Resume해도 다시 돌지 않는다(RadialTimerUI가 막는다).
        /// </summary>
        public void SetTimerPaused(bool paused)
        {
            if (timer == null)
                return;

            if (paused)
                timer.Pause();
            else
                timer.Resume();
        }

        /// <summary>characterIndex번째 캐릭터의 스킬 게이지를 amount(0~1 기준)만큼 충전.</summary>
        public void ChargeSkillGauge(int characterIndex, float amount)
        {
            if (characterIndex >= 0 && characterIndex < playerSkillGauges.Count)
                playerSkillGauges[characterIndex].AddCharge(amount);
        }

        /// <summary>
        /// characterIndex번째 게이지를 비운다. 게이지를 언제 소모할지는 스킬을 실제로 발동시키는
        /// 쪽(BattleManager)이 정할 일이라 탭 시점에 자동으로 비우지 않는다 - 발동이 어떤 이유로
        /// 취소되면 게이지도 그대로 남아야 하기 때문.
        /// </summary>
        public void ConsumeSkillGauge(int characterIndex)
        {
            if (characterIndex >= 0 && characterIndex < playerSkillGauges.Count)
                playerSkillGauges[characterIndex].ConsumeGauge();
        }

        /// <summary>모든 스킬 게이지를 0으로. 배틀 시작 시 초기화용.</summary>
        /// <summary>
        /// 그 자리의 남은 스킬 게이지(0~1). 게임 종료 마무리 처리가 <b>소진할 양</b>을 알려고 읽는다.
        /// 게이지가 없으면 0.
        /// </summary>
        public float GetSkillGauge(int characterIndex)
            => characterIndex >= 0 && characterIndex < playerSkillGauges.Count
                ? playerSkillGauges[characterIndex].CurrentValue
                : 0f;

        /// <summary>
        /// 모든 스킬 게이지를 가득 채운다. "스킬 즉시" 아이템이 배틀 시작 때 부른다.
        ///
        /// <b>만충 연출은 저절로 따라온다</b> - SkillGaugeUI 가 "가득 찬 순간"을 스스로 알아채고
        /// 반지·잔상·반짝임을 돌린다(SetGauge -> ApplyVisual). 여기서 따로 부를 게 없다.
        /// </summary>
        public void FillSkillGauges()
        {
            for (int i = 0; i < playerSkillGauges.Count; i++)
            {
                if (playerSkillGauges[i] != null)
                    playerSkillGauges[i].SetGauge(1f);
            }
        }

        public void ResetSkillGauges()
        {
            for (int i = 0; i < playerSkillGauges.Count; i++)
                playerSkillGauges[i].SetGauge(0f);
        }

        private void HandleSkillActivated(int characterIndex)
            => OnCharacterSkillActivated?.Invoke(characterIndex);
    }
}
