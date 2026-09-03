using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JojoPuzzle.Core;
using JojoPuzzle.Board;
using JojoPuzzle.Battle;

namespace JojoPuzzle.View
{
    /// <summary>
    /// BoardManager(로직)가 들고 있는 BoardData를 씬에 그려주는 역할.
    /// 입력 처리는 하지 않음 (BoardInputController가 담당) - 순수하게 "그리기"만.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        [Header("프리팹/설정")]
        [SerializeField] private PanelView panelPrefab;
        [SerializeField] private float cellSize = 1f;
        [SerializeField] private float cellGap = 0.05f; // 칸과 칸 사이 간격(월드 유닛) - 패널 자체 크기(cellSize)는 안 변하고 배치 간격만 벌어짐
        [SerializeField] private Transform boardOrigin; // 좌하단(0,0) 기준점. 비워두면 이 오브젝트 자신의 위치 사용
        [SerializeField] private PanelFrameSet frameSet; // 팔레트 슬롯의 frameColor를 실제 프레임 스프라이트로 바꿔주는 참조 애셋

        [Header("보드 전체 배경판 (퍼즐판 전체 뒤에 딱 하나)")]
        [SerializeField] private Sprite boardBackgroundSprite; // 비워두면 흰색 사각형(fallback) 사용
        [SerializeField] private Color boardBackgroundColor = new Color(1f, 1f, 1f, 0.5f);
        [SerializeField] private float boardBackgroundPadding = 0.3f; // 보드 바깥으로 얼마나 더 크게 그릴지(월드 유닛)
        private SpriteRenderer boardBackgroundRenderer;

        [Header("보드 테두리 안쪽 스킬 게이지 (보드 가장자리 칸을 따라 하단 중앙에서 시작해 상단 중앙에서 만남)")]
        [SerializeField] private Color boardGaugeColor = Color.yellow; // A/B 둘 다 같은 색 사용
        [SerializeField] private float boardGaugeLineWidth = 0.3f;
        [SerializeField] private float boardGaugeInset = -0.15f; // 보드 바깥 경계선에서 안쪽으로 얼마나 들어와서 그릴지(월드 유닛)
        private LineRenderer[] gaugeSegmentsA; // 경로 A(왼쪽으로 도는 절반)의 3개 구간, 각각 독립된 LineRenderer
        private LineRenderer[] gaugeSegmentsB; // 경로 B(오른쪽으로 도는 절반)의 3개 구간
        private float currentGaugeProgress;

        // 게이지 경로 꼭짓점(하단중앙→모서리→모서리→상단중앙, 4개)을 담아두는 재사용 버퍼.
        // UpdateBoardGaugeVisual이 매 프레임 불릴 수 있어서 매번 새 배열을 만들지 않기 위함.
        private readonly Vector3[] gaugePathA = new Vector3[4];
        private readonly Vector3[] gaugePathB = new Vector3[4];

        private BoardManager boardManager;
        private List<PaletteSlot> currentPalette;
        private PanelView[,] viewGrid;
        private PanelViewPool pool;

        public float CellSize => cellSize;

        /// <summary>
        /// Initialize가 끝나서 보드 크기/중심을 물어봐도 되는 상태인지. GetBoardWorldSize 등은
        /// boardManager를 참조하므로 그 전에 호출하면 안 됨 - 매 프레임 도는 쪽(HudBoardAlign)이
        /// 준비될 때까지 안전하게 기다리기 위한 플래그.
        /// </summary>
        public bool IsInitialized => boardManager != null;

        /// <summary>실제 칸 배치 간격(패널 크기 + 여백). 위치 계산은 전부 이 값을 기준으로 함.</summary>
        private float CellStep => cellSize + cellGap;

        /// <summary>
        /// 보드 전체가 차지하는 월드 유닛 크기 (가로, 세로). CameraFitter에 넘겨줄 때 사용.
        /// </summary>
        public Vector2 GetBoardWorldSize()
        {
            var board = boardManager.Board;
            return new Vector2(board.width * CellStep, board.height * CellStep);
        }

        /// <summary>
        /// 보드 중심의 월드 좌표. 카메라를 이 지점으로 정렬할 때 사용.
        /// </summary>
        public Vector3 GetBoardWorldCenter()
        {
            var board = boardManager.Board;
            Vector3 origin = boardOrigin != null ? boardOrigin.position : transform.position;
            return origin + new Vector3((board.width - 1) * CellStep / 2f, (board.height - 1) * CellStep / 2f, 0f);
        }

        /// <summary>
        /// 화면에 실제로 보이는 퍼즐판 전체의 월드 영역(배경판 포함).
        /// GetBoardWorldSize는 칸이 차지하는 영역만 계산하는데, 배경판(BoardBackgroundPlate)은
        /// 거기서 boardBackgroundPadding만큼 더 바깥으로 나와 그려진다 - 퍼즐판에 뭔가를 딱 맞춰
        /// 붙이는 쪽(HudReservedAreaSync, BoardDimOverlay)은 이 영역을 기준으로 삼아야 여백만큼
        /// 배경판을 침범하거나 반대로 틈이 벌어지지 않는다.
        /// </summary>
        public Bounds GetBoardVisualBounds()
        {
            Vector2 size = GetBoardWorldSize();
            return new Bounds(
                GetBoardWorldCenter(),
                new Vector3(size.x + boardBackgroundPadding * 2f, size.y + boardBackgroundPadding * 2f, 0f));
        }

        /// <summary>화면에 실제로 보이는 보드의 윗변 월드 Y 좌표(배경판 포함).</summary>
        public float GetBoardVisualTopWorldY() => GetBoardVisualBounds().max.y;

        public void Initialize(BoardManager manager, List<PaletteSlot> palette)
        {
            boardManager = manager;
            currentPalette = palette;
            viewGrid = new PanelView[manager.Board.width, manager.Board.height];

            // 보드 칸 수만큼 미리 만들어둠 - 플레이 중 Instantiate가 거의 발생하지 않게
            pool = new PanelViewPool(panelPrefab, transform, manager.Board.width * manager.Board.height);

            CreateBoardBackgroundAndGauge();
            RedrawFull();
        }

        /// <summary>
        /// 퍼즐판 전체 뒤에 딱 하나 놓일 배경판과, 그 뒤에서 보드 둘레를 따라 차오르는 게이지 두 줄을 생성.
        /// 개별 패널/큐브에는 절대 붙이지 않음 - 오직 보드 전체를 감싸는 하나의 오브젝트로만 존재.
        /// </summary>
        private void CreateBoardBackgroundAndGauge()
        {
            Vector2 boardSize = GetBoardWorldSize(); // 칸들이 차지하는 순수 크기(간격 포함, 패딩 제외)
            Vector3 center = GetBoardWorldCenter();

            // 배경판: 보드 크기 + 여백만큼 사각형으로
            var bgObj = new GameObject("BoardBackgroundPlate");
            bgObj.transform.SetParent(transform, false);
            bgObj.transform.position = center;

            boardBackgroundRenderer = bgObj.AddComponent<SpriteRenderer>();
            boardBackgroundRenderer.sprite = boardBackgroundSprite != null ? boardBackgroundSprite : PanelView.FallbackSprite;
            boardBackgroundRenderer.color = boardBackgroundColor;
            boardBackgroundRenderer.sortingOrder = -10; // 모든 패널보다 확실히 뒤

            float bgWidth = boardSize.x + boardBackgroundPadding * 2f;
            float bgHeight = boardSize.y + boardBackgroundPadding * 2f;
            Vector2 spriteNativeSize = boardBackgroundRenderer.sprite.bounds.size;
            bgObj.transform.localScale = new Vector3(
                spriteNativeSize.x > 0f ? bgWidth / spriteNativeSize.x : 1f,
                spriteNativeSize.y > 0f ? bgHeight / spriteNativeSize.y : 1f,
                1f);

            // 게이지 두 줄(A/B), 각 3구간씩 독립된 LineRenderer로 생성 - 모서리 이음매 지오메트리
            // 계산 자체가 없어서 뾰족하게 튀어나오는 왜곡이 구조적으로 생길 수 없음.
            gaugeSegmentsA = new LineRenderer[3];
            gaugeSegmentsB = new LineRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                gaugeSegmentsA[i] = CreateBoardGaugeSegment($"BoardGaugeA_{i}", boardGaugeColor);
                gaugeSegmentsB[i] = CreateBoardGaugeSegment($"BoardGaugeB_{i}", boardGaugeColor);
            }

            UpdateBoardGaugeVisual();
        }

        private LineRenderer CreateBoardGaugeSegment(string name, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);

            var line = obj.AddComponent<LineRenderer>();
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = color;
            line.endColor = color;
            line.startWidth = boardGaugeLineWidth;
            line.endWidth = boardGaugeLineWidth;
            line.useWorldSpace = true; // 보드 전체 기준 월드 좌표로 그림
            line.alignment = LineAlignment.TransformZ; // 항상 XY 평면에 평평하게 그림(카메라 쪽으로 billboard 안 됨)
            line.numCapVertices = 4;   // 선 끝단(진행 중인 쪽)만 살짝 둥글게 - 구간이 독립적이라 모서리 이음매는 없음
            // 이제 보드 안쪽 가장자리를 따라 그리므로, 가장자리 칸의 패널(기본 정렬 0)에 가려지지
            // 않도록 그 위에 그려져야 함(단, 드래그 중인 패널의 최상단 레이어 100보다는 아래).
            line.sortingOrder = 60;
            line.positionCount = 0;

            return line;
        }

        /// <summary>
        /// 보드 테두리 안쪽 스킬 게이지 진행도(0~1) 설정. 보드 가장자리 칸의 경계선을 따라 하단
        /// 중앙에서 시작해서 A(빨강)는 왼쪽→위→상단 중앙 방향으로, B(파랑)는 대칭으로 오른쪽→위→
        /// 상단 중앙 방향으로 그려지다가, progress=1이 되면 둘 다 상단 중앙에서 만나 사각형이 완성됨.
        /// </summary>
        public void SetGaugeProgress(float progress01)
        {
            currentGaugeProgress = Mathf.Clamp01(progress01);
            UpdateBoardGaugeVisual();
        }

        private void UpdateBoardGaugeVisual()
        {
            if (gaugeSegmentsA == null || gaugeSegmentsB == null || boardManager == null)
                return;

            Vector2 boardSize = GetBoardWorldSize();
            Vector3 center = GetBoardWorldCenter();

            // 보드 바깥(배경판 쪽)이 아니라 보드 안쪽 가장자리를 따라 그리도록, 보드 자체의
            // 절반 크기에서 boardGaugeInset만큼 안으로 들어온 지점을 경로로 삼는다.
            float halfW = boardSize.x / 2f - boardGaugeInset;
            float halfH = boardSize.y / 2f - boardGaugeInset;

            Vector3 bottomCenter = center + new Vector3(0f, -halfH, 0f);
            Vector3 bottomLeft = center + new Vector3(-halfW, -halfH, 0f);
            Vector3 topLeft = center + new Vector3(-halfW, halfH, 0f);
            Vector3 bottomRight = center + new Vector3(halfW, -halfH, 0f);
            Vector3 topRight = center + new Vector3(halfW, halfH, 0f);
            Vector3 topCenter = center + new Vector3(0f, halfH, 0f);

            // 경로 배열은 매번 새로 만들지 않고 재사용한다 - 이 메서드는 스탠드업 타임 카운트다운
            // 동안 매 프레임 호출되므로(SetGaugeProgress), new[]로 만들면 10초에 배열 1,200개가
            // 그대로 GC로 간다. 내용만 덮어쓰면 할당이 0이 됨.
            gaugePathA[0] = bottomCenter;
            gaugePathA[1] = bottomLeft;
            gaugePathA[2] = topLeft;
            gaugePathA[3] = topCenter;

            gaugePathB[0] = bottomCenter;
            gaugePathB[1] = bottomRight;
            gaugePathB[2] = topRight;
            gaugePathB[3] = topCenter;

            // 변의 실제 물리적 길이 비율 대신, 세 변(가로-세로-가로)을 동일한 비중(각 1/3)으로
            // 나눠서 채움 - 보드가 가로/세로로 길쭉해도 특정 변만 유난히 빨리 차는 착시가 없어짐.
            ApplySegmentedReveal(gaugeSegmentsA, gaugePathA, currentGaugeProgress);
            ApplySegmentedReveal(gaugeSegmentsB, gaugePathB, currentGaugeProgress);
        }

        /// <summary>
        /// waypoints가 이루는 각 구간을 "독립된" LineRenderer(segmentLines[i])에 나눠 그림.
        /// 구간마다 물리적 길이와 무관하게 동일한 비중(1/구간수)을 차지하고, 그 구간 안에서는
        /// 시작점→(진행도만큼 보간된 지점)까지 2점짜리 직선만 그리므로 모서리 이음매가 아예 없음.
        /// </summary>
        private static void ApplySegmentedReveal(LineRenderer[] segmentLines, Vector3[] waypoints, float progress01)
        {
            int segmentCount = waypoints.Length - 1;
            float scaledProgress = Mathf.Clamp01(progress01) * segmentCount;

            for (int i = 0; i < segmentCount; i++)
            {
                float localProgress = Mathf.Clamp01(scaledProgress - i);
                var line = segmentLines[i];

                if (localProgress <= 0f)
                {
                    line.positionCount = 0;
                    continue;
                }

                Vector3 start = waypoints[i];
                Vector3 end = Vector3.Lerp(waypoints[i], waypoints[i + 1], localProgress);

                line.positionCount = 2;
                line.SetPosition(0, start);
                line.SetPosition(1, end);
            }
        }

        public Vector3 GridToWorld(int x, int y)
        {
            Vector3 origin = boardOrigin != null ? boardOrigin.position : transform.position;
            return origin + new Vector3(x * CellStep, y * CellStep, 0f);
        }

        public bool TryWorldToGrid(Vector3 worldPos, out int x, out int y)
        {
            Vector3 origin = boardOrigin != null ? boardOrigin.position : transform.position;
            Vector3 local = worldPos - origin;

            x = Mathf.RoundToInt(local.x / CellStep);
            y = Mathf.RoundToInt(local.y / CellStep);

            return boardManager.Board.InBounds(x, y);
        }

        [Header("드래그 중 목적지 칸 테두리")]
        [SerializeField] private Color dragHighlightValidColor = Color.white;
        [SerializeField] private Color dragHighlightInvalidColor = new Color(1f, 0.35f, 0.35f);
        [SerializeField] private float dragHighlightLineWidth = 0.08f;
        private LineRenderer dragHighlightLine;

        /// <summary>
        /// 목적지 테두리용 LineRenderer를 최초 1회만 생성(지연 생성) - 씬에 배치할 프리팹/스프라이트가
        /// 따로 필요 없이, 보드 배경판 뒤 스킬 게이지(BoardGaugeA/B)와 같은 방식으로 가볍게 그린다.
        /// </summary>
        private void EnsureDragHighlightLine()
        {
            if (dragHighlightLine != null)
                return;

            var obj = new GameObject("DragTargetHighlight");
            obj.transform.SetParent(transform, false);

            dragHighlightLine = obj.AddComponent<LineRenderer>();
            dragHighlightLine.material = new Material(Shader.Find("Sprites/Default"));
            dragHighlightLine.startWidth = dragHighlightLineWidth;
            dragHighlightLine.endWidth = dragHighlightLineWidth;
            dragHighlightLine.useWorldSpace = true;
            dragHighlightLine.loop = true; // 4점만 찍어도 마지막-첫점이 자동으로 이어져 닫힌 사각형이 됨
            dragHighlightLine.positionCount = 4;
            dragHighlightLine.sortingOrder = 50; // 그 칸에 놓인 패널(기본 0)보다는 위, 드래그 중인 패널(100)보다는 아래
            dragHighlightLine.enabled = false;
        }

        /// <summary>
        /// 드래그 중인 패널을 손 뗄 때 놓일 칸에 테두리 표시. isValid로 실제로 놓을 수 있는 칸인지
        /// 색을 다르게 보여준다(BoardInputController가 lockedCells/BlocksNormalOverwrite로 판단해서 넘김).
        /// </summary>
        public void ShowDragTargetHighlight(int x, int y, bool isValid)
        {
            EnsureDragHighlightLine();

            Vector3 center = GridToWorld(x, y);
            float half = cellSize / 2f;

            dragHighlightLine.SetPosition(0, center + new Vector3(-half, -half, 0f));
            dragHighlightLine.SetPosition(1, center + new Vector3(-half, half, 0f));
            dragHighlightLine.SetPosition(2, center + new Vector3(half, half, 0f));
            dragHighlightLine.SetPosition(3, center + new Vector3(half, -half, 0f));

            Color color = isValid ? dragHighlightValidColor : dragHighlightInvalidColor;
            dragHighlightLine.startColor = color;
            dragHighlightLine.endColor = color;

            dragHighlightLine.enabled = true;
        }

        /// <summary>드래그가 끝나거나(놓거나 취소) 손가락이 보드 밖으로 나가면 테두리를 숨김.</summary>
        public void HideDragTargetHighlight()
        {
            if (dragHighlightLine != null)
                dragHighlightLine.enabled = false;
        }

        /// <summary>
        /// 특정 칸의 현재 뷰를 조회만 함(파괴 X). 드래그 시작 시 집어들 패널을 찾을 때 사용.
        /// </summary>
        public PanelView GetViewAt(int x, int y) => viewGrid[x, y];

        /// <summary>
        /// 뷰를 grid 추적에서만 떼어냄(오브젝트는 유지). 드래그로 집어든 패널을
        /// 손을 뗄 때까지 자유롭게 움직이기 위해, 보드 추적 배열에서만 잠시 제외.
        /// </summary>
        public PanelView DetachView(int x, int y)
        {
            var view = viewGrid[x, y];
            viewGrid[x, y] = null;
            return view;
        }

        /// <summary>
        /// 특정 칸의 뷰를 풀에 반납(즉시 비활성화). 드래그 목적지에 원래 있던 패널이 덮어써질 때 사용.
        /// </summary>
        public void DestroyViewAt(int x, int y)
        {
            if (viewGrid[x, y] != null)
            {
                pool.Release(viewGrid[x, y]);
                viewGrid[x, y] = null;
            }
        }

        /// <summary>
        /// 특정 칸에 이미 뷰가 있으면 풀에 반납. 새 뷰를 그 칸에 등록하기 전
        /// 안전장치로 호출해서, 어떤 경로로든 orphan(추적 안 되는 잔존 오브젝트)이 생기지 않게 함.
        /// </summary>
        private void ReleaseIfOccupied(int x, int y)
        {
            if (viewGrid[x, y] != null)
                pool.Release(viewGrid[x, y]);
        }

        /// <summary>
        /// 그 칸의 뷰를 풀에 반납하고 추적에서도 지운다. <b>칸이 빈 칸이 됐을 때</b> 쓴다
        /// (수명이 다한 구멍이 사라지는 경우 등).
        ///
        /// 다음에 그 칸을 쓰는 쪽(낙하·리필)이 어차피 ReleaseIfOccupied 로 정리하긴 하지만,
        /// 그 사이 몇 프레임 동안 데이터는 빈 칸인데 화면에는 옛 그림이 남아 있게 된다.
        /// 그 틈을 없애려고 지우는 쪽에서 명시적으로 반납한다.
        /// </summary>
        /// <summary>
        /// 뷰 격자 안의 좌표인지. <b>세 곳에 같은 식이 적혀 있던 것을 모았다</b>(2026-08-28) -
        /// 격자 크기를 바꾸는 날 한 곳만 고치고 나머지를 빠뜨리면 조용히 어긋난다.
        /// 보드 데이터의 <c>BoardData.InBounds</c> 와 <b>다른 격자</b>를 본다는 점에 주의할 것.
        /// </summary>
        private bool InViewBounds(int x, int y)
            => viewGrid != null
               && x >= 0 && y >= 0
               && x < viewGrid.GetLength(0) && y < viewGrid.GetLength(1);

        public void ReleaseViewAt(int x, int y)
        {
            if (viewGrid == null)
                return;
            if (!InViewBounds(x, y))
                return;
            if (viewGrid[x, y] == null)
                return;

            pool.Release(viewGrid[x, y]);
            viewGrid[x, y] = null;
        }

        /// <summary>
        /// 이미 존재하는(파괴되지 않은) 뷰를 특정 칸으로 등록 + 이동. DetachView로 떼어냈던 뷰를
        /// 최종 위치(드롭 지점 또는 취소 시 원위치)로 되돌릴 때 사용.
        /// </summary>
        public void PlaceView(PanelView view, int x, int y)
        {
            if (viewGrid[x, y] != null && viewGrid[x, y] != view)
                pool.Release(viewGrid[x, y]); // 안전장치: 목적지에 다른 뷰가 남아있으면 orphan 방지

            view.SetGridPosition(x, y);
            view.MoveTo(GridToWorld(x, y));
            viewGrid[x, y] = view;
        }

        /// <summary>
        /// 보드 전체를 지금 상태 기준으로 다시 그림. 초기화 시 1회 호출.
        /// </summary>
        public void RedrawFull()
        {
            var board = boardManager.Board;

            for (int x = 0; x < board.width; x++)
            {
                for (int y = 0; y < board.height; y++)
                {
                    if (viewGrid[x, y] != null)
                    {
                        pool.Release(viewGrid[x, y]);
                        viewGrid[x, y] = null;
                    }

                    var cell = board.Get(x, y);
                    if (cell.kind == CellKind.Empty)
                        continue;

                    SpawnView(x, y, cell);
                }
            }
        }

        private PanelView SpawnView(int x, int y, Cell cell, Vector3? spawnPositionOverride = null)
        {
            Vector3 spawnPos = spawnPositionOverride ?? GridToWorld(x, y);
            ReleaseIfOccupied(x, y); // 안전장치: 해당 칸에 이미 뷰가 남아있으면 먼저 반납

            var view = pool.Get(spawnPos);
            view.SetTargetCellSize(cellSize);

            switch (cell.kind)
            {
                case CellKind.Normal:
                {
                    var slot = GetPaletteSlot(cell.panelIndex);
                    var frameSprite = slot.HasValue && frameSet != null ? frameSet.GetSprite(slot.Value.frameColor) : null;
                    view.SetupNormal(x, y, slot?.character, frameSprite);
                    break;
                }
                case CellKind.Box:
                {
                    // 박스는 어떤 색 매치로 만들어졌는지가 panelIndex에 보존돼 있음 - 그 퍼즐의 프레임+이미지를 큐브에 입힘
                    var slot = GetPaletteSlot(cell.panelIndex);
                    var frameSprite = slot.HasValue && frameSet != null ? frameSet.GetSprite(slot.Value.frameColor) : null;
                    view.SetupBox(x, y, slot?.character, frameSprite);
                    break;
                }
                case CellKind.Special:
                {
                    // 미스틱의 특수 패널. 그림 자체는 일반 조각과 같고, 위에 룬이 돈다.
                    var slot = GetPaletteSlot(cell.panelIndex);
                    var frameSprite = slot.HasValue && frameSet != null ? frameSet.GetSprite(slot.Value.frameColor) : null;
                    view.SetupNormal(x, y, slot?.character, frameSprite);
                    break;
                }
                case CellKind.Obstacle:
                    view.SetupObstacle(x, y);
                    break;
                case CellKind.Hole:
                    view.SetupHole(x, y);
                    break;
                case CellKind.BurnTrack:
                    view.SetupBurnTrack(x, y);
                    break;
            }

            // 강화 표시는 데이터에 붙어 있으므로 뷰를 새로 만들 때도 그대로 따라와야 한다
            // (예: 강화된 조각이 낙하로 옮겨져 뷰가 다시 그려질 때).
            if (cell.empowered)
            {
                view.SetEmpowered(true);
                if (!empoweredViews.Contains(view))
                    empoweredViews.Add(view);
            }

            // 특수 패널의 룬도 데이터에 붙어 있으므로 뷰를 새로 만들 때 따라와야 한다.
            if (cell.IsSpecial && cell.specialMatchesLeft > 0)
            {
                view.SetSpecial(cell.specialMatchesLeft);
                if (!specialViews.Contains(view))
                    specialViews.Add(view);
            }

            viewGrid[x, y] = view;
            return view;
        }

        /// <summary>
        /// panelIndex에 해당하는 캐릭터(PanelType). 매치된 색이 누구인지 알아야 전투력을 뽑을 수
        /// 있는데 팔레트는 이 클래스만 들고 있어서, 밖에서 조회할 수 있게 열어둔 통로.
        /// 범위 밖이거나 슬롯이 비어 있으면 null.
        /// </summary>
        public PanelType GetCharacter(int panelIndex)
        {
            var slot = GetPaletteSlot(panelIndex);
            return slot.HasValue ? slot.Value.character : null;
        }

        /// <summary>그 색의 <b>보드 위 프레임 색</b>. 범위 밖이면 null.</summary>
        public PanelFrameColor? ColorOf(int panelIndex)
        {
            var slot = GetPaletteSlot(panelIndex);
            return slot.HasValue ? slot.Value.frameColor : (PanelFrameColor?)null;
        }

        /// <summary>
        /// 리더와 기본색이 겹쳐 <b>색을 갈아 낀</b> 슬롯인지 - 스티커가 말하는 "중복색"이다.
        /// </summary>
        public bool IsDuplicateColor(int panelIndex)
        {
            var slot = GetPaletteSlot(panelIndex);
            return slot.HasValue && slot.Value.isSwappedColor;
        }

        /// <summary>
        /// panelIndex에 해당하는 팔레트 슬롯 조회(범위 밖이면 null) - 캐릭터/렌더링 프레임 색을
        /// 함께 들고 다니는 PaletteSlot을 여러 곳에서 반복해서 안전하게 조회하기 위한 헬퍼.
        /// </summary>
        private PaletteSlot? GetPaletteSlot(int panelIndex)
        {
            if (currentPalette == null || panelIndex < 0 || panelIndex >= currentPalette.Count)
                return null;

            return currentPalette[panelIndex];
        }

        /// <summary>
        /// 제거된 좌표들의 뷰를 풀에 반납.
        /// </summary>
        public void ApplyRemoval(IEnumerable<(int x, int y)> removedCells)
        {
            foreach (var (x, y) in removedCells)
            {
                if (viewGrid[x, y] != null)
                {
                    pool.Release(viewGrid[x, y]);
                    viewGrid[x, y] = null;
                }
            }
        }

        /// <summary>
        /// 그룹이 박스로 전환된 경우, 남은 한 칸을 박스 비주얼로 교체.
        /// </summary>
        public void ApplyBoxConversion(int x, int y)
        {
            var cell = boardManager.Board.Get(x, y);
            viewGrid[x, y] = SpawnView(x, y, cell); // SpawnView 내부에서 기존 뷰 반납까지 처리
        }

        /// <summary>
        /// 박스의 십자 변환(BoardManager.ConvertCrossToNormal)으로 바뀐 칸들을 최신 보드 데이터
        /// 기준으로 다시 그림 - 박스였던 칸은 일반 패널 비주얼로, 기존 뷰는 SpawnView가 알아서 반납.
        /// </summary>
        public void ApplyCrossConversion(IEnumerable<(int x, int y)> convertedCells)
        {
            foreach (var (x, y) in convertedCells)
            {
                var cell = boardManager.Board.Get(x, y);
                viewGrid[x, y] = SpawnView(x, y, cell);
            }
        }

        [Header("효과음")]
        [Tooltip("접기 연출의 효과음을 재생할 대상. 비워두면 소리 없이 연출만 돈다.")]
        [SerializeField] private JojoPuzzle.Audio.SfxPlayer sfx;

        [Header("매치 마무리 연출")]
        [Tooltip("조각이 사라질 때 터지는 폭죽·파장·가루 연출. 비워두면 조각이 그냥 사라진다. " +
                 "<b>앞으로 제거와 관련된 연출은 전부 이걸 쓴다</b>(사용자 방침).")]
        [SerializeField] private JojoPuzzle.UI.MatchFinishEffect matchFinishEffect;

        [Tooltip("접기가 취소돼 여러 칸이 한꺼번에 사라질 때의 연출 세기(0~1). " +
                 "칸마다 전부를 터뜨리면 화면이 뒤덮이므로 낮춰 부른다. " +
                 "1보다 작으면 파장(고리)은 나오지 않는다.")]
        [Range(0f, 1f)]
        [SerializeField] private float popOutEffectIntensity = 0.45f;

        [Header("매치 수집 이펙트")]
        [SerializeField] private float collectHopDuration = 0.1f;      // 인접 칸 한 칸을 접어서 넘어가는 데 걸리는 시간
        [SerializeField] private float pulsePeriod = 0.6f;             // 반짝임 한 사이클 시간(초) - 접히는 속도와 별개로 고정
        [SerializeField] private float pulseBrightness = 1.6f;         // 반짝임 최대 밝기 배율

        [Tooltip("조각이 많은 매치에서 <b>절반까지는 평소 속도로 보여주고, 나머지 절반만</b> " +
                 "빠르게 감을지. 끄면 처음부터 끝까지 collectHopDuration 그대로라 " +
                 "큰 매치가 아주 오래 걸린다.\n" +
                 "전부 빠르게 감으면 총 시간이 늘 같아서 4칸을 맞췄든 12칸을 맞췄든 똑같이 " +
                 "느껴진다 - 앞 절반을 평소 속도로 두면 많이 맞출수록 연출도 길어져 " +
                 "'많이 맞췄다'가 전해진다(효과음 횟수도 그만큼 늘어난다).")]
        [SerializeField] private bool collectSpeedsUpAfterHalf = true;

        private static readonly (int dx, int dy)[] OrthogonalNeighbors =
        {
            (0, 1), (0, -1), (1, 0), (-1, 0)
        };

        [Header("스탠드업 타임 - 매치된 조각 제자리 고정")]
        [SerializeField] private float standUpLockSpinDuration = 0.5f;

        /// <summary>
        /// 이번에 새로/더 크게 정사각형을 이루게 된 자리의 목표 크기/위치.
        /// </summary>
        private struct SquareGrowTarget
        {
            public Vector3 toPos;
            public float toSize;
        }

        /// <summary>
        /// 스탠드업 타임에 고정되는 칸들을 "고정된 모습"으로 바꾼다:
        /// (1) 아이콘을 그 캐릭터의 전용 스탠드업 아이콘으로 교체 - standUpIcon이 비어 있는
        ///     캐릭터는 건드리지 않는다(전용 아이콘이 없으면 기존 아이콘을 쓴다는 뜻).
        /// (2) 아이콘 뒤 / 프레임 앞에서 불꽃을 켠다.
        /// </summary>
        // IEnumerable이 아니라 List로 받는다 - 인터페이스로 받으면 foreach가 열거자를 박싱해서
        // 호출마다 힙 할당이 생긴다. 매치마다 불리는 자리라 List로 고정.
        private void ApplyStandUpLook(List<(int x, int y)> cells)
        {
            foreach (var (x, y) in cells)
            {
                var view = viewGrid[x, y];
                if (view == null)
                    continue;

                var slot = GetPaletteSlot(boardManager.Board.Get(x, y).panelIndex);
                var character = slot?.character;

                if (character != null && character.standUpIcon != null)
                    view.SetIconSprite(character.standUpIcon);

                // 불꽃은 그 조각이 실제로 그려지는 프레임 색으로 칠한다. 캐릭터 고유색이 아니라
                // slot.frameColor를 쓰는 이유는, 리더/파트너 색이 겹쳐 스왑된 경우(BattleSetup)
                // 화면에 보이는 프레임과 불꽃 색이 어긋나면 안 되기 때문.
                if (slot.HasValue && frameSet != null)
                    view.SetFlameTint(frameSet.GetColor(slot.Value.frameColor));

                view.SetFlameActive(true);
            }
        }

        /// <summary>
        /// 스탠드업 고정이 풀려 다시 일반 패널이 된 칸들을 원래 모습(기본 아이콘 + 불꽃 꺼짐)으로 되돌린다.
        /// 스탠드업 타임이 정상 종료될 때는 뷰가 풀에 반납됐다가 재사용 시 다시 Setup되므로 저절로
        /// 되돌아가지만, 박스 십자변환으로 무리가 해제되는 경로(ReleaseUndersizedStandHeldGroupsNear)는
        /// 기존 뷰를 그대로 재사용하기 때문에 여기서 명시적으로 되돌려주지 않으면 데이터는 일반
        /// 패널인데 화면에는 스탠드업 아이콘과 불꽃이 남는다.
        /// </summary>
        public void RestoreDefaultLook(IEnumerable<(int x, int y)> cells)
        {
            foreach (var (x, y) in cells)
            {
                var view = viewGrid[x, y];
                if (view == null)
                    continue;

                var slot = GetPaletteSlot(boardManager.Board.Get(x, y).panelIndex);
                view.SetIconSprite(slot?.character != null ? slot.Value.character.icon : null);
                view.SetFlameActive(false);

                // 더 이상 고정된 조각이 아니므로 숨쉬기도 멈추고 아이콘 크기를 원래대로 돌린다 -
                // 안 그러면 평범한 조각 하나의 아이콘만 혼자 계속 커졌다 작아진다.
                if (pulsingStandUpViews.Remove(view))
                    view.SetIconScaleMultiplier(1f);
            }
        }

        /// <summary>
        /// 스탠드업 타임 중 매치가 성립했을 때 재생. 새로 합류한 조각들을 제자리에서 Y축으로 한 바퀴
        /// 돌리고, 2x2 이상 정사각형을 큰 패널 하나로 합쳐 보여준다.
        ///
        /// 정사각형 목록은 **매번 보드 데이터 전체에서 다시 계산한다**(FindStandHeldGroups →
        /// FindSquareBlocks). 데미지 계산(BoardInputController.CalculateStandUpDamage)과 입력도 함수도
        /// 같으므로 화면에 보이는 덩어리와 데미지 구성이 구조적으로 어긋날 수 없다. 예전처럼 이번에
        /// 매치된 그룹 안에서만 증분 계산하면, 새 정사각형이 기존 합체와 칸을 나눠 갖는 배치에서
        /// 어느 쪽으로도 안 잡혀 크기가 갱신되지 않았다.
        ///
        /// 계산 결과와 현재 등록 상태(activeStandMerges)를 대조해서 **해제를 먼저, 등록을 나중에** 한다 -
        /// 해제해야 흡수돼 숨어 있던 멤버 뷰가 다시 활성화되고 그 뷰가 새 정사각형의 호스트가 될 수 있다.
        /// 원점과 크기가 그대로인 합체는 건드리지 않는다(이미 자리 잡은 블록이 흔들려 보이지 않게).
        ///
        /// 호스트 스냅/멤버 숨김/activeStandMerges 등록은 회전이 끝나길 기다리지 않고 전부 지금(동기적으로)
        /// 끝낸다 - 등록을 회전 종료 후로 미루면, 회전이 채 끝나기 전에 스탠드업 타임이 만료돼
        /// ClearAllStandSquareMerges가 먼저 실행될 때 아직 목록에 없는 이 합체를 놓쳐서 커진 블록이
        /// 영영 원래 크기로 못 돌아가는 버그가 있었음.
        ///
        /// ⭐ <b>데이터는 이미 커밋돼 있다</b>(2026-09-03 연출 규칙: 데이터를 먼저 확정하고
        /// 화면은 이미 끝난 일을 보여준다). 부르는 쪽이 HoldGroupAsStandHeld 를 먼저 끝내고
        /// 오므로 여기서 regionCells 는 전부 StandHeld 다 - 정사각형 계산이 보드만 봐도 옳은
        /// 답을 낸다. 예전엔 순서가 반대라 "곧 고정될 칸" 목록을 따로 들고 다녀야 했고,
        /// 그 0.5초 창으로 합체된 정사각형이 통째로 끌려 나가는 버그가 있었다.
        ///
        /// <paramref name="newlyJoined"/> <b>만은 커밋 전에 재서 받는다</b> - 커밋 뒤엔 전부
        /// StandHeld 라 "이번에 새로 합류한 칸"을 데이터에서 구분할 수 없다. 연출에 필요한
        /// 커밋 전 정보는 이렇게 인자로 넘기고, 데이터에서 되묻지 않는다.
        /// </summary>
        public IEnumerator AnimateStandUpLockAndSquareMerge(List<(int x, int y)> regionCells,
            ISet<(int x, int y)> newlyJoined)
        {
            // 고정되는 조각을 스탠드업 전용 아이콘으로 교체. 아래에서 정사각형에 흡수돼 숨겨질
            // 칸까지 포함해 영역 전체에 적용한다 - 나중에 박스 십자변환 등으로 합체가 풀려 그 칸이
            // 다시 보이게 됐을 때도 아이콘이 어긋나 있지 않게 하기 위함.
            ApplyStandUpLook(regionCells);

            // 화면에 있어야 할 정사각형을 보드 데이터 전체에서 다시 계산해 맞춘다.
            // 위에서 등록한 커밋 대기 칸까지 함께 보므로 "곧 고정될 칸"이 빠지지 않는다.
            var growTargets = new Dictionary<(int x, int y), SquareGrowTarget>();
            var absorbedMembers = RebuildStandUpSquareMerges(growTargets);
            // 회전 대상: (새로 합류한 조각 중 흡수되지 않는 것) + (이번에 호스트가 되는 모든 칸 -
            // 새로 합류했든 이미 고정돼 있었든 매치가 성립했으면 항상 처음부터 회전). 크기/위치는
            // 위에서 이미 확정됐으므로 여기서는 순수하게 회전 비주얼만 재생 - 아무도 기다리지 않음.
            var toRotate = new HashSet<(int x, int y)>(newlyJoined);
            toRotate.ExceptWith(absorbedMembers);
            foreach (var origin in growTargets.Keys)
                toRotate.Add(origin);

            var running = new List<Coroutine>();
            foreach (var cell in toRotate)
                running.Add(StartCoroutine(AnimateStandUpLockSpin(cell.x, cell.y)));

            foreach (var routine in running)
                yield return routine;
        }

        /// <summary>
        /// 보드에 고정된 칸 전체에서 정사각형을 다시 계산해 화면 합체를 그 결과에 맞춘다.
        /// 반환값은 정사각형에 흡수돼 숨겨진 칸들(호스트 제외).
        ///
        /// <b>증분으로 고치지 않고 매번 전체를 다시 구하는 게 핵심이다.</b> 데미지 계산
        /// (BoardInputController.CalculateStandUpDamage)과 같은 입력·같은 함수를 쓰므로 화면에
        /// 보이는 덩어리와 데미지 구성이 구조적으로 어긋날 수 없다.
        ///
        /// ⭐ <b>보드 데이터만 본다.</b> 예전엔 "곧 고정될 칸" 목록을 함께 봐야 했다 - 합체 연출이
        /// 데이터 커밋보다 먼저 일어나서, 보드만 보면 방금 만든 합체가 "보드에 없는 정사각형"으로
        /// 보여 통째로 풀렸기 때문이다. 이제 커밋이 먼저라 그 장치가 통째로 필요 없어졌다.
        ///
        /// growTargets: 이번에 새로 만들어진 정사각형의 (호스트 좌표 → 목표 크기/위치).
        ///   회전 연출을 재생하려는 호출부만 넘기면 되고, 필요 없으면 null.
        /// </summary>
        private HashSet<(int x, int y)> RebuildStandUpSquareMerges(
            Dictionary<(int x, int y), SquareGrowTarget> growTargets)
        {
            // 지금 화면에 자리 잡은 정사각형들을 같이 넘겨서, 데미지가 똑같은 조합이 여럿일 때
            // 기존 자리를 그대로 고르게 한다 - 안 그러면 옆에 조각 하나를 붙였을 뿐인데 이미
            // 합쳐져 있던 블록이 새 자리에 다시 만들어져 툭 옮겨 다니는 것처럼 보인다.
            FillActiveStandSquares(currentSquareBuffer);

            desiredSquareBuffer.Clear();
            heldCellBuffer.Clear(); // 숨쉬기 대상을 다시 잡는 데도 쓴다

            foreach (var heldGroup in boardManager.FindStandHeldGroups())
            {
                heldCellBuffer.AddRange(heldGroup);
                desiredSquareBuffer.AddRange(SquareMergeFinder.FindSquareBlocks(heldGroup, currentSquareBuffer));
            }

            // 1) 더 이상 목록에 없는 합체를 먼저 전부 해제한다. 반드시 등록보다 먼저 - 해제해야
            //    숨겨져 있던 멤버 뷰가 다시 활성화되고, 그 뷰가 새 정사각형의 호스트가 될 수 있다.
            for (int i = activeStandMerges.Count - 1; i >= 0; i--)
            {
                var merge = activeStandMerges[i];

                // 람다(List.Exists) 대신 직접 도는 이유: merge 를 붙잡는 클로저가 반복마다 힙에
                // 할당된다. 스탠드업 중 매치마다 불리는 자리라 그만큼 쌓인다.
                bool stillDesired = false;
                for (int s = 0; s < desiredSquareBuffer.Count; s++)
                {
                    var sq = desiredSquareBuffer[s];
                    if (sq.originX == merge.originX && sq.originY == merge.originY && sq.size == merge.size)
                    {
                        stillDesired = true;
                        break;
                    }
                }

                if (stillDesired)
                    continue; // 원점·크기가 그대로면 손대지 않는다(이미 자리 잡은 블록이 흔들려 보이지 않게)

                UnmergeSquare(merge);
                activeStandMerges.RemoveAt(i);
            }

            // 2) 아직 등록되지 않은 정사각형만 새로 적용. 호스트 스냅 + 흡수된 멤버 숨김 +
            //    activeStandMerges 등록을 회전이 끝나길 기다리지 않고 지금 동기적으로 끝낸다.
            absorbedMemberBuffer.Clear();
            var absorbedMembers = absorbedMemberBuffer;

            foreach (var square in desiredSquareBuffer)
            {
                for (int dx = 0; dx < square.size; dx++)
                    for (int dy = 0; dy < square.size; dy++)
                    {
                        var cell = (square.originX + dx, square.originY + dy);
                        if (cell != (square.originX, square.originY))
                            absorbedMembers.Add(cell);
                    }

                bool alreadyApplied = false;
                for (int m = 0; m < activeStandMerges.Count; m++)
                {
                    var active = activeStandMerges[m];
                    if (active.originX == square.originX && active.originY == square.originY
                        && active.size == square.size)
                    {
                        alreadyApplied = true;
                        break;
                    }
                }

                if (alreadyApplied)
                    continue;

                Vector3 toPos = GridToWorld(square.originX, square.originY)
                    + new Vector3((square.size - 1) * CellStep / 2f, (square.size - 1) * CellStep / 2f, 0f);
                float toSize = square.size * CellStep - cellGap;
                var target = new SquareGrowTarget { toPos = toPos, toSize = toSize };

                // 등록에 실패하면(호스트 뷰가 없는 예외 상황) 화면은 합쳐지지 않은 것이므로
                // 회전 목표에도, 라벨에도 넣지 않는다. 예전엔 실패해도 라벨이 떠서
                // "2사이즈"만 뜨고 블록은 그대로인 그림이 나올 수 있었다.
                if (!RegisterSquareMerge(square, target))
                    continue;

                if (growTargets != null)
                    growTargets[(square.originX, square.originY)] = target;

                // 여기까지 온 건 "이번에 새로 생긴" 정사각형뿐이다(같은 자리·같은 크기로 이미
                // 있던 건 바로 위에서 걸러졌다). 라벨을 블록 안에 넣으므로 한가운데와 크기를 넘긴다.
                OnStandUpSquareFormed?.Invoke(square.size, toPos, toSize);
            }

            // 고정된 조각은 이 순간부터 말랑하게 숨쉬기 시작한다(종료 연출에서 불꽃이 되어 날아갈
            // 때까지 계속). 합체가 생기거나 풀리면 화면에 보이는 대표 조각이 바뀌므로, 정사각형을
            // 다시 계산한 지금 이 자리에서 대상도 함께 다시 세운다.
            RefreshStandUpPulseTargets(heldCellBuffer, absorbedMembers);

            return absorbedMembers;
        }

        // RebuildStandUpSquareMerges 전용 재사용 버퍼. 이 함수는 <b>중간에 yield 하지 않는</b>
        // 완전 동기 함수라, 여러 매치가 동시에 처리돼도 호출이 서로 겹쳐 들어올 수 없다.
        // (호출부는 반환된 absorbedMembers 를 첫 yield 전에 다 쓰고 버린다 - 프레임 너머로
        //  들고 있으면 안 된다.)
        private readonly List<SquareMergeFinder.SquareBlock> currentSquareBuffer =
            new List<SquareMergeFinder.SquareBlock>();
        private readonly List<SquareMergeFinder.SquareBlock> desiredSquareBuffer =
            new List<SquareMergeFinder.SquareBlock>();
        private readonly List<(int x, int y)> heldCellBuffer = new List<(int x, int y)>();
        private readonly HashSet<(int x, int y)> absorbedMemberBuffer = new HashSet<(int x, int y)>();

        /// <summary>
        /// 매치가 아니라 보드 상태만 바뀐 뒤(박스 십자변환으로 합체가 깨지거나 고정이 풀린 뒤)
        /// 정사각형 합체를 다시 맞춘다.
        ///
        /// 이게 없으면 BreakStandSquareMergesOverlapping이 깨뜨린 합체가 그대로 방치된다.
        /// 예를 들어 3x3 합체의 한 칸만 박스로 덮어써져도 9칸이 통째로 풀리는데, 남은 8칸으로
        /// 여전히 2x2가 성립하는데도 <b>다음 매치가 일어날 때까지 낱개인 채로 남아 있었다.</b>
        /// 화면만의 문제가 아니라, 데미지는 보드 데이터에서 다시 구하므로 그 사이 화면과 데미지가
        /// 어긋나 있기까지 했다(이 프로젝트가 지키기로 한 불변식 위반).
        /// </summary>
        public void RefreshStandUpSquareMerges()
        {
            RebuildStandUpSquareMerges(null);
        }

        /// <summary>
        /// 조각 하나의 제자리 Y축 360도 회전.
        /// </summary>
        private IEnumerator AnimateStandUpLockSpin(int x, int y)
        {
            var view = viewGrid[x, y];
            if (view == null)
                yield break;

            float t = 0f;
            while (t < standUpLockSpinDuration)
            {
                t += Time.deltaTime;
                float angle = Mathf.Lerp(0f, 360f, Mathf.Clamp01(t / standUpLockSpinDuration));
                view.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                yield return null;
            }

            view.transform.rotation = Quaternion.identity;
        }

        /// <summary>
        /// 매치 그룹의 뷰들을 viewGrid 추적에서 즉시 떼어낸다(DetachView를 여러 칸에 한 번에 적용한 것과
        /// 같음). 호출 직후 그 칸들은 viewGrid상 비어있는 것으로 취급되므로, 호출부가 곧바로
        /// BoardManager.ResolveGroup(데이터 커밋)과 뷰 정리(반납/박스 전환)를 진행해도 안전하다.
        /// 반환된 뷰들은 AnimateDetachedCollectEffect에 넘겨서 접기 애니메이션을 재생시키는 데 쓴다 -
        /// 그 뒤로는 이 뷰들의 생사를 그 코루틴이 전담하며(다 접히면 직접 풀에 반납), viewGrid/풀
        /// 어느 쪽과도 더 이상 연결돼 있지 않으므로 다른 시스템이 실수로 건드릴 일도 없다.
        /// </summary>
        public Dictionary<(int x, int y), PanelView> DetachGroupForCollectEffect(List<(int x, int y)> cells)
        {
            // 개수를 미리 알려주면 Dictionary가 중간에 다시 커지지 않는다(매치마다 도는 자리).
            var detached = new Dictionary<(int x, int y), PanelView>(cells.Count);
            foreach (var (x, y) in cells)
            {
                var view = viewGrid[x, y];
                if (view == null)
                    continue;

                detached[(x, y)] = view;
                viewGrid[x, y] = null;
            }
            return detached;
        }

        /// <summary>
        /// DetachGroupForCollectEffect로 떼어낸 뷰들을 anchor(놓아진 자리) 쪽으로 접어 모으는 이펙트.
        /// 데이터/뷰 정리는 이미 detach 시점에 끝나 있으므로(호출부가 그 직후 pivot을 제외한 칸의
        /// lockedCells를 풀어서, 이 연출이 재생되는 동안에도 그 자리에 다른 조각을 옮기거나 박스로
        /// 새로 만들 수 있음), 이 코루틴은 순수하게 접히는 비주얼만 담당한다.
        /// 다만 낙하(중력)는 이 연출이 완전히 끝난 뒤에 시작돼야 여러 조각이 동시에 쏟아지는 것처럼
        /// 보이지 않으므로, 호출부는 이 코루틴이 끝날 때까지는 계속 기다려야 한다.
        /// 순서: anchor에서 가장 먼 조각부터 처리. 각 조각은 자신의 상하좌우 인접 칸에 아직 처리되지
        /// 않은 다른 매치 조각(또는 anchor)이 있으면 그쪽으로 한 칸 접혀 이동하고, 인접한 칸에 그런
        /// 조각이 전혀 없으면(주변에 남은 게 없음) anchor로 직행한다. 다 접힌 조각은 (더 이상 viewGrid나
        /// 풀 어디에도 연결돼 있지 않으므로) 여기서 직접 풀에 반납한다.
        /// onPieceConsumed: 조각 하나가 실제로 화면에서 사라지는 그 순간마다 호출됨(게이지를
        /// 애니메이션에 맞춰 한 칸씩 올리는 용도). anchorBecomesBox가 true면 anchor 자신이
        /// 사라질 때는 호출 안 함(박스로 바뀌는 거지 "제거"되는 게 아니므로).
        /// </summary>
        /// <summary>
        /// 조각 하나를 경로를 따라 접어 보내고, 도착하면 풀에 반납한다.
        /// </summary>
        /// <param name="hopDuration">
        /// 한 칸 접는 데 걸리는 시간. 조각이 많은 매치일수록 짧게 넘어와서, 연출 전체에 걸리는
        /// 시간이 조금씩 짧아진다 - 앞 절반은 평소 속도, 뒤 절반만 빨라진다
        /// (collectSpeedsUpAfterHalf).
        /// </param>
        private IEnumerator FoldPieceAlongPath(PanelView view, int fromX, int fromY,
            List<(int x, int y)> path, float hopDuration, System.Action onPieceConsumed)
        {
            view.StopPulsing(); // 내 차례 - 반짝임을 그 즉시 확실히 멈추고 실제로 접혀서 이동 시작

            Vector3 currentPos = GridToWorld(fromX, fromY);
            foreach (var step in path)
            {
                Vector3 stepPos = GridToWorld(step.x, step.y);
                yield return StartCoroutine(AnimateFoldHop(view, currentPos, stepPos, hopDuration));
                currentPos = stepPos;
            }

            pool.Release(view); // detach된 뷰라 여기서 직접 풀에 반납해야 함(SetActive만으론 안 됨)
            onPieceConsumed?.Invoke(); // 이 조각이 방금 사라짐 - 게이지를 한 칸 올릴 시점
        }

        /// <param name="speedMultiplier">
        /// 접히는 속도 배율. 2면 두 배 빠르다. 미스틱의 특수 퍼즐이 자리를 옮길 때 쓴다
        /// (2026-08-30 사용자 지시: "평소보다 2배로 빠른 속도로").
        /// </param>
        public IEnumerator AnimateDetachedCollectEffect(Dictionary<(int x, int y), PanelView> detachedViews,
            int anchorX, int anchorY, bool anchorBecomesBox = false, System.Action onPieceConsumed = null,
            float speedMultiplier = 1f)
        {
            var cancellation = new CollectEffectCancellation();
            activeCollectEffects.Add(cancellation);

            // 접기 소리는 조각이 접히는 박자가 아니라 <b>연출이 도는 동안</b> 일정한 간격으로
            // 반복된다. 여기서 켜고 접기가 끝나면 끈다(SfxPlayer.BeginCollectLoop 주석 참고).
            bool collectLoopOn = false;

            try
            {
                var anchorCell = (anchorX, anchorY);

                // 아직 완전히 처리(접혀서 반납)되지 않은 조각들 - 취소되면 여기 남아있는 것들을
                // 애니메이션 없이 한꺼번에 즉시 정리한다.
                var pendingChain = new List<(int x, int y)>();
                foreach (var cell in detachedViews.Keys)
                {
                    if (cell != anchorCell)
                        pendingChain.Add(cell);
                }

                // anchor에서 먼 순서(내림차순)로 정렬 - 처리 순서로만 사용 (목표는 아래에서 인접 여부로 별도 판단)
                pendingChain.Sort((a, b) => SqDistTo(b, anchorX, anchorY).CompareTo(SqDistTo(a, anchorX, anchorY)));

                detachedViews.TryGetValue(anchorCell, out var anchorView);

                // 아직 화면에 남아있는(처리 안 된) 매치 조각들의 좌표 집합 - 인접 판정에 사용
                var stillPresent = new HashSet<(int x, int y)>(pendingChain) { anchorCell };

                // 아직 처리 안 된 조각들(anchor 포함) 전부 반짝임 시작. 각자 자기 코루틴을 스스로 소유하므로
                // 자기 차례가 되면 StopPulsing()으로 그 즉시(같은 프레임) 확실히 멈춤.
                foreach (var cell in pendingChain)
                    detachedViews[cell].StartPulsing(pulsePeriod, pulseBrightness);
                anchorView?.StartPulsing(pulsePeriod, pulseBrightness);

                // 조각이 많아도 <b>연출 전체에 걸리는 시간은 그대로</b> 두고, 대신 한 칸 넘어가는
                // 속도를 그만큼 올린다. 기준은 매치가 성립하는 최소 개수(4개)짜리 매치 -
                // 그때가 지금 보는 속도 그대로이고, 조각이 늘어날수록 템포만 빨라진다.
                //
                // anchor 는 접혀 넘어가지 않고 마지막에 제자리에서 축소되므로, 실제로 접히는 수는
                // (전체 - 1)이다. 기준도 같은 방식으로 세야(4개 매치 = 3번 접힘) 총 시간이 맞는다.
                //
                // 예전에는 "7개 이상이면 2배속" 같은 문턱을 뒀는데, 문턱 앞뒤로 체감이 뚝 끊기고
                // 그 위로는 조각이 늘어날수록 다시 느려졌다. 그 전에는 조각을 2개씩 동시에 보냈는데,
                // 두 조각이 서로의 인접 칸을 목표로 삼는 걸 막느라 출발 전에 둘 다 stillPresent 에서
                // 빼야 했고, 그러다 보니 서로 반대 방향으로 접히거나 한 조각이 남은 이웃을 못 찾아
                // anchor 로 직행하는 그림이 섞여서 접히는 방향이 가끔 이상하게 보였다.
                // 하나씩 보내면 "옆 조각으로 차례차례 접혀 들어간다"는 규칙이 항상 눈에 보인다.
                int totalFolds = pendingChain.Count;                                // 실제로 접히는 조각 수
                int referenceFoldCount = Mathf.Max(1, ConnectionFinder.MinRemoveCount - 1);

                // 앞 절반은 평소 속도, 뒤 절반만 빨리 감는다.
                int normalSpeedFolds = collectSpeedsUpAfterHalf ? totalFolds / 2 : totalFolds;

                float speed = Mathf.Max(0.05f, speedMultiplier);
                float collectHopDuration = this.collectHopDuration / speed;

                float fastHopDuration = collectHopDuration;
                if (collectSpeedsUpAfterHalf && totalFolds > referenceFoldCount)
                {
                    // 기준보다 적은 매치는 존재하지 않지만(4개 미만은 매치가 아님), 혹시 들어와도
                    // 느려지지는 않게 빨라지는 쪽으로만 적용한다.
                    fastHopDuration = collectHopDuration * referenceFoldCount / totalFolds;
                }

                int foldIndex = 0;

                if (pendingChain.Count > 0 && sfx != null)
                {
                    sfx.BeginCollectLoop();
                    collectLoopOn = true;
                }

                while (pendingChain.Count > 0 && !cancellation.cancelRequested)
                {
                    // 1) 이번에 넘어갈 조각을 뽑고 stillPresent 에서 먼저 뺀다 - 자기 자신을
                    //    목표로 삼지 않게.
                    var (x, y) = pendingChain[0];
                    pendingChain.RemoveAt(0);
                    stillPresent.Remove((x, y));

                    // 2) 상하좌우 중 아직 남아있는 매치 조각(또는 anchor)이 있는지 확인,
                    //    있으면 그중 anchor에 가장 가까운 걸 목표로
                    (int x, int y)? nearbyTarget = null;
                    int nearbyBestDist = int.MaxValue;
                    foreach (var (dx, dy) in OrthogonalNeighbors)
                    {
                        var n = (x: x + dx, y: y + dy);
                        if (!stillPresent.Contains(n))
                            continue;

                        int d = SqDistTo(n, anchorX, anchorY);
                        if (nearbyTarget == null || d < nearbyBestDist)
                        {
                            nearbyTarget = n;
                            nearbyBestDist = d;
                        }
                    }

                    // 인접한 곳에 남은 매치 조각이 없으면(anchor조차 인접하지 않으면) anchor로 직행
                    (int x, int y) targetCell = nearbyTarget ?? anchorCell;
                    var path = BuildAdjacentStepPath(x, y, targetCell.x, targetCell.y);

                    // 3) 이 조각이 도착해야 다음 조각을 보낸다 - 안 그러면 몇 개가 넘어갔는지
                    //    눈으로 세어지지 않는다(스탠드업 불꽃을 한 박자씩 쉬게 한 것과 같은 이유).
                    float hopDuration = foldIndex < normalSpeedFolds ? collectHopDuration : fastHopDuration;
                    foldIndex++;

                    yield return StartCoroutine(
                        FoldPieceAlongPath(detachedViews[(x, y)], x, y, path, hopDuration, onPieceConsumed));
                }

                // 접기가 끝났으니 반복 소리도 여기서 멈춘다 - '끝' 소리와 겹치지 않게
                // 아래 분기들보다 먼저 끈다.
                if (collectLoopOn)
                {
                    sfx.EndCollectLoop();
                    collectLoopOn = false;
                }

                if (cancellation.cancelRequested)
                {
                    // 스킬 컷인/스탠드업 타임 진입·종료 등 더 강조해야 할 다른 이펙트가 발생해서 취소됨.
                    // 아직 안 접힌 나머지 조각과 anchor는 접기를 그만두고 그 자리에서 제거 연출과 함께 사라진다 -
                    // 접던 걸 중간에 뚝 끊고 즉시 비활성화하면 조각이 허공에서 증발한 것처럼 보였다.
                    // 반납과 게이지 처리는 RemoveDetachedViews가 접기 연출과 똑같은 규칙으로 해준다.
                    var remaining = new Dictionary<(int x, int y), PanelView>();

                    foreach (var (x, y) in pendingChain)
                    {
                        var view = detachedViews[(x, y)];
                        view.StopPulsing();
                        remaining[(x, y)] = view;
                    }

                    if (anchorView != null)
                    {
                        anchorView.StopPulsing();
                        remaining[anchorCell] = anchorView;
                    }

                    yield return StartCoroutine(
                        RemoveDetachedViews(remaining, anchorX, anchorY, anchorBecomesBox, onPieceConsumed));
                }
                else if (anchorView != null)
                {
                    // 정상 완료 - 다 모인 자리에서 마무리 연출이 터진다.
                    anchorView.StopPulsing();
                    sfx?.PlayCollectFinish();

                    // <b>기다리지 않는다.</b> 예전엔 anchor 가 0.2초 동안 작아지는 걸 여기서
                    // 끝까지 기다렸는데, 그만큼 낙하·리필이 통째로 늦어졌다. 지금은 조각을 곧바로
                    // 치우고 연출만 따로 굴러가므로 판은 바로 다음 단계로 넘어간다.
                    matchFinishEffect?.Play(GridToWorld(anchorX, anchorY), CellStep);

                    pool.Release(anchorView); // 박스가 됐어도 이건 "예전" 조각 뷰라 반납 대상(박스 뷰는 별개로 새로 스폰됨)
                    if (!anchorBecomesBox)
                        onPieceConsumed?.Invoke(); // anchor도 박스가 아니라 그냥 제거되는 거면 게이지 대상에 포함
                }
            }
            finally
            {
                // 중간에 코루틴이 끊겨도 반복 소리가 영영 돌지 않도록 반드시 짝을 맞춘다.
                if (collectLoopOn)
                    sfx.EndCollectLoop();

                activeCollectEffects.Remove(cancellation);
            }
        }

        private class CollectEffectCancellation
        {
            public bool cancelRequested;
        }

        private readonly List<CollectEffectCancellation> activeCollectEffects = new List<CollectEffectCancellation>();

        /// <summary>
        /// 지금 재생 중인 모든 접기(수집) 연출을 즉시 취소한다. 캐릭터 스킬 컷인, 스탠드업 타임
        /// 진입, 적 공격 등 더 강조해야 할 다른 이펙트가 발생했을 때 호출하는 용도 - 진행 중이던
        /// 접기 애니메이션은 즉시 멈추고, 아직 안 사라진 조각들은 애니메이션 없이 곧바로 비활성화된다.
        /// </summary>
        public void CancelAllCollectEffects()
        {
            foreach (var token in activeCollectEffects)
                token.cancelRequested = true;
        }

        /// <summary>
        /// (fromX,fromY)에서 (toX,toY)까지, 상하좌우로만 이동하는 한 칸씩의 경로를 만든다(대각선 없음).
        /// 가로(x) 방향 차이부터 확인해서 있으면 그 방향으로 먼저 전부 이동하고,
        /// 가로가 이미 맞아 더 이상 없으면 그때 세로(y) 방향으로 이동한다.
        /// </summary>
        private List<(int x, int y)> BuildAdjacentStepPath(int fromX, int fromY, int toX, int toY)
        {
            var path = new List<(int x, int y)>();
            int x = fromX, y = fromY;

            // 1순위: 가로열 확인 - x가 다르면 목표 x에 맞을 때까지 가로로 이동
            while (x != toX)
            {
                x += toX > x ? 1 : -1;
                path.Add((x, y));
            }

            // 가로가 다 맞았는데 세로가 남았으면 그때 세로행으로 이동
            while (y != toY)
            {
                y += toY > y ? 1 : -1;
                path.Add((x, y));
            }

            return path;
        }

        private static int SqDistTo((int x, int y) cell, int targetX, int targetY)
        {
            int dx = cell.x - targetX;
            int dy = cell.y - targetY;
            return dx * dx + dy * dy;
        }

        [SerializeField] private float collectHopArcHeight = 0.18f; // 회전 중간에 살짝 떠오르는 높이(동전 튕기는 느낌)

        /// <summary>
        /// 인접한 두 칸(fromPos → toPos, 항상 상하좌우 중 하나) 사이를 "책장 넘기듯" 접으며 한 칸 이동.
        /// 힌지 오브젝트를 매번 만들고 파괴하는 대신, 회전축 위의 점(mid)을 기준으로 매 프레임
        /// 위치를 직접 수학으로 계산한다: position = mid + rotate(startOffset, axis, angle).
        /// 여기에 회전 중간(90도 부근)에서 살짝 떠오르는 호(arc)를 얹어서, 평면 회전이 아니라
        /// 동전을 튕기듯 살짝 들렸다가 내려앉는 3D 느낌을 살림.
        /// 오브젝트 생성/파괴나 SetParent 없이 순수 계산이라 정확하고 가볍다.
        /// 항상 축 정렬된 인접 이동이므로 대각선 보정도 필요 없음.
        /// </summary>
        [Header("스탠드업 타임 - 매치된 조각을 Y축으로 한 바퀴 돌리고 그 자리에 고정")]
        [SerializeField] private float standHoldSpinDuration = 0.4f;

        /// <summary>
        /// 스탠드업 타임 중 매치가 성립했을 때 재생하는 이펙트. 평소의 접기/소멸 대신,
        /// 매치된 조각들을 각자 자기 자리에서 Y축으로 한 바퀴(360도) 동시에 회전시킨다.
        /// 이동도 소멸도 없음 - 회전이 끝나면 원래 자리에 원래 모습 그대로 남아있고,
        /// 데이터상으로만(BoardManager.HoldGroupAsStandHeld) 고정 상태로 바뀜.
        /// </summary>
        public IEnumerator AnimateStandHoldSpin(IEnumerable<(int x, int y)> cells)
        {
            var views = new List<PanelView>();
            foreach (var (x, y) in cells)
            {
                var v = viewGrid[x, y];
                if (v != null)
                    views.Add(v);
            }

            if (views.Count == 0)
                yield break;

            var running = new List<Coroutine>();
            foreach (var v in views)
                running.Add(StartCoroutine(SpinOnceAroundY(v)));

            foreach (var routine in running)
                yield return routine;
        }

        private IEnumerator SpinOnceAroundY(PanelView view)
        {
            float t = 0f;
            while (t < standHoldSpinDuration)
            {
                t += Time.deltaTime;
                float angle = 360f * Mathf.Clamp01(t / standHoldSpinDuration);
                view.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                yield return null;
            }
            view.transform.rotation = Quaternion.identity; // 360도 = 0도와 같은 모습이므로 깔끔하게 초기화
        }

        /// <summary>
        /// 인접한 두 칸(fromPos → toPos, 항상 상하좌우 중 하나) 사이를 "책장 넘기듯" 접으며 한 칸 이동.
        /// 힌지 오브젝트를 매번 만들고 파괴하는 대신, 회전축 위의 점(mid)을 기준으로 매 프레임
        /// 위치를 직접 수학으로 계산한다: position = mid + rotate(startOffset, axis, angle).
        /// 여기에 회전 중간(90도 부근)에서 살짝 떠오르는 호(arc)를 얹어서, 평면 회전이 아니라
        /// 동전을 튕기듯 살짝 들렸다가 내려앉는 3D 느낌을 살림.
        /// 오브젝트 생성/파괴나 SetParent 없이 순수 계산이라 정확하고 가볍다.
        /// 항상 축 정렬된 인접 이동이므로 대각선 보정도 필요 없음.
        /// </summary>
        /// <param name="overrideDuration">
        /// 0보다 크면 이 시간(초)으로 움직인다. 매치 접기와 박스 펼치기가 같은 동작을 쓰지만
        /// 원하는 속도가 달라서(박스는 더 천천히 펼쳐지는 게 잘 보임) 따로 지정할 수 있게 열어둠.
        /// </param>
        private IEnumerator AnimateFoldHop(PanelView view, Vector3 fromPos, Vector3 toPos, float overrideDuration = -1f)
        {
            float hopDuration = overrideDuration > 0f ? overrideDuration : collectHopDuration;

            Vector3 delta = toPos - fromPos;
            bool vertical = Mathf.Abs(delta.y) >= Mathf.Abs(delta.x);
            Vector3 axis = vertical ? Vector3.right : Vector3.up; // 위/아래 이동=X축, 좌/우 이동=Y축

            Vector3 mid = (fromPos + toPos) * 0.5f;
            Vector3 startOffset = fromPos - mid; // 회전축 기준 시작 상대 위치

            float t = 0f;
            while (t < hopDuration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / hopDuration);
                Quaternion rot = Quaternion.AngleAxis(180f * p, axis);

                Vector3 pos = mid + rot * startOffset; // 힌지 축 둘레를 도는 위치
                float arc = Mathf.Sin(p * Mathf.PI) * collectHopArcHeight; // 0→최고점(중간)→0, 살짝 떠오름
                pos.y += arc;

                view.transform.position = pos;
                view.transform.rotation = rot; // 조각 자체도 같이 회전(페이지가 넘어가는 모습)

                yield return null;
            }

            view.MoveTo(toPos);
            view.transform.rotation = Quaternion.identity;
        }

        /// <summary>
        /// 매치된 조각들을 접어 모으지 않고 <b>그 자리에서 다 같이 없앤다</b> - 조각마다
        /// 제거 연출(MatchFinishEffect)이 터지고 조각은 그 즉시 사라진다.
        ///
        /// 접기 연출이 <b>중간에 취소됐을 때</b>의 마무리 경로다(CancelAllCollectEffects). 스탠드업
        /// 배너가 뜨거나 퍼즐판이 가려지는 순간처럼 더 강조할 게 생기면 접던 걸 그만둬야 한다.
        ///
        /// 예전에는 조각이 살짝 부풀었다 줄어드는 "뿅" 연출을 0.16초 재생했다. 조각을 그냥
        /// 비활성화하면 허공에서 증발한 것처럼 보여서 넣었던 것인데, 이제 제거 연출이 그 자리를
        /// 대신하므로 없앴다(2026-08-22 사용자 지시) - 터지는 연출이 사라지는 순간을 덮어 준다.
        /// 덤으로 기다리는 시간도 사라져서 판이 그만큼 빨리 다음 단계로 넘어간다.
        ///
        /// 게이지(onPieceConsumed)와 박스 생성 규칙은 접기 연출과 완전히 같다 - 연출만 다르고
        /// 게임 상의 결과는 달라지지 않아야 하기 때문.
        /// </summary>
        public IEnumerator RemoveDetachedViews(Dictionary<(int x, int y), PanelView> detachedViews,
            int anchorX, int anchorY, bool anchorBecomesBox = false, System.Action onPieceConsumed = null)
        {
            if (detachedViews == null || detachedViews.Count == 0)
                yield break;

            foreach (var pair in detachedViews)
            {
                var view = pair.Value;
                view.SetScaleMultiplier(1f, 1f); // 접다 만 크기가 남은 채로 풀에 들어가지 않도록

                // <b>사라지는 조각마다</b> 제거 연출을 터뜨린다.
                // 세기를 낮춰 부르는 이유: 한 번에 여러 칸이 사라지는데 칸마다 전부를 터뜨리면
                // 화면이 뿌옇게 뒤덮이고 풀도 금방 바닥난다.
                matchFinishEffect?.Play(GridToWorld(pair.Key.x, pair.Key.y), CellStep, popOutEffectIntensity);

                pool.Release(view);

                // anchor가 박스로 바뀌는 경우만 "제거"가 아니므로 게이지에서 뺀다(접기 연출과 동일).
                bool isAnchor = pair.Key == (anchorX, anchorY);
                if (!(isAnchor && anchorBecomesBox))
                    onPieceConsumed?.Invoke();
            }
        }

        [Header("버닝 트랙")]
        [Tooltip("유나의 점화 블록에서 먹인 조각이 <b>한 칸 올라가는 데</b> 걸리는 시간(초). " +
                 "태우는 게 눈에 보여야 하는 연출이라 접기(Collect Hop Duration)보다 조금 느리다.")]
        [SerializeField] private float burnRiseStepDuration = 0.13f;

        [Tooltip("한 칸 올라갈 때 옆으로 흔들리는 폭(칸 크기 대비). 곧장 올라가면 그냥 미끄러지는 " +
                 "것처럼 보여서, 좌우로 번갈아 살짝 통통 튀며 올라가게 한다. 0이면 곧게 오른다.")]
        [SerializeField] private float burnRiseWobble = 0.22f;

        [Tooltip("올라가는 동안 세로로 늘어나는 정도. 0.25면 뛰어오를 때 세로 1.25배까지 늘어난다 " +
                 "(가로는 그만큼 줄어들어 부피가 유지된다).")]
        [SerializeField] private float burnRiseStretch = 0.25f;

        /// <summary>
        /// 유나의 <b>버닝 트랙!</b> 상승 연출(2026-09-01 사용자 기획).
        /// 먹인 조각이 점화 블록 자리에서 출발해 <b>맨 위까지 한 칸씩 뛰어오르며</b>,
        /// 닿는 자리를 뿅 없앤다. 끝에 닿으면 그 조각 자신도 같이 사라진다.
        ///
        /// <b>어느 칸을 태울지는 여기서 정하지 않는다</b> - 그건 규칙이라 부르는 쪽이 정한다.
        /// <paramref name="tryBurnRow"/> 가 true 를 돌려준 줄에서만 조각을 없앤다
        /// (구멍이나 다른 처리가 쥐고 있는 칸은 그냥 지나간다).
        /// </summary>
        public IEnumerator AnimateBurnRise(PanelView riser, int columnX, int fromY,
            System.Func<int, bool> tryBurnRow)
        {
            if (riser == null)
                yield break;

            // 낙하·리필 연출이 이 뷰를 다시 끌고 가지 못하게 소유권을 가져온다.
            riser.TakeLayoutOwnership();
            riser.SetHeldOnTop(true);

            Vector3 from = riser.transform.position;
            int height = boardManager.Board.height;

            for (int y = fromY; y < height; y++)
            {
                Vector3 to = GridToWorld(columnX, y);

                // 줄마다 반대쪽으로 튀게 한다 - 같은 쪽으로만 휘면 비틀어진 것처럼 보인다.
                float side = ((y - fromY) % 2 == 0) ? 1f : -1f;
                yield return AnimateBurnHop(riser, from, to, side);
                from = to;

                if (tryBurnRow != null && tryBurnRow(y))
                    PopViewAt(columnX, y);
            }

            // 맨 끝에 도달했으니 태운 조각도 같이 사라진다(사용자 확정).
            riser.SetScaleMultiplier(1f, 1f);
            riser.SetHeldOnTop(false);
            matchFinishEffect?.Play(from, CellStep);
            pool.Release(riser);
        }

        /// <summary>그 칸의 뷰를 뿅 없앤다. 보드 데이터는 건드리지 않는다(부르는 쪽의 몫).</summary>
        private void PopViewAt(int x, int y)
        {
            var view = viewGrid[x, y];
            if (view == null)
                return;

            viewGrid[x, y] = null;
            view.SetScaleMultiplier(1f, 1f);   // 접다 만 크기가 남은 채로 풀에 들어가지 않도록
            matchFinishEffect?.Play(GridToWorld(x, y), CellStep);
            pool.Release(view);
        }

        /// <summary>한 칸만큼 뛰어오른다 - 옆으로 살짝 튀고 세로로 늘어난다.</summary>
        private IEnumerator AnimateBurnHop(PanelView view, Vector3 from, Vector3 to, float side)
        {
            float duration = Mathf.Max(0.01f, burnRiseStepDuration);
            float wobble = CellStep * burnRiseWobble * side;

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float k = Mathf.Clamp01(elapsed / duration);
                float arc = Mathf.Sin(k * Mathf.PI);     // 가운데에서 가장 큼

                Vector3 p = Vector3.Lerp(from, to, k);
                p.x += wobble * arc;
                view.MoveTo(p);

                float stretch = 1f + burnRiseStretch * arc;
                view.SetScaleMultiplier(stretch > 0f ? 1f / stretch : 1f, stretch);

                yield return null;
            }

            view.MoveTo(to);
            view.SetScaleMultiplier(1f, 1f);
        }

        [Tooltip("박스가 십자로 펼쳐질 때 조각 하나가 튀어나가는 시간(초). 매치 접기(Collect Hop Duration)와 " +
                 "같은 동작이지만 박스는 조금 느려야 펼쳐지는 게 잘 보여서 따로 둔다.")]
        [SerializeField] private float boxUnfoldHopDuration = 0.2f;

        /// <summary>
        /// 박스가 십자 5칸으로 "펼쳐지는" 이펙트.
        /// 1) 박스(큐브)가 즉시 사라지고 중심 칸에 조각이 곧바로 제 크기로 나타남(커지는 연출 없음)
        /// 2) 나머지 4방향 조각들이 전부 동시에 중심에서 튀어나가듯(AnimateFoldHop) 각자 자리로 이동
        ///
        /// 예전엔 여기서 1초를 더 기다려 "플레이어가 박스 사용을 인지할 시간"을 벌었는데, 그 1초
        /// 동안 보드 전체가 멈춰버렸다. 지금은 그 역할을 호출부가 거는 <b>미안착 시간</b>
        /// (BoardManager.MarkUnsettled)이 대신한다 - 펼쳐진 5칸만 매치 대상에서 잠시 빠지고
        /// 나머지 판은 계속 굴러간다.
        /// </summary>
        public IEnumerator AnimateBoxUnfold(int centerX, int centerY, List<(int x, int y)> convertedCells)
        {
            Vector3 centerPos = GridToWorld(centerX, centerY);

            // 1) 박스(큐브) 비주얼을 즉시 제거하고, 중심 칸에 변환된 색의 조각을 곧바로 제 크기로 스폰
            DestroyViewAt(centerX, centerY);

            var centerCell = boardManager.Board.Get(centerX, centerY);
            var centerView = SpawnView(centerX, centerY, centerCell);
            viewGrid[centerX, centerY] = centerView;

            // 2) 나머지 4방향 조각들을 전부 "동시에" 중심에서 튀어나가듯 이동
            var running = new List<Coroutine>();
            foreach (var (x, y) in convertedCells)
            {
                if (x == centerX && y == centerY)
                    continue;

                DestroyViewAt(x, y); // 그 자리에 원래 있던(덮어써질) 뷰는 반납

                var cell = boardManager.Board.Get(x, y);
                var view = SpawnView(x, y, cell, centerPos); // 중심 위치에서 스폰 시작
                viewGrid[x, y] = view;

                Vector3 targetPos = GridToWorld(x, y);
                running.Add(StartCoroutine(AnimateFoldHop(view, centerPos, targetPos, boxUnfoldHopDuration)));
            }

            foreach (var routine in running)
                yield return routine;
        }

        [Header("애니메이션")]
        [SerializeField] private float fallDuration = 0.25f; // 한 칸 낙하가 아니라 "이번 이동 전체"에 걸리는 시간

        /// <summary>
        /// 낙하·리필 속도 배율. 1이 평소이고 크면 그만큼 빨라진다.
        /// <b>러시 타임이 이걸 올린다</b>(BoardInputController.SetRushTime).
        /// 인스펙터 값(fallDuration)은 그대로 두고 나누기만 하므로, 러시가 끝나고 1로 되돌리면
        /// 사용자가 튜닝한 속도가 정확히 복원된다.
        /// </summary>
        public float FallSpeedMultiplier { get; set; } = 1f;
        [SerializeField] private float spawnAboveOffset = 1f; // 리필된 패널이 보드 위 몇 유닛 지점에서부터 떨어지기 시작할지

        /// <summary>
        /// 낙하/리필 애니메이션 대상 하나(어떤 뷰가 어디서 어디로). 조각마다 코루틴을 띄우는 대신
        /// 이 목록을 코루틴 하나가 순회하면서 다 같이 움직인다.
        /// </summary>
        private struct FallAnimation
        {
            public PanelView view;
            public Vector3 start;

            /// <summary>
            /// 연출을 시작할 때의 PanelView.Serial. 도중에 그 뷰가 풀로 반납됐다가 다른 조각으로
            /// 다시 쓰이면 값이 달라지므로, 그때부터는 이 연출이 손대지 않는다.
            /// 좌표만 비교하면(StillOwnsView) 재사용된 뷰를 같은 조각으로 착각한다.
            /// </summary>
            public int serial;
        }

        // 서로 다른 매치가 동시에 각자의 낙하를 돌릴 수 있으므로 목록은 진행 중인 낙하마다 하나씩
        // 필요하다. 매번 새로 만들지 않도록 작은 풀에서 빌려 쓰고, 애니메이션이 끝나면 돌려준다.
        private readonly Stack<List<FallAnimation>> fallListPool = new Stack<List<FallAnimation>>();

        private List<FallAnimation> RentFallList()
            => fallListPool.Count > 0 ? fallListPool.Pop() : new List<FallAnimation>();

        /// <summary>
        /// BoardManager.ApplyGravity()가 반환한 이동 목록을 애니메이션으로 반영.
        /// 모든 낙하가 동시에 진행되고, 전부 끝날 때까지 대기.
        /// </summary>
        public IEnumerator AnimateGravityMoves(List<FallMove> moves)
        {
            if (moves.Count == 0)
                yield break;

            var animations = RentFallList();

            foreach (var move in moves)
            {
                var view = viewGrid[move.x, move.fromY];
                viewGrid[move.x, move.fromY] = null;

                if (view == null)
                    continue; // 안전장치: 뷰가 없으면(이미 파괴된 경우 등) 스킵

                ReleaseIfOccupied(move.x, move.toY); // 안전장치: 목적지에 이미 뷰가 남아있으면 먼저 반납
                view.SetGridPosition(move.x, move.toY);
                viewGrid[move.x, move.toY] = view;

                animations.Add(new FallAnimation
                {
                    view = view,
                    serial = view.Serial,
                    // 시작 위치만 캡처한다. 목표는 캡처하지 않는다 - 낙하가 겹쳐 돌 때 낡은 목표로
                    // 끌고 가는 게 "빈 칸처럼 보이는" 버그의 원인이었다(AnimateFallBatch 참고).
                    start = view.transform.position
                });
            }

            yield return AnimateFallBatch(animations);
        }

        /// <summary>
        /// BoardManager.RefillEmptyCells()가 반환한 신규 스폰 목록을 보드 위에서 떨어지는
        /// 애니메이션으로 반영. 모두 끝날 때까지 대기.
        /// </summary>
        public IEnumerator AnimateRefill(List<(int x, int y, Cell cell)> spawned)
        {
            if (spawned.Count == 0)
                yield break;

            var animations = RentFallList();

            foreach (var (x, y, cell) in spawned)
            {
                Vector3 target = GridToWorld(x, y);
                Vector3 spawnPos = target + new Vector3(0f, spawnAboveOffset, 0f);

                var view = SpawnView(x, y, cell, spawnPos);
                animations.Add(new FallAnimation { view = view, serial = view.Serial, start = spawnPos });
            }

            yield return AnimateFallBatch(animations);
        }

        /// <summary>
        /// 여러 조각의 이동을 코루틴 하나로 함께 처리하고, 다 끝나면 목록을 풀에 돌려준다.
        /// 예전엔 조각마다 코루틴을 띄웠는데, 낙하/리필은 보드 전체가 한꺼번에 움직이는 가장 빈번한
        /// 연출이라 그때마다 이터레이터/Coroutine 객체가 조각 수만큼(최대 보드 칸 수) 할당되고
        /// 매 프레임 그만큼 MoveNext가 불렸다. 어차피 모두 같은 시간(fallDuration) 동안 같은
        /// 진행도로 움직이므로 하나의 루프에서 함께 보간하면 결과가 완전히 같다.
        /// </summary>
        /// <summary>
        /// 낙하/리필 애니메이션을 한 루프가 전부 굴린다.
        ///
        /// <b>목표 지점을 캡처해두지 말 것 - 매 프레임 그 뷰의 현재 칸에서 다시 계산해야 한다.</b>
        /// 2026-08-18에 오래 쫓던 "스탠드업 중 빠르게 조작하면 칸이 빈 칸처럼 보이는" 버그의 원인이
        /// 정확히 이것이었다. 재현과 원인은 이렇다:
        ///
        ///   낙하 A: V를 y=5 → y=3 으로 옮기기로 하고 목표 (x,3)을 캡처, 애니메이션 시작
        ///   낙하 B: (빠른 조작으로 같은 열에 낙하가 하나 더 돈다) 데이터가 또 바뀌어 V는 y=1로.
        ///           viewGrid와 V.GridX/GridY는 (x,1)로 갱신됨
        ///   A가 나중에 끝남 → V를 캡처해둔 (x,3)으로 스냅 → <b>낡은 목표가 이긴다</b>
        ///
        /// 결과: 데이터도 viewGrid도 색도 GridX/GridY도 전부 (x,1)로 정상인데 <b>위치만</b> (x,3)에
        /// 남는다. 그래서 (x,1)은 빈 칸처럼 보이고 (x,3)에는 조각이 겹쳐 보인다. 존재·색·좌표를
        /// 대조하는 어떤 검사로도 안 잡힌다(전부 일치하므로). 그 칸을 탭하면 고쳐지는 것도
        /// 설명된다 - 제자리 탭이 RevertDragToOrigin → PlaceView를 타면서 위치를 다시 잡아준다.
        ///
        /// 지금은 현재 칸에서 목표를 다시 구하므로, 낙하가 몇 개 겹쳐 돌든 어느 게 먼저 끝나든
        /// 결과가 같다. 같은 원칙(위치는 항상 보드 데이터에서)이 정사각형 합체에도 적용돼 있다.
        ///
        /// 재생 중에는 Serial도 함께 본다 - 박스 십자변환이나 합체가 가져간 뷰를 낙하가 매 프레임
        /// 다시 끌어당기면 덜덜 떨리기 때문. 다만 <b>마지막 스냅은 Serial을 보지 않는다</b>:
        /// 거기서 건너뛰면 그 뷰가 아무 데도 정착하지 못하고 붕 뜬 채 남는다(같은 증상).
        /// 합체 호스트만 예외 - 그 뷰는 자기 칸이 아니라 정사각형 한가운데가 제자리다.
        /// </summary>
        private IEnumerator AnimateFallBatch(List<FallAnimation> animations)
        {
            // 움직일 게 하나도 없으면(뷰가 전부 null이라 건너뛴 경우 등) 기다리지 않고 즉시 끝낸다 -
            // 조각마다 코루틴을 띄우던 시절엔 대기할 코루틴이 없어서 자연히 즉시 반환됐던 동작이라,
            // 여기서 막아주지 않으면 아무것도 안 움직이는데 fallDuration만큼 낙하가 지연된다.
            if (animations.Count == 0)
            {
                fallListPool.Push(animations);
                yield break;
            }

            float t = 0f;
            // 이번 낙하가 쓸 시간은 <b>시작할 때 한 번만</b> 정한다. 도중에 배율이 바뀌면
            // 진행률이 튀어서 조각이 순간이동한 것처럼 보인다.
            float duration = fallDuration / Mathf.Max(0.01f, FallSpeedMultiplier);

            while (t < duration)
            {
                t += Time.deltaTime;
                float progress = Mathf.Clamp01(t / duration);

                for (int i = 0; i < animations.Count; i++)
                {
                    var animation = animations[i];
                    if (!StillOwnsView(animation.view, animation.serial))
                        continue; // 플레이어가 집어갔거나 박스가 덮어쓴 조각 - 더 이상 건드리지 않는다

                    // 목표를 캡처해두지 않고 <b>그 뷰의 현재 칸에서 매번 다시 계산</b>한다.
                    // 빠르게 조작하면 같은 열에 낙하가 겹쳐 도는데, 나중 낙하가 이 뷰를 더 아래
                    // 칸으로 재배정해도 먼저 시작한 낙하는 낡은 목표로 끌고 가버린다. 그러면
                    // 데이터·viewGrid·GridX/GridY는 전부 새 칸인데 위치만 옛 칸에 남아,
                    // 그 칸은 빈 칸처럼 보이고 다른 칸에는 조각이 겹쳐 보인다(실제로 확인된 증상).
                    animation.view.MoveTo(Vector3.Lerp(animation.start,
                        GridToWorld(animation.view.GridX, animation.view.GridY), progress));
                }

                yield return null;
            }

            // 마지막 스냅은 <b>Serial을 보지 않는다.</b> 여기서 건너뛰면 그 뷰는 viewGrid와
            // GridX/GridY는 목적지로 갱신됐는데 실제 위치만 출발 지점 근처에 남는 "붕 뜬" 상태가
            // 되고, 그 칸은 데이터·viewGrid·색·좌표가 전부 정상인데 화면에서만 사라진다
            // (다른 칸 자리에 겹쳐 그려짐). 실제로 그 증상으로 확인됐다.
            //
            // 재생 중(위 루프)에는 여전히 Serial을 본다 - 박스 십자변환이나 합체가 가져간 뷰를
            // 낙하가 매 프레임 다시 끌어당기면 덜덜 떨리기 때문. 마지막 한 번만 제자리로 보낸다.
            //
            // 정사각형 합체의 호스트만 예외다. 그 뷰는 여러 칸을 덮는 큰 블록이라 자기 칸 좌표가
            // 아니라 정사각형 한가운데에 있어야 맞다 - 여기서 스냅하면 합체가 다시 어긋난다.
            for (int i = 0; i < animations.Count; i++)
            {
                var view = animations[i].view;
                if (!StillOwnsView(view))
                    continue; // 플레이어가 집어갔거나 다른 칸으로 넘어간 뷰 - 주인이 따로 있다

                if (IsStandSquareHost(view.GridX, view.GridY))
                    continue; // 합체 호스트는 정사각형 한가운데가 제자리

                // 여기서도 캡처한 목표가 아니라 현재 칸으로 보낸다 - 누가 먼저 끝나든 결과가 같아진다.
                view.MoveTo(GridToWorld(view.GridX, view.GridY));
            }

            animations.Clear();
            fallListPool.Push(animations);
        }

        /// <summary>
        /// 이 뷰가 아직 보드 소유인지 - 낙하가 끝나기 전에 플레이어가 그 조각을 집어가면
        /// DetachView가 viewGrid의 자리를 비우므로 여기서 걸러진다. 이걸 안 보면 낙하 루프가
        /// 매 프레임 위치를 덮어써서, 손가락을 따라와야 할 조각이 제자리로 끌려가며 싸운다.
        /// </summary>
        /// <summary>
        /// 좌표뿐 아니라 <b>그 뷰가 아직 같은 조각인지</b>까지 확인한다. 풀이 스택이라 방금 반납한
        /// 뷰가 곧바로 다음 스폰에 다시 나오는데, 좌표만 보면 "여전히 내 것"으로 착각해서 이미
        /// 다른 조각이 된 오브젝트를 옛 연출이 계속 끌어당긴다.
        /// </summary>
        /// <summary>이 칸이 지금 등록된 정사각형 합체의 호스트(= 확대된 대표 조각)인지.</summary>
        private bool IsStandSquareHost(int x, int y)
        {
            foreach (var merge in activeStandMerges)
            {
                if (merge.originX == x && merge.originY == y)
                    return true;
            }
            return false;
        }

        private bool StillOwnsView(PanelView view, int serial)
            => StillOwnsView(view) && view.Serial == serial;

        private bool StillOwnsView(PanelView view)
        {
            if (view == null || viewGrid == null)
                return false;

            int x = view.GridX;
            int y = view.GridY;

            if (!InViewBounds(x, y))
                return false;

            return viewGrid[x, y] == view;
        }

        /// <summary>
        /// 스탠드업 타임 중 정사각형(2x2 이상)으로 이어붙은 같은 색 무더기를 큰 패널 하나로 합쳐 보여줌.
        /// 순수 뷰 레이어 표현일 뿐 - BoardData는 항상 개별 StandHeld 셀 그대로 유지된다.
        /// (나중에 정사각형 크기가 데미지 배율에 연동될 예정이라, size 정보를 BattleManager 쪽에
        /// 넘겨줘야 할 수도 있지만 그 로직은 아직 없어서 지금은 렌더링만 담당함)
        /// </summary>
        private class StandSquareMerge
        {
            public int originX, originY, size;
            public List<(int x, int y)> memberCells;
        }

        private readonly List<StandSquareMerge> activeStandMerges = new List<StandSquareMerge>();

        /// <summary>
        /// 스탠드업 정사각형이 <b>새로</b> 만들어질 때 발행.
        /// 인자 = (한 변의 칸 수, 블록 한가운데의 월드 좌표, 블록 한 변의 월드 크기).
        ///
        /// 월드 크기까지 넘기는 이유: 라벨을 블록 안에 넣으려면 구독자가 블록이 화면에서
        /// 실제로 몇 픽셀인지 알아야 글자 크기를 거기 맞출 수 있기 때문이다.
        /// 같은 자리·같은 크기로 이미 합쳐져 있던 블록은 다시 발행하지 않는다.
        /// "2사이즈" 라벨을 띄우는 데 쓴다(StandUpSizeLabelUI).
        /// </summary>
        public event System.Action<int, Vector3, float> OnStandUpSquareFormed;

        /// <summary>
        /// 지금 화면에 합쳐져 있는 정사각형들을 좌표/크기만 떼어 반환한다.
        /// 정사각형을 다시 계산할 때 "동점이면 이 자리를 유지하라"는 힌트로 넘기는 용도
        /// (SquareMergeFinder.FindSquareBlocks의 preferred 인자).
        /// </summary>
        public List<SquareMergeFinder.SquareBlock> GetActiveStandSquares()
        {
            var squares = new List<SquareMergeFinder.SquareBlock>(activeStandMerges.Count);
            FillActiveStandSquares(squares);
            return squares;
        }

        /// <summary>
        /// GetActiveStandSquares 의 버퍼 채우기 판. 정사각형을 다시 계산할 때마다 부르는 자리라
        /// 새 리스트를 만들지 않는다(이 프로젝트의 Fill~ / Find~ 규칙).
        /// </summary>
        private void FillActiveStandSquares(List<SquareMergeFinder.SquareBlock> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < activeStandMerges.Count; i++)
            {
                var merge = activeStandMerges[i];
                buffer.Add(new SquareMergeFinder.SquareBlock
                {
                    originX = merge.originX,
                    originY = merge.originY,
                    size = merge.size
                });
            }
        }

        /// <summary>
        /// AnimateStandUpLockAndSquareMerge에서 회전 코루틴을 던지기 전에(동기적으로) 호출.
        /// 호스트를 목표 크기/위치로 스냅하고, 정사각형에 흡수된 나머지 칸들의 뷰를 즉시 숨긴 뒤
        /// activeStandMerges에 등록해서 다음 겹침 판정/스탠드업 종료 시 원상복구 대상이 되게 함.
        /// 회전 애니메이션이 끝나길 기다리지 않고 이 시점에 바로 등록해야, 회전 도중 스탠드업 타임이
        /// 만료돼도 ClearAllStandSquareMerges가 이 합체를 놓치지 않고 원상복구할 수 있음.
        /// </summary>
        /// <returns>실제로 합쳐서 등록했으면 true. 호스트 뷰가 없어 건너뛰었으면 false.</returns>
        private bool RegisterSquareMerge(SquareMergeFinder.SquareBlock square, SquareGrowTarget target)
        {
            var hostView = viewGrid[square.originX, square.originY];
            if (hostView == null)
                return false; // 안전장치 - 있어야 할 뷰가 없으면 조용히 건너뜀

            // 위치·크기는 전부 보드 데이터(칸 좌표)에서 계산된 값이다. 여기서 소유권을 가져와야
            // 이 조각이 마침 낙하 중이었더라도 낙하 연출이 다시 끌어가지 않는다 - 그러지 않으면
            // 합체된 블록이 "내려오던 조각의 목적지"에 놓여 한 칸씩 어긋난다.
            hostView.TakeLayoutOwnership();
            hostView.SetMergedCellSize(target.toSize);
            hostView.MoveTo(target.toPos);

            var members = new List<(int x, int y)>();
            for (int dx = 0; dx < square.size; dx++)
                for (int dy = 0; dy < square.size; dy++)
                    members.Add((square.originX + dx, square.originY + dy));

            foreach (var (x, y) in members)
            {
                if (x == square.originX && y == square.originY)
                    continue; // 호스트 자신은 숨기지 않음

                var memberView = viewGrid[x, y];
                if (memberView != null)
                {
                    // 숨기기 전에 소유권을 가져온다. 안 그러면 낙하 연출이 안 보이는 뷰를 계속
                    // 끌고 다니다가, 나중에 합체가 풀려 다시 켜질 때 엉뚱한 자리에서 나타난다.
                    memberView.TakeLayoutOwnership();
                    memberView.gameObject.SetActive(false);
                }
            }

            activeStandMerges.Add(new StandSquareMerge
            {
                originX = square.originX,
                originY = square.originY,
                size = square.size,
                memberCells = members
            });

            return true;
        }

        private void UnmergeSquare(StandSquareMerge merge)
        {
            var hostView = viewGrid[merge.originX, merge.originY];
            if (hostView != null)
            {
                hostView.TakeLayoutOwnership();
                hostView.SetMergedCellSize(cellSize);
                hostView.MoveTo(GridToWorld(merge.originX, merge.originY));
            }

            for (int i = 0; i < merge.memberCells.Count; i++)
            {
                var (x, y) = merge.memberCells[i];
                if (x == merge.originX && y == merge.originY)
                    continue;

                var memberView = viewGrid[x, y];
                if (memberView == null)
                    continue;

                // 숨어 있는 동안 위치가 어디로 갔든, 다시 켤 때는 <b>자기 칸 좌표</b>로 되돌린다.
                // 예전엔 SetActive(true)만 하고 위치를 안 잡아줘서, 합체될 때 낙하 중이었거나
                // 그 사이 뷰가 옮겨진 경우 엉뚱한 자리에서 되살아났다.
                memberView.TakeLayoutOwnership();
                memberView.SetMergedCellSize(cellSize);
                memberView.MoveTo(GridToWorld(x, y));
                memberView.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// 스탠드업 종료 연출에서 날아갈 덩어리 하나. 대기 중엔 스탠드업 캐릭터 아이콘 그대로
        /// 떠 있다가, 자기 차례가 되면(BeginFlameFlight) 타들어가는 불꽃으로 바뀌어 날아간다.
        /// 불꽃 크기는 대기 시점에 이미 정해지므로(정사각형 크기에 비례) 여기 담아 들고 다닌다.
        /// </summary>
        public struct StandUpFlame
        {
            public PanelView view;
            public float flameWorldSize;
        }

        /// <summary>
        /// 스탠드업 종료 연출용: StandHeld 무리를 "날아갈 덩어리들"로 쪼갠다.
        /// 무리를 통째로 하나로 합치지 않고 <b>정사각형 단위</b>로 쪼갠다 - 무리 안에서 찾은
        /// 정사각형마다 덩어리 하나, 정사각형에 못 낀 낱개 칸마다 한 칸짜리 덩어리 하나.
        /// 데미지 계산이 정확히 같은 기준(SquareMergeFinder)으로 나뉘므로, 화면에 날아가는 덩어리와
        /// 데미지 구성이 1:1로 맞아떨어진다.
        ///
        /// <b>여기서는 아직 불꽃으로 바꾸지 않는다.</b> 전부 스탠드업 캐릭터 아이콘이 보이는 "대기"
        /// 모습으로 두고, 실제로 날아가기 시작할 때 BeginFlameFlight가 그 덩어리만 불꽃으로 바꾼다 -
        /// 그래야 아직 차례가 안 온 조각들이 판 위에 캐릭터인 채로 남아 순서대로 넘어가는 게 보인다.
        /// 반환된 뷰는 viewGrid에서 떨어져 나온 상태다(낙하/리필이 건드리지 않음).
        /// 풀 반납은 AnimateFlameBatchToTarget이 도착 시점에 알아서 처리한다.
        /// </summary>
        /// <param name="shownSquares">
        /// 방금까지 화면에 합쳐져 보이던 정사각형들. 쪼개는 방법이 여럿일 때 그중 하나를 고르는
        /// 기준으로 넘겨서, 마지막 순간에 덩어리가 다시 나뉘어 보이지 않게 한다.
        /// </param>
        public List<List<StandUpFlame>> BuildStandUpFlames(List<List<(int x, int y)>> groups,
            List<SquareMergeFinder.SquareBlock> shownSquares = null)
        {
            var result = new List<List<StandUpFlame>>();

            // 고정돼 있던 동안의 숨쉬기 대상을 일단 전부 비운다(크기도 원래대로). 바로 아래에서
            // 대표 조각들만 다시 등록되므로, 흡수돼 사라질 칸이나 풀에 반납될 뷰가 목록에 남아
            // 엉뚱한 칸에서 계속 커졌다 작아지는 일이 없다.
            StopStandUpPulse();

            foreach (var group in groups)
            {
                if (group.Count == 0)
                    continue;

                var flames = new List<StandUpFlame>();

                // 무리의 뷰를 한 번에 떼어내 손에 쥔다(이후 어떤 칸을 쓸지 자유롭게 고르기 위해)
                var views = new Dictionary<(int x, int y), PanelView>();
                foreach (var cell in group)
                {
                    var view = DetachView(cell.x, cell.y);
                    if (view != null)
                        views[cell] = view;
                }

                var squares = SquareMergeFinder.FindSquareBlocks(group, shownSquares);
                var usedCells = new HashSet<(int x, int y)>();

                // 1) 정사각형마다 불꽃 하나 - 그 안의 뷰 중 하나를 쓰고 나머지는 반납
                foreach (var square in squares)
                {
                    PanelView host = null;

                    for (int dx = 0; dx < square.size; dx++)
                    {
                        for (int dy = 0; dy < square.size; dy++)
                        {
                            var cell = (x: square.originX + dx, y: square.originY + dy);
                            usedCells.Add(cell);

                            if (!views.TryGetValue(cell, out var view))
                                continue;

                            if (host == null)
                                host = view;
                            else
                                pool.Release(view);
                        }
                    }

                    if (host == null)
                        continue;

                    Vector3 center = (GridToWorld(square.originX, square.originY)
                                    + GridToWorld(square.originX + square.size - 1, square.originY + square.size - 1)) * 0.5f;

                    flames.Add(PrepareWaitingStandUpPiece(host, center, square.size));
                }

                // 2) 정사각형에 못 낀 낱개 칸마다 한 칸짜리 불꽃 하나
                foreach (var pair in views)
                {
                    if (usedCells.Contains(pair.Key))
                        continue; // 위에서 이미 쓰였거나 반납된 칸

                    flames.Add(PrepareWaitingStandUpPiece(pair.Value, GridToWorld(pair.Key.x, pair.Key.y), 1));
                }

                if (flames.Count > 0)
                    result.Add(flames);
            }

            return result;
        }

        /// <summary>
        /// 아직 자기 차례가 아닌 조각을 "대기" 모습으로 둔다: 스탠드업 캐릭터 아이콘이 그대로 보이고,
        /// 정사각형이면 그 크기 하나로 커진 채로 제자리에 떠 있는다. 머티리얼은 아직 바꾸지 않는다 -
        /// 타들어가는 불꽃으로 바뀌는 건 실제로 날아가기 시작하는 순간(BeginFlameFlight)이다.
        /// 가림막(BoardDimOverlay)보다 위로 올려서 이 조각들만 어두워지지 않게 한다.
        /// </summary>
        private StandUpFlame PrepareWaitingStandUpPiece(PanelView view, Vector3 center, int squareSize)
        {
            view.SetRenderAboveDim(true);
            view.SetMergedCellSize(squareSize * CellStep - cellGap); // 정사각형은 그 크기의 조각 하나로 보이게
            view.MoveTo(center);

            pulsingStandUpViews.Add(view); // 자기 차례를 기다리는 동안에도 계속 말랑하게 숨쉰다

            return new StandUpFlame
            {
                view = view,
                flameWorldSize = squareSize * CellStep * standUpFlameCoverage
            };
        }

        /// <summary>
        /// 이 덩어리의 차례가 됐다 - 대기 모습(캐릭터 아이콘)에서 타들어가는 불꽃으로 바꾼다.
        /// 날아가기 직전에만 호출되므로, 아직 차례가 안 온 덩어리들은 계속 아이콘인 채로 남는다.
        /// </summary>
        public void BeginFlameFlight(List<StandUpFlame> batch)
        {
            if (batch == null)
                return;

            foreach (var flame in batch)
            {
                var view = flame.view;
                if (view == null)
                    continue;

                pulsingStandUpViews.Remove(view);
                view.SetIconScaleMultiplier(1f); // 숨쉬던 아이콘 크기를 정상으로 되돌리고 시작

                view.SetBodyVisible(false); // 프레임/아이콘은 감추고 불꽃만 남긴다
                view.SetFlameActive(true);
                view.SetFlameMaterialOverride(standUpEmberMaterial); // 타들어가는 전용 머티리얼
                view.SetFlameWorldSize(flame.flameWorldSize);
            }
        }

        /// <summary>
        /// 한 매치에서 나온 불꽃들을 <b>다 같이</b> 목표 지점까지 날려보낸다. 도착하면 전부 풀에 반납한다.
        /// 같은 매치의 조각들은 함께 움직여야 "이 매치가 통째로 리더에게 넘어간다"로 읽히기 때문에,
        /// 불꽃마다 코루틴을 띄우지 않고 하나의 루프가 전부 굴린다(할당도 그만큼 줄어든다).
        /// 날아가는 동안 조금 작아지게 해서 캐릭터에게 빨려 들어가는 느낌을 준다.
        /// </summary>
        public IEnumerator AnimateFlameBatchToTarget(List<StandUpFlame> flames, Vector3 targetWorld, float duration, float arriveScale)
        {
            if (flames == null || flames.Count == 0)
                yield break;

            var starts = new Vector3[flames.Count];
            var startScales = new Vector3[flames.Count];
            for (int i = 0; i < flames.Count; i++)
            {
                starts[i] = flames[i].view.transform.position;
                startScales[i] = flames[i].view.transform.localScale;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);

                // 처음엔 천천히 떠올랐다가 뒤로 갈수록 빨라지게(ease-in) - 빨려 들어가는 느낌
                float eased = p * p;
                float scale = Mathf.Lerp(1f, arriveScale, p);

                for (int i = 0; i < flames.Count; i++)
                {
                    flames[i].view.transform.position = Vector3.Lerp(starts[i], targetWorld, eased);
                    flames[i].view.transform.localScale = startScales[i] * scale;
                }

                yield return null;
            }

            for (int i = 0; i < flames.Count; i++)
            {
                flames[i].view.transform.localScale = startScales[i];
                pool.Release(flames[i].view);
            }
        }

        [Header("스탠드업 - 고정된 조각의 말랑한 숨쉬기")]
        [Tooltip("고정된 조각이 커졌다 돌아오는 정도(0.08 = 최대 8% 커짐).")]
        [SerializeField] private float standUpPulseAmount = 0.08f;

        [Tooltip("한 번 커졌다 돌아오는 데 걸리는 시간(초). 길수록 느긋하게 숨쉰다.")]
        [SerializeField] private float standUpPulsePeriod = 1.1f;

        /// <summary>
        /// 지금 말랑하게 숨쉬고 있는 조각들. 스탠드업 타임에 <b>매치가 성립해 고정되는 순간부터</b>
        /// 시작해서, 종료 연출에서 자기 차례가 되어 불꽃으로 날아갈 때까지 계속된다.
        /// 정사각형에 흡수돼 숨겨진 칸은 들어가지 않는다(화면에 보이는 대표 조각만).
        /// 조각마다 코루틴을 띄우지 않고 Update 하나가 전부 굴린다 - 이 프로젝트의 낙하/불꽃/
        /// 데미지 팝업과 같은 방식이다.
        /// </summary>
        private readonly List<PanelView> pulsingStandUpViews = new List<PanelView>();
        private float standUpPulseTime;

        [Header("강화 표시 (파직파직)")]
        [Tooltip("스파크가 다음 모양으로 튀기까지의 간격(초). 짧을수록 정신없이 파직거린다.")]
        [SerializeField] private float sparkStepInterval = 0.07f;

        [Tooltip("스파크가 조각 중심에서 흩어지는 반경(셀 크기 대비 비율). " +
                 "0이면 정확히 가운데에서만 튄다.")]
        [SerializeField] private float sparkJitter = 0.2f;

        [Tooltip("스파크가 한 박자 쉬어 갈 확률. 이게 있어야 '켜져 있는 이펙트'가 아니라 " +
                 "'파직파직 튀는 전기'로 읽힌다.")]
        [Range(0f, 0.9f)]
        [SerializeField] private float sparkBlankChance = 0.3f;

        [Tooltip("스파크 밝기의 최소/최대. 매 박자 이 사이에서 무작위로 뽑는다.")]
        [Range(0f, 1f)]
        [SerializeField] private float sparkMinAlpha = 0.55f;

        [Range(0f, 1f)]
        [SerializeField] private float sparkMaxAlpha = 1f;

        [Tooltip("스파크 크기 배수의 최소/최대. 매 박자 무작위로 뽑아서 크기가 들쭉날쭉해야 " +
                 "'충전된 자리에서 터진다'는 느낌이 난다.")]
        [SerializeField] private float sparkMinScale = 0.75f;
        [SerializeField] private float sparkMaxScale = 1.25f;

        // 지금 강화 표시 중인 뷰들. 조각마다 코루틴을 띄우지 않고 이 목록을 Update 가 훑는다
        // (낙하·불꽃·숨쉬기와 같은 방식).
        private readonly List<PanelView> empoweredViews = new List<PanelView>();
        private float sparkStepTimer;

        // 몇 번째 박자인지. 모든 강화 조각이 이 값 하나를 함께 보고 돌기 때문에 서로 어긋나지 않는다.
        private int sparkStep;

        /// <summary>
        /// 보드 데이터의 강화 상태를 화면에 맞춘다. 강화는 시간이 아니라 <b>조각의 운명</b>에
        /// 묶여 있으므로(매치되거나 덮어써질 때까지 유지), 화면도 데이터에서 다시 읽는 게 맞다.
        /// 칸이 바뀔 때마다 부르면 된다.
        /// </summary>
        public void RefreshEmpowerLook()
        {
            if (viewGrid == null || boardManager == null)
                return;

            empoweredViews.Clear();

            for (int x = 0; x < viewGrid.GetLength(0); x++)
            {
                for (int y = 0; y < viewGrid.GetLength(1); y++)
                {
                    var view = viewGrid[x, y];
                    if (view == null)
                        continue;

                    bool on = boardManager.Board.Get(x, y).empowered;
                    if (view.IsEmpowered != on)
                        view.SetEmpowered(on);

                    if (on)
                        empoweredViews.Add(view);
                }
            }
        }

        private readonly List<PanelView> specialViews = new List<PanelView>();

        [Header("특수 패널 룬 (미스틱 포지셔닝)")]
        [Tooltip("룬이 조각 둘레를 도는 반지름(칸 크기 대비).")]
        [SerializeField] private float specialOrbitRadius = 0.32f;

        [Tooltip("한 바퀴 도는 데 걸리는 시간(초). 느릴수록 신비롭다.")]
        [SerializeField] private float specialOrbitSeconds = 3.2f;

        [Tooltip("밝기가 오르내리는 폭. 0이면 일정하게 밝다.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float specialPulse = 0.28f;

        private float specialOrbitTimer;

        /// <summary>
        /// 특수 패널의 룬을 돌린다. <b>스파크와 달리 규칙적으로 돈다</b> - 저쪽은 "파직거린다"가
        /// 인상이고 이쪽은 "무언가가 조용히 지키고 있다"가 인상이라, 불규칙하면 어수선해진다.
        ///
        /// 룬 개수가 곧 <b>남은 매치 횟수</b>다 - 숫자를 적지 않아도 몇 번 남았는지 읽힌다.
        /// </summary>
        private void StepSpecialRunes()
        {
            if (specialViews.Count == 0)
                return;

            float period = Mathf.Max(0.1f, specialOrbitSeconds);
            specialOrbitTimer = (specialOrbitTimer + Time.deltaTime) % period;

            float baseAngle = specialOrbitTimer / period * Mathf.PI * 2f;
            float radius = specialOrbitRadius * CellStep;

            for (int i = specialViews.Count - 1; i >= 0; i--)
            {
                var view = specialViews[i];
                if (view == null || view.SpecialShown <= 0)
                {
                    specialViews.RemoveAt(i); // 다 쓰였거나 덮어써진 조각은 목록에서 빠진다
                    continue;
                }

                int count = view.SpecialShown;
                for (int slot = 0; slot < count; slot++)
                {
                    // 룬끼리 같은 간격으로 벌려 돈다.
                    float angle = baseAngle + Mathf.PI * 2f * slot / count;
                    var offset = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

                    // 반대 방향으로 천천히 돌려 "떠 있다"는 느낌을 준다.
                    float spin = -angle * Mathf.Rad2Deg * 0.5f;

                    float alpha = 1f - specialPulse * (0.5f + 0.5f * Mathf.Sin(angle * 2f));
                    view.StepSpecial(slot, offset, spin, alpha);
                }
            }
        }

        /// <summary>
        /// 판을 훑어 룬 표시를 데이터와 다시 맞춘다. 매치로 횟수가 줄었을 때 부른다
        /// (<see cref="RefreshEmpowerLook"/> 과 같은 자리).
        /// </summary>
        public void RefreshSpecialLook()
        {
            specialViews.Clear();

            for (int x = 0; x < boardManager.Board.width; x++)
            {
                for (int y = 0; y < boardManager.Board.height; y++)
                {
                    var view = viewGrid[x, y];
                    if (view == null)
                        continue;

                    var cell = boardManager.Board.Get(x, y);
                    int left = cell.IsSpecial ? cell.specialMatchesLeft : 0;

                    view.SetSpecial(left);
                    if (left > 0)
                        specialViews.Add(view);
                }
            }
        }

        /// <summary>
        /// 강화 스파크를 한 박자 넘긴다. 모든 강화 조각의 스파크를 <b>같은 프레임에 한꺼번에</b>
        /// 갈아끼우되 값은 각자 무작위라, 판 전체가 지지직거리면서도 서로 다른 모양이 된다.
        /// </summary>
        private void StepEmpowerSparks()
        {
            if (empoweredViews.Count == 0)
                return;

            sparkStepTimer += Time.deltaTime;
            if (sparkStepTimer < Mathf.Max(0.01f, sparkStepInterval))
                return;

            sparkStepTimer = 0f;
            sparkStep++;

            float radius = sparkJitter * CellStep;

            for (int i = empoweredViews.Count - 1; i >= 0; i--)
            {
                var view = empoweredViews[i];
                if (view == null || !view.IsEmpowered)
                {
                    empoweredViews.RemoveAt(i); // 매치·덮어쓰기로 사라진 조각은 목록에서 빠진다
                    continue;
                }

                int count = view.SparkCount;
                for (int slot = 0; slot < count; slot++)
                {
                    // 자리·모양·크기·밝기를 매 박자 <b>전부 새로 뽑는다.</b>
                    // 규칙적으로 돌게 했더니 전기줄이 뱅글뱅글 도는 것처럼 보였다 -
                    // "충전된 자리에서 파직파직 터진다"는 인상은 불규칙에서 나온다.
                    if (Random.value < sparkBlankChance)
                    {
                        view.StepSpark(slot, 0, Vector2.zero, 0f, 1f, 0f); // 이 박자는 쉼
                        continue;
                    }

                    view.StepSpark(
                        slot,
                        Random.Range(0, 997),
                        Random.insideUnitCircle * radius,
                        Random.Range(0f, 360f),
                        Random.Range(sparkMinScale, sparkMaxScale),
                        Random.Range(sparkMinAlpha, sparkMaxAlpha));
                }
            }
        }

        [Header("힌트 반짝임")]
        [Tooltip("밝아지는 데 걸리는 시간(초). 짧을수록 '탁' 하고 켜진다.")]
        [SerializeField] private float hintGlowRiseDuration = 0.12f;

        [Tooltip("원래 색으로 돌아오는 데 걸리는 시간(초). 밝아지는 시간보다 길어야 " +
                 "'빠르게 밝아졌다 부드럽게 가라앉는' 느낌이 난다.")]
        [SerializeField] private float hintGlowFallDuration = 0.55f;

        [Tooltip("한 번 반짝인 뒤 다음까지 쉬는 시간(초). 0이면 쉼 없이 계속 반복한다.")]
        [SerializeField] private float hintGlowRestDuration = 0.35f;

        // 지금 힌트로 반짝이는 조각들. 조각마다 코루틴을 띄우지 않고 Update 하나가 굴린다
        // (스파크·숨쉬기·낙하와 같은 방식).
        private readonly List<PanelView> hintViews = new List<PanelView>();
        private float hintGlowTime;

        /// <summary>
        /// 힌트로 지목된 칸들을 반짝이게 한다. 이미 다른 힌트가 떠 있으면 그건 끄고 새로 시작한다.
        /// </summary>
        public void ShowHint(List<(int x, int y)> cells)
        {
            ClearHint();

            if (cells == null || viewGrid == null)
                return;

            for (int i = 0; i < cells.Count; i++)
            {
                var (x, y) = cells[i];
                if (!InViewBounds(x, y))
                    continue;

                var view = viewGrid[x, y];
                if (view != null)
                    hintViews.Add(view);
            }

            hintGlowTime = 0f; // 항상 어두운 상태에서 시작해야 첫 반짝임이 눈에 띈다
        }

        /// <summary>
        /// 지금 힌트가 실제로 반짝이고 있는지. 뷰가 뽑히거나(드래그) 재사용되면 아래 StepHintGlow가
        /// 스스로 꺼버리므로, 호출부는 자기가 켰다고 믿지 말고 이걸로 확인해야 한다.
        /// </summary>
        public bool IsHintActive => hintViews.Count > 0;

        /// <summary>힌트를 끄고 조각을 원래 색으로 되돌린다.</summary>
        public void ClearHint()
        {
            for (int i = 0; i < hintViews.Count; i++)
            {
                if (hintViews[i] != null)
                    hintViews[i].SetHintGlow(0f);
            }

            hintViews.Clear();
        }

        /// <summary>
        /// 힌트 반짝임을 한 프레임 굴린다: <b>빠르게 밝아졌다가 부드럽게 원래 색으로</b> 돌아오길 반복.
        /// 올라갈 때와 내려올 때 곡선을 나눈 이유는 스킬 게이지 숨쉬기와 같다 - 대칭 사인으로는
        /// "탁 켜졌다 스르르 꺼진다"는 인상이 안 난다.
        /// </summary>
        private void StepHintGlow()
        {
            if (hintViews.Count == 0)
                return;

            // 뷰가 풀로 반납되거나 다른 칸에 재사용됐으면 힌트 자체가 낡은 것이다.
            // 그대로 두면 엉뚱한 조각이 반짝인다 - 통째로 끄고, 다시 띄울지는 호출부가 정한다.
            for (int i = 0; i < hintViews.Count; i++)
            {
                if (!StillOwnsView(hintViews[i]))
                {
                    ClearHint();
                    return;
                }
            }

            hintGlowTime += Time.deltaTime;

            float rise = Mathf.Max(0.01f, hintGlowRiseDuration);
            float fall = Mathf.Max(0.01f, hintGlowFallDuration);
            float cycle = rise + fall + Mathf.Max(0f, hintGlowRestDuration);

            float t = hintGlowTime % cycle;
            float amount;
            if (t < rise)
            {
                float p = t / rise;
                amount = 1f - (1f - p) * (1f - p); // ease-out - 초반에 확 밝아진다
            }
            else if (t < rise + fall)
            {
                float p = (t - rise) / fall;
                amount = Mathf.SmoothStep(1f, 0f, p); // 양 끝이 부드러워 스르르 가라앉는다
            }
            else
            {
                amount = 0f; // 쉬는 구간
            }

            for (int i = 0; i < hintViews.Count; i++)
                hintViews[i].SetHintGlow(amount);
        }

        private void Update()
        {
            StepEmpowerSparks();
            StepSpecialRunes();
            StepHintGlow();

            if (pulsingStandUpViews.Count == 0)
            {
                standUpPulseTime = 0f; // 다음 스탠드업이 항상 원래 크기에서 시작하도록
                return;
            }

            standUpPulseTime += Time.deltaTime;

            // 0 → 1 → 0으로 부드럽게 오가는 값. cos이라 양 끝에서 느려져 "말랑하게" 읽히고,
            // 1 아래로는 내려가지 않아서 원래 크기보다 작아지는 일이 없다.
            float phase = standUpPulsePeriod > 0f
                ? 0.5f - 0.5f * Mathf.Cos(standUpPulseTime / standUpPulsePeriod * Mathf.PI * 2f)
                : 0f;
            float multiplier = 1f + standUpPulseAmount * phase;

            for (int i = pulsingStandUpViews.Count - 1; i >= 0; i--)
            {
                var view = pulsingStandUpViews[i];
                if (view == null)
                {
                    pulsingStandUpViews.RemoveAt(i); // 어떤 이유로든 사라진 뷰는 목록에서 정리
                    continue;
                }

                view.SetIconScaleMultiplier(multiplier); // 프레임·불꽃은 그대로, 캐릭터 아이콘만 말랑하게
            }
        }

        /// <summary>
        /// 숨쉬기 대상을 지금 화면 상태에 맞춰 통째로 다시 잡는다. 합체가 생기거나 풀릴 때마다
        /// "화면에 보이는 대표 조각"이 바뀌므로, 증분으로 관리하지 않고 정사각형 재계산과 같은
        /// 자리에서 함께 다시 세운다(같은 이유로 증분 방식이 버그를 냈던 전례가 있다).
        /// heldCells: 지금 고정돼 있는(또는 곧 고정될) 모든 칸.
        /// absorbedMembers: 정사각형에 흡수돼 뷰가 숨겨진 칸 - 숨쉬어봐야 안 보이므로 제외한다.
        /// </summary>
        private void RefreshStandUpPulseTargets(List<(int x, int y)> heldCells, HashSet<(int x, int y)> absorbedMembers)
        {
            StopStandUpPulse();

            foreach (var (x, y) in heldCells)
            {
                if (absorbedMembers.Contains((x, y)))
                    continue;

                var view = viewGrid[x, y];
                if (view != null)
                    pulsingStandUpViews.Add(view);
            }
        }

        /// <summary>숨쉬기를 멈추고 크기를 원래대로 되돌린다. 목록을 다시 세우기 전에도 쓴다.</summary>
        private void StopStandUpPulse()
        {
            foreach (var view in pulsingStandUpViews)
            {
                if (view != null)
                    view.SetIconScaleMultiplier(1f);
            }

            pulsingStandUpViews.Clear();
        }

        [Header("스탠드업 종료 - 날아가는 불꽃")]
        [Tooltip("정사각형/낱개 크기 대비 불꽃 크기 배수. 1보다 크면 조각 밖으로 넉넉히 삐져나온다.")]
        [SerializeField] private float standUpFlameCoverage = 1.35f;

        [Tooltip("스탠드업 종료 때 날아가는 불꽃 전용 머티리얼(타들어가는 테두리). " +
                 "비워두면 평소 불꽃 머티리얼을 그대로 쓴다.")]
        [SerializeField] private Material standUpEmberMaterial;

        /// <summary>
        /// 스탠드업 타임이 끝날 때 호출 - 지금까지 합쳐져 있던 정사각형들을 전부 원래 크기의
        /// 개별 조각들로 되돌린다. BoardManager.ClearAllStandHeldCells()로 데이터를 비우고 뷰를
        /// detach/반납하기 전에 반드시 먼저 호출해야 함 - 안 그러면 이미 풀에 반납되거나 다른
        /// 칸에 재사용된 뷰의 크기를 엉뚱하게 바꿔버리는 사고가 날 수 있음.
        /// </summary>
        public void ClearAllStandSquareMerges()
        {
            foreach (var merge in activeStandMerges)
                UnmergeSquare(merge);
            activeStandMerges.Clear();

        }

        /// <summary>
        /// 박스 십자변환처럼 여러 칸이 한꺼번에 다른 색으로 덮어써질 때 호출. 그 칸들 중 활성 정사각형
        /// 합체에 속한 게 하나라도 있으면, 그 합체 전체를 원래 크기의 개별 조각으로 되돌린다
        /// (합체 중 일부만 덮어써져도 나머지 칸까지 포함해서 통째로 풀어줌 - 부분적으로만 합쳐진
        /// 상태로 남겨두면 정사각형이 아닌데 커진 채로 보이는 모양이 나옴).
        /// 호출부(BoardInputController)는 이 칸들의 뷰를 실제로 파괴/재생성하기 전에(예: AnimateBoxUnfold
        /// 이전에) 반드시 먼저 호출해야 함 - 안 그러면 확대된 채인 호스트 뷰가 그대로 파괴되거나,
        /// 숨겨진 멤버 뷰가 다른 칸으로 재활용되면서 activeStandMerges가 엉뚱한 뷰를 참조하게 됨.
        /// </summary>
        public void BreakStandSquareMergesOverlapping(IEnumerable<(int x, int y)> cells)
        {
            var cellSet = new HashSet<(int x, int y)>(cells);

            for (int i = activeStandMerges.Count - 1; i >= 0; i--)
            {
                var merge = activeStandMerges[i];
                bool overlaps = false;
                foreach (var cell in merge.memberCells)
                {
                    if (cellSet.Contains(cell)) { overlaps = true; break; }
                }

                if (overlaps)
                {
                    UnmergeSquare(merge);
                    activeStandMerges.RemoveAt(i);
                }
            }
        }
    }
}