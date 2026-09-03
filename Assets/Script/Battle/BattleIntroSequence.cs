using System.Collections;
using UnityEngine;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using JojoPuzzle.View;

namespace JojoPuzzle.Battle
{
    /// <summary>
    /// 배틀이 시작되기 전의 순서를 <b>코루틴 하나가 통째로 소유한다</b>
    /// (<see cref="UI.BattleResultPanel"/>·<c>SkillPresentation</c> 과 같은 방침).
    ///
    /// <code>
    ///   1. 암막이 걷힌다        - 준비 화면이 덮고 넘어온 그 암막
    ///   2. 캐릭터가 뛰어 들어온다 (아군은 왼쪽에서 / 적은 오른쪽에서, 발밑에 먼지)
    ///      + '스킬 즉시' 아이템을 샀으면 여기서 게이지가 차오른다
    ///   3. 참가하는 무작위 조각 넷 - 조각 아이콘 + 전투력
    ///   4. 보스전이면 보스 대사   (보스가 아니면 건너뛴다)
    ///   5. '준비~' -> '시작!' -> 시계가 돌기 시작한다
    /// </code>
    ///
    /// <b>규칙은 하나도 여기 없다.</b> 판은 <see cref="GameEntryPoint"/> 가 이미 다 만들어놨고
    /// <see cref="BattleManager.BeginBattle"/> 도 이미 지나갔다 - 이 연출이 하는 일은
    /// <b>시계를 언제 굴릴지</b>를 늦추는 것뿐이다(<see cref="BattleManager.StartBattleTimer"/>).
    ///
    /// <b>⚠ 그동안 조작을 막아야 한다.</b> 배틀은 이미 '진행 중'이라 막지 않으면 연출을 보는
    /// 동안 조각을 옮겨 공짜 데미지를 넣을 수 있다 - 단계를
    /// <see cref="Core.BattlePhase.Intro"/> 로 두는 것이 그 역할이다.
    /// </summary>
    public class BattleIntroSequence : MonoBehaviour
    {
        [Header("배틀")]
        [SerializeField] private BattleManager battleManager;
        [SerializeField] private BoardInputController inputController;
        [SerializeField] private BoardView boardView;

        [Header("1. 암막")]
        [Tooltip("준비 화면에서 덮고 넘어온 암막을 여기서 걷는다. 비워두면 그냥 시작한다.")]
        [SerializeField] private ScreenFadeUI fade;

        [SerializeField] private float fadeInDuration = 0.35f;

        [Header("2. 뛰어 들어오기")]
        [Tooltip("아군 초상화들. <b>왼쪽 밖에서</b> 들어온다 - 배틀에서 아군이 서는 쪽이다.")]
        [SerializeField] private RunAcrossUI allyRun;

        [Tooltip("적 초상화. <b>오른쪽 밖에서</b> 들어온다.")]
        [SerializeField] private RunAcrossUI enemyRun;

        [Tooltip("아군이 다 들어온 뒤 적이 들어오기까지 기다리는 시간(초). " +
                 "0이면 둘이 동시에 들어온다.")]
        [SerializeField] private float enemyEnterDelay = 0.12f;

        [Header("3. 참가 조각")]
        [SerializeField] private BattlePieceIntroPanel piecePanel;

        [Header("4. 보스 대사")]
        [Tooltip("배틀의 대사창을 그대로 쓴다(새로 만들지 않는다).")]
        [SerializeField] private SpeechBubbleUI speechBubble;

        [Tooltip("보스 대사를 읽을 시간(초). 글자 수에 비례해 늘어난다.")]
        [SerializeField] private float bossSpeechBaseSeconds = 1.2f;
        [SerializeField] private float bossSpeechSecondsPerChar = 0.08f;

        [Tooltip("보스일 때 적 초상화에 불꽃을 켠다. 비워두면 씬 인스펙터 값 그대로 둔다.")]
        [SerializeField] private BattleFlameController flameController;

        [Header("5. 준비 - 시작")]
        [Tooltip("'준비~'와 '시작!'을 띄울 알림 띠. 타임오버 띠와 같은 컴포넌트를 써도 된다 - " +
                 "둘이 같이 뜨는 일은 없다.")]
        [SerializeField] private NoticeBannerUI banner;

        [SerializeField] private string readyMessage = "준비~";
        [SerializeField] private string startMessage = "시작!";

        /// <summary>연출이 다 끝나고 실제로 판이 시작됐을 때.</summary>
        public event System.Action OnFinished;

        /// <summary>
        /// 지금 시작 연출 중인지. 배틀 씬을 직접 열어 테스트할 때처럼 연출을 안 거치는 길도 있어서,
        /// 밖에서 물어볼 수 있게 열어둔다.
        /// </summary>
        public bool IsPlaying { get; private set; }

        // 대사를 고를 때 쓰는 난수. 같은 상황에 여러 줄이 있으면 그중 하나가 뽑힌다.
        private readonly System.Random rng = new System.Random();

        /// <summary>
        /// <b>캐릭터를 화면 밖에 세워둔다.</b> <see cref="Play"/> 보다 <b>먼저</b>, 그리고 화면이
        /// 한 번이라도 그려지기 전에 불러야 한다 - 안 그러면 제자리에 서 있다가 밖으로 튀었다
        /// 돌아오는 게 한 프레임 보인다.
        ///
        /// <b>Awake 가 아니라 Start 에서 부른다</b>: 같은 RectTransform 을 쓰는
        /// <c>HitFlinchUI</c>·<c>StartleHopUI</c> 가 Awake 에서 제자리를 기억하는데, 그보다 먼저
        /// 움직여버리면 <b>화면 밖을 제자리로</b> 외운다.
        /// </summary>
        public void PrepareOffscreen()
        {
            fade?.CoverInstantly();

            allyRun?.SnapOffscreen(-1f);
            enemyRun?.SnapOffscreen(1f);

            if (inputController != null)
                inputController.EnterPhase(BattlePhase.Intro);
        }

        public IEnumerator Play()
        {
            IsPlaying = true;

            // PrepareOffscreen 에서 이미 Intro 로 옮겼지만, 그걸 안 거치고 곧바로 부르는 길도
            // 있을 수 있어 여기서 한 번 더 확인한다(같은 단계로 두 번 들어가도 안전하다).
            if (inputController != null)
                inputController.EnterPhase(BattlePhase.Intro);

            // ── 1. 암막을 걷는다 ─────────────────────────────────────────
            if (fade != null)
                yield return StartCoroutine(fade.FadeIn(fadeInDuration));

            // ── 2. 뛰어 들어온다 ─────────────────────────────────────────
            // 아군을 먼저 띄우고 적은 살짝 늦게 - 동시에 들어오면 어느 쪽을 봐야 할지 모른다.
            Coroutine ally = allyRun != null ? StartCoroutine(allyRun.RunIn(-1f)) : null;

            if (enemyEnterDelay > 0f)
                yield return new WaitForSeconds(enemyEnterDelay);

            Coroutine enemy = enemyRun != null ? StartCoroutine(enemyRun.RunIn(1f)) : null;

            if (ally != null)
                yield return ally;
            if (enemy != null)
                yield return enemy;

            // '스킬 즉시' 아이템을 샀으면 <b>여기서</b> 게이지가 찬다(사용자 지시).
            // BeginBattle 에서 채우면 아직 캐릭터가 화면 밖일 때 연출이 지나가 버린다.
            battleManager?.PlayIntroSkillFull();

            // ── 3. 참가하는 무작위 조각 ──────────────────────────────────
            if (piecePanel != null)
                yield return StartCoroutine(piecePanel.Play(boardView));

            // ── 4. 보스 대사 ─────────────────────────────────────────────
            yield return StartCoroutine(PlayBossSpeech());

            // ── 5. 준비 - 시작 ───────────────────────────────────────────
            if (banner != null)
            {
                yield return StartCoroutine(banner.Play(readyMessage));
                yield return StartCoroutine(banner.Play(startMessage));
            }

            // 여기서 비로소 시계가 돈다. 조작도 이때 열린다 - 순서가 반대면 시계가 도는데
            // 아직 못 만지는 구간이 생긴다.
            if (inputController != null)
                inputController.EnterPhase(BattlePhase.Playing);

            battleManager?.StartBattleTimer();

            IsPlaying = false;
            OnFinished?.Invoke();
        }

        /// <summary>
        /// 보스전이면 적의 <see cref="SpeechTrigger.BossAppear"/> 줄을 띄운다.
        /// <b>보스가 아니면 아무 일도 하지 않는다</b>(사용자 지시로 건너뛴다).
        /// 보스인데 그 대사가 없어도 조용히 넘어간다 - 대사가 없는 상황은 원래 그렇게 다룬다.
        /// </summary>
        private IEnumerator PlayBossSpeech()
        {
            var stage = StageEntry.Stage;
            bool isBoss = stage != null && stage.isBoss;

            // 불꽃도 여기서 켠다 - "보스인지"를 아는 곳이 여기라, 씬 인스펙터에 손으로 적어두던
            // 값(BattleFlameController.enemyIsBoss)을 스테이지 데이터로 대신한다.
            if (flameController != null)
                flameController.SetEnemyIsBoss(isBoss);

            if (!isBoss || speechBubble == null)
                yield break;

            var enemy = stage.enemy;
            if (enemy == null || enemy.speech == null)
                yield break;

            if (!enemy.speech.TryPick(SpeechTrigger.BossAppear, rng, null, out var line)
                || string.IsNullOrEmpty(line.message))
                yield break;

            // 읽는 시간은 <b>글자 수에 비례</b>한다 - 고정으로 두면 짧은 대사는 늘어지고 긴 대사는
            // 잘린다(결과 화면의 대사 넘기기와 같은 규칙). 대사집이 자기 시간을 적어뒀으면 그게 이긴다.
            float hold = line.holdSeconds > 0f
                ? line.holdSeconds
                : bossSpeechBaseSeconds + bossSpeechSecondsPerChar * line.message.Length;

            var portrait = enemy.speech.portrait != null ? enemy.speech.portrait : enemy.icon;

            speechBubble.Show(SpeechSide.Enemy, portrait, enemy.speech.spine,
                enemy.speech.talkAnimation, line.message, hold);

            yield return new WaitForSeconds(hold);
        }
    }
}
