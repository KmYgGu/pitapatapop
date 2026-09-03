using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.Core;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 스티커를 뽑았을 때 <b>무엇이 나왔는지 보여 주는 연출</b>(2026-09-03 사용자 요청).
    ///
    /// <code>
    ///   화면이 어두워지고 → 스티커가 작게 튀어나와 커지며 한 바퀴 돌고 → 이름과 설명이 뜬다
    ///   아무 데나 누르면 닫힌다
    /// </code>
    ///
    /// ⭐ <b>값을 치른 일에는 연출이 있어야 한다.</b> 안내 문구 한 줄로 끝내면 무엇을 샀는지
    /// 읽히지 않고, 중복이 나왔을 때 "또 그거야?"라는 감정도 안 생긴다.
    ///
    /// <b>씬에 미리 만들어 두지 않는다</b> - 상점이 열릴 때만 필요하고, 부품 하나가 자기 몫의
    /// 오브젝트를 들고 있는 게 다루기 쉽다. 그래서 <see cref="Show"/> 가 처음 불릴 때 만든다.
    /// </summary>
    public sealed class StickerDrawReveal : MonoBehaviour
    {
        [Tooltip("연출을 띄울 캔버스. 비워두면 이 컴포넌트가 붙은 곳의 캔버스를 찾아 쓴다.")]
        [SerializeField] private Canvas canvas;

        [Tooltip("스티커가 다 커지는 데 걸리는 시간(초).")]
        [SerializeField] private float popSeconds = 0.35f;

        [Tooltip("커지면서 도는 바퀴 수. 0이면 안 돈다.")]
        [SerializeField] private float spinTurns = 1f;

        [Tooltip("스티커 한 변의 길이. ⚠ 세로 기준 600에 맞춘 값이다 - 가로는 기기마다 달라서 " +
                 "가로를 기준으로 잡으면 좁은 폰에서 화면을 넘는다.")]
        [SerializeField] private float stickerSize = 190f;

        private RectTransform root;
        private RectTransform card;
        private Image cardImage;
        private Text nameText;
        private Text bodyText;
        private Coroutine playing;

        /// <summary>뽑은 스티커를 보여 준다. <paramref name="owned"/> 는 이제 몇 장째인지.</summary>
        public void Show(StickerDefinition sticker, int owned)
        {
            if (sticker == null)
                return;

            EnsureBuilt();
            if (root == null)
                return;

            cardImage.sprite = sticker.sprite;
            cardImage.enabled = sticker.sprite != null;

            nameText.text = owned > 1
                ? "코스트 " + sticker.cost + "   (" + owned + "장째)"
                : "코스트 " + sticker.cost;
            bodyText.text = sticker.description;

            root.gameObject.SetActive(true);

            if (playing != null)
                StopCoroutine(playing);

            playing = StartCoroutine(PopRoutine());
        }

        /// <summary>
        /// 스티커 없이 <b>한 마디만</b> 띄운다("골드가 모자랍니다" 같은 것).
        ///
        /// ⚠ 상점의 안내 문구는 <b>목록을 다시 그릴 때마다 지워진다</b>(Refresh 가 그 자리에
        /// "준비 중입니다"를 쓰거나 비운다). 그래서 놓치기 쉬운 자리다 - 값을 치르려다 실패한
        /// 것처럼 <b>반드시 읽혀야 하는</b> 말은 이 창으로 띄운다.
        /// </summary>
        public void ShowMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            EnsureBuilt();
            if (root == null)
                return;

            cardImage.enabled = false;
            nameText.text = message;
            bodyText.text = "화면을 누르면 닫힙니다";

            root.gameObject.SetActive(true);

            if (playing != null)
            {
                StopCoroutine(playing);
                playing = null;
            }

            card.localScale = Vector3.one;
            card.localRotation = Quaternion.identity;
        }

        public void Hide()
        {
            if (playing != null)
            {
                StopCoroutine(playing);
                playing = null;
            }

            if (root != null)
                root.gameObject.SetActive(false);
        }

        private IEnumerator PopRoutine()
        {
            float spin = spinTurns * 360f;

            for (float t = 0f; t < popSeconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / popSeconds);

                // 살짝 넘겼다 제자리로 - 그냥 커지기만 하면 툭 나타난 것처럼 보인다.
                float scale = Mathf.LerpUnclamped(0.2f, 1f, 1f - Mathf.Pow(1f - k, 3f));
                scale *= 1f + 0.12f * Mathf.Sin(k * Mathf.PI);

                card.localScale = new Vector3(scale, scale, 1f);
                card.localRotation = Quaternion.Euler(0f, 0f, spin * (1f - k));
                yield return null;
            }

            card.localScale = Vector3.one;
            card.localRotation = Quaternion.identity;
            playing = null;
        }

        /// <summary>처음 쓸 때 한 번만 만든다.</summary>
        private void EnsureBuilt()
        {
            if (root != null)
                return;

            var target = canvas != null ? canvas : GetComponentInParent<Canvas>();
            if (target == null)
                return;

            root = NewRect("StickerDrawReveal", (RectTransform)target.transform);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            // 뒤를 덮어 어둡게. 여기를 누르면 닫힌다.
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            var close = root.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(Hide);

            card = NewRect("Card", root);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(stickerSize, stickerSize);
            card.anchoredPosition = new Vector2(0f, 40f);
            cardImage = card.gameObject.AddComponent<Image>();
            cardImage.raycastTarget = false;
            cardImage.preserveAspect = true;

            nameText = NewText("NameText", root, 24, new Vector2(0f, -95f), 34f);
            bodyText = NewText("BodyText", root, 17, new Vector2(0f, -140f), 60f);

            root.gameObject.SetActive(false);
        }

        private static RectTransform NewRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>
        /// ⚠ 가로를 <b>부모 폭에 맞춰 늘린다</b>(고정 폭이 아니다) - 좁은 폰에서 글자가 넘치지
        /// 않게 하려면 상자가 화면을 따라가야 한다. 글꼴은 BestFit 이라 상자에 맞춰 줄어든다.
        /// </summary>
        private static Text NewText(string name, RectTransform parent, int size,
            Vector2 position, float height)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0.08f, 0.5f);
            rect.anchorMax = new Vector2(0.92f, 0.5f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = position;

            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(9, size / 2);
            text.resizeTextMaxSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
