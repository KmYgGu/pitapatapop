using System;
using JojoPuzzle.Core;
using UnityEngine;
using UnityEngine.UI;
using static JojoPuzzle.UI.UiBind;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 인디언 포커의 <b>화면</b>. 규칙도 돈도 모르고, 시키는 대로 보여주고 눌린 것만 알린다
    /// (2026-09-02). 진행은 전부 <see cref="MiniGameFlow"/> 가 잡는다.
    ///
    /// <b>버튼 줄이 상황마다 통째로 바뀐다</b> - 한 화면에 다 띄워놓고 흐리게 만드는 대신,
    /// 지금 할 수 있는 것만 남긴다. 손가락으로 누르는 화면이라 고를 게 적을수록 좋다.
    ///
    /// <b>카드 그림은 Assets/image/트럼프4x.png</b> 를 잘라 쓴다. 13장만 물려도 돌아가고,
    /// 52장을 다 물리면 판마다 무늬가 바뀌어 눈이 덜 심심하다.
    /// </summary>
    public class PokerTableUI : MonoBehaviour
    {
        [Tooltip("포커 화면 전체를 껐다 켜는 뿌리. 고르는 화면이 떠 있는 동안은 꺼둔다. " +
                 "이 컴포넌트는 <b>항상 켜져 있는</b> 바깥에 붙는다.")]
        [SerializeField] private GameObject root;

        [Header("카드")]
        [Tooltip("카드 앞면. <b>13장(A~K) 또는 52장</b>을 넣는다. 52장이면 " +
                 "무늬 순서대로 13장씩 이어 붙인다(클럽 A~K, 스페이드 A~K, ...).")]
        [SerializeField] private Sprite[] cardFaces;

        [Tooltip("카드 뒷면. 플레이어 자기 패는 공개 전까지 이걸로 덮는다 - 그게 이 게임의 전부다.")]
        [SerializeField] private Sprite cardBack;

        [Tooltip("건너편(캐릭터)의 패. 플레이어에게 <b>보인다</b>.")]
        [SerializeField] private Image opponentCardImage;

        [Tooltip("내 패. 공개 전까지 뒷면이다.")]
        [SerializeField] private Image playerCardImage;

        [Header("숫자")]
        [SerializeField] private Text potText;
        [SerializeField] private Text playerGoldText;
        [SerializeField] private Text opponentMoneyText;
        [SerializeField] private Text opponentNameText;

        [Tooltip("한 줄 안내. 결과와 경고가 여기 뜬다.")]
        [SerializeField] private Text noticeText;

        [Header("버튼 - 시작")]
        [SerializeField] private GameObject idleRow;
        [SerializeField] private Button dealButton;
        [SerializeField] private Button leaveButton;

        [Header("버튼 - 베팅")]
        [SerializeField] private GameObject betRow;
        [SerializeField] private Text betAmountText;
        [SerializeField] private Button betMinusButton;
        [SerializeField] private Button betPlusButton;
        [SerializeField] private Button betConfirmButton;
        [SerializeField] private Button betFoldButton;

        [Header("버튼 - 받기")]
        [SerializeField] private GameObject callRow;
        [SerializeField] private Text callAmountText;
        [SerializeField] private Button callButton;
        [SerializeField] private Button callFoldButton;

        /// <summary>새 판을 돌려달라.</summary>
        public event Action OnDealRequested;

        /// <summary>이만큼 걸겠다.</summary>
        public event Action<long> OnBetConfirmed;

        /// <summary>상대의 레이즈를 받겠다.</summary>
        public event Action OnCallPressed;

        /// <summary>접겠다.</summary>
        public event Action OnFoldPressed;

        /// <summary>그만두고 나가겠다.</summary>
        public event Action OnLeaveRequested;

        // ⚠ <b>무늬는 판당 한 번만 굴린다.</b> 그릴 때마다 굴리면 공개 순간에
        // <b>같은 숫자인데 문양만 바뀌는</b> 것처럼 보인다(2026-09-02 사용자 신고).
        private int playerSuit;
        private int opponentSuit;

        private long betStep = 10L;
        private long betAmount;
        private long betMax;

        private void Awake()
        {
            Bind(dealButton, () => OnDealRequested?.Invoke());
            Bind(leaveButton, () => OnLeaveRequested?.Invoke());
            Bind(betConfirmButton, () => OnBetConfirmed?.Invoke(betAmount));
            Bind(betFoldButton, () => OnFoldPressed?.Invoke());
            Bind(callButton, () => OnCallPressed?.Invoke());
            Bind(callFoldButton, () => OnFoldPressed?.Invoke());

            Bind(betMinusButton, () => StepBet(-betStep));
            Bind(betPlusButton, () => StepBet(betStep));

            HideAllRows();
        }

        /// <summary>포커 화면을 통째로 켜거나 끈다(고르는 화면과 자리를 나눠 쓴다).</summary>
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

        /// <summary>판이 안 돌아가는 중 - 시작할지 나갈지.</summary>
        public void ShowIdle(long ante)
        {
            betStep = ante > 0L ? ante : 1L;

            HideAllRows();
            SetActive(idleRow, true);

            SetCard(playerCardImage, 0, faceDown: true, playerSuit);
            SetCard(opponentCardImage, 0, faceDown: true, opponentSuit);
            SetText(potText, "0");
            SetText(noticeText, $"앞돈 {ante:N0}");
        }

        /// <summary>패를 돌렸다. 상대 패만 보이고 내 패는 덮여 있다.</summary>
        public void ShowDeal(int opponentCard, long pot)
        {
            RollSuits();
            HideAllRows();
            SetCard(playerCardImage, 0, faceDown: true, playerSuit);
            SetCard(opponentCardImage, opponentCard, faceDown: false, opponentSuit);
            ShowPot(pot);
            SetText(noticeText, "상대의 패를 보고 거세요");
        }

        /// <summary>얼마를 걸지 고를 차례.</summary>
        public void ShowBetting(long ante, long maxBet)
        {
            betStep = ante > 0L ? ante : 1L;
            betMax = maxBet < 0L ? 0L : maxBet;
            betAmount = Math.Min(betStep, betMax);

            HideAllRows();
            SetActive(betRow, true);
            RefreshBetText();
        }

        /// <summary>상대가 질렀다 - 받을지 접을지.</summary>
        public void ShowCallOrFold(long need, long pot)
        {
            HideAllRows();
            SetActive(callRow, true);
            ShowPot(pot);

            SetText(callAmountText, $"{need:N0}");
            SetText(noticeText, "상대가 더 걸었습니다");
        }

        /// <summary>패를 깠다.</summary>
        public void ShowShowdown(int playerCard, int opponentCard, PokerOutcome outcome, long payout)
        {
            HideAllRows();
            SetCard(playerCardImage, playerCard, faceDown: false, playerSuit);
            SetCard(opponentCardImage, opponentCard, faceDown: false, opponentSuit);
            SetText(noticeText, DescribeOutcome(outcome, payout));
        }

        /// <summary>더 걸 돈이 없다.</summary>
        public void ShowBroke()
        {
            HideAllRows();
            SetActive(idleRow, true);
            SetInteractable(dealButton, false);
            SetText(noticeText, "판돈이 바닥났습니다");
        }

        public void ShowPot(long pot) => SetText(potText, $"{pot:N0}");

        public void ShowMoney(long playerGold, long opponentMoney)
        {
            SetText(playerGoldText, $"{playerGold:N0}");
            SetText(opponentMoneyText, $"{opponentMoney:N0}");
        }

        public void ShowNotice(string message) => SetText(noticeText, message);

        // ---------------------------------------------------------------- 안쪽

        private static string DescribeOutcome(PokerOutcome outcome, long payout)
        {
            switch (outcome)
            {
                case PokerOutcome.PlayerWin: return $"이겼습니다  +{payout:N0}";
                case PokerOutcome.OpponentWin: return $"졌습니다  -{payout:N0}";
                case PokerOutcome.OpponentFolded: return $"상대가 접었습니다  +{payout:N0}";
                case PokerOutcome.PlayerFolded: return $"접었습니다  -{payout:N0}";
                case PokerOutcome.Draw: return "비겼습니다";
                default: return string.Empty;
            }
        }

        private void StepBet(long delta)
        {
            betAmount += delta;

            // 0(체크)부터 상한까지. 상한은 부르는 쪽이 "상대가 받을 수 있는 만큼"으로 이미 잘라뒀다.
            if (betAmount < 0L)
                betAmount = 0L;

            if (betAmount > betMax)
                betAmount = betMax;

            RefreshBetText();
        }

        private void RefreshBetText()
        {
            SetText(betAmountText, $"{betAmount:N0}");
            SetInteractable(betMinusButton, betAmount > 0L);
            SetInteractable(betPlusButton, betAmount < betMax);
        }

        /// <summary>이번 판에 쓸 무늬을 정한다. 13장만 물려둔 경우엔 무늬가 하나뿐이다.</summary>
        private void RollSuits()
        {
            int suits = cardFaces != null && cardFaces.Length >= IndianPoker.HighestCard
                ? cardFaces.Length / IndianPoker.HighestCard
                : 1;

            if (suits < 1)
                suits = 1;

            playerSuit = UnityEngine.Random.Range(0, suits);
            opponentSuit = UnityEngine.Random.Range(0, suits);
        }

        /// <summary>
        /// 그 칸에 카드를 그린다. <paramref name="card"/> 는 1~13(A~K)이고,
        /// 52장을 물려뒀으면 무늬를 무작위로 골라 눈이 덜 심심하게 한다.
        /// </summary>
        private void SetCard(Image image, int card, bool faceDown, int suit)
        {
            if (image == null)
                return;

            if (faceDown || card < IndianPoker.LowestCard || card > IndianPoker.HighestCard)
            {
                image.sprite = cardBack;
                image.enabled = cardBack != null;
                return;
            }

            int index = card - 1 + suit * IndianPoker.HighestCard;

            if (cardFaces == null || index < 0 || index >= cardFaces.Length)
            {
                image.sprite = cardBack;
                image.enabled = cardBack != null;
                return;
            }

            image.sprite = cardFaces[index];
            image.enabled = true;
        }

        private void HideAllRows()
        {
            SetActive(idleRow, false);
            SetActive(betRow, false);
            SetActive(callRow, false);
            SetInteractable(dealButton, true);
        }

}
}
