using System.Collections;
using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 캐릭터 몇을 <b>한 덩어리로</b> 화면 밖까지 뛰어 내보내거나, 밖에서 제자리로 뛰어 들어오게
    /// 한다(2026-08-28 사용자 지시: 준비 화면에서 나가고 배틀 화면으로 뛰어 들어온다).
    ///
    /// "귀엽게"는 <b>깡충깡충</b>으로 풀었다 - 가로로 미끄러지기만 하면 무빙워크에 실린 것처럼
    /// 보인다. 뛰는 동안 위아래로 통통 튀고, <b>발이 땅에 닿을 때마다</b> 먼지를 피운다
    /// (<see cref="DustPuffUI"/>).
    ///
    /// <b>⚠ 제자리는 Awake 에서 잡는다.</b> 배틀 화면의 초상화는 <see cref="HitFlinchUI"/>·
    /// <see cref="StartleHopUI"/>·<see cref="PortraitCloseUpUI"/> 와 <b>같은 RectTransform</b> 을
    /// 쓰고 그것들도 전부 Awake 에서 자기 기준값을 잡는다. 그래서 이 연출은 <b>Start 이후에만</b>
    /// 움직여야 한다 - Awake 에 움직이면 그들이 "화면 밖"을 제자리로 기억한다.
    /// </summary>
    public class RunAcrossUI : MonoBehaviour
    {
        [Tooltip("같이 움직일 것들. 아군 둘이면 둘 다, 적이면 하나. " +
                 "<b>덩어리째</b> 움직이므로 서로의 간격은 그대로 유지된다.")]
        [SerializeField] private RectTransform[] targets = new RectTransform[0];

        [Tooltip("발밑 먼지. 비워두면 먼지 없이 뛰기만 한다.")]
        [SerializeField] private DustPuffUI dust;

        [Tooltip("화면 밖까지 뛰는 데 걸리는 시간(초).")]
        [SerializeField] private float runDuration = 0.75f;

        [Tooltip("화면 가장자리에서 얼마나 더 나가야 '밖'인지(유닛). 캐릭터가 칸보다 크게 " +
                 "그려지므로 넉넉해야 꼬리가 안 걸린다.")]
        [SerializeField] private float offscreenMargin = 220f;

        [Tooltip("뛰는 동안 통통 튀는 높이(유닛).")]
        [SerializeField] private float hopHeight = 22f;

        [Tooltip("나가는(들어오는) 동안 몇 번 튈지.")]
        [SerializeField] private int hopCount = 3;

        [Tooltip("착지할 때마다 피우는 먼지 장수.")]
        [SerializeField] private int puffsPerLanding = 3;

        [Tooltip("먼지가 나올 자리를 발밑으로 내리는 거리(유닛). 초상화 중심에서 아래로.")]
        [SerializeField] private float dustFootOffset = 60f;

        // 씬에 적혀 있던 제자리. 들어오는 연출은 <b>정확히 여기</b>서 멈춰야 한다.
        private Vector2[] basePositions;
        private bool captured;

        private void Awake()
        {
            Capture();
        }

        private void Capture()
        {
            if (captured)
                return;

            captured = true;
            basePositions = new Vector2[targets.Length];

            for (int i = 0; i < targets.Length; i++)
                basePositions[i] = targets[i] != null ? targets[i].anchoredPosition : Vector2.zero;
        }

        /// <summary>
        /// 화면 밖까지의 거리. <b>실제 화면 폭을 잰다</b> - 가로는 기기마다 다르다
        /// (<see cref="StandUpTimeUI"/>·<see cref="RushTimeBannerUI"/> 와 같은 이유).
        ///
        /// <b>⚠ 캔버스 폭을 그냥 쓰면 안 된다</b>(2026-08-30에 고침): 이 판이 레터박스로 축소돼
        /// 있으면(스테이지 준비 화면이 그렇다) 캔버스 단위 거리는 그만큼 <b>모자라서</b>
        /// 캐릭터가 화면 끝에 걸친 채로 멈춘다. <see cref="UiScreenMetrics"/> 가 배율을 반영한다.
        /// </summary>
        private float TravelDistance()
        {
            // anchoredPosition 은 <b>대상의 부모</b> 공간이므로 거기서 재야 한다.
            var basis = transform as RectTransform;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null && targets[i].parent is RectTransform parent)
                {
                    basis = parent;
                    break;
                }
            }

            float half = UiScreenMetrics.ScreenHalfInLocalUnits(basis).x;
            return (half > 0f ? half : 400f) + offscreenMargin;
        }

        /// <summary>제자리에서 화면 밖(<paramref name="dirX"/> 쪽)으로 뛰어 나간다.</summary>
        public IEnumerator RunOut(float dirX)
        {
            Capture();
            yield return Move(Vector2.zero, new Vector2(Mathf.Sign(dirX) * TravelDistance(), 0f), dirX);
        }

        /// <summary>
        /// 화면 밖(<paramref name="dirX"/> 쪽)에서 제자리로 뛰어 들어온다.
        /// <b>먼저 <see cref="SnapOffscreen"/> 로 밖에 세워둬야 한다</b> - 안 그러면 제자리에
        /// 서 있다가 갑자기 밖으로 튀었다 돌아온다.
        /// </summary>
        public IEnumerator RunIn(float dirX)
        {
            Capture();
            yield return Move(new Vector2(Mathf.Sign(dirX) * TravelDistance(), 0f), Vector2.zero, -dirX);
        }

        /// <summary>연출을 시작하기 전에 화면 밖에 세워둔다. 한 프레임도 제자리가 보이면 안 된다.</summary>
        public void SnapOffscreen(float dirX)
        {
            Capture();
            ApplyOffset(new Vector2(Mathf.Sign(dirX) * TravelDistance(), 0f), 0f);
        }

        /// <summary>제자리로 되돌린다.</summary>
        public void SnapHome()
        {
            Capture();
            ApplyOffset(Vector2.zero, 0f);
        }

        private IEnumerator Move(Vector2 from, Vector2 to, float awayX)
        {
            if (runDuration <= 0f)
            {
                ApplyOffset(to, 0f);
                yield break;
            }

            int landings = 0;
            float elapsed = 0f;

            while (elapsed < runDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / runDuration);

                // 가로는 등속에 가깝게(달리는 것이므로), 다만 출발만 살짝 채어 나간다.
                float travel = Mathf.Pow(t, 0.85f);
                var offset = Vector2.LerpUnclamped(from, to, travel);

                // 위아래 통통. Abs(sin) 이라 <b>바닥에서 튀어오르는</b> 모양이 된다
                // (그냥 sin 이면 땅속으로도 들어간다).
                float hopPhase = t * Mathf.Max(1, hopCount) * Mathf.PI;
                float hop = Mathf.Abs(Mathf.Sin(hopPhase)) * hopHeight;

                ApplyOffset(offset, hop);

                // 착지 순간마다 먼지. sin 이 0을 지나는 지점이 곧 착지다.
                int landed = Mathf.FloorToInt(hopPhase / Mathf.PI);
                if (landed > landings)
                {
                    landings = landed;
                    Puff(awayX);
                }

                yield return null;
            }

            ApplyOffset(to, 0f);
            Puff(awayX);
        }

        private void ApplyOffset(Vector2 offset, float hop)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                    continue;

                targets[i].anchoredPosition = basePositions[i] + offset + new Vector2(0f, hop);
            }
        }

        private void Puff(float awayX)
        {
            if (dust == null || puffsPerLanding <= 0)
                return;

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                    continue;

                // 먼지 층과 캐릭터의 부모가 다를 수 있으므로 월드를 거쳐 자리를 옮긴다.
                dust.Burst(FootAnchoredPosition(targets[i]), awayX, puffsPerLanding);
            }
        }

        /// <summary>그 캐릭터의 발밑이 먼지 층의 좌표로는 어디인지.</summary>
        private Vector2 FootAnchoredPosition(RectTransform target)
        {
            var layer = dust.transform as RectTransform;
            if (layer == null)
                return target.anchoredPosition;

            Vector3 world = target.TransformPoint(new Vector3(0f, -dustFootOffset, 0f));
            Vector3 local = layer.InverseTransformPoint(world);
            return new Vector2(local.x, local.y);
        }
    }
}
