using UnityEngine;
using UnityEngine.UI;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 캐릭터가 뛸 때 발밑에서 피어오르는 <b>먼지구름</b>(2026-08-28 사용자 지시:
    /// "image의 구름들을 활용"). <see cref="CloudBurstEffect"/> 와 같은 그림을 쓰지만
    /// <b>이쪽은 UI 다</b> - 준비 화면도 배틀 HUD 도 캔버스 위라 월드 SpriteRenderer 로는 못 그린다.
    ///
    /// <b>런타임에 오브젝트를 만들지 않는다</b> - Awake 에서 풀을 채워두고 껐다 켜기만 한다
    /// (CloudBurstEffect·DamagePopupUI 와 같은 방식). 퍼프마다 코루틴을 띄우지 않고
    /// 이 컴포넌트의 Update 하나가 전부 굴린다.
    /// </summary>
    public class DustPuffUI : MonoBehaviour
    {
        [Tooltip("퍼프가 생기는 자리. 비워두면 자기 자신. 뛰어가는 캐릭터와 <b>같은 부모</b>여야 " +
                 "좌표가 맞는다 - 다른 부모면 같은 값이 다른 자리를 가리킨다.")]
        [SerializeField] private RectTransform layer;

        [Tooltip("구름 그림들. 여러 개면 무작위로 골라 같은 모양이 반복되지 않는다. " +
                 "배틀 씬 CloudBurstEffect 가 쓰는 것과 같은 애셋을 넣으면 된다.")]
        [SerializeField] private Sprite[] cloudSprites;

        [Tooltip("미리 만들어둘 개수. 한 번에 떠 있는 수보다 넉넉해야 한다.")]
        [SerializeField] private int poolSize = 24;

        [Tooltip("퍼프 한 장의 크기(유닛).")]
        [SerializeField] private float puffSize = 46f;

        [Tooltip("피어올랐다 사라지기까지 걸리는 시간(초).")]
        [SerializeField] private float duration = 0.45f;

        [Tooltip("피어오르는 높이(유닛). 발밑에서 위로 뜬다.")]
        [SerializeField] private float riseDistance = 22f;

        [Tooltip("좌우로 흩어지는 폭(유닛). 뛰어온 <b>반대쪽</b>으로 더 밀린다.")]
        [SerializeField] private float spread = 26f;

        [Tooltip("처음과 끝의 크기 배율. 작게 시작해 부풀며 흐려진다.")]
        [SerializeField] private float startScale = 0.45f;
        [SerializeField] private float endScale = 1.25f;

        [Tooltip("구름 색. 알파는 연출이 직접 줄이므로 여기서는 색만 본다.")]
        [SerializeField] private Color tint = new Color(1f, 1f, 1f, 0.85f);

        private sealed class Puff
        {
            public RectTransform rect;
            public Image image;
            public Vector2 from;
            public Vector2 to;
            public float elapsed;
            public bool alive;
        }

        private Puff[] puffs;

        private void Awake()
        {
            if (layer == null)
                layer = transform as RectTransform;

            puffs = new Puff[Mathf.Max(1, poolSize)];
            for (int i = 0; i < puffs.Length; i++)
                puffs[i] = CreatePuff(i);
        }

        private Puff CreatePuff(int index)
        {
            var go = new GameObject($"DustPuff{index}", typeof(RectTransform), typeof(Image));
            go.layer = layer.gameObject.layer;

            var rect = (RectTransform)go.transform;
            rect.SetParent(layer, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(puffSize, puffSize);

            var image = go.GetComponent<Image>();
            image.raycastTarget = false; // 먼지가 버튼을 가로채면 안 된다
            image.color = tint;

            go.SetActive(false);

            return new Puff { rect = rect, image = image };
        }

        /// <summary>
        /// <paramref name="anchoredPosition"/> 자리에서 먼지를 <paramref name="count"/> 장 피운다.
        /// </summary>
        /// <param name="awayX">
        /// 뛰어가는 방향(-1 왼쪽 / +1 오른쪽). 먼지는 그 <b>반대쪽</b>으로 밀린다 -
        /// 발이 뒤로 차낸 것이라야 "달린다"로 읽힌다.
        /// </param>
        public void Burst(Vector2 anchoredPosition, float awayX, int count = 2)
        {
            if (puffs == null || cloudSprites == null || cloudSprites.Length == 0)
                return;

            for (int i = 0; i < count; i++)
            {
                var puff = Take();
                if (puff == null)
                    return; // 풀이 다 찼다 - 지금 뜬 것들이 사라지면 다시 쓸 수 있다

                puff.image.sprite = cloudSprites[Random.Range(0, cloudSprites.Length)];
                puff.from = anchoredPosition + new Vector2(Random.Range(-6f, 6f), Random.Range(-4f, 4f));
                puff.to = puff.from + new Vector2(
                    -awayX * spread * Random.Range(0.5f, 1.2f),
                    riseDistance * Random.Range(0.6f, 1.3f));

                puff.elapsed = 0f;
                puff.alive = true;
                puff.rect.anchoredPosition = puff.from;
                puff.rect.localScale = Vector3.one * startScale;
                puff.image.color = tint;
                puff.rect.gameObject.SetActive(true);
            }
        }

        private Puff Take()
        {
            for (int i = 0; i < puffs.Length; i++)
            {
                if (!puffs[i].alive)
                    return puffs[i];
            }

            return null;
        }

        private void Update()
        {
            if (puffs == null)
                return;

            float step = Time.deltaTime;

            for (int i = 0; i < puffs.Length; i++)
            {
                var puff = puffs[i];
                if (!puff.alive)
                    continue;

                puff.elapsed += step;
                float t = duration > 0f ? Mathf.Clamp01(puff.elapsed / duration) : 1f;

                // 처음에 확 퍼지고 끝에서 잦아든다 - 등속이면 "떠올랐다"가 아니라 "미끄러졌다"로 보인다.
                float eased = 1f - (1f - t) * (1f - t);

                puff.rect.anchoredPosition = Vector2.LerpUnclamped(puff.from, puff.to, eased);
                puff.rect.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, eased);

                var color = tint;
                color.a = tint.a * (1f - t);
                puff.image.color = color;

                if (t < 1f)
                    continue;

                puff.alive = false;
                puff.rect.gameObject.SetActive(false);
            }
        }

        /// <summary>떠 있는 먼지를 전부 지운다. 화면이 바뀔 때 남아 있지 않도록.</summary>
        public void Clear()
        {
            if (puffs == null)
                return;

            for (int i = 0; i < puffs.Length; i++)
            {
                puffs[i].alive = false;
                puffs[i].rect.gameObject.SetActive(false);
            }
        }
    }
}
