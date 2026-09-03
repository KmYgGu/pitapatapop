using UnityEngine;
using UnityEngine.UI;
using JojoPuzzle.Battle;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 적이 쓰러지는 순간 <b>체력바가 산산조각 나는</b> 연출. 바를 격자로 잘라 그 조각들이
    /// 사방으로 튀며 떨어진다.
    ///
    /// <b>조각은 바 그림을 실제로 잘라 갖는다.</b> 조각마다 <c>RawImage.uvRect</c> 를 자기 칸에
    /// 해당하는 만큼만 잡아주므로, 붙여놓으면 원래 바와 똑같이 보이고 흩어지면 진짜로 쪼개진
    /// 것처럼 보인다. (<c>Image</c> 로는 못 한다 - UV 를 지정할 수단이 없어서 조각마다 바 전체가
    /// 축소돼 그려진다. 그래서 여기만 <c>RawImage</c> 다.)
    /// 그림을 못 찾으면 단색 사각형으로 물러선다.
    ///
    /// <b>조각은 한 번만 만들고 재사용한다</b>(이 프로젝트의 풀링 방침). 한 판에 한 번 있는
    /// 연출이지만 다시 싸우면 또 필요하고, 무엇보다 부서지는 순간에 오브젝트를 수십 개
    /// 만들면 하필 그 프레임이 튄다.
    ///
    /// 코루틴 대신 Update 하나가 조각 전부를 굴린다(HitFlinchUI·BoardView 낙하와 같은 방식).
    /// </summary>
    public class HealthBarShatterUI : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("체력이 0이 되는 순간을 알려줄 곳. 비워두면 이 연출이 저절로 시작되지 않는다.")]
        [SerializeField] private BattleManager battleManager;

        [Tooltip("부서질 체력바의 <b>모양을 잴</b> 칸. 보통 체력바 본체.")]
        [SerializeField] private RectTransform barRect;

        [Tooltip("부서질 때 감출 것들(체력바 판과 채움). 조각이 그 자리를 대신한다.")]
        [SerializeField] private Graphic[] hideOnShatter = new Graphic[0];

        [Tooltip("조각이 생길 자리. 비워두면 이 오브젝트를 쓴다. " +
                 "<b>체력바보다 위에 그려지는 곳</b>이라야 조각이 바 뒤로 숨지 않는다.")]
        [SerializeField] private RectTransform shardParent;

        [Header("쪼개기")]
        [Tooltip("가로로 몇 조각.")]
        [Range(2, 16)]
        [SerializeField] private int columns = 9;

        [Tooltip("세로로 몇 조각.")]
        [Range(1, 8)]
        [SerializeField] private int rows = 3;

        [Tooltip("조각 사이를 살짝 벌린다(유닛). 0이면 빈틈 없이 붙어 있다가 흩어진다.")]
        [SerializeField] private float shardGap = 1f;

        [Tooltip("조각이 잘라 가질 그림. 비워두면 hideOnShatter 의 첫 Image 에서 찾는다. " +
                 "그림이 없으면 단색 사각형이 된다.")]
        [SerializeField] private Image spriteSource;

        [Tooltip("조각 색(그림이 있으면 그 위에 곱해지는 색조). 알파가 0이면 원래 바의 색을 쓴다.")]
        [SerializeField] private Color shardColor = new Color(0f, 0f, 0f, 0f);

        [Header("튀어나가기")]
        [Tooltip("바깥으로 튀는 속도(유닛/초). 바 한가운데에서 멀수록 이 방향으로 밀려난다.")]
        [SerializeField] private float burstSpeed = 210f;

        [Tooltip("위로 솟는 속도(유닛/초). 중력과 함께 포물선을 만든다.")]
        [SerializeField] private float upSpeed = 170f;

        [Tooltip("속도에 섞을 무작위 폭(유닛/초). 0이면 전부 똑같이 날아 부자연스럽다.")]
        [SerializeField] private float speedJitter = 90f;

        [Tooltip("중력(유닛/초^2). 클수록 빨리 떨어진다.")]
        [SerializeField] private float gravity = 900f;

        [Tooltip("조각이 도는 속도의 최대치(도/초).")]
        [SerializeField] private float spinSpeed = 420f;

        [Tooltip("조각 하나가 사라지기까지 걸리는 시간(초).")]
        [SerializeField] private float lifetime = 1.1f;

        [Tooltip("수명의 몇 할이 지난 뒤부터 흐려지기 시작할지. 0.5면 절반부터 흐려진다.")]
        [Range(0f, 1f)]
        [SerializeField] private float fadeStart = 0.45f;

        /// <summary>이미 부서졌는지.</summary>
        public bool IsShattered { get; private set; }

        private struct Shard
        {
            public RectTransform rect;
            public RawImage image;
            public Vector2 velocity;
            public float spin;
            public float age;
            public Color baseColor;
        }

        private Shard[] shards;
        private bool running;

        private void OnEnable()
        {
            if (battleManager != null)
                battleManager.OnEnemyHealthChanged += HandleEnemyHealthChanged;
        }

        private void OnDisable()
        {
            if (battleManager != null)
                battleManager.OnEnemyHealthChanged -= HandleEnemyHealthChanged;
        }

        private void HandleEnemyHealthChanged(float current, float max)
        {
            if (current <= 0f)
            {
                Shatter();
                return;
            }

            // 체력이 다시 찼다 = 새 판이 시작됐다. 부서진 채로 두면 다음 판에 체력바가 없다.
            if (IsShattered)
                Restore();
        }

        /// <summary>지금 부순다. 이미 부서졌으면 아무 일도 하지 않는다.</summary>
        public void Shatter()
        {
            if (IsShattered || barRect == null)
                return;

            IsShattered = true;

            var parent = shardParent != null ? shardParent : transform as RectTransform;
            if (parent == null)
                return;

            EnsureShards(parent);
            LayoutAndLaunch(parent);

            for (int i = 0; i < hideOnShatter.Length; i++)
            {
                if (hideOnShatter[i] != null)
                    hideOnShatter[i].enabled = false;
            }

            running = true;
        }

        /// <summary>부서지기 전 상태로. 다음 판을 위해 되돌린다.</summary>
        public void Restore()
        {
            IsShattered = false;
            running = false;

            if (shards != null)
            {
                for (int i = 0; i < shards.Length; i++)
                {
                    if (shards[i].rect != null)
                        shards[i].rect.gameObject.SetActive(false);
                }
            }

            for (int i = 0; i < hideOnShatter.Length; i++)
            {
                if (hideOnShatter[i] != null)
                    hideOnShatter[i].enabled = true;
            }
        }

        private void EnsureShards(RectTransform parent)
        {
            int needed = columns * rows;
            if (shards != null && shards.Length == needed)
                return;

            // 개수 설정이 바뀌었으면 예전 것을 걷어낸다(에디터에서 값을 만질 때만 생기는 길이다).
            if (shards != null)
            {
                for (int i = 0; i < shards.Length; i++)
                {
                    if (shards[i].rect != null)
                        Destroy(shards[i].rect.gameObject);
                }
            }

            shards = new Shard[needed];

            for (int i = 0; i < needed; i++)
            {
                var go = new GameObject("Shard" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                go.layer = parent.gameObject.layer;

                var rect = (RectTransform)go.transform;
                rect.SetParent(parent, false);

                // 조각은 자리를 직접 잡을 것이므로 앵커를 한 점으로 모은다.
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                var image = go.GetComponent<RawImage>();
                image.raycastTarget = false;

                go.SetActive(false);

                shards[i] = new Shard { rect = rect, image = image };
            }
        }

        /// <summary>
        /// 체력바를 격자로 잘라 조각을 그 자리에 놓고 사방으로 쏜다.
        ///
        /// 자리를 잡을 때 <b>화면 좌표를 거쳐 옮긴다</b> - 체력바와 조각들의 부모가 서로 다른
        /// 칸이라 앵커 값을 그대로 베낄 수 없기 때문이다. 이 프로젝트 캔버스는 Screen Space
        /// Overlay 라서 월드 좌표가 곧 화면 좌표다(그래서 카메라 인자가 null 이다).
        /// </summary>
        private void LayoutAndLaunch(RectTransform parent)
        {
            var corners = new Vector3[4];
            barRect.GetWorldCorners(corners);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, corners[0], null, out var bottomLeft);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, corners[2], null, out var topRight);

            Vector2 size = topRight - bottomLeft;
            Vector2 center = (bottomLeft + topRight) * 0.5f;

            float cellW = size.x / columns;
            float cellH = size.y / rows;

            Color fill = shardColor.a > 0f ? shardColor : ResolveFillColor();

            // 바 그림을 조각 수만큼 나눠 가질 준비. 그림이 없으면 uv 는 안 쓰고 단색으로 간다.
            var source = ResolveSprite();
            Texture texture = null;
            Rect uvWhole = new Rect(0f, 0f, 1f, 1f);
            if (source != null && source.texture != null)
            {
                texture = source.texture;

                // textureRect 는 픽셀 단위다. 아틀라스에 들어 있어도 그 안에서의 자리를 알려주므로
                // 그대로 정규화하면 된다(단, tight 패킹으로 회전돼 들어간 스프라이트는 어긋난다 -
                // 체력바처럼 낱장으로 쓰는 그림에서는 그럴 일이 없다).
                var tr = source.textureRect;
                uvWhole = new Rect(tr.x / texture.width, tr.y / texture.height,
                                   tr.width / texture.width, tr.height / texture.height);
            }

            int index = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++, index++)
                {
                    var shard = shards[index];

                    Vector2 pos = new Vector2(
                        bottomLeft.x + cellW * (x + 0.5f),
                        bottomLeft.y + cellH * (y + 0.5f));

                    shard.rect.sizeDelta = new Vector2(Mathf.Max(1f, cellW - shardGap),
                                                       Mathf.Max(1f, cellH - shardGap));
                    shard.rect.anchoredPosition = pos;
                    shard.rect.localRotation = Quaternion.identity;
                    shard.rect.localScale = Vector3.one;

                    // 한가운데에서 멀수록 그 방향으로 세게 밀려난다 - 가운데가 터진 것처럼 보인다.
                    Vector2 outward = pos - center;
                    if (outward.sqrMagnitude < 0.0001f)
                        outward = Vector2.up;
                    outward.Normalize();

                    shard.velocity = outward * burstSpeed
                                     + Vector2.up * upSpeed
                                     + new Vector2(Random.Range(-speedJitter, speedJitter),
                                                   Random.Range(-speedJitter, speedJitter));

                    shard.spin = Random.Range(-spinSpeed, spinSpeed);
                    shard.age = 0f;
                    shard.baseColor = fill;

                    shard.image.texture = texture;
                    shard.image.color = fill;

                    if (texture != null)
                    {
                        // 이 조각이 맡은 칸만큼만 잘라 온다 - 붙여놓으면 원래 그림 그대로다.
                        shard.image.uvRect = new Rect(
                            uvWhole.x + uvWhole.width * ((float)x / columns),
                            uvWhole.y + uvWhole.height * ((float)y / rows),
                            uvWhole.width / columns,
                            uvWhole.height / rows);
                    }

                    shard.rect.gameObject.SetActive(true);

                    shards[index] = shard;
                }
            }
        }

        /// <summary>조각이 잘라 갈 그림. 지정이 없으면 감출 대상 중 첫 Image 의 스프라이트를 쓴다.</summary>
        private Sprite ResolveSprite()
        {
            if (spriteSource != null)
                return spriteSource.sprite;

            for (int i = 0; i < hideOnShatter.Length; i++)
            {
                if (hideOnShatter[i] is Image image && image.sprite != null)
                    return image.sprite;
            }

            return null;
        }

        /// <summary>조각 색을 정한다. 체력바 색을 그대로 쓰는 게 기본이다.</summary>
        private Color ResolveFillColor()
        {
            for (int i = 0; i < hideOnShatter.Length; i++)
            {
                if (hideOnShatter[i] != null)
                    return hideOnShatter[i].color;
            }

            return Color.white;
        }

        private void Update()
        {
            if (!running || shards == null)
                return;

            float dt = Time.deltaTime;
            bool anyAlive = false;

            for (int i = 0; i < shards.Length; i++)
            {
                var shard = shards[i];
                if (shard.rect == null || !shard.rect.gameObject.activeSelf)
                    continue;

                shard.age += dt;
                if (shard.age >= lifetime)
                {
                    shard.rect.gameObject.SetActive(false);
                    shards[i] = shard;
                    continue;
                }

                anyAlive = true;

                shard.velocity.y -= gravity * dt;
                shard.rect.anchoredPosition += shard.velocity * dt;
                shard.rect.localRotation *= Quaternion.Euler(0f, 0f, shard.spin * dt);

                float t = shard.age / lifetime;
                if (t > fadeStart)
                {
                    var c = shard.baseColor;
                    c.a = shard.baseColor.a * (1f - Mathf.InverseLerp(fadeStart, 1f, t));
                    shard.image.color = c;
                }

                shards[i] = shard;
            }

            if (!anyAlive)
                running = false; // 다 떨어졌다 - 더 굴릴 게 없다
        }
    }
}
