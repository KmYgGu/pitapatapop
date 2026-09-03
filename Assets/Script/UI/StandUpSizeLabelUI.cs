using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 스탠드업 타임에 정사각형이 처음 만들어질 때 그 블록 위에 "2사이즈" 같은 라벨을 띄운다.
    /// 3x3이면 "3사이즈", 4x4면 "4사이즈".
    ///
    /// <b>새로 만들어진 정사각형마다 하나씩</b> 뜬다. 빨간 무리와 노란 무리가 각각 2x2가 되면
    /// 두 블록 위에 "2사이즈"가 하나씩 뜬다. 같은 자리·같은 크기로 이미 합쳐져 있던 블록은
    /// BoardView가 아예 알리지 않으므로(OnStandUpSquareFormed는 새로 생긴 것만 발행) 여기서
    /// 따로 걸러낼 게 없다.
    ///
    /// 라벨은 블록 <b>안쪽 윗부분</b>에 놓이고, 글자 크기는 블록 크기를 따라간다.
    /// 2x2보다 3x3에서 글자가 더 크다.
    ///
    /// <b>Best Fit을 쓰지 않고 폰트 크기를 직접 잰다.</b> Best Fit은 Horizontal Overflow가
    /// Overflow면 가로 제약이 없는 것으로 보고 <b>높이만</b> 맞춰서, 글자가 블록 밖으로 훌쩍
    /// 튀어나가도 그대로 뒀다(실제로 겪음). 지금은 기준 크기로 한 번 재본 preferredWidth/Height에
    /// 맞춰 배율을 구하므로 글자 수가 늘어도("10사이즈") 가로가 먼저 걸려 알아서 줄어든다.
    ///
    /// 퍼즐판은 월드 좌표(SpriteRenderer)인데 이 UI는 Canvas 위에 있어서, 받은 월드 좌표를
    /// 카메라로 화면 좌표까지 변환한 뒤 이 레이어의 로컬 좌표로 옮긴다.
    ///
    /// DamagePopupUI와 같은 방식으로 만들었다 - 시작할 때 template을 복제해 풀에 채워두고
    /// 계속 재사용하며(실행 중 Instantiate 없음), 연출은 라벨마다 코루틴을 띄우지 않고
    /// 이 컴포넌트의 Update 하나가 전부 굴린다.
    /// </summary>
    public class StandUpSizeLabelUI : MonoBehaviour
    {
        [Header("출처")]
        [Tooltip("정사각형이 새로 만들어졌다는 알림을 받을 대상.")]
        [SerializeField] private BoardView boardView;

        [Tooltip("퍼즐판을 비추는 카메라. 월드 좌표를 화면 좌표로 옮기는 데 쓴다. " +
                 "비워두면 Camera.main을 쓴다.")]
        [SerializeField] private Camera boardCamera;

        [Header("표시")]
        [Tooltip("복제해서 풀을 채울 원본. 평소엔 비활성 상태로 씬에 놔둔다.")]
        [SerializeField] private Text template;

        [Tooltip("{0}에 한 변의 칸 수가 들어간다.")]
        [SerializeField] private string labelFormat = "{0}사이즈";

        [Tooltip("블록 윗변에서 안쪽으로 얼마나 들여놓을지(블록 한 변 대비 비율). 0이면 윗변에 딱 붙는다.")]
        [SerializeField] private float topInsetFraction = 0.06f;

        [Tooltip("위에서 정한 자리에서 추가로 밀어 올리거나 내린다(이 레이어의 로컬 단위). 미세 조정용.")]
        [SerializeField] private float verticalOffset;

        [Tooltip("글자 폭을 블록 한 변의 몇 배까지 쓸지. <b>글자 크기를 사실상 이 값이 정한다</b> - " +
                 "가로가 먼저 걸리기 때문. 1이면 블록 폭에 꽉 찬다.")]
        [SerializeField] private float widthFraction = 0.9f;

        [Tooltip("글자 높이 상한(블록 한 변 대비). 가로 기준으로 정한 크기가 이보다 높아지면 " +
                 "그때만 여기에 걸려 더 줄어든다. 보통은 가로가 먼저 걸려서 이 값은 안 쓰인다.")]
        [SerializeField] private float heightFraction = 0.5f;

        [Header("연출")]
        [Tooltip("퐁 하고 나타나는 시간(초).")]
        [SerializeField] private float popInDuration = 0.18f;

        [Tooltip("다 나타난 뒤 그대로 머무는 시간(초). 읽을 시간이라 넉넉히 준다.")]
        [SerializeField] private float holdDuration = 1f;

        [Tooltip("뿅 하고 사라지는 시간(초).")]
        [SerializeField] private float popOutDuration = 0.12f;

        [Tooltip("나타날 때 잠깐 이만큼까지 커졌다가 제 크기로 돌아온다. 1이면 오버슈트 없음.")]
        [SerializeField] private float popOvershoot = 1.25f;

        [Tooltip("동시에 떠 있을 수 있는 라벨 수. 6x8 보드에 정사각형이 최대 12개라 그만큼 잡아둔다.")]
        [SerializeField] private int poolSize = 12;

        private sealed class Label
        {
            public RectTransform rect;
            public Text text;
            public float elapsed;
        }

        private readonly Stack<Label> pool = new Stack<Label>();
        private readonly List<Label> active = new List<Label>();

        private RectTransform selfRect;

        private void Awake()
        {
            selfRect = (RectTransform)transform;

            if (template == null)
                return;

            template.gameObject.SetActive(false); // 원본은 절대 화면에 나오지 않게

            for (int i = 0; i < poolSize; i++)
            {
                var copy = Instantiate(template, transform);
                copy.gameObject.SetActive(false);
                pool.Push(new Label
                {
                    rect = (RectTransform)copy.transform,
                    text = copy
                });
            }
        }

        private void OnEnable()
        {
            if (boardView != null)
                boardView.OnStandUpSquareFormed += HandleSquareFormed;
        }

        private void OnDisable()
        {
            if (boardView != null)
                boardView.OnStandUpSquareFormed -= HandleSquareFormed;
        }

        /// <param name="size">한 변의 칸 수.</param>
        /// <param name="centerWorld">블록 한가운데의 월드 좌표.</param>
        /// <param name="worldSize">블록 한 변의 월드 크기.</param>
        private void HandleSquareFormed(int size, Vector3 centerWorld, float worldSize)
        {
            if (selfRect == null || template == null)
                return;

            var camera = boardCamera != null ? boardCamera : Camera.main;
            if (camera == null)
                return;

            var label = Rent();
            if (label == null)
                return;

            // 월드 → 화면 → 이 레이어의 로컬. Canvas가 Screen Space - Overlay라 마지막 변환에는
            // 카메라를 넘기지 않는다(넘기면 좌표가 어긋난다).
            // 한가운데와 오른쪽 끝을 둘 다 옮겨서, 블록이 화면에서 몇 로컬 단위인지 재는 데 쓴다.
            // 카메라 줌이나 해상도가 어떻든 이 방식이면 항상 블록에 딱 맞는다.
            if (!ToLocal(camera, centerWorld, out Vector2 local) ||
                !ToLocal(camera, centerWorld + new Vector3(worldSize * 0.5f, 0f, 0f), out Vector2 localEdge))
            {
                Return(label);
                return;
            }

            float localSize = Mathf.Abs(localEdge.x - local.x) * 2f;

            // rect를 블록 크기에 맞춰 잡아두면 Text의 Best Fit이 거기 들어가는 글자 크기를
            // 알아서 고른다. 블록이 클수록 rect가 커지고 글자도 따라 커진다.
            //
            // Clamp01이 "글자가 퍼즐보다 커지지 않는다"를 보장하는 자리다. 비율을 1로 묶으면
            // rect가 블록을 절대 벗어나지 않고, Best Fit은 rect 안에 들어가는 크기만 고르므로
            // (가로 넘침이 Overflow라 줄바꿈 없이 한 줄이 통째로 들어갈 때까지 줄인다)
            // 글자도 반드시 블록 안에 머문다. 인스펙터에 1보다 큰 값을 적어도 여기서 잘린다.
            float width = localSize * Mathf.Clamp01(widthFraction);
            float maxHeight = localSize * Mathf.Clamp01(heightFraction);

            // 글자를 먼저 넣고 크기를 정한 뒤, 그 결과에 맞춰 rect를 잡는다(순서가 중요하다 -
            // rect를 먼저 잡아봐야 글자가 얼마나 커질지 모른다).
            label.text.text = string.Format(labelFormat, size);
            float textHeight = FitFontSize(label.text, width, maxHeight);

            // rect 높이를 실제 한 줄 높이에 맞춘다. 넉넉하게 잡아두면 글자가 그 안에서 세로
            // 가운데 정렬돼 위치가 들쭉날쭉해진다.
            label.rect.sizeDelta = new Vector2(width, textHeight);

            // 블록 윗변에 라벨 윗변을 맞추고 안쪽으로 조금 들여놓는다. 피벗은 정중앙으로 두고
            // 중심 y를 직접 계산한다 - 피벗을 위로 옮기면 퐁 하고 커지는 연출이 윗변에 매달려
            // 아래로 자라는 모양이 되어 어색하다.
            float blockTop = local.y + localSize * 0.5f;
            float centerY = blockTop - textHeight * 0.5f - localSize * Mathf.Clamp01(topInsetFraction);
            label.rect.localPosition = new Vector3(local.x, centerY + verticalOffset, 0f);
            label.rect.localScale = Vector3.zero;
            label.elapsed = 0f;

            var color = label.text.color;
            color.a = 1f;
            label.text.color = color;

            label.text.gameObject.SetActive(true);
            active.Add(label);
        }

        /// <summary>
        /// 글자가 <paramref name="width"/> x <paramref name="height"/> 안에 들어가는 가장 큰
        /// 폰트 크기를 잡아준다.
        ///
        /// 방법: 기준 크기(ReferenceFontSize)로 한 번 재보고, 필요한 크기와 실제 필요한 크기의
        /// 비율만큼 줄인다. preferredWidth/Height는 줄바꿈 없이 한 줄로 그렸을 때 필요한 크기라
        /// 가로도 정확히 걸린다. 폰트가 선형으로 커진다는 가정이 들어가는데, 비트맵이 아닌
        /// 다이내믹 폰트라 실제로 선형에 가깝고 마지막에 안전 여유(SafetyMargin)로 덮는다.
        ///
        /// 블록 크기는 기기 해상도가 정해지면 몇 가지로 고정되므로(정사각형 크기가 2~6뿐),
        /// 여기서 나오는 폰트 크기 종류도 몇 개뿐이다 - 폰트 아틀라스가 계속 커질 걱정은 없다.
        ///
        /// <b>Horizontal Overflow가 반드시 Overflow여야 한다.</b> Wrap이면 preferredHeight가
        /// "지금 rect 폭에 맞춰 줄바꿈했을 때의 높이"를 돌려주는데, 그 폭이 아직 정해지기 전이라
        /// 글자마다 줄이 바뀐 것으로 계산돼 필요 높이가 폭증하고 글자가 보이지도 않게 짜부라진다
        /// (실제로 겪음). Overflow면 둘 다 "한 줄로 그렸을 때" 값이라 그대로 믿을 수 있다.
        /// </summary>
        /// <returns>정해진 크기에서 글자 한 줄이 차지하는 높이.</returns>
        private float FitFontSize(Text text, float width, float maxHeight)
        {
            const int ReferenceFontSize = 100;
            const float SafetyMargin = 0.98f;

            text.resizeTextForBestFit = false; // 켜져 있으면 아래에서 정한 크기를 덮어쓴다
            text.fontSize = ReferenceFontSize;

            float neededWidth = text.preferredWidth;
            float neededHeight = text.preferredHeight;
            if (neededWidth <= 0f || neededHeight <= 0f)
                return maxHeight;

            float scale = Mathf.Min(width / neededWidth, maxHeight / neededHeight) * SafetyMargin;
            int fontSize = Mathf.Max(1, Mathf.FloorToInt(ReferenceFontSize * scale));
            text.fontSize = fontSize;

            return neededHeight * fontSize / ReferenceFontSize; // 이 크기에서의 실제 한 줄 높이
        }

        private bool ToLocal(Camera camera, Vector3 world, out Vector2 local)
        {
            Vector2 screen = camera.WorldToScreenPoint(world);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(selfRect, screen, null, out local);
        }

        private Label Rent()
        {
            if (pool.Count > 0)
                return pool.Pop();

            // 풀이 비었으면 새로 만들지 않고 가장 오래된 것을 빼앗아 다시 쓴다 -
            // 실행 중 Instantiate를 한 번도 하지 않기 위한 처리(DamagePopupUI와 같은 방식).
            if (active.Count == 0)
                return null;

            var oldest = active[0];
            active.RemoveAt(0);
            return oldest;
        }

        private void Return(Label label)
        {
            label.text.gameObject.SetActive(false);
            pool.Push(label);
        }

        private void Update()
        {
            if (active.Count == 0)
                return;

            float popIn = Mathf.Max(0.0001f, popInDuration);
            float popOut = Mathf.Max(0.0001f, popOutDuration);
            float total = popIn + Mathf.Max(0f, holdDuration) + popOut;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var label = active[i];
                label.elapsed += Time.deltaTime;

                if (label.elapsed >= total)
                {
                    active.RemoveAt(i);
                    Return(label);
                    continue;
                }

                label.rect.localScale = Vector3.one * ScaleAt(label.elapsed, popIn, popOut, total);
            }
        }

        /// <summary>
        /// 0 → (오버슈트) → 1 로 튀어나왔다가, 머무는 동안 1을 유지하고, 마지막에 0으로 줄어든다.
        /// 사라질 때 알파가 아니라 크기를 줄이는 이유: "뿅" 하고 사라지는 느낌은 페이드보다
        /// 스케일 쪽이 확실하고, 알파를 건드리면 풀에 돌려줄 때 되돌릴 상태가 하나 더 는다.
        /// </summary>
        private float ScaleAt(float elapsed, float popIn, float popOut, float total)
        {
            if (elapsed < popIn)
            {
                // 0 → overshoot → 1. sin으로 한 번 넘겼다가 돌아오게 한다.
                float t = elapsed / popIn;
                float overshoot = (popOvershoot - 1f) * Mathf.Sin(t * Mathf.PI);
                return t + overshoot;
            }

            float outStart = total - popOut;
            if (elapsed < outStart)
                return 1f;

            return 1f - (elapsed - outStart) / popOut;
        }
    }
}
