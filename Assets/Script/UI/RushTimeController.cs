using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.Core;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// <b>러시 타임</b> - 적을 쓰러뜨렸을 때 시간을 많이 남겼으면 주어지는 보너스 구간.
    ///
    /// <code>
    ///   1. 안내 띠가 왼쪽에서 오른쪽으로 흘러간다 (그동안 화면 암전 + 조작 불가)
    ///   2. 러시 타임 - 리필이 빨라지고, 지운 조각이 <b>그 자리에서</b> 점수와 골드가 된다
    ///   3. 끝나면 번 골드를 알린다 -> 승리 연출로 이어진다
    /// </code>
    ///
    /// <b>순서를 코루틴 하나가 통째로 소유한다</b>(SkillPresentation·BattleResultPanel 과 같은 방침).
    ///
    /// <b>이 구간에는 적 체력이 이미 0이다.</b> BattleManager 는 이미 승리를 확정했고
    /// <c>IsBattleRunning</c> 이 false 라서 데미지가 적에게 흘러도 무시된다. 그래서 여기서
    /// 따로 막을 게 없고, 점수는 <see cref="ScoreUI"/> 가 <c>OnMatchDamage</c> 를 직접 구독하고
    /// 있으므로 평소처럼 그대로 쌓인다. <b>골드만 이 클래스가 센다.</b>
    ///
    /// 스킬도 저절로 잠긴다(<c>BattleManager.CanActivateSkill</c> 이 IsBattleRunning 을 본다).
    /// 스탠드업은 <c>BoardInputController.SetRushTime</c> 이 게이지를 잠가서 막는다.
    /// </summary>
    public class RushTimeController : MonoBehaviour
    {
        [Header("씬 참조")]
        [SerializeField] private BoardInputController inputController;

        [Tooltip("안내 띠. 비워두면 안내 없이 곧바로 러시가 시작된다.")]
        [SerializeField] private RushTimeBannerUI banner;

        [Tooltip("안내 띠가 흐르는 동안 조작을 막는 데 쓴다.")]
        [SerializeField] private ScreenDimOverlay screenDimOverlay;

        [Tooltip("이번 판에 번 골드를 보여줄 HUD 배지. 러시 중에 실시간으로 오른다.")]
        [SerializeField] private GoldUI goldUI;

        [Tooltip("제한시간 시계. 러시가 시작될 때 <b>러시 길이로 다시 굴린다</b> - 그래야 " +
                 "남은 시간이 눈에 보이고 초읽기 연출도 그대로 따라온다. " +
                 "비워두면 시계가 배틀 종료 상태 그대로 멈춰 있다.")]
        [SerializeField] private BattleHUDController hud;

        [Tooltip("러시 타임을 건너뛰는 버튼(2026-08-25 사용자 요청). 러시가 도는 동안에만 " +
                 "보인다. 비워두면 건너뛸 수 없다.")]
        [SerializeField] private Button skipButton;

        [Tooltip("매치될 때마다 일그러질 적 캐릭터. 적 초상화의 <b>SpineChar 자식</b>에 붙어 있어야 " +
                 "한다 - 초상화 본체는 HitFlinchUI 와 EnemyDefeatAnimator 가 이미 쓰고 있다.")]
        [SerializeField] private SpineDistortUI enemyDistort;

        [Header("길이")]
        [Tooltip("클리어 시 남은 시간의 몇 배를 러시 타임으로 줄지. " +
                 "0.5면 24초 남기고 이겼을 때 12초. 화면에는 정수로 나간다.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float durationFraction = 0.5f;

        [Tooltip("러시 타임 길이의 하한(초). 조건을 겨우 넘겨 들어왔는데 너무 짧으면 허무하다.")]
        [SerializeField] private int minSeconds = 5;

        [Tooltip("러시 타임 길이의 상한(초).")]
        [SerializeField] private int maxSeconds = 30;

        [Header("진행")]
        [Tooltip("러시 중 낙하·리필 속도 배율. 2면 두 배 빠르다.")]
        [SerializeField] private float fallSpeedMultiplier = 2.2f;

        [Tooltip("안내 띠가 다 흐른 뒤 실제로 시작하기까지의 뜸(초).")]
        [SerializeField] private float startDelay = 0.15f;

        [Tooltip("시간이 다 된 뒤 진행 중이던 매치가 마무리될 여유(초). " +
                 "0이면 접히던 조각이 그대로 굳은 채 승리 연출로 넘어간다.")]
        [SerializeField] private float tailDelay = 0.6f;

        [Tooltip("화면 암전이 켜지고 꺼지는 데 걸리는 시간(초).")]
        [SerializeField] private float dimFadeDuration = 0.15f;

        /// <summary>러시 타임이 진행 중인지(안내 띠 포함).</summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// <b>지금 지운 조각이 러시 골드로 세어지는지.</b> 골드 창은 러시 시계보다 <b>이르게</b>
        /// 열린다 - 띠가 흐르는 동안 채워지며 저절로 터진 조각도 플레이어가 벌어온 것으로
        /// 쳐준다(2026-08-28 사용자 지시). 판을 채우기 시작할 때 열려서 러시가 끝날 때 닫힌다.
        ///
        /// <b>⚠ 밑돌 골드와 짝이다.</b> <see cref="Battle.BattleManager"/> 가 이 값을 보고
        /// <c>TotalPiecesMatched</c> 를 건너뛴다 - 같은 조각이 밑돌과 러시 몫에 두 번 계산되면
        /// 안 되기 때문이다. 그래서 <b>창의 주인은 여기 하나</b>여야 한다. 예전엔 저쪽이
        /// <c>IsRushTimeActive</c> 를 따로 보고 있었는데, 그러면 경계를 한쪽만 옮겼을 때
        /// 조용히 어긋난다.
        /// </summary>
        public bool IsCountingGold { get; private set; }

        /// <summary>이번 러시에 번 골드.</summary>
        public int EarnedGold { get; private set; }

        /// <summary>러시가 완전히 끝났을 때 번 골드와 함께 발행.</summary>
        public event System.Action<int> OnRushFinished;

        // 러시 중에 지운 조각 수. 골드는 이 수에서 나온다.
        private int piecesCleared;

        // 건너뛰기를 눌렀는지. 카운트다운 루프가 이걸 보고 빠져나온다.
        private bool skipRequested;

        private Coroutine routine;

        /// <summary>
        /// 러시 타임을 시작한다. <b>들어갈 자격이 있는지는 부르는 쪽이 판단한다</b>
        /// (<see cref="GoldReward.IsRushTime"/>) - 여기서 또 보면 판정이 두 군데가 된다.
        /// </summary>
        /// <param name="remainingSeconds">클리어 시점에 남아 있던 제한시간(초).</param>
        /// <summary>
        /// 지금 도는 러시 타임을 <b>건너뛴다</b>. 그때까지 번 골드는 그대로 살아 있다 -
        /// 건너뛰는 건 남은 시간을 포기하는 것이지 벌어놓은 걸 버리는 게 아니다.
        /// </summary>
        public void Skip() => skipRequested = true;

        public void Begin(float remainingSeconds)
        {
            if (IsActive)
                return;

            if (routine != null)
                StopCoroutine(routine);

            routine = StartCoroutine(RushRoutine(ResolveSeconds(remainingSeconds)));
        }

        /// <summary>남은 시간에서 러시 길이를 정한다. 화면에 정수로 나가므로 여기서 정수로 만든다.</summary>
        public int ResolveSeconds(float remainingSeconds)
        {
            int seconds = Mathf.FloorToInt(Mathf.Max(0f, remainingSeconds) * durationFraction);
            return Mathf.Clamp(seconds, Mathf.Max(1, minSeconds), Mathf.Max(1, maxSeconds));
        }

        private IEnumerator RushRoutine(int seconds)
        {
            IsActive = true;
            EarnedGold = 0;
            piecesCleared = 0;
            skipRequested = false;

            if (skipButton != null)
            {
                skipButton.onClick.AddListener(Skip);
                skipButton.gameObject.SetActive(false); // 띠가 흐르는 동안은 아직 안 보인다
            }

            // ── 1. 안내 띠. 흐르는 동안은 손을 못 대게 막는다 ─────────────
            // <b>띠가 흐르는 동안 판을 채운다</b>(2026-08-28 사용자 지시). 마무리 처리가 상자와
            // 두 색을 통째로 비워둔 뒤라 여기 들어올 때 판이 휑하다. 띠를 다 읽은 뒤에 채우기
            // 시작하면 러시 시계가 도는 동안 조각이 떨어지기를 기다리게 된다.
            //
            // <b>골드 세기를 채우기보다 먼저 연다</b>(2026-08-28 사용자 지시) - 채워지다 저절로
            // 터지는 조각이 꽤 되는데, 그게 러시 골드에도 밑돌에도 안 잡혀 그냥 사라지고 있었다.
            // 시계는 아직 안 돌지만 그 조각들도 이 판에서 벌어온 것이라 러시 몫으로 쳐준다.
            if (inputController != null)
            {
                inputController.OnPiecesMatched += HandlePiecesMatched;
                IsCountingGold = true;
            }

            // 띠가 흐르는 동안 판을 채우기 시작한다. <b>기다리지는 않는다</b> - 아래 참고.
            //
            // ⭐ <b>채우기 전에 특수 블록을 먼저 걷어낸다</b>(2026-09-03 사용자 지시).
            // 러시는 평범한 매치에만 집중하는 구간이라 특수 블록을 남겨두지 않는데, 채운 뒤에
            // 걷어내면 그 자리가 다시 비어서 판이 한 번 출렁인다. 걷어낸 몫은 러시 골드로
            // 세어지는데, 바로 위에서 골드 세기를 이미 열어 뒀으므로 순서가 맞는다.
            if (inputController != null)
                StartCoroutine(inputController.ClearSpecialBlocksThenRefillRoutine());

            if (banner != null)
            {
                screenDimOverlay?.SetDim(true, dimFadeDuration);
                yield return banner.Play(seconds);
                screenDimOverlay?.SetDim(false, dimFadeDuration);
            }

            // <b>다 채워지기를 기다리지 않는다</b>(2026-08-28 사용자 신고). 예전엔 여기서
            // 기다렸는데, 그 리필이 만든 캐스케이드까지 전부 잦아들어야 끝나는 코루틴이라
            // <b>띠가 끝나고도 몇 초 뒤에야</b> 러시가 시작됐다 - 시계도 건너뛰기 버튼도
            // 그만큼 늦게 나타났다.
            //
            // 기다리지 않아도 판이 비지 않는다: 바로 아래 SetRushTime 이 <b>빈 칸이 없어질
            // 때까지</b> 다시 굴린다. 그 사이에 저절로 터지는 조각도 이미 러시 골드로
            // 세어지고 있다(IsCountingGold 를 리필보다 먼저 열어뒀다).

            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);

            // ── 2. 러시 타임 ──────────────────────────────────────────────
            // 시계를 러시 길이로 다시 굴린다. <b>암전이 걷힌 뒤라야 한다</b> - 암전이 켜져 있는
            // 동안은 BattleManager 가 시계를 멈춰두므로, 먼저 굴리면 띠가 흐르는 내내 멈췄다가
            // 뒤늦게 출발한다. 만료되면 OnTimeUp 이 발행되지만 그때는 이미 IsBattleOver 라
            // BattleManager.HandleTimeUp 이 그냥 돌아간다(패배로 새지 않는다).
            hud?.StartTimer(seconds);

            // 조각 세기는 <b>위에서 이미 열었다</b>(판을 채우기 시작할 때). 여기서는 러시 규칙만 켠다.
            if (inputController != null)
                inputController.SetRushTime(true, fallSpeedMultiplier);

            skipButton?.gameObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < seconds && !skipRequested)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (skipButton != null)
            {
                skipButton.gameObject.SetActive(false);
                skipButton.onClick.RemoveListener(Skip);
            }

            // ── 3. 마무리 ─────────────────────────────────────────────────
            // 조각 세기를 먼저 끊고 속도를 되돌린다. 꼬리 시간에 성립하는 매치는 화면에서는
            // 정리되지만 골드로는 안 쳐준다 - 시간이 끝난 뒤에 번 것이 되면 표시된 시간이 거짓말이 된다.
            if (inputController != null)
            {
                inputController.OnPiecesMatched -= HandlePiecesMatched;
                inputController.SetRushTime(false, 1f);
            }
            IsCountingGold = false;

            if (tailDelay > 0f)
                yield return new WaitForSeconds(tailDelay);

            enemyDistort?.Stop();

            IsActive = false;
            routine = null;

            OnRushFinished?.Invoke(EarnedGold);
        }

        private void HandlePiecesMatched(int count)
        {
            if (count <= 0)
                return;

            piecesCleared += count;

            // 골드는 <b>누적 조각 수에서 다시 계산한다</b> - 매치마다 따로 반올림하면
            // 조각당 단가가 소수일 때 합계가 어긋난다(영수증과 같은 이유).
            int gold = GoldReward.RushGoldFor(piecesCleared);
            int delta = gold - EarnedGold;
            EarnedGold = gold;

            if (delta > 0)
                goldUI?.AddGold(delta);

            enemyDistort?.Hit();
        }

        private void OnDisable()
        {
            // 도중에 꺼지면 러시 상태가 켜진 채로 굳는다 - 다음 판이 빠른 리필로 시작된다.
            if (!IsActive)
                return;

            if (inputController != null)
            {
                inputController.OnPiecesMatched -= HandlePiecesMatched;
                inputController.SetRushTime(false, 1f);
            }
            IsCountingGold = false;

            banner?.Cancel();

            if (skipButton != null)
            {
                skipButton.gameObject.SetActive(false);
                skipButton.onClick.RemoveListener(Skip);
            }

            IsActive = false;
            routine = null;
        }
    }
}
