using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 레터박스로 줄어든 판 안에서도 <b>이 칸만은 화면 끝까지</b> 닿게 늘린다.
    ///
    /// <b>왜 필요한가</b>: <see cref="UiScaleToFit"/> 는 판을 설계 크기로 두고 통째로 축소하므로,
    /// 세로로 긴 기기에서는 판 위아래에 여백이 생긴다. 글자·버튼은 그래야 비율이 지켜지지만
    /// <b>화면을 덮어야 하는 칸</b>은 그러면 안 된다:
    /// <list type="bullet">
    ///   <item>어두운 뒷막 - 여백만큼 게임 화면이 비쳐 보인다.</item>
    ///   <item>터치를 막는 칸 - 여백을 누르면 뒤가 눌린다.</item>
    ///   <item>위/아래 띠 - 화면 끝에서 떨어져 둥둥 떠 보인다.</item>
    /// </list>
    ///
    /// 늘릴 변을 골라 쓴다. 뒷막은 네 변 전부, 아래 띠는 좌·우·아래만 켜면
    /// <b>높이는 설계대로 두고</b> 화면 바닥에만 붙는다.
    ///
    /// 앵커는 건드리지 않는다 - <c>offsetMin/offsetMax</c> 만 고쳐서 늘린다.
    /// 그래서 이 컴포넌트를 떼면 원래 배치로 돌아온다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class UiEdgeToScreen : MonoBehaviour
    {
        [Tooltip("화면 끝까지 늘릴 변. 뒷막이면 넷 다, 아래 띠면 좌·우·아래만 켠다.")]
        [SerializeField] private bool left = true;
        [SerializeField] private bool right = true;
        [SerializeField] private bool top = true;
        [SerializeField] private bool bottom = true;

        private RectTransform self;
        private RectTransform canvasRect;
        private readonly Vector3[] corners = new Vector3[4];

        /// <summary>
        /// 늘리는 중에 다시 불리면 무한히 되돌아온다(<see cref="UiScaleToFit"/> 와 같은 이유).
        /// </summary>
        private bool applying;

        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;

        private void OnEnable()
        {
            lastScreenWidth = -1;
            Apply();
        }

        private void OnRectTransformDimensionsChange()
        {
            Apply();
        }

        /// <summary>
        /// <b>화면 크기가 바뀐 프레임에만</b> 다시 잰다.
        ///
        /// 크기 변화 알림만으로는 모자란다: 부모가 <see cref="UiScaleToFit"/> 로 <b>배율만</b>
        /// 달라지면 부모의 rect 는 그대로라(설계 크기 고정) 알림이 오지 않는데,
        /// 화면 끝까지의 거리는 달라져 있다. 그래서 화면 크기를 지켜본다.
        /// </summary>
        private void LateUpdate()
        {
            if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
                return;

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            Apply();
        }

        private void Apply()
        {
            if (applying)
                return;

            if (self == null)
                self = GetComponent<RectTransform>();

            if (self == null || !(self.parent is RectTransform parent))
                return;

            if (canvasRect == null)
            {
                var canvas = parent.GetComponentInParent<Canvas>();
                canvasRect = canvas != null && canvas.rootCanvas != null
                    ? canvas.rootCanvas.transform as RectTransform
                    : null;
            }

            if (canvasRect == null)
                return;

            // 화면(루트 캔버스)의 네 귀퉁이를 <b>부모 공간</b>으로 옮긴다. 부모가 얼마나 축소돼
            // 있든 월드를 거쳐 오므로 배율이 저절로 반영된다.
            canvasRect.GetWorldCorners(corners);
            Vector2 screenMin = parent.InverseTransformPoint(corners[0]);   // 왼쪽 아래
            Vector2 screenMax = parent.InverseTransformPoint(corners[2]);   // 오른쪽 위

            // 앵커가 가리키는 기준 사각형. offset 은 여기서부터의 거리다.
            Rect pr = parent.rect;
            Vector2 refMin = pr.min + Vector2.Scale(self.anchorMin, pr.size);
            Vector2 refMax = pr.min + Vector2.Scale(self.anchorMax, pr.size);

            Vector2 offMin = self.offsetMin;
            Vector2 offMax = self.offsetMax;

            if (left) offMin.x = screenMin.x - refMin.x;
            if (bottom) offMin.y = screenMin.y - refMin.y;
            if (right) offMax.x = screenMax.x - refMax.x;
            if (top) offMax.y = screenMax.y - refMax.y;

            if (offMin == self.offsetMin && offMax == self.offsetMax)
                return;

            applying = true;
            self.offsetMin = offMin;
            self.offsetMax = offMax;
            applying = false;
        }
    }
}
