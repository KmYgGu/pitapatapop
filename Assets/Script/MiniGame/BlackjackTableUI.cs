using System;
using JojoPuzzle.Core;
using UnityEngine;
using UnityEngine.UI;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 블랙잭의 <b>화면</b>. 규칙도 돈도 모르고, 시키는 대로 보여주고 눌린 것만 알린다
    /// (포커 화면과 같은 방침, 2026-09-02).
    ///
    /// <b>양쪽 패가 다 보인다</b> - 딜러가 없는 규칙이라 감출 이유가 없고, 상대 숫자가 보여야
    /// "저기서 멈췄으니 나는 얼마를 만들어야 한다"는 판단이 선다.
    ///
    /// <b>카드 그림은 여기 없다</b> - 테이블 위 3D 오브젝트라 <see cref="BlackjackCardBoard"/>
    /// 가 따로 놓는다. 이 화면은 합계·판돈·안내·버튼만 맡는다.
    /// </summary>
    public class BlackjackTableUI : MonoBehaviour
    {
        [Tooltip("블랙잭 화면 전체를 껐다 켜는 뿌리. 이 컴포넌트는 <b>항상 켜져 있는</b> 바깥에 붙는다.")]
        [SerializeField] private GameObject root;

        [Header("숫자")]
        [SerializeField] private Text opponentNameText;
        [SerializeField] private Text opponentTotalText;
        [SerializeField] private Text playerTotalText;
        [SerializeField] private Text betText;
        [SerializeField] private Text playerGoldText;
        [SerializeField] private Text opponentMoneyText;

        [Tooltip("한 줄 안내. 결과와 경고가 여기 뜬다.")]
        [SerializeField] private Text noticeText;

        [Header("버튼 - 베팅")]
        [SerializeField] private GameObject betRow;
        [SerializeField] private Text betAmountText;
        [SerializeField] private Button betMinusButton;
        [SerializeField] private Button betPlusButton;
        [SerializeField] private Button betConfirmButton;

        [Header("버튼 - 뽑기")]
        [SerializeField] private GameObject drawRow;
        [SerializeField] private Button hitButton;
        [SerializeField] private Button standButton;

        public event Action<long> OnBetConfirmed;
        public event Action OnHitPressed;
        public event Action OnStandPressed;

        private long betStep = 10L;
        private long betAmount;
        private long betMax;

        private void Awake()
        {
            Bind(betConfirmButton, () => OnBetConfirmed?.Invoke(betAmount));
            Bind(betMinusButton, () => StepBet(-betStep));
            Bind(betPlusButton, () => StepBet(betStep));
            Bind(hitButton, () => OnHitPressed?.Invoke());
            Bind(standButton, () => OnStandPressed?.Invoke());

            HideRows();
        }

        /// <summary>화면을 통째로 켜거나 끈다(다른 미니게임과 자리를 나눠 쓴다).</summary>
        public void SetVisible(bool value)
        {
            if (root != null && root.activeSelf != value)
                root.SetActive(value);
        }

        public void BindCharacter(PanelType character)
        {
            if (opponentNameText != null)
                opponentNameText.text = character != null ? character.DisplayName : string.Empty;
        }

        // ---------------------------------------------------------------- 상황별 화면

        /// <summary>판이 안 돌아가는 중 - 얼마를 걸지 고른다.</summary>
        public void ShowIdle(long step, long maxBet)
        {
            betStep = step > 0L ? step : 1L;
            betMax = maxBet < 0L ? 0L : maxBet;
            betAmount = Math.Min(betStep, betMax);

            HideRows();
            SetActive(betRow, true);
            RefreshBetText();

            SetText(opponentTotalText, "-");
            SetText(playerTotalText, "-");
            SetText(betText, "0");
            SetText(noticeText, "얼마를 걸까요");
        }

        /// <summary>
        /// 합계와 판돈을 다시 그린다.
        /// <b>카드 그림은 여기서 안 그린다</b> - 테이블 위 3D 오브젝트라
        /// <see cref="BlackjackCardBoard"/> 가 따로 놓는다(2026-09-02 사용자 지시).
        /// </summary>
        public void ShowTotals(int opponentTotal, int playerTotal, long bet, bool opponentHidden = false)
        {
            // 덮인 장이 있으면 <b>합계도 감춘다</b> - 카드만 덮고 숫자를 보여주면 감춘 뜻이 없다.
            SetText(opponentTotalText, opponentHidden ? opponentTotal + " + ?" : Describe(opponentTotal));
            SetText(playerTotalText, Describe(playerTotal));
            SetText(betText, $"{bet:N0}");
        }

        /// <summary>플레이어 차례 - 더 받을지 멈출지.</summary>
        /// <summary>
        /// 플레이어 차례 - 더 받을지 멈출지.
        /// <b>딜러의 뒷패는 아직 덮여 있다</b>(2026-09-02) - 보이는 장만 알려준다.
        /// </summary>
        public void ShowPlayerTurn(int opponentVisibleTotal)
        {
            HideRows();
            SetActive(drawRow, true);

            SetText(noticeText, $"상대가 보인 건 {opponentVisibleTotal}. 더 받을까요?");
        }

        /// <summary>승부가 났다.</summary>
        public void ShowShowdown(int opponentTotal, int playerTotal,
            BlackjackOutcome outcome, long bet)
        {
            HideRows();
            ShowTotals(opponentTotal, playerTotal, bet);
            SetText(noticeText, DescribeOutcome(outcome, bet));
        }

        /// <summary>더 걸 돈이 없다.</summary>
        public void ShowBroke()
        {
            HideRows();
            SetText(noticeText, "판돈이 바닥났습니다");
        }

        public void ShowMoney(long playerGold, long opponentMoney)
        {
            SetText(playerGoldText, $"{playerGold:N0}");
            SetText(opponentMoneyText, $"{opponentMoney:N0}");
        }

        public void ShowNotice(string message) => SetText(noticeText, message);

        // ---------------------------------------------------------------- 안쪽

        /// <summary>합계 표시. 넘겼으면 그 사실이 숫자보다 먼저 읽혀야 한다.</summary>
        private static string Describe(int total)
            => total > Blackjack.Target ? $"{total} 버스트" : total.ToString();

        private static string DescribeOutcome(BlackjackOutcome outcome, long bet)
        {
            switch (outcome)
            {
                case BlackjackOutcome.PlayerWin: return $"이겼습니다  +{bet:N0}";
                case BlackjackOutcome.OpponentWin: return $"졌습니다  -{bet:N0}";
                case BlackjackOutcome.Draw: return "비겼습니다";
                default: return string.Empty;
            }
        }

        private void StepBet(long delta)
        {
            betAmount += delta;

            if (betAmount < betStep)
                betAmount = betStep;

            if (betAmount > betMax)
                betAmount = betMax;

            RefreshBetText();
        }

        private void RefreshBetText()
        {
            SetText(betAmountText, $"{betAmount:N0}");
            SetInteractable(betMinusButton, betAmount > betStep);
            SetInteractable(betPlusButton, betAmount < betMax);
            SetInteractable(betConfirmButton, betAmount > 0L && betAmount <= betMax);
        }

        private void HideRows()
        {
            SetActive(betRow, false);
            SetActive(drawRow, false);
        }

}
}
