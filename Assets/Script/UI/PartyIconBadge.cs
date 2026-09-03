using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.App;
using JojoPuzzle.Core;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 편성 버튼 위에 <b>지금 파티의 얼굴</b>을 얹는다.
    ///
    /// ⭐ 버튼 이름만으로는 "누구를 데리고 있는지"가 안 읽힌다(2026-09-03 사용자 기획).
    /// 아이콘 둘을 <b>살짝 기울여</b> 겹쳐 두면 카드 두 장을 쥔 것처럼 보인다.
    ///
    /// <b>편성 버튼이 여러 화면에 있다</b>(아파트 HUD · 스테이지 준비 화면 · 스티커북).
    /// 그래서 화면마다 따로 그리지 않고 이 부품 하나를 버튼에 붙인다.
    ///
    /// ⭐⭐ <b>얼굴 크기를 고르게 맞추는 두 가지</b>(2026-09-03 사용자 신고: 머리가 긴 캐릭터의
    /// 얼굴이 작게 나온다). <b>캐릭터를 가리는 코드는 한 줄도 없다</b> - 모두에게 같은 규칙이다.
    /// <code>
    ///   1) 여백을 걷어낸다  - 아이콘 png 가 캔버스를 채우는 정도가 69%~100% 로 제각각이었다.
    ///                        캔버스째 맞추면 여백 많은 그림이 그만큼 작게 그려진다.
    ///                        PanelType.iconTrim 이 그림이 실제로 든 자리를 들고 있다(계산된 값).
    ///   2) 가로에 맞춘다    - 머리 <b>폭</b>은 캐릭터마다 비슷하지만 <b>높이</b>는 머리 모양에
    ///                        따라 요동친다. 가로를 채우고 넘치는 세로는 잘라내면 얼굴이 고르다.
    /// </code>
    /// 자르기는 <see cref="Mask"/> 로 한다 - <see cref="RectMask2D"/> 는 <b>기울인 사각형을
    /// 못 자른다</b>(축에 나란한 것만 다룬다). 여기 아이콘은 기울어 있다.
    /// </summary>
    public sealed class PartyIconBadge : MonoBehaviour
    {
        [Tooltip("리더 얼굴이 들어갈 자리. 뒤쪽(왼쪽)에 깔린다.")]
        [SerializeField] private Image leaderIcon;

        [Tooltip("파트너 얼굴이 들어갈 자리. 앞쪽(오른쪽)에 겹친다.")]
        [SerializeField] private Image partnerIcon;

        [Tooltip("기울이는 각도(도). 리더는 이만큼 왼쪽으로, 파트너는 오른쪽으로 눕는다. " +
                 "0이면 반듯하게 선다.")]
        [SerializeField] private float tiltDegrees = 8f;

        /// <summary>
        /// 화면이 켜질 때마다 다시 그린다 - 편성을 고치고 돌아오면 곧바로 새 얼굴이어야 한다.
        /// </summary>
        private void OnEnable()
        {
            Refresh();
        }

        /// <summary>편성이 바뀐 걸 아는 쪽에서 직접 부를 수도 있다.</summary>
        public void Refresh()
        {
            Apply(leaderIcon, PartySelection.Leader, +tiltDegrees);
            Apply(partnerIcon, PartySelection.Partner, -tiltDegrees);
        }

        private static void Apply(Image frame, PanelType character, float tilt)
        {
            if (frame == null)
                return;

            var face = EnsureFace(frame);
            var sprite = character != null ? character.icon : null;

            // 얼굴이 없으면 숨긴다 - 빈 네모가 버튼 위에 뜨는 것보다 아무것도 없는 게 낫다.
            frame.enabled = sprite != null;
            face.enabled = sprite != null;
            if (sprite == null)
                return;

            face.sprite = sprite;
            frame.rectTransform.localRotation = Quaternion.Euler(0f, 0f, tilt);

            FitByWidth(frame.rectTransform, face.rectTransform, sprite, TrimOf(character));
        }

        /// <summary>
        /// 그림이 실제로 든 자리. 값이 이상하면 <b>그림 전체</b>로 물러선다 -
        /// 아직 재지 않은 캐릭터도 예전처럼 그려져야 한다.
        /// </summary>
        private static Rect TrimOf(PanelType character)
        {
            var trim = character.iconTrim;

            if (trim.width <= 0.01f || trim.height <= 0.01f)
                return new Rect(0f, 0f, 1f, 1f);

            return trim;
        }

        /// <summary>
        /// 그림의 <b>든 자리 가로</b>가 틀을 꽉 채우도록 키우고, 넘치는 세로는 위를 맞춰 자른다.
        ///
        /// 위를 맞추는 이유: 초상화는 머리가 위에 있어서, 아래(몸통)를 자르는 쪽이
        /// 얼굴을 남긴다. 아래를 맞추면 머리가 잘려 누군지 알 수 없게 된다.
        /// </summary>
        private static void FitByWidth(RectTransform frameRect, RectTransform faceRect,
            Sprite sprite, Rect trim)
        {
            Vector2 sourceSize = sprite.rect.size;
            if (sourceSize.x <= 0f || sourceSize.y <= 0f)
                return;

            float frameWidth = frameRect.rect.width;
            float frameHeight = frameRect.rect.height;

            // 든 자리의 가로가 틀 가로와 같아지는 배율.
            float scale = frameWidth / (trim.width * sourceSize.x);

            float drawnWidth = sourceSize.x * scale;
            float drawnHeight = sourceSize.y * scale;
            faceRect.sizeDelta = new Vector2(drawnWidth, drawnHeight);

            // 든 자리의 가로 한가운데를 틀 한가운데로.
            float centerX = trim.x + trim.width * 0.5f;
            float x = -(centerX - 0.5f) * drawnWidth;

            // 든 자리의 위 끝을 틀 위 끝으로.
            float top = trim.y + trim.height;
            float y = frameHeight * 0.5f - (top - 0.5f) * drawnHeight;

            faceRect.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>
        /// 얼굴을 그릴 자식을 마련한다. 틀 자신은 <b>자르는 모양</b>이 되고 그림은 자식이 그린다 -
        /// 한 오브젝트로는 "꽉 채워 자르기"를 할 수 없다.
        /// </summary>
        private static Image EnsureFace(Image frame)
        {
            var found = frame.transform.Find("Face");
            if (found != null)
                return found.GetComponent<Image>();

            // 틀은 모양만 내주고 자기는 안 그려진다.
            frame.sprite = null;
            frame.color = Color.white;

            var mask = frame.GetComponent<Mask>();
            if (mask == null)
                mask = frame.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var go = new GameObject("Face", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(frame.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }
    }
}
