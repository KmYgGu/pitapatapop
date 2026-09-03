using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.Battle;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// <b>패배 화면</b>. 마무리 처리까지 하고도 적을 눕히지 못했을 때 뜬다.
    ///
    /// <code>
    ///   "패배.." + 적 캐릭터 Spine + 적의 대사
    ///   -> 터치하면 아파트로
    /// </code>
    ///
    /// <b>대사는 적의 <see cref="SpeechTrigger.Defeat"/> 줄이다</b>(2026-08-28 사용자 정정).
    ///
    /// <b>⚠ 상황 이름은 "플레이어 기준"이다.</b> <c>Defeat</c> = "플레이어가 졌다"는 상황이고,
    /// 그 자리에서 입을 여는 건 이긴 쪽인 적이다 - 실제 대사집도 그렇게 쓰여 있다
    /// ("소녀에게 패배하셨군요?" / "내가 강한거니까 슬퍼하지마" - 둘 다 진 사람에게 건네는 말).
    /// 예전엔 "진 건 플레이어지 적이 아니니 적의 <c>Victory</c> 를 읽는다"고 해석했는데
    /// <b>그게 틀렸다</b>. 같은 이유로 승리 화면은 아군의 <c>Victory</c> 를 읽는다 - 그쪽은 맞다.
    ///
    /// 대사창은 <see cref="SpeechSide.Enemy"/> 로 (좌우가 뒤집혀) 띄운다. 창 자체는 스킬 연출과
    /// 같은 <see cref="SpeechBubbleUI"/> 다
    /// (2026-08-25 사용자 방침 - 대사가 나오는 자리는 이 창을 재사용한다).
    ///
    /// <b>적이 누구인지는 스테이지가 안다</b>(<see cref="StageDefinition.enemy"/>). 배틀 씬을
    /// 직접 열어 테스트하면 스테이지가 없으므로 인스펙터의 대체 적을 쓴다.
    /// </summary>
    public class BattleDefeatPanel : MonoBehaviour
    {
        [Header("화면")]
        [Tooltip("화면 전체. <b>평소엔 꺼져 있다.</b> 이 컴포넌트는 항상 켜져 있는 부모에 붙어야 한다.")]
        [SerializeField] private GameObject root;

        [SerializeField] private GameObject titleRoot;
        [SerializeField] private SquashPunch titlePunch;

        [Header("적")]
        [Tooltip("적 캐릭터가 설 자리. 런타임에 Spine 을 만든다.")]
        [SerializeField] private SpineCharacterView enemySpine;

        [Tooltip("Spine 애셋이 없는 적일 때 대신 띄울 아이콘.")]
        [SerializeField] private Image enemyIcon;

        [Tooltip("스테이지를 거치지 않고 배틀 씬을 직접 열었을 때 쓸 적. 없으면 자리가 빈다.")]
        [SerializeField] private PanelType fallbackEnemy;

        [Tooltip("적을 <b>2.win</b> 자세로 바꾼다 - 이긴 건 적이다. 비워두면 자세를 안 바꾼다.")]
        [SerializeField] private EnemyBattleAnimator enemyPose;

        [Header("대사창")]
        [Tooltip("배틀·결과 화면과 <b>같은 대사창 컴포넌트</b>. 이 화면 전용으로 하나 놓았다.")]
        [SerializeField] private SpeechBubbleUI bubble;

        [Header("타이밍")]
        [Tooltip("화면이 뜨고 '패배..' 가 튀어나오기까지(초).")]
        [SerializeField] private float titleDelay = 0.25f;

        [Tooltip("'패배..' 뒤 적이 말하기까지(초).")]
        [SerializeField] private float lineDelay = 0.55f;

        [Tooltip("대사가 뜬 직후 이만큼은 터치를 무시한다(초).")]
        [SerializeField] private float tapGraceSeconds = 0.35f;

        /// <summary>
        /// 플레이어가 넘기겠다고 터치한 순간 발행. <b>패배도 결과 처리를 거친다</b>
        /// (2026-08-27 사용자 지시) - 진 판도 시간을 쓴 판이라 골드·경험치가 나온다.
        /// 어디로 갈지는 <see cref="BattleResultFlow"/> 가 정한다.
        /// </summary>
        public event System.Action OnAdvanceRequested;

        /// <summary>이 화면이 떠 있는지.</summary>
        public bool IsShowing => root != null && root.activeSelf;

        // 대사를 고르는 난수. SpeechDirector 와 같은 이유로 System.Random 을 쓴다.
        private readonly System.Random rng = new System.Random();

        private Coroutine routine;

        private void Awake()
        {
            if (root != null)
                root.SetActive(false);
        }

        /// <summary>패배 화면을 띄운다. 흐름(<see cref="BattleResultFlow"/>)이 부른다.</summary>
        public void Show()
        {
            if (routine != null)
                StopCoroutine(routine);

            routine = StartCoroutine(ShowRoutine());
        }

        private IEnumerator ShowRoutine()
        {
            var enemy = StageEntry.Stage != null && StageEntry.Stage.enemy != null
                ? StageEntry.Stage.enemy
                : fallbackEnemy;

            // 글자와 대사창은 순서대로 나올 것이므로 켜기 전에 감춰둔다.
            if (titleRoot != null)
                titleRoot.SetActive(false);

            bubble?.Hide();

            if (root != null)
                root.SetActive(true);

            // <b>적은 화면을 켠 뒤에 세운다.</b> SpineCharacterView 는 칸의 실제 크기를 재서
            // 배율을 잡는데, 꺼져 있으면 rect 가 0이라 그 측정이 조용히 실패한다.
            yield return null;

            BindEnemy(enemy);

            if (titleDelay > 0f)
                yield return new WaitForSeconds(titleDelay);

            if (titleRoot != null)
                titleRoot.SetActive(true);

            titlePunch?.Play();

            if (lineDelay > 0f)
                yield return new WaitForSeconds(lineDelay);

            // 이긴 건 적이다 - 대사와 함께 승리 자세로 바꾼다.
            enemyPose?.PlayWin();
            ShowEnemyLine(enemy);

            yield return TapGate.Wait(0f, tapGraceSeconds);

            routine = null;
            OnAdvanceRequested?.Invoke();
        }

        private void BindEnemy(PanelType enemy)
        {
            var skeleton = enemy != null && enemy.speech != null ? enemy.speech.spine : null;

            if (enemySpine != null)
            {
                if (skeleton != null)
                    enemySpine.Show(skeleton);
                else
                    enemySpine.Clear();
            }

            if (enemyIcon != null)
            {
                // Spine 이 서 있으면 아이콘까지 겹쳐 보이면 안 된다.
                bool useIcon = skeleton == null && enemy != null && enemy.icon != null;
                enemyIcon.enabled = useIcon;
                if (useIcon)
                    enemyIcon.sprite = enemy.icon;
            }
        }

        /// <summary>
        /// 적의 <b>승리</b> 대사를 띄운다. 대사가 없으면 창을 아예 안 띄운다 -
        /// 빈 창이 떠 있으면 만들다 만 것처럼 보인다.
        /// </summary>
        private void ShowEnemyLine(PanelType enemy)
        {
            if (bubble == null || enemy == null || enemy.speech == null)
                return;

            if (!enemy.speech.TryPick(SpeechTrigger.Defeat, rng, null, out var line)
                || string.IsNullOrEmpty(line.message))
            {
                bubble.Hide();
                return;
            }

            var portrait = enemy.speech.portrait != null ? enemy.speech.portrait : enemy.icon;

            // 유지 시간을 음수로 = 직접 닫을 때까지. 이 화면은 계속 떠 있어야 한다.
            bubble.Show(SpeechSide.Enemy, portrait, enemy.speech.spine, enemy.speech.talkAnimation,
                line.message, -1f);
        }

        /// <summary>화면을 닫는다. 다음 화면으로 넘어갈 때 흐름이 부른다.</summary>
        public void Hide()
        {
            if (root != null)
                root.SetActive(false);
        }
    }
}
