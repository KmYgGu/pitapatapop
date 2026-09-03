using System;

namespace JojoPuzzle.MiniGame
{
    /// <summary>한 판이 지금 어느 단계인지.</summary>
    public enum PokerPhase
    {
        /// <summary>아직 안 돌렸다. 판돈을 걸고 시작하기를 기다린다.</summary>
        Idle,

        /// <summary>패를 나눠줬다. <b>플레이어가 얼마를 걸지</b> 정할 차례.</summary>
        PlayerBet,

        /// <summary>플레이어가 걸었고 <b>캐릭터가 답할</b> 차례(콜·레이즈·다이).</summary>
        OpponentRespond,

        /// <summary>캐릭터가 레이즈했다. <b>플레이어가 받을지 접을지</b> 정할 차례.</summary>
        PlayerCallOrFold,

        /// <summary>승부가 났다. 결과를 보여주고 다음 판을 기다린다.</summary>
        Showdown
    }

    /// <summary>한 판이 어떻게 끝났는지.</summary>
    public enum PokerOutcome
    {
        None,
        PlayerWin,
        OpponentWin,
        Draw,

        /// <summary>플레이어가 접었다 - 그때까지 건 돈을 잃는다.</summary>
        PlayerFolded,

        /// <summary>캐릭터가 접었다.</summary>
        OpponentFolded
    }

    /// <summary>캐릭터가 플레이어의 베팅에 어떻게 답했는지.</summary>
    public enum PokerResponse
    {
        Call,
        Raise,
        Fold
    }

    /// <summary>
    /// <b>인디언 포커</b> 한 판의 규칙과 상태(2026-09-02 사용자 기획).
    ///
    /// <code>
    ///   앞돈 → 서로 한 장씩 (자기 패는 안 보이고 상대 패만 보인다)
    ///   → 플레이어 베팅 → 캐릭터가 콜 / 레이즈 / 다이
    ///   → (레이즈면) 플레이어가 콜 / 다이
    ///   → 공개
    /// </code>
    ///
    /// <b>MonoBehaviour 가 아니다</b> - 이 프로젝트의 규칙 코드는 전부 뷰와 분리해서 순수 클래스로
    /// 둔다(BoardData/BoardManager 와 같은 방침). 화면·연출·대사는 부르는 쪽이 책임진다.
    ///
    /// <b>돈을 직접 건드리지 않는다.</b> 지갑(PlayerProfile.Gold / CharacterWallet)은 부르는 쪽이
    /// 옮긴다 - 여기서 옮기면 연출 도중에 화면과 지갑이 어긋나고, 되돌리기도 어렵다.
    /// </summary>
    public class IndianPoker
    {
        /// <summary>가장 낮은 패. <b>이걸로 이기면 판돈이 두 배</b>가 된다(사용자 확정).</summary>
        public const int LowestCard = 1;

        /// <summary>가장 높은 패. 트럼프 한 벌이라 K = 13 이다(Assets/image/트럼프4x.png).</summary>
        public const int HighestCard = 13;

        /// <summary>가장 낮은 패로 이겼을 때 판돈에 곱하는 값.</summary>
        public const int LowestCardBonus = 2;

        private readonly Random rng;

        public IndianPoker(Random rng = null)
        {
            this.rng = rng ?? new Random();
        }

        public PokerPhase Phase { get; private set; } = PokerPhase.Idle;

        /// <summary>플레이어의 패. <b>플레이어에게는 안 보이고</b> 캐릭터에게는 보인다.</summary>
        public int PlayerCard { get; private set; }

        /// <summary>캐릭터의 패. <b>플레이어에게 보인다.</b></summary>
        public int OpponentCard { get; private set; }

        /// <summary>앞돈. 패를 받는 값으로 양쪽이 똑같이 낸다.</summary>
        public long Ante { get; private set; }

        /// <summary>플레이어가 이 판에 지금까지 낸 돈(앞돈 포함).</summary>
        public long PlayerStake { get; private set; }

        /// <summary>캐릭터가 이 판에 지금까지 낸 돈(앞돈 포함).</summary>
        public long OpponentStake { get; private set; }

        /// <summary>판에 쌓인 돈 전부.</summary>
        public long Pot => PlayerStake + OpponentStake;

        /// <summary>캐릭터가 어떻게 답했는지. 아직 안 답했으면 의미 없다.</summary>
        public PokerResponse LastResponse { get; private set; }

        /// <summary>이 판의 결과.</summary>
        public PokerOutcome Outcome { get; private set; }

        /// <summary>
        /// 이번 판에서 <b>이긴 쪽이 실제로 가져가는 돈</b>(진 쪽이 낸 만큼).
        /// 가장 낮은 패로 이겼으면 두 배다. 비겼거나 아직 안 끝났으면 0.
        /// </summary>
        public long Payout { get; private set; }

        /// <summary>
        /// 새 판을 시작한다. 양쪽이 <paramref name="ante"/> 씩 내고 한 장씩 받는다.
        /// </summary>
        public void Deal(long ante)
        {
            Ante = ante < 0L ? 0L : ante;
            PlayerStake = Ante;
            OpponentStake = Ante;

            PlayerCard = rng.Next(LowestCard, HighestCard + 1);
            OpponentCard = rng.Next(LowestCard, HighestCard + 1);

            LastResponse = PokerResponse.Call;
            Outcome = PokerOutcome.None;
            Payout = 0L;
            Phase = PokerPhase.PlayerBet;
        }

        /// <summary>
        /// 플레이어가 <paramref name="amount"/> 을 건다. 0이면 체크(더 걸지 않음)다.
        /// 다음은 캐릭터가 답할 차례.
        /// </summary>
        public void PlayerBets(long amount)
        {
            if (Phase != PokerPhase.PlayerBet)
                return;

            PlayerStake += amount < 0L ? 0L : amount;
            Phase = PokerPhase.OpponentRespond;
        }

        /// <summary>
        /// 캐릭터가 답한다. 콜이면 플레이어가 건 만큼 맞추고 바로 공개, 레이즈면 그 위에 더
        /// 얹고 플레이어에게 공을 넘기며, 다이면 그 자리에서 끝난다.
        /// </summary>
        /// <param name="raiseAmount">레이즈일 때 <b>맞춘 뒤에 더 얹는</b> 금액.</param>
        public void OpponentResponds(PokerResponse response, long raiseAmount = 0L)
        {
            if (Phase != PokerPhase.OpponentRespond)
                return;

            LastResponse = response;

            if (response == PokerResponse.Fold)
            {
                // 접은 쪽은 그때까지 낸 돈을 잃는다. 이긴 쪽은 그만큼만 가져간다 -
                // 자기가 낸 돈은 원래 자기 것이라 오가지 않는다.
                Outcome = PokerOutcome.OpponentFolded;
                Payout = OpponentStake;
                Phase = PokerPhase.Showdown;
                return;
            }

            // 콜이든 레이즈든 우선 플레이어가 건 만큼은 맞춘다.
            OpponentStake = PlayerStake;

            if (response == PokerResponse.Raise && raiseAmount > 0L)
            {
                OpponentStake += raiseAmount;
                Phase = PokerPhase.PlayerCallOrFold;
                return;
            }

            LastResponse = PokerResponse.Call;
            Resolve();
        }

        /// <summary>캐릭터의 레이즈를 플레이어가 받는다. 맞추고 공개.</summary>
        public void PlayerCalls()
        {
            if (Phase != PokerPhase.PlayerCallOrFold)
                return;

            PlayerStake = OpponentStake;
            Resolve();
        }

        /// <summary>플레이어가 접는다. 그때까지 낸 돈을 잃는다.</summary>
        public void PlayerFolds()
        {
            if (Phase != PokerPhase.PlayerBet && Phase != PokerPhase.PlayerCallOrFold)
                return;

            Outcome = PokerOutcome.PlayerFolded;
            Payout = PlayerStake;
            Phase = PokerPhase.Showdown;
        }

        /// <summary>
        /// 패를 까고 승부를 낸다. <b>가장 낮은 패로 이기면 두 배</b>(사용자 확정) -
        /// 상대 패가 낮을 때 과감하게 지를 이유가 여기서 생긴다.
        /// </summary>
        private void Resolve()
        {
            Phase = PokerPhase.Showdown;

            if (PlayerCard == OpponentCard)
            {
                // 비기면 낸 돈을 그대로 돌려받는다 - 오가는 게 없다.
                Outcome = PokerOutcome.Draw;
                Payout = 0L;
                return;
            }

            bool playerWon = PlayerCard > OpponentCard;
            int winningCard = playerWon ? PlayerCard : OpponentCard;

            // 진 쪽이 낸 만큼만 오간다(자기가 낸 돈은 원래 자기 것).
            long loserStake = playerWon ? OpponentStake : PlayerStake;

            Outcome = playerWon ? PokerOutcome.PlayerWin : PokerOutcome.OpponentWin;
            Payout = winningCard == LowestCard ? loserStake * LowestCardBonus : loserStake;
        }

        /// <summary>이 판이 <b>가장 낮은 패로 이긴</b> 판인지. 연출·대사가 이걸 본다.</summary>
        public bool WonWithLowestCard =>
            (Outcome == PokerOutcome.PlayerWin && PlayerCard == LowestCard)
            || (Outcome == PokerOutcome.OpponentWin && OpponentCard == LowestCard);

        /// <summary>판을 접고 처음 상태로. 다음 판을 걸 수 있게 된다.</summary>
        public void Reset()
        {
            Phase = PokerPhase.Idle;
            Outcome = PokerOutcome.None;
            Payout = 0L;
            PlayerStake = 0L;
            OpponentStake = 0L;
        }
    }
}
