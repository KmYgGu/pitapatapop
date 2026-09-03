using System.Collections.Generic;
using UnityEngine;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// 스킬 게이지가 <b>빛이 되어 자기 퍼즐 조각으로 날아가는</b> 연출. 게임 종료 마무리 처리에서
    /// 쓴다(2026-08-25 사용자 기획) - 조각이 그냥 툭 사라지면 무슨 일이 일어났는지 안 보인다.
    ///
    /// 빛 하나가 조각 하나를 맡는다. 출발점(스킬 게이지 자리)에서 목표 조각까지 <b>빠르게</b>
    /// 날아가고, 지나온 자리에 <b>잔상</b>을 떨군다. 도착하면 <see cref="OnLightArrived"/> 로
    /// 알리고, 그 조각을 지우는 건 부르는 쪽이 한다 - 이 컴포넌트는 보드를 모른다.
    ///
    /// <b>퍼즐판이 월드 좌표라 이 연출도 UI 가 아니라 월드로 그린다</b>
    /// (<see cref="CloudBurstEffect"/> 와 같은 이유·같은 구조).
    ///
    /// 오브젝트는 <b>한 번만 만들고 재사용한다</b>. Update 하나가 날아가는 빛 전부를 굴린다 -
    /// 빛마다 코루틴을 띄우지 않는다(이 프로젝트의 기본 방침).
    /// </summary>
    public class SkillLightEffect : MonoBehaviour
    {
        [Tooltip("빛에 쓸 그림. 비워두면 흰 사각형이 된다(그래도 빛으로는 읽힌다).")]
        [SerializeField] private Sprite lightSprite;

        [Tooltip("동시에 날 수 있는 빛의 최대 수. 조각 수보다 넉넉해야 한다.")]
        [SerializeField] private int poolSize = 48;

        [Tooltip("잔상 하나가 남아 있는 시간(초). 짧을수록 꼬리가 짧다.")]
        [SerializeField] private float trailLifetime = 0.16f;

        [Tooltip("잔상을 떨구는 간격(초). 촘촘할수록 꼬리가 이어져 보인다.")]
        [SerializeField] private float trailInterval = 0.02f;

        [Header("비행")]
        [Tooltip("빛 하나가 날아가는 데 걸리는 시간(초).")]
        [SerializeField] private float flightDuration = 0.32f;

        [Tooltip("빛과 빛 사이의 출발 간격(초). 0이면 전부 한꺼번에 출발한다.")]
        [SerializeField] private float launchInterval = 0.035f;

        [Tooltip("출발점 주변에 흩뿌리는 폭(월드 유닛). 전부 한 점에서 나오면 뭉쳐 보인다.")]
        [SerializeField] private float launchJitter = 0.25f;

        [Header("모양")]
        [SerializeField] private float headScale = 0.5f;
        [SerializeField] private float trailScale = 0.32f;
        [SerializeField] private Color tint = new Color(1f, 0.95f, 0.6f, 1f);

        [Tooltip("정렬 순서. 조각(0~2)과 가림막(150)보다 위, 스탠드업 불꽃(200)보다 아래.")]
        [SerializeField] private int sortingOrder = 170;

        /// <summary>빛이 목표에 닿았을 때 그 목표 인덱스와 함께 발행. 조각을 지우는 건 구독자가 한다.</summary>
        public event System.Action<int> OnLightArrived;

        /// <summary>지금 날고 있는 빛이 있는지.</summary>
        public bool IsPlaying => activeCount > 0 || pendingLaunch > 0;

        private struct Light
        {
            public SpriteRenderer head;
            public Vector3 from;
            public Vector3 to;
            public float elapsed;
            public float delay;
            public int targetIndex;
            public bool arrived;
            public float nextTrailAt;
        }

        private struct Trail
        {
            public SpriteRenderer renderer;
            public float age;
        }

        private Light[] lights;
        private readonly List<Trail> trails = new List<Trail>();
        private readonly Stack<SpriteRenderer> trailPool = new Stack<SpriteRenderer>();

        private int activeCount;
        private int pendingLaunch;

        private void Awake()
        {
            lights = new Light[Mathf.Max(1, poolSize)];
            for (int i = 0; i < lights.Length; i++)
                lights[i].head = CreateRenderer("Light" + i);
        }

        private SpriteRenderer CreateRenderer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = lightSprite;
            sr.sortingOrder = sortingOrder;
            sr.color = tint;
            go.SetActive(false);

            return sr;
        }

        /// <summary>
        /// 출발점에서 목표들로 빛을 쏜다. 이미 날고 있으면 그것부터 정리하고 새로 시작한다.
        /// </summary>
        /// <param name="from">출발할 월드 좌표(스킬 게이지 자리).</param>
        /// <param name="targets">목표 월드 좌표들. <b>인덱스가 그대로 OnLightArrived 로 돌아온다.</b></param>
        public void Launch(Vector3 from, IList<Vector3> targets)
        {
            StopAll();

            if (targets == null || targets.Count == 0)
                return;

            int count = Mathf.Min(targets.Count, lights.Length);

            for (int i = 0; i < count; i++)
            {
                var light = lights[i];

                Vector3 start = from + new Vector3(
                    Random.Range(-launchJitter, launchJitter),
                    Random.Range(-launchJitter, launchJitter),
                    0f);

                light.from = start;
                light.to = targets[i];
                light.elapsed = 0f;
                light.delay = launchInterval * i;
                light.targetIndex = i;
                light.arrived = false;
                light.nextTrailAt = 0f;

                light.head.transform.position = start;
                light.head.transform.localScale = Vector3.one * headScale;
                light.head.color = tint;
                light.head.gameObject.SetActive(false); // delay 가 지나야 나타난다

                lights[i] = light;
            }

            activeCount = count;
            pendingLaunch = count;
        }

        /// <summary>날고 있는 빛과 잔상을 전부 치운다.</summary>
        public void StopAll()
        {
            for (int i = 0; i < activeCount; i++)
                lights[i].head.gameObject.SetActive(false);

            activeCount = 0;
            pendingLaunch = 0;

            for (int i = 0; i < trails.Count; i++)
                Recycle(trails[i].renderer);

            trails.Clear();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            TickLights(dt);
            TickTrails(dt);
        }

        private void TickLights(float dt)
        {
            if (activeCount == 0)
                return;

            bool anyFlying = false;
            float duration = Mathf.Max(0.01f, flightDuration);

            for (int i = 0; i < activeCount; i++)
            {
                var light = lights[i];
                if (light.arrived)
                    continue;

                light.elapsed += dt;

                if (light.elapsed < light.delay)
                {
                    anyFlying = true;
                    lights[i] = light;
                    continue;
                }

                if (!light.head.gameObject.activeSelf)
                    light.head.gameObject.SetActive(true);

                float t = Mathf.Clamp01((light.elapsed - light.delay) / duration);

                // 가속(ease-in) - 게이지에서 빠져나와 <b>점점 빨라지며</b> 꽂힌다.
                // 등속이면 "날아가 꽂혔다"가 아니라 "미끄러졌다"로 보인다.
                float p = t * t;
                light.head.transform.position = Vector3.Lerp(light.from, light.to, p);

                // 잔상은 시간 간격으로 떨군다 - 거리로 재면 느린 초반에 뭉친다.
                if (light.elapsed >= light.nextTrailAt)
                {
                    light.nextTrailAt = light.elapsed + Mathf.Max(0.005f, trailInterval);
                    SpawnTrail(light.head.transform.position);
                }

                if (t >= 1f)
                {
                    light.arrived = true;
                    light.head.gameObject.SetActive(false);
                    lights[i] = light;

                    pendingLaunch--;
                    OnLightArrived?.Invoke(light.targetIndex);
                    continue;
                }

                anyFlying = true;
                lights[i] = light;
            }

            if (!anyFlying)
                activeCount = 0; // 전부 도착했다 - 더 굴릴 게 없다
        }

        private void SpawnTrail(Vector3 position)
        {
            var sr = trailPool.Count > 0 ? trailPool.Pop() : CreateRenderer("Trail");

            sr.transform.position = position;
            sr.transform.localScale = Vector3.one * trailScale;
            sr.color = tint;
            sr.gameObject.SetActive(true);

            trails.Add(new Trail { renderer = sr, age = 0f });
        }

        private void TickTrails(float dt)
        {
            float life = Mathf.Max(0.01f, trailLifetime);

            for (int i = trails.Count - 1; i >= 0; i--)
            {
                var trail = trails[i];
                trail.age += dt;

                if (trail.age >= life)
                {
                    Recycle(trail.renderer);
                    trails.RemoveAt(i);
                    continue;
                }

                float fade = 1f - trail.age / life;

                var c = tint;
                c.a = tint.a * fade;
                trail.renderer.color = c;
                trail.renderer.transform.localScale = Vector3.one * (trailScale * fade);

                trails[i] = trail;
            }
        }

        private void Recycle(SpriteRenderer sr)
        {
            sr.gameObject.SetActive(false);
            trailPool.Push(sr);
        }
    }
}
