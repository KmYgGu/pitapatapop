using System.Collections;
using JojoPuzzle.App;
using JojoPuzzle.Core;
using JojoPuzzle.UI;
using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 도둑잡기 한 자리의 <b>진행</b>(2026-09-02 사용자 기획).
    /// 다른 도박과 같은 자리에 있고, 공통 부분(캐릭터 세우기·인사·나가기)은
    /// <see cref="MiniGameSession"/> 이 이미 해둔다.
    ///
    /// <b>한 판이 되풀이된다</b> - 조커를 집으면 주인이 바뀌고 처음부터 다시 민다.
    /// 매번 반반이라 평균 두 번쯤이면 끝난다.
    /// </summary>
    public class OldMaidFlow : MonoBehaviour
    {
        [Header("이어붙일 것들")]
        [SerializeField] private OldMaidTableUI table;

        [Tooltip("테이블 위에 카드를 놓는 3D 판.")]
        [SerializeField] private OldMaidCardBoard board;

        [Tooltip("대사창. 없으면 대사 없이 게임만 돈다.")]
        [SerializeField] private SpeechDirector speech;

        [Header("규칙")]
        [Min(1)]
        [SerializeField] private long betStep = 10L;

        [Min(1)]
        [SerializeField] private int maxBetMultiplier = 10;

        [Header("연출 간격(초)")]
        [Tooltip("빈털터리 대사를 마치고 자리를 뜨기까지 두는 사이(초). " +
                 "0이면 말이 끝나자마자 나간다.")]
        [Min(0f)]
        [SerializeField] private float brokeExitDelay = 0.6f;

        [SerializeField] private float dealDelay = 0.5f;
        [SerializeField] private float thinkDelay = 0.8f;
        [SerializeField] private float revealDelay = 0.9f;
        [SerializeField] private float showdownDelay = 1.1f;

        private OldMaid game;
        private System.Random rng;
        private PanelType opponent;
        private CharacterPersonality personality;
        private Coroutine running;

        // 카드를 눌렀을 때 그 자리를 받아두는 곳. -1 이면 아직 안 눌렀다.
        private int tapped = -1;

        /// <summary>그만하겠다는 알림. 어디로 갈지는 <see cref="MiniGameSession"/> 이 안다.</summary>
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
            game = new OldMaid(rng);

            if (table != null)
                table.OnBetConfirmed += HandleBet;

            if (board != null)
                board.OnCardPicked += HandleTap;
        }

        private void OnDestroy()
        {
            if (table != null)
                table.OnBetConfirmed -= HandleBet;

            if (board != null)
                board.OnCardPicked -= HandleTap;
        }

        public void Begin()
        {
            game.Reset();
            board?.Clear();
            RefreshMoney();
            table?.BindCharacter(opponent);
            table?.ShowIdle(betStep, MaxBet());
        }

        public void Stop()
        {
            if (running != null)
            {
                StopCoroutine(running);
                running = null;
            }

            board?.Clear();
            game.Reset();
        }

        private void HandleTap(int slot) => tapped = slot;

        // ---------------------------------------------------------------- 한 판

        private void HandleBet(long amount)
        {
            if (running != null || game.Phase != OldMaidPhase.Idle)
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
            table?.ShowBet(bet);

            // 조커가 넘어갈 때마다 이 고리를 다시 돈다.
            while (game.Phase != OldMaidPhase.Showdown)
            {
                LayOut();
                yield return new WaitForSeconds(dealDelay);

                if (game.HolderIsPlayer)
                    yield return PlayerOffersRoutine();
                else
                    yield return OpponentOffersRoutine();
            }

            yield return FinishRoutine();
        }

        /// <summary>
        /// 패를 깐다. <b>양쪽 다 자기 패를 쥔다</b> - 조커를 든 쪽이 두 장, 집을 쪽이 한 장.
        /// 어느 걸 앞면으로 보일지(내 손이면 보인다)는 카드판이 알아서 한다.
        /// </summary>
        private void LayOut()
        {
            board?.Deal(game.HolderIsPlayer, game.CardAt, game.SuitAt,
                        game.PickerCard, game.PickerSuit);
            table?.ShowTurn(game.HolderIsPlayer, game.PassCount);
        }

        /// <summary>내가 조커를 들었다 - 한 장을 밀고, 캐릭터가 집는다.</summary>
        private IEnumerator PlayerOffersRoutine()
        {
            table?.ShowNotice("내밀 카드를 고르세요");

            yield return WaitForTapRoutine();
            int slot = tapped;

            game.Offer(slot);
            board?.Offer(slot);
            yield return new WaitForSeconds(thinkDelay);

            int picked = OldMaidAI.ChoosePick(personality, rng, slot);

            // ⚠ 뭘 집었는지는 Pick <b>전에</b> 봐둔다 - 조커였으면 Pick 안에서 조커 자리가 다시 섞인다.
            int card = game.CardAt(picked);
            int suit = game.SuitAt(picked);
            game.Pick(picked);

            board?.Reveal(picked, card, suit);
            yield return new WaitForSeconds(revealDelay);

            // 뿅 하고 바뀌지 말고, 캐릭터가 가져가는 게 눈에 보여야 한다.
            if (board != null)
                yield return board.TakeRoutine(picked, game.LastPickWasJoker);

            if (game.LastPickWasJoker)
            {
                table?.ShowNotice("상대가 조커를 집었습니다");
                yield return Speak(SpeechTrigger.OldMaidDrewJoker);

                // 캐릭터가 자기 두 장을 바꿔 친다 - 어느 쪽이 조커인지 나를 헷갈리게 하려는 손장난.
                if (board != null)
                    yield return board.ShuffleRoutine();
            }
            else
                yield return Speak(SpeechTrigger.OldMaidWin);
        }

        /// <summary>캐릭터가 조커를 들었다 - 한 장을 밀고, 내가 집는다.</summary>
        private IEnumerator OpponentOffersRoutine()
        {
            int slot = OldMaidAI.ChooseOffer(personality, rng, game.JokerSlot);

            game.Offer(slot);
            board?.Offer(slot);
            yield return Speak(SpeechTrigger.OldMaidOffer);

            table?.ShowNotice("한 장을 집으세요");
            board?.SetPickable(true);
            yield return WaitForTapRoutine();

            int picked = tapped;

            // ⚠ 뭘 집었는지는 Pick <b>전에</b> 봐둔다 - 조커였으면 Pick 안에서 조커 자리가 다시 섞인다.
            int card = game.CardAt(picked);
            int suit = game.SuitAt(picked);
            game.Pick(picked);

            board?.Reveal(picked, card, suit);
            yield return new WaitForSeconds(revealDelay);

            // 내가 가져가는 것도 똑같이 손으로 끌어온다.
            if (board != null)
                yield return board.TakeRoutine(picked, game.LastPickWasJoker);

            if (game.LastPickWasJoker)
            {
                table?.ShowNotice("조커를 집었습니다");
                yield return Speak(SpeechTrigger.OldMaidPassed);

                // 나도 똑같이 바꿔 친다 - 이번엔 내가 미는 쪽이다.
                if (board != null)
                    yield return board.ShuffleRoutine();
            }
            else
                yield return Speak(SpeechTrigger.OldMaidLose);
        }

        /// <summary>카드를 누를 때까지 기다린다.</summary>
        private IEnumerator WaitForTapRoutine()
        {
            tapped = -1;
            board?.SetPickable(true);

            while (tapped < 0)
                yield return null;

            board?.SetPickable(false);
        }

        private IEnumerator FinishRoutine()
        {
            // 진 쪽이 남은 조커를 테이블에 눕혀 보인다. 이긴 사람의 두 장은 그대로 둔다 -
            // 가져온 장과 원래 쥐고 있던 장이 짝이라는 게 보여야 한다(2026-09-02 사용자 지시).
            if (board != null)
                yield return board.LayDownRoutine(game.JokerSlot);

            table?.ShowResult(game.Outcome, game.Bet);
            yield return new WaitForSeconds(showdownDelay);

            SettleMoney();
            RefreshMoney();

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

        private bool TakeBet(long bet)
        {
            if (PlayerProfile.Gold < bet || CharacterWallet.Get(opponent) < bet)
                return false;

            PlayerProfile.Gold -= bet;
            CharacterWallet.TrySpend(opponent, bet);
            return true;
        }

        /// <summary>양쪽이 낸 돈은 이미 지갑에서 빠져 있다. 이긴 쪽이 판에 쌓인 걸 다 가져간다.</summary>
        private void SettleMoney()
        {
            long pot = game.Bet * 2L;

            if (game.Outcome == OldMaidOutcome.PlayerWin)
                PlayerProfile.Gold += pot;
            else
                CharacterWallet.Add(opponent, pot);
        }

        private long MaxBet()
        {
            long byRule = betStep * maxBetMultiplier;
            long limit = Mathf.Min((int)Mathf.Min(byRule, PlayerProfile.Gold),
                                   (int)CharacterWallet.Get(opponent));
            return limit < 0L ? 0L : limit;
        }

        private void RefreshMoney()
            => table?.ShowMoney(PlayerProfile.Gold, CharacterWallet.Get(opponent));

        private IEnumerator Speak(SpeechTrigger trigger)
        {
            if (speech == null || opponent == null)
                yield break;

            yield return speech.Play(opponent, trigger, SpeechSide.Enemy);
        }

        /// <summary>화면에서 그만두기를 눌렀을 때.</summary>
        public void Quit() => OnQuitRequested?.Invoke();
    }
}
