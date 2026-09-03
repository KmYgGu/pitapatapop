using UnityEngine;

namespace JojoPuzzle.MiniGame
{
    /// <summary>
    /// 미니게임 화면의 <b>무대를 짜는</b> 컴포넌트 - 방을 재고, 그 크기에 맞춰 테이블과 캐릭터를
    /// 놓고, 카메라를 맞춘다(2026-09-02).
    ///
    /// <b>⚠ 좌표를 숫자로 박으면 안 된다.</b> FBX 의 <c>UnitScaleFactor</c> 가 1.0(cm) 이라
    /// 아파트 모델은 유니티에서 <b>0.01배로 들어온다</b> - 42 유닛짜리 건물이 0.42 유닛이 된다.
    /// 게다가 루트에 <c>Lcl Scaling (0.688, 1, 1)</c> 이 붙어 있어서 스케일을 1로 덮으면
    /// 비율까지 깨진다. 재익스포트하면 이 값들이 또 바뀐다.
    ///
    /// 그래서 아파트 씬(<c>ApartmentCameraRig</c> · <c>ApartmentRooms</c>)과 같은 방식을 쓴다 -
    /// <b>실제로 그려진 크기를 재서 비율로 배치한다.</b> 모델을 갈아끼워도 따라온다.
    ///
    /// 처음에 이걸 안 하고 좌표를 박았다가 <b>방도 테이블도 화면에 안 나왔다</b>
    /// (캐릭터만 보였는데, Spine 은 자기 키를 직접 맞추기 때문이다).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MiniGameStage : MonoBehaviour
    {
        [Header("무대 위의 것들")]
        [Tooltip("방 모델(아파트 FBX)의 뿌리. 이걸 재서 나머지를 배치한다.")]
        [SerializeField] private Transform room;

        [Tooltip("테이블 모델의 뿌리. 방 폭에 맞춰 크기와 자리를 잡아준다.")]
        [SerializeField] private Transform table;

        [SerializeField] private MiniGameCharacterStand stand;

        [Header("어느 층을 담을지")]
        [Tooltip("건물 높이에서 이 층의 바닥이 있는 비율. ApartmentRooms 의 1층 값과 같게 둔다.")]
        [Range(0f, 1f)]
        [SerializeField] private float floorBottomRatio = 0.020f;

        [Tooltip("천장 비율.")]
        [Range(0f, 1f)]
        [SerializeField] private float floorTopRatio = 0.327f;

        [Header("화면")]
        [Tooltip("아래쪽 UI(카드·대사창·버튼)가 화면을 덮는 비율. 그만큼 캐릭터를 위로 올려 담는다. " +
                 "<b>비율로 두는 이유</b>는 레터박스 배율이 기기마다 다르기 때문이다.")]
        [Range(0f, 0.9f)]
        [SerializeField] private float uiBottomFraction = 0.53f;

        [Tooltip("카메라 화각(도). <b>넓을수록 가까이 다가가 '방 안에 있다'는 느낌이 난다</b> - " +
                 "같은 구도를 짧은 거리에서 잡게 되어 카메라가 방 안까지 들어온다.")]
        [Range(20f, 80f)]
        [SerializeField] private float fieldOfView = 71f;

        [Header("구도 - 캐릭터 키에 대한 비율")]
        [Tooltip("화면에 들어올 맨 아래 지점. 0.3 이면 허벅지쯤부터 보이고 발은 안 보인다.\n" +
                 "<b>0 으로 두면 발밑이 화면 가운데로 온다</b> - 방 전체를 담지 않는 게 요점이다.")]
        [Range(0f, 0.8f)]
        [SerializeField] private float viewBottomFraction = 0.54f;

        [Tooltip("머리 위로 남길 여유.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float viewHeadroom = 0.14f;

        [Header("배치 - 방 크기에 대한 비율")]
        [Tooltip("캐릭터 키 = 방 높이 x 이 값.")]
        [Range(0.2f, 1.2f)]
        [SerializeField] private float characterHeightFraction = 0.84f;

        [Tooltip("캐릭터가 설 깊이. 0이면 뒷벽에 붙고 1이면 방 앞이다.")]
        [Range(0f, 1f)]
        [SerializeField] private float characterDepthFraction = 0.34f;

        [Header("테이블")]
        [Tooltip("테이블 높이 = <b>캐릭터 키</b> x 이 값. 0.42 면 상판이 가슴 밑에 온다 " +
                 "(2026-09-02 사용자 확정). <b>폭이 아니라 높이로 맞추는 이유</b>는 " +
                 "'캐릭터의 어느 지점을 가리느냐'가 보는 사람에게 읽히는 값이기 때문이다.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float tableHeightFraction = 0.32f;

        [Tooltip("테이블이 놓일 깊이. 캐릭터보다 커야 앞에 놓인다.")]
        [Range(0f, 1.2f)]
        [SerializeField] private float tableDepthFraction = 0.56f;

        private void Awake()
        {
            // <b>Awake 에서 한다</b> - MiniGameFlow 가 Start 에서 캐릭터를 세우므로 그보다 먼저
            // 키와 바닥 높이를 정해줘야 한다.
            Build();
        }

        [ContextMenu("무대 다시 짜기")]
        public void Build()
        {
            if (room == null)
                return;

            if (!TryMeasure(room, out Bounds building))
            {
                Debug.LogWarning("[MiniGameStage] 방 모델에서 잴 것을 못 찾았습니다 - 배치를 건너뜁니다.");
                return;
            }

            float height = building.size.y;
            float floorY = building.min.y + height * floorBottomRatio;
            float ceilY = building.min.y + height * floorTopRatio;
            float roomHeight = Mathf.Max(0.0001f, ceilY - floorY);

            float backZ = building.min.z;
            float depth = building.size.z;
            float centerX = building.center.x;

            // ---- 캐릭터 ----
            float characterZ = backZ + depth * characterDepthFraction;
            if (stand != null)
            {
                stand.transform.position = new Vector3(centerX, floorY, characterZ);
                stand.Configure(roomHeight * characterHeightFraction, floorY);
            }

            // ---- 테이블 ----
            // ⭐ <b>폭이 아니라 높이로 맞춘다</b>(2026-09-02 사용자 지시: "캐릭터의 가슴 밑 정도").
            // 어느 지점을 가리는지가 보는 사람에게 읽히는 값이고, 폭은 모델 비율을 따라간다.
            float characterHeight = roomHeight * characterHeightFraction;
            float tableZ = backZ + depth * tableDepthFraction;

            if (table != null && TryMeasure(table, out Bounds tableBounds) && tableBounds.size.y > 0.0001f)
            {
                float want = characterHeight * tableHeightFraction;

                // <b>스케일을 덮어쓰지 않고 곱한다</b> - 임포트 배율이 그 안에 들어 있다.
                table.localScale *= want / tableBounds.size.y;

                // 크기를 바꿨으니 다시 재서 밑면을 바닥에 맞춘다(원점이 어디든 상관없게).
                table.position = new Vector3(centerX, floorY, tableZ);
                if (TryMeasure(table, out Bounds placed))
                    table.position += new Vector3(0f, floorY - placed.min.y, 0f);
            }

            // ---- 카메라 ----
            // ⭐ <b>방이 아니라 캐릭터를 기준으로 잡는다</b>(2026-09-02 사용자 지시).
            // 방 전체를 담으면 세로 화면에서는 바닥이 화면 가운데로 올라와
            // "캐릭터의 발밑을 보고 있는" 그림이 된다. 마주 앉은 느낌을 내려면
            // <b>상체가 화면을 채워야</b> 하고, 그러려면 카메라가 그만큼 다가가야 한다.
            var cam = GetComponent<Camera>();
            cam.fieldOfView = fieldOfView;

            float visible = Mathf.Max(0.05f, 1f - uiBottomFraction);

            // 화면에 담을 세로 구간: 캐릭터의 viewBottomFraction 지점부터 머리 위 여유까지.
            float bandBottom = floorY + characterHeight * viewBottomFraction;
            float bandTop = floorY + characterHeight * (1f + viewHeadroom);
            float band = Mathf.Max(0.0001f, bandTop - bandBottom);

            // 그 구간이 "UI 위쪽"을 채우도록 전체 화면 높이를 역산한다.
            float fullHeight = band / visible;
            float distance = fullHeight * 0.5f / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);

            // 구간의 가운데를 UI 위쪽 구간의 가운데에 놓는다.
            float targetFraction = (uiBottomFraction + 1f) * 0.5f;
            float camY = (bandBottom + bandTop) * 0.5f - (targetFraction - 0.5f) * fullHeight;

            // 거리는 <b>캐릭터가 선 면</b> 기준이다 - 화면에서 제일 중요한 게 캐릭터라서.
            // 화각이 넓으면 이 거리가 짧아져 카메라가 방 안으로 들어온다.
            transform.position = new Vector3(centerX, camY, characterZ + distance);
            transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

            cam.nearClipPlane = Mathf.Max(0.01f, distance * 0.01f);
            cam.farClipPlane = distance + depth * 2f + 1f;
        }

        /// <summary>그 아래 모든 렌더러를 합친 크기. 하나도 없으면 false.</summary>
        private static bool TryMeasure(Transform root, out Bounds bounds)
        {
            bounds = new Bounds(root.position, Vector3.zero);

            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds.size.y > 0.0001f;
        }
    }
}
