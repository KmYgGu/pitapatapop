using System.Collections;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 미니게임(인디언 포커) 한 자리의 <b>진행</b>. 씬에 하나 있고, 화면(<see cref="PokerTableUI"/>)과
    /// 규칙(<see cref="IndianPoker"/>) 사이에서 순서를 잡는다(2026-09-02 사용자 기획).
    ///
    /// <code>
    ///   앞돈을 걸고 시작 → 서로 한 장 (자기 패는 안 보이고 상대 패만 보인다)
    ///   → 플레이어 베팅 → 캐릭터가 콜/레이즈/다이 → (레이즈면) 플레이어가 콜/다이
    ///   → 공개 → 돈이 오가고 → 다음 판 또는 그만두기
    /// </code>
    ///
    /// <b>돈이 오가는 자리는 여기 하나뿐이다.</b> 규칙 클래스는 지갑을 모르고, 화면은 숫자만
    /// 보여준다 - 그래야 연출이 어디서 끊겨도 지갑이 어긋나지 않는다.
    ///
    /// <b>대사가 게임만큼 중요하다</b>(사용자 기획)는 게 이 클래스 모양을 정했다. 한 판의
    /// 마디마다(패를 보고 / 답하고 / 이기고 지고) <see cref="SpeechDirector"/> 를 부르고,
    /// 대사가 없는 마디는 그냥 조용히 넘어간다.
    /// </summary>
    public class MiniGameFlow : MonoBehaviour
    {
        [Header("이어붙일 것들")]
        [SerializeField] private PokerTableUI table;

        [Tooltip("대사창. 없으면 대사 없이 게임만 돈다.")]
        [SerializeField] private SpeechDirector speech;

        [Header("규칙")]
        [Tooltip("한 판을 시작할 때 <b>양쪽이 똑같이 내는</b> 앞돈. 이것만으로도 판이 성립한다.")]
        [Min(1)]
        [SerializeField] private long ante = 10L;

        [Tooltip("플레이어가 한 번에 걸 수 있는 최대 배수(앞돈 대비). " +
                 "<b>상대가 낼 수 있는 만큼</b>으로도 한 번 더 잘린다 - 상대가 못 받는 금액을 " +
                 "부르면 자동으로 접히기만 해서 게임이 안 된다.")]
        [Min(1)]
        [SerializeField] private int maxBetMultiplier = 10;

        [Header("연출 간격(초)")]
        [Tooltip("빈털터리 대사를 마치고 자리를 뜨기까지 두는 사이(초). " +
                 "0이면 말이 끝나자마자 나간다.")]
        [Min(0f)]
        [SerializeField] private float brokeExitDelay = 0.6f;

        [SerializeField] private float dealDelay = 0.5f;
        [SerializeField] private float thinkDelay = 0.8f;
        [SerializeField] private float showdownDelay = 1.1f;

        private IndianPoker game;
        private System.Random rng;
        private PanelType opponent;
        private CharacterPersonality personality;

        /// <summary>지금 진행 중인 코루틴. 두 번 굴러가지 않게 들고 있는다.</summary>
        private Coroutine running;

        private void Awake()
        {
            opponent = MiniGameEntry.Character;
            personality = opponent != null ? opponent.personality : null;

            rng = new System.Random();
            game = new IndianPoker(rng);

            if (table != null)
            {
                table.OnDealRequested += HandleDeal;
                table.OnBetConfirmed += HandleBet;
                table.OnCallPressed += HandleCall;
                table.OnFoldPressed += HandleFold;
            }
        }

        private void OnDestroy()
        {
            if (table == null)
                return;

            table.OnDealRequested -= HandleDeal;
            table.OnBetConfirmed -= HandleBet;
            table.OnCallPressed -= HandleCall;
            table.OnFoldPressed -= HandleFold;
        }

        /// <summary>
        /// 이 게임을 시작한다. 캐릭터를 세우고 인사하는 건 <see cref="MiniGameSession"/> 이 이미 했다 -
        /// 여기서는 <b>판만</b> 연다.
        /// </summary>
        public void Begin()
        {
            quitting = false;
            game.Reset();
            RefreshMoney();
            table?.ShowIdle(ante);
        }

        /// <summary>판을 접는다. 돌던 게 있으면 멈춘다.</summary>
        public void Stop()
        {
            if (running != null)
            {
                StopCoroutine(running);
                running = null;
            }

            game.Reset();
        }

        // ---------------------------------------------------------------- 한 판

        private void HandleDeal()
        {
            if (running != null)
                return;

            running = StartCoroutine(DealRoutine());
        }

        private IEnumerator DealRoutine()
        {
            // 앞돈을 양쪽이 낸다. 못 내면 판이 성립하지 않는다.
            if (!TryTakeAnte())
            {
                yield return Speak(SpeechTrigger.PokerBroke);
                table?.ShowBroke();

                // ⭐ 말만 남기고 멈춰 있지 않는다 - 판돈이 없으면 더 할 게 없으니 자리를 뜬다.
                yield return new WaitForSeconds(brokeExitDelay);

                running = null;
                OnRoomExitRequested?.Invoke();
                yield break;
            }

            game.Deal(ante);
            RefreshMoney();

            // <b>플레이어에게는 자기 패를 감춘다</b> - 인디언 포커의 전부다.
            table?.ShowDeal(game.OpponentCard, game.Pot);
            yield return new WaitForSeconds(dealDelay);

            // 캐릭터는 플레이어의 패를 보고 반응한다. 그 반응 자체가 정보가 된다 -
            // 정직한 캐릭터일수록 여기서 속이 드러난다.
            float chance = PokerAI.WinChance(game.PlayerCard);
            yield return Speak(chance >= 0.55f ? SpeechTrigger.PokerConfident
                                               : SpeechTrigger.PokerWorried);

            table?.ShowBetting(ante, MaxBet());
            running = null;
        }

        private void HandleBet(long amount)
        {
            if (running != null || game.Phase != PokerPhase.PlayerBet)
                return;

            running = StartCoroutine(BetRoutine(amount));
        }

        private IEnumerator BetRoutine(long amount)
        {
            amount = Mathf.Max(0, (int)Mathf.Min(amount, MaxBet()));

            if (amount > 0L && !PlayerPay(amount))
            {
                table?.ShowNotice("골드가 모자랍니다.");
                running = null;
                yield break;
            }

            game.PlayerBets(amount);
            RefreshMoney();
            table?.ShowPot(game.Pot);

            yield return new WaitForSeconds(thinkDelay);

            // 캐릭터가 답한다. 낼 수 있는 한도는 자기 소지금에서 이미 낸 몫을 뺀 만큼이다.
            long toCall = game.PlayerStake - game.OpponentStake;
            long purse = CharacterWallet.Get(opponent);
            var decision = PokerAI.Decide(personality, rng, game.PlayerCard, toCall, game.Pot, purse);

            if (decision.response == PokerResponse.Fold)
            {
                game.OpponentResponds(PokerResponse.Fold);
                yield return Speak(SpeechTrigger.PokerFold);
                yield return FinishRoundRoutine();
                yield break;
            }

            // 콜이든 레이즈든 우선 맞추는 값을 상대가 낸다.
            long pay = toCall + (decision.response == PokerResponse.Raise ? decision.raiseAmount : 0L);
            if (!CharacterWallet.TrySpend(opponent, pay))
            {
                // 여기 오면 안 되지만(부를 수 있는 금액을 미리 잘라둔다), 오면 접는 걸로 친다.
                game.OpponentResponds(PokerResponse.Fold);
                yield return Speak(SpeechTrigger.PokerFold);
                yield return FinishRoundRoutine();
                yield break;
            }

            game.OpponentResponds(decision.response, decision.raiseAmount);
            RefreshMoney();
            table?.ShowPot(game.Pot);

            if (decision.response == PokerResponse.Raise)
            {
                // <b>허세면 허세 대사를 고른다</b> - 대사집에 그 줄이 없으면 조용히 넘어가므로,
                // 잘 속이는 캐릭터에게만 줄을 써두면 그것 자체가 캐릭터의 색이 된다.
                yield return Speak(decision.bluffing ? SpeechTrigger.PokerBluff
                                                     : SpeechTrigger.PokerRaise);

                long need = game.OpponentStake - game.PlayerStake;
                table?.ShowCallOrFold(need, game.Pot);
                running = null;
                yield break;
            }

            yield return Speak(SpeechTrigger.PokerCall);
            yield return FinishRoundRoutine();
        }

        private void HandleCall()
        {
            if (running != null || game.Phase != PokerPhase.PlayerCallOrFold)
                return;

            running = StartCoroutine(CallRoutine());
        }

        private IEnumerator CallRoutine()
        {
            long need = game.OpponentStake - game.PlayerStake;

            if (!PlayerPay(need))
            {
                table?.ShowNotice("골드가 모자랍니다.");
                running = null;
                yield break;
            }

            game.PlayerCalls();
            RefreshMoney();
            yield return FinishRoundRoutine();
        }

        private void HandleFold()
        {
            if (running != null)
                return;

            if (game.Phase != PokerPhase.PlayerBet && game.Phase != PokerPhase.PlayerCallOrFold)
                return;

            running = StartCoroutine(FoldRoutine());
        }

        private IEnumerator FoldRoutine()
        {
            game.PlayerFolds();
            yield return FinishRoundRoutine();
        }

        /// <summary>
        /// 승부가 난 뒤 - 패를 까고, 돈을 옮기고, 대사를 한 줄 하고, 다음 판을 기다린다.
        /// </summary>
        private IEnumerator FinishRoundRoutine()
        {
            table?.ShowShowdown(game.PlayerCard, game.OpponentCard, game.Outcome, game.Payout);
            yield return new WaitForSeconds(showdownDelay);

            SettleMoney();
            RefreshMoney();

            bool playerWon = game.Outcome == PokerOutcome.PlayerWin
                             || game.Outcome == PokerOutcome.OpponentFolded;

            if (game.Outcome == PokerOutcome.Draw)
            {
                // 비긴 판에는 따로 줄을 두지 않았다 - 여기서 말이 없는 게 오히려 자연스럽다.
            }
            else if (!playerWon && game.WonWithLowestCard)
            {
                // ⭐ 역전 대사는 <b>캐릭터가 1로 뒤집었을 때</b>만이다.
                // 플레이어가 1로 이긴 판은 캐릭터 입장에서 그냥 진 판이라 패배 대사가 맞는다
                // (시트의 reversal 줄이 전부 "소녀는 천재인 것시와요" 쪽이다).
                yield return Speak(SpeechTrigger.PokerLowCardWin);
            }
            else
                yield return Speak(playerWon ? SpeechTrigger.PokerLose : SpeechTrigger.PokerWin);

            game.Reset();

            if (CharacterWallet.Get(opponent) < ante || PlayerProfile.Gold < ante)
            {
                yield return Speak(SpeechTrigger.PokerBroke);
                table?.ShowBroke();

                // ⭐ 말만 남기고 멈춰 있지 않는다 - 판돈이 없으면 더 할 게 없으니 자리를 뜬다.
                yield return new WaitForSeconds(brokeExitDelay);

                running = null;
                OnRoomExitRequested?.Invoke();
                yield break;
            }

            table?.ShowIdle(ante);
            running = null;
        }

        // ---------------------------------------------------------------- 돈

        /// <summary>양쪽에서 앞돈을 걷는다. 한쪽이라도 못 내면 <b>아무것도 걷지 않고</b> 실패시킨다.</summary>
        private bool TryTakeAnte()
        {
            if (PlayerProfile.Gold < ante || CharacterWallet.Get(opponent) < ante)
                return false;

            PlayerProfile.Gold -= ante;
            CharacterWallet.TrySpend(opponent, ante);
            return true;
        }

        private bool PlayerPay(long amount)
        {
            if (amount <= 0L)
                return true;

            if (PlayerProfile.Gold < amount)
                return false;

            PlayerProfile.Gold -= amount;
            return true;
        }

        /// <summary>
        /// 판이 끝났으니 돈을 옮긴다.
        ///
        /// <b>양쪽이 낸 돈은 이미 지갑에서 빠져 있다</b> - 여기서는 판에 쌓인 걸 나눠주기만 한다.
        /// 이긴 쪽이 자기가 낸 몫 + 상대가 낸 몫(가장 낮은 패로 이겼으면 두 배)을 가져가고,
        /// 비기면 각자 낸 만큼 돌려받는다.
        /// </summary>
        private void SettleMoney()
        {
            if (game.Outcome == PokerOutcome.Draw)
            {
                PlayerProfile.Gold += game.PlayerStake;
                CharacterWallet.Add(opponent, game.OpponentStake);
                return;
            }

            bool playerWon = game.Outcome == PokerOutcome.PlayerWin
                             || game.Outcome == PokerOutcome.OpponentFolded;

            // 가장 낮은 패 보너스로 상대가 낸 것보다 더 받을 수 있다 -
            // 모자란 몫은 상대 지갑에서 더 빼되, 없으면 있는 만큼만 간다.
            long prize = game.Payout;

            if (playerWon)
            {
                long fromPot = game.OpponentStake;
                long extra = prize - fromPot;

                if (extra > 0L && !CharacterWallet.TrySpend(opponent, extra))
                {
                    // 보너스를 다 못 치르면 남은 걸 전부 준다(빈털터리가 되면 게임이 끝난다).
                    extra = CharacterWallet.Get(opponent);
                    CharacterWallet.TrySpend(opponent, extra);
                }

                PlayerProfile.Gold += game.PlayerStake + fromPot + (extra > 0L ? extra : 0L);
                return;
            }

            long playerLost = game.PlayerStake;
            long bonus = prize - playerLost;

            if (bonus > 0L)
            {
                if (PlayerProfile.Gold < bonus)
                    bonus = PlayerProfile.Gold;

                PlayerProfile.Gold -= bonus;
            }
            else
                bonus = 0L;

            CharacterWallet.Add(opponent, game.OpponentStake + playerLost + bonus);
        }

        /// <summary>
        /// 플레이어가 이번에 걸 수 있는 최대 금액.
        /// <b>상대가 받을 수 있는 만큼으로도 자른다</b> - 상대가 못 받는 금액을 부르면
        /// 자동으로 접히기만 해서 게임이 성립하지 않는다.
        /// </summary>
        private long MaxBet()
        {
            long byRule = ante * maxBetMultiplier;
            long byPlayer = PlayerProfile.Gold;
            long byOpponent = CharacterWallet.Get(opponent);

            long limit = Mathf.Min((int)Mathf.Min(byRule, byPlayer), (int)byOpponent);
            return limit < 0L ? 0L : limit;
        }

        private void RefreshMoney()
            => table?.ShowMoney(PlayerProfile.Gold, CharacterWallet.Get(opponent));

        // ---------------------------------------------------------------- 대사 · 나가기

        private IEnumerator Speak(SpeechTrigger trigger)
        {
            if (speech == null || opponent == null)
                yield break;

            yield return speech.Play(opponent, trigger, SpeechSide.Enemy);
        }

        /// <summary>
        /// 이 게임을 그만하겠다는 알림. <b>어디로 갈지는 여기서 정하지 않는다</b> -
        /// 고르는 화면으로 갈지 방으로 갈지는 <see cref="MiniGameSession"/> 이 안다.
        /// </summary>
        public event System.Action OnQuitRequested;
        /// <summary>
        /// ⭐ <b>방까지 아주 나가겠다</b>는 알림(판돈이 바닥났을 때).
        /// <see cref="OnQuitRequested"/> 는 한 칸만 물러나 게임 목록으로 가지만,
        /// 더 걸 돈이 없으면 목록에 남겨둬도 할 수 있는 게 없다.
        /// </summary>
        public event System.Action OnRoomExitRequested;

        private bool quitting;

        /// <summary>화면에서 그만두기를 눌렀을 때.</summary>
        public void Quit()
        {
            if (quitting)
                return;

            quitting = true;
            OnQuitRequested?.Invoke();
        }

    }
}
