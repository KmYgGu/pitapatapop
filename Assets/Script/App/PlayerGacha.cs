using System.Collections.Generic;
using JojoPuzzle.Core;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 가챠에서 <b>플레이어가 쌓아 온 것</b> - 포인트, 교환권, 배너별 진행도.
    ///
    /// <code>
    ///   한 번 뽑을 때마다 1포인트   (골드 가챠만 안 준다)
    ///   50포인트  → GR 확정권      (픽업이 나올 수도 있다)
    ///   100포인트 → 픽업 교환권
    /// </code>
    ///
    /// ⭐⭐ <b>포인트는 배너끼리 합쳐서 쌓인다</b>(2026-09-03 사용자 확정: "서로 중첩이 가능해").
    /// 배너마다 따로 세면 이 배너에서 49점, 저 배너에서 49점을 모아 놓고도 아무것도 못 바꾸게 된다.
    /// 그래서 <b>주머니는 하나</b>다.
    ///
    /// ⚠ 아직 저장하지 않는다 - 세이브가 붙는 날 이 클래스만 고치면 되도록 한곳에 모아 뒀다.
    /// </summary>
    public static class PlayerGacha
    {
        /// <summary>GR 확정권 한 장의 값.</summary>
        public const int GuaranteedGrCost = 50;

        /// <summary>픽업 교환권 한 장의 값.</summary>
        public const int PickupTicketCost = 100;

        /// <summary>포인트·교환권·진행도가 달라졌다.</summary>
        public static event System.Action OnChanged;

        /// <summary>모아 둔 포인트. <b>모든 배너가 같이 쓴다</b>(골드 가챠만 안 준다).</summary>
        public static int Points { get; private set; }

        /// <summary>바꿔 둔 GR 확정권.</summary>
        public static int GuaranteedGrTickets { get; private set; }

        /// <summary>바꿔 둔 픽업 교환권.</summary>
        public static int PickupTickets { get; private set; }

        // 배너별 스텝업 진행도. 이름표로 기억한다 - 배너 애셋이 바뀌어도 진행도가 안 섞인다.
        private static readonly Dictionary<string, int> steps = new Dictionary<string, int>();

        // 박스 가챠가 지금까지 꺼낸 등급들(배너별). "뽑은 걸 상자에서 뺀다"를 이걸로 셈한다.
        private static readonly Dictionary<string, List<CharacterGrade>> boxDrawn
            = new Dictionary<string, List<CharacterGrade>>();

        /// <summary>
        /// 뽑은 만큼 포인트를 준다. <b>배너가 포인트를 주는 배너일 때만</b>.
        /// </summary>
        public static void AddPoints(GachaBanner banner, int pulls)
        {
            if (banner == null || !banner.givesPoints || pulls <= 0)
                return;

            Points += pulls;
            OnChanged?.Invoke();
        }

        /// <summary>
        /// 프라이즈 룰렛처럼 <b>뽑은 횟수와 상관없이</b> 얹는 포인트.
        /// 배너가 포인트를 안 주는 배너면 이것도 안 준다.
        /// </summary>
        public static void AddBonusPoints(GachaBanner banner, int amount)
        {
            if (banner == null || !banner.givesPoints || amount <= 0)
                return;

            Points += amount;
            OnChanged?.Invoke();
        }

        public static bool CanExchangeGuaranteedGr => Points >= GuaranteedGrCost;

        public static bool CanExchangePickup => Points >= PickupTicketCost;

        /// <summary>포인트를 GR 확정권으로. 모자라면 아무 일도 없다.</summary>
        public static bool ExchangeGuaranteedGr()
        {
            if (!CanExchangeGuaranteedGr)
                return false;

            Points -= GuaranteedGrCost;
            GuaranteedGrTickets++;
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>포인트를 픽업 교환권으로. 모자라면 아무 일도 없다.</summary>
        public static bool ExchangePickup()
        {
            if (!CanExchangePickup)
                return false;

            Points -= PickupTicketCost;
            PickupTickets++;
            OnChanged?.Invoke();
            return true;
        }

        // ---------------------------------------------------------------- 스텝업

        /// <summary>그 배너의 지금 단계(0부터). 스텝업이 아니면 늘 0.</summary>
        public static int StepOf(GachaBanner banner)
        {
            if (banner == null || !banner.IsStepUp || string.IsNullOrEmpty(banner.bannerId))
                return 0;

            return steps.TryGetValue(banner.bannerId, out int step) ? step : 0;
        }

        /// <summary>
        /// 한 단계 올린다. <b>마지막 단계에서는 그 자리에 머문다</b> -
        /// 넘겨 버리면 마지막 단계를 다시 뽑을 길이 없어진다.
        /// 다시 처음부터 돌릴지는 기획이 정할 일이라 여기서 감지 않는다.
        /// </summary>
        public static void AdvanceStep(GachaBanner banner)
        {
            if (banner == null || !banner.IsStepUp || string.IsNullOrEmpty(banner.bannerId))
                return;

            int next = StepOf(banner) + 1;
            steps[banner.bannerId] = UnityEngine.Mathf.Min(next, banner.StepCount - 1);
            OnChanged?.Invoke();
        }

        // ---------------------------------------------------------------- 박스

        /// <summary>박스 가챠가 지금까지 꺼낸 등급들. 확률을 다시 잡을 때 쓴다.</summary>
        public static IReadOnlyList<CharacterGrade> BoxDrawn(GachaBanner banner)
        {
            if (banner == null || string.IsNullOrEmpty(banner.bannerId))
                return System.Array.Empty<CharacterGrade>();

            return boxDrawn.TryGetValue(banner.bannerId, out var list)
                ? (IReadOnlyList<CharacterGrade>)list
                : System.Array.Empty<CharacterGrade>();
        }

        /// <summary>박스에서 하나 꺼냈다고 적어 둔다.</summary>
        public static void RecordBoxDraw(GachaBanner banner, CharacterGrade grade)
        {
            if (banner == null || banner.kind != GachaBannerKind.Box
                || string.IsNullOrEmpty(banner.bannerId))
            {
                return;
            }

            if (!boxDrawn.TryGetValue(banner.bannerId, out var list))
            {
                list = new List<CharacterGrade>();
                boxDrawn[banner.bannerId] = list;
            }

            list.Add(grade);
            OnChanged?.Invoke();
        }

        /// <summary>상자를 새로 채운다(전부 꺼냈을 때).</summary>
        public static void ResetBox(GachaBanner banner)
        {
            if (banner == null || string.IsNullOrEmpty(banner.bannerId))
                return;

            if (boxDrawn.Remove(banner.bannerId))
                OnChanged?.Invoke();
        }

        /// <summary>테스트용으로 포인트를 넣는다. 세이브가 붙기 전까지만 쓴다.</summary>
        public static void GrantPointsForTesting(int amount)
        {
            if (amount <= 0)
                return;

            Points += amount;
            OnChanged?.Invoke();
        }
    }
}
