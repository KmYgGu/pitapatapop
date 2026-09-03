namespace JojoPuzzle.App
{
    /// <summary>
    /// 지금 누가 접속해 있는지. 씬이 바뀌어도 남아야 해서 static 이다
    /// (씬을 갈아끼우는 방식이라 MonoBehaviour 로 들고 있으면 로그인 씬과 함께 사라진다).
    ///
    /// <b>여기 게임 데이터를 얹지 말 것.</b> 보유 캐릭터·진행도·재화는 세이브 데이터이고,
    /// 계정 로그인이 있는 이상 그건 서버에서 와야 한다는 게 기존 방침이다. 이 클래스가 아는 건
    /// "누구로 들어왔는가" 하나뿐이고, 세이브가 생기면 그걸 <b>따로</b> 만들어 이 id 로 불러온다.
    /// </summary>
    public static class SessionState
    {
        /// <summary>어떤 수단으로 들어왔는지. 로그인 전에는 <see cref="AuthKind.None"/>.</summary>
        public static AuthKind Kind { get; private set; } = AuthKind.None;

        /// <summary>접속한 사용자의 id. 로그인 전에는 빈 문자열.</summary>
        public static string UserId { get; private set; } = string.Empty;

        public static bool IsSignedIn => Kind != AuthKind.None;

        /// <summary>로그인이 성공했을 때 <see cref="LoginSceneController"/>가 부른다.</summary>
        public static void SignIn(AuthKind kind, string userId)
        {
            Kind = kind;
            UserId = userId;
        }

        /// <summary>로그아웃. 계정 전환이 생기면 쓸 자리라 미리 열어둔다.</summary>
        public static void SignOut()
        {
            Kind = AuthKind.None;
            UserId = string.Empty;
        }
    }
}
