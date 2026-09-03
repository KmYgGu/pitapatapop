using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using JojoPuzzle.Core;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 일시정지 메뉴. HUD의 일시정지 버튼을 누르면 열리고, 그동안 게임 진행이 전부 멈춘다.
    ///
    /// 멈추는 방법이 두 갈래인 이유:
    /// 1) Time.timeScale = 0 - 보드의 낙하/매치/스탠드업 타임 코루틴, 제한시간 타이머, 박스 큐브
    ///    회전 등은 전부 Time.deltaTime이나 WaitForSeconds에 의존하므로 이것만으로 전부 얼어붙는다.
    /// 2) BoardInputController.IsPausedByMenu - Update()는 timeScale과 무관하게 계속 호출되므로,
    ///    timeScale만 0으로 두면 화면은 멈췄는데 터치로 퍼즐을 집어들 수는 있는 상태가 된다.
    ///    그래서 보드 입력은 별도 플래그로 막아야 한다.
    ///
    /// 이 컴포넌트 자신은 항상 켜져 있는 오브젝트(PauseMenuPanel)에 붙고, 실제로 보이는 부분
    /// (overlayRoot)만 껐다 켠다 - 비활성 오브젝트에 붙이면 Awake가 돌지 않아 일시정지 버튼
    /// 이벤트를 구독조차 못 하기 때문.
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        [Header("열고 닫을 대상")]
        [SerializeField] private GameObject overlayRoot; // 어두운 배경 + 창 전체. 평소엔 꺼져 있음.

        [Header("버튼")]
        [SerializeField] private TapButtonUI openButton;    // HUD 우상단 일시정지 버튼
        [SerializeField] private TapButtonUI closeButton;   // 창 우상단 X
        [SerializeField] private TapButtonUI restartButton;
        [SerializeField] private TapButtonUI quitButton;

        [Header("나가기 확인")]
        [Tooltip("나가기를 눌렀을 때 뜨는 경고 창. <b>비워두면 확인 없이 곧바로 나간다.</b>")]
        [SerializeField] private GameObject quitConfirmRoot;

        [SerializeField] private TapButtonUI quitConfirmYes;
        [SerializeField] private TapButtonUI quitConfirmNo;

        [Header("음량 슬라이더")]
        [SerializeField] private Slider bgmSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Slider voiceSlider;

        [Header("일시정지 중 조작을 막을 대상")]
        [SerializeField] private BoardInputController boardInput;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(false);

            if (openButton != null)
                openButton.OnTapped += Open;
            if (closeButton != null)
                closeButton.OnTapped += Close;
            if (restartButton != null)
                restartButton.OnTapped += Restart;
            if (quitButton != null)
                quitButton.OnTapped += AskQuit;

            if (quitConfirmYes != null)
                quitConfirmYes.OnTapped += QuitGame;

            if (quitConfirmNo != null)
                quitConfirmNo.OnTapped += CancelQuit;

            if (quitConfirmRoot != null)
                quitConfirmRoot.SetActive(false);

            SetUpSlider(bgmSlider, GameAudioSettings.Bgm, GameAudioSettings.SetBgm);
            SetUpSlider(sfxSlider, GameAudioSettings.Sfx, GameAudioSettings.SetSfx);
            SetUpSlider(voiceSlider, GameAudioSettings.Voice, GameAudioSettings.SetVoice);
        }

        private static void SetUpSlider(Slider slider, float initialValue, UnityEngine.Events.UnityAction<float> onChanged)
        {
            if (slider == null)
                return;

            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(initialValue); // 초기 표시값 때문에 저장 로직이 도는 걸 방지
            slider.onValueChanged.AddListener(onChanged);
        }

        public void Open()
        {
            if (IsOpen)
                return;

            IsOpen = true;

            if (overlayRoot != null)
                overlayRoot.SetActive(true);

            Time.timeScale = 0f;
            if (boardInput != null)
                boardInput.IsPausedByMenu = true;
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;

            if (overlayRoot != null)
                overlayRoot.SetActive(false);

            Time.timeScale = 1f;
            if (boardInput != null)
                boardInput.IsPausedByMenu = false;

            // 슬라이더를 만지는 동안엔 메모리에만 써두고, 창을 닫는 지금 한 번만 디스크에 기록
            GameAudioSettings.Save();
        }

        private void Restart()
        {
            // 씬을 다시 로드하기 전에 반드시 timeScale을 되돌려야 함 - 0인 채로 로드하면
            // 새 씬이 멈춘 상태로 시작해서 아무것도 진행되지 않는다.
            Time.timeScale = 1f;
            GameAudioSettings.Save();

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>
        /// 나가기를 눌렀다. <b>곧바로 나가지 않고 먼저 묻는다</b>(2026-08-25 사용자 지시) -
        /// 하트를 쓰고 들어온 판을 되돌릴 수 없이 버리는 선택이라 실수로 눌리면 안 된다.
        /// 확인 창이 없으면(연결 안 됨) 예전처럼 곧바로 나간다.
        /// </summary>
        private void AskQuit()
        {
            if (quitConfirmRoot == null)
            {
                QuitGame();
                return;
            }

            quitConfirmRoot.SetActive(true);
        }

        private void CancelQuit()
        {
            if (quitConfirmRoot != null)
                quitConfirmRoot.SetActive(false);
        }

        /// <summary>
        /// 판을 버리고 <b>아파트로</b> 나간다.
        ///
        /// <b>앱 종료가 아니다</b>(2026-08-25 변경). 경고 문구가 "현재 플레이는 결과에 반영되지
        /// 않는다"인데, 앱을 끄는 동작이라면 그 말이 성립하지 않는다 - 판을 포기하고 메인 화면으로
        /// 돌아가는 뜻으로 읽는 게 맞다.
        ///
        /// <b>하트는 되돌려주지 않는다.</b> 입장할 때 <see cref="App.StageEntry.Commit"/> 이 이미
        /// 차감했고, 포기로 돌려주면 판을 골라 보고 마음에 안 들면 나가는 게 공짜가 된다.
        /// 그래서 경고 문구가 그걸 미리 알린다.
        /// </summary>
        private void QuitGame()
        {
            Time.timeScale = 1f;
            GameAudioSettings.Save();

            if (quitConfirmRoot != null)
                quitConfirmRoot.SetActive(false);

            // 방금 하던 챕터를 기억시켜 둔다 - 승리·패배로 나갈 때와 같은 처리다.
            App.ScreenRequest.ResumeChapter = App.StageEntry.Chapter;
            App.AppScenes.GoToApartment();
        }

        private void OnDestroy()
        {
            // 씬 재로드/종료 시 timeScale이 0인 채로 남지 않도록 안전장치
            if (IsOpen)
                Time.timeScale = 1f;

            if (openButton != null)
                openButton.OnTapped -= Open;
            if (closeButton != null)
                closeButton.OnTapped -= Close;
            if (restartButton != null)
                restartButton.OnTapped -= Restart;
            if (quitButton != null)
                quitButton.OnTapped -= AskQuit;

            if (quitConfirmYes != null)
                quitConfirmYes.OnTapped -= QuitGame;

            if (quitConfirmNo != null)
                quitConfirmNo.OnTapped -= CancelQuit;
        }
    }
}
