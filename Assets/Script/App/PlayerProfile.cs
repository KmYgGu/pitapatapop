namespace JojoPuzzle.App
{
    /// <summary>
    /// 메인 화면에 표시되는 플레이어 상태. <b>지금은 전부 임시값이고 저장되지 않는다.</b>
    ///
    /// <b>왜 임시인가</b>: 로그인이 있는 이상 세이브는 서버에 있어야 한다는 게 기존 방침이라,
    /// 여기에 로컬 저장을 붙이면 나중에 갈아엎게 된다. 지금은 화면을 만들기 위한 값만 들고 있고,
    /// 세이브 계층이 생기면 <b>이 클래스가 그 값을 받아오는 창구</b>가 되면 된다
    /// (화면 코드는 이미 여기만 보고 있으므로 안 고쳐도 된다).
    ///
    /// <b>여기에 게임 규칙을 넣지 말 것.</b> 하트 충전처럼 규칙이 있는 건 별도 클래스
    /// (<see cref="HeartMeter"/>)에 두고 여기서는 들고만 있는다.
    /// </summary>
    public static class PlayerProfile
    {
        /// <summary>플레이어 레벨. 캐릭터 레벨(PanelType)과는 다른 값이다.</summary>
        public static int Level { get; set; } = 12;

        /// <summary>현재 레벨에서 모은 경험치.</summary>
        public static int Exp { get; set; } = 1360;

        /// <summary>다음 레벨까지 필요한 경험치. 0이면 게이지를 0%로 그린다.</summary>
        public static int ExpToNextLevel { get; set; } = 2000;

        /// <summary>
        /// ⭐ <b>재화가 바뀌었다.</b> 표시줄과 상점이 이걸 듣는다.
        ///
        /// 없을 때는 돈을 건드린 쪽이 <b>화면마다 찾아가 다시 그려 달라고</b> 해야 했는데,
        /// 은행에서 보석을 빌렸을 때처럼 <b>한 곳을 빠뜨리면 숫자가 옛것으로 남는다</b>
        /// (2026-09-03 사용자 신고). 값이 바뀌는 자리는 여기 하나뿐이니 여기서 알리는 게 맞다.
        /// </summary>
        public static event System.Action OnCurrencyChanged;

        private static long gold = 12340;

        /// <summary>보유 골드.</summary>
        public static long Gold
        {
            get => gold;
            set
            {
                if (gold == value)
                    return;

                gold = value;
                OnCurrencyChanged?.Invoke();
            }
        }

        /// <summary>
        /// <b>보석</b> - 뽑기와 상점에 함께 쓰는 재화(2026-09-02 사용자 확정).
        /// 상단 표시줄의 <b>보라색 칸</b>이 이것이다.
        ///
        /// ⚠ <b>따로 만들지 말 것.</b> 한때 상점용으로 <c>Gems</c> 를 새로 만들고
        /// 뽑기 재화(<c>GachaTickets</c>)를 그대로 뒀다가 재화가 둘로 갈렸다 -
        /// 화면에는 칸이 하나뿐이라 어느 쪽이 보이는지도 알 수 없었다.
        ///
        /// 골드와는 나뉜다: 골드는 <b>놀아서 버는 돈</b>(배틀 보상·도박)이라 값비싼 물건까지
        /// 골드로 팔면 벌이의 균형이 곧바로 무너진다.
        /// </summary>
        public static int Gems
        {
            get => gems;
            set
            {
                if (gems == value)
                    return;

                gems = value;
                OnCurrencyChanged?.Invoke();
            }
        }

        private static int gems = 2500;

        /// <summary>
        /// 골드를 낸다. <b>모자라면 아무것도 깎지 않고</b> false - 반쯤 깎인 상태를 남기면
        /// 부르는 쪽마다 되돌리는 코드를 갖게 된다.
        /// </summary>
        public static bool TrySpendGold(long amount)
        {
            if (amount < 0L || Gold < amount)
                return false;

            Gold -= amount;
            return true;
        }

        /// <summary>보석을 낸다. 모자라면 false.</summary>
        public static bool TrySpendGems(int amount)
        {
            if (amount < 0 || Gems < amount)
                return false;

            Gems -= amount;
            return true;
        }

        /// <summary>스테이지 입장에 쓰는 하트. 규칙은 <see cref="HeartMeter"/>에 있다.</summary>
        public static HeartMeter Hearts { get; } = new HeartMeter();

        /// <summary>경험치 게이지에 쓸 0~1 비율.</summary>
        public static float ExpFraction
        {
            get
            {
                if (ExpToNextLevel <= 0)
                    return 0f;

                float f = (float)Exp / ExpToNextLevel;
                return f < 0f ? 0f : (f > 1f ? 1f : f);
            }
        }
    }
}
