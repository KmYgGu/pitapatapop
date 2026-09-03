using System;
using System.Collections.Generic;

namespace JojoPuzzle.MiniGame
{
    /// <summary>한 판이 지금 어느 단계인지.</summary>
    public enum BlackjackPhase
    {
        /// <summary>아직 안 돌렸다. 얼마를 걸지 정할 차례.</summary>
        Idle,

        /// <summary>패를 돌렸고 <b>플레이어 차례</b>다. 캐릭터의 두 번째 장은 아직 덮여 있다.</summary>
        PlayerTurn,

        /// <summary>플레이어가 끝냈고 <b>캐릭터(딜러)가 뽑는</b> 중이다.</summary>
        OpponentTurn,

        /// <summary>승부가 났다.</summary>
        Showdown
    }

    public enum BlackjackOutcome
    {
        None,
        PlayerWin,
        OpponentWin,
        Draw
    }

    /// <summary>
    /// <b>블랙잭</b> 한 판의 규칙(2026-09-02 사용자 기획).
    ///
    /// <code>
    ///   걸고 → 서로 두 장 (캐릭터의 둘째 장은 덮인다)
    ///   → 플레이어가 더 뽑거나 멈춤   … 여기서 넘기면 <b>그 자리에서 진다</b>
    ///   → 캐릭터가 뒷패를 까고 성향대로 뽑음 → 비교
    /// </code>
    ///
    /// 사용자가 정한 것:
    ///  - <b>딜러가 없다.</b> 둘 다 같은 규칙으로 뽑고 21에 가까운 쪽이 이긴다.
    ///  - <b>베팅은 돌리기 전에 한 번</b>. 받은 뒤에는 더 뽑을지 멈출지만 고른다.
    ///  - <b>블랙잭 보너스는 없다.</b> 21은 그냥 가장 높은 수다.
    ///
    /// <b>⭐ 캐릭터가 딜러다</b>(2026-09-02에 바꿨다). 처음에는 캐릭터가 먼저 뽑았는데,
    /// 그러면 플레이어가 <b>결과를 다 보고</b> 마지막에 두게 되어 이길 수밖에 없었다 -
    /// 버스트를 구경하다 크게 걸고, 멈춘 수를 넘기기만 하면 됐다.
    ///
    /// 딜러 규칙이 판을 되돌리는 자리는 둘이다:
    ///  1. <b>플레이어가 먼저 끝낸다.</b> 넘기면 캐릭터는 뽑지도 않고 이긴다 - 둘 다 넘겨도
    ///     먼저 넘긴 쪽이 지므로, 이게 진짜 하우스 엣지다.
    ///  2. <b>캐릭터의 둘째 장은 덮여 있다</b>(<see cref="OpponentHoleHidden"/>).
    ///     상대 수를 모른 채 정해야 해서 성향을 읽어도 그대로 이용하긴 어렵다.
    ///
    /// <b>MonoBehaviour 가 아니다</b> - 규칙은 뷰와 분리해 순수 클래스로 둔다(IndianPoker 와 같은 방침).
    /// 돈도 여기서 옮기지 않는다.
    /// </summary>
    public class Blackjack
    {
        /// <summary>넘으면 진다.</summary>
        public const int Target = 21;

        /// <summary>에이스를 11로 세는 값. 1로 세다가 여유가 있으면 이만큼 올린다.</summary>
        private const int AceBonus = 10;

        private readonly Random rng;

        public Blackjack(Random rng = null)
        {
            this.rng = rng ?? new Random();
        }

        public BlackjackPhase Phase { get; private set; } = BlackjackPhase.Idle;

        /// <summary>양쪽이 똑같이 낸 금액. 이긴 쪽이 상대 몫을 가져간다.</summary>
        public long Bet { get; private set; }

        public BlackjackOutcome Outcome { get; private set; }

        private readonly List<int> playerHand = new List<int>();
        private readonly List<int> opponentHand = new List<int>();

        public IReadOnlyList<int> PlayerHand => playerHand;
        public IReadOnlyList<int> OpponentHand => opponentHand;

        public int PlayerTotal => Total(playerHand);
        public int OpponentTotal => Total(opponentHand);

        public bool PlayerBusted => PlayerTotal > Target;
        public bool OpponentBusted => OpponentTotal > Target;

        /// <summary>
        /// 그 손패의 값. <b>에이스는 1 또는 11 중 유리한 쪽</b>으로 센다 -
        /// 1로 다 더한 뒤 여유가 있으면 한 장만 11로 올린다(둘 다 11이면 반드시 넘치므로).
        /// </summary>
        public static int Total(IReadOnlyList<int> hand)
        {
            if (hand == null)
                return 0;

            int sum = 0;
            bool hasAce = false;

            for (int i = 0; i < hand.Count; i++)
            {
                int card = hand[i];
                if (card == 1)
                    hasAce = true;

                sum += CardValue(card);
            }

            return hasAce && sum + AceBonus <= Target ? sum + AceBonus : sum;
        }

        /// <summary>카드 한 장의 기본값. <b>J·Q·K 는 10</b>, 에이스는 여기서 1이다.</summary>
        public static int CardValue(int card)
            => card >= 11 ? 10 : card;

        /// <summary>새 판. 양쪽이 <paramref name="bet"/> 씩 걸고 두 장씩 받는다.</summary>
        public void Deal(long bet)
        {
            Bet = bet < 0L ? 0L : bet;
            Outcome = BlackjackOutcome.None;

            playerHand.Clear();
            opponentHand.Clear();

            for (int i = 0; i < 2; i++)
            {
                opponentHand.Add(Draw());
                playerHand.Add(Draw());
            }

            Phase = BlackjackPhase.PlayerTurn;
        }

        /// <summary>
        /// 캐릭터의 <b>덮여 있는 장이 있는지</b>. 플레이어가 정하는 동안은 둘째 장을 감춘다 -
        /// 딜러의 홀 카드다.
        /// </summary>
        public bool OpponentHoleHidden =>
            Phase == BlackjackPhase.Idle || Phase == BlackjackPhase.PlayerTurn;

        /// <summary>덮인 장을 뺀, <b>플레이어에게 보이는</b> 캐릭터의 값.</summary>
        public int OpponentVisibleTotal
        {
            get
            {
                if (!OpponentHoleHidden)
                    return OpponentTotal;

                if (opponentHand.Count == 0)
                    return 0;

                shown.Clear();
                shown.Add(opponentHand[0]);
                return Total(shown);
            }
        }

        // 보이는 값만 셀 때 돌려 쓰는 버퍼(호출마다 새 List 를 만들지 않으려고).
        private readonly List<int> shown = new List<int>(1);

        /// <summary>캐릭터가 한 장 더 받는다. 넘치면 그 자리에서 승부가 난다.</summary>
        public void OpponentHits()
        {
            if (Phase != BlackjackPhase.OpponentTurn)
                return;

            opponentHand.Add(Draw());

            if (OpponentBusted)
                Resolve();
        }

        /// <summary>캐릭터가 멈춘다. 승부를 낸다.</summary>
        public void OpponentStands()
        {
            if (Phase == BlackjackPhase.OpponentTurn)
                Resolve();
        }

        /// <summary>
        /// 플레이어가 한 장 더 받는다. <b>넘기면 그 자리에서 진다</b> -
        /// 캐릭터는 뽑지도 않는다(딜러 규칙).
        /// </summary>
        public void PlayerHits()
        {
            if (Phase != BlackjackPhase.PlayerTurn)
                return;

            playerHand.Add(Draw());

            if (PlayerBusted)
                Resolve();
        }

        /// <summary>플레이어가 멈춘다. 이제 캐릭터가 뒷패를 까고 뽑을 차례.</summary>
        public void PlayerStands()
        {
            if (Phase == BlackjackPhase.PlayerTurn)
                Phase = BlackjackPhase.OpponentTurn;
        }

        /// <summary>
        /// 승부. ⭐ <b>먼저 넘긴 쪽이 진다</b> - 플레이어가 넘겼으면 캐릭터가 어떻든 캐릭터의
        /// 승리다(그 시점에 캐릭터는 아직 뽑지도 않았다). 이게 딜러 쪽 우위의 핵심이다.
        /// 같은 수는 무승부.
        /// </summary>
        private void Resolve()
        {
            Phase = BlackjackPhase.Showdown;

            if (PlayerBusted)
                Outcome = BlackjackOutcome.OpponentWin;
            else if (OpponentBusted)
                Outcome = BlackjackOutcome.PlayerWin;
            else if (PlayerTotal == OpponentTotal)
                Outcome = BlackjackOutcome.Draw;
            else
                Outcome = PlayerTotal > OpponentTotal
                    ? BlackjackOutcome.PlayerWin
                    : BlackjackOutcome.OpponentWin;
        }

        /// <summary>처음 상태로. 다음 판을 걸 수 있게 된다.</summary>
        public void Reset()
        {
            Phase = BlackjackPhase.Idle;
            Outcome = BlackjackOutcome.None;
            Bet = 0L;
            playerHand.Clear();
            opponentHand.Clear();
        }

        /// <summary>
        /// 한 장 뽑는다. <b>덱을 따로 두지 않는다</b> - 매번 1~13 중 하나를 새로 굴린다.
        /// 트럼프 한 벌을 세는 편이 정석이지만, 한 판이 짧고 상대도 한 명뿐이라
        /// 카드 세기(카운팅)가 성립하지 않아 체감이 같다. 필요해지면 여기만 고치면 된다.
        /// </summary>
        private int Draw() => rng.Next(1, 14);
    }
}
