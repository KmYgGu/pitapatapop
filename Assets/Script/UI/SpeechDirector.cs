using System.Collections;
using UnityEngine;
using JojoPuzzle.Core;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// "이 캐릭터가 이런 상황이다"를 받아서 <b>띄울지 말지, 무엇을, 언제까지</b>를 정하는 층.
    /// 게임 코드는 SpeechBubbleUI를 직접 부르지 않고 항상 여기를 거친다.
    ///
    /// <b>왜 이 층이 반드시 필요한가</b> - 이 게임에서 대사창은 연출이 아니라 게임 상태다.
    /// SpeechBubbleUI.OnShown이 BoardDimOverlay(BoardDimReason.Speech)를 켜고, 그러면
    /// BoardInputController.IsMatchResolveFrozen이 참이 되어 <b>매치 처리가 멈춘다</b>
    /// (판이 어두워지고 터치도 막히며, 진행 중이던 접기 연출은 즉시 취소되고 데이터만 확정된다.
    ///  낙하와 리필은 계속 돌지만 새로 성립한 매치의 처리는 대사가 끝날 때까지 밀린다).
    /// 그래서 아무 데서나 Show를 부르면 판이 얼어붙고, 대사가 겹치면 더 오래 얼어붙는다.
    ///
    /// 스킬 연출 순서(1. 대사 → 2. 캐릭터 애니메이션 → 3. 스킬 효과/보드 반영)에서 1단계가
    /// 이 컴포넌트다. 그래서 Play가 <b>코루틴</b>이다 - 호출부가 yield return으로 기다렸다가
    /// 다음 단계로 넘어갈 수 있어야 한다.
    /// </summary>
    public class SpeechDirector : MonoBehaviour
    {
        [SerializeField] private SpeechBubbleUI bubble;

        [Header("기본값")]
        [Tooltip("대사에 유지 시간이 지정되지 않았을 때(0 이하) 쓸 시간(초).")]
        [SerializeField] private float defaultHoldSeconds = 1.6f;

        [Tooltip("같은 캐릭터가 같은 상황의 대사를 다시 하기까지의 최소 간격(초). " +
                 "짧게 잡으면 큰 매치마다 같은 말을 반복해서 금방 질린다.")]
        [SerializeField] private float sameTriggerCooldown = 8f;

        [Tooltip("대사 중에 화면을 누르면 <b>남은 시간을 건너뛰고 바로 닫는다</b>. " +
                 "대사를 다 읽은 사람이 기다리지 않게 하려는 것이다(2026-09-02 사용자 요청).\n" +
                 "배틀처럼 대사가 짧고 판이 계속 굴러가는 곳은 꺼두는 게 낫다 - " +
                 "조각을 놓으려던 손가락이 대사를 지워버린다.")]
        [SerializeField] private bool skipByTouch;

        [Tooltip("뜨자마자 지워지지 않도록 최소한 이만큼은 띄워둔다(초). " +
                 "대사를 띄운 그 손가락이 같은 동작으로 건너뛰기까지 하는 걸 막는다.")]
        [Min(0f)]
        [SerializeField] private float minShowSeconds = 0.3f;

        [Tooltip("글자 하나당 더해줄 시간(초). <b>0 이면 안 더한다</b>(배틀처럼 빠른 판은 그대로). " +
                 "도박 미니게임처럼 대사를 읽고 판단해야 하는 화면에서 올린다. 0.09 면 40자가 3.6초.")]
        [Min(0f)]
        [SerializeField] private float secondsPerCharacter;

        [Tooltip("글자 수로 늘려도 이 시간을 넘기지 않는다(초). 0 이면 상한 없음.")]
        [Min(0f)]
        [SerializeField] private float maxHoldSeconds = 7f;

        [Tooltip("이미 대사창이 떠 있을 때 Play가 그게 끝나길 기다리는 최대 시간(초). " +
                 "안전장치다 - 누군가 유지 시간을 음수로 띄워두면 여기서 영원히 묶이므로.")]
        [SerializeField] private float maxWaitForBusy = 3f;

        /// <summary>지금 대사창이 떠 있는지.</summary>
        public bool IsBusy => bubble != null && bubble.IsShowing;

        // 대사 선택용 난수. Random.Range를 쓰지 않는 이유는 이 프로젝트의 다른 로직 클래스와
        // 같은 방식(System.Random 주입)으로 맞춰서, 나중에 리플레이/시드 고정이 필요해질 때
        // 대사만 따로 튀지 않게 하기 위함이다.
        private readonly System.Random rng = new System.Random();

        // (캐릭터, 상황)별로 마지막에 말한 시각 - 쿨다운 판정용.
        private readonly System.Collections.Generic.Dictionary<(PanelType, SpeechTrigger), float> lastSpokenTime
            = new System.Collections.Generic.Dictionary<(PanelType, SpeechTrigger), float>();

        // 직전에 실제로 띄운 대사와 그 우선순위. 같은 말 반복 방지 + 겹침 판정에 쓴다.
        private string lastMessage;
        private int currentPriority;

        /// <summary>
        /// 대사를 띄우고 <b>끝날 때까지 기다린다.</b> 스킬처럼 "대사 다음에 뭔가가 이어져야 하는"
        /// 자리에서 쓴다. 대사가 없으면 아무것도 하지 않고 즉시 끝나므로, 호출부는
        /// 대사 유무를 신경 쓰지 않고 항상 yield return 해도 된다.
        /// </summary>
        public IEnumerator Play(PanelType character, SpeechTrigger trigger, SpeechSide side = SpeechSide.Player)
        {
            if (!TryResolve(character, trigger, out var line, out var portrait))
                yield break;

            // 이미 떠 있으면 그게 끝나길 잠깐 기다린다. 겹쳐 띄우면 앞 대사를 읽지 못한 채
            // 갈아끼워지고, 판이 멈춘 시간만 길어진다.
            float waited = 0f;
            while (IsBusy && waited < maxWaitForBusy)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            float hold = ResolveHold(line);
            ShowNow(character, trigger, line, portrait, side, hold);

            // 유지 시간이 음수면 호출부가 Close()로 닫을 때까지 기다리게 되는데, 그동안 매치가
            // 통째로 멈추므로 여기서는 기다리지 않고 바로 넘긴다(닫는 책임은 호출부에 있다).
            if (hold < 0f)
                yield break;

            float shown = 0f;
            while (IsBusy)
            {
                shown += Time.deltaTime;

                // 다 읽었으면 눌러서 넘어간다. 뜬 직후의 짧은 사이에는 안 받는다 -
                // 대사를 띄운 그 손가락이 같은 동작으로 지워버리지 않도록.
                if (skipByTouch && shown >= minShowSeconds && Input.GetMouseButtonDown(0))
                {
                    bubble.Hide();
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// 기다리지 않는 대사. 감탄사처럼 "있으면 좋고 없어도 그만"인 자리에서 쓴다.
        /// 이미 더 중요한 대사가 떠 있으면 <b>그냥 버린다</b> - 큐에 쌓아두면 상황이 다 지난 뒤에
        /// 뒤늦게 튀어나오고, 그만큼 판이 더 오래 멈춘다.
        /// </summary>
        public bool TryReport(PanelType character, SpeechTrigger trigger, SpeechSide side = SpeechSide.Player)
        {
            if (!TryResolve(character, trigger, out var line, out var portrait))
                return false;

            if (IsBusy && line.priority <= currentPriority)
                return false;

            ShowNow(character, trigger, line, portrait, side, ResolveHold(line));
            return true;
        }

        /// <summary>유지 시간을 음수로 띄운 대사를 닫는다.</summary>
        public void Close()
        {
            if (bubble != null)
                bubble.Hide();
        }

        /// <summary>
        /// 이 상황에 띄울 대사가 있는지 확인하고 골라온다. 캐릭터/대사집이 없거나, 그 상황의
        /// 대사가 없거나, 아직 쿨다운이면 false - 그때는 대사창이 아예 안 뜨므로 판도 안 멈춘다.
        /// </summary>
        private bool TryResolve(PanelType character, SpeechTrigger trigger, out SpeechLine line, out Sprite portrait)
        {
            line = default;
            portrait = null;

            if (bubble == null || character == null || character.speech == null || trigger == SpeechTrigger.None)
                return false;

            if (lastSpokenTime.TryGetValue((character, trigger), out float last)
                && Time.time - last < sameTriggerCooldown)
            {
                return false;
            }

            if (!character.speech.TryPick(trigger, rng, lastMessage, out line))
                return false;

            portrait = character.speech.portrait != null ? character.speech.portrait : character.icon;
            return true;
        }

        /// <summary>
        /// 이 대사를 얼마나 띄워둘지. 줄에 적힌 값이 있으면 그걸 쓰고,
        /// 없으면 기본값 위에 <b>글자 수만큼 더 준다</b>.
        ///
        /// <b>길이를 보는 이유</b>(2026-09-02 사용자 신고): 도박 미니게임은 <b>상대의 말로
        /// 패를 유추하는</b> 게 놀이인데, 긴 대사가 짧은 대사와 같은 시간만 떠 있으면
        /// 끝까지 읽을 수가 없다. 배틀처럼 빠른 판은 <see cref="secondsPerCharacter"/> 를
        /// 0 으로 두면 예전과 같이 굴러간다.
        /// </summary>
        private float ResolveHold(SpeechLine line)
        {
            if (line.holdSeconds > 0f)
                return line.holdSeconds;

            if (secondsPerCharacter <= 0f || string.IsNullOrEmpty(line.message))
                return defaultHoldSeconds;

            float byLength = line.message.Length * secondsPerCharacter;
            float hold = defaultHoldSeconds > byLength ? defaultHoldSeconds : byLength;

            return maxHoldSeconds > 0f && hold > maxHoldSeconds ? maxHoldSeconds : hold;
        }

        private void ShowNow(PanelType character, SpeechTrigger trigger, SpeechLine line,
            Sprite portrait, SpeechSide side, float hold)
        {
            lastSpokenTime[(character, trigger)] = Time.time;
            lastMessage = line.message;
            currentPriority = line.priority;

            // 스파인이 있으면 정지 이미지 대신 애니메이션이 나간다(SpeechBubbleUI가 판단).
            bubble.Show(side, portrait, character.speech.spine, character.speech.talkAnimation,
                line.message, hold);
        }
    }
}
