using System.Collections.Generic;
using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 뭉게구름이 한 점에 뭉쳐 있다가 사방으로 흩어지며 작아지는 연출.
    ///
    /// 스킬로 퍼즐이 바뀔 때 그 순간을 가리는 용도다 - 조각이 아무 연출 없이 다른 색으로
    /// 툭 바뀌면 "게임이 몰래 바꿔치기했다"처럼 보이는데, 구름으로 한 번 덮었다가 걷히면
    /// "구름 속에서 바뀌었다"로 읽힌다.
    ///
    /// 퍼즐판은 월드 좌표(SpriteRenderer)라 이 연출도 UI가 아니라 월드로 그린다.
    /// 정렬 순서는 조각보다 위, 날아가는 불꽃보다 아래에 둔다(PanelView 의 정렬 사다리 참고):
    ///   보드 배경(-10) &lt; 조각(0~2) &lt; 드래그 중(100) &lt; 가림막(150) &lt; <b>구름(160)</b> &lt; 불꽃(200)
    ///
    /// 런타임에 오브젝트를 만들지 않는다 - Awake 에서 풀을 채워두고 껐다 켜기만 한다.
    /// 퍼프마다 코루틴을 띄우지 않고 이 컴포넌트의 Update 하나가 전부 굴린다
    /// (낙하·불꽃·데미지 팝업과 같은 방식).
    /// </summary>
    public class CloudBurstEffect : MonoBehaviour
    {
        [Tooltip("구름 조각 스프라이트들. 여러 개 넣으면 무작위로 골라 써서 같은 모양이 반복되지 않는다.")]
        [SerializeField] private Sprite[] cloudSprites;

        [Tooltip("미리 만들어둘 구름 개수. 한 번에 터지는 수보다 넉넉해야 연달아 터져도 모자라지 않는다.")]
        [SerializeField] private int poolSize = 32;

        [Tooltip("한 번 터질 때 나오는 구름 수.")]
        [SerializeField] private int puffsPerBurst = 12;

        [Tooltip("구름 하나가 흩어져 사라지기까지 걸리는 시간(초).")]
        [SerializeField] private float duration = 0.55f;

        [Tooltip("흩어지는 거리(월드 유닛). 퍼즐 한 칸이 약 1이다.")]
        [SerializeField] private float spreadDistance = 1.1f;

        [Tooltip("거리의 무작위 편차 비율. 0.4면 사람마다 0.6~1.4배로 흩어진다.")]
        [Range(0f, 1f)]
        [SerializeField] private float spreadVariance = 0.4f;

        [Tooltip("처음(뭉쳐 있을 때)과 끝(흩어졌을 때)의 크기.")]
        [SerializeField] private float startScale = 0.9f;
        [SerializeField] private float endScale = 0.15f;

        [Tooltip("시작 지점에서 살짝 흩뿌려 두는 반경. 0이면 완전히 한 점에서 시작한다.")]
        [SerializeField] private float startJitter = 0.12f;

        [Tooltip("구름 색. 알파는 연출이 직접 조절하므로 여기서는 색만 본다.")]
        [SerializeField] private Color tint = Color.white;

        [Tooltip("정렬 순서. 조각(0~2)과 가림막(150)보다 위여야 구름이 퍼즐을 덮는다.")]
        [SerializeField] private int sortingOrder = 160;

        [Tooltip("스프라이트 렌더러가 쓸 정렬 레이어 이름. 비워두면 Default.")]
        [SerializeField] private string sortingLayerName = "";

        private sealed class Puff
        {
            public SpriteRenderer renderer;
            public Vector3 start;
            public Vector3 direction;
            public float distance;
            public float spin;
            public float elapsed;
        }

        private readonly List<Puff> pool = new List<Puff>();
        private readonly List<Puff> active = new List<Puff>();

        /// <summary>지금 구름이 하나라도 떠 있는지. 연출이 끝나길 기다릴 때 본다.</summary>
        public bool IsPlaying => active.Count > 0;

        private void Awake()
        {
            for (int i = 0; i < Mathf.Max(1, poolSize); i++)
                pool.Add(CreatePuff());
        }

        private Puff CreatePuff()
        {
            var go = new GameObject("CloudPuff");
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = sortingOrder;
            if (!string.IsNullOrEmpty(sortingLayerName))
                sr.sortingLayerName = sortingLayerName;

            return new Puff { renderer = sr };
        }

        /// <summary>
        /// 그 자리에서 구름을 터뜨린다. 여러 칸을 한꺼번에 덮고 싶으면 칸마다 부르면 된다.
        /// </summary>
        public void Burst(Vector3 worldCenter)
        {
            if (cloudSprites == null || cloudSprites.Length == 0)
                return;

            int count = Mathf.Max(1, puffsPerBurst);
            for (int i = 0; i < count; i++)
            {
                var puff = Rent();
                if (puff == null)
                    return;

                // 방향은 원을 균등하게 나눈 뒤 살짝 흔든다 - 완전 무작위로 뽑으면 한쪽에
                // 몰려서 "터졌다"가 아니라 "흘렀다"처럼 보이는 경우가 생긴다.
                float angle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.4f, 0.4f);
                puff.direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                puff.distance = spreadDistance * (1f + Random.Range(-spreadVariance, spreadVariance));
                puff.start = worldCenter + (Vector3)(Random.insideUnitCircle * startJitter);
                puff.spin = Random.Range(-90f, 90f);
                puff.elapsed = 0f;

                var sr = puff.renderer;
                sr.sprite = cloudSprites[Random.Range(0, cloudSprites.Length)];
                sr.color = tint;
                sr.transform.position = puff.start;
                sr.transform.localScale = Vector3.one * startScale;
                sr.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                sr.gameObject.SetActive(true);

                active.Add(puff);
            }
        }

        private Puff Rent()
        {
            if (pool.Count > 0)
            {
                var p = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                return p;
            }

            // 풀이 비었으면 새로 만들지 않고 가장 오래된 것을 빼앗아 다시 쓴다
            // (DamagePopupUI 와 같은 방식 - 실행 중 Instantiate 를 하지 않기 위한 처리).
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

            float span = Mathf.Max(0.01f, duration);

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var puff = active[i];
                puff.elapsed += Time.deltaTime;

                float p = puff.elapsed / span;
                if (p >= 1f)
                {
                    puff.renderer.gameObject.SetActive(false);
                    active.RemoveAt(i);
                    pool.Add(puff);
                    continue;
                }

                // 이동: 처음엔 빠르게 튀어나갔다가 점점 느려진다(ease-out) -
                // 터지는 느낌은 초반 속도에서 나온다.
                float eased = 1f - (1f - p) * (1f - p);

                // 크기: 이동과 <b>다른 곡선</b>을 쓴다. 이동과 같은 ease-out 을 쓰면 초반에
                // 확 작아져서 퍼즐 조각을 덮어주지 못한다 - 가려야 할 순간에 이미 쪼그라든다.
                // p*p 는 앞부분에서 거의 안 줄고 뒤에서 빠르게 줄어든다.
                float shrink = p * p;

                var t = puff.renderer.transform;
                t.position = puff.start + puff.direction * (puff.distance * eased);
                t.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, shrink);
                t.Rotate(0f, 0f, puff.spin * Time.deltaTime);

                // 끝에서만 옅어지게 한다. 처음부터 옅어지면 뭉쳐 있는 순간의 밀도가 사라진다.
                var color = tint;
                color.a = tint.a * Mathf.Clamp01((1f - p) / 0.45f);
                puff.renderer.color = color;
            }
        }

        /// <summary>진행 중인 구름을 즉시 정리한다(연출이 취소될 때).</summary>
        public void StopAll()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                active[i].renderer.gameObject.SetActive(false);
                pool.Add(active[i]);
            }
            active.Clear();
        }
    }
}
