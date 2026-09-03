using System;
using UnityEngine;

namespace JojoPuzzle.App
{
    /// <summary>어떤 방식으로 로그인했는지.</summary>
    public enum AuthKind
    {
        None,
        Guest,
        Google
    }

    /// <summary>로그인 시도의 결과. 실패해도 예외를 던지지 않고 이걸로 돌려준다.</summary>
    public readonly struct AuthResult
    {
        public readonly bool Success;
        public readonly AuthKind Kind;

        /// <summary>이 사용자를 가리키는 값. 나중에 서버 세이브가 생기면 이게 키가 된다.</summary>
        public readonly string UserId;

        /// <summary>실패했을 때 화면에 보여줄 문구. 성공이면 비어 있다.</summary>
        public readonly string Message;

        private AuthResult(bool success, AuthKind kind, string userId, string message)
        {
            Success = success;
            Kind = kind;
            UserId = userId;
            Message = message;
        }

        public static AuthResult Ok(AuthKind kind, string userId) =>
            new AuthResult(true, kind, userId, string.Empty);

        public static AuthResult Fail(AuthKind kind, string message) =>
            new AuthResult(false, kind, string.Empty, message);
    }

    /// <summary>
    /// 로그인 수단 하나. <b>화면(LoginSceneController)은 이 인터페이스만 알고 실제 SDK는 모른다</b> -
    /// 나중에 Firebase나 Google Play Games 를 붙일 때 여기 구현체 하나만 갈아끼우면 되고
    /// 버튼·문구·씬 전환은 안 건드린다.
    ///
    /// <b>결과를 콜백으로 돌려주는 이유</b>: 실제 인증은 네트워크와 OS 팝업을 거치므로 몇 초가
    /// 걸린다. 지금 구현체들이 즉시 끝난다고 해서 반환값으로 만들어두면, SDK를 붙이는 순간
    /// 인터페이스부터 다시 짜야 한다.
    /// </summary>
    public interface IAuthProvider
    {
        AuthKind Kind { get; }

        /// <summary>지금 이 기기에서 쓸 수 있는 수단인지. false면 버튼을 비활성으로 두면 된다.</summary>
        bool IsAvailable { get; }

        /// <summary>로그인 시도. 성공이든 실패든 <paramref name="onDone"/>이 반드시 한 번 불린다.</summary>
        void SignIn(Action<AuthResult> onDone);
    }

    /// <summary>
    /// 게스트 로그인. 기기 안에서만 통하는 임의의 id 를 하나 만들어 들고 다닌다.
    ///
    /// <b>이건 "신원"이지 "세이브"가 아니다.</b> 계정 로그인이 생기면 세이브는 서버로 가야 한다는 게
    /// 기존 방침이라(로컬 저장만 가정하고 설계하면 나중에 갈아엎어야 함), 여기서 PlayerPrefs 에
    /// 넣는 건 <b>id 하나뿐</b>이다. 진행도·보유 캐릭터 같은 걸 여기에 얹지 말 것.
    /// </summary>
    public sealed class GuestAuthProvider : IAuthProvider
    {
        private const string GuestIdKey = "jojopuzzle.guest_id";

        public AuthKind Kind => AuthKind.Guest;

        /// <summary>게스트는 네트워크도 SDK도 필요 없어서 항상 된다.</summary>
        public bool IsAvailable => true;

        public void SignIn(Action<AuthResult> onDone)
        {
            string id = PlayerPrefs.GetString(GuestIdKey, string.Empty);

            if (string.IsNullOrEmpty(id))
            {
                // 기기 안에서만 구분되면 되므로 Guid 로 충분하다. 서버가 생기면 이 값을 그대로
                // 넘겨 계정에 이어 붙이는 식이 된다(그래서 매번 새로 만들지 않고 저장해둔다).
                id = "guest-" + Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(GuestIdKey, id);
                PlayerPrefs.Save();
            }

            onDone?.Invoke(AuthResult.Ok(AuthKind.Guest, id));
        }
    }

    /// <summary>
    /// 구글 계정 로그인 <b>자리만 잡아둔 것</b>. 아직 인증 SDK가 프로젝트에 없어서 실제로는
    /// 아무것도 하지 않고 "아직 준비 안 됨"으로 돌려준다.
    ///
    /// <b>실제로 붙일 때</b>: 이 클래스를 지우지 말고 내용만 채우거나, 같은 인터페이스를 구현한
    /// 새 클래스를 만들어 <see cref="LoginSceneController"/>가 그걸 쓰게 하면 된다. 어느 쪽이든
    /// 화면 쪽 코드는 그대로다. 다만 SDK 설치 말고도 Google Cloud 콘솔 설정과 서명 키(SHA-1)
    /// 등록이 필요해서 <b>에디터 밖에서 해야 하는 일</b>이 있다.
    /// </summary>
    public sealed class GoogleAuthProviderStub : IAuthProvider
    {
        public AuthKind Kind => AuthKind.Google;

        /// <summary>SDK가 없으므로 false. 화면은 이걸 보고 버튼을 흐리게 만든다.</summary>
        public bool IsAvailable => false;

        public void SignIn(Action<AuthResult> onDone)
        {
            onDone?.Invoke(AuthResult.Fail(AuthKind.Google, "구글 로그인은 아직 준비 중입니다."));
        }
    }
}
