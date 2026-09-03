using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 화면을 이어붙일 때 <b>계속 되풀이되던 잔손질</b>을 한곳에 모은 것.
    ///
    /// 열일곱 개 화면이 저마다 <c>SetText</c>·<c>FindText</c>·<c>Bind</c> 를 똑같이 적어 두고
    /// 있었다(2026-09-03 실측: 35벌). 그 자체로는 짧은 코드지만, 한 벌에서 규칙을 바꾸면
    /// <b>나머지 열여섯은 옛 규칙대로 남는다</b> - 예컨대 "null 이면 빈 글자"를 한 곳만 고치는 식이다.
    ///
    /// ⭐ <b>부르는 쪽은 안 고친다.</b> 이름과 인자를 원래 쓰던 그대로 두었으니
    /// <c>using static JojoPuzzle.UI.UiBind;</c> 한 줄만 더하면 된다.
    ///
    /// <b>여기에 규칙을 넣지 말 것.</b> "무엇을 보여줄지"는 각 화면이 정하고, 여기는
    /// <b>null 을 견디며 값을 얹는 일</b>만 한다.
    /// </summary>
    public static class UiBind
    {
        /// <summary>글자를 얹는다. <b>null 은 빈 글자로</b> - 화면에 "null" 이 찍히면 안 된다.</summary>
        public static void SetText(Text target, string value)
        {
            if (target != null)
                target.text = value ?? string.Empty;
        }

        /// <summary>자식을 이름으로 찾아 글자를 얹는다.</summary>
        public static void SetText(Component root, string childName, string value)
            => SetText(FindText(root, childName), value);

        public static void SetText(GameObject root, string childName, string value)
            => SetText(FindText(root, childName), value);

        /// <summary>
        /// 자식을 <b>이름으로</b> 찾는다.
        /// ⚠ 이름을 바꾸면 <b>조용히 끊긴다</b> - 오류도 안 나고 그 자리만 안 그려진다.
        /// </summary>
        public static Text FindText(Component root, string childName) => Find<Text>(root, childName);

        public static Text FindText(GameObject root, string childName)
            => root != null ? Find<Text>(root.transform, childName) : null;

        public static T Find<T>(Component root, string childName) where T : Component
        {
            if (root == null || string.IsNullOrEmpty(childName))
                return null;

            var child = root.transform.Find(childName);
            return child != null ? child.GetComponent<T>() : null;
        }

        public static T Find<T>(GameObject root, string childName) where T : Component
            => root != null ? Find<T>(root.transform, childName) : null;

        /// <summary>버튼에 할 일을 잇는다.</summary>
        public static void Bind(Button button, UnityAction action)
        {
            if (button != null && action != null)
                button.onClick.AddListener(action);
        }

        /// <summary>
        /// 켜고 끈다. <b>이미 그 상태면 건드리지 않는다</b> -
        /// <c>SetActive</c> 는 값이 같아도 자식들을 훑으므로 공짜가 아니다.
        /// </summary>
        public static void SetActive(GameObject go, bool value)
        {
            if (go != null && go.activeSelf != value)
                go.SetActive(value);
        }

        public static void SetInteractable(Button button, bool value)
        {
            if (button != null)
                button.interactable = value;
        }
    }
}
