using System;

namespace JojoPuzzle.MiniGame
{
    /// <summary>한 판이 지금 어느 단계인지.</summary>
    public enum OldMaidPhase
    {
        /// <summary>아직 안 돌렸다. 얼마를 걸지 정할 차례.</summary>
        Idle,

        /// <summary>조커를 든 쪽이 <b>한 장을 밀어 올릴</b> 차례.</summary>
        Offer,

        /// <summary>상대가 <b>둘 중 하나를 집을</b> 차례.</summary>
        Pick,

        /// <summary>승부가 났다.</summary>
        Showdown
    }

    public enum OldMaidOutcome
    {
        None,
        PlayerWin,
        OpponentWin
    }

    /// <summary>
    /// <b>도둑잡기</b> 한 판의 규칙(2026-09-02 사용자 기획).
    ///
    /// <code>
    ///   걸고 → 조커 한 장 + <b>같은 숫자 두 장</b>이 깔린다
    ///          한 쪽이 조커와 짝패 하나를, 다른 쪽이 나머지 짝패 하나를 든다
    ///   → 조커를 든 쪽이 자기 두 장 중 하나를 밀어 올린다(반드시 한 장)
    ///   → 상대가 둘 중 하나를 집는다
    ///        조커를 집었으면  → 집은 쪽이 새 주인. 카드가 손을 바꾸고 처음부터 되풀이
    ///        짝을 집었으면    → 짝이 맞아 털어낸다. 집은 쪽이 이긴다
    /// </code>
    ///
    /// ⭐ <b>판에 깔리는 카드는 딱 셋이다</b>(2026-09-02 사용자 지적: *"가져온 카드와 내가 가진
    /// 카드의 수치가 달라. 맥락적으로 이건 이상하잖아"*). 도둑잡기는 <b>짝을 맞춰 털어내는</b>
    /// 놀이라, 조커가 아닌 걸 집었다면 그건 내 손의 것과 같은 숫자여야 이기는 게 말이 된다.
    /// 그래서 숫자와 무늬를 <b>판 시작 때 한 번만</b> 뽑고, 조커가 넘어가도 그 셋이 그대로 손만 바꾼다 -
    /// 판마다 내 카드가 바뀌던 것도 이것 때문이었다.
    ///
    /// ⭐ <b>한 판이 짧고 되풀이된다.</b> 매번 반반이라 평균 두 번쯤 집으면 끝난다 -
    /// 그래서 판돈은 시작할 때 한 번만 걸고 이긴 쪽이 다 가져간다(사용자 확정).
    ///
    /// <b>MonoBehaviour 가 아니다</b> - 규칙은 뷰와 분리해 순수 클래스로 둔다.
    /// 돈도 여기서 옮기지 않는다(IndianPoker · Blackjack 과 같은 방침).
    /// </summary>
    public class OldMaid
    {
        /// <summary>조커를 나타내는 값. 진짜 카드는 1~13 이라 0을 쓴다.</summary>
        public const int Joker = 0;

        /// <summary>손에 드는 장 수. 사용자가 "두 장 중 하나"로 정했다.</summary>
        public const int HandSize = 2;

        /// <summary>무늬 수. 트럼프 한 벌이라 넷이다.</summary>
        public const int SuitCount = 4;

        private readonly Random rng;

        public OldMaid(Random rng = null)
        {
            this.rng = rng ?? new Random();
        }

        public OldMaidPhase Phase { get; private set; } = OldMaidPhase.Idle;

        /// <summary>양쪽이 똑같이 낸 금액. 이긴 쪽이 다 가져간다.</summary>
        public long Bet { get; private set; }

        public OldMaidOutcome Outcome { get; private set; }

        /// <summary><b>지금 조커를 들고 있는 쪽</b>이 플레이어인지. 미는 쪽이 이 사람이다.</summary>
        public bool HolderIsPlayer { get; private set; }

        /// <summary>집는 쪽은 언제나 조커를 든 쪽의 반대다.</summary>
        public bool PickerIsPlayer => !HolderIsPlayer;

        /// <summary>
        /// 이 판에 깔린 <b>짝패의 숫자</b>(1~13). 양쪽이 이 숫자를 한 장씩 들고 있다 -
        /// 그래서 조커가 아닌 걸 집으면 짝이 맞아 이긴다.
        /// </summary>
        public int PairRank { get; private set; }

        // 짝패 두 장의 무늬. 같은 숫자라도 무늬는 달라야 한 벌처럼 보인다.
        private readonly int[] pairSuits = new int[2];

        // 조커를 든 쪽이 쥔 짝패가 위 둘 중 몇 번째인지. 조커가 넘어가면 이게 뒤집힌다.
        private int holderSuitIndex;

        /// <summary>조커가 몇 번째 장인지.</summary>
        public int JokerSlot { get; private set; }

        /// <summary>조커를 든 쪽의 두 장. <b>집는 쪽에게는 뒷면으로 보여야 한다.</b></summary>
        public int CardAt(int slot)
        {
            if (slot < 0 || slot >= HandSize)
                return -1;

            return slot == JokerSlot ? Joker : PairRank;
        }

        /// <summary>그 장의 무늬. 조커는 무늬가 없어 -1 이다.</summary>
        public int SuitAt(int slot)
        {
            if (slot < 0 || slot >= HandSize || slot == JokerSlot)
                return -1;

            return pairSuits[holderSuitIndex];
        }

        /// <summary>
        /// <b>집는 쪽이 들고 있는 한 장</b>. 언제나 <see cref="PairRank"/> 다 -
        /// 조커를 든 쪽의 짝패와 짝이 되는 나머지 한 장이다.
        /// </summary>
        public int PickerCard => PairRank;

        /// <summary>그 한 장의 무늬. 조커를 든 쪽이 쥔 짝패와 반대쪽이다.</summary>
        public int PickerSuit => pairSuits[1 - holderSuitIndex];

        /// <summary>밀어 올린 장. 아직 안 밀었으면 -1.</summary>
        public int OfferedSlot { get; private set; } = -1;

        /// <summary>직전에 집은 장. 공개 연출이 이걸 본다. 아직 없으면 -1.</summary>
        public int PickedSlot { get; private set; } = -1;

        /// <summary>조커가 몇 번 넘어갔는지. 대사와 연출이 "또 넘어갔다"를 아는 데 쓴다.</summary>
        public int PassCount { get; private set; }

        /// <summary>새 판. 조커가 <b>무작위로</b> 한 쪽에게 간다.</summary>
        public void Deal(long bet)
        {
            Bet = bet < 0L ? 0L : bet;
            Outcome = OldMaidOutcome.None;
            PassCount = 0;
            PickedSlot = -1;

            // ⭐ 판에 깔리는 셋을 여기서 한 번만 정한다. 이 뒤로는 손만 바뀐다.
            PairRank = rng.Next(1, 14);
            pairSuits[0] = rng.Next(SuitCount);
            pairSuits[1] = (pairSuits[0] + 1 + rng.Next(SuitCount - 1)) % SuitCount;
            holderSuitIndex = rng.Next(2);

            HolderIsPlayer = rng.Next(2) == 0;
            NewHand();

            Phase = OldMaidPhase.Offer;
        }

        /// <summary>조커를 든 쪽이 한 장을 밀어 올린다. 반드시 한 장이다(사용자 확정).</summary>
        public void Offer(int slot)
        {
            if (Phase != OldMaidPhase.Offer || slot < 0 || slot >= HandSize)
                return;

            OfferedSlot = slot;
            Phase = OldMaidPhase.Pick;
        }

        /// <summary>
        /// 상대가 한 장을 집는다.
        ///
        /// 조커를 집었으면 <b>집은 쪽이 새 주인</b>이 되고 처음부터 되풀이한다 -
        /// 이때 <b>카드가 새로 뽑히지 않는다</b>. 원래 자기가 쥐고 있던 짝패를 그대로 든 채
        /// 조커를 하나 더 받는 것이고, 앞사람에게는 나머지 짝패 한 장이 남는다.
        ///
        /// 짝을 집었으면 짝이 맞아 털어내고 집은 쪽이 이긴다.
        /// </summary>
        public void Pick(int slot)
        {
            if (Phase != OldMaidPhase.Pick || slot < 0 || slot >= HandSize)
                return;

            PickedSlot = slot;
            bool pickerIsPlayer = PickerIsPlayer;
            bool tookJoker = slot == JokerSlot;

            if (tookJoker)
            {
                HolderIsPlayer = pickerIsPlayer;
                holderSuitIndex = 1 - holderSuitIndex;   // 짝패 두 장이 손을 바꿨다
                PassCount++;
                NewHand();
                Phase = OldMaidPhase.Offer;
                return;
            }

            Outcome = pickerIsPlayer ? OldMaidOutcome.PlayerWin : OldMaidOutcome.OpponentWin;
            Phase = OldMaidPhase.Showdown;
        }

        /// <summary>직전에 집은 게 조커였는지. 되풀이 연출이 이걸 본다.</summary>
        public bool LastPickWasJoker =>
            PickedSlot >= 0 && Phase == OldMaidPhase.Offer;

        /// <summary>처음 상태로. 다음 판을 걸 수 있게 된다.</summary>
        public void Reset()
        {
            Phase = OldMaidPhase.Idle;
            Outcome = OldMaidOutcome.None;
            Bet = 0L;
            OfferedSlot = -1;
            PickedSlot = -1;
            PassCount = 0;
        }

        /// <summary>
        /// 조커를 두 장 중 어디에 둘지만 다시 섞는다.
        /// <b>카드를 새로 뽑지 않는다</b> - 판에 깔린 셋은 그대로다.
        /// </summary>
        private void NewHand()
        {
            JokerSlot = rng.Next(HandSize);
            OfferedSlot = -1;
        }
    }
}
