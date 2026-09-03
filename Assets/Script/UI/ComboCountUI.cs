using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.Audio;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 매치가 성립할 때마다 <b>그 매치 자리(판 안쪽으로 당겨서)</b>에 지금까지의 매치 횟수를 띄운다.
    /// 그리고 5회, 이후 10의 배수마다(10·20·30…) 칭찬 음성이 나오고 그 대사 이미지가
    /// 숫자 위에 함께 뜬다.
    ///
    /// 대사와 음성은 <see cref="PraiseLine"/> 목록으로 짝지어 둔다 - 음성 폴더 하나가 대사 하나이고
    /// 그 안의 여러 파일은 같은 대사의 다른 녹음이라, <b>대사를 고른 뒤 그 안에서 음성을 다시 고른다.</b>
    /// 이렇게 두면 대사마다 녹음 수가 달라도(1개짜리도, 4개짜리도) 대사가 뽑힐 확률은 똑같다 -
    /// 전체 음성에서 하나를 뽑으면 녹음이 많은 대사만 자주 나온다.
    ///
    /// 표시는 DamagePopupUI·StandUpSizeLabelUI와 같은 방식이다 - 시작할 때 template을 복제해
    /// 풀을 채워두고 계속 재사용하며(실행 중 Instantiate 없음), 라벨마다 코루틴을 띄우지 않고
    /// 이 컴포넌트의 Update 하나가 전부 굴린다.
    /// </summary>
    public class ComboCountUI : MonoBehaviour
    {
        /// <summary>칭찬 대사 하나 - 화면에 뜰 이미지와 그 대사의 음성 녹음들.</summary>
        [System.Serializable]
        public class PraiseLine
        {
            [Tooltip("숫자 위에 뜰 대사 이미지.")]
            public Sprite image;

            [Tooltip("같은 대사의 음성 녹음들. 이 중에서 하나가 무작위로 나온다.")]
            public AudioClip[] voices;
        }

        [Header("출처")]
        [Tooltip("매치가 성립했다는 알림을 받을 대상.")]
        [SerializeField] private BoardInputController boardInput;

        [Tooltip("칭찬 음성을 재생할 대상. 비워두면 소리 없이 이미지만 뜬다.")]
        [SerializeField] private SfxPlayer sfx;

        [Tooltip("글자·그림 크기를 퍼즐 한 칸에 맞추기 위해 참조한다. " +
                 "비워두면 아래 fallbackCellSize 를 쓴다.")]
        [SerializeField] private BoardView boardView;

        [Tooltip("퍼즐판을 비추는 카메라. 비워두면 Camera.main.")]
        [SerializeField] private Camera boardCamera;

        [Header("표시")]
        [Tooltip("복제해서 풀을 채울 원본. 평소엔 비활성 상태로 씬에 놔둔다.")]
        [SerializeField] private Text template;

        [Tooltip("{0}에 지금까지의 매치 횟수가 들어간다.")]
        [SerializeField] private string countFormat = "{0}";

        [Tooltip("매치 자리에서 얼마나 띄워서 보여줄지 - <b>퍼즐 한 칸 크기의 배수</b>. " +
                 "손가락에 가려지지 않도록 위로 올려둔다. 칸 크기를 기준으로 재므로 " +
                 "화면 크기가 달라져도 같은 느낌이 난다.")]
        [SerializeField] private Vector2 cursorOffsetInCells = new Vector2(0f, 1.2f);

        [Tooltip("숫자가 뜰 수 있는 범위를 퍼즐판 안쪽으로 얼마나 좁힐지(0~1). " +
                 "0.3이면 판 가장자리에서 30%만큼 안쪽까지만 뜬다. " +
                 "가장자리에서 매치해도 글자가 화면 밖으로 잘리지 않게 하는 값이다.")]
        [Range(0f, 0.9f)]
        [SerializeField] private float edgeInsetFraction = 0.3f;

        [Header("크기")]
        [Tooltip("대사 그림의 높이 - <b>숫자 글꼴 크기의 몇 배</b>로 할지. 가로는 그림 비율대로 따라간다. " +
                 "글꼴에 묶어두면 타이포그래피 단계를 바꿔도 둘의 비례가 유지된다.")]
        [SerializeField] private float praiseHeightFactor = 1.1f;

        [Tooltip("숫자 윗변과 대사 그림 아랫변 <b>사이의 틈</b> - 숫자 글꼴 크기의 몇 배. " +
                 "0이면 딱 붙고 음수면 살짝 겹친다. 예전엔 '숫자 중심에서 올릴 거리'였는데 " +
                 "그러면 글자 크기를 바꿀 때마다 행간이 따라 벌어져서 틈 자체를 적도록 바꿨다.")]
        [SerializeField] private float praiseGapFactor = 0.05f;

        [Tooltip("퍼즐판을 못 찾았을 때 쓸 한 칸 크기(이 레이어의 로컬 단위).")]
        [SerializeField] private float fallbackCellSize = 90f;

        [Header("칭찬")]
        [Tooltip("첫 칭찬이 나오는 횟수. 이 뒤로는 아래 간격의 배수마다 나온다.")]
        [Min(1)]
        [SerializeField] private int firstMilestone = 5;

        [Tooltip("첫 칭찬 이후 칭찬이 나오는 간격. 10이면 10·20·30…에서 나온다.")]
        [Min(1)]
        [SerializeField] private int milestoneStep = 10;

        [Tooltip("대사와 음성을 짝지은 목록. 칭찬할 때 이 중 하나를 통째로 고르고, " +
                 "고른 대사의 음성 중에서 다시 하나를 고른다.")]
        [SerializeField] private PraiseLine[] praiseLines;

        [Header("연출")]
        [Tooltip("퐁 하고 나타나는 시간(초).")]
        [SerializeField] private float popInDuration = 0.14f;

        [Tooltip("숫자만 뜰 때 머무는 시간(초).")]
        [SerializeField] private float holdDuration = 0.5f;

        [Tooltip("칭찬이 함께 뜰 때 머무는 시간(초). 대사를 읽을 시간이라 더 길게 둔다.")]
        [SerializeField] private float praiseHoldDuration = 1.1f;

        [Tooltip("사라지는 시간(초).")]
        [SerializeField] private float fadeOutDuration = 0.2f;

        [Tooltip("나타날 때 잠깐 이만큼까지 커졌다가 제 크기로 돌아온다. 1이면 오버슈트 없음.")]
        [SerializeField] private float popOvershoot = 1.3f;

        [Tooltip("칭찬일 때 숫자를 이만큼 더 크게 보여준다.")]
        [SerializeField] private float praiseScale = 1.35f;

        [Tooltip("동시에 떠 있을 수 있는 라벨 수. 캐스케이드로 여러 매치가 겹칠 수 있어 넉넉히 잡는다.")]
        [SerializeField] private int poolSize = 8;

        /// <summary>이번 배틀에서 지금까지 성립한 매치 수.</summary>
        public int Count { get; private set; }

        private sealed class Label
        {
            public RectTransform rect;
            public Text text;
            public Image praise;      // 없을 수도 있다(원본에 자식 이미지를 안 붙인 경우)
            public float elapsed;     // 음수면 쉬는 중
            public float hold;
            public float baseScale;
        }

        private readonly List<Label> pool = new List<Label>();
        private RectTransform layerRect;

        // 직전에 고른 대사. 같은 대사가 연달아 나오면 녹음이 여러 개여도 반복처럼 들린다.
        private int lastPraiseIndex = -1;

        private void Awake()
        {
            layerRect = transform as RectTransform;
            BuildPool();
        }

        private void OnEnable()
        {
            if (boardInput != null)
                boardInput.OnMatchCounted += HandleMatchCounted;
        }

        private void OnDisable()
        {
            if (boardInput != null)
                boardInput.OnMatchCounted -= HandleMatchCounted;
        }

        /// <summary>
        /// 배틀이 새로 시작될 때 횟수를 0으로 되돌린다. BattleManager가 불러준다 -
        /// 안 그러면 지난 판의 횟수가 이어져서 시작하자마자 칭찬이 튀어나온다.
        /// </summary>
        public void ResetForNewBattle()
        {
            Count = 0;
            lastPraiseIndex = -1;

            for (int i = 0; i < pool.Count; i++)
                Hide(pool[i]);
        }

        private void BuildPool()
        {
            if (template == null)
                return;

            template.gameObject.SetActive(false);

            for (int i = 0; i < Mathf.Max(1, poolSize); i++)
            {
                var text = Instantiate(template, template.transform.parent);
                text.gameObject.SetActive(false);

                var rect = text.rectTransform;
                var label = new Label
                {
                    rect = rect,
                    text = text,
                    praise = text.GetComponentInChildren<Image>(true),
                    elapsed = -1f,
                    baseScale = rect.localScale.x <= 0f ? 1f : rect.localScale.x
                };

                pool.Add(label);
            }
        }

        private void HandleMatchCounted(Vector3 pivotWorldPosition)
        {
            Count++;

            bool milestone = IsMilestone(Count);
            PraiseLine praise = milestone ? PickPraise() : null;

            Show(pivotWorldPosition, Count, praise);

            if (praise != null && sfx != null)
            {
                var clip = PickVoice(praise);
                if (clip != null)
                    sfx.PlayVoice(clip);
            }
        }

        /// <summary>5회, 그다음부터는 10·20·30… 처럼 간격의 배수마다.</summary>
        private bool IsMilestone(int count)
        {
            if (count == firstMilestone)
                return true;

            int step = Mathf.Max(1, milestoneStep);
            return count >= step && count % step == 0;
        }

        /// <summary>
        /// 대사를 먼저 고른다. 직전과 같은 대사가 뽑히면 한 번만 다시 뽑는다 -
        /// 계속 다시 뽑으면 대사가 하나뿐일 때 무한 루프가 된다.
        /// </summary>
        private PraiseLine PickPraise()
        {
            if (praiseLines == null || praiseLines.Length == 0)
                return null;

            int index = Random.Range(0, praiseLines.Length);
            if (index == lastPraiseIndex && praiseLines.Length > 1)
                index = (index + 1 + Random.Range(0, praiseLines.Length - 1)) % praiseLines.Length;

            lastPraiseIndex = index;
            return praiseLines[index];
        }

        private AudioClip PickVoice(PraiseLine line)
        {
            if (line.voices == null || line.voices.Length == 0)
                return null;

            return line.voices[Random.Range(0, line.voices.Length)];
        }

        /// <summary>
        /// 퍼즐 한 칸이 이 레이어의 로컬 단위로 얼마인지. 퍼즐판은 월드 스프라이트고 이 UI는
        /// Canvas 위라 단위가 달라서, 붙어 있는 두 칸의 좌표를 각각 화면 → 로컬로 옮겨 그 거리를 잰다.
        ///
        /// 고정된 픽셀 값을 쓰지 않는 이유: 카메라가 기기 비율에 맞춰 판 크기를 바꾸므로
        /// (CameraFitter) 픽셀로 박아두면 기기마다 글자가 조각보다 크거나 작아진다.
        /// </summary>
        private float GetCellLocalSize()
        {
            var cam = boardCamera != null ? boardCamera : Camera.main;
            if (boardView == null || cam == null || layerRect == null)
                return fallbackCellSize;

            Vector3 originScreen = cam.WorldToScreenPoint(boardView.GridToWorld(0, 0));
            Vector3 nextScreen = cam.WorldToScreenPoint(boardView.GridToWorld(1, 0));

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    layerRect, originScreen, null, out var a) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    layerRect, nextScreen, null, out var b))
            {
                return fallbackCellSize;
            }

            float size = Vector2.Distance(a, b);
            return size > 1f ? size : fallbackCellSize;
        }

        /// <summary>
        /// 퍼즐판 월드 좌표를 이 레이어의 로컬 좌표로 옮긴다. 판은 월드 스프라이트고 이 UI는
        /// Canvas 위라 화면 좌표를 한 번 거쳐야 한다.
        /// </summary>
        private bool TryWorldToLocal(Vector3 world, out Vector2 local)
        {
            local = Vector2.zero;
            var cam = boardCamera != null ? boardCamera : Camera.main;
            if (cam == null || layerRect == null)
                return false;

            Vector3 screen = cam.WorldToScreenPoint(world);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(layerRect, screen, null, out local);
        }

        /// <summary>
        /// 퍼즐판 안쪽으로 좁힌 영역에 가둔다 - <b>그 매치에서 가장 가까운 안전한 자리</b>가 된다.
        ///
        /// 판 네 귀퉁이를 로컬로 옮겨 영역을 잡고 edgeInsetFraction 만큼 안으로 좁힌다.
        /// 판 크기를 직접 재므로 기기 비율이 달라져도(CameraFitter 가 판 크기를 바꾼다) 따라온다.
        /// </summary>
        private Vector2 ClampIntoBoard(Vector2 local)
        {
            if (boardView == null)
                return local;

            var bounds = boardView.GetBoardVisualBounds();
            if (!TryWorldToLocal(bounds.min, out var min) || !TryWorldToLocal(bounds.max, out var max))
                return local;

            // 카메라 방향에 따라 min/max 가 뒤집혀 나올 수 있어 정렬한다.
            Vector2 lo = Vector2.Min(min, max);
            Vector2 hi = Vector2.Max(min, max);

            Vector2 center = (lo + hi) * 0.5f;
            Vector2 half = (hi - lo) * 0.5f * (1f - Mathf.Clamp01(edgeInsetFraction));

            return new Vector2(
                Mathf.Clamp(local.x, center.x - half.x, center.x + half.x),
                Mathf.Clamp(local.y, center.y - half.y, center.y + half.y));
        }

        private void Show(Vector3 pivotWorldPosition, int count, PraiseLine praise)
        {
            var label = GetIdleLabel();
            if (label == null)
                return;

            float cell = GetCellLocalSize();

            label.text.text = string.Format(countFormat, count);
            // 글자 크기는 퍼즐 칸이 아니라 <b>타이포그래피 단계</b>를 따른다 - 데미지 숫자와
            // 같은 단계라야 둘이 한 몸으로 읽힌다(UITypography 참고).
            int fontSize = UITypography.Headline;
            label.text.fontSize = fontSize;

            if (TryWorldToLocal(pivotWorldPosition, out var local))
            {
                // <b>판 안쪽으로 당겨서 띄운다.</b> pivot 그대로 두면 가장자리 매치에서 숫자와
                // 대사가 화면 밖으로 잘린다. 안쪽으로 좁힌 영역에 가두면 "그 매치에서 제일 가까운
                // 안전한 자리"가 되어 어디서 맞췄는지도 여전히 읽힌다.
                label.rect.anchoredPosition = ClampIntoBoard(local) + cursorOffsetInCells * cell;
            }

            if (label.praise != null)
            {
                bool hasImage = praise != null && praise.image != null;
                label.praise.sprite = hasImage ? praise.image : null;
                label.praise.gameObject.SetActive(hasImage);

                // SetNativeSize는 쓰지 않는다 - 대사 그림마다 원본 크기가 제각각이라
                // 어떤 건 화면을 덮고 어떤 건 작게 뜬다. 정해진 칸 안에서 비율만 지키게
                // 두면(Image의 Preserve Aspect) 어떤 그림이 와도 같은 높이로 보인다.
                if (hasImage)
                {
                    float height = fontSize * praiseHeightFactor;

                    // 가로는 넉넉히 잡아두고 Preserve Aspect 가 알아서 높이에 맞춘다
                    // (그림 중 가장 넓은 것이 2.8:1 이라 4배면 항상 높이가 먼저 걸린다).
                    var praiseRect = label.praise.rectTransform;
                    praiseRect.sizeDelta = new Vector2(height * 4f, height);

                    // 숫자 윗변 바로 위에 붙인다. 글꼴 상자가 아니라 <b>글자가 실제로 차지하는
                    // 높이</b>(상자의 70% 남짓)를 기준으로 잡아야 행간이 뜨지 않는다 - 상자를
                    // 기준으로 하면 위아래 빈 공간까지 틈으로 더해진다.
                    float digitHalf = fontSize * 0.35f;
                    praiseRect.anchoredPosition =
                        new Vector2(0f, digitHalf + height * 0.5f + fontSize * praiseGapFactor);
                }
            }

            label.hold = praise != null ? praiseHoldDuration : holdDuration;
            label.baseScale = praise != null ? praiseScale : 1f;
            label.elapsed = 0f;
            label.rect.gameObject.SetActive(true);
        }

        private Label GetIdleLabel()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i].elapsed < 0f)
                    return pool[i];
            }

            // 전부 쓰는 중이면 가장 오래된 것을 뺏는다 - 새 숫자가 안 뜨는 것보다 낫다.
            Label oldest = null;
            for (int i = 0; i < pool.Count; i++)
            {
                if (oldest == null || pool[i].elapsed > oldest.elapsed)
                    oldest = pool[i];
            }
            return oldest;
        }

        private void Update()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                var label = pool[i];
                if (label.elapsed < 0f)
                    continue;

                label.elapsed += Time.deltaTime;

                float popIn = Mathf.Max(0.01f, popInDuration);
                float fadeOut = Mathf.Max(0.01f, fadeOutDuration);
                float total = popIn + label.hold + fadeOut;

                if (label.elapsed >= total)
                {
                    Hide(label);
                    continue;
                }

                float scale = label.baseScale;
                float alpha = 1f;

                if (label.elapsed < popIn)
                {
                    // 살짝 넘겼다가 제 크기로 - 숫자가 "톡" 하고 튀어나오는 느낌
                    float p = label.elapsed / popIn;
                    float overshoot = Mathf.Sin(p * Mathf.PI) * (popOvershoot - 1f);
                    scale *= Mathf.Lerp(0.4f, 1f, p) + overshoot;
                }
                else if (label.elapsed > popIn + label.hold)
                {
                    float p = (label.elapsed - popIn - label.hold) / fadeOut;
                    alpha = 1f - p;
                    scale *= 1f + p * 0.15f; // 사라지면서 살짝 커져 흩어지는 느낌
                }

                label.rect.localScale = Vector3.one * scale;
                SetAlpha(label, alpha);
            }
        }

        private void SetAlpha(Label label, float alpha)
        {
            var color = label.text.color;
            color.a = alpha;
            label.text.color = color;

            if (label.praise != null && label.praise.gameObject.activeSelf)
            {
                var praiseColor = label.praise.color;
                praiseColor.a = alpha;
                label.praise.color = praiseColor;
            }
        }

        private void Hide(Label label)
        {
            label.elapsed = -1f;
            label.rect.localScale = Vector3.one;
            SetAlpha(label, 1f);
            label.rect.gameObject.SetActive(false);
        }
    }
}
