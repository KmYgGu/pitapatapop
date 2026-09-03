using System.Collections;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 블랙잭 한 자리의 <b>진행</b>(2026-09-02 사용자 기획).
    /// 인디언 포커의 <see cref="MiniGameFlow"/> 와 같은 자리에 있고, 공통 부분(캐릭터 세우기·
    /// 인사·나가기)은 <see cref="MiniGameSession"/> 이 이미 해둔다.
    ///
    /// <b>돈이 오가는 자리는 여기 하나뿐이다</b> - 규칙 클래스는 지갑을 모르고 화면은 숫자만
    /// 보여준다(포커와 같은 방침).
    /// </summary>
    public class BlackjackFlow : MonoBehaviour
    {
        [Header("이어붙일 것들")]
        [SerializeField] private BlackjackTableUI table;

        [Tooltip("테이블 위에 카드를 놓는 3D 판. 비워두면 숫자만 보인다.")]
        [SerializeField] private BlackjackCardBoard board;

        [Tooltip("대사창. 없으면 대사 없이 게임만 돈다.")]
        [SerializeField] private SpeechDirector speech;

        [Header("규칙")]
        [Tooltip("한 판에 걸 수 있는 최소 금액이자 조절 단위.")]
        [Min(1)]
        [SerializeField] private long betStep = 10L;

        [Tooltip("한 번에 걸 수 있는 최대 배수(단위 대비). <b>상대가 낼 수 있는 만큼</b>으로도 잘린다.")]
        [Min(1)]
        [SerializeField] private int maxBetMultiplier = 10;

        [Header("연출 간격(초)")]
        [Tooltip("빈털터리 대사를 마치고 자리를 뜨기까지 두는 사이(초). " +
                 "0이면 말이 끝나자마자 나간다.")]
        [Min(0f)]
        [SerializeField] private float brokeExitDelay = 0.6f;

        [SerializeField] private float dealDelay = 0.45f;
        [SerializeField] private float drawDelay = 0.75f;
        [SerializeField] private float showdownDelay = 1.1f;

        [Tooltip("플레이어가 멈춘 뒤 딜러가 뒷패를 까고 한 박자 쉬는 시간(초).")]
        [SerializeField] private float revealDelay = 0.6f;

        private Blackjack game;
        private System.Random rng;
        private PanelType opponent;
        private CharacterPersonality personality;
        private Coroutine running;

        /// <summary>
        /// 이 게임을 그만하겠다는 알림. 어디로 갈지는 <see cref="MiniGameSession"/> 이 안다.
        /// </summary>
        public event System.Action OnQuitRequested;
        /// <summary>
        /// ⭐ <b>방까지 아주 나가겠다</b>는 알림(판돈이 바닥났을 때).
        /// <see cref="OnQuitRequested"/> 는 한 칸만 물러나 게임 목록으로 가지만,
        /// 더 걸 돈이 없으면 목록에 남겨둬도 할 수 있는 게 없다.
        /// </summary>
        public event System.Action OnRoomExitRequested;

        private void Awake()
        {
            opponent = MiniGameEntry.Character;
            personality = opponent != null ? opponent.personality : null;

            rng = new System.Random();
            game = new Blackjack(rng);

            if (table != null)
            {
                table.OnBetConfirmed += HandleBet;
                table.OnHitPressed += HandleHit;
                table.OnStandPressed += HandleStand;
            }
        }

        private void OnDestroy()
        {
            if (table == null)
                return;

            table.OnBetConfirmed -= HandleBet;
            table.OnHitPressed -= HandleHit;
            table.OnStandPressed -= HandleStand;
        }

        /// <summary>이 게임을 시작한다. 판만 연다 - 인사는 세션이 이미 했다.</summary>
        public void Begin()
        {
            game.Reset();
            RefreshMoney();
            table?.BindCharacter(opponent);
            table?.ShowIdle(betStep, MaxBet());
            board?.Clear();
        }

        /// <summary>판을 접는다.</summary>
        public void Stop()
        {
            board?.Clear();

            if (running != null)
            {
                StopCoroutine(running);
                running = null;
            }

            game.Reset();
        }

        // ---------------------------------------------------------------- 한 판

        private void HandleBet(long amount)
        {
            if (running != null || game.Phase != BlackjackPhase.Idle)
                return;

            running = StartCoroutine(RoundRoutine(amount));
        }

        private IEnumerator RoundRoutine(long amount)
        {
            long bet = Mathf.Max(0, (int)Mathf.Min(amount, MaxBet()));

            if (bet <= 0L || !TakeBet(bet))
            {
                table?.ShowNotice("걸 수 있는 돈이 모자랍니다.");
                running = null;
                yield break;
            }

            game.Deal(bet);
            RefreshMoney();

            ShowTable(bet);
            yield return new WaitForSeconds(dealDelay);

            // ⭐ 딜러 규칙이라 <b>플레이어가 먼저</b> 끝낸다. 캐릭터의 둘째 장은 아직 덮여 있다.
            table?.ShowPlayerTurn(game.OpponentVisibleTotal);

            running = null;
        }

        /// <summary>캐릭터가 성향대로 뽑는다. 한 장 받을 때마다 말할 자리를 준다.</summary>
        private IEnumerator OpponentTurnRoutine()
        {
            while (game.Phase == BlackjackPhase.OpponentTurn)
            {
                // 플레이어는 이미 멈췄다 - 몇을 이겨야 하는지 알려주고 뽑게 한다.
                if (!BlackjackAI.ShouldHit(personality, rng, game.OpponentTotal, game.PlayerTotal))
                {
                    game.OpponentStands();

                    // ⭐ <b>멈추는 순간 이미 이긴 판이면 "그만 받겠다"는 말은 건너뛴다</b>
                    // (2026-09-02 사용자 지적). 이겼다는 말이 곧바로 이어지는데 앞에 한 마디를
                    // 더 끼우면 템포만 늘어진다.
                    if (game.Outcome != BlackjackOutcome.OpponentWin)
                        yield return Speak(SpeechTrigger.BlackjackStand);

                    break;
                }

                yield return Speak(SpeechTrigger.BlackjackHit);
                game.OpponentHits();

                ShowTable(game.Bet);
                yield return new WaitForSeconds(drawDelay);

                if (game.OpponentBusted)
                {
                    yield return Speak(SpeechTrigger.BlackjackBust);
                    break;
                }

                if (game.OpponentTotal == Blackjack.Target)
                {
                    game.OpponentStands();
                    yield return Speak(SpeechTrigger.BlackjackPerfect);
                    break;
                }

            }
        }

        private void HandleHit()
        {
            if (running != null || game.Phase != BlackjackPhase.PlayerTurn)
                return;

            running = StartCoroutine(HitRoutine());
        }

        private IEnumerator HitRoutine()
        {
            game.PlayerHits();
            ShowTable(game.Bet);

            yield return new WaitForSeconds(drawDelay);

            if (game.Phase == BlackjackPhase.Showdown)
            {
                // 넘겼다 - 딜러는 뽑지도 않고 이긴다.
                yield return FinishRoutine();
                yield break;
            }

            table?.ShowPlayerTurn(game.OpponentVisibleTotal);
            running = null;
        }

        private void HandleStand()
        {
            if (running != null || game.Phase != BlackjackPhase.PlayerTurn)
                return;

            running = StartCoroutine(StandRoutine());
        }

        private IEnumerator StandRoutine()
        {
            game.PlayerStands();

            // 뒷패를 깐다 - 여기서부터 캐릭터의 수가 보인다.
            ShowTable(game.Bet);
            yield return new WaitForSeconds(revealDelay);

            yield return OpponentTurnRoutine();
            yield return FinishRoutine();
        }

        private IEnumerator FinishRoutine()
        {
            table?.ShowShowdown(game.OpponentTotal, game.PlayerTotal, game.Outcome, game.Bet);

            yield return new WaitForSeconds(showdownDelay);

            SettleMoney();
            RefreshMoney();

            if (game.Outcome == BlackjackOutcome.PlayerWin)
                yield return SpeakOr(SpeechTrigger.BlackjackLose, SpeechTrigger.PokerLose);
            else if (game.Outcome == BlackjackOutcome.OpponentWin)
                yield return SpeakOr(SpeechTrigger.BlackjackWin, SpeechTrigger.PokerWin);

            game.Reset();

            board?.Clear();

            if (PlayerProfile.Gold < betStep || CharacterWallet.Get(opponent) < betStep)
            {
                yield return Speak(SpeechTrigger.PokerBroke);
                table?.ShowBroke();

                // ⭐ 말만 남기고 멈춰 있지 않는다 - 판돈이 없으면 더 할 게 없으니 자리를 뜬다.
                yield return new WaitForSeconds(brokeExitDelay);

                running = null;
                OnRoomExitRequested?.Invoke();
                yield break;
            }

            table?.ShowIdle(betStep, MaxBet());
            running = null;
        }

        // ---------------------------------------------------------------- 돈

        /// <summary>양쪽에서 같은 금액을 걷는다. 한쪽이라도 못 내면 아무것도 걷지 않는다.</summary>
        private bool TakeBet(long bet)
        {
            if (PlayerProfile.Gold < bet || CharacterWallet.Get(opponent) < bet)
                return false;

            PlayerProfile.Gold -= bet;
            CharacterWallet.TrySpend(opponent, bet);
            return true;
        }

        /// <summary>
        /// 판에 쌓인 걸 나눠준다. <b>양쪽이 낸 돈은 이미 지갑에서 빠져 있다.</b>
        /// 비기면 각자 낸 만큼 돌려받는다.
        /// </summary>
        private void SettleMoney()
        {
            long pot = game.Bet * 2L;

            switch (game.Outcome)
            {
                case BlackjackOutcome.PlayerWin:
                    PlayerProfile.Gold += pot;
                    break;

                case BlackjackOutcome.OpponentWin:
                    CharacterWallet.Add(opponent, pot);
                    break;

                default:
                    PlayerProfile.Gold += game.Bet;
                    CharacterWallet.Add(opponent, game.Bet);
                    break;
            }
        }

        /// <summary>
        /// 이번에 걸 수 있는 최대. <b>상대가 낼 수 있는 만큼으로도 자른다</b> -
        /// 상대가 못 받는 금액은 판이 성립하지 않는다(포커와 같은 규칙).
        /// </summary>
        private long MaxBet()
        {
            long byRule = betStep * maxBetMultiplier;
            long limit = Mathf.Min((int)Mathf.Min(byRule, PlayerProfile.Gold),
                                   (int)CharacterWallet.Get(opponent));
            return limit < 0L ? 0L : limit;
        }

        /// <summary>카드는 테이블 위 3D 판이, 숫자는 UI 가 맡는다.</summary>
        private void ShowTable(long bet)
        {
            // 딜러의 둘째 장은 플레이어가 끝낼 때까지 덮여 있다.
            bool hide = game.OpponentHoleHidden;

            board?.ShowHands(game.OpponentHand, game.PlayerHand, hide ? 1 : -1);
            table?.ShowTotals(hide ? game.OpponentVisibleTotal : game.OpponentTotal,
                              game.PlayerTotal, bet, hide);
        }

        private void RefreshMoney()
            => table?.ShowMoney(PlayerProfile.Gold, CharacterWallet.Get(opponent));

        private IEnumerator Speak(SpeechTrigger trigger)
        {
            if (speech == null || opponent == null)
                yield break;

            yield return speech.Play(opponent, trigger, SpeechSide.Enemy);
        }

        /// <summary>
        /// 블랙잭 전용 대사가 <b>아직 안 적혀 있으면</b> 포커 대사로 대신한다
        /// (2026-09-02 사용자 결정). 판이 끝나는 순간에 아무 말도 안 하는 게 제일 허전하다.
        ///
        /// ⭐ 시트에 <c>bj_win</c>·<c>bj_lose</c> 를 채워 넣으면 <b>코드를 고치지 않아도</b>
        /// 그쪽이 자동으로 우선한다. 쿨다운이 아니라 <b>있느냐 없느냐</b>만 본다 -
        /// "방금 말해서 쉬는 중"인데 포커 대사로 갈아타면 안 되니까.
        /// </summary>
        private IEnumerator SpeakOr(SpeechTrigger preferred, SpeechTrigger fallback)
        {
            bool has = opponent != null && opponent.speech != null
                       && opponent.speech.Has(preferred);

            yield return Speak(has ? preferred : fallback);
        }

        /// <summary>화면에서 그만두기를 눌렀을 때 - 세션이 어디로 갈지 정한다.</summary>
        public void Quit() => OnQuitRequested?.Invoke();
    }
}
