using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 아파트 <b>동</b>들을 들고 있는 곳. 원래는 레벨과 골드를 내고 늘리는 기능이지만,
    /// 지금은 테스트가 먼저라 <b>스페이스바</b>로 오른쪽에 한 동씩 세운다
    /// (2026-08-28 사용자 지시).
    ///
    /// <b>⚠ 저장되지 않는다</b>(사용자 명시). 앱을 다시 켜면 첫 동 하나로 돌아온다 -
    /// 그림과 카메라·전체 보기를 확인하기 위한 임시 장치다.
    ///
    /// <b>동을 복제해서 만든다</b> - 아파트는 프리팹 인스턴스라 원본을 그대로 하나 더 놓으면
    /// 모델을 갈아끼울 때 새 동들도 같이 따라온다(모델을 손으로 다시 놓을 일이 없다).
    /// </summary>
    public class ApartmentBuildings : MonoBehaviour
    {
        [Tooltip("첫 번째 동. 씬에 이미 놓여 있는 아파트 모델의 뿌리.")]
        [SerializeField] private Transform firstBuilding;

        [Tooltip("동과 동 사이 간격(동 하나 폭 대비 비율). 0이면 딱 붙는다.")]
        [Range(0f, 1f)]
        [SerializeField] private float gapFraction = 0.12f;

        [Tooltip("테스트용 - 이 키를 누르면 동이 하나 늘어난다. 저장되지 않는다.")]
        [SerializeField] private KeyCode addKey = KeyCode.Space;

        [Tooltip("동을 몇 개까지 늘릴 수 있는지. 너무 많으면 전체 보기에서 하나하나가 " +
                 "알아볼 수 없이 작아진다.")]
        [Min(1)]
        [SerializeField] private int maxBuildings = 8;

        [Header("떨어지는 연출")]
        [Tooltip("하늘에서 떨어지는 데 걸리는 시간(초). 0이면 그냥 나타난다.")]
        [SerializeField] private float fallDuration = 0.55f;

        [Tooltip("얼마나 높은 데서 떨어질지(동 높이 대비 배). 1이면 자기 키만큼 위에서 떨어진다.")]
        [SerializeField] private float fallHeightMultiplier = 1.6f;

        [Tooltip("착지할 때 납작해지는 정도(0.12면 세로 12% 눌리고 가로가 그만큼 늘어난다).")]
        [Range(0f, 0.4f)]
        [SerializeField] private float squash = 0.12f;

        [Tooltip("납작해졌다 되돌아오는 시간(초).")]
        [SerializeField] private float squashDuration = 0.22f;

        /// <summary>동이 늘거나 줄었을 때. 카메라·방 그림이 다시 맞춘다.</summary>
        public event System.Action OnBuildingsChanged;

        private readonly List<Transform> buildings = new List<Transform>();

        public int Count => buildings.Count;

        /// <summary>
        /// 지금까지 늘려둔 동 수. <b>static 이라 씬을 옮겨다녀도 남는다</b>
        /// (입주 정보 <see cref="ApartmentResidents"/> 와 같은 수명).
        ///
        /// <b>⚠ 이게 없으면 편성·스테이지 화면에 다녀오는 것만으로 1동으로 돌아간다</b>
        /// (2026-08-30 사용자 신고). 그런데 <b>입주 정보는 static 이라 남아 있어서</b>,
        /// 2·3동에 살던 입주민은 있을 방이 없어져 <b>화면에서 사라진다</b> - 둘의 수명이
        /// 어긋나 있었던 게 문제였다.
        ///
        /// <b>파일로 저장하지는 않는다</b>(사용자 지시) - 앱을 껐다 켜면 1동부터다.
        /// </summary>
        private static int persistedCount = 1;

        private void Awake()
        {
            if (firstBuilding == null)
            {
                Debug.LogWarning("[ApartmentBuildings] firstBuilding 이 비어 있습니다 - 스페이스바가 안 듣습니다.", this);
                return;
            }

            buildings.Add(firstBuilding);

            // 씬을 다시 열었으면 늘려뒀던 동을 <b>연출 없이</b> 도로 세운다 - 화면에 들어올 때마다
            // 하늘에서 떨어지면 그게 더 이상하다.
            for (int i = buildings.Count; i < persistedCount; i++)
                SpawnBuilding();
        }

        private void Update()
        {
            if (Input.GetKeyDown(addKey))
                AddBuilding();
        }

        /// <summary>동 하나를 오른쪽에 세운다.</summary>
        public bool AddBuilding()
        {
            // <b>조용히 실패하지 않는다</b> - 눌러도 아무 일이 없으면 무엇이 막았는지 알 수가 없다.
            if (firstBuilding == null)
            {
                Debug.LogWarning("[ApartmentBuildings] firstBuilding 이 비어 있어 동을 못 늘립니다.", this);
                return false;
            }

            if (buildings.Count >= maxBuildings)
            {
                Debug.Log($"[ApartmentBuildings] 동은 {maxBuildings}개까지입니다.", this);
                return false;
            }

            // ⭐ <b>떨어지는 중인 동을 먼저 내려앉힌다</b>(2026-09-02 사용자 신고:
            // "동추가를 빠르게 누르면 카메라가 잠시 더 축소되어 하늘도 찍힌다").
            // 새 동을 알리면 카메라가 <b>모든 동을 감싸도록</b> 크기를 재는데, 그때 앞 동이
            // 아직 하늘에 떠 있으면 그 하늘까지 화면에 담긴다. 아래 SpawnBuilding 이
            // 첫 동 폭을 재는 데에도 눌린 크기가 섞이면 안 된다.
            LandPendingDrops();

            if (!SpawnBuilding())
                return false;

            // 씬을 옮겨다녀도 남게 적어둔다.
            persistedCount = buildings.Count;
            Debug.Log($"[ApartmentBuildings] {buildings.Count}동이 됐습니다.", this);

            // <b>⚠ 자리를 잡고 알린 다음에 떨어뜨린다</b>(2026-08-30에 순서가 깨져 다시 겪음).
            // 카메라는 알림을 받고 크기를 재는데, 그때 새 동이 <b>하늘에 떠 있으면</b>
            // 하늘까지 감싸도록 화면이 확 물러나고 그대로 남는다.
            OnBuildingsChanged?.Invoke();

            var landed = buildings[buildings.Count - 1];
            if (fallDuration > 0f && isActiveAndEnabled
                && TryGetBuildingBounds(0, out var first))
            {
                var drop = new Drop { building = landed, home = landed.position,
                                      scale = landed.localScale };
                drop.routine = StartCoroutine(DropRoutine(drop, first.size.y));
                falling.Add(drop);
            }

            return true;
        }

        // 지금 떨어지는 중인 동들. 다음 동을 세우기 전에 이것부터 내려앉힌다.
        private sealed class Drop
        {
            public Transform building;
            public Vector3 home;
            public Vector3 scale;
            public Coroutine routine;
        }

        private readonly List<Drop> falling = new List<Drop>();

        /// <summary>
        /// 떨어지는 중인 동을 <b>지금 당장</b> 제자리에 앉힌다. 연출을 끊는 셈이지만,
        /// 빠르게 여러 번 누른 사람은 이미 다음 동을 보고 있다 - 하늘까지 찍히는 것보다 낫다.
        /// </summary>
        private void LandPendingDrops()
        {
            for (int i = 0; i < falling.Count; i++)
            {
                var drop = falling[i];
                if (drop.routine != null)
                    StopCoroutine(drop.routine);

                if (drop.building == null)
                    continue;

                drop.building.position = drop.home;
                drop.building.localScale = drop.scale;   // 아파트 함정 ① - 반드시 되돌린다
            }

            falling.Clear();
        }

        /// <summary>
        /// 동 하나를 <b>제자리에</b> 세운다. 떨어지는 연출은 부르는 쪽이 알린 뒤에 시작한다
        /// (그래야 카메라가 하늘이 아니라 제자리를 잰다).
        /// </summary>
        private bool SpawnBuilding()
        {
            if (!TryGetBuildingBounds(0, out var first))
            {
                Debug.LogWarning("[ApartmentBuildings] 첫 동에서 렌더러를 못 찾아 크기를 잴 수 없습니다.", this);
                return false;
            }

            // <b>폭은 실제로 잰다</b> - 임포트 배율이 바뀔 수 있어 숫자로 박으면 안 된다
            // (아파트 함정 ②). 몇 번째 동인지로 자리가 정해지므로 누적 오차도 없다.
            float step = first.size.x * (1f + gapFraction);

            var clone = Instantiate(firstBuilding, firstBuilding.parent);
            clone.name = $"{firstBuilding.name}_{buildings.Count + 1}";

            // <b>회전·크기는 건드리지 않는다</b>(아파트 함정 ①) - 프리팹이 가진 값이 곧 제자리다.
            Vector3 home = firstBuilding.position + Vector3.right * (step * buildings.Count);
            clone.position = home;

            buildings.Add(clone);
            return true;
        }

        /// <summary>
        /// 하늘에서 떨어져 <b>쿵</b> 하고 내려앉는다(2026-08-28 사용자 지시).
        ///
        /// <b>가속해서 떨어진다</b>(t²) - 등속이면 "내려왔다"가 아니라 "미끄러졌다"로 보인다.
        /// 착지하는 순간 세로로 눌렸다 돌아오고(<see cref="squash"/>) 밑변을 따라 먼지가 인다.
        /// </summary>
        private IEnumerator DropRoutine(Drop drop, float buildingHeight)
        {
            Transform clone = drop.building;
            Vector3 home = drop.home;
            Vector3 baseScale = drop.scale;
            float height = buildingHeight * Mathf.Max(0f, fallHeightMultiplier);

            float elapsed = 0f;
            while (elapsed < fallDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fallDuration);

                clone.position = home + Vector3.up * (height * (1f - t * t));
                yield return null;
            }

            clone.position = home;

            // 납작해졌다 되돌아온다. <b>반드시 원래 값으로 되돌린다</b> - 프리팹이 가진 크기가
            // 곧 제자리다(아파트 함정 ①).
            if (squash > 0f && squashDuration > 0f)
            {
                float back = 0f;
                while (back < squashDuration)
                {
                    back += Time.deltaTime;
                    float t = Mathf.Clamp01(back / squashDuration);

                    // 처음에 확 눌렸다가 서서히 펴진다.
                    float amount = squash * (1f - t) * (1f - t);
                    clone.localScale = new Vector3(baseScale.x * (1f + amount),
                                                   baseScale.y * (1f - amount),
                                                   baseScale.z);
                    yield return null;
                }
            }

            clone.localScale = baseScale;

            drop.routine = null;
            falling.Remove(drop);

            // <b>다 내려앉은 뒤에 한 번 더 알린다</b>(2026-08-30 사용자 지시: "추가가 완료되면
            // 다시 원래 카메라로 돌아와줘"). 떨어지고 눌리는 동안 잰 값이 남아 있으면
            // 화면이 물러난 채로 굳는다. 이미 맞게 잡혀 있으면 같은 자리라 아무 일도 안 일어난다.
            OnBuildingsChanged?.Invoke();
        }

        public Transform Get(int index)
            => index >= 0 && index < buildings.Count ? buildings[index] : null;

        /// <summary>동 하나가 월드에서 차지하는 영역. 렌더러를 합쳐서 잰다.</summary>
        public bool TryGetBuildingBounds(int index, out Bounds bounds)
        {
            bounds = default;

            var building = Get(index);
            if (building == null)
                return false;

            var renderers = building.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return true;
        }

        /// <summary>동 전부를 감싸는 영역. 전체 보기가 이걸로 카메라를 맞춘다.</summary>
        public bool TryGetAllBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            for (int i = 0; i < buildings.Count; i++)
            {
                if (!TryGetBuildingBounds(i, out var one))
                    continue;

                if (!any)
                {
                    bounds = one;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(one);
                }
            }

            return any;
        }

        /// <summary>그 x 좌표에 있는 동의 번호. 어느 동에도 안 걸리면 -1.</summary>
        /// <summary>
        /// 그 x 자리의 동. 어느 동도 아니면 -1.
        ///
        /// <b>⚠ 먼저 걸리는 동이 아니라 가장 가까운 동을 고른다</b>(2026-08-30 사용자 신고:
        /// "1동에서 2동으로 옮기려면 2동보다 한참 멀리 놓아야 한다"). 예전엔 순서대로 훑다가
        /// 처음 맞는 동을 돌려줬는데, 여유(<paramref name="slackFraction"/>)를 넉넉히 주면
        /// <b>1동의 여유가 2동 위까지 뻗어</b> 2동에 놓아도 1동이 나왔다.
        /// 안에 들어온 동이 있으면 그게 답이고, 없을 때만 여유 안의 가장 가까운 동을 고른다.
        /// </summary>
        public int FindBuildingAt(float worldX, float slackFraction = 0.05f)
        {
            int nearest = -1;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < buildings.Count; i++)
            {
                if (!TryGetBuildingBounds(i, out var b))
                    continue;

                if (worldX >= b.min.x && worldX <= b.max.x)
                    return i;

                float slack = b.size.x * Mathf.Max(0f, slackFraction);
                float distance = worldX < b.min.x ? b.min.x - worldX : worldX - b.max.x;

                if (distance <= slack && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = i;
                }
            }

            return nearest;
        }
    }
}
