using UnityEngine;
using JojoPuzzle.UI;
using JojoPuzzle.View;
using JojoPuzzle.Core;

using JojoPuzzle.App;

namespace JojoPuzzle.Battle
{
    public enum BattleResult
    {
        Victory, // 적 체력을 0으로 만듦
        Defeat   // 제한시간 초과 (현재 유일한 패배 조건 - 적의 반격도 플레이어 체력도 없음)
    }

    /// <summary>
    /// 배틀이 끝났을 때 결과와 함께 넘기는 정보. 보상 계산(러시타임 보너스 등)이 나중에
    /// 붙을 자리라 남은 시간 비율과 누적 데미지를 같이 담아둔다.
    /// </summary>
    public struct BattleOutcome
    {
        public BattleResult result;
        public float remainingTimeFraction; // 1=시작 직후, 0=시간 초과. 러시타임(1/3 이상 남기고 승리) 판정용
        public int totalDamageDealt;

        /// <summary>이번 판에 매치한 조각 수 누적. 골드 보상의 밑돌이다(<see cref="Core.GoldReward"/>).</summary>
        public int totalPiecesMatched;

        /// <summary>러시 타임에 직접 벌어들인 골드. 러시를 못 갔으면 0.</summary>
        public int rushGold;

        /// <summary>
        /// <summary>스티커 "큰 한 방"으로 번 코인. 스티커가 없으면 0.</summary>
        public int bigHitCoins;

        /// <summary>
        /// 이번 판에 쓴 <b>캐릭터 스킬 수</b>. "쓴 스킬 수만큼 추가 코인" 스티커가 이걸 읽는다.
        /// 마무리 처리가 게이지를 태우는 건 세지 않는다 - 플레이어가 고른 게 아니다.
        /// </summary>
        public int skillsUsed;
    }

    /// <summary>
    /// 배틀 한 판의 규칙을 담당. 지금까지 데미지는 계산만 되고 팝업·점수에만 반영됐는데,
    /// 이 클래스가 그 데미지를 받아 적 체력을 실제로 깎고 승패를 판정한다.
    ///
    /// 역할 분담:
    ///  - 적 체력의 "진짜 값"은 이 클래스가 갖는다. HealthBarUI/BattleHUDController는 표시 전용이라
    ///    값을 따로 들고 줄이지 않고 SetEnemyHealth로 받아 그리기만 한다(로직/뷰 분리 원칙).
    ///  - 데미지는 BoardInputController의 OnMatchDamage/OnStandUpDamage를 구독해서 받는다.
    ///    두 이벤트 모두 "연출이 끝나고 데미지가 확정된 시점"에 발행되므로 그대로 적용하면 된다.
    ///
    /// 규칙(2026-08-06 확정):
    ///  - 제한시간 60초가 기본. 특별한 스테이지만 예외적으로 다른 값을 준다.
    ///  - 패배 조건은 시간 초과 하나뿐. 적의 반격이나 플레이어 체력은 없다.
    ///
    /// 씬 세팅: 빈 GameObject에 붙이고 아래 참조 4개를 연결한 뒤, GameEntryPoint에 이 컴포넌트를
    /// 물려주면 보드 초기화가 끝난 직후 BeginBattle()이 호출된다.
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        [Header("씬 참조")]
        [SerializeField] private BoardInputController inputController;
        [SerializeField] private BattleHUDController hud;

        [Tooltip("팔레트에서 캐릭터를 조회하는 데 필요하다(스킬 게이지 분모인 skillRequiredMatchCount).")]
        [SerializeField] private BoardView boardView;
        [SerializeField] private ClearConditionUI clearConditionUI; // 선택 - 없으면 조건 표시만 생략

        [Tooltip("적의 가벼운 방해. 선택 - 없으면 방해가 아예 일어나지 않는다. " +
                 "배틀이 시작될 때 트리거 상태를 되돌려주기 위해서만 참조한다.")]
        [SerializeField] private EnemyHarassment enemyHarassment;

        [Tooltip("연속 매칭 카운트 표시. 선택 - 배틀이 시작될 때 횟수를 0으로 되돌려주기 " +
                 "위해서만 참조한다.")]
        [SerializeField] private ComboCountUI comboCountUI;

        [Tooltip("러시 타임(클리어 후 보너스 구간). <b>비워두면 러시 타임이 아예 없다</b> - " +
                 "시간을 많이 남기고 이겨도 곧바로 결과가 발표된다.")]
        [SerializeField] private RushTimeController rushTime;

        [Tooltip("타임오버를 알리는 띠. 마무리 처리 <b>전에</b> 잠깐 띄워 무슨 일이 났는지 " +
                 "알아차릴 시간을 준다. 비워두면 곧바로 마무리로 넘어간다.")]
        [SerializeField] private NoticeBannerUI timeOverBanner;

        [Tooltip("타임오버 띠에 띄울 문구.")]
        [SerializeField] private string timeOverMessage = "타임 오버";

        [Header("배틀 아이템")]
        [Tooltip("준비 화면에서 산 아이템의 <b>효과 수치</b>를 읽는 곳. " +
                 "비워두면 아이템을 사도 아무 효과가 없다.")]
        [SerializeField] private BattleItemCatalog battleItemCatalog;

        [Header("제한시간을 멈출 연출")]
        [Tooltip("둘 중 하나라도 떠 있는 동안 제한시간이 멈춘다. 플레이어가 조작할 수 없는 구간이라 " +
                 "시간이 흐르면 '아무것도 못 하는 사이에 시간이 깎였다'가 되기 때문.")]
        [SerializeField] private BoardDimOverlay boardDimOverlay;
        [SerializeField] private ScreenDimOverlay screenDimOverlay;

        [Header("적")]
        [Tooltip("적 최대 체력. 기획 참고값: 1-5 보스 18,000 / 4-5 보스 200,000 / 스킬 4-5 보스 650,000. " +
                 "밸런싱은 아직 미조정 상태다.")]
        [SerializeField] private float enemyMaxHealth = 18000f;

        [Header("제한시간")]
        [Tooltip("배틀 제한시간(초). 특별한 스테이지가 아니면 60초 고정.")]
        [SerializeField] private float battleDuration = 60f;

        [Header("클리어 조건 표시")]
        [SerializeField] private string clearConditionText = "보스를 쓰러뜨려라";

        [Header("임시 - 승패 처리")]
        [Tooltip("끄면 시간이 다 돼도, 적 체력이 0이 돼도 배틀이 끝나지 않는다. " +
                 "제한시간 시계와 체력바는 정상으로 도니 수치와 연출만 먼저 확인하고 싶을 때 쓴다. " +
                 "결과 화면이 준비되면 켤 것.")]
        [SerializeField] private bool endBattleEnabled;

        /// <summary>승패가 확정됐을 때 한 번만 발행. 결과 연출/보상 화면이 이걸 구독하면 된다.</summary>
        public event System.Action<BattleOutcome> OnBattleEnded;

        /// <summary>적 체력이 바뀔 때마다 발행 (현재값, 최대값). 보스 연출 단계 전환 등에 쓸 수 있다.</summary>
        public event System.Action<float, float> OnEnemyHealthChanged;

        /// <summary>
        /// 게이지가 가득 찬 캐릭터를 탭해서 스킬이 발동됐을 때 발행 (인자 = 편성 순서, 0=리더 1=파트너).
        /// 실제 스킬 효과가 붙을 자리다 - 지금은 구독자가 없어서 게이지만 비워진다.
        /// </summary>
        public event System.Action<int> OnCharacterSkillUsed;

        /// <summary>
        /// 편성 인원 수. BattleSetup.BuildPalette가 팔레트 앞자리에 리더(0)·파트너(1)를 넣고 그 뒤를
        /// 무작위 색으로 채우므로, <b>팔레트 색 인덱스가 곧 편성 순서</b>다. 스킬 게이지도 같은 순서로 늘어선다.
        /// </summary>
        private const int PartySlotCount = 2;

        public bool IsBattleRunning { get; private set; }
        public bool IsBattleOver { get; private set; }
        public float EnemyHealth { get; private set; }
        public float EnemyMaxHealth => enemyMaxHealth;
        public int TotalDamageDealt { get; private set; }

        /// <summary>
        /// 이번 판에 매치한 조각 수 누적. <b>일반 제거와 스탠드업 고정 둘 다</b> 센다 -
        /// 게이지를 채우는 것과 같은 기준이다(둘 다 "플레이어가 맞춘 조각"이므로).
        /// 골드 보상이 이 값을 밑돌로 쓴다.
        ///
        /// <b>⚠ 러시 타임에 지운 조각만은 빼고 센다</b> - 그건 <see cref="UI.RushTimeController"/>
        /// 가 따로 세어 러시 골드로 얹기 때문에, 여기서도 세면 같은 조각이 밑돌과 러시 몫에
        /// 두 번 계산된다. 마무리 처리로 지운 조각은 러시 골드로 안 가므로 그대로 센다.
        /// (캐릭터별 <see cref="View.BoardInputController.PiecesMatchedByPanel"/> 는 반대로
        /// 러시 조각도 센다 - 그건 "그 캐릭터를 얼마나 썼나"라 나눌 이유가 없다.)
        /// </summary>
        public int TotalPiecesMatched { get; private set; }

        /// <summary>남은 제한시간(초). 시계가 없으면 0.</summary>
        public float RemainingTimeSeconds => hud != null ? hud.RemainingTimeFraction * battleDuration : 0f;

        /// <summary>
        /// 지금 다른 연출이 화면을 잡고 있는지(가림막 또는 암전). <b>제한시간이 멈추는 조건과 같다</b> -
        /// 플레이어가 조작할 수 없는 구간이라는 뜻이라, 새 연출을 끼워 넣어도 되는지 판단할 때도
        /// 같은 기준을 쓴다(EnemyHarassment 가 이걸 본다).
        /// </summary>
        public bool IsPresentationBlocking =>
            (boardDimOverlay != null && boardDimOverlay.IsDimmed)
            || (screenDimOverlay != null && screenDimOverlay.IsDimmed);

        // 제한시간이 다 됐지만 스탠드업 종료 연출이 아직 재생 중이라 패배 판정을 미뤄둔 상태.
        // 그 연출 끝에 확정될 데미지로 적을 쓰러뜨릴 수도 있어서 결과를 먼저 못 박으면 안 된다.
        private bool defeatPendingStandUpResolution;

        // 승패는 확정됐지만 연출(스탠드업 / 스킬)이 아직 도는 중이라 <b>발표만</b> 미뤄둔 결과.
        // 위의 패배 유예와 방향이 반대다 - 그쪽은 "판정을 미루는" 것이고 이쪽은 판정을 이미
        // 끝낸 뒤 "화면에 알리는 것만" 미룬다.
        private bool outcomePendingPresentation;
        private BattleOutcome pendingOutcome;

        // 인스펙터에 적어둔 원래 값. 스테이지와 아이템이 이 위에 덮어쓰므로, 판을 다시 시작할 때
        // <b>여기서부터 다시 계산</b>해야 한다 - 안 그러면 시간 증가 아이템이 판마다 누적된다.
        private float inspectorBattleDuration;
        private float inspectorEnemyMaxHealth;
        private string inspectorClearConditionText;
        private bool inspectorDefaultsCaptured;

        private void Awake()
        {
            // Initialize 전이라도 이벤트 구독 자체는 안전하다(구독 시점과 무관하게 나중에 발행됨).
            // IsBattleRunning 가드가 있어서 BeginBattle 전에 들어온 데미지는 무시된다.
            if (inputController != null)
            {
                inputController.OnMatchDamage += ApplyDamageToEnemy;
                inputController.OnStandUpDamage += ApplyDamageToEnemy;
                inputController.OnStandUpTimeEnd += HandleStandUpTimeEnd;
                inputController.OnPiecesMatched += HandlePiecesMatched;

                // 스킬 게이지만 <b>다른 수</b>를 듣는다 - 강화 조각을 여러 개로 치는 스티커 때문이다.
                inputController.OnGaugePiecesMatched += ChargeSkillGauges;
                inputController.OnFinisherGaugeSpent += HandleFinisherGaugeSpent;
            }

            if (hud != null)
            {
                hud.OnBattleTimeUp += HandleTimeUp;
                hud.OnCharacterSkillActivated += HandleSkillActivated;
            }

            if (boardDimOverlay != null)
                boardDimOverlay.OnDimChanged += HandleOverlayChanged;
            if (screenDimOverlay != null)
                screenDimOverlay.OnDimChanged += HandleOverlayChanged;
        }

        private void OnDestroy()
        {
            if (inputController != null)
            {
                inputController.OnMatchDamage -= ApplyDamageToEnemy;
                inputController.OnStandUpDamage -= ApplyDamageToEnemy;
                inputController.OnStandUpTimeEnd -= HandleStandUpTimeEnd;
                inputController.OnPiecesMatched -= HandlePiecesMatched;
                inputController.OnGaugePiecesMatched -= ChargeSkillGauges;
                inputController.OnFinisherGaugeSpent -= HandleFinisherGaugeSpent;
            }

            if (hud != null)
            {
                hud.OnBattleTimeUp -= HandleTimeUp;
                hud.OnCharacterSkillActivated -= HandleSkillActivated;
            }

            if (boardDimOverlay != null)
                boardDimOverlay.OnDimChanged -= HandleOverlayChanged;
            if (screenDimOverlay != null)
                screenDimOverlay.OnDimChanged -= HandleOverlayChanged;
        }

        /// <summary>
        /// 가림막이나 화면 암전이 뜨고 사라질 때마다 제한시간을 멈추고 다시 굴린다.
        /// 둘 다 "플레이어가 조작할 수 없는 구간"을 뜻하므로, 그동안 시간이 흐르면
        /// 아무것도 못 하는 사이에 시간이 깎이는 셈이 된다.
        /// 하나가 꺼져도 다른 하나가 아직 떠 있을 수 있으므로 항상 둘 다 보고 판단한다
        /// (예: 스탠드업 배너가 걷히는데 대사창이 이어서 뜨는 경우).
        /// </summary>
        private void HandleOverlayChanged(bool _) => ApplyTimerPause();

        /// <summary>
        /// 매치된 조각 수만큼 <b>편성한 두 캐릭터의 게이지를 함께</b> 채운다.
        /// 어떤 색을 맞췄는지는 상관없다 - 리더 색을 맞췄다고 리더만 차는 게 아니라 둘 다 오른다.
        /// 캐릭터마다 분모(skillRequiredMatchCount)가 달라서 차는 속도만 갈린다 - 스킬이 센 캐릭터일수록
        /// 이 값을 크게 잡아 늦게 차게 만드는 식으로 밸런싱하라고 둔 값이다.
        /// </summary>
        private void HandlePiecesMatched(int matchedCount)
        {
            if (!IsBattleRunning || matchedCount <= 0)
                return;

            // <b>러시 타임에 지운 조각은 여기서 세지 않는다</b>(2026-08-28) - 그건
            // RushTimeController 가 따로 세어 러시 골드로 얹는다. 양쪽에서 세면 같은 조각이
            // <b>밑돌 골드(x0.35)와 러시 골드(x1)에 두 번</b> 계산된다.
            //
            // 예전에는 EndBattle 이 IsBattleRunning 을 꺼서 위 가드에 저절로 걸렸는데, 마무리
            // 처리를 넣으면서 그 플래그가 러시가 끝날 때까지 켜진 채로 남게 되어 막이 뚫렸다.
            //
            // <b>⚠ 창의 주인은 RushTimeController 하나다.</b> 저쪽이 조각을 세는 구간을 그대로
            // 물어본다 - 같은 판정을 여기서 따로 짜면(예전엔 IsRushTimeActive 를 봤다) 저쪽이
            // 경계를 옮기는 순간 조용히 어긋나서, 경계의 조각이 어디에도 안 세어지거나 또
            // 두 번 세어진다. 실제로 러시 골드 창이 시계보다 이르게 열리도록 바뀌었다.
            //
            // 마무리 처리로 지운 조각은 <b>여기서 센다</b> - 그쪽은 러시 골드로 안 가므로
            // 이중 계산이 아니고, 실제로 지워진 조각이라 밑돌에 들어가는 게 맞다.
            bool countedAsRushGold = rushTime != null && rushTime.IsCountingGold;

            // 게이지보다 먼저 센다. 게이지는 HUD 가 없으면 못 채우지만 보상은 그와 무관하다.
            if (!countedAsRushGold)
                TotalPiecesMatched += matchedCount;

        }

        /// <summary>
        /// 아군 <b>모두</b>의 스킬 게이지를 그 비율만큼 채운다(0.1 = 10%).
        /// 스티커 "N초마다 아군 캐릭터의 스킬 게이지 M% 회복" 이 쓴다 -
        /// 조각과 상관없이 시간으로 차는 유일한 길이라 따로 연다.
        /// </summary>
        public void ChargeAllSkillGauges(float fraction)
        {
            if (fraction <= 0f || hud == null || boardView == null)
                return;

            // 마무리·러시에는 안 찬다 - 조각으로 채울 때와 같은 규율이다(아래 주석 참고).
            if (inputController != null && inputController.Phase != BattlePhase.Playing)
                return;

            for (int slot = 0; slot < PartySlotCount; slot++)
            {
                if (boardView.GetCharacter(slot) != null)
                    hud.ChargeSkillGauge(slot, fraction);
            }
        }

        /// <summary>
        /// 스킬 게이지를 채운다. <b>골드 세기와 갈라 뒀다</b>(2026-09-03) - 강화 조각을
        /// 여러 개로 치는 스티커가 <b>게이지에만</b> 들어서, 두 쪽이 다른 수를 듣는다.
        /// </summary>
        private void ChargeSkillGauges(int matchedCount)
        {
            if (!IsBattleRunning || matchedCount <= 0)
                return;

            if (hud == null || boardView == null)
                return;

            // <b>마무리 처리와 러시 타임에는 게이지가 차지 않는다</b>(2026-08-28 사용자 지시).
            //  - 마무리: 게이지를 <b>소진해서</b> 조각을 지우는 구간이다. 그때 지워진 조각으로
            //    게이지가 다시 차면 방금 비운 막대가 눈앞에서 도로 채워진다(리더 차례에 지운
            //    조각이 파트너 게이지를 채우던 것이 실제 증상).
            //  - 러시: 오직 매칭으로 골드를 버는 시간이다. 게이지가 차면 "스킬을 쓸까"로 손이
            //    갈라져서 그 짧은 구간의 집중이 깨진다. 애초에 쓸 수도 없다(CanActivateSkill).
            if (inputController != null && inputController.Phase != BattlePhase.Playing)
                return;

            for (int slot = 0; slot < PartySlotCount; slot++)
            {
                var character = boardView.GetCharacter(slot);
                if (character == null)
                    continue;

                int required = Mathf.Max(1, character.skillRequiredMatchCount);
                hud.ChargeSkillGauge(slot, (float)matchedCount / required);
            }
        }

        /// <summary>
        /// 가득 찬 게이지를 탭해서 스킬이 발동됐을 때. 실제 효과는 아직 없어서 지금은 게이지만 비우고
        /// 알림만 발행한다 - 스킬이 생기면 여기서 효과를 실행한 뒤 게이지를 비우면 된다.
        /// </summary>
        /// <summary>
        /// 지금 스킬을 발동할 수 있는 상태인지. <b>게이지를 비우기 전에</b> 반드시 확인해야 한다 -
        /// 여기서 막지 않으면 게이지만 사라지고 효과는 안 나오는 사고가 난다(실제로 겪음:
        /// 리더 스킬 연출 중에 파트너 게이지를 누르면 게이지만 날아갔다).
        ///
        /// 막는 조건:
        ///  - 배틀이 안 돌고 있음(시작 전 / 이미 종료)
        ///  - 다른 스킬 연출이 진행 중(SkillPresentation 이 skillHoldCount 를 올려둔다)
        ///  - 판이 가려져 있음: 대사창·스탠드업 종료 연출(BoardDimOverlay), 스킬 암전(ScreenDimOverlay).
        ///    플레이어가 조작할 수 없는 구간이라 그때의 탭은 의도한 입력으로 보기 어렵다.
        ///  - <b>러시 타임</b>(2026-08-28 사용자 지시): 매칭으로 골드를 버는 데만 집중하는 구간이다.
        ///    예전엔 IsBattleRunning 이 false 라 저절로 잠겼는데, 마무리 처리를 넣으면서 그게
        ///    러시가 끝날 때까지 켜진 채로 남아 <b>스킬을 쓸 수 있게 돼 있었다</b>.
        /// </summary>
        public bool CanActivateSkill =>
            IsBattleRunning
            && (inputController == null || inputController.Phase == BattlePhase.Playing)
            && skillHoldCount <= 0
            && (boardDimOverlay == null || !boardDimOverlay.IsDimmed)
            && (screenDimOverlay == null || !screenDimOverlay.IsDimmed);

        // 스킬 발동을 잠가둔 요청 수. 연출이 여러 겹으로 들어올 수 있어서 bool 이 아니라 카운터다
        // (하나가 끝났다고 풀어버리면 아직 도는 다른 연출이 열린 채로 남는다).
        private int skillHoldCount;

        /// <summary>
        /// 스킬 발동을 잠그거나 푼다. 연출이 시작할 때 true, 끝날 때 false 로 <b>짝을 맞춰</b> 부를 것.
        /// UI 쪽(SkillPresentation)이 부르므로 BattleManager 가 UI 를 알 필요가 없다.
        /// </summary>
        public void HoldSkillActivation(bool hold)
        {
            skillHoldCount = Mathf.Max(0, skillHoldCount + (hold ? 1 : -1));

            // 연출이 도는 사이에 판이 끝났다면 <b>여기가 발표할 자리</b>다(2026-08-28 사용자 결정).
            // 마지막 연출이 풀리는 순간이라, 이때는 화면을 덮을 것이 없다.
            if (skillHoldCount <= 0)
                TryAnnouncePendingOutcome();
        }

        private void HandleSkillActivated(int characterIndex)
        {
            // 게이지를 비우기 전에 판단한다 - 순서가 바뀌면 막아도 게이지는 이미 사라진 뒤다.
            if (!CanActivateSkill)
                return;

            hud?.ConsumeSkillGauge(characterIndex);
            skillsUsed++;
            OnCharacterSkillUsed?.Invoke(characterIndex);
        }

        /// <summary>
        /// 마무리 처리가 그 자리의 게이지를 썼다 - 막대를 비운다(2026-08-28 사용자 지적).
        /// 빛이 게이지에서 튀어나가는데 막대가 그대로 차 있으면 무엇을 쓴 건지 읽히지 않는다.
        ///
        /// <b>스킬 발동(HandleSkillActivated)과는 다른 길이다</b> - 이건 플레이어가 누른 게 아니라
        /// 종료 처리가 강제로 쓰는 것이라 CanActivateSkill 을 보지 않는다(그 조건은 이미 거짓이다).
        /// </summary>
        private void HandleFinisherGaugeSpent(int characterIndex)
            => hud?.ConsumeSkillGauge(characterIndex);

        private void ApplyTimerPause()
        {
            if (hud == null)
                return;

            // <b>⚠ 끝난 판의 시계는 절대 다시 굴리지 않는다</b>(2026-08-28 사용자 신고:
            // 결과 화면에서 시계가 계속 돌았다). EndBattle 이 StopTimer 로 세워두는데,
            // 그 뒤에 가림막이 걷힐 때마다(타임오버 띠·결과 화면 배경) 이 함수가 불려
            // <b>Resume 으로 도로 굴려버리고</b> 있었다.
            //
            // <b>러시 타임 동안은 아예 손대지 않는다</b> - 그 시계는 러시가 자기 길이로 새로
            // 굴린 것이라 주인이 다르다. 여기서 멈추면 보너스 시간이 그대로 얼어붙고,
            // 반대로 풀면 띠가 흐르는 동안 <b>끝난 판의 시계</b>가 되살아난다.
            if (rushTime != null && rushTime.IsActive)
                return;

            if (IsBattleOver)
            {
                hud.SetTimerPaused(true);
                return;
            }

            bool blocked = IsPresentationBlocking;

            hud.SetTimerPaused(blocked);
        }

        /// <summary>
        /// 배틀 시작. 보드가 다 만들어진 뒤에 불러야 하므로 GameEntryPoint가 마지막 단계에서 호출한다
        /// (여기서 타이머가 돌기 시작하는데, 보드가 아직 없는 동안 시간이 깎이면 안 되기 때문).
        /// </summary>
        /// <param name="deferStart">
        /// true 면 <b>시계를 굴리지 않고 '스킬 즉시' 만충 연출도 하지 않는다</b> - 시작 연출
        /// (<see cref="BattleIntroSequence"/>)이 자기 순서 안에서
        /// <see cref="PlayIntroSkillFull"/> 과 <see cref="StartBattleTimer"/> 로 부른다.
        ///
        /// 나머지(적 체력·팔레트·아이템 효과)는 <b>그대로 지금 적용된다</b> - 판은 이미 다
        /// 만들어져 있어야 연출 뒤에 곧바로 시작할 수 있다. 그동안 조각을 못 만지게 막는 건
        /// 연출 쪽 일이다(<c>BoardInputController.IsIntroPlaying</c>).
        /// </param>
        public void BeginBattle(bool deferStart = false)
        {
            CaptureInspectorDefaults();
            ApplySelectedStage();
            ApplyPurchasedItems();

            EnemyHealth = Mathf.Max(1f, enemyMaxHealth);
            TotalDamageDealt = 0;
            TotalPiecesMatched = 0;
            IsBattleOver = false;
            IsBattleRunning = true;
            defeatPendingStandUpResolution = false;
            outcomePendingPresentation = false;

            if (inputController != null)
            {
                // ResetForNewBattle 이 단계를 Playing 으로 되돌린다. 시작 연출이 붙는 판은
                // 그 뒤에 GameEntryPoint 가 Intro 로 옮긴다.
                inputController.ResetForNewBattle();
            }

            clearConditionUI?.SetCondition(clearConditionText, 1);
            hud?.ResetSkillGauges();

            // 시작 연출이 있으면 적 체력만 맞춰두고 시계는 그쪽이 굴린다.
            if (deferStart)
                hud?.PrepareBattle(EnemyHealth);
            else
                hud?.StartBattle(EnemyHealth, battleDuration);

            // <b>ResetSkillGauges 다음이다</b> - 한 번 0으로 밀어둔 뒤에 채워야 "가득 찬 순간"이
            // 잡혀 만충 연출이 돈다. 연출이 있는 판은 캐릭터가 뛰어 들어온 뒤로 미룬다
            // (여기서 채우면 아직 화면 밖일 때 연출이 지나가 버린다).
            if (!deferStart)
                PlayIntroSkillFull();

            // 1회짜리 트리거(체력 절반 등)가 지난 판에서 터진 채로 남아 있으면 이번 판엔
            // 아무 일도 일어나지 않는다.
            enemyHarassment?.ResetForNewBattle();
            comboCountUI?.ResetForNewBattle();

            OnEnemyHealthChanged?.Invoke(EnemyHealth, enemyMaxHealth);
        }

        /// <summary>
        /// '스킬 즉시' 아이템을 샀으면 게이지를 가득 채운다(만충 연출은 <c>SkillGaugeUI</c> 가
        /// "가득 찬 순간"을 스스로 알아채고 돌린다 - 여기서 따로 부를 게 없다).
        ///
        /// 시작 연출이 <b>캐릭터가 다 들어온 뒤에</b> 부른다. 안 샀으면 아무 일도 안 하므로
        /// 연출 쪽에서 조건을 따로 볼 필요가 없다.
        /// </summary>
        public void PlayIntroSkillFull()
        {
            if (skillFullRequested)
                hud?.FillSkillGauges();
        }

        /// <summary>
        /// 제한시간을 굴리기 시작한다. <see cref="BeginBattle"/> 를 <c>deferStart: true</c> 로
        /// 부른 판에서 시작 연출이 마지막에 부른다 - '시작!'이 뜨는 그 순간이다.
        /// </summary>
        public void StartBattleTimer()
        {
            hud?.StartTimer(battleDuration);
        }

        /// <summary>
        /// 스테이지 선택에서 고르고 들어왔으면 <b>인스펙터 값 대신 그 스테이지의 수치</b>를 쓴다.
        ///
        /// 적 체력·제한시간·클리어 조건이 여기 인스펙터에 박혀 있던 게 오래된 부채였다. 이제
        /// <see cref="StageDefinition"/> 이 그 자리인데, <b>인스펙터 값을 지우지는 않았다</b> -
        /// 배틀 씬을 직접 열어 테스트할 때(스테이지를 안 거쳤을 때) 쓸 기본값이 필요하다.
        /// </summary>
        private void ApplySelectedStage()
        {
            var stage = StageEntry.Stage;
            if (stage == null)
            {
                // 스테이지 없이 배틀 씬을 직접 열었을 때. <b>인스펙터 값으로 되돌린다</b> -
                // 그냥 두면 지난 판에 아이템이 올려놓은 값이 그대로 남는다.
                enemyMaxHealth = inspectorEnemyMaxHealth;
                battleDuration = inspectorBattleDuration;
                clearConditionText = inspectorClearConditionText;
                return;
            }

            enemyMaxHealth = stage.enemyMaxHealth;
            battleDuration = stage.battleDuration;
            clearConditionText = stage.clearConditionText;
        }

        /// <summary>인스펙터에 적어둔 값을 한 번만 기억해둔다. 판을 다시 시작할 때의 기준점이다.</summary>
        private void CaptureInspectorDefaults()
        {
            if (inspectorDefaultsCaptured)
                return;

            inspectorDefaultsCaptured = true;
            inspectorBattleDuration = battleDuration;
            inspectorEnemyMaxHealth = enemyMaxHealth;
            inspectorClearConditionText = clearConditionText;
        }

        /// <summary>
        /// 준비 화면에서 산 아이템을 이번 판에 반영한다(2026-08-27 구현).
        ///
        /// <code>
        ///   데미지 증가 - 이번 판의 데미지 배율을 올린다(전투력 +50% 와 같은 말)
        ///   시간 증가   - 제한시간을 그만큼 늘린다
        ///   스킬 즉시   - 게이지를 가득 채운 채로 시작한다
        ///   코인 증가   - <b>여기서 안 한다</b>. 정산할 때 결과 화면이 카탈로그를 직접 읽는다
        ///                 (배틀 중에는 쓸 일이 없는 값이라 그때 읽는 게 맞다)
        /// </code>
        ///
        /// <b>반드시 ApplySelectedStage 다음이다</b> - 그쪽이 제한시간을 스테이지 값으로 덮어쓰므로,
        /// 먼저 더하면 그 위에 덮여서 시간 증가가 사라진다.
        /// </summary>
        private void ApplyPurchasedItems()
        {
            // 산 게 없어도 지난 판의 배율이 남으면 안 되므로 항상 되돌려놓고 시작한다.
            if (inputController != null)
                inputController.ItemDamageMultiplier = 1f;

            skillFullRequested = false;

            if (battleItemCatalog == null || battleItemCatalog.items == null)
                return;

            for (int i = 0; i < battleItemCatalog.items.Length; i++)
            {
                var item = battleItemCatalog.items[i];
                if (item == null || !StageEntry.IsItemSelected(item.kind))
                    continue;

                switch (item.kind)
                {
                    case BattleItemKind.DamageUp:
                        if (inputController != null)
                            inputController.ItemDamageMultiplier = Mathf.Max(1f, item.value);
                        break;

                    case BattleItemKind.TimeUp:
                        battleDuration += Mathf.Max(0f, item.value);
                        break;

                    case BattleItemKind.SkillFull:
                        skillFullRequested = true;
                        break;
                }
            }
        }

        // "스킬 즉시" 를 샀는지. 게이지는 <b>시계가 시작된 뒤에</b> 채워야 해서 표시만 해둔다.
        private bool skillFullRequested;

        /// <summary>
        /// 적에게 데미지. 매치/스탠드업 이벤트가 자동으로 호출하지만, 나중에 캐릭터 스킬처럼
        /// 보드 밖에서 오는 데미지도 같은 문을 통과하도록 public으로 열어둔다.
        /// </summary>
        public void ApplyDamageToEnemy(int amount)
        {
            if (!IsBattleRunning || amount <= 0)
                return; // 이미 끝난 배틀에 뒤늦게 도착한 데미지는 버린다(동시 매치가 여러 개일 때 실제로 생김)

            EnemyHealth = Mathf.Max(0f, EnemyHealth - amount);
            TotalDamageDealt += amount;
            AccumulateBigHitCoins(amount);

            hud?.SetEnemyHealth(EnemyHealth);
            OnEnemyHealthChanged?.Invoke(EnemyHealth, enemyMaxHealth);

            if (EnemyHealth <= 0f)
                EndBattle(BattleResult.Victory);
        }

        /// <summary>
        /// 스티커 "한 번의 데미지가 보스 최대 체력 N% 초과시 준 데미지의 M%만큼 코인 지급".
        ///
        /// ⭐ <b>코인을 그 자리에서 주지 않고 세어만 둔다</b> - 이 게임의 코인은 결과 화면
        /// 영수증 <b>한 곳</b>에서 정산한다(러시 골드만 예외이고, 그건 그 구간이 통째로
        /// 코인을 버는 시간이라서다). 두 곳에서 주면 영수증의 합이 실제 지급액과 어긋나고,
        /// 그러면 플레이어 눈에는 고장으로 보인다.
        ///
        /// ⚠ <b>한 방마다</b> 본다 - 누적 데미지가 아니다. 그게 이 스티커의 값어치다.
        /// </summary>
        private void AccumulateBigHitCoins(int amount)
        {
            var sticker = StickerEffects.FindAttached(StickerEffect.BigHitCoin);
            if (sticker == null || enemyMaxHealth <= 0f)
                return;

            float need = enemyMaxHealth * sticker.threshold * 0.01f;
            if (amount <= need)
                return;

            bigHitCoins += Mathf.RoundToInt(amount * sticker.value * 0.01f);
        }

        // 큰 한 방으로 번 코인. 판마다 0에서 시작한다.
        private int bigHitCoins;

        /// <summary>
        /// 제한시간 종료. 스탠드업 종료 연출이 재생 중이면 곧바로 패배로 확정하지 않고 미룬다 -
        /// 그 연출은 몇 초 뒤에 큰 데미지를 확정하는데, 시간이 다 됐다고 먼저 패배를 선언해버리면
        /// 이미 성립한 한 방이 통째로 사라진다. 스탠드업 타임 자체가 진행 중인 경우(아직 고정된
        /// 조각을 쌓는 중)도 마찬가지로 그 정산까지 기다린다.
        /// </summary>
        private void HandleTimeUp()
        {
            if (IsBattleOver)
                return;

            // 두 플래그를 OR로 묶던 것을 하나로 바꿨다(2026-08-21). 10초가 끝나고 종료 연출이
            // 시작되기 전까지 <b>둘 다 false인 구간</b>이 있어서, 하필 그때 시간이 다 되면 유예가
            // 통째로 새어 곧바로 패배가 확정됐다 - 몇 초 뒤에 큰 데미지가 확정될 참인데도.
            // IsStandUpEpisodeActive 는 그 구간까지 끊기지 않는다.
            if (inputController != null && inputController.IsStandUpEpisodeActive)
            {
                // 스탠드업 타임 도중에 시간이 다 됐다 - 여기서 끝내지 않고 남은 10초를 그대로 쓰게 둔다.
                // 조작도 막지 않는다: 스탠드업은 플레이어가 보상을 챙기는 구간이라 중간에 잘라버리면
                // 시간이 다 됐다는 이유로 이미 얻은 기회를 뺏는 셈이 된다.
                // 종료 연출(불꽃 → 공격 모션)까지 전부 끝난 뒤 HandleStandUpTimeEnd가 패배를 확정한다.
                defeatPendingStandUpResolution = true;
                return;
            }

            EndBattle(BattleResult.Defeat);
        }

        /// <summary>
        /// 스탠드업 종료 연출까지 완전히 끝난 시점. 여기서 할 일이 둘이다:
        ///  - 스탠드업 도중에 이미 승패가 확정돼 <b>발표만 미뤄둔 결과</b>가 있으면 지금 알린다.
        ///  - 시간이 다 돼서 <b>패배 판정을 미뤄뒀다면</b> 여기서 확정한다.
        /// 그 사이 데미지로 적이 쓰러졌다면 ApplyDamageToEnemy가 이미 승리로 끝냈으므로
        /// IsBattleOver 가드에 걸려 아무 일도 일어나지 않는다.
        /// </summary>
        private void HandleStandUpTimeEnd()
        {
            if (outcomePendingPresentation)
            {
                // 스킬 연출이 겹쳐 있으면 그쪽이 끝날 때 발표된다 - 여기서 못 나가도 길이 남는다.
                TryAnnouncePendingOutcome();
                return;
            }

            if (defeatPendingStandUpResolution && !IsBattleOver)
                EndBattle(BattleResult.Defeat);
        }

        private void EndBattle(BattleResult result)
        {
            if (IsBattleOver)
                return; // 승패는 한 번만 확정된다 - 동시 매치가 같은 프레임에 여러 번 들어와도 안전

            if (!endBattleEnabled)
            {
                // 아직 승패 처리를 켜지 않았다 - 시계는 끝까지 채워진 채로 멈추고, 적 체력도 0에서
                // 머무르며, 조작은 계속 가능하다. 결과 화면이 생기면 이 스위치만 켜면 된다.
                defeatPendingStandUpResolution = false;
                return;
            }

            // <b>IsBattleRunning 은 아직 끄지 않는다.</b> 마무리 처리(남은 상자·스킬 게이지를
            // 데미지로 바꾸는 구간)의 데미지가 적에게 통해야 하기 때문이다 - 여기서 꺼버리면
            // ApplyDamageToEnemy 가 전부 버려서 "타임오버여도 마무리로 이길 수 있다"가 성립하지 않는다.
            // IsBattleOver 는 지금 켠다(재진입 방지). 끄는 건 ResolveRoutine 끝이다.
            IsBattleOver = true;
            defeatPendingStandUpResolution = false;

            hud?.StopTimer();

            // <b>여기서 조작을 막지 않는다.</b> 러시 타임에 들어가면 플레이어가 계속 판을
            // 만져야 하기 때문이다. 막는 건 결과를 실제로 발표하는 자리(AnnounceOutcome)로 옮겼다.

            var outcome = new BattleOutcome
            {
                result = result,
                remainingTimeFraction = hud != null ? hud.RemainingTimeFraction : 0f,
                totalDamageDealt = TotalDamageDealt,
                totalPiecesMatched = TotalPiecesMatched,
                skillsUsed = skillsUsed,
                bigHitCoins = bigHitCoins
            };

            // <b>스탠드업이 아직 도는 중이면 결과 발표를 그 정산 뒤로 미룬다.</b>
            //
            // 스탠드업 타임에 평타로 적을 눕히는 일이 실제로 일어난다(제거형 스킬이 붙으면 더
            // 잦아진다). 그때 곧바로 결과 화면을 띄우면 <b>고정된 조각이 불꽃이 되어 날아가는
            // 연출 위로 결과판이 덮인다</b> - 게다가 그 연출이 끝나야 StandHeld 칸이 풀리고
            // lockedCells 도 반납되므로, 화면만 덮어놓으면 판은 뒤에서 계속 돌고 있게 된다.
            //
            // 그렇다고 연출을 잘라내지도 않는다. 자르면 정리 코드를 전부 손으로 다시 짜야 하고,
            // 무엇보다 스탠드업 마무리(불꽃 흡수 → 리더의 공격)는 승리 직전에 보기 딱 좋은 그림이다.
            // 대신 남은 카운트다운만 앞당겨서(CutStandUpTimeShort) 이긴 뒤에 10초를 세는 일이
            // 없게 한다. 발표는 OnStandUpTimeEnd 에서 HandleStandUpTimeEnd 가 이어받는다.
            //
            // 스탠드업 <b>종료 데미지로</b> 적이 쓰러진 경우도 같은 길로 간다 - 그때도
            // IsStandUpEpisodeActive 는 아직 true 라서, 데미지 숫자를 읽는 시간까지 끝난 뒤에야
            // 결과가 뜬다(예전에는 숫자가 떠 있는 채로 결과판이 덮었다).
            // <b>스킬 연출도 같은 길로 간다</b>(2026-08-28 사용자 결정) - 매치 데미지로 적이
            // 쓰러지거나 하필 그때 타임오버가 나도, 도는 중인 연출을 <b>끝까지 보여주고</b>
            // 결과는 그 뒤에 발표한다. 잘라내면 암전·스킬 잠금·조각 상태를 손으로 되돌리는
            // 코드를 따로 짜야 하고, 스탠드업 때 그 방식을 안 쓰기로 한 것과도 어긋난다.
            if (IsOutcomeAnnouncementBlocked)
            {
                pendingOutcome = outcome;
                outcomePendingPresentation = true;

                // 스탠드업이면 남은 카운트다운만 앞당긴다 - 이미 이긴 판에서 10초를 다 셀 이유가 없다.
                // (스킬 연출은 짧아서 앞당길 게 없다.)
                if (inputController != null && inputController.IsStandUpEpisodeActive)
                    inputController.CutStandUpTimeShort();

                return;
            }

            AnnounceOutcome(outcome);
        }

        /// <summary>
        /// <b>지금 결과를 발표하면 다른 연출 위로 덮이는가.</b> 발표를 미뤄야 하는 이유가
        /// 여럿이라 <b>한 곳에 모아 둔다</b> - 부르는 쪽마다 나열하면 새 연출이 생겼을 때
        /// 그 줄들을 전부 찾아 고쳐야 한다(단계 플래그가 그래서 열거형이 됐다).
        ///
        ///  - <b>스탠드업</b>: 고정된 조각이 불꽃이 되어 날아가는 중이고, 그 연출이 끝나야
        ///    StandHeld 칸이 풀리고 lockedCells 도 반납된다.
        ///  - <b>스킬 연출</b>: 암전이 걸려 있고 스킬 잠금(skillHoldCount)도 아직 안 풀렸다.
        /// </summary>
        private bool IsOutcomeAnnouncementBlocked =>
            (inputController != null && inputController.IsStandUpEpisodeActive)
            || IsSkillSequenceRunning;

        /// <summary>
        /// <b>스킬 한 판이 도는 중인가</b> - 대사 → 캐릭터 연출 → 스킬 적용까지 통째로.
        /// <see cref="UI.SkillPresentation"/> 이 시작할 때 잠그고 끝날 때 푼다.
        ///
        /// <b>가림막(<see cref="IsPresentationBlocking"/>)으로 대신하지 말 것</b>: 그건 대사창이나
        /// 암전이 <b>떠 있는 순간</b>만 참이라, 단계와 단계 사이의 빈틈에서 적의 방해가 끼어든다
        /// (2026-08-28 사용자 신고). 이쪽은 시퀀스가 끝날 때까지 끊기지 않는다.
        /// </summary>
        public bool IsSkillSequenceRunning => skillHoldCount > 0;

        /// <summary>
        /// 미뤄둔 결과가 있으면 <b>지금 발표해도 되는지 보고</b> 발표한다.
        /// 막고 있던 연출이 끝나는 자리마다 부른다 - 어느 쪽이 마지막으로 끝나든 여기로 모인다.
        /// </summary>
        private void TryAnnouncePendingOutcome()
        {
            if (!outcomePendingPresentation || IsOutcomeAnnouncementBlocked)
                return;

            outcomePendingPresentation = false;
            AnnounceOutcome(pendingOutcome);
        }

        /// <summary>
        /// 종료가 확정된 뒤의 순서를 <b>코루틴 하나가 통째로 소유한다</b>
        /// (SkillPresentation·BattleResultPanel 과 같은 방침).
        ///
        /// <code>
        ///   조작 차단
        ///   판 다시 채우기 - 안내 띠와 같이 흐른다. 빈 판으로 마무리를 해봤자 뒤집을 수 없다
        ///   (타임오버 띠)  - 시간이 다 돼서 끝났을 때만
        ///   마무리 처리    - 남은 상자와 스킬 게이지를 전부 데미지로 (타임오버든 승리든 똑같이)
        ///   러시 타임      - 이미 적을 눕혔고 시간을 많이 남겼을 때만
        ///   승패 확정      - <b>그때의 적 체력</b>으로 정한다
        ///   결과 발표
        /// </code>
        ///
        /// <b>승패를 여기서, 마무리 처리가 끝난 뒤에 정하는 게 핵심이다</b>(2026-08-25 사용자 기획) -
        /// 타임오버로 들어와도 마무리에서 적을 눕히면 승리가 된다. 그래서 EndBattle 이 받은
        /// 트리거(왜 끝났는가)와 최종 결과(이겼는가)는 다를 수 있다.
        /// </summary>
        private void AnnounceOutcome(BattleOutcome value)
        {
            StartCoroutine(ResolveRoutine(value));
        }

        private System.Collections.IEnumerator ResolveRoutine(BattleOutcome value)
        {
            // ── 여기서부터 조작 불가 ─────────────────────────────────────
            // <b>타임오버 띠보다 먼저</b> 막아야 한다. 띠가 떠 있는 동안에도 조각을 옮길 수
            // 있으면 안 된다(2026-08-25 사용자 신고). 러시 타임은 아래 마무리 처리 다음이고
            // 그때 SetRushTime 이 이 차단을 다시 풀어준다 - 보너스 구간을 막지 않는다.
            inputController?.BeginEndSequence();

            // ── 판을 다시 채운다 ─────────────────────────────────────────
            // <b>마무리 처리에 들어가기 전에</b> 판을 가득 채운다(2026-08-28 사용자 지시).
            // 조각이 얼마 없는 판으로 마무리를 해봤자 큰 이득이 없어서, 타임오버로 몰린 판이
            // 뒤집힐 여지 자체가 없었다. 채우다 매치가 성립하면 그 데미지도 그대로 들어간다.
            //
            // <b>안내 띠와 같이 흐른다</b> - 띠를 다 읽고 나서 조각이 채워지기 시작하면 그만큼
            // 늘어진다. 조작은 위에서 이미 막혔으므로 채워지는 동안 손댈 수는 없다.
            var refill = inputController != null
                ? StartCoroutine(inputController.RefillBoard())
                : null;

            // ── 타임오버 알림 ────────────────────────────────────────────
            // <b>마무리 처리 전에</b> 띄운다(2026-08-25 사용자 지시) - 갑자기 조각이 사라지기
            // 시작하면 왜 그런지 알 수 없다. 시간이 다 돼서 끝난 경우에만 띄운다.
            if (value.result == BattleResult.Defeat && timeOverBanner != null)
                yield return StartCoroutine(timeOverBanner.Play(timeOverMessage));

            // 띠가 먼저 끝났으면 채우기가 끝날 때까지 기다린다 - 판이 채워지는 도중에 마무리가
            // 시작되면 리필이 멈춰서 <b>빈 칸이 그대로 굳는다</b>.
            if (refill != null)
                yield return refill;

            // ── 마무리 처리 ──────────────────────────────────────────────
            // 이 구간에도 IsBattleRunning 이 켜져 있어서 데미지가 적에게 그대로 통한다.
            if (inputController != null)
            {
                yield return StartCoroutine(
                    inputController.RunFinisher(hud != null ? hud.GetSkillGauge(0) : 0f,
                                                hud != null ? hud.GetSkillGauge(1) : 0f));
            }

            // ── 러시 타임 ────────────────────────────────────────────────
            // <b>마무리 처리 다음이다</b>(2026-08-25 사용자 지시로 순서를 바꿨다) - 남은 자원을
            // 다 쓴 뒤에 보너스 구간이 온다. 조건은 "이미 적을 눕혔고 시간이 많이 남았다"라,
            // 타임오버로 들어왔으면 남은 시간이 0이라 저절로 걸리지 않는다.
            if (value.result == BattleResult.Victory
                && rushTime != null
                && GoldReward.IsRushTime(value.remainingTimeFraction))
            {
                rushFinished = false;
                rushTime.OnRushFinished += HandleRushFinished;
                rushTime.Begin(RemainingTimeSeconds);

                while (!rushFinished)
                    yield return null;

                rushTime.OnRushFinished -= HandleRushFinished;
                value.rushGold = rushGoldEarned;
            }

            // ── 승패 확정 ────────────────────────────────────────────────
            // <b>결과 화면에서는 퍼즐과 관련된 것이 전부 선다</b>(2026-08-28 사용자 지시).
            // 판은 아래 EnterPhase(Finished)가 세우고, 시계는 여기서 세운다 - 러시가 자기
            // 시계를 굴려놨을 수 있어서 EndBattle 때 한 번 멈춘 것만으로는 부족하다.
            hud?.StopTimer();

            IsBattleRunning = false;

            if (inputController != null)
                inputController.EnterPhase(BattlePhase.Finished);

            value.result = EnemyHealth <= 0f ? BattleResult.Victory : BattleResult.Defeat;

            if (value.result == BattleResult.Victory)
                clearConditionUI?.SetProgress(1);

            value.totalDamageDealt = TotalDamageDealt;
            value.totalPiecesMatched = TotalPiecesMatched;
            value.skillsUsed = skillsUsed;
            value.bigHitCoins = bigHitCoins;

            OnBattleEnded?.Invoke(value);
        }

        // 이번 판에 플레이어가 쓴 스킬 수. 판마다 0에서 시작한다(이 컴포넌트가 판 하나를 산다).
        private int skillsUsed;

        // 러시 타임을 코루틴에서 기다리기 위한 표시. 이벤트를 그대로 기다릴 수는 없다.
        private bool rushFinished;
        private int rushGoldEarned;

        private void HandleRushFinished(int gold)
        {
            rushGoldEarned = gold;
            rushFinished = true;
        }
    }
}
