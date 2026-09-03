using System.Collections;
using System.Text;
using UnityEngine;
using Spine.Unity;
using UnityEngine.UI;
using JojoPuzzle.Core;
using JojoPuzzle.View;
using JojoPuzzle.Battle;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 배틀이 끝난 뒤의 <b>승리 화면</b>. 지금은 승리만 만들어져 있다 - 패배는 아직 화면이 없어서
    /// 이 컴포넌트가 아무것도 하지 않고 넘긴다(<see cref="HandleBattleEnded"/> 참고).
    ///
    /// <b>순서를 코루틴 하나가 통째로 소유한다</b>(<see cref="SkillPresentation"/> 과 같은 방침).
    /// 적이 날아가는 것, 화면이 밝아지는 것, 대사가 차례로 뜨는 것을 각자 자기 컴포넌트에
    /// 흩어놓으면 순서가 어긋나고 "언제 다음으로 넘어가는지"를 아무도 모르게 된다.
    ///
    /// <code>
    ///   1. 적이 빙빙 돌며 하늘로 날아간다   (EnemyDefeatAnimator)
    ///   2. 화면이 어두워지며 결과판이 뜬다
    ///   3. 리더·파트너 클로즈업              (겹쳐 서고 리더가 앞 - 준비 화면과 같은 구도)
    ///   4. "승리!" 와 총 점수
    ///   5. 리더 승리 대사 → (다 읽을 때쯤 자동 / 터치하면 즉시) → 파트너 승리 대사
    ///   6. 터치해야 다음 화면으로 (OnAdvanceRequested)
    /// </code>
    ///
    /// <b>대사창은 스킬 연출과 같은 <see cref="SpeechBubbleUI"/> 다</b>(2026-08-25 사용자 지시 -
    /// 대사가 나오는 자리는 앞으로도 이 창을 재사용한다). 다만 배틀에 있는 그 하나를 빌려오는
    /// 게 아니라 <b>이 화면 전용으로 둘을 따로 놓았다</b>. 이유가 둘이다:
    ///  - 리더와 파트너가 <b>동시에</b> 떠 있어야 하는데 배틀 쪽은 창이 하나다.
    ///  - 배틀의 그 창은 떠 있는 동안 <see cref="BoardDimOverlay"/> 를 켜서 매치 처리를 멈추는
    ///    <b>게임 상태</b>다(<see cref="SpeechDirector"/> 주석 참고). 여기 둘은 그 오버레이에
    ///    연결돼 있지 않아서 판을 건드리지 않는다.
    /// <b>대사 내용은 같은 곳에서 온다</b> - 캐릭터의 <see cref="CharacterSpeechSet"/> 의
    /// <see cref="SpeechTrigger.Victory"/> 줄이다.
    /// </summary>
    public class BattleResultPanel : MonoBehaviour
    {
        [Header("씬 참조")]
        [SerializeField] private BattleManager battleManager;

        [Tooltip("편성 캐릭터를 슬롯 번호로 찾는 데 쓴다(팔레트 조회). 0=리더, 1=파트너.")]
        [SerializeField] private BoardView boardView;

        [Tooltip("총 점수를 가져올 곳. 비워두면 BattleOutcome 의 누적 데미지를 쓴다 - " +
                 "지금은 데미지가 그대로 점수라 값이 같지만, 보너스가 붙으면 갈린다.")]
        [SerializeField] private ScoreUI scoreUI;

        [Tooltip("적이 날아가는 연출. 비워두면 그 단계를 건너뛰고 곧바로 결과판이 뜬다.")]
        [SerializeField] private EnemyDefeatAnimator enemyDefeat;

        [Header("화면")]
        [Tooltip("결과 화면 전체. <b>평소엔 꺼져 있다.</b> 이 컴포넌트는 항상 켜져 있는 부모에 " +
                 "붙어야 한다 - 꺼진 오브젝트에 붙으면 이벤트를 구독하지 못한다.")]
        [SerializeField] private GameObject root;

        [Tooltip("뒤를 덮는 판. 알파를 0에서 원래 값까지 올리며 나타난다.")]
        [SerializeField] private Graphic backdrop;

        [SerializeField] private GameObject titleRoot;
        [SerializeField] private SquashPunch titlePunch;

        [SerializeField] private Text scoreText;

        [Tooltip("점수 앞에 붙일 문구. 배너 그림이 생기면 비우면 된다.")]
        [SerializeField] private string scorePrefix = "총 점수  ";

        [Header("승리 자세")]
        [Tooltip("대사가 나올 때 그 캐릭터를 <b>2.win</b> 자세로 바꾼다. 리더·파트너 순서. " +
                 "초상화 안 SpineChar 의 <b>SkeletonAnimation</b> 을 그대로 넣는다 - " +
                 "그 캐릭터에게 2.win 이 없으면 자기 idle 로 대신 재생된다(SpinePlayback).")]
        [SerializeField] private SkeletonAnimation[] allyPoses = new SkeletonAnimation[0];

        [Header("초상화 클로즈업")]
        [Tooltip("<b>배틀 화면에 서 있던</b> 아군 초상화들. 리더·파트너 순서. " +
                 "화면 한가운데로 끌어온 뒤 승리판이 뜬다(2026-08-25 사용자 지시) - " +
                 "따로 세운 캐릭터보다 '방금까지 싸우던 그 캐릭터'라는 게 분명하게 읽힌다. " +
                 "이걸 쓰면 아래 슬롯(leaderSpine/partnerSpine)은 비워두면 된다.")]
        [SerializeField] private PortraitCloseUpUI[] allyCloseUps = new PortraitCloseUpUI[0];

        [Header("캐릭터 클로즈업 - 슬롯 방식(대체)")]
        [SerializeField] private GameObject leaderRoot;
        [SerializeField] private SpineCharacterView leaderSpine;

        [Tooltip("Spine 애셋이 없는 캐릭터일 때 대신 띄울 아이콘 자리.")]
        [SerializeField] private Image leaderIcon;
        [SerializeField] private SquashPunch leaderPunch;

        [SerializeField] private GameObject partnerRoot;
        [SerializeField] private SpineCharacterView partnerSpine;
        [SerializeField] private Image partnerIcon;
        [SerializeField] private SquashPunch partnerPunch;

        [Header("대사창")]
        [Tooltip("배틀에서 쓰는 것과 <b>같은 대사창 컴포넌트</b>다. 대사가 나오는 자리는 앞으로도 " +
                 "이걸 재사용한다 - 다만 배틀의 그 하나를 빌려오는 게 아니라 이 화면 전용으로 " +
                 "둘을 따로 놓았다(둘이 동시에 떠 있어야 하므로).")]
        [SerializeField] private SpeechBubbleUI leaderBubble;

        [SerializeField] private SpeechBubbleUI partnerBubble;

        [Tooltip("<b>임시</b> - 대사집이 아직 없는 캐릭터가 쓸 대사집. 지금은 파트너(BB)에게 " +
                 "대사집이 없어서 이게 없으면 아래 대사창이 아예 안 뜬다. 캐릭터마다 대사집이 " +
                 "붙으면 비워둘 것.")]
        [SerializeField] private CharacterSpeechSet fallbackSpeech;

        [Header("타이밍")]
        [Tooltip("적이 날아간 뒤 결과판이 뜨기까지의 뜸(초). 곧바로 뜨면 숨 돌릴 틈이 없다.")]
        [SerializeField] private float afterEnemyDelay = 0.25f;

        [Tooltip("뒤 판이 밝아지는 시간(초).")]
        [SerializeField] private float backdropFadeDuration = 0.28f;

        [Tooltip("캐릭터가 뜬 뒤 '승리!' 가 튀어나오기까지(초).")]
        [SerializeField] private float titleDelay = 0.18f;

        [Tooltip("'승리!' 뒤 점수가 굴러가기 시작하기까지(초).")]
        [SerializeField] private float scoreDelay = 0.3f;

        [Tooltip("점수가 0부터 총점까지 굴러가는 시간(초).")]
        [SerializeField] private float scoreRollDuration = 0.8f;

        [Tooltip("점수가 다 굴러간 뒤 리더 대사가 뜨기까지(초).")]
        [SerializeField] private float leaderLineDelay = 0.2f;

        [Header("대사 넘기기")]
        [Tooltip("리더 대사를 다 읽었다고 볼 때까지의 <b>기본</b> 시간(초). " +
                 "여기에 글자 수만큼의 시간이 더해진다.")]
        [SerializeField] private float readBaseSeconds = 1.1f;

        [Tooltip("글자 하나마다 더할 읽는 시간(초). 긴 대사일수록 오래 머문다 - " +
                 "고정 시간으로 두면 짧은 대사는 늘어지고 긴 대사는 잘린다.")]
        [SerializeField] private float readSecondsPerChar = 0.12f;

        [Tooltip("대사가 뜬 직후 이만큼은 터치를 무시한다(초). " +
                 "없으면 한 번 누른 손가락이 두 단계를 통째로 넘겨버린다.")]
        [SerializeField] private float tapGraceSeconds = 0.25f;

        /// <summary>결과 화면이 떠 있는지.</summary>
        public bool IsShowing => root != null && root.activeSelf;

        // 대사를 고르는 난수. SpeechDirector 와 같은 이유로 System.Random 을 쓴다.
        private readonly System.Random rng = new System.Random();

        // 점수를 굴리는 동안 매 프레임 문자열을 새로 만들지 않도록 재사용한다(ScoreUI 와 같은 방식).
        private readonly StringBuilder builder = new StringBuilder(32);

        private Color backdropColor;
        private Coroutine routine;

        // 이번 화면에서 마지막으로 띄운 대사. 아래 대사창이 같은 말을 반복하지 않도록 넘긴다.
        private string lastShownLine;

        private void Awake()
        {
            if (backdrop != null)
                backdropColor = backdrop.color;

            // 씬에 켜둔 채로 저장돼 있어도 배틀 중에는 보이면 안 된다.
            if (root != null)
                root.SetActive(false);
        }

        private void OnEnable()
        {
            if (battleManager != null)
                battleManager.OnBattleEnded += HandleBattleEnded;
        }

        private void OnDisable()
        {
            if (battleManager != null)
                battleManager.OnBattleEnded -= HandleBattleEnded;
        }

        private void HandleBattleEnded(BattleOutcome outcome)
        {
            // 패배 화면은 아직 없다. 여기서 붙잡고 아무것도 안 하면 "끝났는데 화면이 검은 채로
            // 멈춘 것"처럼 보이므로, 만들 때까지는 손대지 않고 넘긴다.
            if (outcome.result != BattleResult.Victory)
                return;

            if (routine != null)
                StopCoroutine(routine);

            lastShownLine = null;
            IsWaitingForAdvance = false;
            routine = StartCoroutine(VictoryRoutine(outcome));
        }

        private IEnumerator VictoryRoutine(BattleOutcome outcome)
        {
            // ── 1. 적이 빙빙 돌며 날아간다 ────────────────────────────────
            if (enemyDefeat != null)
            {
                enemyDefeat.PlayDefeat();
                yield return new WaitForSeconds(enemyDefeat.TotalDuration + Mathf.Max(0f, afterEnemyDelay));
            }

            // ── 1-2. 아군 초상화를 화면 한가운데로 ────────────────────────
            // <b>결과판을 켜기 전에</b> 한다 - 판이 먼저 뜨면 초상화가 그 뒤로 숨는다.
            yield return PlayAllyCloseUps();

            // ── 2. 결과판 등장 ────────────────────────────────────────────
            // 글자와 대사창은 순서대로 튀어나올 것이므로 켜기 <b>전에</b> 전부 감춰둔다.
            // 켠 다음에 감추면 한 프레임 번쩍인다.
            if (titleRoot != null)
                titleRoot.SetActive(false);

            leaderBubble?.Hide();
            partnerBubble?.Hide();

            if (scoreText != null)
                scoreText.text = string.Empty;

            if (backdrop != null)
            {
                var start = backdropColor;
                start.a = 0f;
                backdrop.color = start;
            }

            if (root != null)
                root.SetActive(true);

            // <b>캐릭터는 화면을 켠 뒤에 세운다.</b> SpineCharacterView 는 칸의 실제 크기를 재서
            // 배율을 잡는데, 꺼져 있는 오브젝트는 rect 가 아직 0이라 그 측정이 조용히 실패한다.
            // 한 프레임 기다려 레이아웃이 잡히게 한 뒤에 세운다.
            yield return null;

            var leader = boardView != null ? boardView.GetCharacter(0) : null;
            var partner = boardView != null ? boardView.GetCharacter(1) : null;

            BindCharacter(leader, leaderRoot, leaderSpine, leaderIcon);
            BindCharacter(partner, partnerRoot, partnerSpine, partnerIcon);

            yield return FadeInBackdrop();

            // ── 3. 캐릭터가 말랑 등장 ─────────────────────────────────────
            if (leaderPunch != null)
                leaderPunch.Play();
            if (partnerPunch != null)
                partnerPunch.Play();

            // ── 4. "승리!" 와 총 점수 ─────────────────────────────────────
            if (titleDelay > 0f)
                yield return new WaitForSeconds(titleDelay);

            if (titleRoot != null)
                titleRoot.SetActive(true);
            if (titlePunch != null)
                titlePunch.Play();

            if (scoreDelay > 0f)
                yield return new WaitForSeconds(scoreDelay);

            yield return RollScore(ResolveTotalScore(outcome));

            // ── 5. 리더 대사 → (읽을 시간 or 터치) → 파트너 대사 → 터치 ──
            if (leaderLineDelay > 0f)
                yield return new WaitForSeconds(leaderLineDelay);

            // <b>대사가 나오는 순간 그 캐릭터만 2.win 으로 바꾼다</b>(2026-08-25 사용자 지시).
            // 그전까지는 1.idle 로 서 있다.
            PlayWinPose(0);
            string leaderLine = ShowLine(leader, leaderBubble);

            // 리더 대사를 다 읽을 때쯤 자동으로 넘어가되, 먼저 터치하면 곧바로 넘어간다.
            // 대사가 아예 없으면 기다릴 것도 없다.
            if (leaderLine != null)
                yield return WaitForTapOrSeconds(ReadSeconds(leaderLine));

            PlayWinPose(1);
            ShowLine(partner, partnerBubble);

            // 파트너 대사부터는 <b>자동으로 넘어가지 않는다</b> - 다음 화면으로 가는 건
            // 플레이어가 정한다(2026-08-25 사용자 지시).
            IsWaitingForAdvance = true;
            yield return WaitForTapOrSeconds(0f);
            IsWaitingForAdvance = false;

            // <b>확대를 풀어 원래 자리로 돌려보낸다.</b> 확대 중에는 초상화가 overrideSorting 으로
            // 맨 앞에 서 있어서, 그대로 두면 다음 화면(결과·캐릭터)들 위에 계속 얹혀 있다
            // (2026-08-25 실제로 그랬다). 원래 자리로 돌아가면 HudContent 안이라 결과 화면
            // 배경 아래로 들어가 저절로 가려진다.
            //
            // <b>이 화면 자체는 닫지 않는다</b> - 닫으면 다음 배경이 밝아지기 전에 배틀 화면이
            // 한 프레임 드러난다. 어차피 결과 화면 배경이 불투명하게 덮는다.
            ResetAllyCloseUps();

            routine = null;
            OnAdvanceRequested?.Invoke();
        }

        /// <summary>
        /// 파트너 대사까지 다 보고 <b>플레이어가 넘기겠다고 터치한</b> 순간 발행.
        ///
        /// <b>지금은 구독자가 없다</b> - 결과 화면 다음으로 갈 곳(보상 화면이든 스테이지 선택이든)이
        /// 아직 없어서, 여기서 화면이 그대로 멈춘다. 다음 화면이 생기면 이걸 구독하면 되고
        /// 이 컴포넌트는 어디로 가는지 몰라도 된다(StageSelectFlow 가 화면 이동을 혼자 아는 것과 같은 방침).
        /// </summary>
        public event System.Action OnAdvanceRequested;

        /// <summary>파트너 대사까지 다 뜨고 플레이어의 터치를 기다리는 중인지.</summary>
        public bool IsWaitingForAdvance { get; private set; }

        /// <summary>이 대사를 다 읽는 데 걸린다고 볼 시간(초).</summary>
        private float ReadSeconds(string message)
            => Mathf.Max(0f, readBaseSeconds) + Mathf.Max(0f, readSecondsPerChar) * message.Length;

        /// <summary>넘기기 규칙은 결과 화면들이 함께 쓰는 <see cref="TapGate"/> 에 있다.</summary>
        private IEnumerator WaitForTapOrSeconds(float autoAdvanceAfter)
            => TapGate.Wait(autoAdvanceAfter, tapGraceSeconds);

        /// <summary>
        /// 그 자리의 캐릭터를 승리 자세로. 연결이 없으면 아무 일도 하지 않는다.
        ///
        /// <b>⚠ 예전엔 <see cref="LeaderMecanimAnimator"/> 를 거쳤다</b>(2026-08-30에 고침).
        /// 그건 <b>리더 초상화에만 붙어 있는</b> 스탠드업용 다리라, 파트너 자리는 연결할 게 없어
        /// 비어 있었다 - 그래서 <b>파트너는 승리 대사를 하면서도 승리 동작을 안 했다</b>
        /// (사용자 신고: 라뷰린스가 파트너일 때). 재생기를 직접 들고 있으면 둘이 대칭이 된다.
        /// </summary>
        private void PlayWinPose(int index)
        {
            if (allyPoses == null || index < 0 || index >= allyPoses.Length)
                return;

            SpinePlayback.Play(allyPoses[index], SpinePlayback.Win, true);
        }

        /// <summary>확대를 풀어 초상화들을 배틀 화면의 제자리로 돌려보낸다.</summary>
        private void ResetAllyCloseUps()
        {
            if (allyCloseUps == null)
                return;

            for (int i = 0; i < allyCloseUps.Length; i++)
            {
                if (allyCloseUps[i] != null)
                    allyCloseUps[i].Reset();
            }
        }

        /// <summary>
        /// 아군 초상화 여럿을 <b>동시에</b> 끌어온다 - 차례로 하면 늘어진다.
        /// 전부 같은 시간이 걸리므로 마지막 하나만 기다리면 된다.
        /// </summary>
        private IEnumerator PlayAllyCloseUps()
        {
            if (allyCloseUps == null || allyCloseUps.Length == 0)
                yield break;

            // <b>무리의 한가운데를 먼저 구한다.</b> 그걸 기준으로 다 같이 밀려나야
            // 서로의 거리가 크기와 같은 배로 벌어진다 - 각자 화면 한가운데로 오라고 하면
            // 둘이 같은 자리로 몰려 겹치고, 좌우가 뒤바뀌기까지 한다(2026-08-25 실제로 그랬다).
            Vector2 groupCenter = Vector2.zero;
            int counted = 0;

            for (int i = 0; i < allyCloseUps.Length; i++)
            {
                if (allyCloseUps[i] == null)
                    continue;

                groupCenter += allyCloseUps[i].GetCanvasCenter();
                counted++;
            }

            if (counted == 0)
                yield break;

            groupCenter /= counted;

            PortraitCloseUpUI last = null;
            for (int i = 0; i < allyCloseUps.Length; i++)
            {
                if (allyCloseUps[i] == null)
                    continue;

                // 앞의 것들은 띄워만 두고, 마지막 하나만 기다린다(전부 같은 시간이 걸린다).
                if (last != null)
                    StartCoroutine(last.Play(groupCenter));

                last = allyCloseUps[i];
            }

            if (last != null)
                yield return last.Play(groupCenter);
        }

        /// <summary>
        /// 캐릭터를 클로즈업 자리에 세운다. Spine 이 있으면 그걸, 없으면 아이콘으로 물러선다 -
        /// 지금은 대사집이 붙은 캐릭터만 Spine 을 갖고 있어서 <b>물러서는 쪽이 기본에 가깝다</b>.
        /// </summary>
        private void BindCharacter(PanelType character, GameObject slotRoot,
            SpineCharacterView spine, Image icon)
        {
            if (slotRoot != null)
                slotRoot.SetActive(character != null);

            if (character == null)
            {
                if (spine != null)
                    spine.Clear();
                return;
            }

            var skeleton = character.speech != null ? character.speech.spine : null;

            if (spine != null)
            {
                if (skeleton != null)
                    spine.Show(skeleton);
                else
                    spine.Clear();
            }

            if (icon != null)
            {
                // Spine 이 서 있으면 아이콘까지 겹쳐 보이면 안 된다.
                bool useIcon = skeleton == null && character.icon != null;
                icon.enabled = useIcon;
                if (useIcon)
                    icon.sprite = character.icon;
            }
        }

        private IEnumerator FadeInBackdrop()
        {
            if (backdrop == null || backdropFadeDuration <= 0f)
            {
                if (backdrop != null)
                    backdrop.color = backdropColor;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < backdropFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / backdropFadeDuration);

                var c = backdropColor;
                c.a = backdropColor.a * t;
                backdrop.color = c;

                yield return null;
            }

            backdrop.color = backdropColor;
        }

        /// <summary>
        /// 총 점수를 <see cref="ScoreUI"/> 에서 가져온다. 없으면 이번 배틀의 누적 데미지로 물러선다.
        /// </summary>
        private int ResolveTotalScore(BattleOutcome outcome)
            => scoreUI != null ? scoreUI.CurrentScore : outcome.totalDamageDealt;

        /// <summary>0에서 총점까지 숫자를 굴린다. 감속(ease-out)이라 끝에서 천천히 멈춘다.</summary>
        private IEnumerator RollScore(int total)
        {
            if (scoreText == null)
                yield break;

            if (scoreRollDuration <= 0f || total <= 0)
            {
                ApplyScore(total);
                yield break;
            }

            float elapsed = 0f;
            int shown = -1;

            while (elapsed < scoreRollDuration)
            {
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / scoreRollDuration);
                float eased = 1f - (1f - t) * (1f - t);

                int next = (int)(total * eased);
                if (next != shown)
                {
                    shown = next;
                    ApplyScore(next);
                }

                yield return null;
            }

            ApplyScore(total);
        }

        private void ApplyScore(int value)
        {
            builder.Length = 0;
            builder.Append(scorePrefix);
            ScoreUI.AppendGrouped(builder, value);
            scoreText.text = builder.ToString();
        }

        /// <summary>
        /// 이 캐릭터의 승리 대사를 띄운다. 대사가 없으면 <b>대사창을 아예 띄우지 않는다</b> -
        /// 빈 창이 떠 있으면 만들다 만 것처럼 보인다.
        /// </summary>
        /// <returns>실제로 띄운 대사. 띄우지 않았으면 null - 호출부가 읽을 시간을 잴 때 쓴다.</returns>
        private string ShowLine(PanelType character, SpeechBubbleUI bubble)
        {
            if (bubble == null || character == null)
                return null;

            // <b>말과 얼굴을 따로 구한다.</b> 대사집이 없는 캐릭터는 말만 fallbackSpeech 에서
            // 빌려오고, 얼굴은 어디까지나 자기 것이라야 한다 - 안 그러면 위에 선 캐릭터와
            // 대사창 속 캐릭터가 서로 다른 사람이 된다.
            var lineSource = character.speech != null ? character.speech : fallbackSpeech;
            var face = character.speech;

            // 위 대사를 피할 대상으로 넘긴다 - 대사집이 없어 둘이 같은 대사집으로 물러섰을 때
            // 후보가 여럿이면 적어도 다른 줄이 뽑힌다(한 줄뿐이면 같은 말이 나올 수밖에 없다).
            if (lineSource == null
                || !lineSource.TryPick(SpeechTrigger.Victory, rng, lastShownLine, out var line)
                || string.IsNullOrEmpty(line.message))
            {
                bubble.Hide();
                return null;
            }

            lastShownLine = line.message;

            var portrait = face != null && face.portrait != null ? face.portrait : character.icon;
            var skeleton = face != null ? face.spine : null;
            string talk = face != null ? face.talkAnimation : null;

            // 유지 시간을 음수로 준다 = 직접 닫을 때까지. 결과 화면은 계속 떠 있어야 한다.
            // (배틀 중이라면 이렇게 띄우면 판이 멈추지만, 여기 대사창은 BoardDimOverlay 에
            //  연결돼 있지 않아서 판을 건드리지 않는다.)
            bubble.Show(SpeechSide.Player, portrait, skeleton, talk, line.message, -1f);
            return line.message;
        }
    }
}
