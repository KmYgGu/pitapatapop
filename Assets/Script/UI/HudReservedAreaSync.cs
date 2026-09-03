using UnityEngine;
using JojoPuzzle.View;

namespace JojoPuzzle.UI
{
    /// <summary>
    /// HUD 패널(hudPanel)의 아래쪽 경계를 "실제로 화면에 그려진 퍼즐판의 윗변"에 맞춘다.
    ///
    /// 예전엔 CameraFitter.reservedTopFraction과 같은 비율을 HUD 앵커에도 그대로 넣어서 맞췄는데,
    /// 그 방식은 화면이 길쭉한 기기에서 틈이 벌어졌다: CameraFitter는 보드가 잘리지 않도록
    /// "가로/세로 중 더 넉넉히 필요한 쪽" 기준으로 맞추기(contain) 때문에, 세로로 긴 화면에서는
    /// 가로가 기준이 되면서 보드가 예약 비율이 가정한 것보다 더 아래에 그려진다. 그러면 예약
    /// 비율로만 계산된 HUD 바닥과 실제 보드 윗변 사이에 빈 공간이 생겨 화면이 허전해 보였음.
    ///
    /// 그래서 비율을 따라 계산하는 대신, 보드의 월드 좌표 윗변을 카메라로 뷰포트 좌표(0~1)로
    /// 변환해서 그 값을 그대로 앵커로 쓴다 - 화면 비율이 어떻든 HUD는 항상 퍼즐판 바로 위에 붙는다.
    /// 남는 공간은 패널 위쪽(상태바/노치 영역)으로 몰리는데, HudContent가 pivot을 아래로 두고
    /// 바닥 정렬되기 때문에 HUD 내용물 자체는 계속 보드에 붙어있다.
    /// </summary>
    public class HudReservedAreaSync : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera; // 비워두면 Camera.main 사용
        [SerializeField] private BoardView boardView;
        [SerializeField] private RectTransform hudPanel;

        [Tooltip("보드 윗변에서 HUD를 얼마나 더 띄울지(화면 높이 대비 비율). 0이면 딱 붙음.")]
        [SerializeField] private float extraGapFraction = 0f;

        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;
        private Rect lastSafeArea;

        private void LateUpdate()
        {
            // 보드가 아직 안 만들어졌으면(GameEntryPoint.Start 이전) 크기를 물어볼 수 없으므로,
            // 준비될 때까지 매 프레임 조용히 재시도한다. last* 를 갱신하지 않고 빠져나가므로
            // 준비되는 순간 바로 한 번 적용됨.
            if (boardView == null || hudPanel == null || !boardView.IsInitialized)
                return;

            if (targetCamera == null)
                targetCamera = Camera.main;
            if (targetCamera == null)
                return;

            // 화면 크기나 세이프에어리어가 실제로 바뀐 프레임에만 재계산(기기 회전/해상도 변경 대응).
            // 세이프에어리어도 봐야 하는 이유: CameraFitter가 하단 세이프에어리어만큼 카메라를
            // 다시 맞추므로, 해상도가 그대로여도 세이프에어리어만 바뀌면 보드 위치가 달라진다.
            // (CameraFitter는 Update, 이쪽은 LateUpdate라 항상 갱신된 카메라를 읽게 됨)
            if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight && Screen.safeArea == lastSafeArea)
                return;

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastSafeArea = Screen.safeArea;

            ApplyBoardTopAsPanelBottom();
        }

        private void ApplyBoardTopAsPanelBottom()
        {
            // 칸 영역이 아니라 배경판까지 포함한 "실제로 보이는" 윗변을 기준으로 삼아야 함 -
            // 칸 영역 기준으로 붙이면 배경판이 그보다 위로 튀어나와 있어서 HUD가 그 위를 덮는다.
            float boardTopWorldY = boardView.GetBoardVisualTopWorldY();

            // 카메라가 회전 없는 orthographic이라 x는 결과의 y에 영향을 주지 않음 - 0으로 둬도 안전.
            Vector3 viewportPoint = targetCamera.WorldToViewportPoint(new Vector3(0f, boardTopWorldY, 0f));

            var anchorMin = hudPanel.anchorMin;
            anchorMin.y = Mathf.Clamp01(viewportPoint.y + extraGapFraction);
            hudPanel.anchorMin = anchorMin;

            var anchorMax = hudPanel.anchorMax;
            anchorMax.y = 1f; // HUD는 항상 화면 맨 위까지 차지
            hudPanel.anchorMax = anchorMax;
        }
    }
}
