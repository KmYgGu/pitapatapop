using UnityEngine.SceneManagement;

namespace JojoPuzzle.App
{
    /// <summary>
    /// 앱에 있는 씬 이름을 모아두는 <b>단 하나의 자리</b>. 씬 이름 문자열을 여기저기 적지 말 것 -
    /// 씬은 파일 이름으로 로드되기 때문에 오타가 나도 컴파일이 통과하고 실행할 때야 터진다.
    ///
    /// 전체 흐름(2026-08-06 사용자 계획):
    ///   타이틀/로그인 → <b>아파트(메인 화면)</b> → 스테이지 선택 → 편성 → 배틀 → 결과 → 복귀
    /// 지금 있는 건 로그인·아파트·배틀 셋이고 나머지는 아직 없다.
    /// </summary>
    public static class AppScenes
    {
        /// <summary>타이틀 겸 로그인. 앱이 시작되는 씬이라 빌드 세팅 0번이어야 한다.</summary>
        public const string Login = "Login";

        /// <summary>메인 화면 = 아파트. 캐릭터들이 보이고 방을 눌러 대화를 보는 공간.</summary>
        public const string Apartment = "Apartment";

        /// <summary>스테이지 선택. 챕터 목록 → 스테이지 목록 → 준비 화면이 한 씬 안의 패널들이다.</summary>
        public const string StageSelect = "StageSelect";

        /// <summary>
        /// 배틀. <b>이름이 SampleScene 그대로인 건 의도된 것</b> - 씬 파일을 바꾸면 빌드 세팅과
        /// 에디터가 들고 있는 guid 참조를 다시 이어야 하는데 얻는 게 이름뿐이다. 나중에 정말
        /// 바꾸고 싶어지면 파일을 바꾸고 <b>이 줄 하나만</b> 고치면 된다.
        /// </summary>
        public const string Battle = "SampleScene";

        /// <summary>
        /// 미니게임(도박). 아파트 방 화면에서 들어가고, 나올 때 그 방으로 돌아간다
        /// (<see cref="MiniGameEntry"/> 가 어느 방이었는지를 들고 건너간다).
        /// </summary>
        public const string MiniGame = "MiniGame";

        public static void GoToLogin() => SceneManager.LoadScene(Login);

        public static void GoToApartment() => SceneManager.LoadScene(Apartment);

        public static void GoToStageSelect() => SceneManager.LoadScene(StageSelect);

        public static void GoToBattle() => SceneManager.LoadScene(Battle);

        public static void GoToMiniGame() => SceneManager.LoadScene(MiniGame);
    }
}
