using System.Collections.Generic;
using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 매치가 다 모인 자리(pivot)에서 터지는 마무리 연출. 세 가지가 <b>순서대로</b> 일어난다:
    ///
    ///   1) <b>폭죽</b> - 조각들이 사방으로 팍 튀어나간다("때렸다"는 타격감)
    ///   2) <b>파장</b> - 고리 하나가 퍼져 나가며 옅어진다
    ///   3) <b>가루</b> - 잔가루가 하늘로 떠오르며 사라진다
    ///
    /// 2번은 <b>스킬 게이지 만충 연출을 뒤집은 것</b>이다. 그쪽은 큰 고리가 중심으로 빨려들며
    /// 또렷해지는데(모인다), 여기서는 작은 고리가 퍼져 나가며 옅어진다(터진다). 같은 그림을
    /// 반대로 쓰면 "모으는 것"과 "터뜨리는 것"이 한 쌍으로 읽힌다.
    ///
    /// <b>기다리지 않는 연출이다.</b> 예전엔 pivot 조각이 0.2초 동안 작아지는 걸 접기 코루틴이
    /// 끝까지 기다렸는데, 그만큼 낙하·리필이 늦어졌다. 지금은 조각을 곧바로 치우고 이 연출만
    /// 따로 굴러가므로 판은 바로 다음 단계로 넘어간다(이 프로젝트의 논블로킹 연출 방침).
    ///
    /// 퍼즐판이 월드 좌표(SpriteRenderer)라 이것도 UI가 아니라 월드로 그린다.
    /// 런타임에 오브젝트를 만들지 않고 Awake 에서 풀을 채워두며, 조각마다 코루틴을 띄우지 않고
    /// 이 컴포넌트의 Update 하나가 전부 굴린다(구름·낙하·데미지 팝업과 같은 방식).
    ///
    /// <b>크기는 전부 "퍼즐 한 칸의 배수"로 적는다.</b> 이 스프라이트들은 PPU 가 750이라
    /// 스케일 1이 0.25 유닛밖에 안 된다 - 스케일 값을 직접 적으면 "0.6으로 줬는데 안 보인다" 같은
    /// 일이 반복된다. 여기서는 원하는 실제 크기를 적으면 스프라이트 크기로 알아서 환산한다.
    /// </summary>
    public class MatchFinishEffect : MonoBehaviour
    {
        private enum Motion
        {
            Burst,   // 빠르게 튀어나갔다가 잦아든다
            Ring,    // 제자리에서 커지기만 한다
            Rise     // 위로 떠오른다
        }

        [Header("1. 폭죽")]
        [Tooltip("사방으로 튀어나갈 파편 스프라이트들. 여러 개면 무작위로 골라 쓴다.")]
        [SerializeField] private Sprite[] burstSprites;

        [Tooltip("한 번에 튀어나가는 파편 수.")]
        [SerializeField] private int burstCount = 10;

        [Tooltip("파편이 날아가는 거리(퍼즐 한 칸 대비).")]
        [SerializeField] private float burstDistance = 1.1f;

        [Tooltip("파편 크기(퍼즐 한 칸 대비) - 처음과 끝.")]
        [SerializeField] private float burstStartSize = 0.5f;
        [SerializeField] private float burstEndSize = 0.15f;

        [Tooltip("파편이 날아가 사라지기까지의 시간(초). 짧을수록 '팍' 하고 때린 느낌이 난다.")]
        [SerializeField] private float burstDuration = 0.26f;

        [Tooltip("파편 색.")]
        [SerializeField] private Color burstTint = new Color(1f, 0.95f, 0.7f, 1f);

        [Header("2. 파장 (스킬 만충 고리를 뒤집은 것)")]
        [Tooltip("퍼져 나갈 고리 스프라이트. 스킬 게이지가 쓰는 것과 같은 그림을 넣으면 된다.")]
        [SerializeField] private Sprite ringSprite;

        [Tooltip("폭죽이 터지고 파장이 시작되기까지의 간격(초).")]
        [SerializeField] private float ringDelay = 0.05f;

        [Tooltip("고리가 퍼지는 시간(초).")]
        [SerializeField] private float ringDuration = 0.34f;

        [Tooltip("고리 크기(퍼즐 한 칸 대비) - 처음과 끝. 스킬 만충은 큰 것에서 작은 것으로 " +
                 "빨려들지만, 여기서는 반대로 작은 것에서 크게 퍼진다.")]
        [SerializeField] private float ringStartSize = 0.35f;
        [SerializeField] private float ringEndSize = 2.2f;

        [Tooltip("고리 투명도 - 처음과 끝. 스킬 만충은 작아질수록 또렷해지는데, " +
                 "여기서는 반대로 퍼질수록 옅어진다.")]
        [SerializeField] private float ringStartAlpha = 0.9f;
        [SerializeField] private float ringEndAlpha;

        [SerializeField] private Color ringTint = Color.white;

        [Header("3. 가루")]
        [Tooltip("하늘로 떠오를 가루 스프라이트들.")]
        [SerializeField] private Sprite[] dustSprites;

        [Tooltip("가루 알갱이 수.")]
        [SerializeField] private int dustCount = 8;

        [Tooltip("파장이 시작되고 가루가 떠오르기까지의 간격(초).")]
        [SerializeField] private float dustDelay = 0.14f;

        [Tooltip("가루가 떠올라 사라지기까지의 시간(초). 앞의 둘보다 길어야 '남은 잔재'로 읽힌다.")]
        [SerializeField] private float dustDuration = 0.62f;

        [Tooltip("가루가 올라가는 높이(퍼즐 한 칸 대비).")]
        [SerializeField] private float dustRise = 1.3f;

        [Tooltip("가루가 좌우로 흩어지는 폭(퍼즐 한 칸 대비). 0이면 똑바로만 올라간다.")]
        [SerializeField] private float dustDrift = 0.45f;

        [Tooltip("가루 크기(퍼즐 한 칸 대비) - 처음과 끝. 올라가면서 잘게 부서지듯 작아진다.")]
        [SerializeField] private float dustStartSize = 0.3f;
        [SerializeField] private float dustEndSize = 0.06f;

        [SerializeField] private Color dustTint = new Color(1f, 1f, 1f, 1f);

        [Header("공통")]
        [Tooltip("미리 만들어둘 조각 수. 한 매치에 쓰는 수(폭죽+고리+가루)보다 넉넉해야 " +
                 "캐스케이드로 여러 곳이 동시에 터져도 모자라지 않는다.")]
        [SerializeField] private int poolSize = 64;

        [Tooltip("정렬 순서. 조각(0~2)과 가림막(150)보다 위여야 가려지지 않는다.")]
        [SerializeField] private int sortingOrder = 160;

        [Tooltip("스프라이트 렌더러가 쓸 정렬 레이어 이름. 비워두면 Default.")]
        [SerializeField] private string sortingLayerName = "";

        private sealed class Bit
        {
            public SpriteRenderer renderer;
            public Motion motion;
            public Vector3 start;
            public Vector3 direction;
            public float distance;
            public float startSize, endSize;   // 월드 유닛
            public float startAlpha, endAlpha;
            public float spin;
            public float delay;
            public float duration;
            public float elapsed;
            public float spriteUnits;          // 이 스프라이트의 스케일 1 크기(월드 유닛)
        }

        private readonly List<Bit> pool = new List<Bit>();
        private readonly List<Bit> active = new List<Bit>();

        /// <summary>지금 뭔가 재생 중인지.</summary>
        public bool IsPlaying => active.Count > 0;

        private void Awake()
        {
            for (int i = 0; i < Mathf.Max(1, poolSize); i++)
                pool.Add(CreateBit());
        }

        private Bit CreateBit()
        {
            var go = new GameObject("MatchFinishBit");
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = sortingOrder;
            if (!string.IsNullOrEmpty(sortingLayerName))
                sr.sortingLayerName = sortingLayerName;

            return new Bit { renderer = sr };
        }

        /// <summary>
        /// 그 자리에서 마무리 연출을 터뜨린다.
        /// </summary>
        /// <param name="cellSize">퍼즐 한 칸의 월드 크기. 모든 크기·거리가 이 값의 배수로 계산된다.</param>
        /// <param name="intensity">
        /// 세기(0~1). <b>여러 칸이 한꺼번에 사라질 때</b> 쓴다 - 칸마다 전부를 터뜨리면
        /// 조각 수 × 19개라 풀이 금방 바닥나고 화면도 지저분해진다. 1보다 작으면 파편·가루가
        /// 그만큼 줄고 <b>파장(고리)은 아예 나오지 않는다</b> - 고리가 칸마다 겹치면
        /// 하나하나가 안 읽히고 뿌옇게만 보이기 때문이다.
        /// </param>
        public void Play(Vector3 worldCenter, float cellSize, float intensity = 1f)
        {
            float cell = cellSize > 0f ? cellSize : 1f;
            float strength = Mathf.Clamp01(intensity);

            SpawnBurst(worldCenter, cell, strength);

            if (strength >= 1f)
                SpawnRing(worldCenter, cell);

            SpawnDust(worldCenter, cell, strength);
        }

        private void SpawnBurst(Vector3 center, float cell, float strength)
        {
            if (burstSprites == null || burstSprites.Length == 0)
                return;

            int count = Mathf.Max(1, Mathf.RoundToInt(burstCount * strength));
            for (int i = 0; i < count; i++)
            {
                var bit = Rent();
                if (bit == null)
                    return;

                // 원을 균등하게 나눈 뒤 살짝 흔든다 - 완전 무작위면 한쪽에 몰려서
                // "터졌다"가 아니라 "흘렀다"로 보이는 경우가 생긴다(구름과 같은 이유).
                float angle = (i / (float)count) * Mathf.PI * 2f
                              + Random.Range(-0.25f, 0.25f);

                Setup(bit, burstSprites[Random.Range(0, burstSprites.Length)], burstTint, Motion.Burst,
                      center, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f),
                      burstDistance * cell * Random.Range(0.75f, 1.25f),
                      burstStartSize * cell, burstEndSize * cell,
                      1f, 0f, 0f, burstDuration, Random.Range(-220f, 220f));
            }
        }

        private void SpawnRing(Vector3 center, float cell)
        {
            if (ringSprite == null)
                return;

            var bit = Rent();
            if (bit == null)
                return;

            Setup(bit, ringSprite, ringTint, Motion.Ring, center, Vector3.zero, 0f,
                  ringStartSize * cell, ringEndSize * cell,
                  ringStartAlpha, ringEndAlpha, ringDelay, ringDuration, 0f);
        }

        private void SpawnDust(Vector3 center, float cell, float strength)
        {
            if (dustSprites == null || dustSprites.Length == 0)
                return;

            int count = Mathf.Max(1, Mathf.RoundToInt(dustCount * strength));
            for (int i = 0; i < count; i++)
            {
                var bit = Rent();
                if (bit == null)
                    return;

                // 위로 가되 좌우로 조금씩 벌어진다. 방향을 정규화하지 않는 이유는
                // 가로 흩어짐과 세로 상승을 따로 조절하고 싶어서다.
                var dir = new Vector3(Random.Range(-1f, 1f) * dustDrift, 1f, 0f);

                Setup(bit, dustSprites[Random.Range(0, dustSprites.Length)], dustTint, Motion.Rise,
                      center + new Vector3(Random.Range(-0.2f, 0.2f) * cell,
                                           Random.Range(-0.2f, 0.2f) * cell, 0f),
                      dir, dustRise * cell * Random.Range(0.7f, 1.3f),
                      dustStartSize * cell, dustEndSize * cell,
                      1f, 0f,
                      dustDelay + Random.Range(0f, 0.12f), dustDuration, Random.Range(-90f, 90f));
            }
        }

        private void Setup(Bit bit, Sprite sprite, Color tint, Motion motion, Vector3 start,
                           Vector3 direction, float distance, float startSize, float endSize,
                           float startAlpha, float endAlpha, float delay, float duration, float spin)
        {
            bit.renderer.sprite = sprite;

            var color = tint;
            color.a = 0f; // 대기 중에는 투명 - delay 가 끝나야 보인다
            bit.renderer.color = color;

            // 스케일 1일 때의 실제 크기. 이걸로 나눠야 "칸 대비 크기"가 그림의 PPU 와 무관해진다.
            float units = sprite != null ? Mathf.Max(0.0001f, sprite.bounds.size.x) : 1f;

            bit.motion = motion;
            bit.start = start;
            bit.direction = direction;
            bit.distance = distance;
            bit.startSize = startSize;
            bit.endSize = endSize;
            bit.startAlpha = startAlpha;
            bit.endAlpha = endAlpha;
            bit.delay = delay;
            bit.duration = Mathf.Max(0.01f, duration);
            bit.spin = spin;
            bit.elapsed = 0f;
            bit.spriteUnits = units;

            bit.renderer.transform.position = start;
            bit.renderer.transform.localScale = Vector3.one * (startSize / units);
            bit.renderer.transform.rotation = Quaternion.identity;
            bit.renderer.gameObject.SetActive(true);

            active.Add(bit);
        }

        private Bit Rent()
        {
            if (pool.Count > 0)
            {
                var bit = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                return bit;
            }

            // 풀이 비면 새로 만들지 않고 가장 오래된 것을 뺏는다 - 실행 중 생성을 하지 않기 위한 처리.
            if (active.Count == 0)
                return null;

            var oldest = active[0];
            active.RemoveAt(0);
            return oldest;
        }

        private void Update()
        {
            if (active.Count == 0)
                return;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var bit = active[i];
                bit.elapsed += Time.deltaTime;

                if (bit.elapsed < bit.delay)
                    continue; // 아직 차례가 아니다(투명한 채로 대기)

                float p = (bit.elapsed - bit.delay) / bit.duration;
                if (p >= 1f)
                {
                    bit.renderer.gameObject.SetActive(false);
                    active.RemoveAt(i);
                    pool.Add(bit);
                    continue;
                }

                float moved;
                float fade;
                switch (bit.motion)
                {
                    case Motion.Burst:
                        // 처음에 확 튀어나갔다가 급격히 잦아든다 - 타격감은 이 감속에서 나온다.
                        moved = 1f - (1f - p) * (1f - p) * (1f - p);
                        fade = 1f - p * p;
                        break;

                    case Motion.Rise:
                        // 천천히 떠오르다 끝에서 스르르 사라진다.
                        moved = 1f - (1f - p) * (1f - p);
                        fade = Mathf.Sin(p * Mathf.PI); // 나타났다 사라짐 - 가루가 흩날리는 느낌
                        break;

                    default: // Ring
                        moved = 0f;
                        fade = 1f;
                        break;
                }

                var t = bit.renderer.transform;
                t.position = bit.start + bit.direction * (bit.distance * moved);

                float size = Mathf.Lerp(bit.startSize, bit.endSize, p);
                t.localScale = Vector3.one * (size / bit.spriteUnits);

                if (bit.spin != 0f)
                    t.rotation = Quaternion.Euler(0f, 0f, bit.spin * (bit.elapsed - bit.delay));

                var color = bit.renderer.color;
                color.a = Mathf.Lerp(bit.startAlpha, bit.endAlpha, p) * fade;
                bit.renderer.color = color;
            }
        }
    }
}
