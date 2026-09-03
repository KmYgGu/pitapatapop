using System.Collections;
using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 배틀 화면에 서 있던 <b>그 초상화</b>를 화면 한가운데로 부드럽게 끌어와 크게 키운다.
    /// 승리·패배 화면이 이걸 먼저 돌리고 나서 뜬다(2026-08-25 사용자 지시) - 초상화를 따로
    /// 하나 더 세우는 것보다 "방금까지 싸우던 그 캐릭터"라는 게 훨씬 분명하게 읽힌다.
    ///
    /// <b>카메라가 다가오는 것처럼 움직인다.</b> 기준점에서 얼마나 떨어져 있는지를 그대로
    /// <see cref="zoom"/> 배 해서 밀고, 크기도 같은 배로 키운다. 그래서
    /// <b>둘 사이의 거리도 같은 배로 벌어진다</b> - 배틀에서 나란히 서 있었으면 확대해도
    /// 나란히 선 채 커진다(2026-08-25 사용자 요구).
    ///
    /// <b>초상화를 옮기지 않고 자기 자리에서 밀어낸다.</b> 부모를 갈아끼우면 HudContent 의
    /// 퍼센트 앵커 배치가 통째로 깨지고 되돌리기도 어렵다.
    ///
    /// <b>그리기 순서 문제</b>: 초상화는 HudContent 안에 있어서 결과 화면들보다 아래에 그려진다.
    /// 그대로 두면 확대해봐야 배경에 가린다. 그래서 확대하는 동안만 <see cref="Canvas"/> 를 하나
    /// 얹어 <c>overrideSorting</c> 으로 맨 앞에 세운다 - 끝나면 그 Canvas 를 꺼서 원래대로 돌린다.
    ///
    /// <b>크기를 "화면 높이의 몇 할"로 잡지 말 것</b>(2026-08-25 실제로 실패했다) - 재는 건
    /// RectTransform 인데 그 안의 Spine 캐릭터는 칸을 한참 넘쳐서 그려진다. 칸을 화면의 42%로
    /// 맞췄더니 캐릭터가 화면을 뒤덮었다. <b>배율로 잡는 게 맞다.</b>
    /// </summary>
    public class PortraitCloseUpUI : MonoBehaviour
    {
        [Tooltip("확대할 초상화. 비워두면 이 컴포넌트가 붙은 오브젝트를 쓴다.")]
        [SerializeField] private RectTransform target;

        [Tooltip("화면 한가운데를 재는 기준. 보통 Canvas 의 RectTransform. " +
                 "비워두면 부모를 거슬러 올라가 Canvas 를 찾는다.")]
        [SerializeField] private RectTransform canvasRect;

        [Header("확대")]
        [Tooltip("얼마나 다가올지. 크기와 <b>서로의 거리</b>가 함께 이 배가 된다. " +
                 "1이면 자리만 옮기고 크기는 그대로다.")]
        [SerializeField] private float zoom = 1.6f;

        [Tooltip("확대가 끝났을 때 화면 한가운데에서 얼마나 비켜 있을지" +
                 "(<b>캔버스 세로</b> 대비 비율). 여럿이 함께 확대되면 <b>그 무리의 한가운데</b>가 " +
                 "이 자리로 온다.\n" +
                 "가로도 세로 길이로 재는 이유: 캔버스 가로는 기기 비율마다 달라서 " +
                 "가로로 재면 좁은 폰에서 자리가 달라진다(세로는 어떤 기기에서도 600이다).")]
        [SerializeField] private Vector2 focusOffset = new Vector2(0f, 0.06f);

        [Header("타이밍")]
        [Tooltip("끌어오는 데 걸리는 시간(초).")]
        [SerializeField] private float duration = 0.8f;

        [Tooltip("맨 앞에 세울 때 쓸 정렬 순서. 결과 화면 배경보다 커야 한다. " +
                 "여럿이 겹칠 때는 이 값으로 앞뒤를 가른다(리더가 파트너보다 커야 앞에 온다).")]
        [SerializeField] private int sortingOrder = 300;

        /// <summary>지금 확대돼 있는지.</summary>
        public bool IsClosedUp { get; private set; }

        // 원래 상태. 되돌릴 때 정확히 이 값으로 돌아간다.
        private Vector2 basePosition;
        private Vector3 baseScale;
        private bool hasBaseState;

        // 확대하는 동안만 켜는 Canvas. 한 번 만들고 껐다 켠다.
        private Canvas overrideCanvas;

        private RectTransform Target => target != null ? target : transform as RectTransform;

        /// <summary>혼자 확대한다. 자기 자신이 기준이라 화면 한가운데로 온다.</summary>
        public IEnumerator Play() => Play(GetCanvasCenter());

        /// <summary>
        /// 무리로 확대한다. <paramref name="groupCenter"/> 를 기준으로 밀려나므로,
        /// <b>여럿이 같은 기준을 받으면 서로의 거리가 그대로 zoom 배로 벌어진다</b> -
        /// 배틀에서 나란히 서 있던 둘이 겹치지 않고 나란히 선 채 커진다.
        /// </summary>
        /// <param name="groupCenter">무리의 한가운데(캔버스 좌표). <see cref="GetCanvasCenter"/> 들의 평균.</param>
        public IEnumerator Play(Vector2 groupCenter)
        {
            var rect = Target;
            if (rect == null)
                yield break;

            CaptureBaseState(rect);
            RaiseToFront(rect);
            IsClosedUp = true;

            Vector2 to = ResolveTargetPosition(rect, groupCenter);
            Vector3 toScale = baseScale * Mathf.Max(0.01f, zoom);

            if (duration <= 0f)
            {
                rect.anchoredPosition = to;
                rect.localScale = toScale;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                // 시작도 끝도 부드럽게(smoothstep) - "카메라가 다가온다"로 읽히려면 양쪽 끝이
                // 다 뭉툭해야 한다. 한쪽만 감속하면 튀어나왔다 멎는 느낌이 난다.
                float t = Mathf.Clamp01(elapsed / duration);
                float e = t * t * (3f - 2f * t);

                rect.anchoredPosition = Vector2.Lerp(basePosition, to, e);
                rect.localScale = Vector3.Lerp(baseScale, toScale, e);

                yield return null;
            }

            rect.anchoredPosition = to;
            rect.localScale = toScale;
        }

        /// <summary>
        /// 지금 이 초상화의 한가운데가 캔버스 좌표로 어디인지. 무리의 기준점을 구할 때
        /// 부르는 쪽이 이걸 평균 낸다.
        /// </summary>
        public Vector2 GetCanvasCenter()
        {
            var rect = Target;
            var canvas = ResolveCanvasRect(rect);
            if (rect == null || canvas == null)
                return Vector2.zero;

            return canvas.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
        }

        /// <summary>확대를 풀고 원래 자리로 즉시 되돌린다.</summary>
        public void Reset()
        {
            IsClosedUp = false;

            var rect = Target;
            if (rect != null && hasBaseState)
            {
                rect.anchoredPosition = basePosition;
                rect.localScale = baseScale;
            }

            if (overrideCanvas != null)
                overrideCanvas.enabled = false;
        }

        private void OnDisable()
        {
            // 확대된 채로 꺼지면 그 상태로 굳는다.
            if (IsClosedUp)
                Reset();
        }

        private void CaptureBaseState(RectTransform rect)
        {
            if (hasBaseState)
                return;

            basePosition = rect.anchoredPosition;
            baseScale = rect.localScale;
            hasBaseState = true;
        }

        /// <summary>
        /// 확대가 끝났을 때 있어야 할 anchoredPosition.
        ///
        ///   목표 = 화면 한가운데(+오프셋) + (지금 자리 - 무리 한가운데) x zoom
        ///
        /// 카메라가 무리 한가운데로 다가오는 것과 같은 식이라, <b>서로의 거리도 크기와 같은 배로</b>
        /// 벌어진다. 그래서 배틀에서 안 겹쳐 있었으면 확대해도 안 겹친다.
        /// </summary>
        private Vector2 ResolveTargetPosition(RectTransform rect, Vector2 groupCenter)
        {
            var canvas = ResolveCanvasRect(rect);
            if (canvas == null)
                return basePosition;

            Vector2 current = GetCanvasCenter();

            // 가로도 세로 길이를 기준으로 잰다 - 캔버스 가로는 기기 비율마다 달라진다.
            Vector2 focus = new Vector2(canvas.rect.height * focusOffset.x,
                                        canvas.rect.height * focusOffset.y);

            Vector2 wanted = focus + (current - groupCenter) * Mathf.Max(0.01f, zoom);

            // 부모 기준 차이로 바꿔 더한다. 캔버스와 부모의 배율이 다를 수 있으므로
            // 방향만 쓰지 말고 부모 공간으로 옮겨서 재야 한다.
            var parent = rect.parent as RectTransform;
            if (parent == null)
                return basePosition;

            Vector2 delta = (Vector2)parent.InverseTransformPoint(canvas.TransformPoint(wanted))
                            - (Vector2)parent.InverseTransformPoint(canvas.TransformPoint(current));

            return basePosition + delta;
        }

        private RectTransform ResolveCanvasRect(RectTransform rect)
        {
            if (canvasRect != null)
                return canvasRect;

            if (rect == null)
                return null;

            var canvas = rect.GetComponentInParent<Canvas>();
            if (canvas != null)
                canvasRect = canvas.rootCanvas.transform as RectTransform;

            return canvasRect;
        }

        /// <summary>
        /// 확대하는 동안만 맨 앞에 세운다. 초상화는 HudContent 안이라 결과 화면 배경보다
        /// 아래에 그려지는데, 그대로면 확대해봐야 가려서 안 보인다.
        /// </summary>
        private void RaiseToFront(RectTransform rect)
        {
            if (overrideCanvas == null)
            {
                overrideCanvas = rect.GetComponent<Canvas>();
                if (overrideCanvas == null)
                    overrideCanvas = rect.gameObject.AddComponent<Canvas>();
            }

            overrideCanvas.enabled = true;
            overrideCanvas.overrideSorting = true;
            overrideCanvas.sortingOrder = sortingOrder;
        }
    }
}
