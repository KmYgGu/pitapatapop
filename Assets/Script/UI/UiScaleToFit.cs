using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// UI 판을 <b>정해진 설계 크기 그대로 두고, 화면에 맞게 통째로 축소</b>한다.
    /// 남는 자리는 위아래(또는 좌우) 레터박스가 된다.
    ///
    /// <b>왜 AspectRatioFitter 로는 안 되는가</b>(2026-08-24에 실제로 겪음): 그건 <b>상자 크기만</b>
    /// 바꾸고 글꼴 크기는 건드리지 않는다. 좁은 화면에서 상자가 줄면 글자는 그대로라 상대적으로
    /// 더 커져서 넘치고, 버튼 문구가 세 줄로 쪼개지는 식이 된다. 비율을 지키려면 상자·글자·여백이
    /// <b>같은 배율로</b> 줄어야 하고, 그건 크기가 아니라 <c>localScale</c> 을 건드려야 나온다.
    ///
    /// <b>쓰는 법</b>: 캔버스 바로 아래에 이 컴포넌트를 단 판을 두고, 실제 UI 는 전부 그 자식으로
    /// 넣는다. 자식들은 지금처럼 퍼센트 앵커만 쓰면 되는데, 그 퍼센트의 기준이 화면이 아니라
    /// <see cref="designSize"/> 가 되므로 <b>어느 기기에서나 배치가 똑같아진다</b>.
    ///
    /// 캔버스 스케일러(Match=Height, 기준 800x600)와 함께 쓰는 걸 전제로 한다. 그래서 세로는
    /// 늘 600이고 실제로 변하는 건 가로뿐이다 - 세로로 긴 기기일수록 많이 줄어든다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class UiScaleToFit : MonoBehaviour
    {
        [Tooltip("이 UI 를 설계한 크기(캔버스 단위). 가로 337.5 x 세로 600 은 9:16 폰 기준이다.\n" +
                 "이 크기의 화면에서는 배율 1로 딱 맞고, 더 좁은 기기에서는 통째로 줄어든다.\n" +
                 "<b>여기 값을 바꾸면 모든 자식의 퍼센트 앵커가 가리키는 실제 크기가 달라진다.</b>")]
        [SerializeField] private Vector2 designSize = new Vector2(337.5f, 600f);

        [Tooltip("1을 넘겨 확대할 수 있게 할지. 꺼두면 넓은 기기에서는 원래 크기로 두고 " +
                 "좌우에 여백을 남긴다(글자가 쓸데없이 커지지 않는다).")]
        [SerializeField] private bool allowUpscale;

        private RectTransform self;

        /// <summary>
        /// 자기 크기를 바꾸면 <see cref="OnRectTransformDimensionsChange"/> 가 다시 불린다.
        /// 그대로 두면 무한히 되돌아오므로 적용 중에는 잠근다.
        /// </summary>
        private bool applying;

        private void Awake()
        {
            self = GetComponent<RectTransform>();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void OnRectTransformDimensionsChange()
        {
            Apply();
        }

        private void Apply()
        {
            if (applying)
                return;

            if (self == null)
                self = GetComponent<RectTransform>();

            if (self == null || designSize.x <= 0f || designSize.y <= 0f)
                return;

            if (!(self.parent is RectTransform parent))
                return;

            Rect available = parent.rect;
            if (available.width <= 0f || available.height <= 0f)
                return;

            float scale = Mathf.Min(available.width / designSize.x, available.height / designSize.y);
            if (!allowUpscale)
                scale = Mathf.Min(scale, 1f);

            applying = true;

            // 판은 <b>늘 설계 크기</b>다. 화면에 맞추는 건 크기가 아니라 배율이 한다.
            self.anchorMin = new Vector2(0.5f, 0.5f);
            self.anchorMax = new Vector2(0.5f, 0.5f);
            self.pivot = new Vector2(0.5f, 0.5f);
            self.anchoredPosition = Vector2.zero;
            self.sizeDelta = designSize;
            self.localScale = new Vector3(scale, scale, 1f);

            applying = false;
        }
    }
}
