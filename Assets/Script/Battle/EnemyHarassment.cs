using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JojoPuzzle.UI;
using JojoPuzzle.View;

using JojoPuzzle.Core;

namespace JojoPuzzle.Battle
{
    /// <summary>
    /// 적이 가벼운 방해를 걸 <b>조건</b>. 종류마다 반복 여부가 정해져 있다(별도 스위치 없음) -
    /// 섞어 두면 "1회짜리인데 반복 켜짐" 같은 설정이 나와서 무엇이 맞는지 알 수 없어진다.
    /// </summary>
    /// <summary>
    /// 적의 <b>가벼운 방해</b>를 담당한다. "언제 걸지"(트리거)와 "걸 때 무엇이 보이는지"(연출)를
    /// 한 곳에서 소유한다 - 스킬을 SkillPresentation 하나가 소유하는 것과 같은 이유다.
    /// 흩어놓으면 순서가 어긋나고, 겹쳐 들어올 때 무엇을 막아야 하는지 알 수 없어진다.
    ///
    /// 지금 하는 일(1·2단계):
    ///   1) 트리거 판정 - 맞춘 조각 수 / 적 체력 비율 / 남은 시간 / 주기
    ///   2) 연출 - 적이 공격 모션을 하고, 리더·파트너가 깜짝 놀라 깡총 뛴다
    ///
    /// <b>3단계(보드 변화)가 붙을 자리는 <see cref="OnHarass"/> 다.</b> 무작위 칸이 다른 색으로
    /// 바뀌거나 방해블록·구멍이 되는 처리는 아직 없다 - 그걸 여기서 직접 하지 않고 이벤트로 빼둔
    /// 이유는, 보드를 건드리는 코드가 BoardInputController 쪽 규율(잠긴 칸 회피·미안착 부여·
    /// 합체 재계산)을 따라야 해서 그쪽에 두는 게 맞기 때문이다.
    ///
    /// <b>"가벼운" 방해라 판을 멈추지 않는다</b> - 암전도 가림막도 없고 제한시간도 계속 흐른다.
    /// 그래서 다른 연출과 달리 이 연출은 플레이어의 조작을 전혀 막지 않는다.
    /// </summary>
    public class EnemyHarassment : MonoBehaviour
    {
        [Header("씬 참조")]
        [SerializeField] private BattleManager battleManager;

        [Tooltip("맞춘 조각 수 트리거에 쓴다. 비워두면 그 종류만 동작하지 않는다.")]
        [SerializeField] private BoardInputController inputController;

        [Header("연출 대상")]
        [Tooltip("적 초상화의 EnemyBattleAnimator. 비워두면 공격 모션 없이 나머지만 진행한다.")]
        [SerializeField] private EnemyBattleAnimator enemyAnimator;

        [Tooltip("깜짝 놀라 뛸 아군 초상화들(리더·파트너). 순서는 상관없다.")]
        [SerializeField] private StartleHopUI[] startleTargets;

        [Tooltip("방해블록이 생기는 자리를 덮을 뭉게구름. 비워두면 구름 없이 조각이 그냥 바뀐다 " +
                 "- 어디가 바뀌었는지 알아채기 어려워지므로 되도록 연결할 것.")]
        [SerializeField] private CloudBurstEffect cloudBurst;

        [Tooltip("구름이 피어오르고 방해로 바뀌기까지의 시간(초). 구름이 칸을 충분히 덮은 " +
                 "뒤에 바꿔야 바뀌는 순간이 그대로 보이지 않는다. 스킬과 같은 값을 기본으로 둔다.")]
        [SerializeField] private float changeDelayUnderClouds = 0.12f;

        // 방해블록(PlaceObstacle)과 구멍(PlaceHole)도 만들어져 있지만 <b>가벼운 방해는 쓰지 않는다</b>
        // (2026-08-22 사용자 방침). 가벼운 방해는 가장 낮은 단계인 "조각 하나를 다른 색으로 바꾸기"
        // 하나뿐이다. 방해블록과 구멍은 그보다 훨씬 무거운 요소라 보스의 공격 패턴이나 돌발 미션
        // 실패 같은 어려운 상황에 쓸 것 - 그때는 BoardInputController 의 PlaceObstacle / PlaceHole 을
        // 직접 부르면 된다(둘 다 public 이고 구름 연출까지 붙어 있다).

        [Header("트리거")]
        [Tooltip("하나라도 조건을 만족하면 방해가 발동한다. 종류별 반복 여부는 HarassTriggerKind 참고.")]
        [SerializeField]
        private List<HarassTrigger> triggers = new List<HarassTrigger>
        {
            new HarassTrigger { kind = HarassTriggerKind.MatchedPieces, value = 30f },
            new HarassTrigger { kind = HarassTriggerKind.EnemyHealthBelow, value = 0.5f },
            new HarassTrigger { kind = HarassTriggerKind.RemainingTimeBelow, value = 20f },
        };

        [Tooltip("방해와 방해 사이 최소 간격(초). 트리거 여러 개가 거의 동시에 걸려도 " +
                 "연달아 터지지 않게 막는다.")]
        [SerializeField] private float minInterval = 6f;

        [Tooltip("한 판에 걸 수 있는 최대 횟수. 0이면 제한 없음.")]
        [SerializeField] private int maxPerBattle = 0;

        [Tooltip("스탠드업 타임이 끝나고 이 시간(초)이 지나야 밀어뒀던 방해가 발동한다. " +
                 "끝나자마자 때리면 조각이 쏟아지는 와중이라 대응할 틈이 없다.")]
        [SerializeField] private float postStandUpDelay = 1.8f;

        [Header("연출 타이밍")]
        [Tooltip("적이 <b>내려찍는 순간</b>부터 아군이 놀라기까지의 간격(초). " +
                 "0이면 맞는 순간과 동시에 놀란다. 적이 준비 자세를 잡는 시간은 여기 포함되지 " +
                 "않는다 - EnemyBattleAnimator 가 알려주는 값으로 자동으로 맞춘다.")]
        [SerializeField] private float hopDelay = 0.05f;

        /// <summary>
        /// 방해가 실제로 발동한 순간 발행. <b>3단계(보드 변화)가 여기 붙는다.</b>
        /// 연출 시작과 같은 시점이 아니라 <b>아군이 놀란 직후</b>에 발행된다 - 조각이 먼저 바뀌고
        /// 나서 놀라면 인과가 뒤집혀 보이기 때문이다.
        /// </summary>
        public event System.Action OnHarass;

        /// <summary>이번 판에 방해를 건 횟수.</summary>
        public int HarassCount { get; private set; }

        private float lastHarassTime = -999f;
        private bool sequenceRunning;

        // 이 시각까지는 방해를 걸지 않는다(스탠드업이 끝난 뒤 한숨 돌리는 시간).
        private float harassBlockedUntil;

        // 조건은 만족했는데 지금은 걸 수 없어서(연출 중 등) 미뤄둔 상태.
        // 그냥 버리면 하필 그 순간에 걸린 트리거가 영영 사라진다 - 1회짜리는 특히.
        private bool pending;

        // 미뤄둔 방해가 <b>무엇을</b> 할지. 트리거마다 효과가 다르므로 요청과 함께 실어 나른다 -
        // 연출 시점에 트리거를 다시 찾으면 그 사이 다른 트리거가 끼어들어 엉뚱한 효과가 나간다.
        private HarassEffectKind pendingEffect = HarassEffectKind.Recolor;

        private float battleElapsed;

        private void OnEnable()
        {
            if (inputController != null)
            {
                inputController.OnPiecesMatched += HandlePiecesMatched;
                inputController.OnStandUpTimeEnd += HandleStandUpTimeEnd;
            }

            if (battleManager != null)
                battleManager.OnEnemyHealthChanged += HandleEnemyHealthChanged;
        }

        private void OnDisable()
        {
            if (inputController != null)
            {
                inputController.OnPiecesMatched -= HandlePiecesMatched;
                inputController.OnStandUpTimeEnd -= HandleStandUpTimeEnd;
            }

            if (battleManager != null)
                battleManager.OnEnemyHealthChanged -= HandleEnemyHealthChanged;
        }

        /// <summary>
        /// 스탠드업 종료 연출까지 전부 끝난 시점. 여기서 곧바로 밀린 방해를 터뜨리면 조각이
        /// 우수수 쏟아지는 와중에 겹쳐서 무슨 일이 일어난 건지도 모르고 대응도 못 한다.
        /// 한숨 돌릴 틈을 준 뒤에 발동시킨다.
        /// </summary>
        private void HandleStandUpTimeEnd()
        {
            harassBlockedUntil = Time.time + Mathf.Max(0f, postStandUpDelay);
        }

        /// <summary>
        /// 배틀이 다시 시작될 때 트리거 상태를 처음으로 되돌린다. 1회짜리 트리거가 이미 터진
        /// 채로 남아 있으면 다음 판에서 아무 일도 일어나지 않는다.
        /// </summary>
        /// <summary>
        /// 고른 스테이지에 방해 설정이 있으면 <b>그걸 쓴다</b>. 없으면 인스펙터 기본값 그대로다
        /// (배틀 씬을 직접 열어 테스트할 때).
        ///
        /// <b>스테이지 애셋의 목록을 그대로 쓰지 않고 복사한다</b> - HarassTrigger 는 fired/progress
        /// 같은 런타임 상태를 들고 있어서, 애셋 것을 직접 쓰면 그 진행도가 애셋에 남아 다음 판까지
        /// 따라간다(에디터에서는 저장까지 된다).
        /// </summary>
        private void ApplyStagePlan()
        {
            var stage = App.StageEntry.Stage;
            if (stage == null || stage.harassTriggers == null || stage.harassTriggers.Count == 0)
                return;

            triggers.Clear();
            for (int i = 0; i < stage.harassTriggers.Count; i++)
            {
                var source = stage.harassTriggers[i];
                if (source == null)
                    continue;

                triggers.Add(new HarassTrigger
                {
                    kind = source.kind,
                    value = source.value,
                    effect = source.effect
                });
            }

            if (stage.harassMinInterval > 0f)
                minInterval = stage.harassMinInterval;
        }

        /// <summary>
        /// 고른 칸에 이번 방해의 효과를 건다. 셋 다 <b>보드 규율은 BoardInputController 가</b>
        /// 챙긴다(잠긴 칸 회피·미안착 부여·합체 재계산) - 여기서는 무엇을 걸지만 정한다.
        /// </summary>
        private void ApplyEffect(HarassEffectKind effect, (int x, int y) cell)
        {
            if (inputController == null)
                return;

            switch (effect)
            {
                case HarassEffectKind.Obstacle:
                    inputController.PlaceObstacle(cell);
                    break;

                case HarassEffectKind.Hole:
                    inputController.PlaceHole(cell);
                    break;

                default:
                    inputController.RecolorCell(cell);
                    break;
            }
        }

        public void ResetForNewBattle()
        {
            ApplyStagePlan();

            for (int i = 0; i < triggers.Count; i++)
            {
                triggers[i].fired = false;
                triggers[i].progress = 0f;
            }

            HarassCount = 0;
            lastHarassTime = -999f;
            harassBlockedUntil = 0f;
            pending = false;
            battleElapsed = 0f;
        }

        private void Update()
        {
            if (battleManager == null || !battleManager.IsBattleRunning)
                return;

            battleElapsed += Time.deltaTime;

            EvaluateTimeTriggers();

            if (pending && CanHarassNow())
                StartCoroutine(HarassRoutine());
        }

        /// <summary>매치로 조각이 지워질 때마다 들어온다(스탠드업 고정도 포함).</summary>
        private void HandlePiecesMatched(int count)
        {
            if (count <= 0)
                return;

            for (int i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];
                if (trigger.kind != HarassTriggerKind.MatchedPieces)
                    continue;

                trigger.progress += count;
                if (trigger.progress < Mathf.Max(1f, trigger.value))
                    continue;

                // 넘긴 만큼만 덜어낸다 - 0으로 밀면 큰 매치 한 번에 쌓인 초과분이 사라져서
                // 조각을 많이 지울수록 오히려 방해가 뜸해진다.
                trigger.progress -= Mathf.Max(1f, trigger.value);
                RequestHarass(trigger.effect);
            }
        }

        private void HandleEnemyHealthChanged(float current, float max)
        {
            if (max <= 0f)
                return;

            float ratio = current / max;

            for (int i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];
                if (trigger.kind != HarassTriggerKind.EnemyHealthBelow || trigger.fired)
                    continue;

                if (ratio > trigger.value)
                    continue;

                trigger.fired = true;
                RequestHarass(trigger.effect);
            }
        }

        private void EvaluateTimeTriggers()
        {
            float remaining = battleManager.RemainingTimeSeconds;

            for (int i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];

                switch (trigger.kind)
                {
                    case HarassTriggerKind.RemainingTimeBelow:
                        if (trigger.fired || remaining > trigger.value)
                            break;
                        trigger.fired = true;
                        RequestHarass(trigger.effect);
                        break;

                    case HarassTriggerKind.EverySeconds:
                        float interval = Mathf.Max(1f, trigger.value);
                        if (battleElapsed - trigger.progress < interval)
                            break;
                        trigger.progress = battleElapsed;
                        RequestHarass(trigger.effect);
                        break;
                }
            }
        }

        /// <summary>
        /// 방해를 걸어달라는 요청. 지금 걸 수 없으면 <b>버리지 않고 미뤄둔다</b> -
        /// 조건은 이미 만족했는데 하필 연출 중이라는 이유로 사라지면, 1회짜리 트리거는
        /// 그 판에서 영영 발동하지 않는다.
        /// </summary>
        private void RequestHarass(HarassEffectKind effect)
        {
            if (maxPerBattle > 0 && HarassCount >= maxPerBattle)
                return;

            pending = true;
            pendingEffect = effect;
        }

        /// <summary>
        /// 지금 방해를 걸어도 되는 상태인지. 판이 멈춰 있거나 다른 연출이 화면을 잡고 있으면
        /// 미룬다 - 가벼운 방해가 스킬 컷인 위에 겹쳐 나오면 무슨 일이 일어난 건지 안 읽힌다.
        /// </summary>
        private bool CanHarassNow()
        {
            if (sequenceRunning)
                return false;

            if (battleManager == null || !battleManager.IsBattleRunning || battleManager.IsBattleOver)
                return false;

            if (Time.time - lastHarassTime < minInterval)
                return false;

            if (battleManager.IsPresentationBlocking)
                return false;

            // <b>스킬 시퀀스에는 끼어들지 않는다</b>(2026-08-28 사용자 지시).
            // 위 IsPresentationBlocking 은 대사창·암전이 <b>떠 있는 순간</b>만 참이라
            // 대사 → 연출 → 스킬 적용 사이의 빈틈으로 방해가 새어 나왔다. 이건 시퀀스가
            // 끝날 때까지 끊기지 않으므로 그 틈을 막는다.
            // 조건을 만족한 트리거는 pending 으로 남아 있다가 나중에 발동한다 - 스탠드업과 같다.
            if (battleManager.IsSkillSequenceRunning)
                return false;

            if (Time.time < harassBlockedUntil)
                return false;

            if (inputController != null)
            {
                if (inputController.IsPausedByMenu || !inputController.IsPlayablePhase)
                    return false;

                // 스탠드업 타임에는 <b>절대</b> 방해하지 않는다(사용자 방침). 플레이어가 큰 한 방을
                // 만들려고 집중하는 구간이라 여기서 끼어들면 몰입이 깨진다. 조건을 만족한 트리거는
                // pending 으로 남아 있다가, 스탠드업이 끝나고 postStandUpDelay 만큼 숨을 돌린
                // 뒤에 발동한다 - 그때는 충분히 대응할 수 있다.
                //
                // IsStandUpTimeActive 가 아니라 <b>Episode</b> 를 보는 게 핵심이다. 그쪽은 10초가
                // 끝나는 순간 꺼지는데 종료 연출은 그 뒤에 시작돼서, 화면에는 고정된 조각이 그대로
                // 있는데 방해가 새어 나왔다(실제 신고된 증상).
                if (inputController.IsStandUpEpisodeActive)
                    return false;
            }

            return true;
        }

        private IEnumerator HarassRoutine()
        {
            sequenceRunning = true;
            pending = false;

            try
            {
                // 1) 적이 공격. 준비 자세를 잡고 내려찍은 뒤 스스로 대기 자세로 돌아간다.
                enemyAnimator?.PlayAttack();

                // 2) 내려찍는 순간에 맞춰 아군이 놀란다. 준비 자세 시간은 애니메이터에게 물어봐서
                //    더한다 - 인스펙터에서 준비 시간을 바꿔도 놀라는 시점이 저절로 따라온다.
                float windup = enemyAnimator != null ? enemyAnimator.AttackImpactDelay : 0f;
                float wait = windup + Mathf.Max(0f, hopDelay);
                if (wait > 0f)
                    yield return new WaitForSeconds(wait);

                // 기다리는 사이에 스탠드업이 시작됐으면 여기서 접는다. 준비 자세가 나가고 나서
                // 스탠드업 배너가 뜨는 경우가 실제로 생기는데, 그대로 진행하면 집중해야 할 구간에
                // 방해가 겹친다. <b>다시 pending 으로 돌려놓아</b> 스탠드업이 끝난 뒤에 발동시킨다.
                // 스킬 시퀀스도 같이 본다 - 준비 자세가 나간 뒤에 플레이어가 스킬을 눌렀으면
                // 그대로 진행할 게 아니라 접어야 한다(2026-08-28 사용자 지시).
                if ((inputController != null && inputController.IsStandUpEpisodeActive)
                    || battleManager.IsSkillSequenceRunning)
                {
                    pending = true;
                    yield break; // 횟수도 시각도 기록하지 않는다 - 이번 건 일어나지 않은 것이다
                }

                // <b>아군은 반드시 같은 프레임에 뛴다.</b> 예전엔 시차를 뒀는데 둘이 어긋나 보여서
                // 사용자가 반려했다 - 같이 놀라야 "둘 다 방금 맞았다"로 읽힌다. 시차를 다시 넣지 말 것.
                if (startleTargets != null)
                {
                    for (int i = 0; i < startleTargets.Length; i++)
                        startleTargets[i]?.Hop();
                }

                // 방해가 실제로 일어난 지금에서야 기록한다. 위에서 접힌 경우까지 세면 아무 일도
                // 없었는데 maxPerBattle 만 소모되고 minInterval 도 헛돈다.
                lastHarassTime = Time.time;
                HarassCount++;

                // 3) 보드 변화 - 무작위 칸 하나가 방해블록이 된다.
                //    놀란 <b>뒤에</b> 바꾸는 이유는 조각이 먼저 바뀌면 인과가 뒤집혀 보이기 때문이다.
                //
                //    순서가 핵심이다: <b>자리를 고르고 → 구름을 피우고 → 가려진 뒤에 바꾼다.</b>
                //    바꾸고 나서 구름을 피우면 바뀌는 순간이 그대로 보이고, 구름 없이 바꾸면
                //    판 어딘가가 조용히 달라져서 플레이어가 알아채지 못한다.
                if (inputController != null && inputController.TryPickHarassCell(out var cell, out var worldPos))
                {
                    cloudBurst?.Burst(worldPos);

                    if (changeDelayUnderClouds > 0f)
                        yield return new WaitForSeconds(changeDelayUnderClouds);

                    // 고른 뒤 구름이 덮이는 사이에 그 칸이 매치로 사라졌을 수 있다.
                    // RecolorCell 이 자격을 다시 확인하고, 잃었으면 그냥 넘어간다
                    // (구름만 한 번 피고 마는 셈 - 드물고, 억지로 다른 칸에 놓으면
                    //  구름이 뜬 자리와 실제로 바뀐 자리가 어긋난다).
                    ApplyEffect(pendingEffect, cell);
                }

                // 다른 방해 효과(다른 색으로 변환·구멍 등)가 붙을 자리.
                OnHarass?.Invoke();
            }
            finally
            {
                // 중간에 코루틴이 끊겨도(오브젝트 비활성 등) 잠금이 반드시 풀려야 한다 -
                // 안 풀리면 그 판에서 방해가 영영 다시 안 걸린다.
                sequenceRunning = false;
            }
        }
    }
}
