using System;
using JojoPuzzle.Core;
using UnityEngine;
using UnityEngine.UI;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 도둑잡기의 <b>화면</b>. 규칙도 돈도 모르고, 시키는 대로 보여주고 눌린 것만 알린다
    /// (다른 도박 화면과 같은 방침, 2026-09-02).
    ///
    /// <b>카드는 여기 없다</b> - 테이블 위 3D 오브젝트라 <see cref="OldMaidCardBoard"/> 가 놓고,
    /// 집는 것도 카드를 직접 눌러서 한다. 이 화면은 판돈·안내·베팅 버튼만 맡는다.
    /// </summary>
    public class OldMaidTableUI : MonoBehaviour
    {
        [Tooltip("도둑잡기 화면 전체를 껐다 켜는 뿌리. 이 컴포넌트는 <b>항상 켜져 있는</b> 바깥에 붙는다.")]
        [SerializeField] private GameObject root;

        [Header("숫자")]
        [SerializeField] private Text opponentNameText;
        [SerializeField] private Text betText;
        [SerializeField] private Text playerGoldText;
        [SerializeField] private Text opponentMoneyText;

        [Tooltip("지금 누가 조커를 들고 있는지 알려주는 줄.")]
        [SerializeField] private Text turnText;

        [Tooltip("한 줄 안내. 결과와 경고가 여기 뜬다.")]
        [SerializeField] private Text noticeText;

        [Header("버튼 - 베팅")]
        [SerializeField] private GameObject betRow;
        [SerializeField] private Text betAmountText;
        [SerializeField] private Button betMinusButton;
        [SerializeField] private Button betPlusButton;
        [SerializeField] private Button betConfirmButton;

        public event Action<long> OnBetConfirmed;

        private long betStep = 10L;
        private long betAmount;
        private long betMax;

        private void Awake()
        {
            Bind(betConfirmButton, () => OnBetConfirmed?.Invoke(betAmount));
            Bind(betMinusButton, () => StepBet(-betStep));
            Bind(betPlusButton, () => StepBet(betStep));

            SetActive(betRow, false);
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

            SetActive(betRow, true);
            RefreshBetText();

            SetText(betText, "0");
            SetText(turnText, string.Empty);
            SetText(noticeText, "얼마를 걸까요");
        }

        public void ShowBet(long bet)
        {
            SetActive(betRow, false);
            SetText(betText, $"{bet:N0}");
        }

        /// <summary>지금 누가 조커를 들고 있는지. 넘어간 횟수도 같이 보여준다.</summary>
        public void ShowTurn(bool holderIsPlayer, int passCount)
        {
            string who = holderIsPlayer ? "내가 조커를 들었습니다" : "상대가 조커를 들었습니다";
            SetText(turnText, passCount > 0 ? $"{who}  ({passCount}번째)" : who);
        }

        /// <summary>승부가 났다.</summary>
        public void ShowResult(OldMaidOutcome outcome, long bet)
        {
            SetText(turnText, string.Empty);
            SetText(noticeText, outcome == OldMaidOutcome.PlayerWin
                ? $"이겼습니다  +{bet:N0}"
                : $"졌습니다  -{bet:N0}");
        }

        /// <summary>더 걸 돈이 없다.</summary>
        public void ShowBroke()
        {
            SetActive(betRow, false);
            SetText(turnText, string.Empty);
            SetText(noticeText, "판돈이 바닥났습니다");
        }

        public void ShowMoney(long playerGold, long opponentMoney)
        {
            SetText(playerGoldText, $"{playerGold:N0}");
            SetText(opponentMoneyText, $"{opponentMoney:N0}");
        }

        public void ShowNotice(string message) => SetText(noticeText, message);

        // ---------------------------------------------------------------- 안쪽

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

}
}
