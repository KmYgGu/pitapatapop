using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.UI;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 메인 화면(아파트)의 버튼과 안내 문구를 맡는다.
    ///
    /// <b>레벨·재화·하트 표시는 여기 없다</b> - <see cref="PlayerStatusBar"/> 가 맡는다.
    /// 스테이지 준비 화면도 같은 것을 보여줘야 해서, 특히 시계가 도는 하트 표시가 두 벌이 되지
    /// 않도록 뺐다.
    ///
    /// <b>화면 중앙은 비워둔다.</b> 아파트를 눌러 방으로 들어가야 하므로, 여기 붙는 어떤 것도
    /// 가운데를 덮으면 안 된다. 배경을 깔아 터치를 막는 일이 없도록 특히 주의할 것.
    /// </summary>
    public class ApartmentHudController : MonoBehaviour
    {
        [Header("버튼")]
        [Tooltip("캐릭터 <b>뽑기</b>. 상점과 나뉜 자리다(2026-09-02 사용자 지시) - " +
                 "값을 알고 사는 것과 운에 맡기는 것은 다른 화면이어야 한다.")]
        [SerializeField] private Button gachaButton;

        [Tooltip("<b>상점</b>. 스티커·은행·인테리어·선물을 판다.")]
        [SerializeField] private Button shopButton;
        [SerializeField] private Button stageEnterButton;
        [SerializeField] private Button formationButton;
        [SerializeField] private Button optionButton;
        [SerializeField] private Button mailButton;
        [SerializeField] private Button friendButton;
        [SerializeField] private Button treasureButton;
        [SerializeField] private Button apartmentOverviewButton;

        [Header("화면")]
        [Tooltip("우편함. 물려두면 우편 버튼이 이 화면을 연다(비어 있으면 '준비 중' 안내).")]
        [SerializeField] private MailboxPanel mailboxPanel;

        [Tooltip("상점 화면. 비워두면 상점 버튼이 \"준비 중\"만 띄운다.")]
        [SerializeField] private ShopPanel shopPanel;

        [Tooltip("재화 표시줄. 상점에서 돌아오면 골드·보석을 다시 그린다.")]
        [SerializeField] private JojoPuzzle.UI.PlayerStatusBar statusBar;

        [Tooltip("안 받은 우편 수를 적는 자리. 없으면 배지를 안 그린다. " +
                 "0통이면 그 오브젝트째로 꺼진다.")]
        [SerializeField] private Text mailBadgeText;

        [Tooltip("아파트 전체 보기. 물려두면 그 버튼이 이 화면을 연다(비어 있으면 '준비 중' 안내).")]
        [SerializeField] private ApartmentRoomFlow roomFlow;

        [Header("안내")]
        [Tooltip("아직 없는 화면을 눌렀을 때 잠깐 뜨는 한 줄. 평소에는 비어 있다.")]
        [SerializeField] private Text noticeText;
        [SerializeField] private float noticeDuration = 1.4f;

        [Header("가로 맞춤")]
        [Tooltip("글자 수가 많아 좁은 화면에서 상자를 넘칠 수 있는 문구들. 씬에 적어둔 크기를 " +
                 "<b>최대</b>로 삼아 UITypography 사다리를 한 단씩 내려간다.")]
        [SerializeField] private Text[] autoFitTexts;

        private float noticeRemaining;

        /// <summary>
        /// <see cref="autoFitTexts"/> 가 씬에서 갖고 있던 원래 크기. <b>매번 이 값에서 다시 시작</b>해야
        /// 한다 - 줄어든 크기에서 또 줄이면 화면이 커져도 영영 안 돌아온다.
        /// </summary>
        private int[] autoFitBaseSizes;

        private void Awake()
        {
            if (autoFitTexts == null)
                return;

            autoFitBaseSizes = new int[autoFitTexts.Length];
            for (int i = 0; i < autoFitTexts.Length; i++)
                autoFitBaseSizes[i] = autoFitTexts[i] != null ? autoFitTexts[i].fontSize : 0;
        }

        private void Start()
        {
            // 스테이지 입장만 실제로 화면이 생겼다. 나머지는 아직 갈 곳이 없다.
            if (stageEnterButton != null)
                stageEnterButton.onClick.AddListener(AppScenes.GoToStageSelect);

            // 편성은 스테이지 선택 씬 안의 패널이라, 거기로 가되 편성부터 열라고 부탁한다.
            if (formationButton != null)
                formationButton.onClick.AddListener(GoToFormation);

            // 우편함은 화면이 생겼다(2026-08-28). 아직 안 물려뒀으면 예전처럼 안내만 띄운다 -
            // 씬을 아직 안 고친 상태에서도 버튼이 조용히 죽어 있지는 않게.
            if (mailboxPanel != null)
            {
                if (mailButton != null)
                    mailButton.onClick.AddListener(OpenMailbox);

                mailboxPanel.OnClosed += RefreshMailBadge;
            }
            else
            {
                BindNotReady(mailButton, "우편함");
            }

            // 아파트 전체 보기도 화면이 생겼다(2026-08-28). 우편함과 같은 방식으로,
            // 물려두지 않았으면 예전처럼 안내만 띄운다.
            if (roomFlow != null)
            {
                if (apartmentOverviewButton != null)
                    apartmentOverviewButton.onClick.AddListener(roomFlow.OpenOverview);
            }
            else
            {
                BindNotReady(apartmentOverviewButton, "아파트 전체 보기");
            }

            BindNotReady(gachaButton, "뽑기");

            if (shopPanel != null)
            {
                if (shopButton != null)
                    shopButton.onClick.AddListener(OpenShop);

                shopPanel.OnClosed += HandleShopClosed;
            }
            else
                BindNotReady(shopButton, "상점");
            BindNotReady(optionButton, "옵션");
            BindNotReady(friendButton, "친구 목록");
            BindNotReady(treasureButton, "보물 상자");

            ShowNotice(string.Empty);
            RefreshMailBadge();
            FitTexts();
        }

        private void OnDestroy()
        {
            if (mailboxPanel != null)
                mailboxPanel.OnClosed -= RefreshMailBadge;
        }

        private void OpenMailbox()
        {
            mailboxPanel.Show();
        }

        /// <summary>
        /// 상점을 연다. <b>HUD 를 화면 밖으로 비키지 않는다</b> - 상점 창이 화면을 통째로 덮어
        /// 어차피 안 보이고, 비켜뒀다가 되돌리는 사이에 HUD 가 미끄러지는 게 보인다
        /// (우편함과 같은 방식).
        /// </summary>
        private void OpenShop()
        {
            shopPanel.Open();
        }

        /// <summary>상점을 닫고 나오면 골드·보석이 달라져 있다 - 표시줄을 다시 그린다.</summary>
        private void HandleShopClosed()
        {
            if (statusBar != null)
                statusBar.RefreshProfile();
        }

        /// <summary>
        /// 안 받은 우편 수를 다시 그린다. 우편함을 닫을 때마다 부른다 - 받고 나오면 배지가
        /// 그대로 남아 "아직 뭔가 있다"고 거짓말을 한다.
        /// </summary>
        private void RefreshMailBadge()
        {
            if (mailBadgeText == null)
                return;

            int unread = Mailbox.UnreadCount;

            mailBadgeText.text = unread > 0 ? unread.ToString() : string.Empty;
            mailBadgeText.gameObject.SetActive(unread > 0);
        }

        private void Update()
        {
            TickNotice();
        }

        /// <summary>
        /// 화면 크기가 바뀌면(기기 회전) 상자 폭도 달라지므로 다시 맞춘다.
        /// 세로 기준으로 고른 크기는 어느 기기에서나 같지만 <b>가로는 비율마다 좁아진다</b>.
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            FitTexts();
        }

        private void FitTexts()
        {
            if (autoFitTexts == null || autoFitBaseSizes == null)
                return;

            for (int i = 0; i < autoFitTexts.Length; i++)
                FitOne(i);
        }

        private void FitOne(int index)
        {
            if (autoFitTexts == null || autoFitBaseSizes == null)
                return;

            if (index < 0 || index >= autoFitTexts.Length || index >= autoFitBaseSizes.Length)
                return;

            var text = autoFitTexts[index];
            if (text == null || autoFitBaseSizes[index] <= 0)
                return;

            UITypography.FitToWidth(text, text.rectTransform.rect.width, autoFitBaseSizes[index]);
        }

        private static void GoToFormation()
        {
            ScreenRequest.OpenFormationDirectly = true;
            AppScenes.GoToStageSelect();
        }

        private void BindNotReady(Button button, string screenName)
        {
            if (button == null)
                return;

            button.onClick.AddListener(() => ShowNotice($"{screenName} - 준비 중입니다"));
        }

        private void ShowNotice(string message)
        {
            if (noticeText != null)
            {
                noticeText.text = message;

                // 문구가 바뀌면 폭도 달라지므로 다시 맞춰야 한다 - 화면 크기가 바뀔 때만
                // 맞추면 긴 안내가 그대로 삐져나간다.
                if (autoFitTexts != null)
                {
                    for (int i = 0; i < autoFitTexts.Length; i++)
                    {
                        if (autoFitTexts[i] == noticeText)
                            FitOne(i);
                    }
                }
            }

            noticeRemaining = string.IsNullOrEmpty(message) ? 0f : noticeDuration;
        }

        private void TickNotice()
        {
            if (noticeRemaining <= 0f)
                return;

            noticeRemaining -= Time.deltaTime;
            if (noticeRemaining <= 0f)
                ShowNotice(string.Empty);
        }
    }
}
