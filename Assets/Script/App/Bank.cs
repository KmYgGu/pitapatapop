using System;
using System.Collections.Generic;

namespace JojoPuzzle.App
{
    /// <summary>맡겨 둔 돈 한 건.</summary>
    public class BankDeposit
    {
        public ShopCurrency currency;

        /// <summary>맡긴 원금.</summary>
        public long amount;

        /// <summary>어느 상품인지(<see cref="BankPlanCatalog"/> 의 번호).</summary>
        public int planIndex;

        public DateTime maturesAt;

        /// <summary>만기에 받을 금액. <b>맡길 때 정해 둔다</b> - 나중에 상품 값을 손봐도 약속은 그대로다.</summary>
        public long payout;

        /// <summary>중도에 찾으면 받을 금액. 이것도 맡길 때 정해 둔다.</summary>
        public long earlyPayout;

        public bool IsMature(DateTime now) => now >= maturesAt;

        public TimeSpan TimeLeft(DateTime now)
        {
            TimeSpan left = maturesAt - now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// <b>은행</b>(2026-09-02 사용자 기획). 상점의 은행 칸이 쓴다.
    ///
    /// <code>
    ///   맡긴다 → 기간이 흐른다 → 만기에 찾으면 원금 + 이자
    ///                          → 중간에 찾으면 이자 없이 원금에서 수수료를 뗀다
    /// </code>
    ///
    /// ⭐ <b>레벨이 높을수록 더 오래·더 많이·이자도 세다</b> - 상품 자체가 레벨로 열리고
    /// (<see cref="BankPlan.unlockLevel"/>), 한도도 레벨에 따라 늘어난다.
    ///
    /// ⭐ <b>받을 금액은 맡길 때 정해 둔다.</b> 찾을 때 다시 계산하면, 그 사이에 상품 값을
    /// 손봤을 때 <b>이미 맡긴 사람의 약속이 바뀐다</b>.
    ///
    /// <b>화폐마다 한 건씩</b> 맡길 수 있다. 여러 건을 겹치게 하면 화면이 곧 목록 관리가 되고,
    /// "얼마를 얼마나 묶어둘까"라는 결정의 무게도 사라진다.
    ///
    /// <b>⚠ 저장되지 않는다</b>(이 프로젝트의 모든 유저 상태와 같다) - 앱을 껐다 켜면 사라진다.
    /// 시각은 <c>DateTime.UtcNow</c> 로 흐른다(<see cref="HeartMeter"/> 와 같은 방식).
    /// </summary>
    public static class Bank
    {
        private static readonly Dictionary<ShopCurrency, BankDeposit> deposits =
            new Dictionary<ShopCurrency, BankDeposit>();

        /// <summary>맡기거나 찾았다. 화면이 다시 그린다.</summary>
        public static event Action OnChanged;

        /// <summary>그 화폐로 맡겨 둔 것. 없으면 null.</summary>
        public static BankDeposit Get(ShopCurrency currency)
            => deposits.TryGetValue(currency, out var d) ? d : null;

        public static bool Has(ShopCurrency currency) => Get(currency) != null;

        /// <summary>
        /// 맡긴다. <b>돈이 실제로 빠져야</b> 성립한다 - 모자라면 아무 일도 없다.
        /// 이미 그 화폐로 맡긴 게 있으면 거절한다.
        /// </summary>
        public static bool TryDeposit(BankPlan plan, int planIndex, ShopCurrency currency,
                                      long amount, int level, DateTime now)
        {
            if (plan == null || amount <= 0L)
                return false;

            if (!plan.IsUnlocked(level) || Has(currency))
                return false;

            if (amount > plan.MaxAmount(currency, level))
                return false;

            bool paid = currency == ShopCurrency.Gem
                ? PlayerProfile.TrySpendGems((int)amount)
                : PlayerProfile.TrySpendGold(amount);

            if (!paid)
                return false;

            deposits[currency] = new BankDeposit
            {
                currency = currency,
                amount = amount,
                planIndex = planIndex,
                maturesAt = now.AddHours(plan.hours),
                payout = plan.Payout(amount),
                earlyPayout = plan.EarlyPayout(amount),
            };

            OnChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 만기가 된 것을 찾는다. <b>아직 안 됐으면 아무 일도 없다</b> -
        /// 중간에 찾는 건 <see cref="Cancel"/> 로 뜻을 분명히 해야 한다.
        /// </summary>
        public static bool TryClaim(ShopCurrency currency, DateTime now)
        {
            var deposit = Get(currency);
            if (deposit == null || !deposit.IsMature(now))
                return false;

            Pay(currency, deposit.payout);
            deposits.Remove(currency);

            OnChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 중간에 포기하고 찾는다. <b>이자는 없고 원금에서 수수료를 뗀다</b>(사용자 기획).
        /// 만기가 지난 것에 부르면 그냥 만기 처리한다 - 다 기다린 사람이 손해 볼 이유가 없다.
        /// </summary>
        public static bool Cancel(ShopCurrency currency, DateTime now)
        {
            var deposit = Get(currency);
            if (deposit == null)
                return false;

            if (deposit.IsMature(now))
                return TryClaim(currency, now);

            Pay(currency, deposit.earlyPayout);
            deposits.Remove(currency);

            OnChanged?.Invoke();
            return true;
        }

        private static void Pay(ShopCurrency currency, long amount)
        {
            if (amount <= 0L)
                return;

            if (currency == ShopCurrency.Gem)
                PlayerProfile.Gems += (int)amount;
            else
                PlayerProfile.Gold += amount;
        }

        public static void Clear()
        {
            if (deposits.Count == 0)
                return;

            deposits.Clear();
            OnChanged?.Invoke();
        }
    }
}
