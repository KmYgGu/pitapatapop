using UnityEngine;

namespace JojoPuzzle.View
{
    /// <summary>
    /// Orthographic 카메라를 보드 크기에 맞춰 자동으로 조정.
    /// 기종/화면 비율마다 카메라 세팅을 따로 안 해도 되도록, 런타임에 화면 비율을 읽어서 계산함.
    /// 세로/가로 화면 모두 대응 (더 넉넉히 필요한 쪽 기준으로 맞춰서 보드가 잘리지 않게 함 = "contain" 방식).
    /// 화면 상단에 HUD(적/내 캐릭터, 타이머 등)를 위한 공간을 예약해서, 보드가 그 아래쪽에 오도록 함.
    /// 화면 하단은 Screen.safeArea(노치/홈 인디케이터/제스처 바 영역)만큼 비워서, 맨 아랫줄 퍼즐을
    /// 누르려다 시스템 제스처가 먼저 먹혀 게임이 중단되는 걸 막는다.
    /// </summary>
    public class CameraFitter : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;

        [Tooltip("보드 가장자리에 남길 여백 (월드 유닛)")]
        [SerializeField] private float padding = 0.5f;

        [Range(0f, 0.8f)]
        [Tooltip("화면 상단에 HUD용으로 비워둘 비율 (0.35 = 화면 위쪽 35%를 보드가 침범하지 않음)")]
        [SerializeField] private float reservedTopFraction = 0.35f;

        [Range(0f, 0.3f)]
        [Tooltip("Screen.safeArea가 알려주는 하단 시스템 영역에 더해서 추가로 비워둘 비율. " +
                 "안드로이드 제스처 내비게이션은 세이프에어리어에 잡히지 않는 기기도 있어서 여유분을 둔다.")]
        [SerializeField] private float extraBottomFraction = 0.03f;

        // 마지막으로 맞춘 보드 정보 - 해상도/세이프에어리어가 바뀌었을 때(기기 회전, 폴더블 펼침,
        // 안드로이드가 첫 프레임엔 세이프에어리어를 아직 확정 못 준 경우 등) 같은 보드로 다시
        // 계산하기 위해 보관해둔다.
        private bool hasFitted;
        private float fittedBoardWidth;
        private float fittedBoardHeight;
        private Vector3 fittedBoardCenter;

        private Rect lastSafeArea;
        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = GetComponent<Camera>();
            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        private void Update()
        {
            if (!hasFitted)
                return;

            // 값이 실제로 바뀐 프레임에만 다시 계산 - 평소엔 아무 일도 하지 않음.
            if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight && Screen.safeArea == lastSafeArea)
                return;

            Apply();
        }

        /// <summary>
        /// boardWidthUnits/boardHeightUnits: 보드 전체가 차지하는 월드 유닛 크기 (칸 수 × cellSize).
        /// boardCenter: 보드 중심의 월드 좌표 (카메라를 이 위치로 옮겨서 정중앙 정렬).
        /// GameEntryPoint에서 보드 생성 직후 호출.
        /// </summary>
        public void FitToBoard(float boardWidthUnits, float boardHeightUnits, Vector3 boardCenter)
        {
            fittedBoardWidth = boardWidthUnits;
            fittedBoardHeight = boardHeightUnits;
            fittedBoardCenter = boardCenter;
            hasFitted = true;

            Apply();
        }

        private void Apply()
        {
            if (targetCamera == null || !targetCamera.orthographic)
            {
                Debug.LogWarning("[CameraFitter] Orthographic 카메라가 아니거나 카메라를 못 찾았습니다.");
                return;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastSafeArea = Screen.safeArea;

            float bottomFraction = GetBottomReservedFraction();

            float halfHeightNeeded = fittedBoardHeight / 2f + padding;
            float halfWidthNeeded = fittedBoardWidth / 2f + padding;

            // 세로: 상단(HUD) + 하단(시스템 제스처 영역) 예약분을 뺀 나머지 띠에 보드가 딱 맞도록
            // 전체 세로 시야를 그만큼 더 크게 잡음. 예약분 합이 1에 가까워도 0으로 나누지 않도록 하한을 둔다.
            float usableFraction = Mathf.Max(0.05f, 1f - reservedTopFraction - bottomFraction);
            float visibleHalfHeightForBoard = halfHeightNeeded / usableFraction;

            // 가로 여유분을 세로 기준(orthographicSize)으로 환산해서, 화면 비율(aspect)이
            // 좁든 넓든 어느 쪽도 잘리지 않도록 둘 중 더 큰 값을 채택 (contain 방식)
            float requiredSizeForWidth = halfWidthNeeded / targetCamera.aspect;

            float orthoSize = Mathf.Max(visibleHalfHeightForBoard, requiredSizeForWidth);
            targetCamera.orthographicSize = orthoSize;

            // 보드 중심이 "예약분을 뺀 띠"의 한가운데 오도록 카메라를 옮긴다.
            // 상단 예약분만큼은 위로, 하단 예약분만큼은 아래로 밀리므로 그 차이만큼만 이동하면 된다
            // (하단 예약이 0이면 기존 동작과 완전히 동일).
            float cameraYOffset = orthoSize * (reservedTopFraction - bottomFraction);

            Vector3 pos = targetCamera.transform.position;
            targetCamera.transform.position = new Vector3(fittedBoardCenter.x, fittedBoardCenter.y + cameraYOffset, pos.z);
        }

        /// <summary>
        /// 화면 아래쪽에서 비워둬야 할 비율. Screen.safeArea.y가 곧 "화면 바닥에서 안전 영역까지의
        /// 픽셀 거리"라서 그대로 비율로 환산하면 되고, 여기에 extraBottomFraction을 더한다.
        /// iOS 홈 인디케이터는 세이프에어리어에 확실히 잡히지만 안드로이드 제스처 바는 전체화면
        /// 모드/기기에 따라 안 잡히는 경우가 있어서, 추가 여유분을 둘 수 있게 해둠.
        /// </summary>
        private float GetBottomReservedFraction()
        {
            float screenHeight = Screen.height;
            if (screenHeight <= 0f)
                return extraBottomFraction;

            float safeInsetFraction = Screen.safeArea.y / screenHeight;
            return Mathf.Clamp01(safeInsetFraction + extraBottomFraction);
        }
    }
}
