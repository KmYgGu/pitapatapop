using UnityEngine;

namespace JojoPuzzle.Apartment
{
    /// <summary>
    /// 아파트 모델 전체가 화면에 들어오도록 카메라를 맞춘다.
    /// 배틀의 <c>CameraFitter</c>와 같은 방식(contain - 가로·세로 중 더 넉넉히 필요한 쪽 기준)이고,
    /// 화면 크기가 바뀐 프레임에만 다시 계산한다.
    ///
    /// <b>크기를 숫자로 적지 않고 모델에서 재는 이유</b>: 이 FBX 의 임포트 배율은 파일의 단위
    /// 설정(UnitScaleFactor)과 노드 스케일이 곱해져 정해지고, 모델을 다시 익스포트하면 그 값이
    /// 바뀔 수 있다. 카메라 값을 손으로 박아두면 모델을 갈아끼울 때마다 화면이 어긋난다.
    /// 렌더러의 실제 bounds 를 읽으면 배율이 몇이든 저절로 맞는다.
    /// (이 프로젝트의 기존 방침과도 같다 - 화면 값은 캡처하지 말고 데이터에서 구할 것.)
    ///
    /// <b>원근(망원) 인 이유</b>(2026-08-24 변경): 처음엔 Orthographic 이었는데, 원근이 아예 없으니
    /// 3D 모델인데도 <b>세워둔 2D 그림과 구분이 안 됐다</b>. 그렇다고 화각을 넓히면 가장자리 방이
    /// 사다리꼴로 일그러진다. 그래서 <b>화각만 좁힌 원근</b>(망원 렌즈)으로 간다 - 방 안쪽 벽이
    /// 살짝 보여 깊이가 읽히면서 형태는 무너지지 않는다. 세기는 <see cref="fieldOfView"/> 하나로 조절한다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ApartmentCameraRig : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("아파트 모델의 루트. 비워두면 시작할 때 자기 씬에서 찾지 않고 경고만 낸다 - " +
                 "엉뚱한 오브젝트를 잡아 조용히 이상한 화면이 되는 것보다 낫다.")]
        [SerializeField] private Transform apartmentRoot;

        [Tooltip("아파트 동 목록. 물려두면 <b>동 전부</b>를 감싸도록 맞춘다. " +
                 "비워두면 apartmentRoot 하나만 본다(예전 동작).")]
        [SerializeField] private ApartmentBuildings buildings;

        [Header("렌즈")]
        [Tooltip("세로 화각(도). <b>망원 렌즈처럼 좁게</b> 두는 게 이 화면의 방침이다 - " +
                 "정투영은 원근이 아예 없어 세워둔 2D 그림과 구분이 안 되고, 화각이 넓으면 " +
                 "가장자리 방이 사다리꼴로 일그러진다.\n" +
                 "35mm 환산: 14도=98mm / 18도=76mm / 20도=68mm / 28도=48mm.\n" +
                 "<b>입체감을 더 주고 싶으면 올리고, 더 납작하게 하려면 내린다.</b>")]
        [Range(5f, 60f)]
        [SerializeField] private float fieldOfView = 20f;

        [Header("맞추기")]
        [Tooltip("모델 가장자리에 남길 여백(월드 유닛이 아니라 <b>모델 크기 대비 비율</b>). " +
                 "0.05면 위아래·좌우로 5%씩 여유가 생긴다. 모델 배율이 바뀌어도 여백 비율은 유지된다.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float paddingFraction = 0.06f;

        [Tooltip("카메라가 모델을 <b>바라보는</b> 방향. 이 모델은 정면(방 UI 판)이 +Z 끝에 있고 " +
                 "몸통이 -Z 쪽으로 뻗어 있어서, 카메라는 +Z 에 서서 -Z 를 봐야 한다 → 기본값 (0,0,-1). " +
                 "뒤통수가 보이면 이 값을 (0,0,1)로 뒤집으면 된다.")]
        [SerializeField] private Vector3 viewDirection = new Vector3(0f, 0f, -1f);

        [Header("HUD 가 덮는 영역")]
        [Tooltip("레터박스된 UI 판(HudContent). 아래 예약 비율은 <b>화면이 아니라 이 판 기준</b>이라, " +
                 "세로로 긴 기기에서 UI 가 레터박스 안으로 줄어들어도 아파트가 그만큼만 비켜준다.\n" +
                 "비워두면 화면 전체를 UI 판으로 치고 계산한다(레터박스 없던 시절 동작).")]
        [SerializeField] private RectTransform hudContent;

        [Tooltip("위쪽에서 HUD 가 차지하는 비율. 아파트가 그 아래에 들어오도록 화면을 넓게 잡고 " +
                 "카메라를 밀어준다. 배틀의 CameraFitter.reservedTopFraction 과 같은 방식.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float reservedTopFraction = 0.24f;

        [Tooltip("아래쪽 버튼 줄이 차지하는 비율.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float reservedBottomFraction = 0.16f;

        [Tooltip("오른쪽 세로 아이콘 줄이 차지하는 비율.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float reservedRightFraction = 0.22f;

        [Tooltip("왼쪽에 비워둘 비율. 지금 왼쪽에는 세로로 겹치는 UI 가 없어서 0이다.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float reservedLeftFraction = 0f;

        private Bounds modelBounds;
        private bool hasBounds;

        private Camera cam;
        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;

        /// <summary>HUD 판 모서리를 받는 버퍼. 매번 새 배열을 만들면 그것만으로 GC 가 돈다.</summary>
        private readonly Vector3[] worldCorners = new Vector3[4];

        private void Awake()
        {
            cam = GetComponent<Camera>();
        }

        private void Start()
        {
            if (!TryMeasureModel())
                return;

            // 페이저가 이미 동 하나로 좁혔으면 그대로 둔다(위 viewRequested 주석 참고).
            if (!viewRequested)
                FocusAll();
        }

        /// <summary>
        /// <b>LateUpdate 인 이유</b>: HUD 판 크기를 <c>UiScaleToFit</c> 이 정하는데 그건 레이아웃
        /// 단계에서 돌아간다. Update 에서 읽으면 화면이 바뀐 첫 프레임에 옛 크기를 읽는다.
        /// (배틀의 HudReservedAreaSync 도 같은 이유로 LateUpdate 다.)
        /// </summary>
        private void LateUpdate()
        {
            if (!hasBounds)
                return;

            // 값이 실제로 바뀐 프레임에만 다시 계산 - 평소엔 아무 일도 하지 않는다.
            if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
                return;

            // <b>지금 보고 있는 것</b>을 다시 맞춘다. 무조건 FocusAll 하면 방을 확대해 둔 채로
            // 기기를 돌렸을 때 아파트 전체로 튕겨 나간다.
            if (focusedRoom.HasValue)
                FrameFocused(focusedRoom.Value);
            else
                FocusAll(false, allKeepsHud, allExtraBottom);
        }

        /// <summary>
        /// 지금 확대해 둔 방의 영역. 없으면 전체를 보고 있다는 뜻이다.
        /// <b>영역을 들고 있는 이유</b>: 화면 크기가 바뀌면 같은 방을 다시 맞춰야 하는데,
        /// 방 번호만 들고 있으면 방 목록을 다시 물어봐야 해서 의존이 하나 더 생긴다.
        /// </summary>
        private Bounds? focusedRoom;

        /// <summary>지금 방 하나를 확대해 보고 있는지.</summary>
        public bool IsRoomFocused => focusedRoom.HasValue;

        /// <summary>
        /// 아직 <b>움직이는 중</b>인지. 화면을 옮기는 동안에는 방을 열지 않는 등,
        /// "다 돌아온 뒤에" 받아야 하는 조작이 이걸 본다.
        /// </summary>
        public bool IsMoving => moveRoutine != null;

        /// <summary>
        /// 잰 모델 전체 영역. 방을 자르려면 이게 필요하다(<see cref="ApartmentRooms"/>).
        /// 아직 못 쟀으면 여기서 한 번 재본다.
        /// </summary>
        /// <summary>동이 늘었다 - 크기를 다시 재고 화면에 맞춘다.</summary>
        public void RemeasureAndRefocus()
        {
            hasBounds = false;
            if (!TryMeasureModel())
                return;

            if (focusedRoom.HasValue)
                FrameFocused(focusedRoom.Value);
            else
                FocusAll(true, allKeepsHud, allExtraBottom);
        }

        public bool TryGetModelBounds(out Bounds bounds)
        {
            if (!hasBounds)
                TryMeasureModel();

            bounds = modelBounds;
            return hasBounds;
        }

        /// <summary>
        /// 방 하나를 화면 가득 확대한다. 돌아올 때는 <see cref="FocusAll"/>.
        ///
        /// <b>예약 여백은 그대로 지킨다</b> - 확대해도 HUD 뒤로 방이 들어가면 안 된다.
        /// </summary>
        /// <param name="extraBottomFraction">
        /// 화면 <b>아래쪽을 추가로 비워둘</b> 비율. 입주 화면이 아래에서 올라와 방을 가리므로,
        /// 그만큼 방을 위로 밀어 올려야 한다. 예약 여백과 같은 방식이라 <b>확대 배율도 같이
        /// 줄어든다</b> - 남은 띠에 방이 통째로 들어간다.
        /// </param>
        /// <param name="smooth">
        /// 부드럽게 옮길지. <b>씬에 들어오자마자 방을 여는 길에서는 꺼야 한다</b> -
        /// 메인 화면에서 확대되는 게 보이면 "한 번 떴다가 들어가는" 흔적이 된다
        /// (2026-09-02 사용자 지시).
        /// </param>
        public void FocusRoom(Bounds roomBounds, float extraBottomFraction = 0f,
            float extraTopFraction = 0f, bool smooth = true)
        {
            focusedRoom = roomBounds;
            viewRequested = true;
            focusedExtraBottom = Mathf.Clamp01(extraBottomFraction);
            focusedExtraTop = Mathf.Clamp01(extraTopFraction);
            focusedKeepsHud = false;

            animate = smooth;
            FrameFocused(roomBounds);
            animate = false;
        }

        /// <summary>
        /// 동 <b>하나</b>만 화면에 담는다(메인 화면). <see cref="FocusRoom"/> 과 달리
        /// <b>HUD 예약 여백을 그대로 지킨다</b> - 여기서는 HUD 가 화면에 그대로 있기 때문이다.
        /// </summary>
        public void FocusBuilding(Bounds buildingBounds, bool smooth = true)
        {
            focusedRoom = buildingBounds;
            viewRequested = true;
            focusedExtraBottom = 0f;
            focusedExtraTop = 0f;
            focusedKeepsHud = true;

            animate = smooth;
            FrameFocused(buildingBounds);
            animate = false;
        }

        // 지금 확대해 둔 것이 HUD 자리를 비켜줘야 하는지. 방 확대는 HUD 가 비켜나므로 false,
        // 메인 화면의 동 보기는 HUD 가 그대로 있으므로 true.
        private bool focusedKeepsHud;

        // 방을 확대한 동안 아래·위쪽에 더 비워둘 비율(입주 화면·방 화면 띠가 차지하는 만큼).
        private float focusedExtraBottom;
        private float focusedExtraTop;

        private void FrameFocused(Bounds target)
        {
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            // 메인 화면에서 동 하나를 보는 경우에는 HUD 가 그대로 있으므로 그 자리를 비켜준다.
            if (focusedKeepsHud)
            {
                TryResolveReservedArea(out var hudReserved);
                FrameBounds(target, hudReserved);
                return;
            }

            // <b>⚠ HUD 예약을 쓰지 않는다</b>(2026-08-28 사용자 신고: 확대했는데 오히려 작아졌다).
            // 방을 확대하는 동안 HUD 는 화면 밖으로 비켜나므로(<c>HudSlideAway</c>) 그 자리를
            // 남겨둘 이유가 없다. 예전엔 HUD 예약(위 0.24 + 아래 0.16)에 입주 화면(0.44)까지
            // 얹어서 <b>쓸 수 있는 세로가 16% 밖에 안 남았다</b> - 그래서 방이 도리어 작아졌다.
            var reserved = new Reserved
            {
                Top = focusedTopFraction + focusedExtraTop,
                Bottom = focusedExtraBottom,
                Left = 0f,
                Right = 0f
            };

            FrameBounds(target, reserved);
        }

        [Tooltip("방을 확대했을 때 화면 위쪽에 남길 여백 비율. 천장이 화면 끝에 딱 붙지 않게 하는 정도면 된다.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float focusedTopFraction = 0.04f;

        /// <summary>
        /// 모델 전체가 보이도록 맞춘다. 방 하나를 확대하는 기능이 생기면 그 반대편(돌아오기)이
        /// 이것이다.
        /// </summary>
        /// <summary>
        /// 전체로 돌아간다. <paramref name="smooth"/> 를 켜면 부드럽게 축소한다 -
        /// 화면 크기 변화로 다시 맞출 때는 <b>즉시</b>여야 하므로 기본은 꺼져 있다
        /// (기기를 돌렸는데 카메라가 슬금슬금 움직이면 고장처럼 보인다).
        /// </summary>
        /// <param name="keepHud">
        /// HUD 자리를 비켜줄지. 메인 화면은 HUD 가 그대로 있으니 true,
        /// <b>전체 보기는 HUD 가 화면 밖으로 비켜나므로 false</b> - true 로 두면 HUD 가 없는데도
        /// 오른쪽에 빈 띠가 남는다(2026-08-30 사용자 신고).
        /// </param>
        /// <param name="extraBottom">
        /// 아래쪽에 더 비워둘 비율. 전체 보기의 아래 띠처럼 <b>실제로 잰 값</b>을 넘긴다.
        /// </param>
        public void FocusAll(bool smooth = false, bool keepHud = true, float extraBottom = 0f)
        {
            if (!hasBounds && !TryMeasureModel())
                return;

            focusedRoom = null;
            focusedExtraBottom = 0f;
            focusedExtraTop = 0f;
            focusedKeepsHud = false;
            viewRequested = true;
            animate = smooth;

            allKeepsHud = keepHud;
            allExtraBottom = Mathf.Max(0f, extraBottom);

            bool measured;
            Reserved reserved;

            if (keepHud)
            {
                // HUD 판을 아직 못 읽었으면(첫 프레임 등) 이번엔 대충 맞춰두고 last* 를 갱신하지
                // 않는다 - 다음 프레임에 다시 시도하게 된다.
                measured = TryResolveReservedArea(out reserved);
            }
            else
            {
                reserved = new Reserved
                {
                    Top = allMarginFraction,
                    Bottom = allMarginFraction + allExtraBottom,
                    Left = allMarginFraction,
                    Right = allMarginFraction
                };
                measured = true;
            }

            if (measured)
            {
                lastScreenWidth = Screen.width;
                lastScreenHeight = Screen.height;
            }

            FrameBounds(modelBounds, reserved);
            animate = false;
        }

        [Tooltip("전체 보기에서 화면 가장자리에 남길 여백 비율. HUD 가 비켜나 있으므로 " +
                 "아파트가 화면을 꽉 채우면 된다 - 딱 붙지 않을 만큼만.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float allMarginFraction = 0.03f;

        // 전체를 볼 때 HUD 자리를 비켜주고 있었는지. 화면 크기가 바뀌어 다시 맞출 때 같은 선택을
        // 이어가야 한다 - 안 그러면 전체 보기 중에 기기를 돌렸을 때 오른쪽 띠가 도로 생긴다.
        private bool allKeepsHud = true;
        private float allExtraBottom;

        /// <summary>
        /// 누군가 "무엇을 볼지"를 이미 정했는지. <b>Start 끼리는 순서가 보장되지 않는다</b> -
        /// 페이저가 먼저 동 하나로 좁혔는데 이쪽 Start 가 뒤에 돌면서 전체 보기로 되돌리는 일이
        /// 있었다(2026-08-30 사용자 신고: 편성 화면에 다녀오면 아파트가 전부 보인다).
        /// </summary>
        private bool viewRequested;

        /// <summary>화면 기준으로 환산된 예약 비율.</summary>
        private struct Reserved
        {
            public float Top;
            public float Bottom;
            public float Left;
            public float Right;
        }

        /// <summary>
        /// 인스펙터의 예약 비율은 <b>HUD 판 기준</b>이다. 레터박스가 생기면 그 판이 화면보다
        /// 작아지므로, 화면 기준으로 바꿔줘야 아파트가 UI 를 정확히 피한다.
        ///
        /// <b>레터박스 여백은 아파트가 써도 된다</b> - 거기엔 UI 가 없다. 그래서 판 전체가 아니라
        /// "판 안에서 UI 가 실제로 덮는 띠"만 예약한다.
        /// </summary>
        private bool TryResolveReservedArea(out Reserved reserved)
        {
            reserved.Top = reservedTopFraction;
            reserved.Bottom = reservedBottomFraction;
            reserved.Left = reservedLeftFraction;
            reserved.Right = reservedRightFraction;

            if (hudContent == null)
                return true; // 판이 없으면 화면 전체가 판이다 - 값을 그대로 쓰면 된다.

            if (Screen.width <= 0 || Screen.height <= 0)
                return false;

            hudContent.GetWorldCorners(worldCorners);

            // Screen Space - Overlay 캔버스라 월드 좌표가 곧 화면 픽셀이다.
            float left = worldCorners[0].x / Screen.width;
            float bottom = worldCorners[0].y / Screen.height;
            float right = worldCorners[2].x / Screen.width;
            float top = worldCorners[2].y / Screen.height;

            float width = right - left;
            float height = top - bottom;

            // 레이아웃이 아직 안 돌아 판이 0 크기면 이번 프레임 값은 못 믿는다.
            if (width <= 0.0001f || height <= 0.0001f)
                return false;

            reserved.Top = 1f - (top - reservedTopFraction * height);
            reserved.Bottom = bottom + reservedBottomFraction * height;
            reserved.Left = left + reservedLeftFraction * width;
            reserved.Right = 1f - (right - reservedRightFraction * width);
            return true;
        }

        /// <summary>
        /// 주어진 영역이 화면에 꽉 차도록 카메라 위치·크기를 정한다.
        /// 방 하나만 확대하는 기능은 이 함수에 <b>그 방의 bounds</b> 를 넘기면 되도록 나눠뒀다.
        /// </summary>
        private void FrameBounds(Bounds target, Reserved reserved)
        {
            if (cam == null)
                return;

            Vector3 dir = viewDirection.sqrMagnitude < 0.0001f ? Vector3.forward : viewDirection.normalized;
            var rotation = Quaternion.LookRotation(dir, Vector3.up);

            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            // 모델이 <b>카메라 축 기준으로</b> 얼마나 큰지 잰다. 월드 x/y 를 그대로 쓰면
            // viewDirection 을 바꾼 순간 가로·세로가 뒤바뀐다.
            float halfRight = ExtentAlong(target.extents, right) * (1f + paddingFraction);
            float halfUp = ExtentAlong(target.extents, up) * (1f + paddingFraction);
            float halfDepth = ExtentAlong(target.extents, dir);

            // HUD 를 뺀 "빈 가운데" 띠에 모델이 들어가야 한다. 그 띠가 화면의 일부뿐이므로
            // 시야를 그 비율만큼 넓게 잡는다. 예약분 합이 1에 가까워도 0으로 나누지 않도록 하한.
            float usableHeight = Mathf.Max(0.05f, 1f - reserved.Top - reserved.Bottom);
            float usableWidth = Mathf.Max(0.05f, 1f - reserved.Left - reserved.Right);

            cam.orthographic = false;
            cam.fieldOfView = Mathf.Clamp(fieldOfView, 1f, 120f);

            float tanHalfFov = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = cam.aspect > 0.0001f ? cam.aspect : 1f;

            // <b>원근이라 "앞면" 기준으로 거리를 잡아야 한다.</b> 가운데를 기준으로 맞추면 카메라에
            // 더 가까운 앞면이 그만큼 크게 찍혀 화면 밖으로 나간다(정투영에는 없던 문제).
            float distanceForHeight = halfUp / (usableHeight * tanHalfFov);
            float distanceForWidth = halfRight / (usableWidth * aspect * tanHalfFov);
            float frontDistance = Mathf.Max(distanceForHeight, distanceForWidth);

            // 앞면에서 잰 거리에 두께의 절반을 더해야 중심까지의 거리가 된다.
            float centerDistance = frontDistance + halfDepth;

            // 앞면이 놓인 평면에서의 화면 절반 크기. 빈 띠의 한가운데가 화면 중앙에서 얼마나
            // 벗어나 있는지를 이걸로 환산한다.
            float visibleHalfHeight = frontDistance * tanHalfFov;

            float offsetRight = visibleHalfHeight * aspect * (reserved.Left - reserved.Right);
            float offsetUp = visibleHalfHeight * (reserved.Bottom - reserved.Top);

            Vector3 position = target.center
                             - right * offsetRight
                             - up * offsetUp
                             - dir * centerDistance;

            // 근/원 평면. 기본값(0.3 / 1000)은 모델이 아주 작게 임포트됐을 때 앞면이 근평면에
            // 잘려 통째로 안 보이는 일이 실제로 생긴다. 원근에서는 근평면이 너무 작아도
            // 깊이 정밀도가 나빠지므로 앞면까지 거리의 절반쯤에 둔다.
            //
            // <b>근/원 평면은 옮겨가는 동안에도 곧바로 넓혀 둔다</b> - 목적지 기준으로만 잡으면
            // 이동 중간에 모델이 잘린다. 둘 중 넉넉한 쪽을 쓴다.
            float near = Mathf.Max(0.01f, frontDistance * 0.5f);
            float far = centerDistance + halfDepth * 2f + frontDistance;

            cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, near);
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, far);

            if (moveDuration <= 0f || !animate || !Application.isPlaying)
            {
                transform.SetPositionAndRotation(position, rotation);
                cam.nearClipPlane = near;
                cam.farClipPlane = far;
                return;
            }

            StartMove(position, rotation, near, far);
        }

        [Header("확대/축소 연출")]
        [Tooltip("방을 확대하거나 전체로 돌아갈 때 카메라가 움직이는 시간(초). " +
                 "0이면 즉시 전환(예전 동작).")]
        [SerializeField] private float moveDuration = 0.35f;

        // 화면 크기 변화처럼 <b>연출이 아닌</b> 재조정은 즉시 적용한다 - 기기를 돌렸는데
        // 카메라가 슬금슬금 움직이면 고장처럼 보인다.
        private bool animate;

        private Coroutine moveRoutine;

        private void StartMove(Vector3 position, Quaternion rotation, float near, float far)
        {
            if (moveRoutine != null)
                StopCoroutine(moveRoutine);

            moveRoutine = StartCoroutine(MoveRoutine(position, rotation, near, far));
        }

        /// <summary>
        /// 지금 자리에서 목적지까지 부드럽게 옮긴다(2026-08-28 사용자 지시).
        /// <b>중간에 다시 불려도 지금 자리에서 이어진다</b> - 방을 연달아 누르거나 확대 중에
        /// 닫아도 튀지 않는다.
        /// </summary>
        private System.Collections.IEnumerator MoveRoutine(Vector3 position, Quaternion rotation,
            float near, float far)
        {
            Vector3 fromPos = transform.position;
            Quaternion fromRot = transform.rotation;
            float fromNear = cam.nearClipPlane;
            float fromFar = cam.farClipPlane;

            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / moveDuration);

                // 양쪽 끝이 뭉툭한 곡선 - 한쪽만 감속하면 튀어나왔다 멎는 느낌이 난다.
                float eased = t * t * (3f - 2f * t);

                transform.SetPositionAndRotation(
                    Vector3.LerpUnclamped(fromPos, position, eased),
                    Quaternion.SlerpUnclamped(fromRot, rotation, eased));

                yield return null;
            }

            transform.SetPositionAndRotation(position, rotation);
            cam.nearClipPlane = Mathf.Lerp(fromNear, near, 1f);
            cam.farClipPlane = Mathf.Lerp(fromFar, far, 1f);
            moveRoutine = null;
        }

        /// <summary>
        /// 축 정렬 상자가 임의의 방향으로 갖는 절반 길이. 방향이 월드 축과 어긋나도 맞는다.
        /// </summary>
        private static float ExtentAlong(Vector3 extents, Vector3 axis)
        {
            return Mathf.Abs(extents.x * axis.x)
                 + Mathf.Abs(extents.y * axis.y)
                 + Mathf.Abs(extents.z * axis.z);
        }

        /// <summary>
        /// 모델의 실제 크기를 렌더러에서 잰다. 자식 렌더러 전부를 합치므로 모델이 여러 조각으로
        /// 나뉘어 있어도 된다.
        /// </summary>
        private bool TryMeasureModel()
        {
            if (apartmentRoot == null)
            {
                Debug.LogWarning("[ApartmentCameraRig] apartmentRoot 가 비어 있습니다.");
                return false;
            }

            // 동이 여럿이면 <b>전부를 감싸는</b> 영역이 기준이다 - 한 동만 재면 새 동이
            // 화면 밖에 남는다(2026-08-28 아파트 추가 기능).
            if (buildings != null && buildings.TryGetAllBounds(out var all))
            {
                modelBounds = all;
                hasBounds = true;
                return true;
            }

            var renderers = apartmentRoot.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[ApartmentCameraRig] apartmentRoot 아래에 렌더러가 없습니다.");
                return false;
            }

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            modelBounds = bounds;
            hasBounds = true;
            return true;
        }
    }
}
