using System;
using JojoPuzzle.App;
using UnityEngine;
using UnityEngine.UI;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 상점의 <b>은행</b> 칸(2026-09-02 사용자 기획).
    ///
    /// <code>
    ///   화폐를 고르고 → 금액을 정하고 → 상품(기간)을 골라 맡긴다
    ///   만기      → '찾기'로 원금 + 이자
    ///   중간에    → '중도 해지'로 이자 없이 원금에서 수수료를 뗀 만큼
    /// </code>
    ///
    /// ⭐ <b>레벨이 높을수록 더 오래·더 많이·이자도 세다</b> - 상품이 레벨로 열리고 한도도
    /// 레벨에 따라 늘어난다. 아직 못 여는 상품도 <b>숨기지 않고</b> "Lv.20 부터"로 보여준다 -
    /// 무엇을 향해 올리는지가 보여야 레벨이 목표가 된다.
    ///
    /// <b>규칙은 <see cref="Bank"/> 가 갖는다.</b> 여기는 보여주고 누른 것을 넘길 뿐이다.
    /// </summary>
    public class BankView : MonoBehaviour
    {
        [Header("껐다 켜는 곳")]
        [Tooltip("은행 칸을 골랐을 때만 켜지는 뿌리.")]
        [SerializeField] private GameObject root;

        [Header("예금 / 감옥")]
        [SerializeField] private Button depositTabButton;
        [SerializeField] private Button loanTabButton;
        [SerializeField] private GameObject depositRoot;
        [SerializeField] private GameObject loanRoot;

        [Header("상태")]
        [SerializeField] private Text statusText;
        [SerializeField] private Text noticeText;

        [Header("화폐")]
        [SerializeField] private Button goldButton;
        [SerializeField] private Button gemButton;
        [SerializeField] private Color pickedColor = new Color(0.30f, 0.26f, 0.42f, 1f);
        [SerializeField] private Color plainColor = new Color(0.16f, 0.15f, 0.20f, 1f);

        [Header("금액")]
        [SerializeField] private Text amountText;
        [SerializeField] private Button minusButton;
        [SerializeField] private Button plusButton;
        [SerializeField] private Button maxButton;

        [Tooltip("한 번에 오르내리는 폭. 골드와 보석은 자릿수가 달라 따로 둔다.")]
        [Min(1)]
        [SerializeField] private long goldStep = 1000;

        [Min(1)]
        [SerializeField] private int gemStep = 10;

        [Header("상품")]
        [SerializeField] private BankPlanCatalog plans;

        [Tooltip("상품 줄의 본. 꺼진 채로 두면 복제해 쌓는다.")]
        [SerializeField] private RectTransform planTemplate;

        [SerializeField] private RectTransform planListRoot;
        [SerializeField] private float planRowHeight = 52f;
        [SerializeField] private float planRowGap = 5f;

        [Header("찾기")]
        [SerializeField] private Button claimButton;
        [SerializeField] private Button cancelButton;

        [Header("담보 대출")]
        [Tooltip("가진 캐릭터 목록. 여기서 <b>가장 강한 하나</b>만 담보가 될 수 있다.")]
        [SerializeField] private JojoPuzzle.Core.CharacterRoster roster;

        [SerializeField] private Text loanStatusText;
        [SerializeField] private Text loanNameText;
        [SerializeField] private Text loanDetailText;
        [Tooltip("갇힌 캐릭터를 그릴 자리. 스파인이 없을 때만 이 그림이 쓰인다.")]
        [SerializeField] private Image loanPortrait;

        [Tooltip("갇힌 캐릭터의 스파인. <b>우는 동작은 아직 없어 idle 을 쓴다</b>(2026-09-03).")]
        [SerializeField] private JojoPuzzle.UI.SpineCharacterView loanSpine;

        [Tooltip("쇠창살. <b>캐릭터가 감옥에 있는 동안</b> 켜진다 - 갚기 전까지, " +
                 "그리고 갚은 뒤 쿨타임 동안에도(그때는 빈 감옥이다).")]
        [SerializeField] private GameObject jailBars;
        [SerializeField] private Button borrowButton;
        [SerializeField] private Text borrowLabel;
        [SerializeField] private Button repayButton;
        [SerializeField] private Text repayLabel;

        [Header("경고창")]
        [Tooltip("빌리기 전에 뜨는 확인창. <b>가벼운 활동이 아니라</b> 무엇을 잃을 수 있는지 " +
                 "다 적어 보여주고 받는다(2026-09-03 사용자 지시).")]
        [SerializeField] private GameObject warningRoot;

        [SerializeField] private Text warningText;
        [SerializeField] private Button warningConfirmButton;
        [SerializeField] private Button warningCancelButton;

        private ShopCurrency currency = ShopCurrency.Gold;
        private long amount;

        private readonly System.Collections.Generic.List<RectTransform> rows =
            new System.Collections.Generic.List<RectTransform>();

        private void Awake()
        {
            if (goldButton != null)
                goldButton.onClick.AddListener(() => PickCurrency(ShopCurrency.Gold));

            if (gemButton != null)
                gemButton.onClick.AddListener(() => PickCurrency(ShopCurrency.Gem));

            if (minusButton != null)
                minusButton.onClick.AddListener(() => Step(-1));

            if (plusButton != null)
                plusButton.onClick.AddListener(() => Step(1));

            if (maxButton != null)
                maxButton.onClick.AddListener(SetMax);

            if (claimButton != null)
                claimButton.onClick.AddListener(Claim);

            if (cancelButton != null)
                cancelButton.onClick.AddListener(CancelDeposit);

            if (depositTabButton != null)
                depositTabButton.onClick.AddListener(() => ShowLoan(false));

            if (loanTabButton != null)
                loanTabButton.onClick.AddListener(() => ShowLoan(true));

            if (borrowButton != null)
                borrowButton.onClick.AddListener(AskBorrow);

            if (warningConfirmButton != null)
                warningConfirmButton.onClick.AddListener(Borrow);

            if (warningCancelButton != null)
                warningCancelButton.onClick.AddListener(() => warningRoot?.SetActive(false));

            warningRoot?.SetActive(false);

            if (repayButton != null)
                repayButton.onClick.AddListener(Repay);

            if (planTemplate != null)
                planTemplate.gameObject.SetActive(false);

            root?.SetActive(false);
        }

        private void OnEnable()
        {
            Bank.OnChanged += Refresh;
            BankLoan.OnChanged += Refresh;
        }

        private void OnDisable()
        {
            Bank.OnChanged -= Refresh;
            BankLoan.OnChanged -= Refresh;
        }

        /// <summary>은행 칸을 골랐는지에 따라 켜고 끈다.</summary>
        public void SetVisible(bool visible)
        {
            if (root == null)
                return;

            root.SetActive(visible);

            if (visible)
            {
                // 화면이 열릴 때 기한을 따진다 - 시계를 도는 물건을 따로 두지 않는다.
                BankLoan.Tick(DateTime.UtcNow);

                amount = DefaultAmount();
                ShowLoan(loaning);
            }
        }

        public bool IsVisible => root != null && root.activeSelf;

        /// <summary>남은 시간이 흐르는 게 보여야 한다 - 켜져 있을 때만 상태 줄을 다시 쓴다.</summary>
        private void Update()
        {
            if (!IsVisible)
                return;

            BankLoan.Tick(DateTime.UtcNow);

            if (loaning)
                RefreshLoan();
            else
                RefreshStatus();
        }

        // ---------------------------------------------------------------- 예금 / 감옥

        private bool loaning;

        private void ShowLoan(bool on)
        {
            loaning = on;

            depositRoot?.SetActive(!on);
            loanRoot?.SetActive(on);
            warningRoot?.SetActive(false);

            Paint(depositTabButton, !on);
            Paint(loanTabButton, on);

            if (on)
                RefreshLoan();
            else
                Refresh();
        }

        /// <summary>
        /// 감옥 칸. <b>담보로 잡을 수 있는 건 가장 강한 캐릭터 하나뿐</b>이다 -
        /// 아무나 맡길 수 있으면 약한 캐릭터를 마구 맡기고 안 갚는다(2026-09-02 사용자 기획).
        /// </summary>
        private void RefreshLoan()
        {
            DateTime now = DateTime.UtcNow;
            float rescue = plans != null ? plans.RescueMultiplier : 1.5f;

            var held = BankLoan.Collateral;
            var candidate = held ?? BankLoan.FindStrongest(
                roster != null ? roster.ownedCharacters : null);

            // ⭐ 쿨타임 동안에는 <b>빈 감옥만</b> 보인다(2026-09-03 사용자 지시) -
            // 갚고 나온 캐릭터의 정보를 계속 띄워두면 아직 갇혀 있는 것처럼 읽힌다.
            TimeSpan cooling = BankLoan.Cooldown(now,
                plans != null ? plans.LoanCooldownHours : 0f);
            bool emptyCell = held == null && cooling > TimeSpan.Zero;

            // 쇠창살은 <b>감옥을 쓰는 동안</b> 내내 있다 - 갇혀 있을 때도, 갚고 비어 있을 때도.
            if (jailBars != null)
                jailBars.SetActive(held != null || emptyCell);

            var skeleton = !emptyCell && candidate != null && candidate.speech != null
                ? candidate.speech.spine : null;

            if (loanSpine != null)
            {
                if (skeleton != null)
                    loanSpine.Show(skeleton);
                else
                    loanSpine.Clear();
            }

            if (loanPortrait != null)
            {
                // 스파인으로 그렸으면 그림은 비운다 - 둘이 겹쳐 보이면 안 된다.
                bool useSprite = !emptyCell && (skeleton == null || loanSpine == null);
                loanPortrait.sprite = useSprite && candidate != null ? candidate.icon : null;
                loanPortrait.enabled = loanPortrait.sprite != null;
            }

            if (loanNameText != null)
                loanNameText.text = emptyCell ? string.Empty
                    : (candidate != null ? candidate.DisplayName : "맡길 캐릭터가 없습니다");

            if (held == null)
            {
                int max = BankLoan.MaxLoan(candidate, plans != null ? plans.GemsPerPower : 0.2f);

                if (loanStatusText != null)
                    loanStatusText.text = candidate == null
                        ? string.Empty
                        : "가장 강한 캐릭터만 맡길 수 있습니다";

                if (loanDetailText != null)
                    loanDetailText.text = candidate == null || emptyCell
                        ? string.Empty
                        : $"전투력 {candidate.CombatPower:N0}\n빌릴 수 있는 보석 {max:N0}";

                if (borrowLabel != null)
                    borrowLabel.text = max > 0 ? $"보석 {max:N0} 빌리기" : "빌리기";

                // 갚은 뒤 한동안은 다시 못 빌린다 - 왜 못 누르는지 상태 줄이 말해 준다.
                if (emptyCell && loanStatusText != null)
                    loanStatusText.text = $"{Remaining(cooling)} 뒤에 다시 빌릴 수 있습니다";

                if (loanDetailText != null && emptyCell)
                    loanDetailText.text = string.Empty;

                if (borrowButton != null)
                {
                    borrowButton.gameObject.SetActive(true);
                    borrowButton.interactable = max > 0 && !emptyCell;
                }

                repayButton?.gameObject.SetActive(false);
                return;
            }

            int due = BankLoan.AmountDue(rescue);

            if (loanStatusText != null)
                loanStatusText.text = BankLoan.IsSeized
                    ? "기한이 지났습니다 - 감옥에 갇혔습니다"
                    : $"{Remaining(BankLoan.TimeLeft(now))} 안에 갚아야 합니다";

            if (loanDetailText != null)
                loanDetailText.text = BankLoan.IsSeized
                    ? $"엉엉 울고 있습니다\n빌린 보석 {BankLoan.Borrowed:N0} · 구출 {due:N0}"
                    : $"빌린 보석 {BankLoan.Borrowed:N0}\n갚을 보석 {due:N0}";

            if (repayLabel != null)
                repayLabel.text = BankLoan.IsSeized ? $"구출 {due:N0}" : $"갚기 {due:N0}";

            borrowButton?.gameObject.SetActive(false);

            if (repayButton != null)
            {
                repayButton.gameObject.SetActive(true);
                repayButton.interactable = PlayerProfile.Gems >= due;
            }
        }

        /// <summary>
        /// ⭐ <b>바로 빌리지 않는다.</b> 무엇을 잃을 수 있는지 다 적어 보여주고, 받아들여야 빌린다
        /// (2026-09-03 사용자 지시: "이건 가벼운 활동이 아니니").
        /// </summary>
        private void AskBorrow()
        {
            var candidate = BankLoan.FindStrongest(roster != null ? roster.ownedCharacters : null);
            if (candidate == null || plans == null || warningRoot == null)
            {
                Borrow();
                return;
            }

            int gems = BankLoan.MaxLoan(candidate, plans.GemsPerPower);
            float hours = BankLoan.TermHours(gems, plans.LoanBaseHours,
                                             plans.GemsPerLoanHour, plans.LoanMaxHours);
            int rescue = (int)Math.Ceiling(gems * (double)plans.RescueMultiplier);

            if (warningText != null)
            {
                warningText.text = string.Join("\n", new[]
                {
                    $"{candidate.DisplayName} 을(를) 맡기고 보석 {gems:N0} 을 빌립니다.",
                    "",
                    $"· 전투 · 편성 · 아파트 어디에서도 쓸 수 없습니다",
                    $"· 아파트에서 사라지고 은행 감옥에만 있습니다",
                    $"· {DaysAndHours(hours)} 안에 보석 {gems:N0} 을 갚아야 합니다",
                    $"· 못 갚으면 영영 갇히고, 꺼내는 데 보석 {rescue:N0}",
                    $"· 갚은 뒤 {DaysAndHours(plans.LoanCooldownHours)} 동안 다시 못 빌립니다",
                });
            }

            warningRoot.SetActive(true);
        }

        /// <summary>"14일" · "3일" · "5시간" 처럼 사람이 읽는 길이로.</summary>
        private static string DaysAndHours(float hours)
        {
            int days = (int)(hours / 24f);
            int rest = (int)(hours - days * 24f);

            if (days <= 0)
                return $"{rest}시간";

            return rest > 0 ? $"{days}일 {rest}시간" : $"{days}일";
        }

        private void Borrow()
        {
            warningRoot?.SetActive(false);

            var candidate = BankLoan.FindStrongest(roster != null ? roster.ownedCharacters : null);
            if (candidate == null || plans == null)
                return;

            int gems = BankLoan.MaxLoan(candidate, plans.GemsPerPower);
            float hours = BankLoan.TermHours(gems, plans.LoanBaseHours,
                                             plans.GemsPerLoanHour, plans.LoanMaxHours);

            if (BankLoan.TryBorrow(candidate, gems, hours, DateTime.UtcNow))
            {
                Notice($"{candidate.DisplayName} 을 맡기고 보석 {gems:N0} 을 빌렸습니다");
                RefreshLoan();
            }
        }

        private void Repay()
        {
            float rescue = plans != null ? plans.RescueMultiplier : 1.5f;
            bool wasSeized = BankLoan.IsSeized;
            var who = BankLoan.Collateral;

            if (BankLoan.TryRepay(rescue))
            {
                Notice(wasSeized
                    ? $"{(who != null ? who.DisplayName : "캐릭터")} 을 구해 냈습니다"
                    : "갚았습니다");
                RefreshLoan();
                return;
            }

            Notice("보석이 모자랍니다");
        }

        // ---------------------------------------------------------------- 고르기

        private void PickCurrency(ShopCurrency which)
        {
            currency = which;
            amount = DefaultAmount();
            Refresh();
        }

        private long Step(int direction)
        {
            long step = currency == ShopCurrency.Gem ? gemStep : goldStep;
            amount = Math.Max(step, amount + step * direction);
            amount = Math.Min(amount, Ceiling());

            Refresh();
            return amount;
        }

        private void SetMax()
        {
            amount = Math.Max(1L, Ceiling());
            Refresh();
        }

        private long DefaultAmount()
        {
            long step = currency == ShopCurrency.Gem ? gemStep : goldStep;
            return Math.Min(step, Math.Max(1L, Ceiling()));
        }

        /// <summary>지금 넣을 수 있는 최대 - <b>가진 돈</b>과 <b>열린 상품 중 가장 큰 한도</b> 중 작은 쪽.</summary>
        private long Ceiling()
        {
            long held = currency == ShopCurrency.Gem ? PlayerProfile.Gems : PlayerProfile.Gold;
            long best = 0L;

            if (plans != null)
            {
                for (int i = 0; i < plans.Count; i++)
                {
                    var plan = plans.Get(i);
                    if (plan != null && plan.IsUnlocked(PlayerProfile.Level))
                        best = Math.Max(best, plan.MaxAmount(currency, PlayerProfile.Level));
                }
            }

            return Math.Min(held, best);
        }

        // ---------------------------------------------------------------- 그리기

        public void Refresh()
        {
            RefreshStatus();
            RefreshCurrency();
            RefreshPlans();
        }

        private void RefreshStatus()
        {
            var deposit = Bank.Get(currency);
            DateTime now = DateTime.UtcNow;

            if (statusText != null)
            {
                statusText.text = deposit == null
                    ? "맡겨 둔 것이 없습니다"
                    : deposit.IsMature(now)
                        ? $"{Unit()} {deposit.amount:N0} · 만기! 받을 돈 {deposit.payout:N0}"
                        : $"{Unit()} {deposit.amount:N0} · {Remaining(deposit.TimeLeft(now))} 남음"
                          + $" · 만기 {deposit.payout:N0}";
            }

            bool mature = deposit != null && deposit.IsMature(now);

            if (claimButton != null)
            {
                claimButton.gameObject.SetActive(deposit != null);
                claimButton.interactable = mature;
            }

            if (cancelButton != null)
                cancelButton.gameObject.SetActive(deposit != null && !mature);
        }

        /// <summary>남은 시간. 한 시간이 넘으면 분까지만 - 초까지 보이면 눈이 그것만 좇는다.</summary>
        private static string Remaining(TimeSpan left)
        {
            // 대출 기한은 <b>주 단위</b>라 시간으로만 적으면 "336시간"처럼 읽히지 않는다.
            if (left.TotalDays >= 1d)
                return $"{(int)left.TotalDays}일 {left.Hours}시간";

            if (left.TotalHours >= 1d)
                return $"{(int)left.TotalHours}시간 {left.Minutes}분";

            if (left.TotalMinutes >= 1d)
                return $"{left.Minutes}분 {left.Seconds}초";

            return $"{left.Seconds}초";
        }

        private string Unit() => currency == ShopCurrency.Gem ? "보석" : "골드";

        private void RefreshCurrency()
        {
            Paint(goldButton, currency == ShopCurrency.Gold);
            Paint(gemButton, currency == ShopCurrency.Gem);

            amount = Math.Min(Math.Max(1L, amount), Math.Max(1L, Ceiling()));

            if (amountText != null)
                amountText.text = $"{Unit()} {amount:N0}";

            bool free = Bank.Get(currency) == null;

            if (minusButton != null) minusButton.interactable = free;
            if (plusButton != null) plusButton.interactable = free;
            if (maxButton != null) maxButton.interactable = free;
        }

        private void Paint(Button button, bool on)
        {
            if (button == null)
                return;

            var image = button.targetGraphic as Image;
            if (image != null)
                image.color = on ? pickedColor : plainColor;
        }

        private void RefreshPlans()
        {
            if (planTemplate == null || planListRoot == null || plans == null)
                return;

            while (rows.Count < plans.Count)
            {
                var row = Instantiate(planTemplate, planListRoot);
                row.name = "Plan" + rows.Count;
                rows.Add(row);
            }

            bool busy = Bank.Get(currency) != null;
            int level = PlayerProfile.Level;

            for (int i = 0; i < rows.Count; i++)
            {
                var plan = plans.Get(i);
                rows[i].gameObject.SetActive(plan != null);

                if (plan == null)
                    continue;

                rows[i].anchoredPosition = new Vector2(0f, -i * (planRowHeight + planRowGap));

                bool open = plan.IsUnlocked(level);
                long max = plan.MaxAmount(currency, level);

                SetText(rows[i], "TitleText", plan.displayName);
                SetText(rows[i], "BodyText", open
                    ? $"{Hours(plan.hours)} · 이자 {plan.interestPercent:0.#}% · 중도 -{plan.earlyFeePercent:0.#}%"
                    : $"Lv.{plan.unlockLevel} 부터");
                SetText(rows[i], "PriceText", open ? $"한도 {max:N0}" : string.Empty);

                var put = Find<Button>(rows[i], "BuyButton");
                if (put == null)
                    continue;

                put.onClick.RemoveAllListeners();
                put.interactable = open && !busy && amount > 0L && amount <= max;

                int index = i;
                var target = plan;
                put.onClick.AddListener(() => Deposit(target, index));
            }
        }

        private static string Hours(float hours)
            => hours >= 1f ? $"{hours:0.#}시간" : $"{Mathf.RoundToInt(hours * 60f)}분";

        // ---------------------------------------------------------------- 누름

        private void Deposit(BankPlan plan, int index)
        {
            if (Bank.TryDeposit(plan, index, currency, amount, PlayerProfile.Level, DateTime.UtcNow))
            {
                Notice($"{Unit()} {amount:N0} 을 맡겼습니다");
                Refresh();
                return;
            }

            Notice("맡길 수 없습니다 - 한도나 가진 돈을 확인하세요");
        }

        private void Claim()
        {
            if (Bank.TryClaim(currency, DateTime.UtcNow))
            {
                Notice("찾았습니다");
                Refresh();
                return;
            }

            Notice("아직 만기가 아닙니다");
        }

        private void CancelDeposit()
        {
            var deposit = Bank.Get(currency);
            if (deposit == null)
                return;

            // 얼마를 손해 보는지 <b>누르기 전에</b> 알 수 있어야 하지만, 창을 하나 더 띄우면
            // 은행 칸이 무거워진다. 대신 결과를 정확한 숫자로 알린다.
            long lost = deposit.amount - deposit.earlyPayout;

            if (Bank.Cancel(currency, DateTime.UtcNow))
            {
                Notice($"중도 해지 - 수수료 {lost:N0} 을 떼고 돌려받았습니다");
                Refresh();
            }
        }

        private void Notice(string text)
        {
            if (noticeText == null)
                return;

            noticeText.text = text;
            noticeText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

}
}
