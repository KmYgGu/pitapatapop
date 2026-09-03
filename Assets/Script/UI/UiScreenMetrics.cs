using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// "화면 한 변의 길이가, 이 칸이 사는 좌표계에서는 얼마인가"를 재는 곳.
    ///
    /// <b>왜 한 군데 모았는가</b>(2026-08-30): 화면 밖으로 내보내는 연출은 전부 이 값이 필요한데,
    /// 저마다 <c>rootCanvas.rect.width</c> 를 그냥 읽고 있었다. 그 값은 <b>캔버스 단위</b>라,
    /// <see cref="UiScaleToFit"/> 로 축소된 판 안에서 쓰면 <b>모자란다</b> - 축소된 만큼 덜 가서
    /// 캐릭터가 화면 끝에 걸친 채로 멈춘다(스테이지 준비 화면에서 실제로 그랬다).
    ///
    /// 배율만큼 나눠주면 같은 화면 거리를 그 좌표계의 값으로 옮길 수 있다.
    /// </summary>
    public static class UiScreenMetrics
    {
        /// <summary>
        /// <paramref name="rect"/> 의 로컬 단위로 잰 화면 크기.
        /// 캔버스를 못 찾으면 자기 부모 크기로 물러선다.
        /// </summary>
        public static Vector2 ScreenSizeInLocalUnits(RectTransform rect)
        {
            if (rect == null)
                return Vector2.zero;

            var canvas = rect.GetComponentInParent<Canvas>();
            var canvasRect = canvas != null && canvas.rootCanvas != null
                ? canvas.rootCanvas.transform as RectTransform
                : null;

            if (canvasRect == null)
                return rect.parent is RectTransform parent ? parent.rect.size : rect.rect.size;

            // 판이 축소돼 있으면 같은 화면 거리를 <b>더 큰 로컬 값</b>으로 표현해야 한다.
            float own = Mathf.Abs(rect.lossyScale.x);
            float canvasScale = Mathf.Abs(canvasRect.lossyScale.x);
            float ratio = own > 0.0001f ? canvasScale / own : 1f;

            return canvasRect.rect.size * ratio;
        }

        /// <summary>화면 절반. 가운데에서 밖으로 내보낼 때 쓴다.</summary>
        public static Vector2 ScreenHalfInLocalUnits(RectTransform rect)
            => ScreenSizeInLocalUnits(rect) * 0.5f;

        private static readonly Vector3[] corners = new Vector3[4];

        /// <summary>
        /// 이 칸이 화면 <b>위쪽</b>에서 덮는 비율(0~1). 못 재면 음수.
        ///
        /// <b>왜 앵커 비율을 그냥 못 쓰는가</b>: 판이 <see cref="UiScaleToFit"/> 로 축소돼 있으면
        /// "판의 16%"와 "화면의 16%"가 다르다. 카메라가 그만큼 비켜야 하는 건 <b>화면</b> 기준이라
        /// 실제로 그려진 자리를 재야 한다(<c>ApartmentCameraRig</c> 가 HUD 를 재는 것과 같다).
        ///
        /// 캔버스가 Screen Space - Overlay 라 <c>GetWorldCorners</c> 가 곧 화면 픽셀이다.
        /// </summary>
        public static float CoverFractionFromTop(RectTransform rect)
        {
            if (rect == null || Screen.height <= 0)
                return -1f;

            rect.GetWorldCorners(corners);
            return Mathf.Clamp01((Screen.height - corners[0].y) / Screen.height);
        }

        /// <summary>이 칸이 화면 <b>아래쪽</b>에서 덮는 비율(0~1). 못 재면 음수.</summary>
        public static float CoverFractionFromBottom(RectTransform rect)
        {
            if (rect == null || Screen.height <= 0)
                return -1f;

            rect.GetWorldCorners(corners);
            return Mathf.Clamp01(corners[1].y / Screen.height);
        }
    }
}
