using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 로그인 씬의 화면 담당. 버튼을 누르면 <see cref="IAuthProvider"/>에게 시키고, 성공하면
    /// <see cref="SessionState"/>에 기록한 뒤 아파트(메인 화면)로 넘어간다.
    ///
    /// <b>이 클래스는 어떤 인증 SDK도 모른다</b> - 나중에 Firebase 를 붙여도 여기는 안 고친다.
    /// 반대로 "성공하면 어디로 가는가" 같은 흐름은 여기 것이지 인증 구현체 것이 아니다.
    /// </summary>
    public class LoginSceneController : MonoBehaviour
    {
        [Header("버튼")]
        [SerializeField] private Button guestButton;
        [SerializeField] private Button googleButton;

        [Header("문구")]
        [Tooltip("로그인 실패 사유나 진행 상태를 보여주는 한 줄. 평소에는 비어 있다.")]
        [SerializeField] private Text statusText;

        private IAuthProvider guest;
        private IAuthProvider google;

        /// <summary>
        /// 로그인 시도 중인지. <b>버튼 두 개를 같이 잠그는 이유</b>: 실제 SDK 는 몇 초가 걸리고
        /// 그 사이에 다른 버튼을 누르면 로그인 두 개가 동시에 진행돼 어느 쪽 결과로 씬이 넘어갈지
        /// 알 수 없게 된다. 지금 구현은 즉시 끝나지만 그때 가서 넣으면 늦는다.
        /// </summary>
        private bool busy;

        private void Awake()
        {
            guest = new GuestAuthProvider();
            google = new GoogleAuthProviderStub();
        }

        private void Start()
        {
            if (guestButton != null)
                guestButton.onClick.AddListener(() => TrySignIn(guest));

            if (googleButton != null)
            {
                googleButton.onClick.AddListener(() => TrySignIn(google));

                // 쓸 수 없는 수단은 눌리지 않게 해둔다 - 눌러도 실패 문구만 나오는 버튼은
                // "고장난 것"으로 읽힌다.
                googleButton.interactable = google.IsAvailable;
            }

            SetStatus(string.Empty);
        }

        private void TrySignIn(IAuthProvider provider)
        {
            if (busy || provider == null)
                return;

            busy = true;
            SetButtonsInteractable(false);
            SetStatus("접속 중...");

            provider.SignIn(result =>
            {
                busy = false;

                if (!result.Success)
                {
                    SetStatus(result.Message);
                    SetButtonsInteractable(true);
                    return;
                }

                SessionState.SignIn(result.Kind, result.UserId);
                AppScenes.GoToApartment();
            });
        }

        private void SetButtonsInteractable(bool value)
        {
            if (guestButton != null)
                guestButton.interactable = value;

            // 구글 버튼은 원래 못 쓰는 상태일 수 있으므로 되돌릴 때 그 조건을 다시 본다.
            if (googleButton != null)
                googleButton.interactable = value && google != null && google.IsAvailable;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }
    }
}
