using System;
using UnityEngine;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 예금 상품 하나. <b>레벨이 높을수록 더 오래·더 많이·이자도 세게</b>(2026-09-02 사용자 기획).
    /// </summary>
    [Serializable]
    public class BankPlan
    {
        public string displayName = "짧은 예금";

        [Tooltip("이 레벨부터 고를 수 있다. <b>더 오래 맡기는 상품일수록 높게</b> 둔다.")]
        [Min(1)]
        public int unlockLevel = 1;

        [Tooltip("맡기는 기간(시간). 실제 시각으로 흐른다.")]
        [Min(0.01f)]
        public float hours = 1f;

        [Tooltip("만기에 붙는 이자(%). 기간이 길수록 세다.")]
        [Min(0f)]
        public float interestPercent = 3f;

        [Tooltip("중도에 찾으면 <b>이자는 없고</b> 원금에서 이만큼(%)을 은행이 가져간다.")]
        [Range(0f, 90f)]
        public float earlyFeePercent = 10f;

        [Header("한도 - 레벨에 따라 늘어난다")]
        [Tooltip("골드 한도 = 기본 + 레벨당 x 레벨.")]
        [Min(0)]
        public long maxGold = 3000;

        [Min(0)]
        public long maxGoldPerLevel = 500;

        [Min(0)]
        public int maxGems = 50;

        [Min(0)]
        public int maxGemsPerLevel = 10;

        /// <summary>그 레벨에서 이 상품에 맡길 수 있는 최대치.</summary>
        public long MaxAmount(ShopCurrency currency, int level)
        {
            int lv = Mathf.Max(0, level);

            return currency == ShopCurrency.Gem
                ? maxGems + (long)maxGemsPerLevel * lv
                : maxGold + maxGoldPerLevel * lv;
        }

        public bool IsUnlocked(int level) => level >= unlockLevel;

        /// <summary>만기에 받을 금액. 원금 + 이자(버림).</summary>
        public long Payout(long amount)
            => amount + (long)(amount * (interestPercent / 100f));

        /// <summary>중도에 찾을 때 받을 금액. <b>이자는 없다.</b></summary>
        public long EarlyPayout(long amount)
            => amount - (long)(amount * (earlyFeePercent / 100f));
    }

    /// <summary>
    /// 은행에 걸린 예금 상품들. 값을 코드에 박지 않는다 - 이자·기간·한도는 균형을 보며
    /// 계속 손보게 되는 값이다(<c>ShopCatalog</c> 와 같은 방침).
    /// </summary>
    public class BankPlanCatalog : ScriptableObject
    {
        [SerializeField] private BankPlan[] plans = new BankPlan[0];

        [Header("담보 대출")]
        [Tooltip("전투력 1당 빌릴 수 있는 보석. 전투력 1500 이고 0.2 면 300 보석.")]
        [Min(0f)]
        [SerializeField] private float gemsPerPower = 0.2f;

        [Tooltip("기본 기한(시간). 여기에 빌린 양만큼이 더 붙는다. " +
                 "<b>336 = 이주일</b>(2026-09-03 사용자 지시).")]
        [Min(0f)]
        [SerializeField] private float loanBaseHours = 336f;

        [Tooltip("보석 이만큼마다 기한이 한 시간씩 늘어난다. " +
                 "⚠ <b>가챠 상품 값이 정해진 뒤에 다시 맞추기로 했다</b>(2026-09-03).")]
        [Min(1f)]
        [SerializeField] private float gemsPerLoanHour = 50f;

        [Tooltip("아무리 많이 빌려도 이 시간을 넘지 않는다. 720 = 한 달.")]
        [Min(1f)]
        [SerializeField] private float loanMaxHours = 720f;

        [Tooltip("기한을 넘겨 감옥에 갇힌 캐릭터를 꺼내는 값(빌린 것의 배수).")]
        [Min(1f)]
        [SerializeField] private float rescueMultiplier = 1.5f;

        [Tooltip("갚은 뒤 다시 빌릴 수 있을 때까지의 시간. <b>72 = 사흘</b> - " +
                 "돈을 빌리는 건 장난이 아니라, 갚자마자 또 빌리게 두지 않는다(2026-09-03 사용자 지시).")]
        [Min(0f)]
        [SerializeField] private float loanCooldownHours = 72f;

        public float LoanCooldownHours => loanCooldownHours;

        public float GemsPerPower => gemsPerPower;
        public float LoanBaseHours => loanBaseHours;
        public float GemsPerLoanHour => gemsPerLoanHour;
        public float LoanMaxHours => loanMaxHours;
        public float RescueMultiplier => rescueMultiplier;

        public int Count => plans != null ? plans.Length : 0;

        public BankPlan Get(int index)
            => plans != null && index >= 0 && index < plans.Length ? plans[index] : null;
    }
}
